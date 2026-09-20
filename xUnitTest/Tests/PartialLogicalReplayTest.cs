// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

[TestClass(DisableParallelization = true)]
public class PartialLogicalReplayTest
{
    [Theory]
    [InlineData("And", "truth(stop()) and (if c => return else => true)")]
    [InlineData("Or", "truth(stop()) or (if c => return else => true)")]
    [InlineData("Else", "truth(stop()) and (if c => true else => return)")]
    [InlineData("Nested", "truth(stop()) and (if c => (if c => return else => true) else => false)")]
    [InlineData("Effect", "truth(stop()) or (if c => return else => effect(x = 3))")]
    public void NoncompletingLeftRetainsEveryCheckingPath(string name, string expression)
        => Emit("Left" + name, Source("var x = 1", expression, "x = 4", "let y = x") + Truth);

    [Theory]
    [InlineData("var x: i32", "truth(stop()) and (if c => return else => effect(x = 3))", "()", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "truth(stop()) or (if c => effect(x = 3) else => return)", "let y = x", "()", OwnershipFailure.UninitializedUse)]
    [InlineData("let s = \"s\"", "truth(stop()) and (if c => stopTake(s) else => true)", "Console.writeLine(s)", "()", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let s = \"s\"", "truth(stop()) or (if c => true else => take(s))", "()", "Console.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x: i32", "truth(stop()) and (if effect(x = 3) => return else => true)", "x = 4", "()", OwnershipFailure.ReassignedLet)]
    public void NoncompletingLeftDoesNotEraseRhsOrSkippedHistories(string declaration, string expression, string after, string use, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Source(declaration, expression, after, use) + Truth);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("And", "c and (if c => return else => true)")]
    [InlineData("Or", "c or (if c => return else => true)")]
    [InlineData("Else", "c and (if c => true else => return)")]
    [InlineData("Abort", "c or (if c => stop() else => true)")]
    [InlineData("Effect", "c and (if c => return else => effect(x = 3))")]
    [InlineData("NestedIf", "c and (if c => (if c => return else => true) else => false)")]
    [InlineData("NestedLogical", "c and ((if c => return else => true) or false)")]
    [InlineData("NestedRight", "c or (c and (if c => return else => true))")]
    public void PartialRightPathsPreserveEachOriginalTarget(string name, string expression)
        => Emit(name, Source("var x = 1", expression, "x = 4", "let y = x"));

    [Theory]
    [InlineData("and", true)]
    [InlineData("and", false)]
    [InlineData("or", true)]
    [InlineData("or", false)]
    public void OriginalRuntimePathsRemainUnchanged(string op, bool condition)
        => Emit(op + condition, Source("var x = 1", "c " + op + " (if c => return else => true)", "()", "let y = x", condition), condition);

    [Theory]
    [InlineData("and")]
    [InlineData("or")]
    public void TerminalMoveDoesNotPolluteNormalLogicalSuccessor(string op)
        => Emit("NormalMove" + op, Source("let s = \"s\"", "c " + op + " (if c => stopTake(s) else => true)", "Console.writeLine(s)", "()"));

    [Theory]
    [InlineData("var x: i32", "c and (if c => return else => effect(x = 3))", "let y = x", "()", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "c or (if c => effect(x = 3) else => return)", "let y = x", "()", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "c and (if c => return else => true)", "x = 4", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let s = \"s\"", "c and (if c => stopTake(s) else => true)", "()", "Console.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let s = \"s\"", "c or (if c => true else => stopTake(s))", "()", "Console.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let s = \"s\"", "c and (if c => return else => take(s))", "Console.writeLine(s)", "()", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let s = \"s\"", "c or (if take(s) => return else => true)", "Console.writeLine(s)", "()", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x: i32", "c and (if c => return else => effect(x = 3))", "x = 4", "()", OwnershipFailure.ReassignedLet)]
    [InlineData("let x: i32", "c or (if effect(x = 3) => return else => true)", "()", "x = 4", OwnershipFailure.ReassignedLet)]
    public void SkippedNormalAndTerminalPathsKeepTheirOwnEffects(string declaration, string expression, string after, string use, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Source(declaration, expression, after, use));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("c and (if c => return else => effect(counter.value = 9))")]
    [InlineData("c or (if effect(counter.value = 9) => return else => true)")]
    [InlineData("truth(stop()) and (if c => return else => effect(counter.value = 9))")]
    [InlineData("truth(stop()) or (if effect(counter.value = 9) => return else => true)")]
    public void StoredLoansFollowPartialRightHistories(string expression)
    {
        var c = MinimalEmissionTest.Analyze(Source("var counter = Counter.init()\n    let r = counter@ref", expression, "()", "let n = r.value") + Counter + Truth);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData("and", false)]
    [InlineData("or", false)]
    [InlineData("and", true)]
    [InlineData("or", true)]
    public void ReloadedPartialLogicalJoinsAllocateNothingWhenWarm(string op, bool terminalLeft)
    {
        var c = MinimalEmissionTest.Analyze(Source("var counter = Counter.init()\n    let r = counter@ref", (terminalLeft ? "truth(stop()) " : "c ") + op + " (if c => return else => counter.value == 0)", "()", "let n = r.value") + Counter + Truth);
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
        => ScalarEmissionTest.EmitFixture("NeverPartialLogical" + Configuration + name, source + "\nConsole.writeLine(\"done\")", condition ? "done\n" : string.Empty, condition ? 0 : 1, condition ? string.Empty : "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    private static string Source(string declaration, string expression, string after, string use, bool condition = true)
        => "func stop() -> Never => $abort(\"stop\")\nfunc f(c?: bool)\n    " + declaration + "\n    do\n        loop\n            if c => return else => exit\n            let b = " + expression + "\n            " + after + "\n        stop()\n    " + use + "\nf(" + (condition ? "true" : "false") + ")" + Helpers;

    private const string Helpers = "\nfunc effect(x?: ()) -> bool => true\nfunc take(s?: string) -> bool => true\nfunc stopTake(s?: string) -> Never => stop()";
    private const string Counter = "\nstruct Counter\n    public var value: i32 = 0";
    private const string Truth = "\nfunc truth(x?: i32) -> bool => true";

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
