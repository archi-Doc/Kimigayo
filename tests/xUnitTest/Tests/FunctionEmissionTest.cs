// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class FunctionEmissionTest
{
    private const string Snapshot = "func result() -> i32\n    var x = 1\n    defer => x = 2\n    return x\nif result() == 1 => Console.writeLine(\"ok\")";

    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "FunctionSimple", "func add(a: i32, b: i32) -> i32 => a + b\nif add(1, 2) == 3 => Console.writeLine(\"ok\")", "ok\n" },
        { "FunctionMain", "public func main() -> ()\n    defer => Console.writeLine(\"end\")\n    Console.writeLine(\"body\")\n    return\n    Console.writeLine(\"bad\")", "body\nend\n" },
        { "FunctionLocal", "public func main()\n    func twice(x: i32) -> i32 => x * 2\n    if twice(3) == 6 => Console.writeLine(\"ok\")", "ok\n" },
        { "FunctionNamed", "func pair(! left => v3: i32, right => p3: i32) -> i32 => v3 * 10 + p3\nvar x = 1\nif pair(right: x++, left: x++) == 21 and x == 3 => Console.writeLine(\"ok\")", "ok\n" },
        { "FunctionNested", "func add(a: i32, b: i32) -> i32 => a + b\nvar x = 1\nlet y = add(x++, add(x++, x++))\nif y == 6 and x == 4 => Console.writeLine(\"ok\")", "ok\n" },
        { "FunctionRecursion", "func fact(n: i32) -> i32\n    if n == 0 => return 1\n    return n * fact(n - 1)\nif fact(6) == 720 => Console.writeLine(\"ok\")", "ok\n" },
        { "FunctionMutual", "func even(n: i32) -> bool => if n == 0 => true else => odd(n - 1)\nfunc odd(n: i32) -> bool => if n == 0 => false else => even(n - 1)\nif even(10) and odd(9) => Console.writeLine(\"ok\")", "ok\n" },
        { "FunctionSnapshot", Snapshot, "ok\n" },
        { "FunctionUnit", "func f(a: (), b: i32, c: ()) -> i32 => b\nif f(Console.writeLine(\"first\"), 7, Console.writeLine(\"last\")) == 7 => Console.writeLine(\"ok\")", "first\nlast\nok\n" },
        { "FunctionBool", "func opposite(b3: bool) -> bool => not b3\nvar x = opposite(false)\nif opposite(opposite(x)) => Console.writeLine(\"ok\")", "ok\n" },
        { "FunctionReturnIf", "func choose(c: bool) -> i32\n    defer => Console.writeLine(\"end\")\n    return if c => 21 / 2 else => 3 % 2\nif choose(true) == 10 and choose(false) == 1 => Console.writeLine(\"ok\")", "end\nend\nok\n" },
        { "FunctionOverload", "func f(x: i32) -> i32 => x\nfunc f(x: bool) -> bool => x\nif f(1) == 1 and f(true) => Console.writeLine(\"ok\")", "ok\n" },
        { "FunctionUnused", "func unused() => 123\npublic func main() => Console.writeLine(\"ok\")", "ok\n" },
        { "FunctionSkipped", "func zero() -> i32 => 1 / 0\nif false => zero()\nif true or zero() == 0 => Console.writeLine(\"ok\")", "ok\n" },
        { "FunctionTransferArgument", "func add(a: i32, b: i32) -> i32 => a + b\nlet x = label outer: do\n    defer => Console.writeLine(\"end\")\n    add(1, (label inner: do => exit to outer 7))\nif x == 7 => Console.writeLine(\"ok\")", "end\nok\n" },
        { "FunctionReturnOperand", "func f() -> i32\n    defer => Console.writeLine(\"end\")\n    return (label inner: do => return 7)\nif f() == 7 => Console.writeLine(\"ok\")", "end\nok\n" },
        { "FunctionNames", "func 日本語(v3: i32, p3: i32, b3: bool, checked3: i32, a0: i32) -> i32 => if b3 => v3 + p3 + checked3 + a0 else => 0\nif 日本語(1, 2, true, 3, 4) == 10 => Console.writeLine(\"ok\")", "ok\n" },
        { "FunctionDeferredCalls", "func tick() => Console.writeLine(\"tick\")\nfunc f(x: i32) -> i32\n    defer => tick()\n    if x == 1 => return x\n    return 2\nif f(1) + f(0) == 3 => Console.writeLine(\"ok\")", "tick\ntick\nok\n" },
        { "FunctionConditionalTransfer", "func f(a: i32, b: i32) -> i32 => a + b\nlet x = label outer: do\n    defer => Console.writeLine(\"end\")\n    exit to outer f(2, (if false => exit to outer 9 else => 3))\nif x == 5 => Console.writeLine(\"ok\")", "end\nok\n" },
        { "FunctionTransferThenArgument", "func f(a: i32, b: i32) -> i32 => a + b\nfunc side() -> i32\n    Console.writeLine(\"bad\")\n    return 1\nlet x = label outer: do\n    f((label inner: do => exit to outer 7), side())\nif x == 7 => Console.writeLine(\"ok\")", "ok\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EmitsFunctions(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture(name, source, stdout);

    [Theory]
    [InlineData("FunctionAbort", "func divide(x: i32) -> i32 => 1 / x\npublic func main()\n    defer => Console.writeLine(\"bad\")\n    divide(0)", "", 1, 31)]
    [InlineData("FunctionArgumentAbort", "func f(x: i32) => Console.writeLine(\"bad\")\ndefer => Console.writeLine(\"bad\")\nf(1 / 0)", "", 3, 3)]
    [InlineData("FunctionDeferredAbort", "func f() -> i32\n    defer => Console.writeLine(\"bad\")\n    defer => 1 / 0\n    return 7\nf()\nConsole.writeLine(\"bad\")", "", 3, 14)]
    public void AbortDoesNotReturnOrUnwind(string name, string source, string stdout, int line, int column)
        => ScalarEmissionTest.EmitFixture(name, source, stdout, 1, $"Hello.kimi:{line}:{column}: abort KIMI_E_INT_DIV_ZERO: Integer division or remainder by zero\n");

    [Theory]
    [InlineData("FunctionNever", "func spin() -> Never => loop => ()\npublic func main()\n    Console.writeLine(\"begin\")\n    spin()\n    Console.writeLine(\"bad\")")]
    [InlineData("FunctionNeverArgument", "func spin() -> Never => loop => ()\nfunc f(a: i32, b: i32) => Console.writeLine(\"bad\")\nConsole.writeLine(\"begin\")\nf(1, spin())")]
    [InlineData("FunctionDivergentReturn", "func f() -> i32\n    defer => loop => ()\n    return 1\nConsole.writeLine(\"begin\")\nf()\nConsole.writeLine(\"bad\")")]
    [InlineData("FunctionNeverFirstArgument", "func spin() -> Never => loop => ()\nfunc f(a: i32, b: i32) => Console.writeLine(\"bad\")\nfunc side() -> i32\n    Console.writeLine(\"bad\")\n    return 1\nConsole.writeLine(\"begin\")\nf(spin(), side())")]
    [InlineData("FunctionNeverExpression", "func spin() -> Never => loop => ()\nfunc f() -> i32 => spin()\nConsole.writeLine(\"begin\")\nf()")]
    public void NonterminationHasNoFabricatedReturn(string name, string source)
    {
        var ir = ScalarEmissionTest.EmitFixture(name, source, "begin\n", observationMilliseconds: 200);
        GeneratedIr.Parse(ir).AssertNoTerminationClaim();
    }

    [Theory]
    [InlineData("let x = 1\nfunc capture() -> i32 => x\ncapture()")]
    [InlineData("public func main()\n    let x = 1\n    func capture() -> i32 => x\n    capture()")]
    [InlineData("group G\n    func f() => ()\n()", true)]
    [InlineData("func f(x: i32 = 1) => ()\n()", true)]
    [InlineData("func f<T>() => ()\n()", true)]
    [InlineData("func f(x: uniq/string) => ()\n()", true)]
    [InlineData("func unused() -> ()\n    " + MinimalEmissionTest.FloatExpression + "\n()", true)]
    [InlineData("public func main() -> i32 => 0")]
    [InlineData("public func main() => ()\n()")]
    [InlineData("func spin() -> Never => loop => ()\nfunc f(a: i32, b: i32) => ()\nf(spin(), 2)", true)]
    [InlineData("func f(a: i32, b: i32) -> i32 => a + b\nlet y = label outer: do\n    f((label inner: do => exit to outer 7), 2)", true)]
    public void SelectedBodiesAndStartupRespectSupportedFeatures(string source, bool emitted = false)
    {
        var c = MinimalEmissionTest.Analyze(source);
        FloatEmissionTest.AssertEmissionSupport(c, emitted);
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmFunctionAnalysisAndWritingAllocateNothing(bool parameters)
    {
        var c = MinimalEmissionTest.Analyze(parameters ? "func f(a: i32, b: (), c: bool) -> i32 => if c => a else => 0\nvar x = 0\nwhile x < 100\n    x += f(c: true, b: (), a: 1)" : Snapshot);
        var valid = true;
        var allocated = AllocationMeasurement.Measure(() =>
        {
            valid &= c.Analyze().IsVerified;
            valid &= c.WriteIr(TextWriter.Null, out _);
        });

        Assert.Equal(0, allocated);
        Assert.True(valid);
    }
}
