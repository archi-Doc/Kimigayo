// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class ScopedContinuationTest
{
    private const string Stop = "func stop() -> Never => $abort(\"stop\")\n";
    private const string Default = "func value(y: i32 = (label scope: do\n    var n: i32 = loop => continue\n    exit to scope 1\n)) -> i32 => y\n";

    [Theory]
    [InlineData("Direct", "do => stop()\nConsole.writeLine(\"after\")")]
    [InlineData("DeadStatement", "do\n    stop()\n    Console.writeLine(\"dead\")\nConsole.writeLine(\"after\")")]
    [InlineData("Nested", "do\n    do => stop()\n    Console.writeLine(\"dead\")\nConsole.writeLine(\"after\")")]
    [InlineData("Borrow", "func inspect(s: ref/string) -> Never => stop()\nvar s = \"s\"\ndo => inspect(s)\nConsole.writeLine(\"after\")")]
    public void AbortingScopesHaveNoRuntimeSuccessor(string name, string source)
        => ScalarEmissionTest.EmitFixture("NeverScope" + Configuration + name, Stop + source, string.Empty, 1, "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Fact]
    public void OmittedScopedDefaultHasNoCallerSuccessor()
        => ScalarEmissionTest.EmitFixture("NeverScope" + Configuration + "Default", Default + "Console.writeLine(\"begin\")\nvalue()\nConsole.writeLine(\"after\")", "begin\n", timeoutMilliseconds: 200);

    [Fact]
    public void OuterTransferDoesNotExecuteLaterArgumentEffects()
        => ScalarEmissionTest.EmitFixture(
            "NeverScope" + Configuration + "OuterTransfer",
            "func f(a: i32, b: i32) -> i32 => a + b\nfunc g() -> i32\n    Console.writeLine(\"bad\")\n    return 2\nlet y = label outer: do\n    f((label inner: do => exit to outer 7), g())\nif y == 7 => Console.writeLine(\"ok\") else => Console.writeLine(\"bad\")",
            "ok\n");

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
