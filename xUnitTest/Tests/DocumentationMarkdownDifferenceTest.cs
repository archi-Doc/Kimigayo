// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Documentation;
using Markdig;
using Xunit;

namespace XunitTest;

#pragma warning disable xUnit1051 // Synchronous fixtures intentionally use the default token.

public class DocumentationMarkdownDifferenceTest
{
    [Theory]
    [InlineData("")]
    [InlineData("u")]
    public void RecordsMarkdigNestedAutolinksAgainstTheNoNestedLinksRule(string destination)
    {
        var input = "[<https://a.b>](" + destination + ")";
        var doc = DocumentationMarkdownDocument.Parse(input);
        Assert.Equal("<p>[<a href=\"https://a.b\">https://a.b</a>](" + destination + ")</p>\n", DocumentationMarkdownParserTest.RenderSyntax(doc.Root));
        Assert.Equal("<p><a href=\"" + destination + "\"><a href=\"https://a.b\">https://a.b</a></a></p>\n", Markdown.ToHtml(input));
        Assert.DoesNotContain(doc.Summary!.Value.Children, x => x.Kind == DocumentationMarkdownKind.Link);
    }

    [Fact]
    public void RecordsMarkdigEmptyTitleSerializationWithoutLosingSyntaxMetadata()
    {
        var doc = DocumentationMarkdownDocument.Parse("[x](u \"\") [y](u)");
        var links = doc.Summary!.Value.Children.Where(x => x.Kind == DocumentationMarkdownKind.Link).ToArray();
        Assert.Equal(string.Empty, links[0].Title);
        Assert.Null(links[1].Title);
        Assert.Equal("<p><a href=\"u\" title=\"\">x</a> <a href=\"u\">y</a></p>\n", DocumentationMarkdownParserTest.RenderSyntax(doc.Root));
        Assert.Equal("<p><a href=\"u\">x</a> <a href=\"u\">y</a></p>\n", Markdown.ToHtml(doc.Text));
    }

    [Theory]
    [InlineData("example\n---", "<p>example\n---</p>\n", "<h2>example</h2>\n")]
    [InlineData("    # example", "<p># example</p>\n", "<pre><code># example\n</code></pre>\n")]
    [InlineData("    - a", "<p>- a</p>\n", "<pre><code>- a\n</code></pre>\n")]
    [InlineData("- a\nb", "<ul>\n<li>a</li>\n</ul>\n<p>b</p>\n", "<ul>\n<li>a\nb</li>\n</ul>\n")]
    [InlineData("> a\nb", "<blockquote>\n<p>a</p>\n</blockquote>\n<p>b</p>\n", "<blockquote>\n<p>a\nb</p>\n</blockquote>\n")]
    [InlineData("10. a\n   b", "<ol start=\"10\">\n<li>a</li>\n</ol>\n<p>b</p>\n", "<ol start=\"10\">\n<li>a\nb</li>\n</ol>\n")]
    [InlineData("[x][r]\n\n[r]: guide.md", "<p>[x][r]</p>\n<p>[r]: guide.md</p>\n", "<p><a href=\"guide.md\">x</a></p>\n")]
    [InlineData("![図](guide.md)", "<p>!<a href=\"guide.md\">図</a></p>\n", "<p><img src=\"guide.md\" alt=\"図\" /></p>\n")]
    [InlineData("---", "<p>---</p>\n", "<hr />\n")]
    [InlineData("&copy; &AMP;", "<p>&amp;copy; &amp;AMP;</p>\n", "<p>© &amp;</p>\n")]
    [InlineData("<T>\n*説明*", "<p>&lt;T&gt;\n<em>説明</em></p>\n", "<T>\n*説明*\n")]
    [InlineData("-     a", "<ul>\n<li>a</li>\n</ul>\n", "<ul>\n<li>\n<pre><code>a\n</code></pre>\n</li>\n</ul>\n")]
    public void RecordsIntentionalProfileDifferences(string input, string profile, string commonMark)
    {
        Assert.Equal(profile, DocumentationMarkdownParserTest.RenderSyntax(DocumentationMarkdownDocument.Parse(input).Root));
        // Full CommonMark defaults here expose the deliberately removed rules, including HTML.
        Assert.Equal(commonMark, Markdown.ToHtml(input).Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.NotEqual(profile, commonMark);
    }

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
        Assert.Equal("<hr />\n", Markdown.ToHtml(doc.Text));
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
        // §7 output rejection belongs to the future renderer; do not simulate it here.
        Assert.Equal("<p><a href=\"Key:Value\">Key:Value</a></p>\n", Markdown.ToHtml(input));
    }
}
