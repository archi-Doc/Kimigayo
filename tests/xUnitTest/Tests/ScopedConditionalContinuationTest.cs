// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class ScopedConditionalContinuationTest
{
    private const string Default = "func value(c: bool, y: i32 = (label scope: do\n    if c\n        loop => continue\n    else\n        loop => continue\n    exit to scope 0\n)) -> i32 => y\n";
    private const string MissingConditionDefault = "func value(c: bool, y: i32 = (do => if (loop => continue) => 1 else => 2)) -> i32 => y\n";
    private const string Stop = "func stop() -> Never => $abort(\"stop\")\n";

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void OmittedDefaultDoesNotComplete(string condition)
        => ScalarEmissionTest.EmitFixture("NeverScopedConditional" + Configuration + condition, Default + "Console.writeLine(\"begin\")\nvalue(" + condition + ")\nConsole.writeLine(\"after\")", "begin\n", timeoutMilliseconds: 200);

    [Theory]
    [InlineData("BothBranches", "do\n    if stop() => Console.writeLine(\"yes\") else => Console.writeLine(\"no\")\nConsole.writeLine(\"after\")")]
    [InlineData("NoElse", "do\n    if stop() => ()\nConsole.writeLine(\"after\")")]
    [InlineData("Borrow", "func inspect(s: ref/string, b: bool) => ()\nvar s = \"s\"\ninspect(s, do => if stop() => true else => false)\nConsole.writeLine(\"after\")")]
    public void ScopedMissingConditionsHaveNoRuntimeSuccessor(string name, string source)
        => ScalarEmissionTest.EmitFixture("NeverScopedCondition" + Configuration + name, Stop + source, string.Empty, 1, "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Fact]
    public void ScopedDefaultConditionDoesNotInventACallerResult()
        => ScalarEmissionTest.EmitFixture("NeverScopedCondition" + Configuration + "Default", MissingConditionDefault + "Console.writeLine(\"begin\")\nvalue(true)\nConsole.writeLine(\"after\")", "begin\n", timeoutMilliseconds: 200);

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
