// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

[TestClass(DisableParallelization = true)]
public class BranchReplayContinuationTest
{
    [Theory]
    [InlineData("Both", "var x: i32", "return", "if c => x = 3 else => x = 4", "x = 2", "let y = x", true)]
    [InlineData("BothExit", "var x: i32", "return", "if c => x = 3 else => x = 4", "x = 2", "let y = x", false)]
    [InlineData("Result", "var x: i32", "return", "x = if c => 3 else => 4", "x = 2", "let y = x", true)]
    [InlineData("ResultExit", "var x: i32", "return", "x = if c => 3 else => 4", "x = 2", "let y = x", false)]
    [InlineData("MissingElse", "var x: i32", "x = 1\n                return", "if c => x = 3", "x = 2", "let y = x", true)]
    [InlineData("MissingElseExit", "var x: i32", "x = 1\n                return", "if c => x = 3", "x = 2", "let y = x", false)]
    [InlineData("Nested", "var x: i32", "return", "if c\n                if c => x = 3 else => x = 4\n            else => x = 5", "x = 2", "let y = x", true)]
    [InlineData("Locals", "var x: i32", "return", "if c\n                let n = 3\n                x = n\n            else\n                let n = 4\n                x = n", "x = 2", "let y = x", true)]
    [InlineData("Transfer", "var x: i32", "return", "if c => x = 3 else => x = 4\n            return", "x = 2", "let y = x", true)]
    [InlineData("Chain", "var x: i32", "return", "if c => x = 3 else => x = 4\n            return\n            if c => x = 5 else => x = 6\n            exit", "x = 2", "let y = x", true)]
    [InlineData("ChainExit", "var x: i32", "return", "if c => x = 3 else => x = 4\n            return\n            if c => x = 5 else => x = 6\n            exit", "x = 2", "let y = x", false)]
    [InlineData("Move", "var x = \"old\"", "return", "if c => Console.writeLine(x) else => ()\n            x = \"new\"", "()", "Console.writeLine(x)", true)]
    [InlineData("MoveExit", "var x = \"old\"", "return", "if c => Console.writeLine(x) else => ()\n            x = \"new\"", "()", "Console.writeLine(x)", false)]
    [InlineData("While", "var x: i32", "x = 1\n                return", "while c => x = 3", "x = 2", "let y = x", true)]
    [InlineData("WhileExit", "var x: i32", "x = 1\n                return", "while c => x = 3", "x = 2", "let y = x", false)]
    [InlineData("WhileReinit", "var x: i32", "return", "while c => x = 3\n            x = 4", "x = 2", "let y = x", true)]
    [InlineData("WhileChain", "var x: i32", "x = 1\n                return", "while c => x = 3\n            return\n            while c => x = 4\n            exit", "x = 2", "let y = x", true)]
    [InlineData("WhileNested", "var x: i32", "x = 1\n                return", "while c\n                while c => x = 3", "x = 2", "let y = x", true)]
    [InlineData("WhileBranch", "var x: i32", "x = 1\n                return", "while c\n                if c => x = 3 else => x = 4", "x = 2", "let y = x", true)]
    [InlineData("BranchWhile", "var x: i32", "x = 1\n                return", "if c\n                while c => x = 3\n            else => x = 4", "x = 2", "let y = x", true)]
    [InlineData("WhileMove", "var x = \"old\"", "return", "while c\n                Console.writeLine(x)\n                x = \"new\"", "()", "Console.writeLine(x)", true)]
    [InlineData("WhileMoveExit", "var x = \"old\"", "return", "while c\n                Console.writeLine(x)\n                x = \"new\"", "()", "Console.writeLine(x)", false)]
    public void ClosedBranchReplayPreservesTargets(string name, string declaration, string early, string dead, string tail, string use, bool condition)
        => ScalarEmissionTest.EmitFixture(
            "NeverBranchReplay" + Configuration + name,
            Source(declaration, early, dead, tail, use, condition) + "\nConsole.writeLine(\"done\")",
            condition ? "done\n" : string.Empty,
            condition ? 0 : 1,
            condition ? string.Empty : "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Theory]
    [InlineData("var x: i32", "return", "if c => x = 3", "x = 2", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "return", "if c => () else => x = 3", "x = 2", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "return", "if c => x = 3 else => x = 4", "()", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let x = \"s\"", "return", "if c => Console.writeLine(x) else => ()", "()", "Console.writeLine(x)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x = \"s\"", "return", "if c => () else => Console.writeLine(x)", "()", "Console.writeLine(x)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x: i32", "return", "if c => x = 3 else => ()", "()", "x = 2", OwnershipFailure.ReassignedLet)]
    [InlineData("let x: i32", "return", "if c => () else => x = 3\n            return\n            exit", "()", "x = 2", OwnershipFailure.ReassignedLet)]
    [InlineData("var x: i32", "x = 1\n                return", "if c => x = 3\n            let n = x", "x = 2", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "return", "while c => x = 3", "x = 2", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "return", "while c => x = 3\n            let n = x", "x = 2", "()", OwnershipFailure.UninitializedUse)]
    [InlineData("let x: i32", "return", "while c => x = 3", "()", "()", OwnershipFailure.ReassignedLet)]
    [InlineData("let x = \"s\"", "return", "while c => Console.writeLine(x)", "()", "()", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("var x = \"s\"", "return", "while c\n                if c => Console.writeLine(x) else => ()", "()", "Console.writeLine(x)", OwnershipFailure.PossiblyMovedUse)]
    public void BranchReplayKeepsOnlyCommonGuarantees(string declaration, string early, string dead, string tail, string use, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Source(declaration, early, dead, tail, use));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("loop => x = 3")]
    [InlineData("while c => return")]
    [InlineData("if c\n                defer => x = 3\n            else => x = 4")]
    public void EscapingDivergentAndDeferredPathsRemainGuarded(string dead)
    {
        var c = MinimalEmissionTest.Analyze(Source("var x: i32", "x = 1\n                return", dead, "x = 2", "let y = x"));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData("if c => counter.value = 9 else => ()")]
    [InlineData("if c => () else => counter.value = 9")]
    [InlineData("if c => counter.value = 9 else => ()\n            return\n            exit")]
    [InlineData("while c => counter.value = 9")]
    [InlineData("while c\n                if c => counter.value = 9 else => ()")]
    public void StoredLoansStayLiveAcrossBranches(string dead)
    {
        var c = MinimalEmissionTest.Analyze(Source("var counter = Counter.init()\n    let r = counter@ref", "return", dead, "()", "let n = r.value") + Counter);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("if c\n                let n = counter.value\n            else => ()\n            return")]
    [InlineData("while c\n                let n = counter.value\n            return")]
    public void ReloadAndWarmBranchReplayPreserveLoansWithoutAllocating(string dead)
    {
        var c = MinimalEmissionTest.Analyze(Source("var counter = Counter.init()\n    let r = counter@ref", "return", dead, "()", "let n = r.value") + Counter);
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

    private static string Source(string declaration, string early, string dead, string tail, string use, bool condition = true)
        => "func stop() -> Never => $abort(\"stop\")\nfunc f(c: bool)\n    " + declaration + "\n    do\n        loop\n            if c\n                " + early + "\n            else => exit\n            " + dead + "\n        " + tail + "\n        stop()\n    " + use + "\nf(" + (condition ? "true" : "false") + ")";

    private const string Counter = "\nstruct Counter\n    public var value: i32 = 0";

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
