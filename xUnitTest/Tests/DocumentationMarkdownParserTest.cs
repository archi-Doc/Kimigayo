// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.
using System.Text;
using Kimi.Compiler.Documentation;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

#pragma warning disable xUnit1051 // Explicitly exercises both default-token operation and cancellation/retry.

public class DocumentationMarkdownParserTest
{
    [Theory]
    [InlineData("", "")]
    [InlineData("plain", "<p>plain</p>\n")]
    [InlineData("a\nb\n\nc", "<p>a\nb</p>\n<p>c</p>\n")]
    [InlineData("a  \nb", "<p>a<br />\nb</p>\n")]
    [InlineData("a\\\nb", "<p>a<br />\nb</p>\n")]
    [InlineData("# a ###", "<h1>a</h1>\n")]
    [InlineData("    # a", "<p># a</p>\n")]
    [InlineData("    - a", "<p>- a</p>\n")]
    [InlineData("example\n---", "<p>example\n---</p>\n")]
    [InlineData("---", "<p>---</p>\n")]
    [InlineData("<T>\n*explanation*", "<p>&lt;T&gt;\n<em>explanation</em></p>\n")]
    [InlineData("**a** _b_", "<p><strong>a</strong> <em>b</em></p>\n")]
    [InlineData("***foo***", "<p><em><strong>foo</strong></em></p>\n")]
    [InlineData("foo_bar_baz", "<p>foo_bar_baz</p>\n")]
    [InlineData("*a **b** c*", "<p><em>a <strong>b</strong> c</em></p>\n")]
    [InlineData("**a *b* c**", "<p><strong>a <em>b</em> c</strong></p>\n")]
    [InlineData("*a _b* c_", "<p><em>a _b</em> c_</p>\n")]
    [InlineData("` foo   bar `", "<p><code>foo   bar</code></p>\n")]
    [InlineData("``a ` b``", "<p><code>a ` b</code></p>\n")]
    [InlineData("`a\nb`", "<p><code>a b</code></p>\n")]
    [InlineData("` `", "<p><code> </code></p>\n")]
    [InlineData("`unclosed", "<p>`unclosed</p>\n")]
    [InlineData("\\*a\\*", "<p>*a*</p>\n")]
    [InlineData("&lt; &amp; &copy; &AMP; &#169; &#x1F600;", "<p>&lt; &amp; &amp;copy; &amp;AMP; © 😀</p>\n")]
    [InlineData("&#0; &#xD800; &#1114112;", "<p>� � �</p>\n")]
    [InlineData("&#42;a&#42;", "<p>*a*</p>\n")]
    [InlineData("- a\n- b", "<ul>\n<li>a</li>\n<li>b</li>\n</ul>\n")]
    [InlineData("- a\n\n- b", "<ul>\n<li>\n<p>a</p>\n</li>\n<li>\n<p>b</p>\n</li>\n</ul>\n")]
    [InlineData("- a\nb", "<ul>\n<li>a</li>\n</ul>\n<p>b</p>\n")]
    [InlineData("- a\n  b", "<ul>\n<li>a\nb</li>\n</ul>\n")]
    [InlineData("10. a\n   b", "<ol start=\"10\">\n<li>a</li>\n</ol>\n<p>b</p>\n")]
    [InlineData("-     a\n  b", "<ul>\n<li>a\nb</li>\n</ul>\n")]
    [InlineData("> a\nb", "<blockquote>\n<p>a</p>\n</blockquote>\n<p>b</p>\n")]
    [InlineData("> a\n>\n> b", "<blockquote>\n<p>a</p>\n<p>b</p>\n</blockquote>\n")]
    [InlineData("```kimi\n# example\n```", "<pre><code class=\"language-kimi\"># example\n</code></pre>\n")]
    [InlineData("~~~\na", "<pre><code>a\n</code></pre>\n")]
    [InlineData("[x][r]\n\n[r]: guide.md", "<p>[x][r]</p>\n<p>[r]: guide.md</p>\n")]
    [InlineData("![x](guide.md)", "<p>!<a href=\"guide.md\">x</a></p>\n")]
    [InlineData("[**x**](a(b)c \"title\")", "<p><a href=\"a(b)c\" title=\"title\"><strong>x</strong></a></p>\n")]
    [InlineData("[x]()", "<p><a href=\"\">x</a></p>\n")]
    [InlineData("[x]( \"title\")", "<p><a href=\"&quot;title&quot;\">x</a></p>\n")]
    [InlineData("[x](<> \"title\")", "<p><a href=\"\" title=\"title\">x</a></p>\n")]
    [InlineData("[x]( \"two words\")", "<p>[x]( &quot;two words&quot;)</p>\n")]
    [InlineData("[x](a \"\")", "<p><a href=\"a\" title=\"\">x</a></p>\n")]
    [InlineData("[a [b](u)](v)", "<p>[a <a href=\"u\">b</a>](v)</p>\n")]
    [InlineData("<https://example.com>", "<p><a href=\"https://example.com\">https://example.com</a></p>\n")]
    [InlineData("<a@b.example>", "<p><a href=\"mailto:a@b.example\">a@b.example</a></p>\n")]
    [InlineData("- - -", "<ul>\n<li>\n<ul>\n<li>\n<ul>\n<li></li>\n</ul>\n</li>\n</ul>\n</li>\n</ul>\n")]
    [InlineData("* foo\n  * bar\n\n  baz", "<ul>\n<li>\n<p>foo</p>\n<ul>\n<li>bar</li>\n</ul>\n<p>baz</p>\n</li>\n</ul>\n")]
    [InlineData("- a\n  - b\n\n- c", "<ul>\n<li>\n<p>a</p>\n<ul>\n<li>b</li>\n</ul>\n</li>\n<li>\n<p>c</p>\n</li>\n</ul>\n")]
    [InlineData("- a\n  - b\n\n    c\n- d", "<ul>\n<li>a\n<ul>\n<li>\n<p>b</p>\n<p>c</p>\n</li>\n</ul>\n</li>\n<li>d</li>\n</ul>\n")]
    [InlineData("```\nx\n", "<pre><code>x\n</code></pre>\n")]
    [InlineData("```\n\0\n```", "<pre><code>�\n</code></pre>\n")]
    [InlineData("[x](a(\\<b))", "<p><a href=\"a(&lt;b)\">x</a></p>\n")]
    [InlineData("[<https://a.b>](c)", "<p>[<a href=\"https://a.b\">https://a.b</a>](c)</p>\n")]
    public void ParsesSupportedSyntaxAndDeliberateDifferences(string input, string expected)
    {
        var document = DocumentationMarkdownDocument.Parse(input);
        Assert.Equal(expected, RenderSyntax(document.Root));
        AssertRanges(document);
    }

