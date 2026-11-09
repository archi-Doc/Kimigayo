// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class WhileConditionContinuationTest
{
    private const string Stop = "func stop() -> Never => $abort(\"stop\")\n";

    // SPEC 14.9.1, 14.10.3 (the owner's 「SPEC's letter」; DECISIONS R4-M175): a while whose condition does not complete is a Never form;
    // its false path is an assumed one, which adds no implicit Unit, so the while initializes nothing it is placed into. An unread local
    // of Type Never has no record (fail-before: InternalInvariant_Kd, a Unit placed into Never); a read of it, or of a `()` local it
    // initializes, is UninitializedPlace_Kd (fail-before: InternalInvariant_Kd; accepted).
    [Theory]
    [InlineData("let result = while fail() => ()", "")]
    [InlineData("let result = while fail() => ()\n    let copy = result", "UninitializedPlace_Kd result")]
    [InlineData("let r: () = while fail() => ()\n    let s = r", "UninitializedPlace_Kd r")]
    public void ANeverWhileInitializesNothing(string statements, string records)
    {
        var c = MinimalEmissionTest.Analyze("func fail() -> Never => fail()\nfunc g()\n    " + statements + "\n", output: OutputKind.Library);
        Assert.Empty(c.Binding.Issues);
        Assert.Equal(records, MinimalEmissionTest.Problems(c));
    }

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
            observationMilliseconds: 200);

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
