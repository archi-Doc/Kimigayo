// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
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
            Source("var x: i32", body, "x = 2", "let y = x\n    writeLine(\"bad\")", condition) + "\nwriteLine(\"done\")",
            name == "LoopExit" ? string.Empty : "done\n",
            name == "LoopExit" ? 1 : 0,
            name == "LoopExit" ? "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n" : string.Empty);

    [Theory]
    [InlineData("var x: i32", "return", "x = 2", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "x = 1\n                return", "()", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let x = \"s\"", "writeLine(x)\n                return", "()", "writeLine(x)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x = \"s\"", "return", "writeLine(x)", "writeLine(x)", OwnershipFailure.PossiblyMovedUse)]
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
            Source("var x: i32", "loop\n            if c\n                x = 1\n                return\n            else => exit\n            " + dead, "x = 2", "let y = x", condition) + "\nwriteLine(\"done\")",
            condition ? "done\n" : string.Empty,
            condition ? 0 : 1,
            condition ? string.Empty : "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Theory]
    [InlineData("var x: i32", "return", "x = 2", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let x = \"s\"", "writeLine(x)\n                return", "()", "writeLine(x)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x: i32", "x = 1\n                return", "()", "x = 2", OwnershipFailure.ReassignedLet)]
    public void BareTransfersDoNotEraseEarlierHistory(string declaration, string early, string tail, string use, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Source(declaration, "loop\n            if c\n                " + early + "\n            else => exit\n            return", tail, use));
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Fact]
    public void ReloadedAndReusedMixedTargetsAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Source("var x: i32", "loop\n            if c\n                x = 1\n                return\n            else => exit\n            return\n            exit", "x = 2", "let y = x"));
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
