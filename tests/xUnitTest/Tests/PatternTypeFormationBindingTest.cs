// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class PatternTypeFormationBindingTest
{
    [Theory]
    [InlineData("Box<string>", "_")]
    [InlineData("raw/Box<string>", "_")]
    [InlineData("Option<Box<string>>", ".Some(_)\n    .None")]
    [InlineData("Option<Box<string>>", ".Some(_)")]
    [InlineData("Box<string>", "_\n    _")]
    [InlineData("Box<string>", "let item")]
    public void InvalidSubjectTypesCannotPublishCoverage(string type, string pattern)
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\nfunc f(value: " + type + ") => match value\n    " + pattern.Replace("\n", " => ()\n", StringComparison.Ordinal) + " => ()");
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(MatchCoverageState.Invalid, Plan(c).Coverage.State);
        Assert.Empty(c.Binding.PatternWarnings);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Node.FailureOf() == BindingFailure.NonExhaustiveMatch);
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(MatchCoverageState.Invalid, Plan(c).Coverage.State);
        var restored = CompilationTestHelper.Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.Equal(MatchCoverageState.Invalid, Plan(restored).Coverage.State);
    }

    [Fact]
    public void InvalidEnumContextCannotPublishWildcardCoverage()
    {
        var c = MinimalEmissionTest.Analyze("#Unknown\ngroup Invalid\n    public enum E\n        A\nfunc f(value: Invalid.E) => match value\n    _ => ()");
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(MatchCoverageState.Invalid, Plan(c).Coverage.State);
    }

    [Theory]
    [InlineData("Box<i32>", "_")]
    [InlineData("raw/Box<i32>", "_")]
    [InlineData("Option<Box<i32>>", ".Some(_)\n    .None")]
    public void ValidSubjectTypesRetainCoverage(string type, string pattern)
    {
        // A structural pattern decomposes an owned Non-Copy subject only under `match value@move` (SPEC 15.1.6).
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\nfunc f(value: " + type + ") => match value@move\n    " + pattern.Replace("\n", " => ()\n", StringComparison.Ordinal) + " => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Equal(MatchCoverageState.Exhaustive, Plan(c).Coverage.State);
        var restored = CompilationTestHelper.Reload(c);
        Assert.True(restored.Bind().IsComplete);
        Assert.Equal(MatchCoverageState.Exhaustive, Plan(restored).Coverage.State);
    }

    [Fact]
    public void DependentSubjectUsesDefinitionEvidence()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\nfunc f<U>(value: Box<U>)\n    U is i32\n    match value\n        _ => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    [Fact]
    public void RemovingInvalidOwnerRestoresRetainedPlan()
    {
        var c = MinimalEmissionTest.Analyze("group Types\n    public enum E\n        A\nfunc f(value: Types.E) => match value\n    _ => ()");
        Assert.True(c.Binding.Result.IsComplete);
        var plan = Plan(c);
        c.Kotonoha.AddSource(new SourceDocument("marker.kimi", "#Unknown\ngroup Types"));
        Assert.False(c.Bind().IsComplete);
        Assert.Same(plan, Plan(c));
        Assert.Equal(MatchCoverageState.Invalid, plan.Coverage.State);
        var owner = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Types");
        Assert.True(owner.RemoveAttribute(Assert.IsType<AttributeKoto>(owner.AttributeChain)));
        Assert.True(c.Bind().IsComplete);
        Assert.Same(plan, Plan(c));
        Assert.Equal(MatchCoverageState.Exhaustive, plan.Coverage.State);
    }

    [Fact]
    public void LateEnumApiFailureInvalidatesEarlierCoverage()
    {
        var c = MinimalEmissionTest.Analyze("contract Hidden\npublic struct Source\n    Self is Hidden\npublic enum E<T>\n    T is Hidden\n    A\nfunc f(value: E<Source>) => match value\n    _ => ()");
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.FailureOf() == BindingFailure.Access);
        Assert.Equal(MatchCoverageState.Invalid, Plan(c).Coverage.State);

        // A Pattern whose Type fails the late recheck keeps its kind and its test (this Wildcard tests nothing), so its match is built
        // with its coverage Invalid, no longer an Opaque (DECISIONS R4-M179 as D-1163 amends it).
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var body = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == "f");
        Assert.DoesNotContain(body.Statements.ToArray(), static x => x is { Kind: MirStatementKind.Assign, Rvalue.Kind: MirRvalueKind.Opaque });
    }

    [Fact]
    public void UnrelatedInvalidContextPreservesValidPattern()
    {
        var c = MinimalEmissionTest.Analyze("#Unknown\ngroup Invalid\nfunc f(value: bool) => match value\n    _ => ()");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(MatchCoverageState.Exhaustive, Plan(c).Coverage.State);
    }

    // SPEC 23.1.2 (rule W, PLAN G1321), 8.3, 17.3: a match judges its Subject Type's formation once, at its root position (the first
    // arm's Pattern; a `try`'s first synthesized Pattern, never the `try` or its operand), and every other arm rests on that record:
    // the written match's second arm adds none (its first arm restates 3:18 at 5:9, PLAN G1278's family). Each published error as
    // Code@line:column. Controls: a valid Subject Type keeps the `try`'s 17.3 record, and its `@move` twin is valid.
    [Theory]
    [InlineData("func f(o: Option<Box<string>>) -> i32\n    return match o\n        .Some(_) => 1\n        .None => 0", "UnsatisfiedConstraint_Kd@3:18 | UnsatisfiedConstraint_Kd@5:9")]
    [InlineData("func f(o: Option<Box<i32>>) -> Option<i32>\n    let b = try o\n    return .Some(1)", "TransferRequired_Kd@4:17")]
    [InlineData("func f(o: Option<Box<i32>>) -> Option<i32>\n    let b = try o@move\n    return .Some(1)", "")]
    public void ASubjectTypeIsJudgedOnceAtTheMatchRoot(string function, string expected)
    {
        var source = "struct Box<T>\n    T is i32\n" + function + "\n\npublic func main()\n    Console.writeLine(\"x\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(expected.Length == 0, c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.Equal(expected, TestDiagnostics.ErrorSites(c, source));
    }

    // The same rule at a `try`, whose two synthesized arms share its span: a `try` over a failed operand Type records its Subject Type's
    // failure once, at its first synthesized Pattern, so its site holds no two equal records and no internal fault (before: two equal
    // records at one site, CheckFaulted_Kd). Each row asserts that and the records its SPEC labels give that this rule keeps
    // (`required`); it pins no exact list. Labels (SPEC 17.3, 23.1.2): gtrymin is invalid by one independent failure, its written
    // Constraint's subject `I.Iterator.Item` (3:5: I declares no member Iterator; 8.5.2 spells the projection `I.(Iterator).Item`); its
    // operand, a call's temporary, is transferred and its return target `Option<i64>` takes `.None`, so the `try` (6:18) and its operand
    // (6:22) read only that failed Constraint and rest on it. gtryinv and gtryres are invalid by `Box<string>` where the parameter Type is
    // written (3:18, 8.3) and by the bare non-Copy Place operand moved without `@move` (4:17, 17.3); gtryres's return target
    // `Result<i32, i32>` takes `.Err(error)`, error `i32` fitting `i32`, so it holds; the `try`'s first synthesized Pattern restates 3:18
    // (4:13, PLAN G1278's family). Not asserted, a known remainder while the arms rest on a refuted Subject Type (PLAN, after G1278):
    // gtryinv's and gtryres's TransferRequired_Kd@4:17 is missing and gtryres gives an InvalidTryReturn_Kd@4:13.
    [Theory]
    [InlineData("func drain<I>(iterator: uniq/I) -> Option<i64>\n    I is Iterator\n    I.Iterator.Item is i64\n    var total: i64 = 0\n    loop\n        total += try iterator.next()\n        if total > 10\n            exit\n    return .Some(total)", "UnresolvedBinding_Kd@3:5")]
    [InlineData("struct Box<T>\n    T is i32\nfunc f(o: Option<Box<string>>) -> Option<i32>\n    let b = try o\n    return .Some(1)", "UnsatisfiedConstraint_Kd@3:18 | UnsatisfiedConstraint_Kd@4:13")]
    [InlineData("struct Box<T>\n    T is i32\nfunc f(o: Result<Box<string>, i32>) -> Result<i32, i32>\n    let b = try o\n    return .Ok(1)", "UnsatisfiedConstraint_Kd@3:18 | UnsatisfiedConstraint_Kd@4:13")]
    public void ATryOverAFailedOperandTypeRecordsOncePerSite(string function, string required)
    {
        var source = function + "\n\npublic func main()\n    Console.writeLine(\"x\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var errors = TestDiagnostics.ErrorSites(c, source).Split(" | ");
        Assert.Equal(errors.Length, errors.Distinct().Count());
        Assert.DoesNotContain(errors, static x => x.StartsWith("CheckFaulted_Kd", StringComparison.Ordinal) || x.StartsWith("InternalInvariant_Kd", StringComparison.Ordinal));
        Assert.Empty(required.Split(" | ").Except(errors));

        // MIR is built for these programs (the fault ended the check before it): the builder meets no broken contract (C-551 K2).
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.DoesNotContain(program.Records(MirRecordClass.Construction), static x => x.Code == DiagnosticCode.InternalInvariant_Kd);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmPatternFormationChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\nfunc f(value: Option<Box<i32>>) => match value@move\n    .Some(_) => ()\n    .None => ()");
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Pattern formation failed.");
            }
        }));
    }

    private static BoundMatch Plan(Compilation c)
    {
        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "f");
        var match = Assert.IsType<MatchKoto>(function.ExpressionBody);
        Assert.True(c.Binding.TryGetMatch(match, out var plan));
        return Assert.IsType<BoundMatch>(plan);
    }
}
