// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

[TestClass(DisableParallelization = true)]
public class PartialBranchReplayTest
{
    [Theory]
    [InlineData("Then", "inner: if c => yield to inner else => x = 3")]
    [InlineData("Else", "inner: if c => x = 3 else => yield to inner")]
    [InlineData("All", "inner: if c => yield to inner else => yield to inner")]
    [InlineData("MissingElse", "inner: if c => yield to inner")]
    [InlineData("Escaping", "inner: if c => return else => yield to inner")]
    [InlineData("OwnedLocal", "inner: if c\n                let local = \"local\"\n                yield to inner\n            else => x = 3")]
    [InlineData("Nested", "outer: if c\n                inner: if c => yield to outer else => yield to inner\n            else => x = 4")]
    [InlineData("Scope", "scope: do\n                inner: if c => exit to scope else => yield to inner")]
    [InlineData("Scalar", "let n = inner: if c => yield to inner: 3 else => 4\n            x = n")]
    public void CaughtSelectionTransfersRetainNormalArrivals(string name, string dead)
        => Emit("Caught" + name, Source("var x = 1", dead + "\n            x = 4", "x = 2", "let y = x"));

    [Theory]
    [InlineData("Initialization", "var x: i32", "inner: if c\n                x = 3\n                yield to inner\n            else => x = 4\n            let y = x")]
    [InlineData("DeadMove", "let s = \"s\"", "inner: if c\n                yield to inner\n                Console.writeLine(s)\n            else => ()\n            Console.writeLine(s)")]
    [InlineData("DeadLet", "let x: i32", "inner: if c\n                yield to inner\n                x = 3\n            else => ()\n            x = 4")]
    [InlineData("MissingCondition", "var x: i32", "inner: if c => x = 3 else if truth(stop()) => yield to inner else => x = 4\n            let y = x")]
    [InlineData("LocalLoan", "var counter = Counter.init()", "inner: if c\n                let r = counter@ref\n                let n = r.value\n                yield to inner\n            else => ()\n            counter.value = 9")]
    public void CaughtSelectionArrivalsIncludeCleanupAndExcludeLaterDeadEffects(string name, string declaration, string dead)
        => Emit("CaughtNormal" + name, Source(declaration, dead, "()", "()") + Counter + "\nfunc truth(x?: i32) -> bool => true");

    [Theory]
    [InlineData("var x: i32", "inner: if c\n                yield to inner\n                x = 3\n            else => x = 4\n            let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let s = \"s\"", "inner: if c\n                Console.writeLine(s)\n                yield to inner\n            else => ()\n            Console.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let s = \"s\"", "inner: if c => () else\n                Console.writeLine(s)\n                yield to inner\n            Console.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x: i32", "inner: if c\n                x = 3\n                yield to inner\n            else => ()\n            x = 4", OwnershipFailure.ReassignedLet)]
    public void CaughtSelectionArrivalsRetainTheirOwnEffects(string declaration, string dead, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Source(declaration, dead, "()", "()"));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CaughtSelectionCheckingAddsNoRuntimeEdges(bool condition)
        => Emit("CaughtRuntime" + condition, Source("var x = 1", "inner: if c => yield to inner else => x = 3", "x = 2", "let y = x", condition), condition);

    [Theory]
    [InlineData("ThenReturn", "if c => return else => x = 3")]
    [InlineData("ElseReturn", "if c => x = 3 else => return")]
    [InlineData("MissingElse", "if c => return")]
    [InlineData("ElseIf", "if c => return else if c => x = 3 else => x = 4")]
    [InlineData("TwoNormal", "if c => x = 3 else if c => return else => x = 4")]
    [InlineData("Nested", "if c\n                if c => return else => x = 3\n            else => x = 4")]
    [InlineData("TailEffect", "if c => return else => x = 3\n            x = 4")]
    [InlineData("TailReturn", "if c => return else => x = 3\n            return")]
    [InlineData("OwnedLocal", "if c => return else\n                let s = \"local\"\n                x = 3")]
    [InlineData("Require", "if c => return else\n                require c else => return\n                x = 3")]
    [InlineData("Abort", "if c => stop() else => x = 3")]
    public void PartialBranchesRetainOriginalTargets(string name, string dead)
        => Emit(name, Source("var x = 1", dead, "x = 2", "let y = x"));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OriginalRuntimePathsStayUnchanged(bool condition)
        => Emit("Runtime" + condition, Source("var x = 1", "if c => return else => x = 3", "x = 2", "let y = x", condition), condition);

