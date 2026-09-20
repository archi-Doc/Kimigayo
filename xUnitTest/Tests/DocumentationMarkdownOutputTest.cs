// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Documentation;
using Xunit;

namespace XunitTest;

#pragma warning disable xUnit1051 // Explicit cancellation cases and synchronous rendering fixtures.

public class DocumentationMarkdownOutputTest
{
    [Theory]
    [InlineData("guide.md#sec", "src/api.kimi", "/src/guide.md#sec")]
    [InlineData("api%23v2.kimi?q", "src/api.kimi", "/src/api%23v2.kimi?q")]
    [InlineData("%252e%252e/file", "src/api.kimi", "/src/%252e%252e/file")]
    [InlineData("%2e%2e/file", "src/api.kimi", "/file")]
    [InlineData(".", "src/api.kimi", "/src/")]
    [InlineData("%2e%2e", "src/nested/api.kimi", "/src/")]
    [InlineData("%2e%2e/%2e%2e/file", "src/api.kimi", null)]
    [InlineData("a%2fb", "src/api.kimi", null)]
    [InlineData("a%5cb", "src/api.kimi", null)]
    [InlineData("a%00b", "src/api.kimi", null)]
    [InlineData("a%C2%85b", "src/api.kimi", null)]
    [InlineData("%FF", "src/api.kimi", null)]
    [InlineData("bad%2", "src/api.kimi", null)]
    [InlineData("%20a%20", "src/api.kimi", "/src/%20a%20")]
    [InlineData("a", "src%23/dir#/api.kimi", "/src%2523/dir%23/a")]
    [InlineData("guide.md", null, null)]
    [InlineData("", null, "")]
    [InlineData("?#", null, "?#")]
    [InlineData("?q", null, "?q")]
    [InlineData("#f", null, "#f")]
    [InlineData("/guide.md", null, "/guide.md")]
    [InlineData("//host/path", null, null)]
    [InlineData("https:example.com", null, null)]
    [InlineData("https:///path", null, null)]
    [InlineData("HTTPS://example.com/a%20b?q=1&x=2#", null, "HTTPS://example.com/a%20b?q=1&x=2#")]
    [InlineData("https://[::1]/a", null, "https://[::1]/a")]
    [InlineData("mailto:a@example.com", null, "mailto:a@example.com")]
    [InlineData("javascript:alert(1)", null, null)]
    [InlineData(" https://example.com", null, null)]
    [InlineData("https://example.com ", null, null)]
    [InlineData("https://example.com/\\a", null, null)]
    [InlineData("https://example.com/a\u0085", null, null)]
    [InlineData("/a%2fb", null, null)]
    [InlineData("./a:b", "api.kimi", "/a:b")]
    [InlineData("1:a", "api.kimi", null)]
    public void ResolvesAndSerializesComponents(string input, string? source, string? expected)
        => Assert.Equal(expected, DocumentationMarkdown.ResolveLink(input, source));

    [Theory]
    [InlineData("", "https://docs.test/api/page.html?old")]
    [InlineData("#f", "https://docs.test/api/page.html?old#f")]
    [InlineData("?q", "https://docs.test/api/page.html?q")]
    [InlineData("?#", "https://docs.test/api/page.html?#")]
    [InlineData("#", "https://docs.test/api/page.html?old#")]
    public void EmptyPathsUseDisplayPageEvenWithDifferentHtmlBase(string input, string expected)
    {
        foreach (var logicalName in new string?[] { null, "src/api.kimi" })
        {
            var options = new DocumentationHtmlOptions { LogicalSourceName = logicalName, PageUrl = "https://docs.test/api/page.html?old#previous", HtmlBaseUrl = "https://cdn.test/other/" };
            Assert.Equal(expected, DocumentationLinks.Resolve(input, options));
        }
    }

    [Fact]
    public void MapsStructuredTargetsWithoutReinterpretingLogicalData()
    {
        var project = new object();
        DocumentationLinkTarget? seen = null;
        var options = new DocumentationHtmlOptions
        {
            ProjectIdentity = project,
            LogicalSourceName = "src/api.kimi",
            MapSourcePath = target =>
            {
                seen = target;
                return "a:b#%.html";
            },
        };
        Assert.Equal("./a:b%23%25.html?#", DocumentationLinks.Resolve("guide.md?#", options));
        Assert.Same(project, seen!.ProjectIdentity);
        Assert.Equal("src/guide.md", seen.LogicalPath);
        Assert.Equal(string.Empty, seen.Query);
        Assert.Equal(string.Empty, seen.Fragment);
        var absent = DocumentationLinks.ResolveSource("guide.md", "src/api.kimi")!;
        Assert.Null(absent.Query);
        Assert.Null(absent.Fragment);
        Assert.Null(DocumentationLinks.Resolve("guide.md", options with { MapSourcePath = _ => null }));
        Assert.Null(DocumentationLinks.Resolve("guide.md", options with { MapSourcePath = _ => "//host/path" }));
        Assert.Equal("/elsewhere/guide.html", DocumentationLinks.Resolve("guide.md", options with { MapSourcePath = _ => "/elsewhere/guide.html" }));
    }

    [Theory]
    [InlineData(" javascript:alert(1)")]
    [InlineData("javascript:alert(1)")]
    [InlineData("//host/path")]
    [InlineData("/x%2fsecret")]
    [InlineData("https:example.com")]
    [InlineData("/bad%")]
    public void ValidatesRewriteOutput(string rewrite)
    {
        var document = DocumentationMarkdownDocument.Parse("[*label*](https://example.com) <https://example.com>");
        Assert.Equal("<p><em>label</em> &lt;https://example.com&gt;</p>\n", document.ToHtml(new() { RewriteLink = _ => rewrite }));
    }

