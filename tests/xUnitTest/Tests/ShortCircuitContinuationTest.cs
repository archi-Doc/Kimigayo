// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class ShortCircuitContinuationTest
{
    [Theory]
    [InlineData("AndSkip", "and", false, "", 1)]
    [InlineData("AndEvaluate", "and", true, "probe\ndone\n", 0)]
    [InlineData("OrSkip", "or", true, "done\n", 0)]
    [InlineData("OrEvaluate", "or", false, "probe\ndone\n", 0)]
    public void RuntimeShortCircuitEvaluationIsPreserved(string name, string op, bool condition, string stdout, int exit)
    {
        var source = "func stop() -> Never => $abort(\"stop\")\nfunc f(c: bool)\n    var x: i32\n    do\n        loop\n            if c " + op + " probe()\n                x = 1\n                return\n            else => exit\n        x = 2\n        stop()\nf(" + (condition ? "true" : "false") + ")" +
            "\nConsole.writeLine(\"done\")\nfunc probe() -> bool\n    Console.writeLine(\"probe\")\n    return true";
        ScalarEmissionTest.EmitFixture("NeverShortCircuit" + Configuration + name, source, stdout, exit, exit == 0 ? string.Empty : "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");
    }

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
