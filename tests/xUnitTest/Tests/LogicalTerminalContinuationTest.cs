// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class LogicalTerminalContinuationTest
{
    private const string Helpers = "\nfunc truth(x: i32) -> bool => true";

    [Theory]
    [InlineData("LeftAnd", "truth(stop()) and truth(2)", true, true)]
    [InlineData("LeftOr", "truth(stop()) or truth(2)", true, true)]
    [InlineData("RightAndSkip", "c and truth(stop())", false, false)]
    [InlineData("RightAndAbort", "c and truth(stop())", true, true)]
    [InlineData("RightOrSkip", "c or truth(stop())", true, false)]
    [InlineData("RightOrAbort", "c or truth(stop())", false, true)]
    [InlineData("Both", "truth(stop()) and truth(stop())", true, true)]
    [InlineData("Nested", "(truth(stop()) and truth(2)) or truth(3)", true, true)]
    public void AbsentOperandsNeverCreateRuntimeResults(string name, string expression, bool condition, bool abort)
        => ScalarEmissionTest.EmitFixture("NeverLogicalTerminal" + Configuration + name, Source(expression, "return", condition) + "\nConsole.writeLine(\"done\")" + Helpers, abort ? string.Empty : "done\n", abort ? 1 : 0, abort ? "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n" : string.Empty);

    [Theory]
    [InlineData("AndReturn", "c and (return)", true, "done\n")]
    [InlineData("AndSkipReturn", "c and (return)", false, "tail\ndone\n")]
    [InlineData("OrReturn", "c or (return)", false, "done\n")]
    [InlineData("OrSkipReturn", "c or (return)", true, "tail\ndone\n")]
    public void RightTransferOnlyRunsOnTheEvaluatedPath(string name, string expression, bool condition, string stdout)
        => ScalarEmissionTest.EmitFixture("NeverLogicalTerminal" + Configuration + name, Source(expression, "Console.writeLine(\"tail\")\n        return", condition) + "\nConsole.writeLine(\"done\")", stdout);

    [Theory]
    [InlineData("and")]
    [InlineData("or")]
    public void DefaultLogicalDivergenceNeverReturns(string op)
        => ScalarEmissionTest.EmitFixture("NeverLogicalTerminal" + Configuration + "Default" + op, "func value(c: bool, x: i32 = (do\n    do\n        let b = (if (loop => continue) => true else => false) " + op + " true\n        loop => continue\n    1\n)) -> i32 => x\nConsole.writeLine(\"begin\")\nvalue(true)", "begin\n", timeoutMilliseconds: 200);

    private static string Source(string expression, string tail, bool condition)
        => "func stop() -> Never => $abort(\"stop\")\nfunc f(c: bool)\n    do\n        let b = " + expression + "\n        " + tail + "\nf(" + (condition ? "true" : "false") + ")";

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
