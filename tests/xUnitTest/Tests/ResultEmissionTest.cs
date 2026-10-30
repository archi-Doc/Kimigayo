// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class ResultEmissionTest
{
    internal const string Nested = "var i = 0\nwhile i < 1000000\n    let x = if i < 5 => (if true => i else => -1) else => i\n    if x != i => Console.writeLine(\"bad\")\n    i += 1\nif i == 1000000 => Console.writeLine(\"ok\")";

    public static TheoryData<string, string> Fixtures => new()
    {
        { "ResultIf", "var c = false\nlet x = if c => 1 else => 2\nif x == 2 => Console.writeLine(\"ok\")" },
        { "ResultElseIf", "var c = 2\nlet x = if c == 0 => 10 else if c == 1 => 20 else => 30\nif x == 30 => Console.writeLine(\"ok\")" },
        { "ResultBool", "let x = if false => false else => true\nif x and true => Console.writeLine(\"ok\")" },
        { "ResultYield", "let x = if true\n    yield 1\nelse\n    yield 2\nif x == 1 => Console.writeLine(\"ok\")" },
        { "ResultDo", "let x = do => 3\nif x == 3 => Console.writeLine(\"ok\")" },
        { "ResultDoExit", "let x = label work: do\n    exit to work 4\nif x == 4 => Console.writeLine(\"ok\")" },
        { "ResultLabeledDo", "let x = label work: do\n    if true => exit to work 5\n    exit to work 6\nif x == 5 => Console.writeLine(\"ok\")" },
        { "ResultLabeledIf", "let x = label choice: if true\n    if true => yield to choice 7\n    yield 8\nelse\n    yield 9\nif x == 7 => Console.writeLine(\"ok\")" },
        { "ResultLoop", "var i = 0\nlet x = loop\n    i += 1\n    if i < 3 => continue\n    exit i\nif x == 3 => Console.writeLine(\"ok\")" },
        { "ResultLoopExits", "var c = false\nlet x = loop\n    if c => exit 1\n    exit 2\nif x == 2 => Console.writeLine(\"ok\")" },
        { "ResultOuterLoop", "let x = label outer: loop\n    loop\n        exit to outer 3\nif x == 3 => Console.writeLine(\"ok\")" },
        { "ResultOneInput", "var c = true\nwhile true\n    let x = if c => 1 else => exit\n    if x == 1 => Console.writeLine(\"ok\")\n    exit" },
        { "ResultNestedLoop", Nested },
        { "ResultReadOrder", "var x = 2\nlet y = x + (if true => x++ else => 0)\nif y == 4 and x == 3 => Console.writeLine(\"ok\")" },
        { "ResultCheckedEdge", "var x = 3\nlet y = if true => x + 1 else => x - 1\nif y == 4 => Console.writeLine(\"ok\")" },
        { "ResultDead", "while true\n    exit\n    let x = if true => 1 else => 2\nConsole.writeLine(\"ok\")" },
        { "ResultNever", "while true\n    let x: i32 = if true => exit else => continue\nConsole.writeLine(\"ok\")" },
        { "ResultDeadLoop", "while true\n    exit\n    let x: i32 = loop => continue\nConsole.writeLine(\"ok\")" },
        { "ResultShortTransfer", "let x = label outer: do\n    let b = true and (label inner: do => exit to outer 1)\n    exit to outer 2\nif x == 1 => Console.writeLine(\"ok\")" },
        { "ResultShortSkipTransfer", "let x = label outer: do\n    let b = false and (label inner: do => exit to outer 1)\n    exit to outer 2\nif x == 2 => Console.writeLine(\"ok\")" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EmitsResultFixtures(string name, string source)
        => ScalarEmissionTest.EmitFixture(name, source, "ok\n");

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmNestedResultAnalysisAndWritingAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Nested);
        var success = true;
        var bytes = AllocationMeasurement.Measure(() =>
        {
            success &= c.Analyze().IsVerified;
            success &= c.WriteIr(TextWriter.Null, out _);
        });
        Assert.True(success);
        Assert.Equal(0, bytes);
    }

    [Theory]
    [InlineData("var x = 1\nif true\n    defer => x = 2\n    ()")]
    [InlineData("var x = 1\nlet y = label work: do\n    defer => x = 2\n    exit to work x")]
    public void DeferredCleanupUsesVerifiedExitPlans(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Analysis.IsVerified);
        var ir = CompilationTestHelper.WriteIr(c);
        Assert.NotEmpty(ir);
    }
}