    [Fact]
    public void EscapesTextCodeTitlesAndAttributesAndKeepsRejectedAutolinkSpelling()
    {
        var document = DocumentationMarkdownDocument.Parse("[x](<https://example.com/a b?q=1&y=2> '\"title\"') <Foo::bar> `\"<&`\n\n```a\"b\n<&\n```");
        var html = document.ToHtml();
        Assert.Contains("href=\"https://example.com/a%20b?q=1&amp;y=2\" title=\"&quot;title&quot;\"", html);
        Assert.Contains("&lt;Foo::bar&gt;", html);
        Assert.Contains("<code>&quot;&lt;&amp;</code>", html);
        Assert.Contains("class=\"language-a&quot;b\"", html);
        Assert.Contains("&lt;&amp;\n</code>", html);
        Assert.Contains("<a href=\"Foo::bar\">", document.ToHtml(new() { AllowScheme = scheme => scheme == "foo" }));
    }

    [Fact]
    public void IterativeRenderingIsStableConcurrentAndCancellable()
    {
        var document = DocumentationMarkdownDocument.Parse(new string('>', 2048) + " **text**", maximumDepth: 4096);
        var first = document.ToHtml();
        Assert.Equal(2048, first.Split("<blockquote>").Length - 1);
        Parallel.For(0, 16, _ => Assert.Equal(first, document.ToHtml()));
        Assert.Throws<OperationCanceledException>(() => document.ToHtml(cancellationToken: new CancellationToken(true)));
        using var cancellation = new CancellationTokenSource();
        var link = DocumentationMarkdownDocument.Parse("[x](https://example.com)");
        var original = link.Root.FirstChild!.Value.FirstChild!.Value;
        var cancellingOptions = new DocumentationHtmlOptions
        {
            RewriteLink = url =>
            {
                cancellation.Cancel();
                return url;
            },
        };
        Assert.Throws<OperationCanceledException>(() => link.ToHtml(cancellingOptions, cancellation.Token));
        Assert.Equal("<p><a href=\"https://example.com\">x</a></p>\n", link.ToHtml());
        Assert.Equal(original, link.Root.FirstChild!.Value.FirstChild!.Value);
        Assert.Throws<ArgumentException>(() => DocumentationLinks.Resolve("#a", new() { HtmlBaseUrl = "https://other.test/" }));
    }

    [Theory]
    [InlineData("struct S", "self", DocumentationMarkdownItemKind.Unknown)]
    [InlineData("group G", "self: i32", DocumentationMarkdownItemKind.Parameter)]
    public void ProductFacadeUsesBindingRolesAndReclassifiesAfterBinding(string container, string parameter, DocumentationMarkdownItemKind expected)
    {
        var tree = DocumentationCommentTest.Parse(container + "\n    /// - self: description\n    /// - note: parameter\n    /// # note\n    /// Section\n    public func f(" + parameter + ", note?: i32) => ()");
        var comment = Assert.Single(Assert.Single(tree.DocumentationSources).Comments);
        var product = DocumentationMarkdown.Parse(comment);
        Assert.Equal(DocumentationMarkdownItemKind.Unclassified, product.Items.Span[0].Kind);
        Assert.True(tree.Compilation.Bind().IsComplete);
        var items = product.Items.ToArray();
        Assert.Equal(expected, items[0].Kind);
        Assert.Equal(DocumentationMarkdownItemKind.Parameter, items[1].Kind);
        Assert.Equal(DocumentationMarkdownItemKind.Standard, items[2].Kind);
        Assert.Equal(product.Document.GetItemCandidates()[0].Node, items[0].Node);
    }

    [Fact]
    public void ProductAssemblyHasNoMarkdigReference()
        => Assert.DoesNotContain(typeof(DocumentationMarkdown).Assembly.GetReferencedAssemblies(), assembly => assembly.Name == "Markdig");

    [Fact]
    public void ReentrantCallbacksAndChangedOutputSettingsDoNotShareOutput()
    {
        var document = DocumentationMarkdownDocument.Parse("[outer](https://example.com)");
        var nested = DocumentationMarkdownDocument.Parse("**nested**");
        var calls = 0;
        var options = new DocumentationHtmlOptions
        {
            RewriteLink = url =>
            {
                calls++;
                Assert.Equal("<p><strong>nested</strong></p>\n", nested.ToHtml());
                return url + "/changed";
            },
        };
        Assert.Equal("<p><a href=\"https://example.com/changed\">outer</a></p>\n", document.ToHtml(options));
        Assert.Equal(1, calls);
        Assert.Equal("<p><a href=\"https://example.com\">outer</a></p>\n", document.ToHtml());
        Assert.Equal("<p>outer</p>\n", document.ToHtml(options with { RewriteLink = _ => null }));
    }

    [Fact]
    public void ProductMappingReceivesOwningModuleIdentity()
    {
        var tree = DocumentationCommentTest.Parse("/// [guide](guide.md)\nfunc f() => ()");
        var comment = Assert.Single(Assert.Single(tree.DocumentationSources).Comments);
        var product = DocumentationMarkdown.Parse(comment);
        DocumentationLinkTarget? seen = null;
        var options = new DocumentationHtmlOptions
        {
            MapSourcePath = target =>
            {
                seen = target;
                return "/published/guide.html";
            },
        };
        Assert.Contains("href=\"/published/guide.html\"", product.ToHtml(options));
        Assert.Same(tree, seen!.ProjectIdentity);
    }
}
