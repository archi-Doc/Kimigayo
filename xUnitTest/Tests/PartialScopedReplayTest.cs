// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

[TestClass(DisableParallelization = true)]
public class PartialScopedReplayTest
{
    [Theory]
    [InlineData("Then", "inner: do\n                if c => exit to inner else => x = 3")]
    [InlineData("Else", "inner: do\n                if c => x = 3 else => exit to inner")]
    [InlineData("All", "inner: do\n                if c => exit to inner else => exit to inner")]
    [InlineData("Bare", "inner: do => exit to inner")]
    [InlineData("Escaping", "inner: do\n                if c => return else => exit to inner")]
    [InlineData("Nested", "outer: do\n                inner: do\n                    if c => exit to outer else => exit to inner\n                x = 3")]
    [InlineData("OwnedLocal", "inner: do\n                let local = \"local\"\n                if c => exit to inner else => x = 3")]
    [InlineData("AfterExit", "inner: do\n                if c\n                    exit to inner\n                    x = 3\n                else => x = 4")]
    [InlineData("Scalar", "let n = inner: do\n                if c => exit to inner: 3 else => exit to inner: 4\n            x = n")]
    public void CaughtScopeTransfersJoinNormalArrivals(string name, string dead)
        => Emit("Caught" + name, Source("var x = 1", dead, "x = 4", "let y = x", "x = 2"));

    [Theory]
    [InlineData("Initialization", "var x: i32", "inner: do\n                if c\n                    x = 3\n                    exit to inner\n                else => x = 4", "let y = x")]
    [InlineData("DeadMove", "let s = \"s\"", "inner: do\n                if c\n                    exit to inner\n                    Console.writeLine(s)\n                else => ()", "Console.writeLine(s)")]
    [InlineData("DeadLet", "let x: i32", "inner: do\n                if c\n                    exit to inner\n                    x = 3\n                else => ()", "x = 4")]
    [InlineData("LocalLoan", "var counter = Counter.init()", "inner: do\n                if c\n                    let r = counter@ref\n                    let n = r.value\n                    exit to inner\n                else => ()", "counter.value = 9")]
    [InlineData("MissingCondition", "var x: i32", "inner: do\n                if c => x = 3 else if truth(stop()) => exit to inner else => x = 4", "let y = x")]
    [InlineData("MissingConditionMove", "let s = \"s\"", "inner: do\n                if c => () else if truth(stop())\n                    Console.writeLine(s)\n                    exit to inner\n                else => ()", "Console.writeLine(s)")]
    [InlineData("MissingConditionLet", "let x: i32", "inner: do\n                if c => () else if truth(stop())\n                    x = 3\n                    exit to inner\n                else => ()", "x = 4")]
    public void CaughtArrivalsExcludeLaterDeadEffectsAndIncludeCleanup(string name, string declaration, string dead, string after)
        => Emit("CaughtNormal" + name, Source(declaration, dead, after, "()") + Counter);

