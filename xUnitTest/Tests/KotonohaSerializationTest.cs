// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class KotonohaSerializationTest
{
    [Fact]
    public void PreservesOriginalDocumentsAndRebuildsTreeInParsingOrder()
    {
        var compilation = Compilation.CreateForTest();
        var original = new Kotonoha(compilation, "library", "library.kotonoha");
        var first = new SourceDocument("first.kimi", "// 日本語のコメント\r\nfunc first() => 1\r\nvar firstValue = 2\r\n");
        var second = new SourceDocument("second.kimi", "\nfunc second() => 3\nvar secondValue = 4\n");
        original.AddSource(first);
        original.AddSource(second);

        var restored = TinyhandSerializer.Deserialize<Kotonoha>(TinyhandSerializer.Serialize(original));
        Assert.NotNull(restored);
        var destination = Compilation.CreateForTest();
        restored.OnDeserialized(destination);

        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.Name, restored.Name);
        Assert.Equal(original.Url, restored.Url);
        Assert.Same(destination, restored.Compilation);
        Assert.Collection(
            restored.SourceDocuments,
            document => AssertDocument(first, document),
            document => AssertDocument(second, document));
        Assert.NotNull(restored.GeneratedFunction);
        Assert.Same(restored.RootKoto, restored.GeneratedFunction.Parent);
        Assert.Equal(original.GeneratedFunction!.ToString(), restored.GeneratedFunction.ToString());

        var originalFunction = Descendants(original.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "first");
        var restoredFunction = Descendants(restored.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "first");
        Assert.Equal(originalFunction.Span, restoredFunction.Span);
        Assert.Equal(originalFunction.KotoId, restoredFunction.KotoId);
        Assert.Same(restored.SourceDocuments[0], restoredFunction.DiagnosticCollection!.SourceDocument);
        Assert.True(restored.TryGetKoto(originalFunction.KotoId, out var indexed));
        Assert.Same(restoredFunction, indexed);
        Assert.All(Descendants(restored.RootKoto), node =>
        {
            Assert.Same(restored, node.Kotonoha);
            Assert.All(node.ChildNodes, child => Assert.Same(node, child.Parent));
        });

        // Reinitializing must neither append documents nor retain old tree/index entries.
        restored.OnDeserialized(destination);
        Assert.Equal(2, restored.SourceDocuments.Count);
        Assert.Equal(original.GeneratedFunction.ToString(), restored.GeneratedFunction!.ToString());
        Assert.True(restored.TryGetKoto(originalFunction.KotoId, out indexed));
        Assert.NotSame(restoredFunction, indexed);
    }

    [Fact]
    public void RestoresEmptySourceUnitIntoPreviouslyParsedInstance()
    {
        var compilation = Compilation.CreateForTest();
        var restored = new Kotonoha(compilation);
        restored.AddSource(new SourceDocument("stale.kimi", "func stale() => 1"));
        var stale = Descendants(restored.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "stale");
        Assert.True(restored.TryGetKoto(stale.KotoId, out _));

        var bytes = TinyhandSerializer.Serialize(new Kotonoha(compilation, "empty", string.Empty));
        TinyhandSerializer.DeserializeObject(bytes, ref restored);
        Assert.NotNull(restored);
        restored.OnDeserialized(compilation);

        Assert.Empty(restored.SourceDocuments);
        Assert.Empty(restored.RootKoto.ChildNodes);
        Assert.Null(restored.GeneratedFunction);
        Assert.False(restored.TryGetKoto(stale.KotoId, out _));
    }

    [Fact]
    public void ReparsesConditionalCompilationUsingDestinationTarget()
    {
        var compilation = Compilation.CreateForTest();
        Assert.True(compilation.Prepare("x86_64-pc-windows-msvc"));
        var original = compilation.Kotonoha;
        original.AddSource(new SourceDocument("target.kimi", "#case windows\n    var windowsValue = 1\n#case _\n    var otherValue = 2"));
        Assert.Contains("windowsValue", original.GeneratedFunction!.ToString());

        var destination = Compilation.CreateForTest();
        Assert.True(destination.Prepare("x86_64-unknown-linux-gnu"));
        var restored = TinyhandSerializer.Deserialize<Kotonoha>(TinyhandSerializer.Serialize(original));
        Assert.NotNull(restored);
        restored.OnDeserialized(destination);

        Assert.Contains("otherValue", restored.GeneratedFunction!.ToString());
        Assert.DoesNotContain("windowsValue", restored.GeneratedFunction.ToString());
        Assert.Equal(original.SourceDocuments[0].SourceText, restored.SourceDocuments[0].SourceText);
    }

    [Fact]
    public void RebuildsLineIndexWhenDeserializingIntoExistingDocument()
    {
        var restored = new SourceDocument("old.kimi", "old\ntext\n");
        Assert.Equal(3, restored.LineCount);
        var original = new SourceDocument("new.kimi", "日本語\r\nsecond");

        TinyhandSerializer.DeserializeObject(TinyhandSerializer.Serialize(original), ref restored);
        Assert.NotNull(restored);

        AssertDocument(original, restored);
        Assert.Equal(2, restored.LineCount);
        Assert.Equal("second", restored.GetLineSpan(1).ToString());
        Assert.Equal(new SourcePosition(1, 0), restored.GetPosition(5));
    }

    private static void AssertDocument(SourceDocument expected, SourceDocument actual)
    {
        Assert.Equal(expected.Path, actual.Path);
        Assert.Equal(expected.SourceText, actual.SourceText);
    }

    private static IEnumerable<Koto> Descendants(Koto node)
    {
        foreach (var child in node.ChildNodes)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
