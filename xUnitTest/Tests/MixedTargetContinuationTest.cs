// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

[TestClass(DisableParallelization = true)]
public class MixedTargetContinuationTest
{
    private const string Stop = "func stop() -> Never => $abort(\"stop\")\n";

    [Theory]
    [InlineData("LoopReturn", "loop\n            if c\n                x = 1\n                return\n            else => exit", true)]
    [InlineData("LoopExit", "loop\n            if c\n                x = 1\n                return\n            else => exit", false)]
    [InlineData("Reversed", "loop\n            if c => exit\n            else\n                x = 1\n                return", false)]
    [InlineData("While", "while c\n            if c\n                x = 1\n                return\n            else => exit", true)]
    [InlineData("NestedSelection", "loop\n            if c\n                if c\n                    x = 1\n                    return\n                else => exit\n            else => exit", true)]
    [InlineData("NestedLoop", "loop\n            loop\n                if c\n                    x = 1\n                    return\n                else => exit\n            exit", true)]
    [InlineData("Scope", "inner: do\n            if c\n                x = 1\n                return\n            else => exit to inner", true)]
    [InlineData("Yield", "choice: if c\n            if c\n                x = 1\n                return\n            else => yield to choice\n        else => ()", true)]
    public void MixedTargetsReachTheirOwnExtents(string name, string body, bool condition)
        => ScalarEmissionTest.EmitFixture(
            "NeverMixedTarget" + Configuration + name,
            Source("var x: i32", body, "x = 2", "let y = x\n    Console.writeLine(\"bad\")", condition) + "\nConsole.writeLine(\"done\")",
            name == "LoopExit" ? string.Empty : "done\n",
            name == "LoopExit" ? 1 : 0,
            name == "LoopExit" ? "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n" : string.Empty);

