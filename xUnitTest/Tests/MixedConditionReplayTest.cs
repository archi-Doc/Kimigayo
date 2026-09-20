// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

[TestClass(DisableParallelization = true)]
public class MixedConditionReplayTest
{
    [Theory]
    [InlineData("Then", "if truth(stop())\n        if c => return else => x = 2\n    else => x = 3")]
    [InlineData("Else", "if truth(stop()) => x = 3 else\n        if c => x = 2 else => return")]
    public void OrdinaryPartialConditionBodiesHaveNoRuntimeSuccessor(string name, string dead)
        => ScalarEmissionTest.EmitFixture("NeverMixedCondition" + Configuration + "PartialBodyOrdinary" + name, OrdinarySource(dead, true).Replace("var x: i32", "var x = 1", StringComparison.Ordinal), string.Empty, 1, "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Theory]
    [InlineData("Then", "if truth(stop())\n                if c => return else => x = 3\n            else => x = 4")]
    [InlineData("Else", "if truth(stop()) => x = 4 else\n                if c => x = 3 else => return")]
    [InlineData("MissingElse", "if truth(stop())\n                if c => return")]
    [InlineData("LaterCondition", "if c => x = 4 else if truth(stop())\n                if c => return else => x = 3\n            else => x = 5")]
    [InlineData("NormalTail", "if truth(stop())\n                if c => return else => x = 3\n                x = 5\n            else => x = 4")]
    [InlineData("OwnedLocal", "if truth(stop())\n                let local = \"local\"\n                if c => return else => x = 3\n            else => x = 4")]
    [InlineData("Exit", "if truth(stop())\n                if c => exit else => x = 3\n            else => x = 4")]
    [InlineData("Abort", "if truth(stop())\n                if c => stop() else => x = 3\n            else => x = 4")]
    public void PartialBodiesAfterMissingConditionsRetainEveryHistory(string name, string dead)
        => ScalarEmissionTest.EmitFixture("NeverMixedCondition" + Configuration + "PartialBody" + name, Source("var x = 1", dead, "let y = x", "x = 2") + "\nConsole.writeLine(\"done\")", "done\n");

    [Theory]
    [InlineData("var x: i32", "if truth(stop())\n                if c => return else => x = 3\n            else => x = 4", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "if truth(stop()) => x = 4 else\n                if c => x = 3 else => return", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let s = \"s\"", "if truth(stop())\n                if c\n                    Console.writeLine(s)\n                    return\n                else => ()\n            else => ()", "Console.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let s = \"s\"", "if truth(stop()) => () else\n                if c => return else => Console.writeLine(s)", "Console.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x: i32", "if truth(stop())\n                if c\n                    x = 3\n                    return\n                else => ()\n            else => ()", "x = 4", OwnershipFailure.ReassignedLet)]
    [InlineData("let x: i32", "if truth(stop()) => () else\n                if c => return else => x = 3", "x = 4", OwnershipFailure.ReassignedLet)]
    public void PartialMissingConditionBodiesKeepTerminalAndNormalEffects(string declaration, string dead, string use, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Source(declaration, dead, use));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("if truth(stop())\n                if c\n                    counter.value = 9\n                    return\n                else => ()\n            else => ()")]
    [InlineData("if truth(stop()) => () else\n                if c => return else => counter.value = 9")]
    public void PartialMissingConditionBodiesRetainStoredLoans(string dead)
    {
        var c = MinimalEmissionTest.Analyze(Source("var counter = Counter.init()\n    let r = counter@ref", dead, "let n = r.value") + Counter);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OrdinaryNormalJoinsStayOutOfDeadConditionRegions(bool condition)
        => ScalarEmissionTest.EmitFixture("NeverMixedCondition" + Configuration + "CompletingRuntime" + condition, OrdinarySource("if c => x = 1 else if truth(stop()) => x = 2 else => x = 3", condition), condition ? "normal\n" : string.Empty, condition ? 0 : 1, condition ? string.Empty : "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Fact]
    public void SingleTargetDeadJoinsKeepTheirOriginalRegion()
        => ScalarEmissionTest.EmitFixture("NeverMixedCondition" + Configuration + "CompletingSingleTarget", OrdinarySource("if c => x = 1 else if truth(stop()) => x = 2 else => x = 3", true).Replace("    var x: i32", "    return\n    var x: i32", StringComparison.Ordinal) + "\nConsole.writeLine(\"done\")", "done\n");

    [Fact]
    public void DeadConditionBranchesCannotInitializeTheOrdinarySuccessor()
    {
        var c = MinimalEmissionTest.Analyze(OrdinarySource("if c => () else if truth(stop()) => x = 2 else => x = 3", true));
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.UninitializedUse);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("BothWrite", "var x: i32", "if truth(stop()) => x = 3 else => x = 4", "let y = x")]
    [InlineData("Initialized", "var x = 1", "if truth(stop()) => () else => ()", "let y = x")]
    [InlineData("MissingElse", "var x = 1", "if truth(stop()) => x = 3", "let y = x")]
    [InlineData("Transfer", "var x = 1", "if (return) => x = 3 else => x = 4", "let y = x")]
    [InlineData("ElseIf", "var x = 1", "if c => return else if truth(stop()) => x = 3 else => x = 4", "let y = x")]
    [InlineData("TerminalBranch", "var x = 1", "if truth(stop()) => return else => x = 4", "let y = x")]
    [InlineData("Nested", "var x: i32", "if truth(stop())\n                if c => x = 3 else => x = 4\n            else => x = 5", "let y = x")]
    public void MissingConditionKeepsCheckingBranches(string name, string declaration, string dead, string use)
        => ScalarEmissionTest.EmitFixture("NeverMixedCondition" + Configuration + name, Source(declaration, dead, use, "x = 2") + "\nConsole.writeLine(\"done\")", "done\n");

    [Theory]
    [InlineData("var x: i32", "if truth(stop()) => x = 3 else => ()", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "if truth(stop()) => () else => x = 3", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "if truth(stop()) => x = 3", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "if c => return else if truth(stop()) => x = 3 else => x = 4", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let s = \"s\"", "if truth(stop()) => Console.writeLine(s) else => ()", "Console.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let s = \"s\"", "if truth(stopTake(s)) => () else => ()", "Console.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x: i32", "if truth(stop()) => x = 3 else => ()", "x = 4", OwnershipFailure.ReassignedLet)]
    public void EveryCheckingBranchMustSupportTheGuarantee(string declaration, string dead, string use, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Source(declaration, dead, use));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("if truth(stop()) => counter.value = 9 else => ()")]
    [InlineData("if truth(stop()) => () else => counter.value = 9")]
    public void StoredLoansRemainLiveAcrossBothCheckingBranches(string dead)
    {
        var c = MinimalEmissionTest.Analyze(Source("var counter = Counter.init()\n    let r = counter@ref", dead, "let n = r.value") + Counter);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData("Later", "if c => x = 3 else if truth(stop()) => x = 4 else => x = 5")]
    [InlineData("MissingElse", "if c => x = 3 else if truth(stop()) => x = 4")]
    [InlineData("Chain", "if c => x = 3 else if truth(stop()) => x = 4 else if c => x = 5 else => x = 6")]
    [InlineData("TerminalBranch", "if c => x = 3 else if truth(stop()) => return else => x = 5")]
    public void CompletingSelectionsRetainNormalAndTerminalConditionPaths(string name, string dead)
        => ScalarEmissionTest.EmitFixture("NeverMixedCondition" + Configuration + "Completing" + name, Source("var x = 1", dead, "let y = x") + "\nConsole.writeLine(\"done\")", "done\n");

