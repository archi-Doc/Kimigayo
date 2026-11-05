// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public class TestAttributeCertificateBindingTest
{
    [Theory]
    [InlineData("contract C\n#Test\nstruct S\n    Self is C")]
    [InlineData("#Test\ncontract C\nstruct S\n    Self is C")]
    public void InvalidTestTargetCannotCertifyConformance(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidTestDefinition_Kd);
        Assert.False(Definition(c).IsVerified);
        Assert.False(c.Bind().IsComplete);
        Assert.False(Definition(c).IsVerified);
        var restored = CompilationTestHelper.Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.False(Definition(restored).IsVerified);
    }

    [Fact]
    public void InvalidTestTargetCannotCertifyProperty()
    {
        var c = MinimalEmissionTest.Analyze("struct S\n    #Test\n    var value: i32");
        Assert.False(c.Binding.Result.IsComplete);
        var property = Assert.IsType<PropertyKoto>(c.Kotonoha.RootKoto.NestedContainers.Single().Members.Single());
        Assert.False(property.SymbolOf()!.Property!.IsVerified);
    }

    [Theory]
    [InlineData("#Unknown\n#Test")]
    [InlineData("#Test\n#Unknown")]
    public void OtherInvalidMarkerIsStillChecked(string markers)
    {
        var c = MinimalEmissionTest.Analyze(markers + "\nstruct S");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidTestDefinition_Kd);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnresolvedBinding_Kd);
        Assert.Equal(
            ["InvalidTestDefinition_Kd", "UnresolvedBinding_Kd"],
            CompilationTestHelper.BindingErrors(c).Select(x => x.Code).Order());
        Assert.Equal(BindingFailure.None, c.Kotonoha.RootKoto.NestedContainers.Single().FailureOf());
    }

    [Theory]
    [InlineData("#Layout(\"bad\")\n#Test")]
    [InlineData("#Test\n#Layout(\"bad\")")]
    public void LayoutMarkerIsCheckedInEitherOrder(string markers)
    {
        var c = MinimalEmissionTest.Analyze(markers + "\nstruct S");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidTestDefinition_Kd);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidLayoutAttribute_Kd);
        Assert.Equal(
            ["InvalidLayoutAttribute_Kd", "InvalidTestDefinition_Kd"],
            CompilationTestHelper.BindingErrors(c).Select(x => x.Code).Order());
        Assert.Equal(BindingFailure.None, c.Kotonoha.RootKoto.NestedContainers.Single().FailureOf());
    }

    [Fact]
    public void InvalidGroupTestMarkerInvalidatesNestedCertificates()
    {
        var c = MinimalEmissionTest.Analyze("contract C\n#Test\ngroup Outer\n    struct S\n        Self is C");
        Assert.False(c.Binding.Result.IsComplete);
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Outer").NestedContainers.Single();
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
        Assert.False(c.Binding.GetConformanceDefinition(type.TypeOf()!, contract.SymbolOf()!)!.IsVerified);
    }

    [Fact]
    public void RemovingWrongTargetMarkerRestoresCertificate()
    {
        var c = MinimalEmissionTest.Analyze("contract C\n#Test\nstruct S\n    Self is C");
        var definition = Definition(c);
        Assert.False(definition.IsVerified);
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        Assert.True(type.RemoveAttribute(Assert.IsType<AttributeKoto>(type.AttributeChain)));
        Assert.True(c.Bind().IsComplete);
        Assert.True(definition.IsVerified);
    }

    [Fact]
    public void ProductExcludedFunctionsRemainExcluded()
    {
        var c = MinimalEmissionTest.Analyze("#Test\nfunc example()\n    missing()\ncontract C\nstruct S\n    Self is C");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(Definition(c).IsVerified);
        Assert.True(c.Bind().IsComplete);
    }

    // SPEC 6.7/23.1.2: rejecting the marker invalidates its target without inventing a second failed requirement.
    [Theory]
    [InlineData("func e(#Test x: i32) => ()", false, nameof(BindingFailure.None))]
    [InlineData("func e(#Test x: i32) => ()", true, nameof(BindingFailure.None))]
    [InlineData("#Test\nstruct S", false, nameof(BindingFailure.None))]
    [InlineData("#Test\nstruct S", true, nameof(BindingFailure.None))]
    [InlineData("#Test\ngroup G", false, nameof(BindingFailure.None))]
    [InlineData("#Test\ngroup G", true, nameof(BindingFailure.None))]
    [InlineData("struct S\n    #Test\n    let value: i32", false, nameof(BindingFailure.InvalidConstraint))]
    [InlineData("struct S\n    #Test\n    let value: i32", true, nameof(BindingFailure.InvalidConstraint))]
    [InlineData("struct S\n    #Test\n    var value: i32", false, nameof(BindingFailure.InvalidConstraint))]
    [InlineData("struct S\n    #Test\n    var value: i32", true, nameof(BindingFailure.InvalidConstraint))]
    [InlineData("#Test\nfunc e(x: i32) => ()", false, nameof(BindingFailure.None))]
    [InlineData("#Test\nfunc e(x: i32) => ()", true, nameof(BindingFailure.None))]
    [InlineData("#Test\n#Test\nfunc e() => ()", false, nameof(BindingFailure.None))]
    [InlineData("#Test\n#Test\nfunc e() => ()", true, nameof(BindingFailure.None))]
    public void RejectedMarkersOwnTheFailureInEitherBuildMode(string source, bool testBuild, string failure)
    {
        var expectedFailure = Enum.Parse<BindingFailure>(failure);
        var c = CompilationTestHelper.ParseSuccess(source);
        c.IsTestBuild = testBuild;
        var markers = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<AttributeKoto>().ToArray();
        for (var pass = 0; pass < 2; pass++)
        {
            Assert.False(c.Bind().IsComplete);
            Assert.False(c.Binding.CheckBound().IsComplete);
            foreach (var marker in markers)
            {
                var target = marker.Parent!;
                while (target is AttributeKoto)
                {
                    target = target.Parent!;
                }

                Assert.Equal(BindingState.Invalid, target.StateOf());
                Assert.Equal(expectedFailure, target.FailureOf());
                var causes = c.Binding.FailureCauses(target)!;
                var cause = Assert.Single(causes);
                if (expectedFailure == BindingFailure.None)
                {
                    Assert.DoesNotContain(target, c.Binding.DerivedIssues);
                    Assert.Contains(cause.Subject, markers);
                }
                else
                {
                    Assert.Contains(target, c.Binding.DerivedIssues);
                    Assert.Equal(target.KeyOf(DiagnosticRequirement.Binding(expectedFailure)), cause);
                }

                // A later phase using the public key must close through Binding's derived fact to the marker.
                target.ReportDerived(DiagnosticRequirement.Startup, causes);
            }

            var errors = CompilationTestHelper.BindingErrors(c);
            Assert.Equal(markers.Length, errors.Length);
            Assert.All(errors, error => Assert.Equal(nameof(DiagnosticCode.InvalidTestDefinition_Kd), error.Code));
            Assert.Equal(markers.Select(x => x.Span).OrderBy(x => x.Start), errors.Select(x => x.Span).OrderBy(x => x.Start));
            Assert.False(c.Generate(TextWriter.Null));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovingAndRestoringAMarkerRebuildsItsCause(bool testBuild)
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n#Test\nstruct S\n    Self is C");
        c.IsTestBuild = testBuild;
        Assert.False(c.Bind().IsComplete);
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var marker = Assert.IsType<AttributeKoto>(type.AttributeChain);
        Assert.Same(marker, Assert.Single(c.Binding.FailureCauses(type)!).Subject);
        Assert.False(Definition(c).IsVerified);
        Assert.True(type.RemoveAttribute(marker));
        Assert.True(c.Bind().IsComplete);
        Assert.Null(c.Binding.FailureCauses(type));
        Assert.True(Definition(c).IsVerified);
        type.SetAttributeChain(marker);
        Assert.False(c.Bind().IsComplete);
        Assert.Same(marker, Assert.Single(c.Binding.FailureCauses(type)!).Subject);
        Assert.False(Definition(c).IsVerified);
        var restored = CompilationTestHelper.Reload(c);
        restored.IsTestBuild = testBuild;
        Assert.False(restored.Bind().IsComplete);
        var restoredType = restored.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        Assert.Same(restoredType.AttributeChain, Assert.Single(restored.Binding.FailureCauses(restoredType)!).Subject);
        Assert.False(Definition(restored).IsVerified);
    }

    // Product excludes a Test body; Test mode also reaches virtual pending, whose Unsupported record closes to the marker.
    [Theory]
    [InlineData("", false, nameof(BindingFailure.Unsupported), "Unsupported_Kd", "virtual")]
    [InlineData("", true, nameof(BindingFailure.Unsupported), "Unsupported_Kd", "virtual")]
    [InlineData("#Unknown", false, nameof(BindingFailure.Unsupported), "UnresolvedBinding_Kd", "Unknown")]
    [InlineData("#Unknown", true, nameof(BindingFailure.Unsupported), "UnresolvedBinding_Kd", "Unknown")]
    [InlineData("#Test", false, nameof(BindingFailure.None), "InvalidTestDefinition_Kd", "#Test")]
    [InlineData("#Test", true, nameof(BindingFailure.Unsupported), "InvalidTestDefinition_Kd", "#Test")]
    public void ARejectedMarkerSurvivesVirtualPendingBinding(string marker, bool testBuild, string failure, string code, string text)
    {
        var expectedFailure = Enum.Parse<BindingFailure>(failure);
        var attribute = marker.Length == 0 ? string.Empty : "    " + marker + "\n";
        var c = CompilationTestHelper.ParseSuccess("open struct S\n" + attribute + "    public virtual func f(self: objuniq/Self) => ()");
        c.IsTestBuild = testBuild;
        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "f");
        for (var pass = 0; pass < 2; pass++)
        {
            Assert.False(c.Bind().IsComplete);
            Assert.Equal(marker.Length == 0 ? BindingState.Unresolved : BindingState.Invalid, function.StateOf());
            Assert.Equal(expectedFailure, function.FailureOf());
            Assert.Equal(marker.Length != 0 && expectedFailure != BindingFailure.None, c.Binding.DerivedIssues.Contains(function));
            var key = Assert.Single(c.Binding.FailureCauses(function)!);
            if (expectedFailure == BindingFailure.None)
            {
                Assert.Same(function.AttributeChain, key.Subject);
            }
            else
            {
                Assert.Equal(function.KeyOf(DiagnosticRequirement.Binding(BindingFailure.Unsupported)), key);
            }

            function.ReportDerived(DiagnosticRequirement.Startup, [key]);
            var error = Assert.Single(CompilationTestHelper.BindingErrors(c));
            Assert.Equal(code, error.Code);
            Assert.Equal(text, error.Text);
        }
    }

    [Theory]
    [InlineData("#Unknown\nprotected func f() => ()", nameof(BindingFailure.ProtectedPlacement), "ProtectedPlacement_Kd", 1)]
    [InlineData(
        "struct Box<T>\n    func f<U>(x: T) => ()\n    #Unknown\n    func f<V>(x: V) => ()",
        nameof(BindingFailure.ParameterShapeMismatch),
        "ParameterShapeMismatch_Kd",
        1)]
    [InlineData(
        "struct S\n    public func f(self: uniq/Self, x: ref/i32) -> i32 => 1\n" +
        "    #Unknown\n    public func f(self: ref/Self, x: uniq/i64) -> i32 => 2",
        nameof(BindingFailure.ReceiverShapeMismatch),
        "ReceiverShapeMismatch_Kd",
        2)]
    [InlineData(
        "#Unknown\nfunc f<a/A, b/B>(p: a/A, q: b/B) -> ()\n    _ = p@move\n    _ = q@move",
        nameof(BindingFailure.CaseLimit),
        "OwnershipCaseLimit_Kd",
        1)]
    public void IndependentSignatureFailuresRemainDirectBesideAMarker(string source, string failure, string code, int count)
    {
        var c = CompilationTestHelper.ParseSuccess(source);
        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.AttributeChain is not null);
        for (var pass = 0; pass < 2; pass++)
        {
            Assert.False(c.Bind().IsComplete);
            Assert.Equal(BindingState.Invalid, function.StateOf());
            Assert.Equal(Enum.Parse<BindingFailure>(failure), function.FailureOf());
            Assert.DoesNotContain(function, c.Binding.DerivedIssues);
            Assert.Equal(function.KeyOf(DiagnosticRequirement.Binding(function.FailureOf())), Assert.Single(c.Binding.FailureCauses(function)!));
            var errors = CompilationTestHelper.BindingErrors(c);
            Assert.Equal(count + 1, errors.Length);
            Assert.Equal(count, errors.Count(x => x.Code == code));
            Assert.Equal("Unknown", Assert.Single(errors, x => x.Code == "UnresolvedBinding_Kd").Text);
        }
    }

    // Source controls for ordinary pending: a consultation frame alone does not make the declaration Invalid.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AConsultedResultTypeKeepsVirtualBindingPending(bool testBuild)
    {
        var c = CompilationTestHelper.ParseSuccess("open struct S\n    public virtual func f(self: objuniq/Self) -> Missing => ()");
        c.IsTestBuild = testBuild;
        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "f");
        for (var pass = 0; pass < 2; pass++)
        {
            Assert.False(c.Bind().IsComplete);
            Assert.Equal(BindingState.Unresolved, function.StateOf());
            Assert.Equal(BindingFailure.Unsupported, function.FailureOf());
            Assert.Contains(function, c.Binding.DerivedIssues);
            var key = Assert.Single(c.Binding.FailureCauses(function)!);
            Assert.Equal(function.KeyOf(DiagnosticRequirement.Binding(BindingFailure.Unsupported)), key);
            function.ReportDerived(DiagnosticRequirement.Startup, [key]);
            var error = Assert.Single(CompilationTestHelper.BindingErrors(c));
            Assert.Equal("UnresolvedBinding_Kd", error.Code);
            Assert.Equal("Missing", error.Text);
            Assert.Equal(function.ReturnType!.Span, error.Span);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFailedOriginalKeepsAnOverridePending(bool testBuild)
    {
        const string Source = "open struct A<T>\n    public virtual func f(self: objref/Self, x: T) => ()\n" +
            "    public virtual func f(self: objref/Self, x: i32) => ()\nstruct B : A<i32>\n    override func f(self: objref/Self, x: i32) => ()";
        var c = CompilationTestHelper.ParseSuccess(Source);
        c.IsTestBuild = testBuild;
        var functions = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Where(x => x.Name == "f").ToArray();
        var pending = Assert.Single(functions, x => x.IsOverride);
        for (var pass = 0; pass < 2; pass++)
        {
            Assert.False(c.Bind().IsComplete);
            var original = Assert.Single(functions, x => x.FailureOf() == BindingFailure.ParameterShapeMismatch);
            Assert.Equal(BindingState.Invalid, original.StateOf());
            Assert.DoesNotContain(original, c.Binding.DerivedIssues);
            Assert.Equal(BindingState.Unresolved, pending.StateOf());
            Assert.Equal(BindingFailure.Unsupported, pending.FailureOf());
            Assert.Contains(pending, c.Binding.DerivedIssues);
            var key = Assert.Single(c.Binding.FailureCauses(pending)!);
            Assert.Equal(pending.KeyOf(DiagnosticRequirement.Binding(BindingFailure.Unsupported)), key);
            pending.ReportDerived(DiagnosticRequirement.Startup, [key]);
            var error = Assert.Single(CompilationTestHelper.BindingErrors(c));
            Assert.Equal("ParameterShapeMismatch_Kd", error.Code);
            Assert.Equal("i32", error.Text);
            Assert.Equal(original.Parameters[1].Type.Span, error.Span);
        }
    }

    [Fact]
    public void ReplacingAConsultationFrameRetainsTheRejectedPart()
    {
        var c = CompilationTestHelper.ParseSuccess("func f(#Test x: i32) => ()");
        Assert.False(c.Bind().IsComplete);
        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "f");
        var marker = KotoTree.Walk(function).OfType<AttributeKoto>().Single();
        var complete = typeof(Binding).GetMethod(
            "CompleteDependent",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        complete.Invoke(c.Binding, [function, c.Kotonoha.RootKoto]);
        Assert.Equal(BindingState.Invalid, function.StateOf());
        Assert.Equal(BindingFailure.None, function.FailureOf());
        Assert.Same(marker, Assert.Single(c.Binding.FailureCauses(function)!).Subject);
        Assert.Equal("InvalidTestDefinition_Kd", Assert.Single(CompilationTestHelper.BindingErrors(c)).Code);
    }

    private static BoundConformance Definition(Compilation c)
    {
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
        return c.Binding.GetConformanceDefinition(type.TypeOf()!, contract.SymbolOf()!)!;
    }

    [Collection<AllocationIsolationCollection>]
    [Trait("Purpose", "Allocation")]
    public sealed class AllocationTests
    {
        [Theory]
        [InlineData(1)]
        [InlineData(64)]
        public void RejectedMarkerBindingReusesPrerequisites(int count)
        {
            var source = string.Join("\n", Enumerable.Range(0, count).Select(i => $"#Test\nstruct S{i}"));
            var c = CompilationTestHelper.ParseSuccess(source);
            Assert.False(c.Bind().IsComplete);
            var allocated = AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final));
            Assert.False(c.Binding.Result.IsComplete);
            Assert.Equal(0, allocated);
        }
    }
}
