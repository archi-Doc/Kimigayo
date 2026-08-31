// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class CollectionLiteralParseTest
{
    [Fact]
    public void ParsesSwiftStyleArrayAndDictionaryLiterals()
    {
        const string Source = """
            var array = [a, b, c,]
            var dictionary = [k1: v1, k2: v2,]
            var emptyArray = []
            var emptyDictionary = [:]
            """;

        var (kotonoha, fields) = ParseFields(Source);

        var array = Assert.IsType<ArrayLiteralKoto>(fields["array"].InitializerKoto);
        Assert.Equal(["a", "b", "c"], array.Elements.Select(GetIdentifier));

        var dictionary = Assert.IsType<DictionaryLiteralKoto>(fields["dictionary"].InitializerKoto);
        Assert.Collection(
            dictionary.Entries,
            entry =>
            {
                Assert.Equal("k1", GetIdentifier(entry.Key));
                Assert.Equal("v1", GetIdentifier(entry.Value));
            },
            entry =>
            {
                Assert.Equal("k2", GetIdentifier(entry.Key));
                Assert.Equal("v2", GetIdentifier(entry.Value));
            });

        Assert.Empty(Assert.IsType<ArrayLiteralKoto>(fields["emptyArray"].InitializerKoto).Elements);
        Assert.Empty(Assert.IsType<DictionaryLiteralKoto>(fields["emptyDictionary"].InitializerKoto).Entries);
        Assert.All(array.Elements, element => Assert.Same(array, element.Parent));
        Assert.All(
            dictionary.Entries,
            entry =>
            {
                Assert.Same(dictionary, entry.Key.Parent);
                Assert.Same(dictionary, entry.Value.Parent);
            });

        var builder = default(IndentedStringBuilder);
        try
        {
            kotonoha.RootKoto.UnparseAll(ref builder);
            var text = builder.ToString();
            Assert.Contains("var array = [a, b, c]", text, StringComparison.Ordinal);
            Assert.Contains("var dictionary = [k1: v1, k2: v2]", text, StringComparison.Ordinal);
            Assert.Contains("var emptyArray = []", text, StringComparison.Ordinal);
            Assert.Contains("var emptyDictionary = [:]", text, StringComparison.Ordinal);
        }
        finally
        {
            builder.Dispose();
        }
    }

    [Fact]
    public void ParsesNestedMultilineLiteralsAndLiteralPostfixes()
    {
        const string Source = """
            var nested = [
                [1, 2],
                [],
            ]
            var lookup = [key: [3, 4], other: []][key]
            """;

        var (_, fields) = ParseFields(Source);

        var nested = Assert.IsType<ArrayLiteralKoto>(fields["nested"].InitializerKoto);
        Assert.Equal(2, nested.Elements.Count);
        Assert.Equal(2, Assert.IsType<ArrayLiteralKoto>(nested.Elements[0]).Elements.Count);
        Assert.Empty(Assert.IsType<ArrayLiteralKoto>(nested.Elements[1]).Elements);

        var index = Assert.IsType<IndexKoto>(fields["lookup"].InitializerKoto);
        var dictionary = Assert.IsType<DictionaryLiteralKoto>(index.Left);
        Assert.Equal(2, dictionary.Entries.Count);
        Assert.All(dictionary.Entries, entry => Assert.IsType<ArrayLiteralKoto>(entry.Value));
        Assert.Equal("key", GetIdentifier(index.Argument));
    }

    [Fact]
    public void PreservesCollectionLiteralsThroughSerializationAndUnparse()
    {
        const string Source = "var value = [\"items\": [1, 2], \"empty\": []]";
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, Source);
        Assert.Empty(kotonoha.DiagnosticCollection.GetArray());

        var bytes = TinyhandSerializer.Serialize(kotonoha);
        var deserialized = new Kotonoha(compilation);
        TinyhandSerializer.DeserializeObject(bytes, ref deserialized);
        var restored = deserialized ?? throw new InvalidOperationException();
        restored.OnDeserialized(compilation);

        var generatedBody = Assert.IsType<CodeBlockKoto>(restored.GeneratedFunction?.Body);
        var field = Assert.IsType<FieldKoto>(Assert.Single(generatedBody.Items));
        var dictionary = Assert.IsType<DictionaryLiteralKoto>(field.InitializerKoto);
        Assert.All(
            dictionary.Entries,
            entry =>
            {
                Assert.Same(dictionary, entry.Key.Parent);
                Assert.Same(dictionary, entry.Value.Parent);
            });

        var builder = default(IndentedStringBuilder);
        try
        {
            restored.RootKoto.UnparseAll(ref builder);
            var text = builder.ToString();
            Assert.Contains("[\"items\": [1, 2], \"empty\": []]", text, StringComparison.Ordinal);

            var reparsedCompilation = Compilation.CreateForTest();
            var reparsed = reparsedCompilation.Kotonoha;
            reparsed.CreateCodeContext().Parse(reparsed.RootKoto, text);
            Assert.Empty(reparsed.DiagnosticCollection.GetArray());
        }
        finally
        {
            builder.Dispose();
        }
    }

    [Fact]
    public void RecoversFromMalformedCollectionLiterals()
    {
        const string Source = """
            var missingValue = [key:]
            var missingColon = [first: 1, second]
            var missingComma = [1 2]
            var recovered = [3]
            """;

        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, Source);

        Assert.True(kotonoha.DiagnosticCollection.GetArray().Length >= 3);
        var body = Assert.IsType<CodeBlockKoto>(kotonoha.GeneratedFunction?.Body);
        var fields = body.Items.OfType<FieldKoto>().ToArray();
        Assert.Equal(4, fields.Length);
        var recovered = Assert.IsType<ArrayLiteralKoto>(fields[^1].InitializerKoto);
        Assert.Single(recovered.Elements);
    }

    private static (Kotonoha Kotonoha, Dictionary<string, FieldKoto> Fields) ParseFields(string source)
    {
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, source);
        var diagnostics = kotonoha.DiagnosticCollection.GetArray();
        Assert.True(
            diagnostics.Length == 0,
            string.Join(Environment.NewLine, diagnostics.Select(x => $"{x.Span}: {x.Message}")));

        var body = Assert.IsType<CodeBlockKoto>(kotonoha.GeneratedFunction?.Body);
        return (
            kotonoha,
            body.Items
                .OfType<FieldKoto>()
                .ToDictionary(x => x.NameKoto.IdentifierName));
    }

    private static string GetIdentifier(Koto koto)
        => Assert.IsType<IdentifierNameKoto>(koto).IdentifierName;
}
