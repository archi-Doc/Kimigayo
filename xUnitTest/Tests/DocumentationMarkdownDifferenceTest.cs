// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Documentation;
using Xunit;

namespace XunitTest;

#pragma warning disable xUnit1051 // Synchronous fixtures intentionally use the default token.

public class DocumentationMarkdownDifferenceTest
{
    [Theory]
    [InlineData("")]
    [InlineData("u")]
    public void NestedAutolinksDoNotCreateNestedLinks(string destination)
    {
        var input = "[<https://a.b>](" + destination + ")";
        var doc = DocumentationMarkdownDocument.Parse(input);
        Assert.Equal("<p>[<a href=\"https://a.b\">https://a.b</a>](" + destination + ")</p>\n", DocumentationMarkdownParserTest.RenderSyntax(doc.Root));
        Assert.DoesNotContain(doc.Summary!.Value.Children, x => x.Kind == DocumentationMarkdownKind.Link);
    }

    [Fact]
    public void EmptyTitlesRetainSyntaxMetadata()
    {
        var doc = DocumentationMarkdownDocument.Parse("[x](u \"\") [y](u)");
        var links = doc.Summary!.Value.Children.Where(x => x.Kind == DocumentationMarkdownKind.Link).ToArray();
        Assert.Equal(string.Empty, links[0].Title);
        Assert.Null(links[1].Title);
        Assert.Equal("<p><a href=\"u\" title=\"\">x</a> <a href=\"u\">y</a></p>\n", DocumentationMarkdownParserTest.RenderSyntax(doc.Root));
    }

    [Theory]
    [InlineData("example\n---", "<p>example\n---</p>\n")]
    [InlineData("    # example", "<p># example</p>\n")]
    [InlineData("    - a", "<p>- a</p>\n")]
    [InlineData("- a\nb", "<ul>\n<li>a</li>\n</ul>\n<p>b</p>\n")]
    [InlineData("> a\nb", "<blockquote>\n<p>a</p>\n</blockquote>\n<p>b</p>\n")]
    [InlineData("10. a\n   b", "<ol start=\"10\">\n<li>a</li>\n</ol>\n<p>b</p>\n")]
    [InlineData("[x][r]\n\n[r]: guide.md", "<p>[x][r]</p>\n<p>[r]: guide.md</p>\n")]
    [InlineData("![図](guide.md)", "<p>!<a href=\"guide.md\">図</a></p>\n")]
    [InlineData("---", "<p>---</p>\n")]
    [InlineData("&copy; &AMP;", "<p>&amp;copy; &amp;AMP;</p>\n")]
    [InlineData("<T>\n*説明*", "<p>&lt;T&gt;\n<em>説明</em></p>\n")]
    [InlineData("-     a", "<ul>\n<li>a</li>\n</ul>\n")]
    public void RetainsLimitedProfileBoundaries(string input, string expected)
        => Assert.Equal(expected, DocumentationMarkdownParserTest.RenderSyntax(DocumentationMarkdownDocument.Parse(input).Root));

    [Fact]
    public void ThematicBreakRemovalExposesThreeNestedEmptyLists()
    {
        var doc = DocumentationMarkdownDocument.Parse("- - -");
        var parent = doc.Root;
        for (var depth = 0; depth < 3; depth++)
        {
            var list = Assert.Single(parent.Children);
            Assert.Equal(DocumentationMarkdownKind.BulletList, list.Kind);
            parent = Assert.Single(list.Children);
            Assert.Equal(DocumentationMarkdownKind.ListItem, parent.Kind);
        }

        Assert.Empty(parent.Children);
    }

    [Fact]
    public void SchemePolicyDoesNotChangeAutolinkSyntaxOrOriginalSpelling()
    {
        const string input = "<Key:Value>";
        var doc = DocumentationMarkdownDocument.Parse(input);
        var link = Assert.Single(doc.Summary!.Value.Children);
        Assert.Equal(DocumentationMarkdownKind.AutoLink, link.Kind);
        Assert.Equal("Key:Value", link.Destination);
        Assert.Equal(input, doc.Text.Substring(link.Span.Start, link.Span.Length));
    }
}