    [Theory]
    [InlineData("Initialization", "var x: i32", "if c => x = 3 else if truth(stop()) => () else => ()\n            let y = x")]
    [InlineData("Move", "let s = \"s\"", "if c => () else if truth(stopTake(s)) => () else => ()\n            Console.writeLine(s)")]
    [InlineData("Let", "let x: i32", "if c => () else if truth(stop()) => x = 3 else => ()\n            x = 4")]
    public void LaterTerminalEffectsDoNotEnterTheNormalSuccessor(string name, string declaration, string dead)
        => ScalarEmissionTest.EmitFixture("NeverMixedCondition" + Configuration + "CompletingNormal" + name, Source(declaration, dead, "()") + "\nConsole.writeLine(\"done\")", "done\n");

    [Theory]
    [InlineData("var x: i32", "if c => () else if truth(stop()) => x = 3 else => x = 4", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "if c => x = 3 else if truth(stop()) => () else => x = 4", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let s = \"s\"", "if c => () else if truth(stopTake(s)) => () else => ()", "Console.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let s = \"s\"", "if c => Console.writeLine(s) else if truth(stop()) => () else => ()\n            Console.writeLine(s)", "()", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x: i32", "if c => () else if truth(stop()) => x = 3 else => ()", "x = 4", OwnershipFailure.ReassignedLet)]
    public void EarlierNormalAndLaterTerminalHistoriesBothMatter(string declaration, string dead, string use, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Source(declaration, dead, use));
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("if c => counter.value = 9 else if truth(stop()) => () else => ()")]
    [InlineData("if c => () else if truth(stop()) => counter.value = 9 else => ()")]
    public void StoredLoansRetainEarlierAndLaterConditionPaths(string dead)
    {
        var c = MinimalEmissionTest.Analyze(Source("var counter = Counter.init()\n    let r = counter@ref", dead, "let n = r.value") + Counter);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData("if truth(stop()) => counter.value else => counter.value")]
    [InlineData("if c => counter.value else if truth(stop()) => counter.value else => counter.value")]
    [InlineData("if truth(stop())\n                if c => return else => counter.value\n            else => counter.value")]
    public void ReloadedTerminalConditionsAllocateNothingWhenWarm(string dead)
    {
        var c = MinimalEmissionTest.Analyze(Source("var counter = Counter.init()\n    let r = counter@ref", dead, "let n = r.value") + Counter);
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

    private static string Source(string declaration, string dead, string use, string tail = "()")
        => "func stop() -> Never => $abort(\"stop\")\nfunc f(c?: bool)\n    " + declaration + "\n    do\n        loop\n            if c => return else => exit\n            " + dead + "\n        " + tail + "\n        stop()\n    " + use + "\nf(true)\nfunc truth(x?: i32) -> bool => true\nfunc stopTake(s?: string) -> Never => stop()";

    private const string Counter = "\nstruct Counter\n    public var value: i32 = 0";

    private static string OrdinarySource(string dead, bool condition)
        => "func stop() -> Never => $abort(\"stop\")\nfunc truth(x?: i32) -> bool => true\nfunc f(c?: bool)\n    var x: i32\n    " + dead + "\n    let y = x\n    Console.writeLine(\"normal\")\nf(" + (condition ? "true" : "false") + ")";

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
