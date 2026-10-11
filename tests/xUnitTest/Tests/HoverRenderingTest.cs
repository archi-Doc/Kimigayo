// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Documentation;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public sealed class HoverRenderingTest
{
    [Fact]
    public void MarkdownKeepsContractsHierarchyCodeAndCompleteDocumentation()
    {
        var info = Info("Intro **strong** and *emphasis*.\n\n# Section\n\nTail with ``a`b``.\n\n###### Deep\n\n```kimi\n<tag>\n```", copy: ConstraintProof.Unknown);
        var result = HoverRenderer.Render(info, true);
        Assert.Null(result.Reason);
        var body = Assert.IsType<string>(result.Body);
        Assert.Contains("```kimi\nstruct Sample\n```", body);
        var html = Markdig.Markdown.ToHtml(body);
        Assert.Contains("<code>Project</code>", html);
        Assert.DoesNotContain("<h2>Type", html);
        Assert.Contains("<h1>Section</h1>", html);
        Assert.Contains("<h6>Deep</h6>", html);
        Assert.Contains("<strong>strong</strong>", html);
        Assert.Contains("<code>a`b</code>", html);
        Assert.Contains("<code class=\"language-kimi\">&lt;tag&gt;\n</code>", html);
        Assert.Contains("Copy: Unknown", html);
        Assert.Contains("Tail with", html);
    }

    [Fact]
    public void UnadmittedSyntaxCannotBecomeImagesHtmlOrReferenceLinks()
    {
        var body = HoverRenderer.Render(Info("<script>alert(1)</script>\n\n![image](https://example.test/image)\n\n[label][id]\n\n[id]: https://example.test/reference\n\n<command:run>"), true).Body!;
        var html = Markdig.Markdown.ToHtml(body);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("href=\"https://example.test/reference", html);
        Assert.DoesNotContain("href=\"command:", html);
        Assert.Contains("&lt;command:run&gt;", html);
    }

    [Fact]
    public void PlaintextIncludesLabelsUrlsAndHierarchy()
    {
        var text = HoverRenderer.Render(Info("# Links\n\n[guide](https://example.test/guide \"A guide\") and <author@example.test>.\n\n- first\n- second"), false).Body!;
        Assert.Contains("Heading level 1: Links", text);
        Assert.Contains("guide (https://example.test/guide) — A guide", text);
        Assert.Contains("author@example.test (mailto:author@example.test)", text);
        Assert.Contains("- first", text);
        Assert.Contains("- second", text);
    }

    [Fact]
    public void ParameterDocumentationUsesOnlyOneClassifiedItemAndPreservesItsTree()
    {
        var document = Document("Intro.\n\n- T: **The type**.\n\n  ```kimi\n  i32\n  ```\n- value: Other information.", [new("T"), new("value")]);
        var declaration = new HoverDeclaration("Type parameter", "f", "T", [], [document], Parameter: "T");
        var body = HoverRenderer.Render(new([declaration]), true).Body!;
        var html = Markdig.Markdown.ToHtml(body);
        Assert.Contains("<strong>The type</strong>", html);
        Assert.Contains("i32", html);
        Assert.DoesNotContain("Other information", body);
        Assert.DoesNotContain("Intro.", body);
        var duplicate = Document("- T: First.\n- T: Second.", [new("T")]);
        Assert.DoesNotContain("Documentation", HoverRenderer.Render(new([declaration with { Documentation = [duplicate] }]), false).Body);
    }

    [Fact]
    public void EmptyAndDeferredCommentsAreDifferentFromAbsence()
    {
        Assert.Contains("Documentation: explicitly empty", HoverRenderer.Render(Info(string.Empty), false).Body);
        var absent = new HoverInfo([new("Type", string.Empty, "struct Sample", [], [])]);
        Assert.DoesNotContain("Documentation", HoverRenderer.Render(absent, false).Body);
        var deferred = absent with { Declarations = [absent.Declarations[0] with { DocumentationNotice = "Documentation deferred: syntax errors" }] };
        Assert.Contains("Documentation deferred: syntax errors", HoverRenderer.Render(deferred, false).Body);
    }

    [Fact]
    public void InputAndDepthLimitsKeepDeclarationsButMandatoryOverflowReturnsNull()
    {
        var longComment = HoverRenderer.Render(Info(new string('a', HoverLimits.Input + 1)), true);
        Assert.Contains("struct Sample", longComment.Body);
        Assert.Contains("Documentation stopped: input limit", longComment.Body);
        Assert.True(longComment.Cacheable);
        var deep = HoverRenderer.Render(Info(new string('>', 80) + " text"), true);
        Assert.Contains("struct Sample", deep.Body);
        Assert.Contains("Documentation stopped: depth limit", deep.Body);
        var large = Info("Unused.") with { Effects = new string('x', HoverLimits.Output) };
        var rejected = HoverRenderer.Render(large, true);
        Assert.Null(rejected.Body);
        Assert.NotNull(rejected.Reason);
    }

    [Fact]
    public void OutputTruncationNeverCutsACompletedBlockOrLink()
    {
        var comment = "Kept paragraph.\n\n" + new string('*', 40_000) + "\n\n[later](https://example.test/later)";
        var result = HoverRenderer.Render(Info(comment), true);
        Assert.NotNull(result.Reason);
        Assert.DoesNotContain("https://example.test/later", result.Body);
        Assert.True(result.Body!.Length <= HoverLimits.Output);
        var html = Markdig.Markdown.ToHtml(result.Body);
        Assert.Contains("Kept paragraph.", html);
        Assert.Contains("struct Sample", html);
    }

    [Fact]
    public void MixedEmphasisAndNestedListsPreserveTheirStructure()
    {
        var result = HoverRenderer.Render(Info("A **bold _inner_** and *outer __strong__*.\n\n> quoted\n>\n> - first\n>   - nested\n> - last"), true);
        var html = Markdig.Markdown.ToHtml(result.Body!);
        Assert.Contains("<strong>bold <em>inner</em></strong>", html);
        Assert.Contains("<em>outer <strong>strong</strong></em>", html);
        Assert.Contains("<blockquote>", html);
        Assert.Contains("<li>nested</li>", html);
        Assert.Contains("<li>last</li>", html);
    }

    private static HoverInfo Info(string comment, ConstraintProof? copy = null)
        => new([new("Type", "Project", "struct Sample", [], [Document(comment)])], CopyType: copy is null ? null : "Sample", Copy: copy);

    private static HoverDocumentation Document(string text, DocumentationMarkdownParameter[]? parameters = null)
    {
        var raw = "/// " + text.Replace("\n", "\n/// ", StringComparison.Ordinal);
        var source = new SourceDocument("source.kimi", raw);
        return new(source, new SourceSpan(0, raw.Length), 0, "P", "source.kimi", null, 0, default, parameters ?? []);
    }
}
