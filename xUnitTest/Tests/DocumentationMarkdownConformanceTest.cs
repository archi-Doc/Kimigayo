// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Security.Cryptography;
using System.Text.Json;
using Kimi.Compiler.Documentation;
using Markdig;
using Xunit;

namespace XunitTest;

#pragma warning disable xUnit1051 // Synchronous parser fixtures intentionally use the default token.

public class DocumentationMarkdownConformanceTest
{
    private static readonly MarkdownPipeline ComparisonPipeline = new MarkdownPipelineBuilder().DisableHtml().UsePreciseSourceLocation().Build();

    public static IEnumerable<object[]> CommonExamples()
    {
        using var exclusions = ReadData("exclusions.json");
        var excluded = exclusions.RootElement.EnumerateArray().Select(x => x.GetProperty("example").GetInt32()).ToHashSet();
        using var corpus = ReadData("commonmark-0.31.2.json");
        foreach (var example in corpus.RootElement.EnumerateArray())
        {
            var id = example.GetProperty("example").GetInt32();
            if (!excluded.Contains(id))
            {
                yield return [id, example.GetProperty("section").GetString()!, example.GetProperty("markdown").GetString()!, example.GetProperty("html").GetString()!];
            }
        }
    }

    public static IEnumerable<object[]> RenderingExamples()
    {
        // URL policy/resolution intentionally differs from CommonMark serialization.
        // The separate output tests cover those rules; these exact official HTML
        // expectations cover every retained example without link nodes.
        foreach (var example in CommonExamples())
        {
            var document = DocumentationMarkdownDocument.Parse((string)example[2]);
            var pending = new Stack<DocumentationMarkdownNode>();
            pending.Push(document.Root);
            var links = false;
            while (pending.TryPop(out var node))
            {
                links |= node.Kind is DocumentationMarkdownKind.Link or DocumentationMarkdownKind.AutoLink;
                foreach (var child in node.Children)
                {
                    pending.Push(child);
                }
            }

            if (!links)
            {
                yield return example;
            }
        }
    }

    [Theory]
    [MemberData(nameof(RenderingExamples))]
    public void ProductRendererConformsToOfficialNonLinkExamples(int id, string section, string input, string expected)
    {
        var actual = DocumentationMarkdownDocument.Parse(input).ToHtml(new() { DeclarationHeadingLevel = 0 });
        Assert.True(expected == actual, $"Product renderer: {id}, {section}\nExpected: {expected}\nActual: {actual}");
    }

    [Theory]
    [MemberData(nameof(CommonExamples))]
    public void ConformsToRetainedCommonMarkExamples(int id, string section, string input, string expected)
    {
        var document = DocumentationMarkdownDocument.Parse(input);
        var actual = DocumentationMarkdownParserTest.RenderSyntax(document.Root, true);
        Assert.True(expected == actual, $"CommonMark 0.31.2 example {id}, {section}\nExpected: {expected}\nActual: {actual}");
        DocumentationMarkdownParserTest.AssertRanges(document);
    }

    [Theory]
    [MemberData(nameof(CommonExamples))]
    public void ComparesMarkdigOnTheSameRetainedExamples(int id, string section, string input, string expected)
    {
        var independent = DocumentationMarkdownParserTest.RenderSyntax(DocumentationMarkdownDocument.Parse(input).Root, true);
        var markdig = Markdown.ToHtml(input, ComparisonPipeline).Replace("\r\n", "\n", StringComparison.Ordinal);
        // The official expected result remains the oracle for both implementations.
        Assert.True(NormalizeLayout(expected) == NormalizeLayout(markdig), $"Markdig 1.3.2, example {id}, {section}\nExpected: {expected}\nActual: {markdig}");
        Assert.Equal(expected, independent);
    }

