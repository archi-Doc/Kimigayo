// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

[TestClass(DisableParallelization = true)]
public class ShortCircuitContinuationTest
{
    private const string Helpers = "\nfunc take(x: string) -> bool => true\nfunc truth(x: i32) -> bool => true\nfunc effect(x: ()) -> bool => true";

    [Theory]
    [InlineData("And", "if c and c => x = 3", true)]
    [InlineData("AndExit", "if c and c => x = 3", false)]
    [InlineData("Or", "if c or c => x = 3", true)]
    [InlineData("OrExit", "if c or c => x = 3", false)]
    [InlineData("Nested", "if (c and c) or (c and c) => x = 3", true)]
    [InlineData("Value", "let b = c and c\n            if b => x = 3", true)]
    [InlineData("While", "while c and c => x = 3", true)]
    [InlineData("ScopeEffect", "let b = c and effect(x = 3)", true)]
    [InlineData("Chain", "if c and c => x = 3\n            return\n            if c or c => x = 4\n            exit", true)]
    public void CompletingOperandsKeepTargetFacts(string name, string dead, bool condition)
        => ScalarEmissionTest.EmitFixture(
            "NeverShortCircuit" + Configuration + name,
            Source("var x: i32", "x = 1\n                return", dead, "x = 2", "let y = x", condition) + "\nwriteLine(\"done\")" + Helpers,
            condition ? "done\n" : string.Empty,
            condition ? 0 : 1,
            condition ? string.Empty : "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Theory]
    [InlineData("var x: i32", "let b = c and effect(x = 3)", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "let b = c or effect(x = 3)", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let x = \"s\"", "let b = c and take(x)", "writeLine(x)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x = \"s\"", "let b = c or take(x)", "writeLine(x)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x: i32", "let b = c and effect(x = 3)", "x = 4", OwnershipFailure.ReassignedLet)]
    [InlineData("let x: i32", "let b = c or effect(x = 3)", "x = 4", OwnershipFailure.ReassignedLet)]
    public void SkippedOperandPathsRetainOrdinaryDiagnostics(string declaration, string dead, string use, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Source(declaration, "return", dead, "()", use) + Helpers);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("AndSkip", "and", false, "", 1)]
    [InlineData("AndEvaluate", "and", true, "probe\ndone\n", 0)]
    [InlineData("OrSkip", "or", true, "done\n", 0)]
    [InlineData("OrEvaluate", "or", false, "probe\ndone\n", 0)]
    public void RuntimeShortCircuitEvaluationIsPreserved(string name, string op, bool condition, string stdout, int exit)
    {
        var source = Source("var x: i32", "x = 1\n                return", "()", "x = 2", "let y = x", condition)
            .Replace("if c\n", "if c " + op + " probe()\n", StringComparison.Ordinal)
            + "\nwriteLine(\"done\")\nfunc probe() -> bool\n    writeLine(\"probe\")\n    return true";
        ScalarEmissionTest.EmitFixture("NeverShortCircuit" + Configuration + name, source, stdout, exit, exit == 0 ? string.Empty : "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");
    }

    [Theory]
    [InlineData("truth(stop()) and (choice: do\n                if c => return\n                exit to choice: true\n            )")]
    [InlineData("c and (do\n                loop => x = 3\n                true\n            )")]
    [InlineData("c or (do\n                defer => x = 3\n                return\n                true\n            )")]
    public void NoncompletingOperandsRemainGuarded(string expression)
    {
        var c = MinimalEmissionTest.Analyze(Source("var x: i32", "x = 1\n                return", "let b = " + expression, "x = 2", "let y = x") + Helpers);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData("AbortAnd", "c and truth(stop())")]
    [InlineData("AbortOr", "c or truth(stop())")]
    [InlineData("Return", "c and (do\n                return\n                true\n            )")]
    [InlineData("NeutralLoop", "c or (do\n                loop => continue\n                true\n            )")]
    [InlineData("Exit", "c and (exit)")]
    public void TerminalRightOperandsKeepMixedTargets(string name, string expression)
        => ScalarEmissionTest.EmitFixture("NeverMixedLogicalRight" + Configuration + name, Source("var x: i32", "x = 1\n                return", "let b = " + expression, "x = 2", "let y = x") + "\nwriteLine(\"done\")" + Helpers, "done\n");

