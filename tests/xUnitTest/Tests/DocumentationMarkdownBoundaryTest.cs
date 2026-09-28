// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kimi.Compiler.Documentation;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

#pragma warning disable xUnit1051 // Deliberately exercises default tokens and independent cancelled callers.

public class DocumentationMarkdownBoundaryTest
{
    [Theory]
    [InlineData("")]
    [InlineData(" \t ")]
    [InlineData("plain text")]
    [InlineData("   plain text  \t")]
    [InlineData("\t😀 日本語 & text")]
    [InlineData("123. text")]
    [InlineData("text\nnext")]
    [InlineData("  first line \n\t second\t\n third  ")]
    [InlineData("hard  \nbreak")]
    [InlineData("a\n\nb")]
    [InlineData("a\n \nb")]
    [InlineData("a\n")]
    [InlineData("a\n- b")]
    [InlineData("a\n# b")]
    [InlineData("a\n1. b")]
    [InlineData("one\n    four\n\t\ttabs")]
    [InlineData("# heading")]
    [InlineData("- list")]
    [InlineData("<https://a.b>")]
    [InlineData("text **strong**")]
    public void PlainFastPathPreservesGeneralParserTreeAndRanges(string input)
    {
        var optimized = DocumentationMarkdownDocument.Parse(input);
        var parser = new DocumentationMarkdownParser(input, CancellationToken.None, 256);
        DocumentationMarkdownDocument general;
        try
        {
            general = parser.Parse(null);
        }
        finally
        {
            parser.Dispose();
        }

        Assert.Equal(Snapshot(general.Root), Snapshot(optimized.Root));
        DocumentationMarkdownParserTest.AssertRanges(optimized);

        static string[] Snapshot(DocumentationMarkdownNode root)
        {
            var result = new List<string>();
            var pending = new Stack<DocumentationMarkdownNode>();
            pending.Push(root);
            while (pending.TryPop(out var node))
            {
                result.Add($"{node.Kind}|{node.Span}|{node.Text.ToString()}|{node.HeadingLevel}|{node.Destination}|{node.Title}");
                foreach (var child in node.Children.Reverse())
                {
                    pending.Push(child);
                }
            }

            return result.ToArray();
        }
    }

    [Fact]
    public void PlainFastPathRetainsDepthAndCancellationContracts()
    {
        Assert.Throws<DocumentationMarkdownLimitException>(() => DocumentationMarkdownDocument.Parse("plain", maximumDepth: 1));
        Assert.Null(DocumentationMarkdownDocument.Parse(" \t", maximumDepth: 1).Summary);
        Assert.Throws<OperationCanceledException>(() => DocumentationMarkdownDocument.Parse("plain", new CancellationToken(true)));
    }

    [Theory]
    [InlineData("```\none\n\ntwo\n```", "one\n\ntwo\n")]
    [InlineData("```\none\n\ntwo", "one\n\ntwo\n")]
    [InlineData("  ```\n  one\n\n  two\n  ```", "one\n\ntwo\n")]
    [InlineData("> ```\n> one\n>\n> two\n> ```", "one\n\ntwo\n")]
    [InlineData("- ```\n  one\n\n  two\n  ```", "one\n\ntwo\n")]
    [InlineData("```\none\n\0two\nthree\n```", "one\n�two\nthree\n")]
    public void CodeSlicesPreserveBlankLinesPrefixesAndSyntheticFinalNewline(string input, string expected)
    {
        var doc = DocumentationMarkdownDocument.Parse(input);
        var code = doc.Root.FirstChild!.Value;
        while (code.Kind != DocumentationMarkdownKind.CodeBlock)
        {
            code = code.FirstChild!.Value;
        }

        Assert.Equal(expected, string.Concat(code.Children.Select(x => x.Kind == DocumentationMarkdownKind.SoftBreak ? "\n" : x.Text.ToString())));
        DocumentationMarkdownParserTest.AssertRanges(doc);
        foreach (var leaf in code.Children)
        {
            Assert.Equal(leaf.Span, leaf.SourceSpan);
        }
    }

