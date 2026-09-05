// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class ForParseTest
{
    private static readonly PropertyInfo KotoListProperty = typeof(DeclarationContainerKoto).GetProperty(
        "KotoList",
        BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Fact]
    public void ParsesSingleAndTupleBindings()
    {
        var function = ParseSingleFunction(
            """
            func ProcessAll(values: Values, dictionary: Dictionary)
                for x in values
                    process(x)
                for (key, value) in dictionary
                    process(key, value)
            """);

        var body = Assert.IsType<CodeBlockKoto>(function.Body);
        Assert.Equal(2, body.Items.Count);

        var single = Assert.IsType<ForKoto>(body.Items[0]);
        Assert.False(single.IsTupleBinding);
        Assert.Equal("x", Assert.Single(single.Bindings).IdentifierName);
        Assert.Equal("values", Assert.IsType<IdentifierNameKoto>(single.Iterable).IdentifierName);
        Assert.IsType<InvocationKoto>(Assert.Single(single.Body.Items));

        var tuple = Assert.IsType<ForKoto>(body.Items[1]);
        Assert.True(tuple.IsTupleBinding);
        Assert.Equal(["key", "value"], tuple.Bindings.Select(x => x.IdentifierName));
        Assert.Equal("dictionary", Assert.IsType<IdentifierNameKoto>(tuple.Iterable).IdentifierName);
        Assert.IsType<InvocationKoto>(Assert.Single(tuple.Body.Items));

        Assert.Same(body, single.Parent);
        Assert.Same(single, single.Iterable.Parent);
        Assert.Same(single, single.Body.Parent);
        Assert.All(single.Bindings, binding => Assert.Same(single, binding.Parent));
        Assert.All(tuple.Bindings, binding => Assert.Same(tuple, binding.Parent));
    }

    [Fact]
    public void RecognizesForKeywordAndContextualIn()
    {
        Assert.True(TokenKind.For.IsKeyword());
        Assert.Equal(Constants.ForKeyword, TokenKind.For.ToText());
        Assert.False(TokenKind.In.IsKeyword());
        Assert.True(TokenKind.In.IsIdentifierOrContextualKeyword());
        Assert.Equal(Constants.InKeyword, TokenKind.In.ToText());

        var function = ParseSingleFunction(
            """
            func Identity(in: Values)
                in
            """);
        Assert.Equal("in", Assert.Single(function.Parameters).InternalName);
        Assert.Equal(
            "in",
            Assert.IsType<IdentifierNameKoto>(Assert.Single(function.Body!.Items)).IdentifierName);
    }

    [Fact]
    public void RecoversFromMissingInAndTupleClose()
    {
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        var source = """
            func Recover(values: Values)
                for item values
                    process(item)
                for (key, value in values
                    process(key)
                var done = true
            """;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, source);

        Assert.True(kotonoha.DiagnosticCollection.GetArray().Length >= 2);
        var function = Assert.IsType<FunctionKoto>(Assert.Single(GetChildren(kotonoha.RootKoto)));
        var body = Assert.IsType<CodeBlockKoto>(function.Body);
        Assert.Equal(3, body.Items.Count);
        Assert.IsType<ForKoto>(body.Items[0]);
        Assert.IsType<ForKoto>(body.Items[1]);
        Assert.IsType<FieldKoto>(body.Items[2]);
    }

    [Fact]
    public void PreservesForExpressionsThroughSerializationAndUnparse()
    {
        const string Source = """
            func Iterate(values: Values, dictionary: Dictionary)
                for value in values
                    consume(value)
                for (key, value) in dictionary
                    consume(key, value)
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
        Assert.Equal(2, body.Items.Count);
        Assert.All(body.Items, item => Assert.IsType<ForKoto>(item));
        Assert.True(Assert.IsType<ForKoto>(body.Items[1]).IsTupleBinding);

        var builder = new IndentedStringBuilder();
        try
        {
            restored.RootKoto.UnparseAll(ref builder);
            var unparsed = builder.ToString();
            Assert.Contains("for value in values", unparsed, StringComparison.Ordinal);
            Assert.Contains("for (key, value) in dictionary", unparsed, StringComparison.Ordinal);

            var reparsedCompilation = Compilation.CreateForTest();
            var reparsed = reparsedCompilation.Kotonoha;
            reparsed.CreateCodeContext().Parse(reparsed.RootKoto, unparsed);
            Assert.Empty(reparsed.DiagnosticCollection.GetArray());
        }
        finally
        {
            builder.Dispose();
        }
    }

    private static FunctionKoto ParseSingleFunction(string source)
    {
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, source);
        var diagnostics = kotonoha.DiagnosticCollection.GetArray();
        Assert.True(
            diagnostics.Length == 0,
            string.Join(Environment.NewLine, diagnostics.Select(x => $"{x.Span}: {x.Message}")));

        return Assert.IsType<FunctionKoto>(Assert.Single(GetChildren(kotonoha.RootKoto)));
    }

    private static List<Koto> GetChildren(DeclarationContainerKoto group)
        => ReferenceEquals(group, group.Kotonoha.RootKoto)
            ? group.Kotonoha.GeneratedFunction?.Body?.Items.ToList() ?? []
            : (List<Koto>)KotoListProperty.GetValue(group)!;
}
