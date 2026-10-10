// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class WhileConditionContinuationTest
{
    private const string Stop = "func stop() -> Never => $abort(\"stop\")\n";

    [Theory]
    [InlineData("Empty", "while stop() => ()\nConsole.writeLine(\"after\")")]
    [InlineData("Locals", "while stop()\n    var n = 1\n    Console.writeLine(\"body\")\nConsole.writeLine(\"after\")")]
    [InlineData("Borrow", "func inspect(s: ref/string) -> Never => stop()\nvar s = \"s\"\nwhile inspect(s) => ()\nConsole.writeLine(\"after\")")]
    [InlineData("Scoped", "do => while stop() => ()\nConsole.writeLine(\"after\")")]
    public void MissingWhileConditionHasNoBodyOrSuccessorExecution(string name, string source)
        => ScalarEmissionTest.EmitFixture("NeverWhileCondition" + Configuration + name, Stop + source, string.Empty, 1, "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Fact]
    public void DefaultWhileConditionHasNoCallerSuccessor()
        => ScalarEmissionTest.EmitFixture(
            "NeverWhileCondition" + Configuration + "Default",
            "func value(y: () = (while (loop => continue) => ())) => ()\nConsole.writeLine(\"begin\")\nvalue()\nConsole.writeLine(\"after\")",
            "begin\n",
            timeoutMilliseconds: 200);

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