    [Theory]
    [InlineData("# title", DocumentationMarkdownKind.Heading)]
    [InlineData("- item", DocumentationMarkdownKind.BulletList)]
    [InlineData("> quote", DocumentationMarkdownKind.Quote)]
    [InlineData("```", DocumentationMarkdownKind.CodeBlock)]
    public void KeepsThreeColumnBlockStartLimitInsideEveryContainer(string marker, DocumentationMarkdownKind kind)
    {
        foreach (var indent in new[] { 3, 4 })
        {
            var text = new string(' ', indent) + marker;
            var expected = indent == 3 ? kind : DocumentationMarkdownKind.Paragraph;
            var root = DocumentationMarkdownDocument.Parse(text);
            Assert.Equal(expected, root.Root.FirstChild!.Value.Kind);
            var quote = DocumentationMarkdownDocument.Parse("> " + text);
            Assert.Equal(expected, quote.Root.FirstChild!.Value.FirstChild!.Value.Kind);
            var list = DocumentationMarkdownDocument.Parse("- seed\n\n  " + text);
            var item = list.Root.FirstChild!.Value.FirstChild!.Value;
            Assert.Equal(expected, item.Children.Last().Kind);
            Assert.False(item.Parent!.Value.IsTight);
            DocumentationMarkdownParserTest.AssertRanges(root);
            DocumentationMarkdownParserTest.AssertRanges(quote);
            DocumentationMarkdownParserTest.AssertRanges(list);
        }
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    public void PreservesExactDecodedAndMultilineRanges(string newline)
    {
        var lines = new[] { "# note", string.Empty, "- r&#101;turn&#58; 😀", "  more\\", "  end", string.Empty, "# warning", string.Empty, "- `code`: detail" };
        var source = string.Join(newline, lines.Select(x => "    /// " + x)) + newline + "    public func f() => ()";
        source = "group G" + newline + source;
        var comment = Assert.Single(Assert.Single(DocumentationCommentTest.Parse(source).DocumentationSources).Comments);
        var doc = DocumentationMarkdownDocument.Parse(comment);
        var candidates = doc.GetItemCandidates().ToArray();
        Assert.Equal(new[] { "note", "return", "warning", "code" }, candidates.Select(x => x.Name));
        Assert.Equal(doc.Text.IndexOf("&#58;", StringComparison.Ordinal) + 5, candidates[1].DescriptionSpan.Start);
        var description = candidates[1].SourceDescriptionSpan;
        Assert.Equal(" 😀" + newline + "    ///   more\\" + newline + "    ///   end", source.Substring(description.Start, description.Length));
        Assert.Equal(doc.Text.IndexOf("# warning", StringComparison.Ordinal), candidates[0].DescriptionSpan.End);
        Assert.Equal(doc.Text.Length, candidates[2].DescriptionSpan.End);
        var decoded = candidates[1].Node.FirstChild!.Value.Children.Single(x => x.Text.SequenceEqual(":"));
        Assert.Equal("&#58;", source.Substring(decoded.SourceSpan.Start, decoded.SourceSpan.Length));
        foreach (var position in Enumerable.Range(0, doc.Text.Length).Where(i => doc.Text[i] == '\n'))
        {
            var original = doc.GetSourceSpan(new(position, 1));
            Assert.Equal(newline, source.Substring(original.Start, original.Length));
            var insertion = doc.GetSourceSpan(new(position + 1, 0));
            Assert.Equal(original.End + "    /// ".Length, insertion.Start);
        }

        var eof = doc.GetSourceSpan(new(doc.Text.Length, 0));
        Assert.Equal(source.IndexOf(newline + "    public", StringComparison.Ordinal), eof.Start);
        DocumentationMarkdownParserTest.AssertRanges(doc);
    }

    [Fact]
    public void PartialTabConsumptionPreservesCodeAndOriginalCoordinates()
    {
        const string source = "/// > ```\n/// >\tx\n/// > ```\nfunc f() => ()";
        var comment = Assert.Single(Assert.Single(DocumentationCommentTest.Parse(source).DocumentationSources).Comments);
        var doc = DocumentationMarkdownDocument.Parse(comment);
        var code = doc.Root.FirstChild!.Value.FirstChild!.Value;
        Assert.Equal(DocumentationMarkdownKind.CodeBlock, code.Kind);
        Assert.Equal("  x\n", string.Concat(code.Children.Select(x => x.Kind == DocumentationMarkdownKind.SoftBreak ? "\n" : x.Text.ToString())));
        var spaces = code.Children.First();
        Assert.Equal(source.IndexOf('\t'), spaces.SourceSpan.Start);
        Assert.Equal(1, spaces.SourceSpan.Length);
        DocumentationMarkdownParserTest.AssertRanges(doc);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("ja-JP")]
    [InlineData("tr-TR")]
    public void Unicode15AndExactNamesAreCultureIndependent(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal("<p>a*🫨* a<em>🫩</em></p>\n", DocumentationMarkdownParserTest.RenderSyntax(DocumentationMarkdownDocument.Parse("a*🫨* a*🫩*").Root));
            var doc = DocumentationMarkdownDocument.Parse("- é: composed\n- é: decomposed\n- Note: capital\n- note: lowercase\n- I: ascii\n- ı: dotless\n- value : trailing\n- ` value`: leading\n\n# Return\n\n# Returns");
            var items = doc.ClassifyItems([new("é"), new("note"), new("I"), new("value")]).ToArray();
            Assert.Equal(8, items.Length);
            Assert.Equal(new[] { DocumentationMarkdownItemKind.Parameter, DocumentationMarkdownItemKind.Unknown, DocumentationMarkdownItemKind.Unknown, DocumentationMarkdownItemKind.Parameter, DocumentationMarkdownItemKind.Parameter, DocumentationMarkdownItemKind.Unknown, DocumentationMarkdownItemKind.Unknown, DocumentationMarkdownItemKind.Unknown }, items.Select(x => x.Kind));
            Assert.Equal("é", items[1].Name);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("&amp;&lt;&gt;&quot;&apos;", "&<>\"'")]
    [InlineData("&#x41; &#X41; &#0000065;", "A A A")]
    [InlineData("&#00000065; &#x0000041;", "&#00000065; &#x0000041;")]
    [InlineData("&#x110000; &#55296; &#0;", "� � �")]
    [InlineData("&amp &AMP; &copy; &#x; &#;", "&amp &AMP; &copy; &#x; &#;")]
    public void CharacterReferencesHaveExactLimitsAndNeverBecomeSyntax(string input, string expected)
    {
        var doc = DocumentationMarkdownDocument.Parse(input);
        Assert.Equal(expected, string.Concat(doc.Summary!.Value.Children.Select(x => x.Text.ToString())));
        var symbols = DocumentationMarkdownDocument.Parse("&#42;x&#42; &#91;x&#93;&#40;u&#41;");
        Assert.All(symbols.Summary!.Value.Children, x => Assert.Equal(DocumentationMarkdownKind.Text, x.Kind));
    }

    [Fact]
    public void BorrowedDecodedScalarsRemainStableAfterCollectionsAndParserReuse()
    {
        var doc = DocumentationMarkdownDocument.Parse("&#x1F600; &amp; \\*");
        var leaf = doc.Summary!.Value.FirstChild!.Value;
        var borrowed = leaf.Text;
        for (var index = 0; index < 64; index++)
        {
            _ = DocumentationMarkdownDocument.Parse("**reuse** &lt; [x](u)");
        }

        GC.Collect(2, GCCollectionMode.Forced, true, true);
        Assert.Equal("😀", borrowed.ToString());
        Assert.Equal("😀", leaf.Text.ToString());
        GC.KeepAlive(doc);
    }

    [Fact]
    public async Task ConcurrentCancellationDoesNotPoisonSharedCandidates()
    {
        var doc = DocumentationMarkdownDocument.Parse(string.Concat(Enumerable.Repeat("- return: value\n", 2000)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var readers = Enumerable.Range(0, 32).Select(i => Task.Run(() =>
        {
            if (i % 2 == 0)
            {
                Assert.Throws<OperationCanceledException>(() => { doc.GetItemCandidates(cancellation.Token); });
            }
            else
            {
                Assert.Equal(2000, doc.GetItemCandidates().Length);
            }
        }));
        await Task.WhenAll(readers);
        Assert.True(Unsafe.AreSame(ref MemoryMarshal.GetReference(doc.GetItemCandidates()), ref MemoryMarshal.GetReference(doc.GetItemCandidates())));
        Assert.Throws<OperationCanceledException>(() => { doc.ClassifyItems([new("return")], cancellation.Token); });
        Assert.All(doc.ClassifyItems([new("return")]).ToArray(), x => Assert.Equal(DocumentationMarkdownItemKind.Parameter, x.Kind));
        Assert.All(doc.GetItemCandidates().ToArray(), x => Assert.Equal(DocumentationMarkdownItemKind.Unclassified, x.Kind));
    }

    [Fact]
    public async Task CancelsAnActiveLargeParseAndCanRetryTheSameInput()
    {
        var input = string.Concat(Enumerable.Repeat("words **strong** and `code`\n", 100000));
        using var cancellation = new CancellationTokenSource();
        using var started = new ManualResetEventSlim();
        var operation = Task.Run(() =>
        {
            started.Set();
            return DocumentationMarkdownDocument.Parse(input, cancellation.Token);
        });
        Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
        cancellation.CancelAfter(1);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await operation);
        var retried = DocumentationMarkdownDocument.Parse(input);
        Assert.Equal(input, retried.Text);
        DocumentationMarkdownParserTest.AssertRanges(retried);
    }
}
