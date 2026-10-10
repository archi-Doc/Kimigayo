// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class ConditionOperandContinuationTest
{
    private const string Stop = "func stop() -> Never => $abort(\"stop\")\n";

    [Theory]
    [InlineData("NoElse", "if stop() => Console.writeLine(\"bad\")\nConsole.writeLine(\"after\")")]
    [InlineData("ElseIf", "func f(c: bool)\n    if c => return\n    else if stop() => Console.writeLine(\"bad\")\n    else => Console.writeLine(\"bad\")\n    Console.writeLine(\"after\")\nf(false)")]
    public void MissingConditionsNeverExecuteSourceBranches(string name, string source)
        => ScalarEmissionTest.EmitFixture("NeverCondition" + Configuration + name, Stop + source, string.Empty, 1, "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
