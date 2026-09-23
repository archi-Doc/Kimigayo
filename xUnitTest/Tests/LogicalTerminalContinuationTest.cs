// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

[TestClass(DisableParallelization = true)]
public class LogicalTerminalContinuationTest
{
    private const string Helpers = "\nfunc truth(x: i32) -> bool => true\nfunc effect(x: ()) -> bool => true\nfunc take(x: string) -> bool => true\nfunc stopTake(x: string) -> Never => $abort(\"taken\")";

    [Theory]
    [InlineData("LeftAnd", "truth(stop()) and effect(x = 2)", true, true)]
    [InlineData("LeftOr", "truth(stop()) or effect(x = 2)", true, true)]
    [InlineData("RightAndSkip", "c and truth(stop())", false, false)]
    [InlineData("RightAndAbort", "c and truth(stop())", true, true)]
    [InlineData("RightOrSkip", "c or truth(stop())", true, false)]
    [InlineData("RightOrAbort", "c or truth(stop())", false, true)]
    [InlineData("Both", "truth(stop()) and truth(stop())", true, true)]
    [InlineData("Nested", "(truth(stop()) and c) or c", true, true)]
    public void AbsentOperandsNeverCreateRuntimeResults(string name, string expression, bool condition, bool abort)
        => ScalarEmissionTest.EmitFixture("NeverLogicalTerminal" + Configuration + name, Source("var x = 1", expression, "return", "let y = x", condition) + "\nConsole.writeLine(\"done\")" + Helpers, abort ? string.Empty : "done\n", abort ? 1 : 0, abort ? "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n" : string.Empty);

    [Theory]
    [InlineData("AndReturn", "c and (return)", true, "done\n")]
    [InlineData("AndSkipReturn", "c and (return)", false, "tail\ndone\n")]
    [InlineData("OrReturn", "c or (return)", false, "done\n")]
    [InlineData("OrSkipReturn", "c or (return)", true, "tail\ndone\n")]
    public void RightTransferOnlyRunsOnTheEvaluatedPath(string name, string expression, bool condition, string stdout)
        => ScalarEmissionTest.EmitFixture("NeverLogicalTerminal" + Configuration + name, Source("var x = 1", expression, "Console.writeLine(\"tail\")\n        return", "let y = x", condition) + "\nConsole.writeLine(\"done\")", stdout);

    [Theory]
    [InlineData("var x: i32", "truth(stop()) and effect(x = 2)", "return", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "truth(stop()) or effect(x = 2)", "return", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let x: i32", "truth(stop()) and effect(x = 2)", "return", "x = 3", OwnershipFailure.ReassignedLet)]
    [InlineData("let s = \"s\"", "truth(stop()) and take(s@move)", "return", "Console.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let s = \"s\"", "c and truth(stopTake(s@move))", "return", "Console.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let s = \"s\"", "c or truth(stopTake(s@move))", "return", "Console.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("var x: i32", "c and (return)", "x = 2\n        return", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "c or (return)", "x = 2\n        return", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "c and (choice: do\n            if c => return\n            exit to choice: true\n        )", "x = 2\n        return", "let y = x", OwnershipFailure.UninitializedUse)]
    public void CheckingJoinsRetainAllOperandPaths(string declaration, string expression, string tail, string use, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Source(declaration, expression, tail, use) + Helpers);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("and")]
    [InlineData("or")]
    public void StoredLoansStayLiveThroughOperandChecking(string op)
    {
        var c = MinimalEmissionTest.Analyze(Source("var counter = Counter.init()\n    let r = counter@ref", "truth(stop()) " + op + " effect(counter.value = 2)", "return", "let n = r.value") + Helpers + "\nstruct Counter\n    public var value: i32 = 0");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData("and")]
    [InlineData("or")]
    public void DefaultLogicalDivergenceChecksAndNeverReturns(string op)
        => ScalarEmissionTest.EmitFixture("NeverLogicalTerminal" + Configuration + "Default" + op, "func value(c: bool, x: i32 = (do\n    var n = 1\n    do\n        let b = (if (loop => continue) => true else => false) " + op + " c\n        loop => continue\n    n\n)) -> i32 => x\nConsole.writeLine(\"begin\")\nvalue(true)", "begin\n", timeoutMilliseconds: 200);

    [Fact]
    public void ReloadedLogicalTerminalCheckingAllocatesNothingWhenWarm()
    {
        var c = MinimalEmissionTest.Analyze(Source("var x = 1", "truth(stop()) and effect(x = 2)", "return", "let y = x") + Helpers);
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

    private static string Source(string declaration, string expression, string tail, string use, bool condition = true)
        => "func stop() -> Never => $abort(\"stop\")\nfunc f(c: bool)\n    " + declaration + "\n    do\n        let b = " + expression + "\n        " + tail + "\n    " + use + "\nf(" + (condition ? "true" : "false") + ")";

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
