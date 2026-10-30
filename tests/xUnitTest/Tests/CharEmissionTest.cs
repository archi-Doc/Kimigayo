// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class CharEmissionTest
{
    private const string Guard = "func echo(c: char) -> char => c\nvar value = 'A'\nlet result = match echo(value)\n    'B' => 'x'\n    let candidate if candidate == 'A' => candidate\n    _ => '\\0'\nif result == value => Console.writeLine(\"ok\")";

    [Theory]
    [InlineData(0)]
    [InlineData(0x41)]
    [InlineData(0x301)]
    [InlineData(0x378)]
    [InlineData(0x212B)]
    [InlineData(0x3042)]
    [InlineData(0xD7FF)]
    [InlineData(0xE000)]
    [InlineData(0xFDD0)]
    [InlineData(0x1F600)]
    [InlineData(0x10FFFF)]
    public void UnicodeScalarsSurviveStorageCopyReplacementAndCalls(int scalar)
    {
        var literal = scalar == 0 ? "'\\0'" : $"'{char.ConvertFromUtf32(scalar)}'";
        var source = $"func echo(c: char) -> char => c\nvar c: char = {literal}\nlet copy = c\nc = '\\u(10FFFF)'\nc = copy\nif c == '\\u({scalar:X})' and echo(copy) == c => Console.writeLine(\"ok\")";
        var ir = ScalarEmissionTest.EmitFixture($"CharScalar{scalar:X}", source, "ok\n");
        Assert.Contains("alloca i32, align 4", ir);
        Assert.Contains($"store i32 {scalar}", ir);
        Assert.Contains(" = load i32, ptr ", ir);
        Assert.Contains("define internal i32 @__kimi_f", ir);
        Assert.Contains("call i32 @__kimi_f", ir);
    }

    [Theory]
    [InlineData("==", "eq", false, true)]
    [InlineData("!=", "ne", true, false)]
    [InlineData("<", "ult", true, false)]
    [InlineData("<=", "ule", true, true)]
    [InlineData(">", "ugt", false, false)]
    [InlineData(">=", "uge", false, true)]
    public void ComparisonsUseUnicodeScalarOrder(string op, string predicate, bool ordered, bool equal)
    {
        var reverse = op is "==" or "!=" ? ordered : !ordered;
        var source = $"func compare(a: char, b: char) -> bool => a {op} b\nlet a = '\\u(D7FF)'\nlet b = '\\u(E000)'\nif compare(a, b) == {ordered.ToString().ToLowerInvariant()} and compare(a, a) == {equal.ToString().ToLowerInvariant()} and compare(b, a) == {reverse.ToString().ToLowerInvariant()} => Console.writeLine(\"ok\")";
        var ir = ScalarEmissionTest.EmitFixture("CharCompare" + predicate, source, "ok\n");
        Assert.Contains("icmp " + predicate + " i32", ir);
    }

    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "CharIf", "var flag = false\nlet c = if flag => 'A' else => 'あ'\nif c == 'あ' => Console.writeLine(\"ok\")", "ok\n" },
        { "CharDo", "let c = label work: do\n    defer => Console.writeLine(\"cleanup\")\n    exit to work '😀'\nif c == '\\u(1F600)' => Console.writeLine(\"ok\")", "cleanup\nok\n" },
        { "CharLoop", "var n = 0\nlet c = loop\n    n += 1\n    if n < 3 => continue\n    if n == 3 => exit 'A'\n    exit 'B'\nif c == 'A' => Console.writeLine(\"ok\")", "ok\n" },
        { "CharReturnSnapshot", "func f(c: char) -> char\n    var value = c\n    defer => value = 'B'\n    return value\nif f('A') == 'A' => Console.writeLine(\"ok\")", "ok\n" },
        { "CharNamedCall", "func first() -> char\n    Console.writeLine(\"first\")\n    return 'A'\nfunc second() -> char\n    Console.writeLine(\"second\")\n    return 'B'\nfunc choose(a: char, b: char) -> char\n    defer => Console.writeLine(\"cleanup\")\n    return if a < b => a else => b\nif choose(b: second(), a: first()) == 'A' => Console.writeLine(\"ok\")", "second\nfirst\ncleanup\nok\n" },
        { "CharMatch", "let c = match '😀'\n    'A' => 'B'\n    '\\u(1F600)' => '😀'\n    _ => '\\0'\nif c == '😀' => Console.writeLine(\"ok\")", "ok\n" },
        { "CharMatchDuplicate", "match 'A'\n    '\\u(41)' => Console.writeLine(\"ok\")\n    'A' => Console.writeLine(\"bad\")\n    _ => ()", "ok\n" },
        { "CharMatchGuard", Guard, "ok\n" },
        { "CharFalseGuard", "func reject() -> bool\n    Console.writeLine(\"guard\")\n    return false\nmatch 'A'\n    'A' if reject() => Console.writeLine(\"bad\")\n    '\\u(41)' => Console.writeLine(\"ok\")\n    _ => ()", "guard\nok\n" },
        { "CharGuardSnapshot", "var value = 'A'\nlet snapshot = value\nmatch snapshot\n    let c if (label check: do\n        value = 'B'\n        exit to check false\n    ) => ()\n    let c if c == 'A'\n        var changed: char = c\n        changed = 'C'\n        if changed == 'C' and value == 'B' => Console.writeLine(\"ok\")\n    _ => ()", "ok\n" },
        { "CharMatchResultSnapshot", "var value = 'A'\nlet c = match value@move\n    let n\n        defer => value = 'B'\n        yield n\nif c == 'A' and value == 'B' => Console.writeLine(\"ok\")", "ok\n" },
        { "CharNormalization", "var c = 'Å'\nif c != 'Å' and c == '\\u(212B)' => Console.writeLine(\"ok\")", "ok\n" },
        { "CharArrayCopy", "let a: [2 of char] = ['A', '😀']\nlet b = a\nlet c = a\nConsole.writeLine(\"ok\")", "ok\n" },
        { "CharTupleCopy", "var a = ('A', (1, '😀'))\nlet b = a\na = b\nlet c = a\nConsole.writeLine(\"ok\")", "ok\n" },
        { "CharAggregateOrder", "func first() -> char\n    Console.writeLine(\"first\")\n    return 'A'\nfunc second() -> char\n    Console.writeLine(\"second\")\n    return 'B'\nlet a = (first(), second())\nlet b: [2 of char] = [second(), first()]", "first\nsecond\nsecond\nfirst\n" },
        { "CharAbruptArgument", "func take(a: char, b: char) -> char => b\nlet result = label outer: do\n    defer => Console.writeLine(\"cleanup\")\n    take('A', (label inner: do => exit to outer '😀'))\nif result == '😀' => Console.writeLine(\"ok\")", "cleanup\nok\n" },
        { "CharCoveredArm", "match 'A'\n    _ => Console.writeLine(\"ok\")\n    'B'\n        let c = if true => 'あ' else => '😀'", "ok\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ExistingControlFlowAndAggregatePathsExecuteCharacters(string name, string source, string expected)
        => ScalarEmissionTest.EmitFixture(name, source, expected);

    [Fact]
    public void MixedAggregateDestructionRemainsConnected()
    {
        const string Source = "var a = ('A', \"old\", ('😀', \"nested\"))\na = ('B', \"new\", ('あ', \"last\"))";
        var ir = ScalarEmissionTest.EmitFixture("CharOwnedAggregate", Source, string.Empty);
        StringEmissionTest.WriteAuditedFixture("CharOwnedAggregate", ir, string.Empty, "old=1;nested=1;new=1;last=1", order: [1, 0, 3, 2]);
    }

    [Theory]
    [InlineData("'A' + 'B'")]
    [InlineData("'A' - 'B'")]
    [InlineData("'A' * 'B'")]
    [InlineData("'A' / 'B'")]
    [InlineData("'A' % 'B'")]
    [InlineData("'A' & 'B'")]
    [InlineData("'A' | 'B'")]
    [InlineData("'A' ^ 'B'")]
    [InlineData("'A' << 1")]
    [InlineData("'A' >> 1")]
    [InlineData("+'A'")]
    [InlineData("-'A'")]
    [InlineData("not 'A'")]
    [InlineData("var c = 'A'\nc++")]
    [InlineData("var c = 'A'\n--c")]
    [InlineData("'A'@u32")]
    [InlineData("65@char")]
    [InlineData("'A' == 65")]
    [InlineData("'A' < 65")]
    [InlineData("'A' == true")]
    [InlineData("let c: char = 65")]
    [InlineData("match 'A'\n    65 => ()\n    _ => ()")]
    [InlineData("match 'A'\n    'A' => ()")]
    [InlineData("func unused() => 'A' + 'B'\n()")]
    [InlineData("if false => 'A' + 'B'")]
    public void InvalidCharacterOperationsAreRejected(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.NotEmpty(c.Binding.Issues);
        using var writer = new StringWriter();
        Assert.False(c.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ReanalysisAndWritingReuseCharacterPlansWithoutAllocations()
    {
        var c = MinimalEmissionTest.Analyze(Guard + "\nlet aggregate = ('A', (true, '😀'))");
        Assert.True(c.Analyze().IsVerified);
        Assert.True(c.WriteIr(TextWriter.Null, out var error), error);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Analyze().IsVerified || !c.WriteIr(TextWriter.Null, out _))
            {
                throw new InvalidOperationException("Character analysis or emission failed.");
            }
        }));
    }
}
