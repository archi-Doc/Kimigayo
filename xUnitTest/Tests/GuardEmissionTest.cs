// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

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
        { "Mutation", "var state = 0\nmatch 1\n    _ if (check: do\n        state = 7\n        exit to check: false\n    ) => ()\n    _ => if state == 7 => Console.writeLine(\"ok\")", "ok\n" },
        { "Snapshot", "var value = 7\nmatch value\n    let n if (check: do\n        value = 9\n        exit to check: n == 7\n    ) => if n == 7 and value == 9 => Console.writeLine(\"ok\")\n    _ => ()", "ok\n" },
        { "Unit", "match ()\n    let n if true => ()\n    () => ()\nConsole.writeLine(\"ok\")", "ok\n" },
        { "Boolean", "match false\n    true if true => ()\n    false if false => ()\n    true => ()\n    false => Console.writeLine(\"ok\")\n    _ => ()", "ok\n" },
        { "Return", "func f() -> string\n    match 1\n        let n if (return \"ok\") => \"bad\"\n        _ => \"other\"\n    return \"after\"\nConsole.writeLine(f())", "ok\n" },
        { "StringCondition", "func echo(text: string) -> string => text@move\nmatch 1\n    let n if echo(\"a\") == \"a\" and n == 1 => Console.writeLine(\"ok\")\n    _ => ()", "ok\n" },
        { "ShortCircuit", "func test() -> bool\n    Console.writeLine(\"bad\")\n    return true\nmatch 1\n    _ if false and test() => ()\n    _ if true or test() => Console.writeLine(\"ok\")\n    _ => ()", "ok\n" },
        { "BodyVar", "match 1\n    var n if n == 1\n        n += 1\n        if n == 2 => Console.writeLine(\"ok\")\n    _ => ()", "ok\n" },
        { "Nested", "match 1\n    let n if (match n\n        let x if x == 1 => true\n        _ => false\n    ) => Console.writeLine(\"ok\")\n    _ => ()", "ok\n" },
        { "Loop", "var n = 0\nwhile n < 3\n    n += 1\n    match n\n        let x if x == 1 => continue\n        let x if x == 2 => Console.writeLine(\"two\")\n        _ => exit", "two\n" },
        { "Covered", "match 1\n    _ => Console.writeLine(\"ok\")\n    let x if x == 1 => Console.writeLine(\"bad\")", "ok\n" },
        { "OuterLoan", "let text = \"a\"\nif text == (match 1\n    let n if n == 1 => \"a\"\n    _ => \"b\"\n) => Console.writeLine(text)", "a\n" },
        { "Arguments", "func same(a: i32, b: i32) -> bool => a == b\nmatch 3\n    let n if same(n, if true => n else => n) => Console.writeLine(\"ok\")\n    _ => ()", "ok\n" },
        { "Deferred", "defer\n    match 1\n        let x if x == 1 => Console.writeLine(\"ok\")\n        _ => ()", "ok\n" },
        { "DeferredSnapshot", "var flag = true\nmatch 1\n    _ if (check: do\n        defer => flag = false\n        exit to check: flag\n    ) => if not flag => Console.writeLine(\"ok\")\n    _ => ()", "ok\n" },
        { "OuterYield", "let result = outer: match 0\n    _ => match 1\n        _ if (yield 7) => 1\n        _ => 2\nif result == 7 => Console.writeLine(\"ok\")", "ok\n" },
        { "ReturnCandidate", "func f() -> i32\n    match 7\n        let n if (return n) => ()\n        _ => ()\n    return 0\nif f() == 7 => Console.writeLine(\"ok\")", "ok\n" },
        { "GuardContinue", "var n = 0\nwhile n < 2\n    n += 1\n    match n\n        _ if (if n == 1 => continue else => true) => Console.writeLine(\"ok\")\n        _ => ()", "ok\n" },
        { "CheckedBody", "func f() -> i32\n    return match 7\n        let n if (return 9) => n\n        _ => 0\nif f() == 9 => Console.writeLine(\"ok\")", "ok\n" },
        { "ConditionalMove", "var text = \"a\"\nmatch 1\n    _ if (check: do\n        Console.writeLine(text@move)\n        exit to check: false\n    ) => ()\n    _ => ()\ntext = \"b\"\nConsole.writeLine(text)", "a\nb\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Execute(string name, string source, string expected)
        => ScalarEmissionTest.EmitFixture("Guard" + name, source, expected);

    [Theory]
    [InlineData("match 1\n    var n if (check: do\n        n = 2\n        exit to check: true\n    ) => ()\n    _ => ()")]
    [InlineData("match 1.0\n    _ if true => ()\n    _ => ()", true)]
    [InlineData("match 1\n    _ if true => ()")]
    [InlineData("match 1\n    _ => ()\n    _ if 1.0 + 2.0 > 0.0 => ()", true)]
    [InlineData("let result = work: match 1\n    _ if (yield to work: true) => true\n    _ => false")]
    [InlineData("match 1\n    var n if ++n == 2 => ()\n    _ => ()")]
    [InlineData("let text = \"a\"\nmatch 1\n    _ if (check: do\n        _ = text@move\n        exit to check: false\n    ) => ()\n    _ => Console.writeLine(text)")]
    [InlineData("let text = \"a\"\nmatch 1\n    _ if (check: do\n        _ = text@move\n        exit to check: false\n    ) => ()\n    _ => ()\n    _ => Console.writeLine(text)")]
    [InlineData("let text = \"a\"\nmatch 1\n    _ if (check: do\n        Console.writeLine(text)\n        exit to check: false\n    ) => ()\n    _ => Console.writeLine(text)", true)]
    public void GuardSupportPreservesInvalidUseRejection(string source, bool emitted = false)
    {
        var c = MinimalEmissionTest.Analyze(source);
        FloatEmissionTest.AssertEmissionSupport(c, emitted);
    }

    [Fact]
    public void CandidateIdentityAndStorage()
    {
        var c = MinimalEmissionTest.Analyze("match 7\n    let n if n == 7 => if n == 7 => ()\n    _ => ()");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        var body = c.Ownership.Bodies.Single();
        var pattern = body.Matches[0].Binding.Positions[0];
        Assert.NotNull(pattern.CandidateSymbol);
        Assert.NotSame(pattern.BodySymbol, pattern.CandidateSymbol);
        Assert.Same(pattern.BodySymbol!.Declaration, pattern.CandidateSymbol.Declaration);
        Assert.False(body.SymbolPlaces.ContainsKey(pattern.CandidateSymbol));
        Assert.Contains(body.Operations, x => x.Kind == OwnershipOperationKind.Read && x.Source.BoundSymbol == pattern.CandidateSymbol);
        Assert.DoesNotContain(module.GetFunction(0).Slots, x => x.Place == body.Matches[0].Subject);
    }

    [Fact]
    public void WarmAnalysisAndWritingAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Fixtures.First().Data.Item2);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var valid = true;
        for (var i = 0; i < 128; i++)
        {
            valid &= c.Ownership.Analyze().IsVerified;
            valid &= c.Emission.WriteIr(TextWriter.Null, out _);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(valid);
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GuardTemporaryCleanupPrecedesEitherContinuation(bool success)
    {
        var source = "func echo(text: string) -> string => text@move\nmatch 1\n    _ if echo(\"a\") == \"" + (success ? "a" : "b") + "\" => Console.writeLine(\"selected\")\n    _ => Console.writeLine(\"fallback\")";
        var stdout = success ? "selected\n" : "fallback\n";
        var name = "GuardCleanup" + success;
        var ir = ScalarEmissionTest.EmitFixture(name, source, stdout);
        StringEmissionTest.WriteAuditedFixture(name, source, ir, stdout, success ? "a=2;selected=1;fallback=0" : "a=1;b=1;selected=0;fallback=1");
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var body = c.Ownership.Bodies.Single(x => x.Matches.Count != 0);
        var arm = body.MatchArms[0];
        Assert.True(arm.GuardValue < arm.GuardCleanupStart);
        Assert.True(arm.GuardCleanupStart < arm.GuardBranch);
        Assert.True(arm.GuardBranch < arm.BodyEntry);
    }

    [Theory]
    [InlineData("candidate")]
    [InlineData("acquire")]
    [InlineData("cleanup")]
    [InlineData("boolean")]
    public void MalformedGuardPlansFailBeforeWriting(string defect)
    {
        var c = MinimalEmissionTest.Analyze("func echo(text: string) -> string => text@move\nmatch 1\n    let n if echo(\"a\") == \"a\" and n == 1 => ()\n    _ => ()");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies.Single(x => x.Matches.Count != 0);
        var arm = body.MatchArms[0];
        if (defect == "candidate")
        {
            var read = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Read && x.Source.BoundSymbol?.Kind == BindingSymbolKind.PatternCandidate);
            body.OperationSteps[read] = -1;
        }
        else if (defect == "boolean")
        {
            body.MatchArmStorage[0] = arm with { GuardValue = arm.Test };
        }
        else
        {
            var from = defect == "acquire" ? arm.Test : arm.GuardCleanupStart;
            var edge = body.EdgeStorage.FindIndex(x => x.From == from && (defect != "acquire" || x.Kind == OwnershipEdgeKind.True));
            body.EdgeStorage[edge] = body.Edges[edge] with { To = arm.BodyEntry };
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.Validate(out error), error);
    }

    [Fact]
    public void RebindingReusesDistinctCandidateIdentity()
    {
        var c = MinimalEmissionTest.Analyze("match 1\n    let n if n == 1 => ()\n    _ => ()");
        var candidate = c.Ownership.Bodies[0].Matches[0].Binding.Positions[0].CandidateSymbol;
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var valid = true;
        for (var i = 0; i < 100; i++)
        {
            valid &= c.Bind().IsComplete;
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(valid);
        c.Ownership.Analyze();
        Assert.Same(candidate, c.Ownership.Bodies[0].Matches[0].Binding.Positions[0].CandidateSymbol);
    }

    [Fact]
    public void AbortInGuardPreventsBodyAndFallback()
    {
        const string Source = "match 1\n    _ if (check: do\n        var n = 2147483647\n        n += 1\n        exit to check: true\n    ) => Console.writeLine(\"bad\")\n    _ => Console.writeLine(\"after\")";
        ScalarEmissionTest.EmitFixture("GuardAbort", Source, string.Empty, 1, "Hello.kimi:4:9: abort KIMI_E_INT_OVERFLOW: Integer overflow\n");
    }

    [Fact]
    public void DivergentGuardHasNoSelectedBodyEdge()
    {
        const string Source = "func stop() -> Never\n    loop => ()\nmatch 1\n    _ if stop() => Console.writeLine(\"bad\")\n    _ => Console.writeLine(\"after\")";
        var ir = ScalarEmissionTest.EmitFixture("GuardDivergent", Source, string.Empty, timeoutMilliseconds: 300);
        Assert.DoesNotContain("mustprogress", ir);
        var c = MinimalEmissionTest.Analyze(Source);
        var body = c.Ownership.Bodies.Single(x => x.Matches.Count != 0);
        Assert.Equal(-1, body.MatchArms[0].GuardBranch);
        Assert.False(body.IsReachable(body.MatchArms[0].BodyEntry));
    }

    [Fact]
    public void RemovedGuardScopeCannotResurrectCandidate()
    {
        var c = MinimalEmissionTest.Analyze("match 1\n    let n if n == 1 => ()\n    _ => ()");
        var match = c.Ownership.Bodies[0].Matches[0].Binding.Syntax;
        var oldGuard = match.Arms[0].Guard!;
        match.Arms[0].Guard = null;
        Assert.True(KotoHelper.Replace(match, match.Arms[0].Body, oldGuard));
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Binding.TryGetMatch(match, out var plan));
        Assert.Null(plan!.Positions[0].CandidateSymbol);
        Assert.Same(plan.Positions[0].BodySymbol, ((BinaryKoto)oldGuard).Left.BoundSymbol);
    }

    [Fact]
    public void CandidateCannotBeCapturedByLocalFunction()
    {
        var c = MinimalEmissionTest.Analyze("match 1\n    let n if (check: do\n        func read() -> i32 => n\n        exit to check: true\n    ) => ()\n    _ => ()");
        Assert.Contains(c.Binding.Issues, x => x.Code == Kimi.DiagnosticCode.InvalidCaptureBinding_Kd);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FalseGuardMoveReachesLaterCheckingState(bool covered)
    {
        var source = "let text = \"a\"\nmatch 1\n    _ if (check: do\n        _ = text@move\n        exit to check: false\n    ) => ()\n" + (covered ? "    _ => ()\n" : string.Empty) + "    _ => Console.writeLine(text)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.True(c.Ownership.Result.ErrorCount > 0);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.PossiblyMovedUse);
    }

    [Fact]
    public void OuterComparisonLoanSurvivesGuardCleanup()
    {
        const string Source = "let text = \"a\"\ntext == (match 1\n    _ if (check: do\n        defer => _ = text@move\n        exit to check: true\n    ) => \"a\"\n    _ => \"b\"\n)";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Bodies.SelectMany(x => x.Issues), x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
    }
}