    [Fact]
    public void ExtractsDecodedNamesAndClassifiesOnlyWithDeclarationInformation()
    {
        var text = "Summary.\n\n- note: parameter\n- `return`: result\n- r&#101;turn&#58;&#32;other\n- self: ordinary\n- value : unknown\n- T: ambiguous\n- **note**: prose\n\n# `note`\n\nText\n\n## example\n\nExample\n\n# End";
        var doc = DocumentationMarkdownDocument.Parse(text);
        var candidates = doc.GetItemCandidates().ToArray();
        Assert.Equal(new[] { "note", "return", "return", "self", "value ", "T", "note", "example" }, candidates.Select(x => x.Name));
        Assert.All(candidates.Take(6), x => Assert.Equal(DocumentationMarkdownItemKind.Unclassified, x.Kind));
        Assert.StartsWith("&#32;other", text[candidates[2].DescriptionSpan.Start..]);
        var classified = doc.ClassifyItems([new("note"), new("self"), new("T"), new("T"), new("return", true)]).ToArray();
        Assert.Equal(new[] { DocumentationMarkdownItemKind.Parameter, DocumentationMarkdownItemKind.Standard, DocumentationMarkdownItemKind.Standard, DocumentationMarkdownItemKind.Parameter, DocumentationMarkdownItemKind.Unknown, DocumentationMarkdownItemKind.Ambiguous, DocumentationMarkdownItemKind.Standard, DocumentationMarkdownItemKind.Standard }, classified.Select(x => x.Kind));
        Assert.True(candidates[6].DescriptionSpan.End > candidates[7].DescriptionSpan.Start);
        Assert.Equal(text.IndexOf("# End", StringComparison.Ordinal), candidates[6].DescriptionSpan.End);
    }

