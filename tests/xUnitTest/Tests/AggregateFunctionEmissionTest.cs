// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class AggregateFunctionEmissionTest
{
    private const string Echo = "func echo(value: (string, i32)) -> (string, i32) => value@move\n";

    public static TheoryData<string, string, string, string> Fixtures => new()
    {
        { "Echo", Echo + "let saved = echo((\"a\", 1))", string.Empty, "a=1" },
        { "Nested", Echo + "echo(echo((\"a\", 1)))", string.Empty, "a=1" },
        { "Replace", Echo + "var pair = (\"a\", 1)\npair = echo(pair@move)\npair = echo((\"b\", 2))", string.Empty, "a=1;b=1" },
        { "Loop", Echo + "var i = 0\nwhile i < 3\n    echo((\"a\", i))\n    i += 1", string.Empty, "a=3" },
        { "Forward", Echo + "func forward(value: (string, i32)) -> (string, i32)\n    defer => Console.writeLine(\"cleanup\")\n    return echo(value@move)\nforward((\"a\", 1))", "cleanup\n", "a=1;cleanup=1" },
        { "Conditional", Echo + "func maybe(value: (string, i32), flag: bool)\n    if flag => echo(value@move)\nmaybe((\"a\", 1), true)\nmaybe((\"b\", 2), false)", string.Empty, "a=1;b=1" },
        { "Recursion", "func repeat(value: (string, i32), n: i32) -> (string, i32)\n    if n == 0 => return value@move\n    return repeat(value@move, n - 1)\nrepeat((\"a\", 1), 3)", string.Empty, "a=1" },
        { "Selection", "func choose(a: (string, i32), b: (string, i32), flag: bool) -> (string, i32)\n    return if flag => a@move else => b@move\nchoose((\"a\", 1), (\"b\", 2), true)\nchoose((\"a\", 1), (\"b\", 2), false)", string.Empty, "a=2;b=2" },
        { "Array", "func echo(value: [2 of string]) -> [2 of string] => value@move\nlet array: [2 of string] = [\"a\", \"b\"]\necho(array@move)", string.Empty, "a=1;b=1" },
        { "NestedArray", "func echo(value: ([2 of string], (i128, string))) -> ([2 of string], (i128, string)) => value@move\nlet array: [2 of string] = [\"a\", \"b\"]\nlet value: ([2 of string], (i128, string)) = (array@move, (42, \"c\"))\necho(value@move)", string.Empty, "a=1;b=1;c=1" },
        { "Copy", "func echo(value: (i128, u8, f64, char)) -> (i128, u8, f64, char) => value\nlet value: (i128, u8, f64, char) = (42, 200, 2.5, 'a')\necho(value)\necho(value)", string.Empty, string.Empty },
        { "Zero", "func echo(value: [0 of string]) -> [0 of string] => value@move\nlet empty: [0 of string] = []\necho(echo(empty@move))", string.Empty, string.Empty },
        { "UnitArray", "func echo(value: [2 of ()]) -> [2 of ()] => value\nlet value: [2 of ()] = [(), ()]\necho(value)\necho(value)", string.Empty, string.Empty },
        { "Mixed", "func choose(! first => a: (string, i32), gap: (), z: [0 of string], last => b: (string, i32), n: i8) -> (string, i32)\n    if n == -7 => return b@move\n    return a@move\nlet empty: [0 of string] = []\nchoose(last: (\"b\", 2), n: -7, z: empty@move, gap: Console.writeLine(\"gap\"), first: (\"a\", 1))", "gap\n", "a=1;b=1;gap=1" },
        { "Shared", "func choose(text: ref/string, other: ref/string, value: (string, i32)) -> (string, i32)\n    if text == other => return value@move\n    return (\"bad\", 0)\nlet text = \"test\"\nchoose(text, text, (\"a\", 1))\nConsole.writeLine(text)", "test\n", "a=1;bad=0;test=1" },
        { "Abandoned", Echo + "func take(value: (string, i32), gap: ()) => ()\nfunc f()\n    take(echo((\"a\", 1)), (return))\nf()", string.Empty, "a=1" },
        { "NeverArgument", Echo + "func f()\n    echo((return))\nf()\nConsole.writeLine(\"done\")", "done\n", "done=1" },
        { "Payload", Echo + "let nested = (echo((\"a\", 1)), \"b\")", string.Empty, "a=1;b=1" },
        { "Match", Echo + "func f() -> (string, i32)\n    return match true\n        _ => echo((\"a\", 1))\n        true => echo((\"b\", 2))\nf()", string.Empty, "a=1;b=0" },
        { "LoopResult", Echo + "func f() -> (string, i32)\n    return loop => exit echo((\"a\", 1))\nf()", string.Empty, "a=1" },
        { "Deferred", Echo + "var n = 0\nloop\n    defer => echo((\"a\", 1))\n    n += 1\n    if n < 3 => continue\n    exit", string.Empty, "a=3" },
        { "ArrayReturn", "func make() -> [2 of string] => [\"a\", \"b\"]\nlet value = make()", string.Empty, "a=1;b=1" },
        { "ZeroReturn", "func make() -> [0 of string] => []\nmake()", string.Empty, string.Empty },
        { "ParameterLoop", Echo + "func f(value: (string, i32), flag: bool)\n    var n = 0\n    loop\n        n += 1\n        if n < 3 => continue\n        if flag => echo(value@move)\n        exit\nf((\"a\", 1), true)\nf((\"b\", 2), false)", string.Empty, "a=1;b=1" },
        { "Unused", Echo + "Console.writeLine(\"done\")", "done\n", "done=1" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void WholeValuesCrossFunctionBoundaries(string name, string source, string stdout, string destruction)
    {
        name = "AggregateFunction" + name;
        var ir = ScalarEmissionTest.EmitFixture(name, source, stdout);
        if (destruction.Length != 0)
        {
            StringEmissionTest.WriteAuditedFixture(name, ir, stdout, destruction);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NamedArgumentAcquisitionAndParameterCleanupHaveDifferentOrders(bool transfer)
    {
        var source = "func take(first: (string, i32), second: (string, i32), gap: ()) => ()\nfunc f()\n    take(second: (\"second\", 2), first: (\"first\", 1), gap: " + (transfer ? "(return)" : "()") + ")\nf()";
        var name = "AggregateFunctionOrder" + transfer;
        var ir = ScalarEmissionTest.EmitFixture(name, source, string.Empty);
        int[] order = transfer ? [0, 1] : [1, 0];
        StringEmissionTest.WriteAuditedFixture(name, ir, string.Empty, "first=1;second=1", order: order);
    }

    [Fact]
    public void ReturnSecuresTheValueBeforeDeferredReplacement()
    {
        const string Source = "func f() -> (string, i32)\n    var value = (\"before\", 1)\n    defer => value = (\"after\", 2)\n    return value@move\nf()";
        var ir = ScalarEmissionTest.EmitFixture("AggregateFunctionSnapshot", Source, string.Empty);
        StringEmissionTest.WriteAuditedFixture("AggregateFunctionSnapshot", ir, string.Empty, "before=1;after=1", order: [1, 0]);
    }

    [Theory]
    [InlineData("Argument", "func f(value: (string, i32), n: i32) => ()\nf((\"held\", 1), 2147483647 + 1)", 2, 16)]
    [InlineData("Body", "func f(value: (string, i32)) -> (string, i32)\n    var n = 2147483647\n    n += 1\n    return value@move\nf((\"held\", 1))", 3, 5)]
    [InlineData("Return", "func f(value: (string, i32)) -> (string, i32)\n    defer\n        var n = 2147483647\n        n += 1\n    return value@move\nf((\"held\", 1))", 4, 9)]
    public void AbortDoesNotUnwindOrDeliver(string name, string source, int line, int column)
    {
        var stderr = $"Hello.kimi:{line}:{column}: abort KIMI_E_INT_OVERFLOW: Integer overflow\n";
        name = "AggregateFunctionAbort" + name;
        var ir = ScalarEmissionTest.EmitFixture(name, source, string.Empty, 1, stderr);
        StringEmissionTest.WriteAuditedFixture(name, ir, string.Empty, "held=0", 1, stderr);
    }

    [Fact]
    public void DivergentCleanupCannotReturnOrForwardTheCallersResultSlot()
    {
        const string Source = Echo + "func f(value: (string, i32)) -> (string, i32)\n    defer => loop => ()\n    return echo(value@move)\nf((\"held\", 1))";
        var ir = ScalarEmissionTest.EmitFixture("AggregateFunctionDivergent", Source, string.Empty, observationMilliseconds: 300);
        GeneratedIr.Parse(ir).AssertNoTerminationClaim();
    }

    [Fact]
    public void ZeroByteArgumentsAndResultsKeepLogicalOwnershipWithoutStorage()
    {
        const string Source = "func echo(value: [0 of string]) -> [0 of string] => value@move\nlet empty: [0 of string] = []\necho(echo(empty@move))";
        CompilationTestHelper.WriteIr(MinimalEmissionTest.Analyze(Source));
        var invalid = MinimalEmissionTest.Analyze(Source + "\necho(empty@move)");
        Assert.False(invalid.Analysis.IsVerified);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmBorrowedAndAggregateFunctionPreparationAllocatesNothing()
    {
        const string Source = "func f(text: ref/string, value: (string, i32)) -> (string, i32) => value@move\nvar n = 0\nlet text = \"text\"\nloop\n    defer => f(text, (\"a\", n))\n    n += 1\n    if n < 3 => continue\n    exit";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Analyze().IsVerified);
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
        var success = true;
        var bindingBytes = AllocationMeasurement.Measure(() => success &= c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        var bytes = AllocationMeasurement.Measure(() =>
        {
            success &= c.Analyze().IsVerified;
            success &= c.Generate(TextWriter.Null);
        });
        Assert.True(success);
        Assert.Equal(0, bindingBytes);
        Assert.Equal(0, bytes);
    }
}
