// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 8.4.7.2 and 13.5.1: payload role evidence and independent adaptation-target lookup.</summary>
public class AdaptationTargetBindingTest
{
    [Theory]
    [InlineData("T is ObjectPayload", "")]
    [InlineData("T is Payload", "contract Payload\n    Self is ObjectPayload\n")]
    public void ObjectPayloadProvesThePairTargetsValueRole(string premise, string declarations)
    {
        var source = declarations + "func box<s/T>(value: T) -> obj/T\n    s is object\n    " + premise + "\n    return Kimi.Intrinsics.makeObj(value@move)";
        var c = CompilationTestHelper.Parse(source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.True(CompilationTestHelper.Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("s is object")]
    [InlineData("s is objectborrow")]
    public void ObjectTargetEvidenceAloneDoesNotProveAValuePayload(string premise)
    {
        var c = CompilationTestHelper.Parse("func box<s/T>(value: T) -> obj/T\n    " + premise + "\n    return Kimi.Intrinsics.makeObj(value@move)");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnprovenConstraint_Kd);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.Unsupported_Kd);
    }

    [Theory]
    [InlineData("value@move@s")]
    [InlineData("7@s")]
    public void ATypeAndSemanticsAtDifferentStagesAreAmbiguous(string expression)
    {
        var c = CompilationTestHelper.Parse("struct s\nfunc adapt<s/T>(value: T)\n    s is object\n    T is ObjectPayload\n    _ = " + expression);
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.AmbiguousBinding_Kd);
    }

    [Fact]
    public void AnAmbiguityWithinTheTypeRoleCannotBeSolvedByTheOperand()
    {
        const string Source = "alias A\nalias B\ngroup A\n    public struct Target\ngroup B\n    public struct Target\nlet value = 7@Target";
        var c = CompilationTestHelper.Parse(Source);
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.AmbiguousBinding_Kd);
    }

    [Fact]
    public void ACompleteSemanticsTargetDoesNotCompeteWithTheTypeRole()
    {
        const string Source = "struct s\nfunc adapt<s/T>(value: T) -> s/T\n    s is object\n    T is ObjectPayload\n    return value@move@s/T";
        var c = CompilationTestHelper.Parse(Source);
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void QualificationSelectsOnlyTheTypeRole()
    {
        const string Source = "struct s\n    Self is Copy\nfunc adapt<s/T>(value: ::s) -> ::s\n    s is object\n    return value@::s";
        var c = CompilationTestHelper.Parse(Source);
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void TheFirstEligibleTypeStageDoesNotReopenForArity()
    {
        const string Source = "struct Target\ngroup Scope\n    struct Target<T>\n    func adapt(value: ::Target)\n        _ = value@move@Target";
        var c = CompilationTestHelper.Parse(Source);
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidTypeFormation_Kd);
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("Container")]
    [InlineData("Requirement")]
    public void MissingAndWrongRoleTargetsRetainOrdinaryErrors(string target)
    {
        var c = CompilationTestHelper.Parse("group Container\ncontract Requirement\nlet value = 7@" + target);
        Assert.False(c.Bind().IsComplete);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.Unsupported_Kd || x.Code == DiagnosticCode.AmbiguousBinding_Kd);
    }

    private static string Describe(Compilation c) => string.Join("; ", c.Binding.Issues);
}
