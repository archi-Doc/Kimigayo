// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

[TestClass(DisableParallelization = true)]
public class ConditionalContinuationTest
{
    [Theory]
    [InlineData("var x: i32", "x = 1\n        return", "return", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "return", "x = 1\n        return", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let x = \"s\"", "Console.writeLine(x)\n        return", "return", "Console.writeLine(x)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x = \"s\"", "return", "Console.writeLine(x)\n        return", "Console.writeLine(x)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x: i32", "x = 1\n        return", "return", "x = 2", OwnershipFailure.ReassignedLet)]
    [InlineData("let x: i32", "return", "x = 1\n        return", "x = 2", OwnershipFailure.ReassignedLet)]
    public void AllTerminalPathsContributeTheirState(string declaration, string yes, string no, string tail, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Source(declaration, yes, no, tail));
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("Initialized", "let x = 1", "return", "return", "let y = x")]
    [InlineData("BothWrite", "var x: i32", "x = 1\n        return", "x = 2\n        return", "let y = x")]
    [InlineData("CheckingWrite", "var x: i32", "return\n        x = 1", "return\n        x = 2", "let y = x")]
    [InlineData("Reinitialize", "var x = \"s\"", "Console.writeLine(x)\n        return", "return", "x = \"new\"\n    Console.writeLine(x)")]
    public void JoinedContinuationsNeverExecute(string name, string declaration, string yes, string no, string tail)
        => ScalarEmissionTest.EmitFixture(
            "NeverConditional" + Configuration + name,
            Source(declaration, yes, no, tail) + "\nf(true)\nf(false)\nConsole.writeLine(\"done\")",
            name == "Reinitialize" ? "s\ndone\n" : "done\n");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JoinedStateIsSeparateAndRecomputedAfterReload(bool reload)
    {
        var c = MinimalEmissionTest.Analyze(Source("var x: i32", "x = 1\n        return", "x = 2\n        return", "x = 3\n    let y = x") + "\nf(true)");
        if (reload)
        {
            var bytes = TinyhandSerializer.Serialize(c.Kotonoha);
            c = Compilation.CreateForTest();
            Assert.True(c.Prepare(WindowsProfile.Target));
            var kotonoha = c.Kotonoha;
            TinyhandSerializer.DeserializeObject(bytes, ref kotonoha);
            Assert.NotNull(kotonoha);
            kotonoha.OnDeserialized(c);
            Assert.True(c.Bind().IsComplete);
            Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
            c.Ownership.Analyze();
        }

        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var body = Assert.Single(c.Ownership.Bodies, b => b.Function.Name == "f");
        var local = body.Places.First(p => p.Kind == OwnershipPlaceKind.Local);
        var write = Enumerable.Range(0, body.Operations.Count).Last(i => body.Operations[i] is { Kind: OwnershipOperationKind.Write, Source: BinaryKoto } op && op.Place == local.Id);
        Assert.False(body.IsReachable(write));
        Assert.True(body.HasCheckingState(write));
        Assert.Equal(PlaceState.None, body.GetInputState(write, local.Id));
        Assert.True(body.GetCheckingInputState(write, local.Id).HasFlag(PlaceState.MustInit));
        Assert.Equal(PlacementKind.None, body.Operations[write].Placement);
        c.Bind();
        Assert.False(body.HasCheckingState(write));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out _));
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Ownership.Analyze();
            c.Emission.WriteIr(TextWriter.Null, out _);
        }));
    }

    [Theory]
    [InlineData("func f(c: bool, d: bool)\n    let x = \"s\"\n    if c\n        if d\n            Console.writeLine(x)\n            return\n        else => return\n    else => return\n    Console.writeLine(x)")]
    [InlineData("func f(c: bool)\n    let x = \"s\"\n    return\n    if c\n        Console.writeLine(x)\n        return\n    else => return\n    Console.writeLine(x)")]
    public void NestedAndAlreadyUnreachableJoinsRetainMoves(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.PossiblyMovedUse);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Fact]
    public void UnavailableBranchStateCannotBeDroppedFromTheJoin()
    {
        var c = MinimalEmissionTest.Analyze("func f(c: bool, x: i32)\n    var n = 0\n    if c\n        loop\n            n += 1\n    else => return\n    let y = x");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Fact]
    public void NestedTerminalBranchesHaveNoRuntimeContinuation()
        => ScalarEmissionTest.EmitFixture(
            "NeverConditional" + Configuration + "Nested",
            "func f(c: bool, d: bool)\n    var x: i32\n    if c\n        if d\n            x = 1\n            return\n        else\n            x = 2\n            return\n    else\n        x = 3\n        return\n    let y = x\n    Console.writeLine(\"bad\")\nf(true, true)\nf(true, false)\nf(false, false)\nConsole.writeLine(\"done\")",
            "done\n");

    [Fact]
    public void CommonOuterBorrowSurvivesTheJoinUntilCallAcquisitionEnds()
        => ScalarEmissionTest.EmitFixture(
            "NeverConditional" + Configuration + "Borrow",
            "func stop() -> Never => $abort(\"stop\")\nfunc inspect(s: ref/string, ready: bool) => ()\nfunc f(c: bool)\n    var s = \"s\"\n    inspect(s, if c => stop() else => stop())\n    s = \"new\"\n    Console.writeLine(s)\nf(true)",
            string.Empty,
            1,
            "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Fact]
    public void UnequalActiveLoansRetainTheJoinGuard()
    {
        var c = MinimalEmissionTest.Analyze("func stop() -> Never => $abort(\"stop\")\nfunc inspect(s: ref/string, ready: bool) => ()\nfunc f(c: bool)\n    var s = \"s\"\n    inspect(s, if c => return else => stop())\n    s = \"new\"\n    Console.writeLine(s)\nf(true)");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    private static string Source(string declaration, string yes, string no, string tail)
        => "func f(c: bool)\n    " + declaration + "\n    if c\n        " + yes + "\n    else\n        " + no + "\n    " + tail;

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