    [Theory]
    [InlineData("- ` value `: x", "value")]
    [InlineData("- return\\: x", "return")]
    [InlineData("- return:&#32;x", "return")]
    [InlineData("- value name: x", "value name")]
    [InlineData("- `a`b: x", null)]
    [InlineData("- a`b`: x", null)]
    [InlineData("- return:bad", null)]
    [InlineData("- : x", null)]
    public void UsesTheExactCandidateBoundary(string input, string? name)
    {
        var items = DocumentationMarkdownDocument.Parse(input).GetItemCandidates().ToArray();
        if (name is null)
        {
            Assert.Empty(items);
        }
        else
        {
            Assert.Equal(name, Assert.Single(items).Name);
        }
    }

    [Fact]
    public void ExcludesNestedAndQuotedItemsAndKeepsDuplicateRootItems()
    {
        var doc = DocumentationMarkdownDocument.Parse("- note: one\n  - warning: nested\n- note: two\n\n> - abort: quote\n\n```\n- safety: code\n```");
        Assert.Equal(new[] { "note", "note" }, doc.GetItemCandidates().ToArray().Select(x => x.Name));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    public void MapsSourceRangesIncludingLineEndingsAndInsertions(string ending)
    {
        var source = "/// 😀 first" + ending + "/// - return&#58; x" + ending + "func f() => ()";
        var tree = DocumentationCommentTest.Parse(source);
        var comment = Assert.Single(Assert.Single(tree.DocumentationSources).Comments);
        var doc = DocumentationMarkdownDocument.Parse(comment);
        var lf = doc.Text.IndexOf('\n');
        Assert.Equal(ending, source.Substring(doc.GetSourceSpan(new(lf, 1)).Start, doc.GetSourceSpan(new(lf, 1)).Length));
        Assert.Equal(source.IndexOf("- return", StringComparison.Ordinal), doc.GetSourceSpan(new(lf + 1, 0)).Start);
        Assert.Equal(source.IndexOf(ending + "func", StringComparison.Ordinal), doc.GetSourceSpan(new(doc.Text.Length, 0)).Start);
        AssertRanges(doc);
    }

    [Fact]
    public void FixesUnicodeClassificationToVersion15()
    {
        Assert.True(MarkdownUnicode.IsPunctuation(0x1FAE8)); // Shaking face, assigned in 15.0.
        Assert.False(MarkdownUnicode.IsPunctuation(0x1FAE9)); // Unassigned in 15.0.
        Assert.True(MarkdownUnicode.IsWhitespace(0x2007));
        Assert.False(MarkdownUnicode.IsWhitespace(0x0085));
        Assert.False(MarkdownUnicode.IsWhitespace(0x000B));
        Assert.False(MarkdownUnicode.IsWhitespace(0x2028));
        Assert.Equal("<p>a_🫨_b</p>\n", RenderSyntax(DocumentationMarkdownDocument.Parse("a_🫨_b").Root));
    }

    [Fact]
    public async Task SharesCompletedNodeIdentitiesUnderConcurrentReads()
    {
        var doc = DocumentationMarkdownDocument.Parse("- return: x\n\n# note\n\nText");
        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => doc.GetItemCandidates().ToArray())));
        Assert.All(results, result => Assert.Equal(results[0], result));
        Assert.NotEqual(doc.Root, DocumentationMarkdownDocument.Parse(doc.Text).Root);
    }

    [Fact]
    public void CancelsWithoutPublishingAndReportsDepthLimits()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => DocumentationMarkdownDocument.Parse("x", cancellation.Token));
        var doc = DocumentationMarkdownDocument.Parse("- return: x");
        Assert.Throws<OperationCanceledException>(() =>
        {
            doc.GetItemCandidates(cancellation.Token);
        });
        Assert.Single(doc.GetItemCandidates().ToArray());
        Assert.Throws<DocumentationMarkdownLimitException>(() => DocumentationMarkdownDocument.Parse(new string('>', 300) + " x"));
        var deep = DocumentationMarkdownDocument.Parse(new string('>', 3000) + " x", maximumDepth: 3010);
        Assert.Equal(DocumentationMarkdownKind.Quote, deep.Root.FirstChild!.Value.Kind);
        Assert.Throws<DocumentationMarkdownLimitException>(() => DocumentationMarkdownDocument.Parse(new string('*', 1000) + "x" + new string('*', 1000), maximumDepth: 100));
    }

    [Fact]
    public void HandlesLargeUnmatchedDelimiterInputs()
    {
        var input = string.Concat(Enumerable.Range(1, 400).Select(n => new string('`', n) + "x ")) + new string('[', 20000);
        var doc = DocumentationMarkdownDocument.Parse(input);
        Assert.NotNull(doc.Summary);
        AssertRanges(doc);
        Assert.NotNull(DocumentationMarkdownDocument.Parse(string.Concat(Enumerable.Repeat("[x](a(", 10000))).Summary);
    }

    [Fact]
    public void KeepsTreeAndRangeInvariantsForMixedIncompleteSyntax()
    {
        var random = new Random(7419);
        const string alphabet = "ab \t\n*_[]()`~<>\\&;#:-\0";
        for (var sample = 0; sample < 1500; sample++)
        {
            var input = new char[random.Next(1, 160)];
            for (var i = 0; i < input.Length; i++)
            {
                input[i] = alphabet[random.Next(alphabet.Length)];
            }

            var doc = DocumentationMarkdownDocument.Parse(new string(input));
            AssertRanges(doc);
            foreach (var candidate in doc.GetItemCandidates())
            {
                Assert.InRange(candidate.DescriptionSpan.Start, candidate.Node.Span.Start, candidate.Node.Span.End);
                Assert.InRange(candidate.DescriptionSpan.End, candidate.DescriptionSpan.Start, doc.Text.Length);
            }
        }
    }

    internal static void AssertRanges(DocumentationMarkdownDocument document)
    {
        var pending = new Stack<DocumentationMarkdownNode>();
        pending.Push(document.Root);
        while (pending.TryPop(out var node))
        {
            Assert.InRange(node.Span.Start, 0, document.Text.Length);
            Assert.InRange(node.Span.End, node.Span.Start, document.Text.Length);
            foreach (var child in node.Children)
            {
                Assert.Equal(node, child.Parent);
                Assert.InRange(child.Span.Start, node.Span.Start, node.Span.End);
                Assert.InRange(child.Span.End, child.Span.Start, node.Span.End);
                pending.Push(child);
            }
        }
    }

    // Structural test renderer only: intentionally no product URL resolution or publication policy.
    internal static string RenderSyntax(DocumentationMarkdownNode node, bool encodeUrls = false)
    {
        var builder = new StringBuilder();
        Render(node, builder, encodeUrls);
        return builder.ToString();
    }

    private static void Render(DocumentationMarkdownNode node, StringBuilder output, bool encodeUrls)
    {
        var tight = node.Kind == DocumentationMarkdownKind.Paragraph && node.Parent is { Kind: DocumentationMarkdownKind.ListItem } item && item.Parent!.Value.IsTight;
        var tag = node.Kind switch
        {
            DocumentationMarkdownKind.Paragraph => tight ? null : "p",
            DocumentationMarkdownKind.Heading => "h" + node.HeadingLevel,
            DocumentationMarkdownKind.BulletList => "ul",
            DocumentationMarkdownKind.OrderedList => "ol",
            DocumentationMarkdownKind.ListItem => "li",
            DocumentationMarkdownKind.Quote => "blockquote",
            DocumentationMarkdownKind.Code => "code",
            DocumentationMarkdownKind.Emphasis => "em",
            DocumentationMarkdownKind.Strong => "strong",
            DocumentationMarkdownKind.Link or DocumentationMarkdownKind.AutoLink => "a",
            _ => null,
        };
        if (node.Kind == DocumentationMarkdownKind.CodeBlock)
        {
            output.Append("<pre><code");
            if (!node.Text.IsEmpty)
            {
                output.Append(" class=\"language-").Append(Escape(node.Text)).Append('"');
            }

            output.Append('>');
        }

        if (tag is not null)
        {
            output.Append('<').Append(tag);
            if (node.Kind == DocumentationMarkdownKind.OrderedList && node.StartNumber != 1)
            {
                output.Append(" start=\"").Append(node.StartNumber).Append('"');
            }

            if (tag == "a")
            {
                output.Append(" href=\"").Append(Escape(encodeUrls ? EncodeUrl(node.Destination!) : node.Destination.AsSpan())).Append('"');
            }

            if (node.Title is { } title)
            {
                output.Append(" title=\"").Append(Escape(title)).Append('"');
            }

            output.Append('>');
            if (node.Kind is DocumentationMarkdownKind.Quote or DocumentationMarkdownKind.BulletList or DocumentationMarkdownKind.OrderedList ||
                (node.Kind == DocumentationMarkdownKind.ListItem && node.FirstChild is { } first && (!node.Parent!.Value.IsTight || first.Kind != DocumentationMarkdownKind.Paragraph)))
            {
                output.Append('\n');
            }
        }

        if (node.Kind is DocumentationMarkdownKind.Text or DocumentationMarkdownKind.Code or DocumentationMarkdownKind.AutoLink)
        {
            output.Append(Escape(node.Text));
        }

        if (node.Kind == DocumentationMarkdownKind.SoftBreak)
        {
            output.Append('\n');
        }

        if (node.Kind == DocumentationMarkdownKind.HardBreak)
        {
            output.Append("<br />\n");
        }

        foreach (var child in node.Children)
        {
            Render(child, output, encodeUrls);
        }

        if (tight && node.NextSibling is not null)
        {
            output.Append('\n');
        }

        if (tag is not null)
        {
            output.Append("</").Append(tag).Append('>');
            if (node.Kind is DocumentationMarkdownKind.Paragraph or DocumentationMarkdownKind.Heading or DocumentationMarkdownKind.Quote or DocumentationMarkdownKind.BulletList or DocumentationMarkdownKind.OrderedList or DocumentationMarkdownKind.ListItem)
            {
                output.Append('\n');
            }
        }

        if (node.Kind == DocumentationMarkdownKind.CodeBlock)
        {
            output.Append("</code></pre>\n");
        }
    }

    // Match the official examples' serialization only; no product URL policy or resolution.
    private static string EncodeUrl(string value)
    {
        var output = new StringBuilder();
        foreach (var rune in value.EnumerateRunes())
        {
            if (rune.IsAscii && (char.IsAsciiLetterOrDigit((char)rune.Value) || "!#$%&'()*+,-./:;=?@_~".Contains((char)rune.Value)))
            {
                output.Append((char)rune.Value);
            }
            else
            {
                output.Append(Uri.EscapeDataString(rune.ToString()));
            }
        }

        return output.ToString();
    }

    private static string Escape(ReadOnlySpan<char> text) => text.ToString().Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
