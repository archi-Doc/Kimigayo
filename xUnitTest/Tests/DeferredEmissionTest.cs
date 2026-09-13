// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class DeferredEmissionTest
{
    private const string Snapshot = "var x = 1\nlet y = work: do\n    defer => x = 2\n    exit to work: x\nif y == 1 and x == 2 => writeLine(\"ok\")";

    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "DeferredOnly", "defer => writeLine(\"end\")", "end\n" },
        { "DeferredEmpty", "defer => ()\nwriteLine(\"ok\")", "ok\n" },
        { "DeferredOrder", "defer => writeLine(\"first\")\ndefer => writeLine(\"second\")\nwriteLine(\"body\")", "body\nsecond\nfirst\n" },
        { "DeferredSnapshot", Snapshot, "ok\n" },
        { "DeferredRead", "var x = 1\ndefer\n    if x == 2 => writeLine(\"ok\")\nx = 2", "ok\n" },
        { "DeferredBranch", "if true => defer => writeLine(\"branch\")\nif false => defer => writeLine(\"bad\")\nwriteLine(\"end\")", "branch\nend\n" },
        { "DeferredNested", "defer => writeLine(\"last\")\ndefer\n    defer => writeLine(\"inner\")\n    writeLine(\"outer\")", "outer\ninner\nlast\n" },
        { "DeferredSelfExit", "defer => writeLine(\"last\")\ndefer\n    defer => writeLine(\"inner\")\n    if true => exit\n    writeLine(\"bad\")", "inner\nlast\n" },
        { "DeferredInnerLoop", "defer\n    var x = 0\n    loop\n        x += 1\n        if x == 1 => continue\n        exit\n    if x == 2 => writeLine(\"ok\")", "ok\n" },
        { "DeferredContinue", "var x = 0\nwhile x < 3\n    defer => writeLine(\"iteration\")\n    x += 1\n    if x < 3 => continue\n    exit\nwriteLine(\"end\")", "iteration\niteration\niteration\nend\n" },
        { "DeferredUnregistered", "loop\n    exit\n    defer => writeLine(\"bad\")\nwriteLine(\"ok\")", "ok\n" },
        { "DeferredYield", "var x = 1\nlet y = choice: if true\n    defer => x = 2\n    loop => yield to choice: x\nelse => 3\nif y == 1 and x == 2 => writeLine(\"ok\")", "ok\n" },
        { "DeferredResultInside", "defer\n    var c = true\n    let x = if c => 1 + 2 else => 4\n    if x == 3 => writeLine(\"ok\")", "ok\n" },
        { "DeferredOperandTransfer", "let x = outer: do\n    defer => writeLine(\"outer\")\n    let y = work: do\n        defer => writeLine(\"work\")\n        exit to work: (if false => 1 else => exit to outer: 2)\n    exit to outer: y\nif x == 2 => writeLine(\"ok\")", "work\nouter\nok\n" },
        { "DeferredUnitExit", "defer => writeLine(\"end\")\ndefer => exit ()", "end\n" },
        { "DeferredMillion", "var x = 0\nwhile x < 1000000\n    defer => x += 1\n    continue\nif x == 1000000 => writeLine(\"ok\")", "ok\n" },
        { "DeferredPartialDelivery", "var c = true\nlet x = if c => 1 else\n    defer => loop => ()\n    yield 2\nif x == 1 => writeLine(\"ok\")", "ok\n" },
        { "DeferredDirective", "var x = 0\n#if true\n    defer\n        if x == 1 => writeLine(\"ok\")\nx = 1", "ok\n" },
        { "DeferredSwitch", "var x = 0\nloop\n    #switch\n        #case true\n            defer => x = 1\n            exit\n        #case _\n            defer => x = 2\n    writeLine(\"bad\")\nif x == 1 => writeLine(\"ok\")", "ok\n" },
        { "DeferredSelectedLocal", "#if true\n    var x = 0\n    defer\n        if x == 1 => writeLine(\"ok\")\nx = 1", "ok\n" },
        { "DeferredBooleanResult", "var c = true\nlet x = work: do\n    defer => c = false\n    exit to work: c\nif x and not c => writeLine(\"ok\")", "ok\n" },
        { "DeferredMultiExit", "var x = 0\nlet y = work: do\n    defer\n        defer => x += 10\n        if true => exit\n        if false => exit\n        x += 100\n    defer => x += 1\n    exit to work: x\nif y == 0 and x == 11 => writeLine(\"ok\")", "ok\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EmitsDeferredFixtures(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture(name, source, stdout);

    [Theory]
    [InlineData("DeferredOperandOverflow", "var x = 2147483647\nlet y = work: do\n    defer => writeLine(\"bad\")\n    exit to work: x + 1\nwriteLine(\"bad\")", "", 4, 19)]
    [InlineData("DeferredBodyOverflow", "var x = 2147483647\ndefer => writeLine(\"bad\")\ndefer\n    writeLine(\"begin\")\n    x += 1\n    writeLine(\"bad\")", "begin\n", 5, 5)]
    public void OverflowStopsCleanupAndDelivery(string name, string source, string stdout, int line, int column)
        => ScalarEmissionTest.EmitFixture(name, source, stdout, 1, $"Hello.kimi:{line}:{column}: abort KIMI_E_INT_OVERFLOW: Integer overflow\n");

    [Fact]
    public void CleanupSegmentsHaveUniqueMembershipAndDeferredEndpoints()
    {
        var c = MinimalEmissionTest.Analyze("var x = 1\ndefer\n    var y = 2\n    defer => y = 3\n    x = y\nvar z = 4");
        Assert.True(c.Emission.Validate(out var error), MinimalEmissionTest.Describe(c, error));
        var body = c.Ownership.Bodies[0];
        var seen = new HashSet<int>();
        foreach (var plan in body.CleanupPlans)
        {
            var first = body.CleanupSteps[plan.Start].Operation;
            Assert.Equal(first, body.Edges[plan.Edge].To);
            for (var i = plan.Start; i < plan.Start + plan.Count; i++)
            {
                Assert.True(seen.Add(i));
            }
        }

        Assert.Equal(body.Operations.Count(x => x.Kind == OwnershipOperationKind.Cleanup), seen.Count);
        Assert.Equal(2, body.DeferredPlans.Count);
        Assert.All(body.DeferredPlans, plan => Assert.Equal(plan.Entry, body.Edges[plan.Edge].To));
    }

    [Fact]
    public void RepeatedExpansionReusesLocalStorage()
    {
        var c = MinimalEmissionTest.Analyze("var c = true\nloop\n    defer\n        var x = 1\n        x += 1\n        if x == 2 => writeLine(\"ok\")\n    if c => exit\n    continue");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        var body = c.Ownership.Bodies[0];
        Assert.Equal(2, body.Places.Count(x => x.Kind == OwnershipPlaceKind.Local));
        Assert.Equal(2, module.GetFunction(0).Slots.Count(x => body.Places[x.Place].Kind == OwnershipPlaceKind.Local));
        Assert.True(body.DeferredPlans.Count >= 2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmDeferredAnalysisAndWritingAllocateNothing(bool nested)
    {
        var c = MinimalEmissionTest.Analyze(nested ? ExpansionSource(2) : Snapshot);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var success = true;
        for (var i = 0; i < 128; i++)
        {
            success &= c.Ownership.Analyze().IsVerified;
            success &= c.Emission.WriteIr(TextWriter.Null, out _);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(success);
    }

    [Theory]
    [InlineData("defer => 1 / 1\ndefer => loop => ()")]
    [InlineData("defer => loop => ()\nlet x = 1 / 1")]
    [InlineData("if false => defer => 1 / 1")]
    public void UnsupportedCleanupNeverWritesIr(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void DivergentCleanupHasNoRuntimeDeliveryOrPhi()
    {
        var c = MinimalEmissionTest.Analyze("let x = work: do\n    defer => loop => ()\n    exit to work: 1\nwriteLine(\"bad\")");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        var body = c.Ownership.Bodies[0];
        Assert.Contains(body.DeferredPlans, plan => body.IsReachable(plan.Entry) && !body.IsReachable(plan.Continuation));
        Assert.DoesNotContain(module.GetFunction(0).Instructions, x => x.Opcode is EmissionOpcode.Phi or EmissionOpcode.ReturnVoid);
        using var writer = new StringWriter();
        module.WriteIr(writer);
        Assert.DoesNotContain("mustprogress", writer.ToString());
        Assert.DoesNotContain("willreturn", writer.ToString());
    }

    [Fact]
    public void DivergentCleanupChecksRemainingBodiesAndRunsUntilKilled()
    {
        const string Source = "var x = 1\ndefer\n    x = 2\n    writeLine(\"bad\")\ndefer => loop => ()\nwriteLine(\"begin\")";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var body = c.Ownership.Bodies[0];
        var write = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Write && x.Source is BinaryKoto);
        Assert.False(body.IsReachable(write));
        Assert.True(body.HasCheckingState(write));
        ScalarEmissionTest.EmitFixture("DeferredDivergent", Source, "begin\n", timeoutMilliseconds: 1000);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("entry")]
    [InlineData("continuation")]
    [InlineData("completion")]
    [InlineData("duplicate")]
    [InlineData("segment")]
    [InlineData("escape")]
    public void MalformedCleanupPlansFailBeforeWriting(string mutation)
    {
        var c = MinimalEmissionTest.Analyze(Snapshot);
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies[0];
        var plan = body.DeferredPlans[0];
        switch (mutation)
        {
            case "missing": body.DeferredPlanStorage.Clear(); break;
            case "entry": body.DeferredPlanStorage[0] = plan with { Edge = 0 }; break;
            case "continuation": body.DeferredPlanStorage[0] = plan with { Continuation = plan.Entry }; break;
            case "completion": body.DeferredPlanStorage[0] = plan with { CanComplete = false }; break;
            case "duplicate": body.DeferredPlanStorage.Add(plan); break;
            case "segment": body.CleanupPlanStorage.Add(body.CleanupPlans[0]); break;
            case "escape":
                var edge = body.EdgeHeads[plan.Entry];
                body.EdgeStorage[edge] = body.Edges[edge] with { To = 0 };
                break;
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out error));
        Assert.NotNull(error);
        Assert.Empty(writer.ToString());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void NestedSelfExitsAndContinuesHaveMeasuredBoundedExpansion(int depth)
    {
        var c = MinimalEmissionTest.Analyze(ExpansionSource(depth));
        var body = c.Ownership.Bodies[0];
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../bin/deferred-growth"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"depth-{depth}.txt"), $"depth={depth}; operations={body.Operations.Count}; places={body.Places.Count}; defers={body.DeferredPlans.Count}; locals={body.Places.Count(x => x.Kind == OwnershipPlaceKind.Local)}");
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.Validate(out var error), error);
        Assert.InRange(body.Operations.Count, 1, OwnershipAnalysis.DeferredOperationLimit);
        Assert.Equal(depth + 2, body.Places.Count(x => x.Kind == OwnershipPlaceKind.Local));
    }

    [Fact]
    public void ExcessiveExpansionReportsAnExplicitLimitAndCanReanalyze()
    {
        var c = MinimalEmissionTest.Analyze(ExpansionSource(8));
        for (var i = 0; i < 2; i++)
        {
            Assert.False(c.Ownership.Analyze().IsVerified);
            Assert.Single(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ExpansionLimit);
            Assert.Equal(OwnershipAnalysis.DeferredOperationLimit, c.Ownership.Bodies[0].Operations.Count);
            using var writer = new StringWriter();
            Assert.False(c.Emission.WriteIr(writer, out _));
            Assert.Empty(writer.ToString());
        }

        c.Ownership.ReportDiagnostics();
        var issue = Assert.Single(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ExpansionLimit);
        Assert.Contains(issue.Source.DiagnosticCollection!.GetArray(), x => x.Message.Contains("8192", StringComparison.Ordinal));
    }

    [Fact]
    public void DeferredDiagnosticsAreDeduplicatedBeforePublication()
    {
        var c = MinimalEmissionTest.Analyze("var flag = true\nloop\n    defer => \"a\" + \"b\"\n    if flag => exit\n    continue");
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.Equal(c.Ownership.Issues.Count, c.Ownership.Issues.Select(x => (x.Source, x.Failure)).Distinct().Count());
        Assert.NotEmpty(c.Ownership.Issues);
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

        return source.Append("    n += 1\n    if n == 1 => continue\n    if n == 2 => continue\n    exit\nwriteLine(\"ok\")").ToString();
    }
}