    [Theory]
    [InlineData("And", "truth(stop()) and c")]
    [InlineData("Or", "truth(stop()) or c")]
    [InlineData("Both", "truth(stop()) and truth(stop())")]
    [InlineData("Nested", "(truth(stop()) and c) or c")]
    [InlineData("Effect", "truth(stop()) or effect(x = 3)")]
    public void TerminalLeftOperandsKeepMixedTargets(string name, string expression)
        => ScalarEmissionTest.EmitFixture("NeverMixedLogicalLeft" + Configuration + name, Source("var x: i32", "x = 1\n                return", "let b = " + expression, "x = 2", "let y = x") + "\nwriteLine(\"done\")" + Helpers, "done\n");

    [Theory]
    [InlineData("var x: i32", "c and (return)", "x = 3", "x = 2", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "c or (return)", "x = 3", "x = 2", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let s = \"s\"", "c and truth(stopTake(s))", "()", "()", "writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let s = \"s\"", "c or truth(stopTake(s))", "()", "()", "writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x: i32", "c and (do\n                x = 3\n                return\n                true\n            )", "()", "()", "x = 4", OwnershipFailure.ReassignedLet)]
    [InlineData("var x: i32", "truth(stop()) and effect(x = 3)", "()", "x = 2", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "truth(stop()) or effect(x = 3)", "()", "x = 2", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let x: i32", "truth(stop()) and effect(x = 3)", "()", "()", "x = 4", OwnershipFailure.ReassignedLet)]
    [InlineData("let s = \"s\"", "truth(stop()) and take(s)", "()", "()", "writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    public void TerminalRightEffectsReachOnlyEnclosingJoins(string declaration, string expression, string after, string tail, string use, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Source(declaration, "return", "let b = " + expression + "\n            " + after, tail, use) + Helpers + "\nfunc stopTake(s: string) -> Never => stop()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void TerminalRightMoveDoesNotPolluteTheSkippedSuccessor()
        => ScalarEmissionTest.EmitFixture("NeverMixedLogicalRight" + Configuration + "SkippedMove", Source("let s = \"s\"", "return", "let b = c and truth(stopTake(s))\n            writeLine(s)", "()", "()") + "\nwriteLine(\"done\")" + Helpers + "\nfunc stopTake(s: string) -> Never => stop()", "done\n");

    [Theory]
    [InlineData("c")]
    [InlineData("truth(stop())")]
    public void StoredLoansSurviveTerminalRightMutation(string left)
    {
        var c = MinimalEmissionTest.Analyze(Source("var counter = Counter.init()\n    let r = counter@ref", "return", "let b = " + left + " and (do\n                counter.value = 9\n                return\n                true\n            )", "()", "let n = r.value") + "\nstruct Counter\n    public var value: i32 = 0" + Helpers);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData("and")]
    [InlineData("or")]
    public void StoredLoansSurviveConditionalMutation(string op)
    {
        var source = Source("var counter = Counter.init()\n    let r = counter@ref", "return", "let b = c " + op + " effect(counter.value = 9)", "()", "let n = r.value") + "\nstruct Counter\n    public var value: i32 = 0" + Helpers;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("if (c and c) or c => x = 3\n            return")]
    [InlineData("let b = c and truth(stop())")]
    [InlineData("let b = truth(stop()) and c")]
    public void ReloadedLogicalReplayAllocatesNothingWhenWarm(string dead)
    {
        var c = MinimalEmissionTest.Analyze(Source("var x: i32", "x = 1\n                return", dead, "x = 2", "let y = x") + Helpers);
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

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
