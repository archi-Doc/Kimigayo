// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class DeferredEmissionTest
{
    private const string Snapshot = "var x = 1\nlet y = label work: do\n    defer => x = 2\n    exit to work x\nif y == 1 and x == 2 => Console.writeLine(\"ok\")";

    private const string Formatting = "func show(flag: bool)\n    let value = 7\n    defer => Console.writeLine(\"\\(value)\")\n    if flag => return\nshow(true)\nshow(false)";

    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "DeferredOnly", "defer => Console.writeLine(\"end\")", "end\n" },
        { "DeferredFormattingReturn", Formatting, "7\n7\n" },
        { "DeferredFormattingLoop", "var value = 0\nwhile value < 3\n    defer => Console.writeLine(\"\\(value)\")\n    value += 1\n    if value < 3 => continue\n    exit", "1\n2\n3\n" },
        { "DeferredFormattingNested", "func show(flag: bool)\n    let value = 7\n    defer\n        defer => Console.writeLine(\"inner \\(value)\")\n        Console.writeLine(\"outer \\(value)\")\n        if flag => exit\n    if flag => return\nshow(true)\nshow(false)", "outer 7\ninner 7\nouter 7\ninner 7\n" },
        { "DeferredEmpty", "defer => ()\nConsole.writeLine(\"ok\")", "ok\n" },
        { "DeferredOrder", "defer => Console.writeLine(\"first\")\ndefer => Console.writeLine(\"second\")\nConsole.writeLine(\"body\")", "body\nsecond\nfirst\n" },
        { "DeferredSnapshot", Snapshot, "ok\n" },
        { "DeferredRead", "var x = 1\ndefer\n    if x == 2 => Console.writeLine(\"ok\")\nx = 2", "ok\n" },
        { "DeferredBranch", "if true => defer => Console.writeLine(\"branch\")\nif false => defer => Console.writeLine(\"bad\")\nConsole.writeLine(\"end\")", "branch\nend\n" },
        { "DeferredNested", "defer => Console.writeLine(\"last\")\ndefer\n    defer => Console.writeLine(\"inner\")\n    Console.writeLine(\"outer\")", "outer\ninner\nlast\n" },
        { "DeferredSelfExit", "defer => Console.writeLine(\"last\")\ndefer\n    defer => Console.writeLine(\"inner\")\n    if true => exit\n    Console.writeLine(\"bad\")", "inner\nlast\n" },
        { "DeferredInnerLoop", "defer\n    var x = 0\n    loop\n        x += 1\n        if x == 1 => continue\n        exit\n    if x == 2 => Console.writeLine(\"ok\")", "ok\n" },
        { "DeferredContinue", "var x = 0\nwhile x < 3\n    defer => Console.writeLine(\"iteration\")\n    x += 1\n    if x < 3 => continue\n    exit\nConsole.writeLine(\"end\")", "iteration\niteration\niteration\nend\n" },
        { "DeferredUnregistered", "loop\n    exit\n    defer => Console.writeLine(\"bad\")\nConsole.writeLine(\"ok\")", "ok\n" },
        { "DeferredYield", "var x = 1\nlet y = label choice: if true\n    defer => x = 2\n    loop => yield to choice x\nelse => 3\nif y == 1 and x == 2 => Console.writeLine(\"ok\")", "ok\n" },
        { "DeferredResultInside", "defer\n    var c = true\n    let x = if c => 1 + 2 else => 4\n    if x == 3 => Console.writeLine(\"ok\")", "ok\n" },
        { "DeferredOperandTransfer", "let x = label outer: do\n    defer => Console.writeLine(\"outer\")\n    let y = label work: do\n        defer => Console.writeLine(\"work\")\n        exit to work (if false => 1 else => exit to outer 2)\n    exit to outer y\nif x == 2 => Console.writeLine(\"ok\")", "work\nouter\nok\n" },
        { "DeferredUnitExit", "defer => Console.writeLine(\"end\")\ndefer => exit ()", "end\n" },
        { "DeferredMillion", "var x = 0\nwhile x < 1000000\n    defer => x += 1\n    continue\nif x == 1000000 => Console.writeLine(\"ok\")", "ok\n" },
        { "DeferredPartialDelivery", "var c = true\nlet x = if c => 1 else\n    defer => loop => ()\n    yield 2\nif x == 1 => Console.writeLine(\"ok\")", "ok\n" },
        { "DeferredDirective", "var x = 0\n#if true\n    defer\n        if x == 1 => Console.writeLine(\"ok\")\nx = 1", "ok\n" },
        { "DeferredSwitch", "var x = 0\nloop\n    #switch\n        #case true\n            defer => x = 1\n            exit\n        #case _\n            defer => x = 2\n    Console.writeLine(\"bad\")\nif x == 1 => Console.writeLine(\"ok\")", "ok\n" },
        { "DeferredSelectedLocal", "#if true\n    var x = 0\n    defer\n        if x == 1 => Console.writeLine(\"ok\")\nx = 1", "ok\n" },
        { "DeferredBooleanResult", "var c = true\nlet x = label work: do\n    defer => c = false\n    exit to work c\nif x and not c => Console.writeLine(\"ok\")", "ok\n" },
        { "DeferredMultiExit", "var x = 0\nlet y = label work: do\n    defer\n        defer => x += 10\n        if true => exit\n        if false => exit\n        x += 100\n    defer => x += 1\n    exit to work x\nif y == 0 and x == 11 => Console.writeLine(\"ok\")", "ok\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EmitsDeferredFixtures(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture(name, source, stdout);

    [Theory]
    [InlineData("DeferredOperandOverflow", "var x = 2147483647\nlet y = label work: do\n    defer => Console.writeLine(\"bad\")\n    exit to work x + 1\nConsole.writeLine(\"bad\")", "", 4, 18)]
    [InlineData("DeferredBodyOverflow", "var x = 2147483647\ndefer => Console.writeLine(\"bad\")\ndefer\n    Console.writeLine(\"begin\")\n    x += 1\n    Console.writeLine(\"bad\")", "begin\n", 5, 5)]
    public void OverflowStopsCleanupAndDelivery(string name, string source, string stdout, int line, int column)
        => ScalarEmissionTest.EmitFixture(name, source, stdout, 1, $"Hello.kimi:{line}:{column}: abort KIMI_E_INT_OVERFLOW: Integer overflow\n");

    // A deferred body that registers its own cleanup generates (its MIR: tests/mir/scopes.kimi `nestedDefer`).
    [Fact]
    public void CleanupSegmentsHaveUniqueMembershipAndDeferredEndpoints()
    {
        var c = MinimalEmissionTest.Analyze("var x = 1\ndefer\n    var y = 2\n    defer => y = 3\n    x = y\nvar z = 4");
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void WarmDeferredAnalysisAndWritingAllocateNothing(int scenario)
    {
        var c = MinimalEmissionTest.Analyze(scenario == 2 ? Formatting : scenario == 1 ? ExpansionSource(2) : Snapshot);
        var success = true;
        var allocated = AllocationMeasurement.Measure(() =>
        {
            success &= c.Analyze().IsVerified;
            success &= c.Generate(TextWriter.Null);
        });
        Assert.True(success);
        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData("value += 1")]
    [InlineData("let moved = value@move")]
    public void RepeatedFormattingStillRejectsConflictingOuterAccess(string mutation)
    {
        var c = MinimalEmissionTest.Analyze($$"""
            func show(flag: bool)
                var value = 7
                defer
                    let borrowed = value@ref
                    {{mutation}}
                    Console.writeLine("\(borrowed)")
                if flag => return
            show(true)
            show(false)
            """);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Problems, x => x.Code.IsLoanConflict());
        using var writer = new StringWriter();
        Assert.False(c.Generate(writer));
        Assert.Empty(writer.ToString());
    }

    [Theory]
    [InlineData("defer => " + MinimalEmissionTest.FloatExpression + "\ndefer => loop => ()")]
    [InlineData("defer => loop => ()\n" + MinimalEmissionTest.FloatExpression)]
    [InlineData("if false => defer => " + MinimalEmissionTest.FloatExpression)]
    public void FloatingCleanupIsCheckedEvenWhenUnreachable(string source, bool emitted = true)
    {
        var c = MinimalEmissionTest.Analyze(source);
        FloatEmissionTest.AssertEmissionSupport(c, emitted);
    }

    // A cleanup that never completes leaves the cleanups registered before it to the checks (their MIR: tests/mir/scopes.kimi
    // `divergentCleanup`).
    [Fact]
    public void DivergentCleanupSkipsRemainingBodiesAndRunsUntilKilled()
    {
        const string Source = "defer\n    Console.writeLine(\"bad\")\ndefer => loop => ()\nConsole.writeLine(\"begin\")";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        ScalarEmissionTest.EmitFixture("DeferredDivergent", Source, "begin\n", observationMilliseconds: 1000);
    }

    // The MIR body's blocks, measured per depth, bound the expansion of nested deferred bodies at each self exit and continue.
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(1, 72)]
    [InlineData(2, 168)]
    [InlineData(3, 360)]
    [InlineData(4, 744)]
    public void NestedSelfExitsAndContinuesHaveMeasuredBoundedExpansion(int depth, int blocks)
    {
        var c = MinimalEmissionTest.Analyze(ExpansionSource(depth));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
        Assert.Equal(blocks, c.BuildMir().Bodies.ToArray().Single(static x => x.Key.Decl is null && x.Source.Module == 0).Blocks.Length);
    }

    // An expansion past the old analysis's deferred-cleanup limit (its 8192 operations) has no
    // MIR counterpart: since the MIR decides acceptance (R6-check H5; 1a, the owner 2026-11-03, DECISIONS R5-M182's O-2), check accepts
    // it with no record and generates its module, on every analysis.
    [Fact]
    public void AnExpansionPastTheOldLimitIsGeneratedOnEveryAnalysis()
    {
        var c = MinimalEmissionTest.Analyze(ExpansionSource(8));
        for (var i = 0; i < 2; i++)
        {
            c.Analyze();
            Assert.True(c.Accepts, MinimalEmissionTest.Describe(c));
            Assert.Empty(c.Problems);
            using var writer = new StringWriter();
            Assert.True(c.Generate(writer), MinimalEmissionTest.Describe(c));
            Assert.NotEmpty(writer.ToString());
        }
    }

    // SPEC 14.11, 14.3 (PLAN G427 F6, F7): a require's failure body and an unsafe block, each indented or a single item, are Bodies no
    // target ends: the defers they register run where they are left, and a failure body has no successor (its fallthrough rows are
    // MirAnalysisTest's RequireEnds). The valid forms are accepted; a defer reading a local moved later in its body is one MovedPlace_Kd
    // at that read.
    [Theory]
    [InlineData("func f(flag: bool) -> i32\n    require flag else\n        defer => Console.writeLine(\"failed\")\n        return 0\n    return 1", true)]
    [InlineData("func f(flag: bool) -> i32\n    require flag else => return 0\n    return 1", true)]
    [InlineData("func f(flag: bool) -> i32\n    require flag else\n        let s = \"text\"\n        defer => Console.writeLine(s)\n        let moved = s@move\n        return 0\n    return 1", false)]
    [InlineData("func f() -> i32\n    var x = 1\n    unsafe\n        defer => x += 1\n        x += 1\n    return x", true)]
    [InlineData("func f() -> i32\n    var x = 1\n    unsafe => defer => x += 1\n    return x", true)]
    [InlineData("func f(c: bool) -> i32\n    var x = 1\n    match c\n        true => unsafe => defer => x += 1\n        false => ()\n    return x", true)]
    [InlineData("func f()\n    unsafe\n        let s = \"text\"\n        defer => Console.writeLine(s)\n        let moved = s@move\n    Console.writeLine(\"after\")", false)]
    public void BodiesNoTargetEndsCleanUpWhereTheyAreLeft(string source, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(source + "\nConsole.writeLine(\"ok\")");
        if (valid)
        {
            Assert.True(c.Accepts, MinimalEmissionTest.Describe(c));
            Assert.Empty(c.Problems);
            return;
        }

        var issue = Assert.Single(c.Problems);
        var at = source.IndexOf("writeLine(s)", StringComparison.Ordinal) + "writeLine(".Length;
        Assert.Equal((DiagnosticCode.MovedPlace_Kd, at, 1), (issue.Code, c.SyntaxOf(issue.Subject).Span.Start, c.SyntaxOf(issue.Subject).Span.Length));
    }

    [Fact]
    public void DeferredDiagnosticsAreDeduplicatedBeforePublication()
    {
        // The deferred body's bare move is Binding's TransferRequired_Kd (R6-check H1, DECISIONS BU-84): one record, however often the
        // body is emitted; it rejects the program (R6-check H5).
        var c = MinimalEmissionTest.Analyze("var flag = true\nloop\n    defer\n        let moved = (\"first\", \"last\").0\n    if flag => exit\n    continue");
        Assert.False(c.Accepts);
        c.Binding.ReportDiagnostics();
        Assert.Equal([nameof(DiagnosticCode.TransferRequired_Kd)], TestDiagnostics.Of(c).Select(static x => x.Code));
    }

    [Fact]
    public void SelectedDeclarationsShareTheirSurroundingScope()
    {
        var c = MinimalEmissionTest.Analyze("var x = 1\n#if true\n    var x = 2");
        Assert.False(c.Binding.Result.IsComplete);
    }

    private static string ExpansionSource(int depth)
    {
        var source = new StringBuilder("var c = true\nvar n = 0\nwhile n < 3\n");
        for (var i = 0; i < depth; i++)
        {
            source.Append(' ', (i + 1) * 4).Append("defer\n");
            source.Append(' ', (i + 2) * 4).Append("var local").Append(i).Append(" = 1\n");
        }

        for (var i = depth - 1; i >= 0; i--)
        {
            source.Append(' ', (i + 2) * 4).Append("if c => exit\n");
            source.Append(' ', (i + 2) * 4).Append("if c => exit\n");
            source.Append(' ', (i + 2) * 4).Append("()\n");
        }

        return source.Append("    n += 1\n    if n == 1 => continue\n    if n == 2 => continue\n    exit\nConsole.writeLine(\"ok\")").ToString();
    }
}