    [Theory]
    [InlineData("var x: i32", "inner: do\n                if c => exit to inner else => x = 3", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "inner: do\n                if c\n                    exit to inner\n                    x = 3\n                else => x = 4", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let s = \"s\"", "inner: do\n                if c\n                    Console.writeLine(s)\n                    exit to inner\n                else => ()", "Console.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let s = \"s\"", "inner: do\n                if c => () else\n                    Console.writeLine(s)\n                    exit to inner", "Console.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x: i32", "inner: do\n                if c\n                    x = 3\n                    exit to inner\n                else => ()", "x = 4", OwnershipFailure.ReassignedLet)]
    public void CaughtScopeArrivalsRetainTheirEffects(string declaration, string dead, string after, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Source(declaration, dead, after, "()"));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("Then", "do\n                if c => return else => x = 3")]
    [InlineData("Else", "do\n                if c => x = 3 else => return")]
    [InlineData("MissingElse", "do\n                if c => return")]
    [InlineData("Tail", "do\n                if c => return else => x = 3\n                x = 4")]
    [InlineData("Nested", "do\n                do\n                    if c => return else => x = 3")]
    [InlineData("OwnedLocal", "do\n                let local = \"local\"\n                if c => return else => x = 3")]
    [InlineData("ScalarResult", "let n = do => if c => return else => 3\n            x = n")]
    [InlineData("Logical", "do\n                c and (if c => return else => true)")]
    [InlineData("BranchScope", "if c\n                do\n                    if c => return else => x = 3\n            else => x = 4")]
    [InlineData("TerminalCondition", "do\n                if c => x = 3 else if truth(stop()) => x = 4 else => x = 5")]
    public void CompletingScopesRetainNormalTails(string name, string dead)
        => Emit(name, Source("var x = 1", dead, "x = 4", "let y = x", "x = 2"));

    [Theory]
    [InlineData("Initialization", "var x: i32", "do\n                if c => return else => x = 3", "let y = x")]
    [InlineData("Move", "let s = \"s\"", "do\n                if c\n                    Console.writeLine(s)\n                    return\n                else => ()", "Console.writeLine(s)")]
    [InlineData("Let", "let x: i32", "do\n                if c\n                    x = 3\n                    return\n                else => ()", "x = 4")]
    public void TerminalEffectsDoNotEnterScopeNormalSuccessors(string name, string declaration, string dead, string after)
        => Emit("Normal" + name, Source(declaration, dead, after, "()"));

    [Theory]
    [InlineData("var x: i32", "do\n                if c => return else => x = 3", "()", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "do\n                if c => x = 3 else => return", "()", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "do\n                if c => return", "let y = x", "()", OwnershipFailure.UninitializedUse)]
    [InlineData("let s = \"s\"", "do\n                if c\n                    Console.writeLine(s)\n                    return\n                else => ()", "()", "Console.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let s = \"s\"", "do\n                if c => return else => Console.writeLine(s)", "Console.writeLine(s)", "()", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x: i32", "do\n                if c\n                    x = 3\n                    return\n                else => ()", "()", "x = 4", OwnershipFailure.ReassignedLet)]
    public void ScopeJoinsKeepOnlyCommonGuarantees(string declaration, string dead, string after, string use, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Source(declaration, dead, after, use));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("do\n                if c\n                    counter.value = 9\n                    return\n                else => ()")]
    [InlineData("do\n                if c => return else => counter.value = 9")]
    [InlineData("inner: do\n                if c\n                    counter.value = 9\n                    exit to inner\n                else => ()")]
    [InlineData("inner: do\n                if c => () else\n                    counter.value = 9\n                    exit to inner")]
    public void StoredLoansSurvivePartialScopes(string dead)
    {
        var c = MinimalEmissionTest.Analyze(Source("var counter = Counter.init()\n    let r = counter@ref", dead, "()", "let n = r.value") + Counter);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OriginalRuntimePathsArePreserved(bool condition)
        => Emit("Runtime" + condition, Source("var x = 1", "do\n                if c => return else => x = 3", "x = 4", "let y = x", condition: condition), condition);

    [Fact]
    public void CaughtScopeExitsCannotLoseTheirNormalArrival()
    {
        var c = MinimalEmissionTest.Analyze(Source("var x: i32", "inner: do\n                if c => exit to inner else => x = 3", "let y = x", "()"));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.UninitializedUse);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("do\n                if c => return else => counter.value")]
    [InlineData("inner: do\n                if c => exit to inner else => counter.value")]
    public void ReloadedPartialScopesAllocateNothingWhenWarm(string dead)
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
        => ScalarEmissionTest.EmitFixture("NeverPartialScoped" + Configuration + name, source + "\nConsole.writeLine(\"done\")", condition ? "done\n" : string.Empty, condition ? 0 : 1, condition ? string.Empty : "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    private static string Source(string declaration, string dead, string after, string use, string tail = "()", bool condition = true)
        => "func stop() -> Never => $abort(\"stop\")\nfunc f(c?: bool)\n    " + declaration + "\n    do\n        loop\n            if c => return else => exit\n            " + dead + "\n            " + after + "\n        " + tail + "\n        stop()\n    " + use + "\nf(" + (condition ? "true" : "false") + ")\nfunc truth(x?: i32) -> bool => true";

    private const string Counter = "\nstruct Counter\n    public var value: i32 = 0";

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
