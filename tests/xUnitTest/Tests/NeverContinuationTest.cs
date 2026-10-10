// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class NeverContinuationTest
{
    private const string Stop = "func stop() -> Never => $abort(\"stop\")\n";

    [Theory]
    [InlineData("Nested", "var n: i32 = do => do => loop => continue\nConsole.writeLine(\"after\")")]
    [InlineData("Default", "func f(y: i32 = (do => loop => continue)) -> i32 => y\nvar n: i32 = f()\nConsole.writeLine(\"after\")")]
    public void TransparentDivergentScopesHaveNoRuntimeSuccessor(string name, string source)
        => ScalarEmissionTest.EmitFixture("NeverWrapper" + Configuration + name, "Console.writeLine(\"begin\")\n" + source, "begin\n", timeoutMilliseconds: 200);

    [Theory]
    [InlineData("stop() + 1", "i32")]
    [InlineData("stop() * wide", "i64")]
    [InlineData("stop() == 1", "bool")]
    [InlineData("stop() < wide", "bool")]
    public void ANonCompletingLeftOperandFitsTheOtherOperand(string expression, string type)
    {
        // SPEC 3.1.5, 13.3, 13.4: Never fits the other operand's Type; it imposes no expected Never on that operand.
        var c = MinimalEmissionTest.Analyze(Stop + "let wide: i64 = 2\nlet n: " + type + " = " + expression);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void ANonCompletingLeftOperandSkipsTheOperation()
        => ScalarEmissionTest.EmitFixture("NeverOperand" + Configuration + "Left", Stop + "Console.writeLine(\"begin\")\nlet n: i32 = stop() + 1\nConsole.writeLine(\"after\")", "begin\n", 1, "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Fact]
    public void DivergentOperatorSkipsCalleeAndLaterOutput()
        => ScalarEmissionTest.EmitFixture("NeverOperand" + Configuration + "Divergence", "Console.writeLine(\"begin\")\nvar n: i32 = 1 << (loop => continue)\nConsole.writeLine(\"after\")", "begin\n", timeoutMilliseconds: 200);

    [Fact]
    public void NoncompletingFirstArgumentSkipsLaterAcquisitionsAndCallee()
        => ScalarEmissionTest.EmitFixture(
            "NeverContinuation" + Configuration + "FirstArgument",
            "func spin() -> Never => loop => ()\nfunc f(a: i32, b: i32) => Console.writeLine(\"bad\")\nConsole.writeLine(\"begin\")\nf(spin(), 2)\nConsole.writeLine(\"bad\")",
            "begin\n",
            timeoutMilliseconds: 200);

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