    [Fact]
    public void ComparesGeneratedDelimiterAndLinkInteractions()
    {
        string[] markers = ["*", "**", "***", "_", "__", "___"];
        string[] contents = ["a", " a", "a ", "a b", "日本語", "a_b", "a*b", "a **b** c", "a _b_ c", "a`*`b", "a [b](u) c", "a\\*b"];
        foreach (var left in markers)
        {
            foreach (var right in markers)
            {
                foreach (var content in contents)
                {
                    Compare("prefix " + left + content + right + " suffix");
                }
            }
        }

        string[] labels = ["x", "*x*", "**x**", "`x`", "a [b] c", "[b](u)", "a\\]b", "a&amp;b"];
        string[] destinations = [string.Empty, "u", "a(b)c", "a(b(c)d)e", "a\\(b", "<a b>", "<a\\>b>", "u \"title\"", "u 'title'", "u (title)", " \"title\"", "u\n\"title\"", "u \"unclosed", "a(b", "a\\)b", "<> \"title\"", " \"two words\"", "a<b", "a(b<c)d", "<a<b>"];
        foreach (var label in labels)
        {
            foreach (var destination in destinations)
            {
                Compare("prefix [" + label + "](" + destination + ") suffix");
            }
        }

        static void Compare(string input)
        {
            var document = DocumentationMarkdownDocument.Parse(input);
            var independent = DocumentationMarkdownParserTest.RenderSyntax(document.Root, true);
            var markdig = Markdown.ToHtml(input, ComparisonPipeline).Replace("\r\n", "\n", StringComparison.Ordinal);
            Assert.True(NormalizeLayout(independent) == NormalizeLayout(markdig), $"Input: {JsonSerializer.Serialize(input)}\nIndependent: {independent}\nMarkdig: {markdig}");
            DocumentationMarkdownParserTest.AssertRanges(document);
        }
    }

    [Fact]
    public void PinsCorpusIdentityAndAuditsEveryExclusion()
    {
        using var stream = OpenData("commonmark-0.31.2.json");
        Assert.Equal("D431B29D97B6F73E69D547109CF5081578FAC931E72AFE95639EBE766C1B2A20", Convert.ToHexString(SHA256.HashData(stream)));
        using var exclusions = ReadData("exclusions.json");
        var ids = new HashSet<int>();
        foreach (var entry in exclusions.RootElement.EnumerateArray())
        {
            var id = entry.GetProperty("example").GetInt32();
            Assert.InRange(id, 1, 652);
            Assert.True(ids.Add(id));
            Assert.Contains(entry.GetProperty("reason").GetString(), new[] { "setext", "indented-code", "thematic-break", "html", "reference-link", "image", "named-reference", "explicit-continuation" });
        }

        using var corpus = ReadData("commonmark-0.31.2.json");
        Assert.Equal(Enumerable.Range(1, 652), corpus.RootElement.EnumerateArray().Select(x => x.GetProperty("example").GetInt32()));
        Assert.Equal(652, CommonExamples().Count() + ids.Count);
        // Excluded examples still exercise termination and syntax/source-range invariants.
        foreach (var entry in corpus.RootElement.EnumerateArray())
        {
            if (ids.Contains(entry.GetProperty("example").GetInt32()))
            {
                DocumentationMarkdownParserTest.AssertRanges(DocumentationMarkdownDocument.Parse(entry.GetProperty("markdown").GetString()!));
            }
        }
    }

    private static Stream OpenData(string name) => typeof(DocumentationMarkdownConformanceTest).Assembly.GetManifestResourceStream("xUnitTest.TestData.DocumentationMarkdown." + name) ?? throw new InvalidOperationException(name);

    // Markdig omits only this optional block-formatting newline. Never normalize text/code whitespace.
    private static string NormalizeLayout(string html) => html.Replace("<li>\n<p>", "<li><p>", StringComparison.Ordinal);

    private static JsonDocument ReadData(string name)
    {
        using var stream = OpenData(name);
        return JsonDocument.Parse(stream);
    }
}
