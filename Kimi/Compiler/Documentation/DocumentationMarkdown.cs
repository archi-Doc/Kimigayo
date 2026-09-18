// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text;
using Kimi.Compiler.Helper;
using Kimi.Compiler.Parsing;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Kimi.Compiler.Documentation;

#pragma warning disable SA1204 // Keep parsing, presentation and extraction helpers together.

/// <summary>A parsed, source-mapped CommonMark document. Consumers must treat its tree as read-only.</summary>
public sealed class DocumentationMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().DisableHtml().UsePreciseSourceLocation().Build();

    private DocumentationMarkdown(DocumentationComment comment)
    {
        this.Comment = comment;
        this.Document = Markdown.Parse(comment.GetText().Text, Pipeline);
        this.Items = this.Extract().AsReadOnly();
    }

    /// <summary>Gets the source-backed comment.</summary>
    public DocumentationComment Comment { get; }

    /// <summary>Gets the tree used by both rendering and extraction; do not mutate it.</summary>
    public MarkdownDocument Document { get; }

    /// <summary>Gets references to recognized items in source order, retaining duplicates and containment.</summary>
    public IReadOnlyList<DocumentationItem> Items { get; }

    /// <summary>Gets the opening paragraph, or null when the document does not begin with one.</summary>
    public ParagraphBlock? Summary => this.Document.Count > 0 ? this.Document[0] as ParagraphBlock : null;

    /// <summary>Parses documentation only when requested.</summary>
    /// <param name="comment">The comment to parse.</param>
    /// <returns>The parsed document.</returns>
    public static DocumentationMarkdown Parse(DocumentationComment comment)
    {
        ArgumentNullException.ThrowIfNull(comment);
        return new(comment);
    }

    /// <summary>Renders HTML below a declaration heading without mutating the parsed tree.</summary>
    /// <param name="declarationHeadingLevel">The enclosing heading level.</param>
    /// <param name="rewriteLink">An optional deterministic rewriter of resolved URLs; null results disable a target.</param>
    /// <returns>Escaped HTML with only relative, HTTP(S) and mailto links enabled.</returns>
    public string ToHtml(int declarationHeadingLevel = 1, Func<string, string?>? rewriteLink = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(declarationHeadingLevel, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(declarationHeadingLevel, 6);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.ObjectRenderers.RemoveAll(static x => x is HeadingRenderer);
        renderer.ObjectRenderers.Add(new RelativeHeadingRenderer(declarationHeadingLevel));
        renderer.ObjectRenderers.RemoveAll(static x => x is LinkInlineRenderer);
        renderer.ObjectRenderers.Add(new DocumentationLinkRenderer(this.Comment.Owner.LogicalName, rewriteLink));
        renderer.Render(this.Document);
        return writer.ToString();
    }

    /// <summary>Returns missing or ambiguous parameter-item diagnostics, independently of compilation.</summary>
    /// <returns>The optional documentation diagnostics.</returns>
    public IEnumerable<DocumentationDiagnostic> GetDiagnostics()
    {
        if (!this.Comment.IsSelected || this.Comment.Declaration is null)
        {
            yield break;
        }

        var text = this.Comment.GetText();
        foreach (var item in this.Items)
        {
            if (item.IsParameter && item.ParameterMatchCount != 1)
            {
                var start = text.GetSourceOffset(Math.Clamp(item.Node.Span.Start, 0, text.Text.Length));
                yield return new(text.Source, new(start, 0), item.ParameterMatchCount == 0 ? "UnknownDocumentationParameter" : "AmbiguousDocumentationParameter");
            }
        }

        if (this.Comment.Declaration is FunctionKoto { Modifier: var modifier } && modifier.HasFlag(ModifierKind.Unsafe) && !this.Items.Any(x => x.Label == "Safety" && !x.IsParameter))
        {
            yield return new(text.Source, this.Comment.Span, "MissingSafetyDocumentation");
        }
    }

    /// <summary>Resolves a relative path against a normalized logical source name without filesystem access.</summary>
    /// <param name="url">The Markdown destination.</param>
    /// <param name="logicalSourceName">The project-relative source name; null for generated sources.</param>
    /// <returns>The project-relative destination, an allowed absolute URL, or null if unresolved.</returns>
    public static string? ResolveLink(string url, string? logicalSourceName)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (AllowedUrl(url).Length == 0)
        {
            return null;
        }

        if (url.StartsWith('#') || url.StartsWith('/') || Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            return url;
        }

        if (logicalSourceName is null)
        {
            return null;
        }

        var suffixIndex = url.IndexOfAny(['?', '#']);
        var path = suffixIndex < 0 ? url : url[..suffixIndex];
        var suffix = suffixIndex < 0 ? string.Empty : url[suffixIndex..];
        if (path.Length == 0)
        {
            return logicalSourceName + suffix;
        }

        var parent = logicalSourceName.LastIndexOf('/');
        var segments = new List<string>();
        foreach (var part in ((parent < 0 ? string.Empty : logicalSourceName[..(parent + 1)]) + path).Split('/'))
        {
            if (part is "" or ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (segments.Count == 0)
                {
                    return null;
                }

                segments.RemoveAt(segments.Count - 1);
            }
            else
            {
                segments.Add(part);
            }
        }

        return string.Join('/', segments) + suffix;
    }

    private static string AllowedUrl(string? url)
    {
        if (string.IsNullOrEmpty(url) || url.Any(char.IsControl) || url.StartsWith("//", StringComparison.Ordinal) || url.Contains('\\'))
        {
            return string.Empty;
        }

        return !Uri.TryCreate(url, UriKind.Absolute, out var absolute) || absolute.Scheme is "http" or "https" or "mailto" ? url : string.Empty;
    }

    private List<DocumentationItem> Extract()
    {
        var result = new List<DocumentationItem>();
        for (var i = 0; i < this.Document.Count; i++)
        {
            if (this.Document[i] is HeadingBlock heading && PlainHeading(heading) is { } headingLabel && IsLabel(headingLabel))
            {
                var end = this.Comment.GetText().Text.Length;
                for (var j = i + 1; j < this.Document.Count; j++)
                {
                    if (this.Document[j] is HeadingBlock next && next.Level <= heading.Level)
                    {
                        end = next.Span.Start;
                        break;
                    }
                }

                result.Add(new(headingLabel, false, heading, heading.Span.End + 1, end, 0));
            }
            else if (this.Document[i] is ListBlock list)
            {
                foreach (var child in list)
                {
                    if (child is not ListItemBlock { Count: > 0 } item || item[0] is not ParagraphBlock paragraph)
                    {
                        continue;
                    }

                    if (!this.TryItem(paragraph.Inline?.FirstChild, out var label, out var parameter, out var descriptionStart))
                    {
                        continue;
                    }

                    result.Add(new(label, parameter, item, descriptionStart, item.Span.End + 1, parameter ? this.CountParameters(label) : 0));
                }
            }
        }

        return result;
    }

    private bool TryItem(Inline? first, out string label, out bool parameter, out int descriptionStart)
    {
        parameter = first is CodeInline;
        label = parameter ? ((CodeInline)first!).Content : string.Empty;
        descriptionStart = 0;
        if (parameter && (label.Length == 0 || !UnicodeIdentifierHelper.IsValid(label)))
        {
            return false;
        }

        var prefix = new StringBuilder();
        for (var node = parameter ? first!.NextSibling : first; node is not null; node = node.NextSibling)
        {
            if (PlainInline(node) is not { } text)
            {
                return false;
            }

            var colon = text.IndexOf(':');
            if (colon < 0)
            {
                prefix.Append(text);
                continue;
            }

            prefix.Append(text.AsSpan(0, colon));
            if (parameter ? prefix.Length != 0 : !IsLabel(label = prefix.ToString()))
            {
                return false;
            }

            var following = colon + 1 < text.Length ? text[colon + 1] :
                node.NextSibling is null or LineBreakInline ? '\n' :
                PlainInline(node.NextSibling) is { Length: > 0 } next ? next[0] : '\0';
            if (following is not (' ' or '\t' or '\n'))
            {
                return false;
            }

            // Escapes and entities can split an otherwise plain prefix into
            // several nodes. Map the delimiter through its actual source span.
            var raw = this.Comment.GetText().Text.AsSpan(node.Span.Start, node.Span.Length);
            descriptionStart = node is HtmlEntityInline ? node.Span.End + 1 : node.Span.Start + raw.IndexOf(':') + 1;
            return true;
        }

        return false;
    }

    private static string? PlainInline(Inline node) => node switch
    {
        LiteralInline literal => literal.Content.ToString(),
        HtmlEntityInline entity => entity.Transcoded.ToString(),
        _ => null,
    };

    private static string? PlainHeading(HeadingBlock heading)
    {
        var text = new StringBuilder();
        for (var node = heading.Inline?.FirstChild; node is not null; node = node.NextSibling)
        {
            if (PlainInline(node) is not { } part)
            {
                return null;
            }

            text.Append(part);
        }

        return text.ToString();
    }

    private static bool IsLabel(string value) => value is "Returns" or "Abort" or "Safety" or "Note" or "Warning" or "Example";

    private int CountParameters(string name)
    {
        var declaration = this.Comment.Declaration;
        var generics = declaration is FunctionKoto function ? function.GenericArguments : declaration is DeclarationContainerKoto container ? container.GenericParameterNodes : [];
        var count = 0;
        foreach (var generic in generics)
        {
            count += generic.Identifier == name ? 1 : 0;
            count += generic.SemanticsParameter == name ? 1 : 0;
        }

        var origins = declaration is FunctionKoto f ? f.Origins : declaration is DeclarationContainerKoto c ? c.OriginNames : [];
        count += origins.Count(x => x == name);
        if (declaration is FunctionKoto callable)
        {
            count += callable.Parameters.Count(x => x.InternalName != "self" && x.ExternalName == name);
        }

        return count;
    }

    private sealed class DocumentationLinkRenderer(string? logicalName, Func<string, string?>? rewrite) : LinkInlineRenderer
    {
        protected override void Write(HtmlRenderer renderer, LinkInline obj)
        {
            var url = obj.GetDynamicUrl?.Invoke() ?? obj.Url ?? string.Empty;
            var resolved = ResolveLink(url, logicalName);
            var allowed = resolved is null ? string.Empty : AllowedUrl(rewrite is null ? resolved : rewrite(resolved));
            if (allowed.Length == 0)
            {
                renderer.WriteChildren(obj);
                return;
            }

            var previous = renderer.LinkRewriter;
            renderer.LinkRewriter = _ => allowed;
            try
            {
                base.Write(renderer, obj);
            }
            finally
            {
                renderer.LinkRewriter = previous;
            }
        }
    }

    private sealed class RelativeHeadingRenderer(int parentLevel) : HtmlObjectRenderer<HeadingBlock>
    {
        protected override void Write(HtmlRenderer renderer, HeadingBlock obj)
        {
            var level = parentLevel + obj.Level;
            var tag = level <= 6 ? $"h{level}" : "div";
            renderer.Write("<").Write(tag);
            if (level > 6)
            {
                renderer.Write(" role=\"heading\" aria-level=\"").Write(level.ToString(CultureInfo.InvariantCulture)).Write("\"");
            }

            renderer.Write(">");
            renderer.WriteLeafInline(obj);
            renderer.Write("</").Write(tag).WriteLine(">");
        }
    }
}

/// <summary>An extracted item referencing its original Markdown node and normalized description range.</summary>
/// <param name="Label">The standard label or parameter name.</param>
/// <param name="IsParameter">Whether this is a parameter candidate.</param>
/// <param name="Node">The original syntax node; do not mutate it.</param>
/// <param name="DescriptionStart">The normalized description start offset.</param>
/// <param name="DescriptionEnd">The exclusive normalized end offset.</param>
/// <param name="ParameterMatchCount">The number of declaration-side matches; only one permits automatic association.</param>
public sealed record DocumentationItem(string Label, bool IsParameter, MarkdownObject Node, int DescriptionStart, int DescriptionEnd, int ParameterMatchCount);