    [Theory]
    [InlineData("var x: i32", "return", "x = 2", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "x = 1\n                return", "()", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let x = \"s\"", "_ = x@move\n                return", "()", "Console.writeLine(x)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x = \"s\"", "return", "_ = x@move", "Console.writeLine(x)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x: i32", "x = 1\n                return", "()", "x = 2", OwnershipFailure.ReassignedLet)]
    [InlineData("let x: i32", "return", "x = 1", "x = 2", OwnershipFailure.ReassignedLet)]
    public void EscapingFactsAreNotReplacedByTheMergedState(string declaration, string early, string tail, string use, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Source(declaration, "loop\n            if c\n                " + early + "\n            else => exit", tail, use));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Fact]
    public void LocalDeadSourceStillUsesTheJoinedState()
    {
        var c = MinimalEmissionTest.Analyze(Source("var x: i32", "loop\n            if c\n                x = 1\n                return\n            else => exit\n            let bad = x", "x = 2", "()"));
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.UninitializedUse);
    }

    [Theory]
    [InlineData("Return", "return", true)]
    [InlineData("ReturnExit", "return", false)]
    [InlineData("Exit", "exit", true)]
    [InlineData("ExitExit", "exit", false)]
    [InlineData("Continue", "continue", true)]
    [InlineData("ContinueExit", "continue", false)]
    [InlineData("Chain", "return\n            exit\n            continue", true)]
    [InlineData("ChainExit", "return\n            exit\n            continue", false)]
    public void BareTransfersKeepOriginalTargets(string name, string dead, bool condition)
        => ScalarEmissionTest.EmitFixture(
            "NeverMixedBare" + Configuration + name,
            Source("var x: i32", "loop\n            if c\n                x = 1\n                return\n            else => exit\n            " + dead, "x = 2", "let y = x", condition) + "\nConsole.writeLine(\"done\")",
            condition ? "done\n" : string.Empty,
            condition ? 0 : 1,
            condition ? string.Empty : "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Theory]
    [InlineData("var x: i32", "return", "x = 2", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let x = \"s\"", "_ = x@move\n                return", "()", "Console.writeLine(x)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x: i32", "x = 1\n                return", "()", "x = 2", OwnershipFailure.ReassignedLet)]
    public void BareTransfersDoNotEraseEarlierHistory(string declaration, string early, string tail, string use, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Source(declaration, "loop\n            if c\n                " + early + "\n            else => exit\n            return", tail, use));
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData("Assignment", "var x: i32", "x = 3", "x = 2", "let y = x", true)]
    [InlineData("AssignmentExit", "var x: i32", "x = 3", "x = 2", "let y = x", false)]
    [InlineData("Arithmetic", "var x: i32", "x = 3\n            x += 1", "x = 2", "let y = x", true)]
    [InlineData("Return", "var x: i32", "x = 3\n            return", "x = 2", "let y = x", true)]
    [InlineData("ReturnExit", "var x: i32", "x = 3\n            return", "x = 2", "let y = x", false)]
    [InlineData("Chain", "var x: i32", "x = 3\n            return\n            x = 4\n            exit\n            x = 5\n            continue", "x = 2", "let y = x", true)]
    [InlineData("ChainExit", "var x: i32", "x = 3\n            return\n            x = 4\n            exit\n            x = 5\n            continue", "x = 2", "let y = x", false)]
    [InlineData("Local", "var x: i32", "let n = 3\n            x = n", "x = 2", "let y = x", true)]
    [InlineData("Abort", "var x: i32", "x = 3\n            stop()", "x = 2", "let y = x", true)]
    [InlineData("AbortExit", "var x: i32", "x = 3\n            stop()", "x = 2", "let y = x", false)]
    [InlineData("Divergence", "var x: i32", "x = 3\n            loop => continue", "x = 2", "let y = x", true)]
    [InlineData("DivergenceExit", "var x: i32", "x = 3\n            loop => continue", "x = 2", "let y = x", false)]
    [InlineData("Move", "var x = \"old\"", "_ = x@move\n            x = \"new\"", "()", "Console.writeLine(x)", true)]
    [InlineData("MoveExit", "var x = \"old\"", "_ = x@move\n            x = \"new\"", "()", "Console.writeLine(x)", false)]
    [InlineData("Borrow", "var x = \"old\"", "inspect(x)\n            _ = x@move\n            x = \"new\"", "()", "Console.writeLine(x)", true)]
    [InlineData("BorrowExit", "var x = \"old\"", "inspect(x)\n            _ = x@move\n            x = \"new\"", "()", "Console.writeLine(x)", false)]
    public void LinearEffectsRetainSeparateTargets(string name, string declaration, string dead, string tail, string use, bool condition)
        => ScalarEmissionTest.EmitFixture(
            "NeverMixedEffect" + Configuration + name,
            Source(declaration, "loop\n            if c => return\n            else => exit\n            " + dead, tail, use, condition) + "\nConsole.writeLine(\"done\")\nfunc inspect(x: ref/string) => ()",
            condition ? "done\n" : string.Empty,
            condition ? 0 : 1,
            condition ? string.Empty : "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Theory]
    [InlineData("var x: i32", "return", "let n = 3", "x = 2", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "return", "x = 3", "()", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let x = \"s\"", "return", "_ = x@move", "()", "Console.writeLine(x)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x = \"s\"", "_ = x@move\n                return", "let n = 3", "()", "Console.writeLine(x)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x: i32", "return", "x = 3", "()", "x = 2", OwnershipFailure.ReassignedLet)]
    [InlineData("let x: i32", "return", "x = 3\n            return\n            exit", "()", "x = 2", OwnershipFailure.ReassignedLet)]
    [InlineData("let x: i32", "x = 1\n                return", "x = 3", "()", "()", OwnershipFailure.ReassignedLet)]
    [InlineData("var x: i32", "x = 1\n                return", "let n = x\n            x = 3", "x = 2", "let y = x", OwnershipFailure.UninitializedUse)]
    public void LinearEffectsPreserveHistoryAndLocalJoinChecks(string declaration, string early, string dead, string tail, string use, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Source(declaration, "loop\n            if c\n                " + early + "\n            else => exit\n            " + dead, tail, use));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("Reversed", "loop\n            if c => exit\n            else => return\n            x = 3", false)]
    [InlineData("Nested", "loop\n            loop\n                if c => return\n                else => exit\n                x = 3\n            exit", true)]
    [InlineData("Scope", "inner: do\n            if c => return\n            else => exit to inner\n            x = 3", true)]
    [InlineData("Yield", "choice: if c\n            if c => return\n            else => yield to choice\n            x = 3\n        else => ()", true)]
    public void EffectsSurviveEnclosingTargetFiltering(string name, string body, bool condition)
        => ScalarEmissionTest.EmitFixture(
            "NeverMixedEffect" + Configuration + name,
            Source("var x: i32", body, "x = 2", "let y = x", condition) + "\nConsole.writeLine(\"done\")",
            "done\n");

    [Fact]
    public void CheckingReplayDoesNotPublishRuntimeWritesOrCleanup()
    {
        var c = MinimalEmissionTest.Analyze(Source("var x: i32", "loop\n            if c => return\n            else => exit\n            x = 3\n            return", "x = 2", "let y = x"));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var body = Assert.Single(c.Ownership.Bodies, x => x.Function.Name == "f");
        var writes = Enumerable.Range(0, body.Operations.Count).Where(i => body.Operations[i] is { Kind: OwnershipOperationKind.Write, Source: BinaryKoto }).ToArray();
        var dead = Assert.Single(writes, i => !body.IsReachable(i));
        Assert.True(body.HasCheckingState(dead));
        Assert.Equal(PlaceState.None, body.GetInputState(dead, body.Operations[dead].Place));
        Assert.Equal(PlacementKind.None, body.Operations[dead].Placement);
        Assert.DoesNotContain(body.CleanupSteps, x => x.Operation == dead);
        var live = Assert.Single(writes, body.IsReachable);
        Assert.False(body.GetInputState(live, body.Operations[live].Place).HasFlag(PlaceState.MayInit));
        c.Bind();
        Assert.False(body.HasCheckingState(dead));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void ReplayedCallsStillRejectConflictingArgumentLoans()
    {
        var c = MinimalEmissionTest.Analyze(Source("let x = \"s\"", "loop\n            if c => return\n            else => exit\n            inspect(x, x@move)", "()", "()") + "\nfunc inspect(a: ref/string, b: string) => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("counter.value = 9")]
    [InlineData("counter.value = 9\n            return\n            exit")]
    [InlineData("let moved = counter")]
    public void StoredLoansRemainLiveAcrossReplayAndOuterJoin(string dead)
    {
        var source = Source("var counter = Counter.init()\n    let r = counter@ref", "loop\n            if c => return\n            else => exit\n            " + dead, "()", "let n = r.value") + "\nstruct Counter\n    public var value: i32 = 0";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LastStoredLoanUseBeforeReplayAllowsMutation(bool condition)
        => ScalarEmissionTest.EmitFixture(
            "NeverMixedLoan" + Configuration + condition,
            Source("var counter = Counter.init()\n    let r = counter@ref\n    let n = r.value", "loop\n            if c => return\n            else => exit\n            counter.value = 9", "()", "()", condition) + "\nConsole.writeLine(\"done\")\nstruct Counter\n    public var value: i32 = 0",
            condition ? "done\n" : string.Empty,
            condition ? 0 : 1,
            condition ? string.Empty : "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Theory]
    [InlineData("return\n            exit")]
    [InlineData("x = 3\n            return\n            x = 4\n            exit")]
    public void ReloadedAndReusedMixedTargetsAllocateNothing(string dead)
        => CheckReloadAndReuse(Source("var x: i32", "loop\n            if c\n                x = 1\n                return\n            else => exit\n            " + dead, "x = 2", "let y = x"));

    [Fact]
    public void StoredLoanReplayReloadAndReuseAllocateNothing()
        => CheckReloadAndReuse(Source("var counter = Counter.init()\n    let r = counter@ref", "loop\n            if c => return\n            else => exit\n            let n = counter.value", "()", "let n = r.value") + "\nstruct Counter\n    public var value: i32 = 0");

    private static void CheckReloadAndReuse(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var bytes = TinyhandSerializer.Serialize(c.Kotonoha);
        c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        var kotonoha = c.Kotonoha;
        TinyhandSerializer.DeserializeObject(bytes, ref kotonoha);
        Assert.NotNull(kotonoha);
        kotonoha.OnDeserialized(c);
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out _));
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Ownership.Analyze();
            c.Emission.WriteIr(TextWriter.Null, out _);
        }));
    }

    private static string Source(string declaration, string body, string tail, string use, bool condition = true)
        => Stop + "func f(c: bool)\n    " + declaration + "\n    do\n        " + body + "\n        " + tail + "\n        stop()\n    " + use + "\nf(" + (condition ? "true" : "false") + ")";

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
