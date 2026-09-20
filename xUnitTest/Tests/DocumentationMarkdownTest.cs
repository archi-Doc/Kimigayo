// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Documentation;
using Xunit;

namespace XunitTest;

#pragma warning disable xUnit1051 // Synchronous documentation fixtures exercise default tokens.

public class DocumentationMarkdownTest
{
    [Fact]
    public void DisablesHtmlParsingWhilePreservingOtherCommonMarkRules()
    {
        var doc = Parse("<T>\n*emphasis*\n\n<script>alert(1)</script>\n\n<https://example.com>\n\n| a | b |\n| - | - |\n\n```kimi\n/// - return: not an item\n```\n");
        var html = doc.ToHtml();
        Assert.Contains("&lt;T&gt;\n<em>emphasis</em>", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.Contains("<a href=\"https://example.com\">", html);
        Assert.DoesNotContain("<table", html);
        Assert.DoesNotContain("<script", html);
        Assert.Contains("class=\"language-kimi\"", html);
        Assert.Empty(doc.Items.ToArray());
    }

    // Representative fixed CommonMark 0.31.2 examples: code spans, emphasis,
    // reference links, entities, lists and block quotes; HTML is tested separately.
    [Theory]
    [InlineData("` foo   bar `", "<p><code>foo   bar</code></p>\n")]
    [InlineData("***foo***", "<p><em><strong>foo</strong></em></p>\n")]
    [InlineData("[foo][bar]\n\n[bar]: /url \"title\"", "<p>[foo][bar]</p>\n<p>[bar]: /url &quot;title&quot;</p>\n")]
    [InlineData("&copy; &amp;", "<p>&amp;copy; &amp;</p>\n")]
    [InlineData("> foo\n> bar", "<blockquote>\n<p>foo\nbar</p>\n</blockquote>\n")]
    [InlineData("- foo\n- bar", "<ul>\n<li>foo</li>\n<li>bar</li>\n</ul>\n")]
    public void MatchesFixedLimitedProfileExamples(string markdown, string html)
        => Assert.Equal(html, Parse(markdown).ToHtml().Replace("\r\n", "\n"));

    [Fact]
    public void ExtractsOnlyRootItemsPreservingDuplicatesAndContainment()
    {
        var doc = Parse("Summary.\n\n# return\n\nDescription.\n\n- note: first\n  - warning: nested\n- note: second\n- Unknown: retained\n- return:bad\n- **return**: decorated\n\n> - abort: quoted\n\n## example\n\n```kimi\n- abort: code\n```\n\n# Other\n\nText.");
        Assert.NotNull(doc.Summary);
        Assert.Equal(new[] { "return", "note", "note", "Unknown", "example" }, doc.Items.ToArray().Select(x => x.Name));
        Assert.Equal(doc.Document.Root.Children.ElementAt(1), doc.Items.ToArray()[0].Node);
        Assert.True(doc.Items.ToArray()[0].DescriptionSpan.End > doc.Items.ToArray()[4].DescriptionSpan.Start);
        Assert.Contains("<h3>example</h3>", doc.ToHtml());
        Assert.Contains("aria-level=\"8\"", doc.ToHtml(6));
        Assert.Equal(1, doc.Items.ToArray()[0].Node.HeadingLevel);
    }

    [Theory]
    [InlineData("- return:", true)]
    [InlineData("- return: **bold**", true)]
    [InlineData("- return:\ttext", true)]
    [InlineData("- return:\n  text", true)]
    [InlineData("- return:**bold**", false)]
    [InlineData("- Returns: text", false)]
    [InlineData("- return： text", false)]
    [InlineData("# **return**", false)]
    [InlineData("# return extra", false)]
    public void UsesExactLabelsAndAsciiDelimiters(string body, bool recognized)
        => Assert.Equal(recognized ? 1 : 0, Parse(body).Items.ToArray().Count(x => x.Kind == DocumentationMarkdownItemKind.Standard));

    [Fact]
    public void DescriptionRangesPreserveEscapesAndSourcePositions()
    {
        var doc = Parse("- return\\: text");
        var item = Assert.Single(doc.Items.ToArray());
        Assert.Equal(" text", doc.Comment.GetText().Text[item.DescriptionSpan.Start..item.DescriptionSpan.End]);
    }

    [Fact]
    public void MatchesExternalNamesAndReportsUnmatchedItemsOutsideLanguageDiagnostics()
    {
        var doc = Parse("- `value`: input\n- `by`: scale\n- `factor`: internal\n- `self`: receiver", "func scale(value?: i32, by => factor: i32) -> i32 => value * factor");
        Assert.Equal(new[] { 1, 1, 0, 0 }, doc.Items.ToArray().Select(x => x.ParameterMatchCount));
        Assert.Equal(2, doc.GetDiagnostics().Count());
        Assert.All(doc.GetDiagnostics(), x => Assert.Equal('`', x.Source.SourceText[x.Span.Start + 2]));
    }

    [Fact]
    public void KeepsAmbiguousNamespacesAndUnsafeObligations()
    {
        var doc = Parse("- `T`: ambiguous", "unsafe func f<T>(T?: i32) => ()");
        Assert.Equal(2, Assert.Single(doc.Items.ToArray()).ParameterMatchCount);
        Assert.Equal(new[] { "AmbiguousDocumentationParameter", "MissingSafetyDocumentation" }, doc.GetDiagnostics().Select(x => x.Code));
        Assert.Empty(Parse("# safety\n\nKeep memory alive.", "unsafe func f() => ()").GetDiagnostics());
    }

    [Theory]
    [InlineData("guide.md#part", "src/api.kimi", "/src/guide.md#part")]
    [InlineData("../guide.md", "src/api.kimi", "/guide.md")]
    [InlineData("../../escape", "src/api.kimi", null)]
    [InlineData("guide.md", null, null)]
    [InlineData("#part", null, "#part")]
    [InlineData("?view=1", "src/api.kimi", "?view=1")]
    [InlineData("https://example.com", null, "https://example.com")]
    [InlineData("javascript:alert(1)", "src/api.kimi", null)]
    [InlineData("//example.com", "src/api.kimi", null)]
    public void ResolvesLinksFromLogicalSources(string url, string? source, string? expected)
        => Assert.Equal(expected, DocumentationMarkdown.ResolveLink(url, source));

    [Fact]
    public void FiltersUnsafeLinksAfterRewritingAndKeepsReferenceDefinitionsLocal()
    {
        var first = Parse("[x](javascript:alert%281%29)\n\n[good](https://example.com)\n\n[id]: /first");
        Assert.DoesNotContain("javascript:", first.ToHtml());
        Assert.DoesNotContain("javascript:", first.ToHtml(rewriteLink: _ => "javascript:alert(1)"));
        Assert.Contains("[id]", Parse("[id]").ToHtml());
        Assert.Null(Parse("# example").Summary);
    }

    [Fact]
    public void RenderingResolvesSourceRelativeLinksAndDisablesGeneratedRelativeLinks()
    {
        var c = Kimi.Compiler.Compilation.CreateForTest();
        c.CollectDocumentation = true;
        var tree = c.Kotonoha;
        tree.CreateCodeContext().Parse(tree.RootKoto, new Kimi.Compiler.SourceDocument("src/api.kimi", "/// [guide](guide.md)\nstruct S"));
        tree.CreateCodeContext().Parse(tree.RootKoto, new Kimi.Compiler.SourceDocument("generated.kimi", "/// [guide](guide.md)\nstruct T"), "mod");
        var comments = tree.DocumentationSources.SelectMany(x => x.Comments).ToArray();
        Assert.Contains("href=\"/src/guide.md\"", DocumentationMarkdown.Parse(comments[0]).ToHtml());
        Assert.DoesNotContain("guide.md", DocumentationMarkdown.Parse(comments[1]).ToHtml());
        Assert.Equal("<p>guide</p>\n", DocumentationMarkdown.Parse(comments[1]).ToHtml().Replace("\r\n", "\n"));
    }

    [Fact]
    public void ExcludedDocumentationDoesNotProduceMarkdownDiagnostics()
    {
        var tree = DocumentationCommentTest.Parse("#if false\n    /// - `missing`: description\n    let invalid =\n()");
        var comment = Assert.Single(Assert.Single(tree.DocumentationSources).Comments);
        Assert.Empty(DocumentationMarkdown.Parse(comment).GetDiagnostics());
    }

    private static DocumentationMarkdown Parse(string body, string declaration = "func f() => ()")
    {
        var source = string.Join("\n", body.Split('\n').Select(x => "/// " + x)) + "\n" + declaration;
        var tree = DocumentationCommentTest.Parse(source);
        _ = tree.Compilation.Bind();
        return DocumentationMarkdown.Parse(Assert.Single(Assert.Single(tree.DocumentationSources).Comments));
    }
}
