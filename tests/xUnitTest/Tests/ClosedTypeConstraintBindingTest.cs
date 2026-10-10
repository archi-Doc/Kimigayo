// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ClosedTypeConstraintBindingTest
{
    [Theory]
    [InlineData("struct", "i32 is Copy", true)]
    [InlineData("enum", "i32 is Copy", true)]
    [InlineData("struct", "i32 is string", false)]
    [InlineData("enum", "i32 is string", false)]
    [InlineData("struct", "Source is Origin", true)]
    [InlineData("enum", "Source is Origin", true)]
    [InlineData("struct", "string is not Copy", true)]
    [InlineData("enum", "string is not Copy", true)]
    [InlineData("struct", "i32 is i32 or string", true)]
    [InlineData("enum", "i32 is i32 and string", false)]
    [InlineData("struct", "Source is not Origin", false)]
    public void ClosedSubjectsAreDeclarationObligations(string kind, string clause, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(Source(kind, clause));
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid ? BindingState.Resolved : BindingState.Invalid, Target(c).StateOf());
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, x => x.Node.FailureOf() == BindingFailure.UnsatisfiedConstraint);
        }

        Assert.Equal(valid, c.Bind().IsComplete);
        var restored = CompilationTestHelper.Reload(c);
        Assert.Equal(valid, restored.Bind().IsComplete);
        Assert.Equal(valid ? BindingState.Resolved : BindingState.Invalid, Target(restored).StateOf());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AClosedClauseDoesNotDeclareConformance(bool generic)
    {
        var c = MinimalEmissionTest.Analyze("public contract Origin\npublic struct Source\npublic struct Target" + (generic ? "<T>" : string.Empty) + "\n    Source is Origin");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(BindingState.Invalid, Target(c).StateOf());
        var source = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Source");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Origin");
        Assert.Null(c.Binding.GetConformanceDefinition(source.TypeOf()!, contract.BoundSymbol!));
        Assert.Contains(c.Binding.Issues, x => x.Node.FailureOf() == BindingFailure.UnsatisfiedConstraint);
    }

    [Theory]
    [InlineData("struct")]
    [InlineData("enum")]
    public void FailedClosedObligationRevokesConformance(string kind)
    {
        var c = MinimalEmissionTest.Analyze(Source(kind, "i32 is string\n    Self is Origin"));
        Assert.False(c.Binding.Result.IsComplete);
        var target = Target(c);
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Origin");
        Assert.False(c.Binding.GetConformanceDefinition(target.TypeOf()!, contract.BoundSymbol!)?.IsVerified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LateInvalidClosedSubjectRevokesDependentCertificates(bool reverse)
    {
        const string source = "public struct Source\n    string is Copy\n    Self is Origin\n";
        const string target = "public struct Target\n    Source is Origin\n    Self is Origin\n    public computed value: i32\n        get(self: ref/Self) -> i32 => 1\n    public func read() -> i32 => 1\n";
        var c = MinimalEmissionTest.Analyze("public contract Origin\n" + (reverse ? target + source : source + target) + "let x = Target.read()");
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(BindingState.Invalid, Target(c).StateOf());
        Assert.Null(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Single().BoundCall);
        Assert.False(Target(c).Members.OfType<PropertyKoto>().Single().BoundSymbol!.Property!.IsVerified);
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Origin");
        Assert.False(c.Binding.GetConformanceDefinition(Target(c).TypeOf()!, contract.BoundSymbol!)?.IsVerified);
    }

    [Theory]
    [InlineData("public", true)]
    [InlineData("internal", false)]
    public void ClosedSubjectsRespectApiExposure(string access, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(Source("struct", "Source is Origin or Copy").Replace("public struct Source", access + " struct Source", StringComparison.Ordinal));
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, x => x.Node.FailureOf() == BindingFailure.Access);
        }
    }

    [Theory]
    [InlineData("i32 is Copy")]
    [InlineData("Source is Origin")]
    public void FunctionSubjectsRetainTheirOwnRestriction(string clause)
    {
        var c = MinimalEmissionTest.Analyze("public contract Origin\npublic struct Source\n    Self is Origin\nfunc f<T>()\n    " + clause + "\n    ()");
        Assert.False(c.Binding.Result.IsComplete);

        // The parser reports the prefix whose subject the function does not permit (SPEC 7.4); Binding's own restriction rests on that Error.
        Assert.Contains(c.Binding.DerivedIssues, x => x.FailureOf() == BindingFailure.InvalidConstraint);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Node.FailureOf() == BindingFailure.InvalidConstraint);
        Assert.Contains(TestDiagnostics.Of(c), x => x.Code == nameof(DiagnosticCode.MisplacedSyntax_Kd));
    }

    [Fact]
    public void ReplacingAClosedRequirementRestoresTheDeclaration()
    {
        var c = MinimalEmissionTest.Analyze(Source("struct", "i32 is string"));
        var clause = Target(c).ConstraintNodes[0];
        var original = clause.Right;
        var donor = MinimalEmissionTest.Analyze(Source("struct", "i32 is Copy"));
        var replacement = Target(donor).ConstraintNodes[0].Right;
        Assert.True(KotoHelper.Replace(clause, original, replacement));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(KotoHelper.Replace(clause, replacement, original));
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(BindingState.Invalid, Target(c).StateOf());
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmClosedObligationChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Source("struct", "Source is Origin\n    i32 is Copy"));
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Closed constraint failed.");
            }
        }));
    }

    private static string Source(string kind, string clause)
        => "public contract Origin\npublic struct Source\n    Self is Origin\npublic " + kind + " Target\n    " + clause + (kind == "enum" ? "\n    A" : string.Empty);

    private static DeclarationContainerKoto Target(Compilation c)
        => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Target");
}
