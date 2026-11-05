// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class GuardEmissionTest
{
    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "Candidate", "func f(n: i32) -> string\n    return match n\n        let x if x < 0 => \"negative\"\n        let x if x == 0 => \"zero\"\n        _ => \"positive\"\nConsole.writeLine(f(-1))\nConsole.writeLine(f(0))\nConsole.writeLine(f(1))", "negative\nzero\npositive\n" },
        { "Duplicate", "func test() -> bool\n    Console.writeLine(\"guard\")\n    return false\nmatch 0\n    0 if test() => ()\n    0 => Console.writeLine(\"ok\")\n    _ => ()", "guard\nok\n" },
        { "Mismatch", "func test() -> bool\n    Console.writeLine(\"bad\")\n    return true\nmatch 1\n    0 if test() => ()\n    _ => Console.writeLine(\"ok\")", "ok\n" },
        { "Mutation", "var state = 0\nmatch 1\n    _ if (label check: do\n        state = 7\n        exit to check false\n    ) => ()\n    _ => if state == 7 => Console.writeLine(\"ok\")", "ok\n" },
        { "Snapshot", "var value = 7\nmatch value@move\n    let n if (label check: do\n        value = 9\n        exit to check n == 7\n    ) => if n == 7 and value == 9 => Console.writeLine(\"ok\")\n    _ => ()", "ok\n" },
        { "Unit", "match ()\n    let n if true => ()\n    () => ()\nConsole.writeLine(\"ok\")", "ok\n" },
        { "Boolean", "match false\n    true if true => ()\n    false if false => ()\n    true => ()\n    false => Console.writeLine(\"ok\")\n    _ => ()", "ok\n" },
        { "Return", "func f() -> string\n    match 1\n        let n if (return \"ok\") => \"bad\"\n        _ => \"other\"\n    return \"after\"\nConsole.writeLine(f())", "ok\n" },
        { "StringCondition", "func echo(text: string) -> string => text@move\nmatch 1\n    let n if echo(\"a\") == \"a\" and n == 1 => Console.writeLine(\"ok\")\n    _ => ()", "ok\n" },
        { "ShortCircuit", "func test() -> bool\n    Console.writeLine(\"bad\")\n    return true\nmatch 1\n    _ if false and test() => ()\n    _ if true or test() => Console.writeLine(\"ok\")\n    _ => ()", "ok\n" },
        { "BodyVar", "match 1@move\n    var n if n == 1\n        n += 1\n        if n == 2 => Console.writeLine(\"ok\")\n    _ => ()", "ok\n" },
        { "Nested", "match 1\n    let n if (match n\n        let x if x == 1 => true\n        _ => false\n    ) => Console.writeLine(\"ok\")\n    _ => ()", "ok\n" },
        { "Loop", "var n = 0\nwhile n < 3\n    n += 1\n    match n\n        let x if x == 1 => continue\n        let x if x == 2 => Console.writeLine(\"two\")\n        _ => exit", "two\n" },
        { "Covered", "match 1\n    _ => Console.writeLine(\"ok\")\n    let x if x == 1 => Console.writeLine(\"bad\")", "ok\n" },
        { "OuterLoan", "let text = \"a\"\nif text == (match 1\n    let n if n == 1 => \"a\"\n    _ => \"b\"\n) => Console.writeLine(text)", "a\n" },
        { "Arguments", "func same(a: i32, b: i32) -> bool => a == b\nmatch 3\n    let n if same(n, if true => n else => n) => Console.writeLine(\"ok\")\n    _ => ()", "ok\n" },
        { "Deferred", "defer\n    match 1\n        let x if x == 1 => Console.writeLine(\"ok\")\n        _ => ()", "ok\n" },
        { "DeferredSnapshot", "var flag = true\nmatch 1\n    _ if (label check: do\n        defer => flag = false\n        exit to check flag\n    ) => if not flag => Console.writeLine(\"ok\")\n    _ => ()", "ok\n" },
        { "OuterYield", "let result = label outer: match 0\n    _ => match 1\n        _ if (yield 7) => 1\n        _ => 2\nif result == 7 => Console.writeLine(\"ok\")", "ok\n" },
        { "ReturnCandidate", "func f() -> i32\n    match 7\n        let n if (return n) => ()\n        _ => ()\n    return 0\nif f() == 7 => Console.writeLine(\"ok\")", "ok\n" },
        { "GuardContinue", "var n = 0\nwhile n < 2\n    n += 1\n    match n\n        _ if (if n == 1 => continue else => true) => Console.writeLine(\"ok\")\n        _ => ()", "ok\n" },
        { "ReturningGuard", "func f() -> i32\n    return match 7\n        let n if (return 9) => 1\n        _ => 0\nif f() == 9 => Console.writeLine(\"ok\")", "ok\n" },
        { "ConditionalMove", "var text = \"a\"\nmatch 1\n    _ if (label check: do\n        Console.writeLine(text@move)\n        exit to check false\n    ) => ()\n    _ => ()\ntext = \"b\"\nConsole.writeLine(text)", "a\nb\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Execute(string name, string source, string expected)
        => ScalarEmissionTest.EmitFixture("Guard" + name, source, expected);

    [Theory]
    [InlineData("match 1\n    var n if (label check: do\n        n = 2\n        exit to check true\n    ) => ()\n    _ => ()")]
    [InlineData("match 1.0\n    _ if true => ()\n    _ => ()", true)]
    [InlineData("match 1\n    _ if true => ()")]
    [InlineData("match 1\n    _ => ()\n    _ if 1.0 + 2.0 > 0.0 => ()", true)]
    [InlineData("let result = label work: match 1\n    _ if (yield to work true) => true\n    _ => false")]
    [InlineData("match 1\n    var n if ++n == 2 => ()\n    _ => ()")]
    [InlineData("let text = \"a\"\nmatch 1\n    _ if (label check: do\n        _ = text@move\n        exit to check false\n    ) => ()\n    _ => Console.writeLine(text)")]
    [InlineData("let text = \"a\"\nmatch 1\n    _ if (label check: do\n        _ = text@move\n        exit to check false\n    ) => ()\n    _ => ()\n    _ => Console.writeLine(text)")]
    [InlineData("let text = \"a\"\nmatch 1\n    _ if (label check: do\n        Console.writeLine(text)\n        exit to check false\n    ) => ()\n    _ => Console.writeLine(text)", true)]
    public void GuardSupportPreservesInvalidUseRejection(string source, bool emitted = false)
    {
        var c = MinimalEmissionTest.Analyze(source);
        FloatEmissionTest.AssertEmissionSupport(c, emitted);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmAnalysisAndWritingAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Fixtures.First().Data.Item2);
        var valid = true;
        var allocated = AllocationMeasurement.Measure(() =>
        {
            valid &= c.Analyze().IsVerified;
            valid &= c.Generate(TextWriter.Null);
        });
        Assert.True(valid);
        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData("i8", "-128")]
    [InlineData("u8", "255")]
    [InlineData("i16", "-32768")]
    [InlineData("u16", "65535")]
    [InlineData("i32", "-2147483648")]
    [InlineData("u32", "4294967295")]
    [InlineData("i64", "-9223372036854775808")]
    [InlineData("u64", "18446744073709551615")]
    [InlineData("isize", "-9223372036854775808")]
    [InlineData("usize", "18446744073709551615")]
    public void CandidateWidth(string type, string literal)
        => ScalarEmissionTest.EmitFixture("GuardWidth" + type, $"func f(value: {type}) -> bool\n    return match value\n        let n if n == {literal} => true\n        _ => false\nif f({literal}) => Console.writeLine(\"ok\")", "ok\n");

    // The guard's temporaries are destroyed after its value and before its branch to either continuation (their MIR: tests/mir/matches.kimi
    // `guardCleanup`).
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GuardTemporaryCleanupPrecedesEitherContinuation(bool success)
    {
        var source = "func echo(text: string) -> string => text@move\nmatch 1\n    _ if echo(\"a\") == \"" + (success ? "a" : "b") + "\" => Console.writeLine(\"selected\")\n    _ => Console.writeLine(\"fallback\")";
        var stdout = success ? "selected\n" : "fallback\n";
        var name = "GuardCleanup" + success;
        var ir = ScalarEmissionTest.EmitFixture(name, source, stdout);
        StringEmissionTest.WriteAuditedFixture(name, ir, stdout, success ? "a=1;selected=1;fallback=0" : "a=1;b=0;selected=0;fallback=1");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RebindingReusesDistinctCandidateIdentity()
    {
        var c = MinimalEmissionTest.Analyze("match 1\n    let n if n == 1 => ()\n    _ => ()");
        var match = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<MatchKoto>());
        Assert.True(c.Binding.TryGetMatch(match, out var plan));
        var candidate = plan!.Positions[0].CandidateSymbol;
        var valid = true;
        var allocated = AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete);
        Assert.True(valid);
        Assert.Equal(0, allocated);
        Assert.True(c.Binding.TryGetMatch(match, out plan));
        Assert.Same(candidate, plan!.Positions[0].CandidateSymbol);
    }

    [Fact]
    public void AbortInGuardPreventsBodyAndFallback()
    {
        const string Source = "match 1\n    _ if (label check: do\n        var n = 2147483647\n        n += 1\n        exit to check true\n    ) => Console.writeLine(\"bad\")\n    _ => Console.writeLine(\"after\")";
        ScalarEmissionTest.EmitFixture("GuardAbort", Source, string.Empty, 1, "Hello.kimi:4:9: abort KIMI_E_INT_OVERFLOW: Integer overflow\n");
    }

    // A guard that never completes branches nowhere, so the checks alone reach its body (its MIR: tests/mir/matches.kimi `divergentGuard`).
    [Fact]
    public void DivergentGuardHasNoSelectedBodyEdge()
    {
        const string Source = "func stop() -> Never\n    loop => ()\nmatch 1\n    _ if stop() => Console.writeLine(\"bad\")\n    _ => Console.writeLine(\"after\")";
        var ir = ScalarEmissionTest.EmitFixture("GuardDivergent", Source, string.Empty, observationMilliseconds: 300);
        GeneratedIr.Parse(ir).AssertNoTerminationClaim();
    }

    [Fact]
    public void RemovedGuardScopeCannotResurrectCandidate()
    {
        var c = MinimalEmissionTest.Analyze("match 1\n    let n if n == 1 => ()\n    _ => ()");
        var match = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<MatchKoto>());
        var oldGuard = match.Arms[0].Guard!;
        match.Arms[0].Guard = null;
        oldGuard.Parent = null;
        ParseTestHelper.VerifyParents(c.Kotonoha.RootKoto);
        Assert.True(KotoHelper.Replace(match, match.Arms[0].Body, oldGuard));
        ParseTestHelper.VerifyParents(c.Kotonoha.RootKoto);
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Binding.TryGetMatch(match, out var plan));
        Assert.Null(plan!.Positions[0].CandidateSymbol);
        Assert.Same(plan.Positions[0].BodySymbol, ((BinaryKoto)oldGuard).Left.SymbolOf());
    }

    [Fact]
    public void CandidateCannotBeCapturedByLocalFunction()
    {
        var c = MinimalEmissionTest.Analyze("match 1\n    let n if (label check: do\n        func read() -> i32 => n\n        exit to check true\n    ) => ()\n    _ => ()");
        Assert.Contains(c.Binding.Issues, x => x.Code == Kimi.DiagnosticCode.InvalidCaptureBinding_Kd);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FalseGuardMoveReachesLaterCheckingState(bool covered)
    {
        var source = "let text = \"a\"\nmatch 1\n    _ if (label check: do\n        _ = text@move\n        exit to check false\n    ) => ()\n" + (covered ? "    _ => ()\n" : string.Empty) + "    _ => Console.writeLine(text)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.False(c.Analysis.IsVerified);
        Assert.True(c.Analysis.ErrorCount > 0);
        Assert.Contains(c.Problems, x => x.Code == DiagnosticCode.MovedPlace_Kd);
    }

    [Fact]
    public void OuterComparisonLoanSurvivesGuardCleanup()
    {
        const string Source = "let text = \"a\"\ntext == (match 1\n    _ if (label check: do\n        defer => _ = text@move\n        exit to check true\n    ) => \"a\"\n    _ => \"b\"\n)";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Problems, static x => x.Code.IsLoanConflict());
    }
}
