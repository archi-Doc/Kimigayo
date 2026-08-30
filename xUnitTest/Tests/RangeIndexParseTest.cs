// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class RangeIndexParseTest
{
    private static readonly PropertyInfo KotoListProperty = typeof(GroupKoto).GetProperty(
        "KotoList",
        BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Fact]
    public void ParsesElementAndFromEndIndexes()
    {
        const string Source = """
            var first = data[0]
            var last = data[^1]
            var end = ^0
            var xor = a ^ b
            """;
        var fields = ParseFields(Source);

        var first = Assert.IsType<IndexKoto>(fields["first"].InitializerKoto);
        Assert.False(first.IsSlice);
        Assert.IsType<NumberLiteralKoto>(first.Argument);
        Assert.Equal("data[0]", Source.AsSpan(first.Span.Start, first.Span.Length).ToString());

        var last = Assert.IsType<IndexKoto>(fields["last"].InitializerKoto);
        var fromEnd = Assert.IsType<FromEndIndexKoto>(last.Argument);
        Assert.IsType<NumberLiteralKoto>(fromEnd.Value);
        Assert.Same(last, fromEnd.Parent);

        Assert.IsType<FromEndIndexKoto>(fields["end"].InitializerKoto);
        Assert.IsType<CaretKoto>(fields["xor"].InitializerKoto);
    }

    [Fact]
    public void ParsesEveryRustStyleRangeForm()
    {
        var fields = ParseFields(
            """
            var full = data[..]
            var from = data[1..]
            var to = data[..4]
            var toInclusive = data[..=4]
            var bounded = data[1..4]
            var boundedInclusive = data[1..=4]
            var withoutLast = data[1..^1]
            var throughLast = data[1..=^1]
            """);

        AssertRange(fields["full"], hasStart: false, hasEnd: false, isInclusive: false);
        AssertRange(fields["from"], hasStart: true, hasEnd: false, isInclusive: false);
        AssertRange(fields["to"], hasStart: false, hasEnd: true, isInclusive: false);
        AssertRange(fields["toInclusive"], hasStart: false, hasEnd: true, isInclusive: true);
        AssertRange(fields["bounded"], hasStart: true, hasEnd: true, isInclusive: false);
        AssertRange(fields["boundedInclusive"], hasStart: true, hasEnd: true, isInclusive: true);

        var withoutLast = AssertRange(fields["withoutLast"], hasStart: true, hasEnd: true, isInclusive: false);
        Assert.IsType<FromEndIndexKoto>(withoutLast.End);

        var throughLast = AssertRange(fields["throughLast"], hasStart: true, hasEnd: true, isInclusive: true);
        Assert.IsType<FromEndIndexKoto>(throughLast.End);
    }

    [Fact]
    public void GivesRangeLowerPrecedenceThanLogicalOrAndHigherThanAssignment()
    {
        var fields = ParseFields(
            """
            var value = 1 + 2..3 or flag
            var stored = 0..10
            var assigned = target = 0..10
            """);

        var range = Assert.IsType<RangeKoto>(fields["value"].InitializerKoto);
        Assert.IsType<PlusKoto>(range.Start);
        Assert.IsType<OrKoto>(range.End);
        Assert.IsType<RangeKoto>(fields["stored"].InitializerKoto);

        var assignment = Assert.IsType<EqualsKoto>(fields["assigned"].InitializerKoto);
        Assert.IsType<RangeKoto>(assignment.Right);
    }

    [Fact]
    public void DiagnosesChainedRangesAndMissingInclusiveEndThenRecovers()
    {
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        var source = """
            var chained = 1..2..3
            var missing = data[..=]
            var valid = data[0..^0]
            """;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, source);

        Assert.True(kotonoha.DiagnosticCollection.GetArray().Length >= 2);
        var fields = GetChildren(kotonoha.RootKoto).OfType<FieldKoto>().ToArray();
        Assert.Equal(3, fields.Length);
        Assert.IsType<RangeKoto>(fields[0].InitializerKoto);
        Assert.IsType<IndexKoto>(fields[2].InitializerKoto);
    }

    [Fact]
    public void PreservesRangesThroughSerializationAndUnparse()
    {
        const string Source = """
            func Slice(data: Array<i32>)
                var middle = data[1..^1]
                data[..=^1]
            """;
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, Source);
        Assert.Empty(kotonoha.DiagnosticCollection.GetArray());

        var bytes = TinyhandSerializer.Serialize(kotonoha);
        var deserialized = new Kotonoha(compilation);
        TinyhandSerializer.DeserializeObject(bytes, ref deserialized);
        var restored = deserialized ?? throw new InvalidOperationException();
        restored.OnDeserialized(compilation);

        var function = Assert.IsType<FunctionKoto>(Assert.Single(GetChildren(restored.RootKoto)));
        var body = Assert.IsType<CodeBlockKoto>(function.Body);
        var field = Assert.IsType<FieldKoto>(body.Items[0]);
        var index = Assert.IsType<IndexKoto>(field.InitializerKoto);
        Assert.True(index.IsSlice);
        Assert.IsType<FromEndIndexKoto>(Assert.IsType<RangeKoto>(index.Argument).End);

        var builder = new IndentedStringBuilder();
        try
        {
            restored.RootKoto.UnparseAll(ref builder);
            var reparsedCompilation = Compilation.CreateForTest();
            var reparsed = reparsedCompilation.Kotonoha;
            reparsed.CreateCodeContext().Parse(reparsed.RootKoto, builder.ToString());
            Assert.Empty(reparsed.DiagnosticCollection.GetArray());
        }
        finally
        {
            builder.Dispose();
        }
    }

    private static Dictionary<string, FieldKoto> ParseFields(string source)
    {
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, source);
        var diagnostics = kotonoha.DiagnosticCollection.GetArray();
        Assert.True(
            diagnostics.Length == 0,
            string.Join(Environment.NewLine, diagnostics.Select(x => $"{x.Span}: {x.Message}")));

        return GetChildren(kotonoha.RootKoto)
            .OfType<FieldKoto>()
            .ToDictionary(x => x.NameKoto.IdentifierName);
    }

    private static RangeKoto AssertRange(FieldKoto field, bool hasStart, bool hasEnd, bool isInclusive)
    {
        var index = Assert.IsType<IndexKoto>(field.InitializerKoto);
        Assert.True(index.IsSlice);
        var range = Assert.IsType<RangeKoto>(index.Argument);
        Assert.Equal(hasStart, range.Start is not null);
        Assert.Equal(hasEnd, range.End is not null);
        Assert.Equal(isInclusive, range.IsInclusive);
        return range;
    }

    private static List<Koto> GetChildren(GroupKoto group)
        => (List<Koto>)KotoListProperty.GetValue(group)!;
}
