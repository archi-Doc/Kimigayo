// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Backend;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class StringResultEmissionTest
{
    private const string Choice = "var c = true\nlet result = if c => \"a\" else => \"b\"\nConsole.writeLine(result)";
    private const string SelfChoice = "var c = true\nvar text = \"old\"\ntext = if c => text@move else => \"new\"\nConsole.writeLine(text)";
    private const string Deferred = "var i = 0\nloop\n    defer\n        let result = if i == 1 => \"first\" else => \"later\"\n        Console.writeLine(result)\n    i += 1\n    if i == 3 => exit\n    continue";

    public static TheoryData<string, string, string, string> Fixtures => new()
    {
        { "StringResultTrue", Choice, "a\n", "a=1;b=0" },
        { "StringResultFalse", Choice.Replace("true", "false"), "b\n", "a=0;b=1" },
        { "StringResultEmpty", "Console.writeLine(if true => \"\" else => \"unused\")", "\n", "=1;unused=0" },
        { "StringResultDirectCall", "Console.writeLine(if false => \"a\" else => \"b\")", "b\n", "a=0;b=1" },
        { "StringResultNested", "let result = if true => (if false => \"a\" else => \"b\") else => \"c\"\nConsole.writeLine(result)", "b\n", "a=0;b=1;c=0" },
        { "StringResultDo", "let text = label work: do\n    exit to work \"a\"\nConsole.writeLine(text)", "a\n", "a=1" },
        { "StringResultTrailing", "let text = do => \"a\"\nConsole.writeLine(text)", "a\n", "a=1" },
        { "StringResultElseIf", "var n = 2\nlet text = if n == 1 => \"a\" else if n == 2 => \"b\" else => \"c\"\nConsole.writeLine(text)", "b\n", "a=0;b=1;c=0" },
        { "StringResultYield", "let text = label choice: if true\n    loop => yield to choice \"a\"\nelse => \"b\"\nConsole.writeLine(text)", "a\n", "a=1;b=0" },
        { "StringResultLoop", "var i = 0\nlet text = loop\n    i += 1\n    if i < 3 => continue\n    exit \"a\"\nConsole.writeLine(text)", "a\n", "a=1" },
        { "StringResultRepeated", "var i = 0\nwhile i < 3\n    let text = if i == 0 => \"first\" else => \"later\"\n    Console.writeLine(text)\n    i += 1", "first\nlater\nlater\n", "first=1;later=2" },
        { "StringResultSelfTrue", SelfChoice, "old\n", "old=1;new=0" },
        { "StringResultSelfFalse", SelfChoice.Replace("true", "false"), "new\n", "old=1;new=1" },
        { "StringResultSnapshot", "var text = \"old\"\nlet result = label work: do\n    defer => text = \"new\"\n    exit to work text@move\nConsole.writeLine(result)\nConsole.writeLine(text)", "old\nnew\n", "old=1;new=1" },
        { "StringResultLocalMove", "var text = \"local\"\nlet result = if true => text@move else => \"other\"\nConsole.writeLine(result)", "local\n", "local=1;other=0" },
        { "StringResultDeferred", Deferred, "first\nlater\nlater\n", "first=1;later=2" },
        { "StringResultDrop", "let text = if true => \"a\" else => \"b\"", string.Empty, "a=1;b=0" },
        { "StringResultDead", "if false\n    let text = if true => \"a\" else => \"b\"\nConsole.writeLine(\"ok\")", "ok\n", "a=0;b=0;ok=1" },
        { "StringResultOutward", "let text = label outer: do\n    let result = label work: do\n        exit to work (if false => \"a\" else => exit to outer \"b\")\n    exit to outer result@move\nConsole.writeLine(text)", "b\n", "a=0;b=1" },
        { "StringResultReplacementOrder", "var text = \"old\"\ntext = label work: do\n    defer => Console.writeLine(\"cleanup\")\n    exit to work \"new\"\nConsole.writeLine(text)", "cleanup\nnew\n", "old=1;new=1;cleanup=1" },
        { "StringResultPartialDivergence", "let text = if true => \"a\" else\n    defer => loop => ()\n    yield \"b\"\nConsole.writeLine(text)", "a\n", "a=1;b=0" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ResultOwnershipFollowsNormalArrivals(string name, string source, string stdout, string destructions)
    {
        var ir = ScalarEmissionTest.EmitFixture(name, source, stdout);
        StringEmissionTest.WriteAuditedFixture(name, ir, stdout, destructions);
    }

    [Fact]
    public void SecuredResultIsNotDestroyedWhenCleanupAborts()
    {
        const string Source = "var text = \"old\"\ntext = label work: do\n    defer\n        var n = 2147483647\n        n += 1\n    exit to work \"secured\"";
        const string Error = "Hello.kimi:5:9: abort KIMI_E_INT_OVERFLOW: Integer overflow\n";
        var ir = ScalarEmissionTest.EmitFixture("StringResultAbort", Source, string.Empty, 1, Error);
        StringEmissionTest.WriteAuditedFixture("StringResultAbort", ir, string.Empty, "old=0;secured=0", 1, Error);
    }

    [Fact]
    public void AbortBeforeAcquisitionDoesNotInitializeAResult()
    {
        const string Source = "let text = if 2147483647 + 1 > 0 => \"a\" else => \"b\"";
        const string Error = "Hello.kimi:1:15: abort KIMI_E_INT_OVERFLOW: Integer overflow\n";
        var ir = ScalarEmissionTest.EmitFixture("StringResultAbortBefore", Source, string.Empty, 1, Error);
        StringEmissionTest.WriteAuditedFixture("StringResultAbortBefore", ir, string.Empty, "a=0;b=0", 1, Error);
    }

    [Fact]
    public void DivergentCleanupHasNoArrivalOrResultDestruction()
    {
        const string Source = "let text = label work: do\n    defer => loop => ()\n    exit to work \"secured\"";
        ScalarEmissionTest.EmitFixture("StringResultDivergent", Source, string.Empty, observationMilliseconds: 300);
    }

    [Theory]
    [InlineData("let text = if true => \"a\"")]
    [InlineData("if false\n    let text = if true => \"a\"")]
    [InlineData("func f() -> string => if true => \"a\" else => " + MinimalEmissionTest.FloatExpression + "\nConsole.writeLine(\"ok\")")]
    [InlineData("let text = if true => \"a\" else => \"b\"\n_ = text@move\nConsole.writeLine(text)")]
    public void InvalidAndOutOfScopeResultsPublishNoIr(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var writer = new StringWriter();
        Assert.False(c.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmResultAnalysisAndWritingAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Deferred);
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
