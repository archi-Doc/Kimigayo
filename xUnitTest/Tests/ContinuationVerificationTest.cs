// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Text;
using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

[TestClass(DisableParallelization = true)]
public class ContinuationVerificationTest
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MixedMatchCheckingAddsNoRuntimeExecution(bool early)
    {
        var source = MixedSource + "\nf(" + (early ? "true" : "false") + ")\nConsole.writeLine(\"done\")";
        ScalarEmissionTest.EmitFixture(
            "VerificationContinuation" + Configuration + early,
            source,
            early ? "done\n" : string.Empty,
            early ? 0 : 1,
            early ? string.Empty : "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");
        var c = MinimalEmissionTest.Analyze(source);
        foreach (var body in c.Ownership.Bodies)
        {
            foreach (var region in body.CheckingRegions.Skip(1))
            {
                if (region.Entry >= 0)
                {
                    Assert.False(body.IsReachable(region.Entry));
                }
            }
        }
    }

    [Fact]
    public void PreparedTupleCopiesPreserveNamedAndReturnedArgumentSnapshots()
    {
        const string Source = "func pair(n?: i32) -> (i32, bool) => (n, true)\n" +
            "func choose(a?: (i32, bool), b?: (i32, bool), x?: i32 = (match a\n    var saved\n        saved.0 += b.0\n        yield saved.0\n), y?: i32 = (match (b, x)\n    ((let n, _), let value) if n > 0 => n + value\n    _ => x\n)) -> i32 => y\n" +
            "var value = (3, true)\nif choose(b: pair(4), a: value) != 11 or value.0 != 3 => $abort(\"snapshot\")\n" +
            "if choose(pair(2), pair(5), y: 19) != 19 => $abort(\"supplied\")\n" +
            "var i = 0\nvar sum = 0\nwhile i < 4\n    sum += choose(pair(i), pair(2))\n    i += 1\nif sum != 22 => $abort(\"repeated\")\nConsole.writeLine(\"ok\")";
        ScalarEmissionTest.EmitFixture("VerificationContinuation" + Configuration + "TupleCalls", Source, "ok\n");
    }

    [Fact]
    public void ScopedFalseGuardCleansTemporariesBeforeSubjectAcquisition()
    {
        const string Source = "func same(a?: ref/string, b?: ref/string) -> bool => a == b\n" +
            "match \"subject\"\n    let text if (guard: do\n        let local = \"guard\"\n        exit to guard: same(text, \"other\")\n    ) => ()\n    let text if same(text, \"subject\") => Console.writeLine(text)\n    _ => ()";
        var name = "VerificationContinuation" + Configuration + "GuardCleanup";
        var ir = ScalarEmissionTest.EmitFixture(name, Source, "subject\n");
        StringEmissionTest.WriteAuditedFixture(name, Source, ir, "subject\n", "subject=2;guard=1;other=1", order: [2, 1, 0, 0]);
    }

    [Theory]
    [InlineData("Literal", "(3, true)")]
    [InlineData("Local", "value")]
    [InlineData("Returned", "pair(3)")]
    [InlineData("Selected", "if true => value else => pair(3)")]
    [InlineData("SelectedFalse", "if false => value else => pair(3)")]
    public void TupleDefaultReadsEveryPreparedStorageSource(string name, string argument)
    {
        var source = "func pair(n?: i32) -> (i32, bool) => (n, true)\n" +
            "func read(a?: (i32, bool), b?: (i32, bool), result?: i32 = (match a\n    var saved\n        saved.0 += b.0\n        yield saved.0 + a.0\n)) -> i32 => result\n" +
            "func forward(value?: (i32, bool)) -> i32 => read(b: pair(4), a: value)\n" +
            "var value = (3, true)\nif read(b: pair(4), a: (" + argument + ")) != 10 or forward(value) != 10 or value.0 != 3 => $abort(\"prepared tuple\")";
        ScalarEmissionTest.EmitFixture("VerificationContinuation" + Configuration + name, source, string.Empty);
    }

    [Fact]
    public void PreparedSelectionCopyDoesNotRequireElementProjections()
    {
        const string Source = "func f(pair?: (i32, bool), result?: i32 = (match pair\n    (let n, _) => n\n)) -> i32 => result\n" +
            "if f(if true => (7, true) else => (2, false)) != 7 => $abort(\"selection copy\")";
        ScalarEmissionTest.EmitFixture("VerificationContinuation" + Configuration + "SelectionCopy", Source, string.Empty);
    }

    [Fact]
    public void ReloadAndWarmAnalysisPreserveGuardHistories()
    {
        var c = MinimalEmissionTest.Analyze(MixedSource + "\nf(true)");
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var bytes = TinyhandSerializer.Serialize(c.Kotonoha);
        c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        var syntax = c.Kotonoha;
        TinyhandSerializer.DeserializeObject(bytes, ref syntax);
        Assert.NotNull(syntax);
        syntax.OnDeserialized(c);
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        for (var i = 0; i < 8; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Ownership.Analyze().IsVerified || !c.Emission.WriteIr(TextWriter.Null, out _))
            {
                throw new InvalidOperationException("Reloaded guard verification failed.");
            }
        }));
    }

    [Theory]
    [InlineData("Array", "[2 of i32]", "[2, 3]", "value[1]", "")]
    [InlineData("OwnedTuple", "(i32, string)", "(3, \"payload\")", "value.0", "payload=1")]
    [InlineData("OwnedArray", "[2 of (i32, string)]", "[(2, \"first\"), (3, \"last\")]", "value[1].0", "first=1;last=1")]
    public void DefaultsInspectAcquiredAggregateArguments(string name, string type, string value, string read, string drops)
    {
        var source = "func inspect(value?: " + type + ", result?: i32 = " + read + ") -> i32 => result\n" +
            "func forward(value?: " + type + ") -> i32 => inspect(value)\n" +
            "let value: " + type + " = " + value + "\nif forward(value) != 3 => $abort(\"prepared aggregate\")";
        var fixture = "VerificationContinuation" + Configuration + "Acquired" + name;
        var ir = ScalarEmissionTest.EmitFixture(fixture, source, string.Empty);
        if (drops.Length != 0)
        {
            StringEmissionTest.WriteAuditedFixture(fixture, source, ir, string.Empty, drops);
        }
    }

    [Fact]
    public void ReplayCannotRestoreAbandonedGuardProtection()
    {
        var c = MinimalEmissionTest.Analyze(MixedSource + "\nf(true)");
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        var body = Assert.Single(c.Ownership.Bodies, x => x.CheckingSeeds.Any(s => s.Replay >= 0 && x.LoanStates[s.Operation] != x.LoanStates[x.CheckingReplays[s.Replay].End]));
        var seed = body.CheckingSeeds.First(s => s.Replay >= 0 && body.LoanStates[s.Operation] != body.LoanStates[body.CheckingReplays[s.Replay].End]);
        var replay = body.CheckingReplays[seed.Replay];
        foreach (var invalid in new[] { seed.Operation, -1, int.MaxValue })
        {
            body.CheckingReplays[seed.Replay] = replay with { End = invalid };
            Assert.False(body.ValidateComparisonLoans());
        }

        body.CheckingReplays[seed.Replay] = replay;
        Assert.True(body.ValidateComparisonLoans());
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }

    [Fact]
    public void RetainedHistoriesGrowWithGuardCountAndReuseTheirCapacity()
    {
        var results = new List<object>();
        var previous = 0;
        foreach (var count in new[] { 4, 16, 64 })
        {
            var arms = new StringBuilder();
            for (var i = 0; i < count; i++)
            {
                arms.Append("true if check(x = 2, c) => return\n                ");
            }

            var source = "func check(effect?: (), value?: bool) -> bool => value\nfunc stop() -> Never => $abort(\"stop\")\nfunc f(c?: bool)\n    var x = 1\n    do\n        loop\n            if c => return else => exit\n            match c\n                " + arms + "_ => x = 3\n            x = 4\n        stop()\n    let y = x\nf(true)";
            var c = MinimalEmissionTest.Analyze(source);
            Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
            var retained = c.Ownership.Bodies.Sum(x => x.CheckingSeeds.Count + x.CheckingReplays.Count + x.CheckingRegions.Count);
            if (previous != 0)
            {
                Assert.InRange(retained, previous, previous * 5);
            }

            previous = retained;
            for (var i = 0; i < 8; i++)
            {
                Assert.True(c.Ownership.Analyze().IsVerified);
            }

            var allocated = AllocationMeasurement.Measure(() => Assert.True(c.Ownership.Analyze().IsVerified));
            Assert.Equal(0, allocated);
            Assert.Equal(retained, c.Ownership.Bodies.Sum(x => x.CheckingSeeds.Count + x.CheckingReplays.Count + x.CheckingRegions.Count));
            var start = Stopwatch.GetTimestamp();
            for (var i = 0; i < 64; i++)
            {
                Assert.True(c.Ownership.Analyze().IsVerified);
            }

            results.Add(new { Guards = count, RetainedRecords = retained, AllocatedBytes = allocated, AnalysisIterations = 64, ElapsedSeconds = Stopwatch.GetElapsedTime(start).TotalSeconds });
        }

        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../bin/continuation-verification"));
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, Configuration + "-growth.json"), System.Text.Json.JsonSerializer.Serialize(results));
    }

    private const string MixedSource = "func stop() -> Never => $abort(\"stop\")\nfunc same(a?: ref/string, b?: ref/string) -> bool => a == b\nfunc f(c?: bool)\n    var x = 1\n    do\n        loop\n            if c => return else => exit\n            choice: match \"subject\"\n                let text if (guard: do\n                    x += 1\n                    exit to guard: same(text, \"subject\")\n                )\n                    Console.writeLine(text)\n                    yield to choice\n                _ => return\n            match (c, x)\n                (true, var n) if n > 0\n                    n += 1\n                    return\n                (_, let n) => x = n\n            x = 3\n        stop()\n    let result = x";

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
