// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class PartialScopeContinuationTest
{
    private const string Stop = "func stop() -> Never => $abort(\"stop\")\n";
    private const string Default = "func value(c: bool, y: i32 = (do\n    var n: i32\n    do\n        if c\n            n = 1\n            loop => continue\n        n = 2\n        loop => continue\n    0\n)) -> i32 => y\n";

    [Theory]
    [InlineData("Initialized", "var x: i32", "x = 1\n            return", "x = 2\n        stop()", "Console.writeLine(\"after\")", "done\n")]
    [InlineData("Nested", "var x: i32", "if c => x = 1 else => x = 3\n            return", "x = 2\n        stop()", "Console.writeLine(\"after\")", "done\n")]
    [InlineData("Deep", "var x: i32", "x = 1\n            if c => return\n            stop()", "x = 2\n        stop()", "Console.writeLine(\"after\")", "done\n")]
    [InlineData("MoveBefore", "let s = \"s\"", "Console.writeLine(s)\n            _ = s@move\n            return", "Console.writeLine(s)\n        _ = s@move\n        stop()", "()", "s\ndone\n")]
    public void ReturnPathsExecuteWithoutLaterSource(string name, string declaration, string yes, string tail, string use, string stdout)
        => ScalarEmissionTest.EmitFixture("NeverPartialScope" + Configuration + name, Source(declaration, yes, tail, use) + "\nConsole.writeLine(\"done\")", stdout);

    [Fact]
    public void BranchBorrowsEndBeforeEveryTerminalPath()
        => ScalarEmissionTest.EmitFixture(
            "NeverPartialScope" + Configuration + "Borrow",
            "func inspect(s: ref/string) => ()\n" + Source("var s = \"s\"", "inspect(s)\n            return", "inspect(s)\n        stop()", "Console.writeLine(\"after\")") + "\nConsole.writeLine(\"done\")",
            "done\n");

    [Fact]
    public void LaterTerminationAbortsWithoutRunningSuccessors()
        => ScalarEmissionTest.EmitFixture(
            "NeverPartialScope" + Configuration + "Abort",
            Source("var x: i32", "x = 1\n            return", "x = 2\n        stop()", "Console.writeLine(\"bad\")").Replace("f(true)", "f(false)", StringComparison.Ordinal),
            string.Empty,
            1,
            "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void OmittedPartialDefaultDoesNotComplete(string condition)
        => ScalarEmissionTest.EmitFixture(
            "NeverPartialScope" + Configuration + "Default" + condition,
            Default + "Console.writeLine(\"begin\")\nvalue(" + condition + ")\nConsole.writeLine(\"after\")",
            "begin\n",
            timeoutMilliseconds: 200);

    private static string Source(string declaration, string yes, string tail, string use)
        => Stop + "func f(c: bool)\n    " + declaration + "\n    do\n        if c\n            " + yes + "\n        " + tail + "\n    " + use + "\nf(true)";

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