    [Theory]
    [InlineData("Then", "if c => return else => x = 3\n            let y = x")]
    [InlineData("Else", "if c => x = 3 else => return\n            let y = x")]
    [InlineData("Two", "if c => x = 3 else if c => return else => x = 4\n            let y = x")]
    [InlineData("Nested", "if c\n                if c => return else => x = 3\n            else => x = 4\n            let y = x")]
    public void TerminalStatesDoNotReplaceNormalTailStates(string name, string dead)
        => Emit("Normal" + name, Source("var x: i32", dead, "()", "()"));

    [Theory]
    [InlineData("Move", "let s = \"s\"", "if c\n                Console.writeLine(s)\n                return\n            else => ()\n            Console.writeLine(s)")]
    [InlineData("Let", "let x: i32", "if c\n                x = 3\n                return\n            else => ()\n            x = 4")]
    [InlineData("Loan", "var counter = Counter.init()\n    let r = counter@ref", "if c\n                counter.value = 9\n                return\n            else => ()\n            let n = r.value")]
    public void TerminalEffectsDoNotPolluteNormalSuccessors(string name, string declaration, string dead)
        => Emit("NormalEffects" + name, Source(declaration, dead, "()", "()") + Counter);

    [Fact]
    public void CaughtYieldArrivalsMustNotBeDroppedFromTheNormalJoin()
    {
        var c = MinimalEmissionTest.Analyze(Source("var x: i32", "inner: if c => yield to inner else => x = 3\n            let y = x", "()", "()"));
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Empty(c.Ownership.ControlFlow!.Issues);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.UninitializedUse);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("var x: i32", "if c => return else => x = 3", "x = 2", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "if c => x = 3 else => return", "x = 2", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "if c => x = 3 else if c => return else => ()\n            let y = x", "()", "()", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "if c => return\n            let y = x", "()", "()", OwnershipFailure.UninitializedUse)]
    [InlineData("let s = \"s\"", "if c\n                Console.writeLine(s)\n                return\n            else => ()", "()", "Console.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let s = \"s\"", "if c => return else => Console.writeLine(s)", "()", "Console.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let s = \"s\"", "if c => return else => Console.writeLine(s)\n            Console.writeLine(s)", "()", "()", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x: i32", "if c\n                x = 3\n                return\n            else => ()", "()", "x = 4", OwnershipFailure.ReassignedLet)]
    [InlineData("let x: i32", "if c => return else => x = 3\n            x = 4", "()", "()", OwnershipFailure.ReassignedLet)]
    public void EveryRelevantPathRetainsItsEffects(string declaration, string dead, string tail, string use, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Source(declaration, dead, tail, use));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("if c\n                counter.value = 9\n                return\n            else => ()")]
    [InlineData("if c => return else => counter.value = 9")]
    [InlineData("if c => return else => ()\n            counter.value = 9")]
    [InlineData("inner: if c\n                counter.value = 9\n                yield to inner\n            else => ()")]
    [InlineData("inner: if c => () else\n                counter.value = 9\n                yield to inner")]
    public void StoredLoansCoverTerminalAndNormalPaths(string dead)
    {
        var c = MinimalEmissionTest.Analyze(Source("var counter = Counter.init()\n    let r = counter@ref", dead, "()", "let n = r.value") + Counter);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData("if c => return else\n                if c => return else => ()\n                let n = counter.value")]
    [InlineData("inner: if c => yield to inner else => counter.value")]
    public void ReloadedNormalJoinsAllocateNothingWhenWarm(string dead)
    {
        var c = MinimalEmissionTest.Analyze(Source("var counter = Counter.init()\n    let r = counter@ref", dead, "()", "let n = r.value") + Counter);
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

    private static void Emit(string name, string source, bool condition = true)
        => ScalarEmissionTest.EmitFixture("NeverPartialBranch" + Configuration + name, source + "\nConsole.writeLine(\"done\")", condition ? "done\n" : string.Empty, condition ? 0 : 1, condition ? string.Empty : "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    private static string Source(string declaration, string dead, string tail, string use, bool condition = true)
        => "func stop() -> Never => $abort(\"stop\")\nfunc f(c?: bool)\n    " + declaration + "\n    do\n        loop\n            if c => return else => exit\n            " + dead + "\n        " + tail + "\n        stop()\n    " + use + "\nf(" + (condition ? "true" : "false") + ")";

    private const string Counter = "\nstruct Counter\n    public var value: i32 = 0";

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
