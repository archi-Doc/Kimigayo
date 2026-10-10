// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class RequireContinuationTest
{
    private const string Stop = "func stop() -> Never => $abort(\"stop\")\n";
    private const string Counter = "\nstruct Counter\n    public var value: i32 = 0";
    private const string Default = "func value(c: bool, y: i32 = (do\n    var n: i32\n    do\n        require c else\n            n = 1\n            loop => continue\n        n = 2\n        loop => continue\n    0\n)) -> i32 => y\n";

    [Theory]
    [InlineData("Return", "var x: i32", "require c else\n            x = 1\n            return", "x = 2\n        stop()", false, "done\n")]
    [InlineData("Success", "var x: i32", "require c else\n            x = 1\n            return", "x = 2\n        return", true, "done\n")]
    [InlineData("Direct", "var x = 1", "require c else => return", "x = 2\n        return", false, "done\n")]
    [InlineData("Nested", "var x: i32", "require c else\n            x = 1\n            if c => return\n            return", "x = 2\n        return", false, "done\n")]
    [InlineData("Chain", "var x = 1", "require c else => return\n        require c else => return", "return", true, "done\n")]
    [InlineData("Move", "let s = \"s\"", "require c else\n            Console.writeLine(s)\n            return", "return", false, "s\ndone\n")]
    [InlineData("Loan", "var counter = Counter.init()\n    let r = counter@ref", "require c else => return", "return", false, "done\n")]
    public void FailureAndSuccessPathsExecute(string name, string declaration, string require, string tail, bool condition, string stdout)
        => ScalarEmissionTest.EmitFixture("NeverRequire" + Configuration + name, Source(declaration, require, tail, "()", condition) + "\nConsole.writeLine(\"done\")" + Counter, stdout);

    [Fact]
    public void SuccessAbortsWithoutExecutingLaterSource()
        => ScalarEmissionTest.EmitFixture("NeverRequire" + Configuration + "Abort", Source("var x: i32", "require c else\n            x = 1\n            return", "x = 2\n        stop()", "Console.writeLine(\"bad\")", true), string.Empty, 1, "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Theory]
    [InlineData("Exit", "exit", false)]
    [InlineData("Return", "return", true)]
    public void FailureTransfersKeepTheirOriginalTarget(string name, string transfer, bool condition)
        => ScalarEmissionTest.EmitFixture("NeverRequire" + Configuration + name + "Target", Stop + "func f(c: bool)\n    var x: i32\n    do\n        loop\n            require c else\n                x = 1\n                " + transfer + "\n            exit\n        x = 2\n        return\nf(" + (condition ? "true" : "false") + ")\nConsole.writeLine(\"done\")", "done\n");

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void DefaultRequireDoesNotCreateRuntimeCompletion(string condition)
        => ScalarEmissionTest.EmitFixture("NeverRequire" + Configuration + "Default" + condition, Default + "Console.writeLine(\"begin\")\nvalue(" + condition + ")", "begin\n", timeoutMilliseconds: 200);

    [Fact]
    public void DefaultFailureCanDeliverToItsEnclosingScope()
        => ScalarEmissionTest.EmitFixture("NeverRequire" + Configuration + "DefaultResult", "func value(c: bool, y: i32 = (label choice: do\n    require c else => exit to choice 4\n    exit to choice 7\n)) -> i32 => y\nrequire value(false) == 4 else => $abort(\"failure\")\nrequire value(true) == 7 else => $abort(\"success\")\nrequire value(false, 9) == 9 else => $abort(\"supplied\")\nConsole.writeLine(\"done\")", "done\n");

    [Fact]
    public void FailureFactsDoNotFlowIntoTheSuccessor()
    {
        var c = MinimalEmissionTest.Analyze(Stop + "func f(c: bool)\n    var x: i32\n    require c else\n        x = 1\n        return\n    let y = x\nf(true)");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.UninitializedUse);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    private static string Source(string declaration, string require, string tail, string use, bool condition)
        => Stop + "func f(c: bool)\n    " + declaration + "\n    do\n        " + require + "\n        " + tail + "\n    " + use + "\nf(" + (condition ? "true" : "false") + ")";

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
