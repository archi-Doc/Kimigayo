// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class UnresolvedConstraintBindingTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingConformanceReportsItsNameWithoutDerivedDeclarationErrors(bool missingFirst)
    {
        var clauses = missingFirst ? "    Self is Missing\n    Self is Copy\n" : "    Self is Copy\n    Self is Missing\n";
        var c = Parse("public struct Reading\n" + clauses + "    public let value: i32");
        Assert.False(c.Bind().IsComplete);
        var reading = Container(c, "Reading");
        Assert.Equal(BindingState.Invalid, reading.StateOf());
        Assert.False(Assert.IsType<PropertyKoto>(Assert.Single(reading.Members)).SymbolOf()!.Property!.IsVerified);
        Assert.Equal(ConstraintProof.Error, c.Binding.Prove(reading.ConstraintNodes.Single(x => x.Right.ToString() == "Copy").ConstraintOf()!, reading));
        c.Binding.ReportDiagnostics();
        var diagnostic = Assert.Single(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.Equal("UnresolvedBinding_Kd", diagnostic.Code);
        Assert.Equal("Missing", diagnostic.Text);
        Assert.False(c.Binding.CheckBound().IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.Single(TestDiagnostics.Of(c, "Hello.kimi"));
    }

    [Fact]
    public void MissingConformanceDoesNotHideIndependentErrorsOrCertifyAUse()
    {
        var c = Parse("""
            public contract Marker
            public struct Reading
                Self is Copy
                Self is Missing
                Self is Marker
                public let value: i32
                public let other: MissingField
                public func wrong() -> i32 => true
            func take<T>(value: ref/T) -> i32
                T is Marker
                return 1
            func use(value: ref/Reading) -> i32 => take(value)
            struct Contradiction<T>
                T is i32
                T is not i32
            """);
        Assert.False(c.Bind().IsComplete);
        var reading = Container(c, "Reading");
        var marker = Container(c, "Marker");
        Assert.False(c.Binding.GetConformanceDefinition(reading.TypeOf()!, marker.SymbolOf()!)!.IsVerified);
        var use = c.Kotonoha.GeneratedFunction!.Body!.Items.OfType<FunctionKoto>().Single(x => x.Name == "use");
        Assert.Contains(c.Binding.Issues, x => ReferenceEquals(x.Node, use) || ReferenceEquals(x.Node, use.ExpressionBody));
        c.Binding.ReportDiagnostics();
        var diagnostics = TestDiagnostics.Of(c, "Hello.kimi");
        Assert.Contains(diagnostics, x => x.Code == "UnresolvedBinding_Kd" && x.Text == "Missing");
        Assert.Contains(diagnostics, x => x.Code == "UnresolvedBinding_Kd" && x.Text == "MissingField");
        Assert.Contains(diagnostics, x => x.Code == "TypeMismatch_Kd");
        Assert.Contains(diagnostics, x => x.Code == "InvalidConstraint_Kd" && x.Span.Start == Container(c, "Contradiction").Span.Start);
        Assert.DoesNotContain(diagnostics, x => x.Code == "InvalidConstraint_Kd" && x.Span.Start == reading.Span.Start);
    }

    [Fact]
    public void ConformanceDiagnosticCausesAreRebuiltAfterSourceChanges()
    {
        var c = Parse("public struct Reading\n    Self is Copy\n    Self is Missing\n    public let value: i32");
        Assert.False(c.Bind().IsComplete);
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", "public contract Missing"));
        Assert.True(c.Bind().IsComplete);
        Assert.Empty(c.Binding.Issues);
        c.Binding.ReportDiagnostics();
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.True(CompilationTestHelper.Reload(c).Bind().IsComplete);
    }

    [Fact]
    public void MissingConformanceInAnotherFragmentKeepsItsOwnLocation()
    {
        var c = Parse("public struct Reading\n    public let value: i32");
        c.Kotonoha.AddSource(new SourceDocument("Other.kimi", "public struct Reading\n    Self is Copy\n    Self is Missing\n    public let other: i32"));
        Assert.Empty(TestDiagnostics.Of(c, "Other.kimi"));
        Assert.False(c.Bind().IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        var diagnostic = Assert.Single(TestDiagnostics.Of(c, "Other.kimi"));
        Assert.Equal("UnresolvedBinding_Kd", diagnostic.Code);
        Assert.Equal("Other.kimi", diagnostic.Path);
    }

    [Fact]
    public void MultipleMissingConformanceNamesRemainSeparateCauses()
    {
        var c = Parse("public struct Reading\n    Self is Copy\n    Self is Missing and Other\n    public let value: i32");
        Assert.False(c.Bind().IsComplete);
        c.Binding.ReportDiagnostics();
        var diagnostics = TestDiagnostics.Of(c, "Hello.kimi");
        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, x => Assert.Equal("UnresolvedBinding_Kd", x.Code));
        Assert.Equal(new[] { "Missing", "Other" }, diagnostics.OrderBy(x => x.Span.Start).Select(x => x.Text));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmMissingConformanceBindingReusesDiagnosticCauseStorage()
    {
        var c = Parse("public struct Reading\n    Self is Copy\n    Self is Missing\n    public let value: i32");
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Missing conformance became valid.");
            }
        }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingConformanceDoesNotHideContradictoryInputsOfTheSameType(bool missingFirst)
    {
        const string Missing = "    Self is Missing\n";
        const string Inputs = "    T is i32\n    T is not i32\n";
        var c = Parse("public struct Reading<T>\n" + (missingFirst ? Missing + Inputs : Inputs + Missing));
        Assert.False(c.Bind().IsComplete);
        c.Binding.ReportDiagnostics();
        var diagnostics = TestDiagnostics.Of(c, "Hello.kimi");
        Assert.Contains(diagnostics, x => x.Code == "UnresolvedBinding_Kd");
        Assert.Contains(diagnostics, x => x.Code == "InvalidConstraint_Kd" && x.Span.Start == Container(c, "Reading").Span.Start);
    }

    [Theory]
    [InlineData("public struct Target\n    Future is Copy", "public struct Future\n    Self is Copy", true)]
    [InlineData("public contract Target\n    Future is Copy", "public struct Future\n    Self is Copy", true)]
    [InlineData("public enum Target\n    Future is Copy\n    A", "public struct Future\n    Self is Copy", true)]
    [InlineData("public struct Target\n    i32 is Future", "public struct Future", false)]
    [InlineData("public struct Target<T>\n    T is Future", "public contract Future", true)]
    [InlineData("group G\n    func take<T>()\n        T is Future\n        ()", "public contract Future", true)]
    [InlineData("public struct Target<T>\n    T is [2 of Future]", "public struct Future", true)]
    [InlineData("public struct Target\n    Self is Future", "public contract Future", true)]
    [InlineData("public struct Target<T>\n    T is not Future", "public contract Future", true)]
    // A conditional conformance's Contract and its conditions are pending like an unconditional one's (SPEC 8.8.1, 9.2, 9.5): a nearer
    // struct or group the Contract Reference role excludes decides nothing.
    [InlineData("public struct Target<T>\n    Self is Future when T is Copy", "public contract Future", true)]
    [InlineData("public group G\n    struct Future\n    public struct Target<T>\n        Self is Future when T is Copy", "public contract Future", true)]
    [InlineData("public group G\n    group Future\n        func z() -> i32 => 0\n    public enum Target<T>\n        Self is Future when T is Copy\n        A", "public contract Future", true)]
    [InlineData("public contract C\npublic struct Target<T>\n    Self is C when T is Future and Copy", "public contract Future", true)]
    [InlineData("public struct Target<T>\n    Self is Future when T is Copy", "public struct Future", false)]
    [InlineData("public struct Target<T>\n    Self is Future\n    Self is Future when T is Copy", "public contract Future", false)]
    // A subject rooted in an enclosing parameter whose projection qualifier is missing can still complete (SPEC 7.5, 8.5.2, 8.8.1), in
    // a `when` list, a function's region and a member's region rooted in the declaring Type's parameter.
    [InlineData("public contract C\npublic struct Target<T>\n    Self is C when T is Future, T.(Future).Item is Copy", "public contract Future\n    associate Item", true)]
    [InlineData("group G\n    func take<T>()\n        T is Future\n        T.(Future).Item is Copy\n        ()", "public contract Future\n    associate Item", true)]
    [InlineData("public struct Target<T>\n    public func m(self: ref/Self)\n        T is Future\n        T.(Future).Item is Copy\n        ()", "public contract Future\n    associate Item", true)]
    [InlineData("public contract C\npublic struct Target<T>\n    Self is C when T is Future, T.(Future).Item is Copy", "public contract Future", false)]
    // The unqualified projection (SPEC 8.5.2) finds its associated Type under the pending Constraint, in a function and a `when` list.
    [InlineData("func take<T>()\n    T is Future\n    T.Item is Copy\n    ()", "public contract Future\n    associate Item", true)]
    [InlineData("public contract C\npublic struct Target<T>\n    Self is C when T is Future, T.Item is Copy", "public contract Future\n    associate Item", true)]
    // A bound Contract reference whose Type argument or Contract is missing (SPEC 8.4.3, 8.6.1, 9.7.1), in every form.
    [InlineData("public contract C<U>\npublic struct Target<T>\n    Self is C<Future> when T is Copy", "public struct Future", true)]
    [InlineData("public contract C<U>\npublic struct Target\n    Self is C<Future>", "public struct Future", true)]
    [InlineData("public contract C<U>\ngroup G\n    func take<T>()\n        T is C<Future>\n        ()", "public struct Future", true)]
    [InlineData("public contract C<U>\npublic contract Target: C<Future>", "public struct Future", true)]
    [InlineData("public contract C<U>\npublic struct Target\n    Self is C<Future>", "public group Future\n    func z() -> i32 => 0", false)]
    // An associate selector (SPEC 8.5.1) is a Contract reference too: a missing Contract or Type argument.
    [InlineData("public struct Target\n    Self is Future\n    associate Future.Item is i32", "public contract Future\n    associate Item", true)]
    [InlineData("public contract C<U>\n    associate Item\npublic struct Target\n    Self is C<Future>\n    associate C<Future>.Item is i32", "public struct Future", true)]
    [InlineData("public struct Target\n    Self is Future\n    associate Future.Item is i32", "public contract Future", false)]
    // SPEC 6.1.2: a later fragment of the group declares the selector's Contract.
    [InlineData("public group G\n    func z() -> i32 => 0\npublic struct Target\n    Self is G.Future\n    associate G.Future.Item is i32", "public group G\n    public contract Future\n        associate Item", true)]
    public void MissingDeclarationsRemainUnresolvedUntilFinalBinding(string source, string generated, bool valid)
    {
        var c = Parse(source);
        var provisional = c.Binding.Bind(BindingMode.Provisional);
        Assert.Equal(0, provisional.InvalidCount);
        Assert.True(provisional.UnresolvedCount > 0);
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", generated));
        Assert.Empty(TestDiagnostics.Of(c, "Generated.kimi"));
        Assert.Equal(valid, c.Bind().IsComplete);
        Assert.Equal(valid, CompilationTestHelper.Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("public struct Target\n    Future is Copy")]
    [InlineData("public contract Target\n    Future is Copy")]
    [InlineData("public enum Target\n    Future is Copy\n    A")]
    [InlineData("public struct Target<T>\n    T is Future")]
    [InlineData("group G\n    func take<T>()\n        T is Future\n        ()")]
    // A group of the name is no candidate of a subject or a requirement (SPEC 9.2, 9.5), so it decides nothing: the name is missing.
    [InlineData("public group Future\npublic struct Target\n    i32 is Future")]
    [InlineData("public group Invalid\npublic struct Target\n    Invalid is Copy")]
    [InlineData("public group Invalid\npublic struct Target<T>\n    T is Future or Invalid")]
    [InlineData("public struct Future\npublic struct Target<T>\n    Self is Future when T is Copy")]
    [InlineData("public contract C\npublic struct Target<T>\n    Self is C when T is Future, T.(Future).Item is Copy")]
    [InlineData("group G\n    func take<T>()\n        T is Future\n        T.(Future).Item is Copy\n        ()")]
    [InlineData("public contract C<U>\npublic struct Target\n    Self is C<Future>")]
    [InlineData("public struct Target\n    Self is Future\n    associate Future.Item is i32")]
    public void FinalBindingRejectsNamesThatRemainMissing(string source)
    {
        var c = Parse(source);
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.FailureOf() is BindingFailure.MissingType or BindingFailure.InvalidConstraint);
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        Assert.False(CompilationTestHelper.Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("public struct Target<s/T>\n    s is Future")]
    // SPEC 8.8.1: a non-generic Type, a non-parameter subject and `or` or `not` fail whatever the missing name finds.
    [InlineData("public struct Target\n    Self is Future when Self is Copy")]
    [InlineData("public contract C\npublic struct Target<T>\n    Self is C when i32 is Future")]
    [InlineData("public contract C\npublic struct Target<T>\n    Self is C when T is Future or Copy")]
    [InlineData("public contract C\npublic struct Target<T>\n    Self is C when T is not Future")]
    // SPEC 7.5, 8.8.1: a projection rooted in a missing name is rooted in no enclosing parameter whatever the name finds; an arity is
    // decided by the written list (9.7.1).
    [InlineData("group G\n    func take<T>()\n        U.(Future).Item is Copy\n        ()")]
    [InlineData("public contract C\npublic struct Target<T>\n    Self is C when U.(Future).Item is Copy")]
    [InlineData("public contract C<U>\npublic struct Target\n    Self is C<Future, Other>")]
    [InlineData("public contract C<U>\npublic contract Target: C<Future, Other>")]
    // SPEC 7.5: a local function's subject is never rooted in its enclosing function's parameter; 9.7.1: a Contract without Type
    // parameters takes no argument list.
    [InlineData("func outer<T>()\n    T is Future\n    func inner()\n        T.(Future).Item is Copy\n        ()\n    ()")]
    [InlineData("public contract C\nfunc take<T>()\n    T is C<Future>\n    ()")]
    // A selector naming an available Contract that is no conformance of the owner (8.5.1) is decided by what is written.
    [InlineData("public contract C\n    associate Item\npublic contract D\n    associate Item\npublic struct Target\n    Self is C\n    associate D.Item is i32")]
    public void AvailableViolationsRemainInvalidInProvisionalBinding(string source)
    {
        var c = Parse(source);
        Assert.True(c.Binding.Bind(BindingMode.Provisional).InvalidCount > 0);
        Assert.False(c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("Copy or Future")]
    [InlineData("Future or Copy")]
    [InlineData("not ((not Copy) and Future)")]
    public void ATrueBooleanBranchDoesNotCompleteAnUnformedRequirement(string requirement)
    {
        var c = Parse("public contract Marker\npublic struct Target\n    i32 is " + requirement + "\n    Self is Marker");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        var target = Container(c, "Target");
        Assert.Equal(BindingState.Unresolved, target.StateOf());
        Assert.False(c.Binding.GetConformanceDefinition(target.TypeOf()!, Container(c, "Marker").SymbolOf()!)!.IsVerified);
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", "public contract Future"));
        Assert.True(c.Bind().IsComplete, string.Join(", ", c.Binding.Issues.Select(x => $"{x.Node.Akind}:{x.Node.FailureOf()}:{x.Node.Span}")));
        Assert.True(CompilationTestHelper.Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("Future", false)]
    [InlineData("Future or Copy", true)]
    public void MissingFunctionRequirementsCannotPublishCompletedCalls(string requirement, bool valid)
    {
        var c = Parse("group G\n    func take<T>()\n        T is " + requirement + "\n        ()\n    func call() => take<i32>()");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        var function = Container(c, "G").Members.OfType<FunctionKoto>().Single(x => x.Name == "take");
        Assert.Equal(BindingState.Unresolved, function.StateOf());
        var call = Assert.IsType<InvocationKoto>(Container(c, "G").Members.OfType<FunctionKoto>().Single(x => x.Name == "call").ExpressionBody);
        Assert.Null(call.CallOf());
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", "public contract Future"));
        Assert.Equal(valid, c.Bind().IsComplete);
        Assert.Equal(valid, call.CallOf() is not null);
    }

    // A conformance whose Contract is pending leaves its Type pending, conditional or not (SPEC 8.6.2, 8.8.3, BU-151): once
    // `contract C: D` exists S<i64> is D through C, so neither `X is D` (take) nor `X is not D` (plain) is decided before it.
    [Theory]
    [InlineData("public struct S<T>\n    Self is D when T is i32\n    Self is C when T is i64", "take", true)]
    [InlineData("public struct S<T>\n    Self is D when T is i32\n    Self is C when T is i64", "plain", false)]
    [InlineData("public enum S<T>\n    Self is D when T is i32\n    Self is C when T is i64\n    A", "plain", false)]
    [InlineData("public struct S<T>\n    Self is D when T is i32\n    Self is C", "plain", false)]
    public void PendingConformancesLeaveTheirTypePending(string type, string callee, bool valid)
    {
        var c = Parse("public contract D\n" + type + "\nfunc take<X>(x: X)\n    X is D\n    ()\nfunc plain<X>(x: X)\n    X is not D\n    ()\nfunc use(x: S<i64>) => " + callee + "(x@move)");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        Assert.Equal(BindingState.Unresolved, Container(c, "S").StateOf());
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
        Assert.Equal(BindingState.Unresolved, call.StateOf());
        Assert.Null(call.CallOf());
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", "public contract C: D"));
        Assert.Equal(valid, c.Bind().IsComplete);
        Assert.Equal(valid, CompilationTestHelper.Reload(c).Bind().IsComplete);
    }

    // A pending target is proven in its Type's scope and hides no independent failure (SPEC 23.1.2): a condition's malformed Type fails
    // the conformance, never the Type; another conformance's missing implementation stays its own failure.
    [Theory]
    [InlineData("public struct Box<U>\n    U is i64\npublic struct S<T>\n    Self is C when T is Box<i32>", "Box<i32>")]
    [InlineData("public contract D\n    func m(self: ref/Self) -> i32\npublic struct S<T>\n    Self is D\n    Self is C when T is Copy", "Self is D")]
    public void PendingConformancesKeepIndependentFailures(string source, string independent)
    {
        var c = Parse(source);
        Assert.True(c.Binding.Bind(BindingMode.Provisional).InvalidCount > 0);
        Assert.Equal(BindingState.Unresolved, Container(c, "S").StateOf());
        Assert.Contains(KotoTree.Walk(c.Kotonoha.RootKoto), x => x.ToString() == independent && x.StateOf() == BindingState.Invalid);
        Assert.False(c.Bind().IsComplete);
    }

    // A pending condition is a premise (SPEC 8.8.1-8.8.3): a proof applying the conformance reads it as Unknown, so a use waits while
    // the Type stays decided; once `contract E` exists i64 is not E, so S<i64> is neither C nor D.
    [Theory]
    [InlineData("take", false)]
    [InlineData("plain", true)]
    public void PendingConditionsLeaveTheirUsesPending(string callee, bool valid)
    {
        var c = Parse("public contract D\npublic contract C: D\npublic struct S<T>\n    Self is C when T is E\nfunc take<X>(x: X)\n    X is D\n    ()\nfunc plain<X>(x: X)\n    X is not D\n    ()\nfunc use(x: S<i64>) => " + callee + "(x@move)");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        Assert.Equal(BindingState.Resolved, Container(c, "S").StateOf());
        Assert.Equal(BindingState.Unresolved, Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>()).StateOf());
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", "public contract E"));
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Fact]
    public void MissingPropositionsAreNotAddedAsAssumptions()
    {
        var c = Parse("public struct Target<T>\n    T is Future\n    T is not Other");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        var target = Container(c, "Target");
        foreach (var clause in target.ConstraintNodes)
        {
            Assert.Equal(ConstraintProof.Unknown, c.Binding.Prove(clause.ConstraintOf()!, target));
        }

        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", "public contract Future\npublic contract Other"));
        Assert.True(c.Bind().IsComplete);
    }

    [Fact]
    public void FunctionSubjectRestrictionsRemainDiagnostics()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", "group G\n    func take<T>()\n        Future is Copy\n        ()"));
        // SPEC 7.5: the leading clause is a Constraint whatever it names; Binding rejects its subject, and does not wait for a source.
        Assert.True(c.Binding.Bind(BindingMode.Provisional).InvalidCount > 0);
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(["UnresolvedBinding_Kd"], c.Binding.Issues.Select(static x => x.Code.ToString()));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmMissingNamePassesAllocateNothing()
    {
        var c = Parse("public struct Target\n    Future is Copy");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (c.Binding.Bind(BindingMode.Provisional).InvalidCount != 0)
            {
                throw new InvalidOperationException("Missing constraint declaration became invalid.");
            }
        }));
    }

    private static DeclarationContainerKoto Container(Compilation c, string name)
        => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == name);

    private static Compilation Parse(string source)
    {
        var c = CompilationTestHelper.Parse(source, "Hello.kimi");
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        return c;
    }
}
