// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

[TestClass(DisableParallelization = true)]
public class CompletingLoopContinuationTest
{
    private const string Stop = "func stop() -> Never => $abort(\"stop\")\n";

    [Theory]
    [InlineData("var x: i32", "loop\n            if c => return\n            exit", "x = 2", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "loop\n            if c\n                x = 1\n                return\n            exit", "()", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let s = \"s\"", "loop\n            if c\n                Console.writeLine(s)\n                return\n            exit", "()", "Console.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let s = \"s\"", "loop\n            if c => return\n            Console.writeLine(s)\n            exit", "()", "Console.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x: i32", "loop\n            if c\n                x = 1\n                return\n            exit", "()", "x = 2", OwnershipFailure.ReassignedLet)]
    [InlineData("var x: i32", "while c\n            return", "x = 2", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let s = \"s\"", "while c\n            Console.writeLine(s)\n            return", "()", "Console.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    public void EscapingPathsRetainTheirFacts(string declaration, string loop, string tail, string use, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Source(declaration, loop, tail, use));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData("Loop", "loop\n            if c\n                x = 1\n                return\n            exit", true)]
    [InlineData("LoopExit", "loop\n            if c\n                x = 1\n                return\n            exit", false)]
    [InlineData("While", "while c\n            x = 1\n            return", true)]
    [InlineData("WhileExit", "while c\n            x = 1\n            return", false)]
    [InlineData("InternalExit", "loop\n            if c => exit\n            x = 1\n            exit", false)]
    [InlineData("InternalContinue", "var again = true\n        loop\n            if again\n                again = false\n                continue\n            exit", false)]
    [InlineData("ScopeExit", "inner: do\n            if c => exit to inner\n            x = 1", false)]
    [InlineData("NestedLoop", "loop\n            loop\n                if c\n                    x = 1\n                    return\n                exit\n            exit", true)]
    [InlineData("OuterExit", "outer: loop\n            loop\n                if c => exit to outer\n                exit\n            exit", false)]
    [InlineData("Yield", "choice: if c\n            if c => yield to choice\n            x = 1\n        else => ()", false)]
    [InlineData("SameTarget", "loop\n            if c => exit else => exit", false)]
    [InlineData("SameOuterTarget", "outer: loop\n            do\n                if c => exit to outer else => exit to outer", false)]
    [InlineData("DeadTransfer", "loop\n            if c\n                x = 1\n                return\n                exit\n            exit", true)]
    public void InternalTransfersDoNotPolluteOuterJoins(string name, string loop, bool returns)
        => ScalarEmissionTest.EmitFixture(
            "NeverCompletingLoop" + Configuration + name,
            Source("var x: i32", loop, "x = 2", "let y = x\n    Console.writeLine(\"bad\")", returns) + "\nConsole.writeLine(\"done\")",
            returns ? "done\n" : string.Empty,
            returns ? 0 : 1,
            returns ? string.Empty : "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Theory]
    [InlineData("\n            if c => return")]
    public void MixedTargetPropagationRemainsGuarded(string dead)
    {
        var loop = "loop\n            if c\n                x = 1\n                return\n            else => exit" + dead;
        var c = MinimalEmissionTest.Analyze(Source("var x: i32", loop, "x = 2", "let y = x"));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.UninitializedUse);
    }

    [Theory]
    [InlineData("var x: i32", "return", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let x = \"s\"", "Console.writeLine(x)\n                return", "Console.writeLine(x)", OwnershipFailure.PossiblyMovedUse)]
    public void TerminalSelectionsKeepLoopEscapePaths(string declaration, string early, string use, OwnershipFailure failure)
    {
        var source = Stop + "func f(c: bool, d: bool)\n    " + declaration + "\n    if c\n        loop\n            if d\n                " + early + "\n            exit\n        stop()\n    else => return\n    " + use;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Fact]
    public void LoopBorrowEndsBeforeTheEscapingContinuation()
        => ScalarEmissionTest.EmitFixture(
            "NeverCompletingLoop" + Configuration + "Borrow",
            "func inspect(s: ref/string) => ()\n" + Source("var s = \"s\"", "loop\n            if c\n                inspect(s)\n                return\n            exit", "inspect(s)", "s = \"new\"\n    Console.writeLine(s)") + "\nConsole.writeLine(\"done\")",
            "done\n");

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void CompletingDefaultLoopPreservesCallerState(string condition)
        => ScalarEmissionTest.EmitFixture(
            "NeverCompletingLoop" + Configuration + "Default" + condition,
            "func value(c: bool, y?: i32 = (scope: do\n    var n: i32\n    loop\n        if c => exit\n        n = 1\n        exit\n    n = 2\n    loop => continue\n    exit to scope: n\n)) -> i32 => y\nvar x = 1\nConsole.writeLine(\"begin\")\nvalue(" + condition + ")\nlet y = x",
            "begin\n",
            timeoutMilliseconds: 200);

    [Fact]
    public void SuppliedDefaultStillChecksTheLoopDeclaration()
    {
        var c = MinimalEmissionTest.Analyze("func value(c: bool, y?: i32 = (scope: do\n    var n: i32\n    loop\n        if c => exit\n        n = 1\n        exit\n    loop => continue\n    exit to scope: n\n)) -> i32 => y\nvalue(true, 3)");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.UninitializedUse);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Fact]
    public void ReusedAndReloadedLoopJoinsAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Source("var x: i32", "loop\n            if c\n                x = 1\n                return\n            exit", "x = 2", "let y = x"));
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

    private static string Source(string declaration, string loop, string tail, string use, bool condition = true)
        => Stop + "func f(c: bool)\n    " + declaration + "\n    do\n        " + loop + "\n        " + tail + "\n        stop()\n    " + use + "\nf(" + (condition ? "true" : "false") + ")";

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
