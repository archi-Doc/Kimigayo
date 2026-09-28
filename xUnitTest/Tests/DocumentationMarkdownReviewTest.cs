// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Security.Cryptography;
using System.Text;
using Kimi.Compiler.Documentation;
using Xunit;

namespace XunitTest;

#pragma warning disable xUnit1051 // Synchronous parser fixtures intentionally use the default token.

public class DocumentationMarkdownReviewTest
{
    [Theory]
    [InlineData("***a****b*", "<p><em><strong>a</strong></em><em>b</em></p>\n")]
    [InlineData("****a***b*", "<p><em><em><strong>a</strong></em>b</em></p>\n")]
    public void EmphasisMatchingUsesOriginalDelimiterRunLengths(string input, string expected)
        => Assert.Equal(expected, DocumentationMarkdownDocument.Parse(input).ToHtml());

    [Fact]
    public void NodeFormattingDoesNotTraverseParentChildCycles()
    {
        var document = DocumentationMarkdownDocument.Parse(new string('>', 1024) + " - note: **text**", maximumDepth: 2048);
        foreach (var node in new[] { default(DocumentationMarkdownNode), document.Root, document.Root.FirstChild!.Value })
        {
            Assert.InRange(node.ToString().Length, 1, 100);
        }

        Assert.InRange(DocumentationMarkdownDocument.Parse("- note: text").GetItemCandidates()[0].ToString().Length, 1, 500);
    }

    [Theory]
    [InlineData("prefix \\``x`", "<p>prefix `<code>x</code></p>\n")]
    [InlineData("prefix \\```x``", "<p>prefix `<code>x</code></p>\n")]
    [InlineData("prefix \\``x`y`", "<p>prefix `<code>x</code>y`</p>\n")]
    [InlineData("prefix `x\\`", "<p>prefix <code>x\\</code></p>\n")]
    public void EscapedBackticksConsumeOnlyTheEscapedCharacter(string input, string expected)
        => Assert.Equal(expected, DocumentationMarkdownDocument.Parse(input).ToHtml());

    [Theory]
    [InlineData("a*\0b*")]
    [InlineData("*b\0*a")]
    [InlineData("a_\0b_")]
    [InlineData("_b\0_a")]
    [InlineData("` \0 `")]
    [InlineData("[x](a\0b 't\0t')")]
    [InlineData("<https://example.com/\0&amp;>")]
    [InlineData("<custom:\0&amp;>")]
    [InlineData("```lang\0\nx\0y\n```")]
    public void NullReplacementPreservesSyntaxAndDisplay(string input)
    {
        var actual = DocumentationMarkdownDocument.Parse(input);
        var expected = DocumentationMarkdownDocument.Parse(input.Replace('\0', '�'));
        var options = new DocumentationHtmlOptions { LogicalSourceName = "api.kimi" };
        Assert.Equal(expected.ToHtml(options), actual.ToHtml(options));
        Assert.Equal(DocumentationMarkdownParserTest.RenderSyntax(expected.Root), DocumentationMarkdownParserTest.RenderSyntax(actual.Root));
        DocumentationMarkdownParserTest.AssertRanges(actual);
    }

    [Theory]
    [InlineData("/日本語/😀?q=あ#é", "/%E6%97%A5%E6%9C%AC%E8%AA%9E/%F0%9F%98%80?q=%E3%81%82#%C3%A9")]
    [InlineData("/a%20b/é?#", "/a%20b/%C3%A9?#")]
    [InlineData("/%E6%97%A5?q=%F0%9F%98%80", "/%E6%97%A5?q=%F0%9F%98%80")]
    public void UrlEncodingPreservesUtf8AndExistingEscapes(string input, string expected)
        => Assert.Equal(expected, DocumentationLinks.Resolve(input, new()));

    [Theory]
    [InlineData("[](u)", 2)]
    [InlineData("[x](u)", 3)]
    [InlineData("[**x**](u)", 4)]
    public void LinkDepthLimitMatchesItsActualTree(string input, int depth)
    {
        var document = DocumentationMarkdownDocument.Parse(input, maximumDepth: depth);
        DocumentationMarkdownParserTest.AssertRanges(document);
        Assert.Throws<DocumentationMarkdownLimitException>(() => DocumentationMarkdownDocument.Parse(input, maximumDepth: depth - 1));
    }

    [Theory]
    [InlineData("[x](a\0b)", "a�b")]
    [InlineData("[x](a(b\0c))", "a(b�c)")]
    [InlineData("[x](<a\0b>)", "a�b")]
    [InlineData("<https://example.com/a\0b>", "https://example.com/a�b")]
    public void NullCharactersAreReplacedBeforeLinkRecognition(string input, string destination)
    {
        var document = DocumentationMarkdownDocument.Parse(input);
        var link = Assert.Single(document.Root.FirstChild!.Value.Children);
        Assert.True(link.Kind is DocumentationMarkdownKind.Link or DocumentationMarkdownKind.AutoLink);
        Assert.Equal(destination, link.Destination);
        Assert.Equal(input, document.Text);
        Assert.Equal(input.Length, link.Span.End);
        Assert.DoesNotContain('\0', document.ToHtml(new() { LogicalSourceName = "api.kimi" }));
        DocumentationMarkdownParserTest.AssertRanges(document);
    }

    [Fact]
    public void DisabledAutolinksRetainSpellingWithNullReplacement()
    {
        var document = DocumentationMarkdownDocument.Parse("<custom:a\0b&amp;>");
        Assert.Equal(DocumentationMarkdownKind.AutoLink, document.Root.FirstChild!.Value.FirstChild!.Value.Kind);
        Assert.Equal("<p>&lt;custom:a�b&amp;amp;&gt;</p>\n", document.ToHtml());
    }

    [Fact]
    public void DeterministicInlineInteractionsMatchPinnedCommonMarkResults()
    {
        // Offline snapshots from the official commonmark.js 0.31.2 implementation.
        // Inputs exclude the profile's omitted syntax and known Unicode-version
        // differences. Each UTF-8 value is followed by a zero byte for framing.
        var random = new Random(20260920);
        string[] tokens = ["a", "b", " ", "*", "**", "_", "__", "[", "]", "(", ")", "`", "``", "\\", "&amp;"];
        using var inputHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var htmlHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (var sample = 0; sample < 10000; sample++)
        {
            var input = "prefix " + string.Concat(Enumerable.Range(0, random.Next(1, 40)).Select(_ => tokens[random.Next(tokens.Length)])) + " suffix";
            var document = DocumentationMarkdownDocument.Parse(input);
            DocumentationMarkdownParserTest.AssertRanges(document);
            var actual = DocumentationMarkdownParserTest.RenderSyntax(document.Root, true);
            inputHash.AppendData(Encoding.UTF8.GetBytes(input));
            inputHash.AppendData([0]);
            htmlHash.AppendData(Encoding.UTF8.GetBytes(actual));
            htmlHash.AppendData([0]);
        }

        Assert.Equal("DBF9B6CD84AFB01D0165E9781DFE594151B915F47C54BAF4526C936C06C5D404", Convert.ToHexString(inputHash.GetHashAndReset()));
        Assert.Equal("64D0D07976FC72BD1771B673229DBF35A802F1079C987E65DDA6B8B218B7E5B3", Convert.ToHexString(htmlHash.GetHashAndReset()));
    }
}
