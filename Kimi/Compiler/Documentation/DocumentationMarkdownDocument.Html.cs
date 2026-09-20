// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Globalization;
using System.Text;

namespace Kimi.Compiler.Documentation;

#pragma warning disable SA1201, SA1202, SA1204, SA1600 // Iterative renderer helpers.

public sealed partial class DocumentationMarkdownDocument
{
    private static readonly string[] HeadingTags = [string.Empty, "h1", "h2", "h3", "h4", "h5", "h6"];

    private static readonly SearchValues<char> HtmlEscapes = SearchValues.Create("&<>\"");

    [ThreadStatic]
    private static StringBuilder? htmlBuilder;

    /// <summary>Renders the immutable syntax with explicit placement and URL policy, using LF output.</summary>
    /// <param name="options">Output inputs. Zero heading offset renders standalone Markdown.</param>
    /// <param name="cancellationToken">Cancellation; no partial HTML is published.</param>
    /// <returns>Completed escaped HTML.</returns>
    public string ToHtml(DocumentationHtmlOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        ArgumentOutOfRangeException.ThrowIfLessThan(options.DeclarationHeadingLevel, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.DeclarationHeadingLevel, 6);
        cancellationToken.ThrowIfCancellationRequested();
        // One bounded builder per rendering thread. Remove it while in use so
        // reentrant link callbacks cannot share mutable output with this call.
        var output = htmlBuilder ?? new StringBuilder(Math.Min(this.Text.Length, 4096));
        htmlBuilder = null;
        output.Clear();
        var stack = new MarkdownBuffer<HtmlFrame>(stackalloc HtmlFrame[32]);
        try
        {
            var node = this.Root;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var link = WriteOpen(node, output, options, cancellationToken);
                stack.Add(new(node.Id, link));
                if (node.FirstChild is { } child)
                {
                    node = child;
                    continue;
                }

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var frame = stack[--stack.Count];
                    node = new(this, frame.Id);
                    WriteClose(node, output, options.DeclarationHeadingLevel, frame.Link);
                    if (node.NextSibling is { } next)
                    {
                        node = next;
                        break;
                    }

                    if (stack.Count == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return output.ToString();
                    }
                }
            }
        }
        finally
        {
            stack.Dispose();
            if (output.Capacity <= 65536 && htmlBuilder is null)
            {
                output.Clear();
                htmlBuilder = output;
            }
        }
    }

    private static bool IsTightParagraph(DocumentationMarkdownNode node)
        => node.Kind == DocumentationMarkdownKind.Paragraph && node.Parent is { Kind: DocumentationMarkdownKind.ListItem } item && item.Parent!.Value.IsTight;

    private static string? Tag(DocumentationMarkdownNode node, int offset) => node.Kind switch
    {
        DocumentationMarkdownKind.Paragraph => IsTightParagraph(node) ? null : "p",
        DocumentationMarkdownKind.Heading => node.HeadingLevel + offset <= 6 ? HeadingTags[node.HeadingLevel + offset] : "div",
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

    private static bool WriteOpen(DocumentationMarkdownNode node, StringBuilder output, DocumentationHtmlOptions options, CancellationToken token)
    {
        var tag = Tag(node, options.DeclarationHeadingLevel);
        var linked = false;
        if (tag == "a")
        {
            var url = DocumentationLinks.Resolve(node.Destination!, options);
            token.ThrowIfCancellationRequested();
            if (url is null)
            {
                tag = null;
                if (node.Kind == DocumentationMarkdownKind.AutoLink)
                {
                    var spelling = node.Document.Text.AsSpan(node.Span.Start, node.Span.Length);
                    int nul;
                    while ((nul = spelling.IndexOf('\0')) >= 0)
                    {
                        token.ThrowIfCancellationRequested();
                        Escape(output, spelling[..nul], token);
                        output.Append('\uFFFD');
                        spelling = spelling[(nul + 1)..];
                    }

                    Escape(output, spelling, token);
                    return false;
                }
            }
            else
            {
                linked = true;
                output.Append("<a href=\"");
                Escape(output, url, token);
                output.Append('"');
                if (node.Title is { } title)
                {
                    output.Append(" title=\"");
                    Escape(output, title, token);
                    output.Append('"');
                }

                output.Append('>');
            }
        }
        else if (tag is not null)
        {
            output.Append('<').Append(tag);
            if (node.Kind == DocumentationMarkdownKind.Heading && node.HeadingLevel + options.DeclarationHeadingLevel > 6)
            {
                output.Append(" role=\"heading\" aria-level=\"").Append((node.HeadingLevel + options.DeclarationHeadingLevel).ToString(CultureInfo.InvariantCulture)).Append('"');
            }
            else if (node.Kind == DocumentationMarkdownKind.OrderedList && node.StartNumber != 1)
            {
                output.Append(" start=\"").Append(node.StartNumber.ToString(CultureInfo.InvariantCulture)).Append('"');
            }

            output.Append('>');
            if (node.Kind is DocumentationMarkdownKind.Quote or DocumentationMarkdownKind.BulletList or DocumentationMarkdownKind.OrderedList ||
                (node.Kind == DocumentationMarkdownKind.ListItem && node.FirstChild is { } first && (!node.Parent!.Value.IsTight || first.Kind != DocumentationMarkdownKind.Paragraph)))
            {
                output.Append('\n');
            }
        }

        if (node.Kind == DocumentationMarkdownKind.CodeBlock)
        {
            output.Append("<pre><code");
            if (!node.Text.IsEmpty)
            {
                output.Append(" class=\"language-");
                Escape(output, node.Text, token);
                output.Append('"');
            }

            output.Append('>');
        }
        else if (node.Kind is DocumentationMarkdownKind.Text or DocumentationMarkdownKind.Code or DocumentationMarkdownKind.AutoLink)
        {
            Escape(output, node.Text, token);
        }
        else if (node.Kind == DocumentationMarkdownKind.SoftBreak)
        {
            output.Append('\n');
        }
        else if (node.Kind == DocumentationMarkdownKind.HardBreak)
        {
            output.Append("<br />\n");
        }

        return linked;
    }

    private static void WriteClose(DocumentationMarkdownNode node, StringBuilder output, int offset, bool linked)
    {
        var tag = Tag(node, offset);
        if (tag == "a" && !linked)
        {
            return;
        }

        if (tag is not null)
        {
            output.Append("</").Append(tag).Append('>');
            if (node.Kind is DocumentationMarkdownKind.Paragraph or DocumentationMarkdownKind.Heading or DocumentationMarkdownKind.Quote or DocumentationMarkdownKind.BulletList or DocumentationMarkdownKind.OrderedList or DocumentationMarkdownKind.ListItem)
            {
                output.Append('\n');
            }
        }
        else if (IsTightParagraph(node) && node.NextSibling is not null)
        {
            output.Append('\n');
        }
        else if (node.Kind == DocumentationMarkdownKind.CodeBlock)
        {
            output.Append("</code></pre>\n");
        }
    }

    private static void Escape(StringBuilder output, ReadOnlySpan<char> text, CancellationToken token)
    {
        while (!text.IsEmpty)
        {
            token.ThrowIfCancellationRequested();
            var chunk = text[..Math.Min(text.Length, 4096)];
            var escaped = chunk.IndexOfAny(HtmlEscapes);
            if (escaped < 0)
            {
                output.Append(chunk);
                text = text[chunk.Length..];
            }
            else
            {
                output.Append(chunk[..escaped]);
                output.Append(chunk[escaped] switch { '&' => "&amp;", '<' => "&lt;", '>' => "&gt;", _ => "&quot;" });
                text = text[(escaped + 1)..];
            }
        }
    }

    private readonly record struct HtmlFrame(int Id, bool Link);
}
