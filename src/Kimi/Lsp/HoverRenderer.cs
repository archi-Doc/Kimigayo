// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Documentation;

namespace Kimi.Lsp;

#pragma warning disable SA1201, SA1202, SA1204, SA1402, SA1600, SA1649 // Internal synchronous renderer and result vocabulary.

internal readonly record struct HoverRendering(string? Body, string? Reason = null, bool Cacheable = true);

internal sealed class HoverRenderer(bool markdown)
{
    internal const string PreviousNotice = "Previous analysis: update pending";
    private const int NoticeReserve = 256;
    private static readonly int BodyLimit = HoverLimits.Output - PreviousNotice.Length - 2;
    private readonly StringBuilder body = new();
    private readonly StringBuilder block = new();
    private readonly HoverBudget budget = new();
    private StringBuilder output = null!;
    private HoverDocumentation? documentation;
    private int descriptionStart;
    private bool trimDescription;

    internal static HoverRendering Render(HoverInfo info, bool markdown)
        => new HoverRenderer(markdown).Render(info);

    private HoverRendering Render(HoverInfo info)
    {
        this.output = this.body;
        try
        {
            for (var i = 0; i < info.Declarations.Length; i++)
            {
                var declaration = info.Declarations[i];
                this.Append(markdown ? "## " : string.Empty);
                this.Text(declaration.Kind);
                if (declaration.Owner.Length != 0)
                {
                    this.Append(" · ");
                    this.Text(declaration.Owner);
                }

                this.End(string.Empty);
                // A merged requirement displays its common contract once, retaining each owner's attributed documentation.
                if (i == 0 || info.Declarations[i - 1].Header != declaration.Header)
                {
                    this.CodeBlock(declaration.Header, "kimi", string.Empty);
                }

                if (declaration.Details is { } details)
                {
                    this.CodeBlock(details, string.Empty, string.Empty);
                }
            }

            if (info.Use is { } use)
            {
                this.CodeBlock(use, string.Empty, string.Empty);
            }

            if (info.Copy is { } copy)
            {
                this.Append("Copy: ");
                this.Append(copy switch { ConstraintProof.Proven => "Yes", ConstraintProof.Refuted => "No", ConstraintProof.Unknown => "Unknown", _ => "Error" });
                if (info.CopyType is { } type)
                {
                    this.Append(" (");
                    this.Code(type);
                    this.Append(')');
                }

                this.End(string.Empty);
            }

            if (info.Effects is { } effects)
            {
                this.CodeBlock(effects, string.Empty, string.Empty);
            }
        }
        catch (HoverLimitException ex)
        {
            return new(null, ex.Message);
        }

        string? reason = null;
        var cacheable = true;
        foreach (var declaration in info.Declarations)
        {
            if (declaration.DocumentationNotice is { } notice && !this.Notice(notice))
            {
                return new(null, "Hover mandatory documentation notice exceeds output limit");
            }

            foreach (var documentation in declaration.Documentation)
            {
                this.documentation = documentation;
                this.output = this.block;
                this.block.Clear();
                this.descriptionStart = 0;
                this.trimDescription = false;
                try
                {
                    if (documentation.Span.Length > HoverLimits.Input)
                    {
                        throw new HoverLimitException("Documentation stopped: input limit");
                    }

                    var text = DocumentationComment.Extract(documentation.Source, documentation.Span, documentation.Indent).Text;
                    this.budget.Charge(text.Length);
                    var parsed = DocumentationMarkdownDocument.Parse(text, maximumDepth: HoverLimits.Depth);
                    DocumentationMarkdownNode? parameterItem = null;
                    if (declaration.Parameter is { } parameter)
                    {
                        var matches = 0;
                        foreach (var item in parsed.ClassifyItems(documentation.Parameters).Span)
                        {
                            if (item.Kind == DocumentationMarkdownItemKind.Parameter && item.Name == parameter)
                            {
                                matches++;
                                parameterItem = item.Node;
                                this.descriptionStart = item.DescriptionSpan.Start;
                            }
                        }

                        if (matches != 1)
                        {
                            continue;
                        }

                        this.trimDescription = true;
                    }

                    this.Append(markdown ? "**" : string.Empty);
                    this.Append(declaration.ImplementationNote ? "Implementation note" : "Documentation");
                    this.Append(markdown ? "** — " : " — ");
                    this.Text(documentation.LogicalName ?? documentation.ModId ?? documentation.Source.Path);
                    this.End(string.Empty);
                    if (!this.CommitBlock())
                    {
                        return this.Truncated(reason, cacheable);
                    }

                    if (text.Length == 0)
                    {
                        if (!this.Notice("Documentation: explicitly empty"))
                        {
                            return new(null, "Hover documentation notice exceeds output limit");
                        }
                    }
                    else
                    {
                        foreach (var child in (parameterItem ?? parsed.Root).Children)
                        {
                            this.output = this.block;
                            this.block.Clear();
                            this.Block(child, string.Empty);
                            if (!this.CommitBlock())
                            {
                                return this.Truncated(reason, cacheable);
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is HoverLimitException or DocumentationMarkdownLimitException)
                {
                    if (ex is HoverLimitException && ex.Message == "Hover output limit exceeded")
                    {
                        return this.Truncated(reason, cacheable);
                    }

                    reason ??= ex.Message;
                    if (!this.Notice(ex is DocumentationMarkdownLimitException ? "Documentation stopped: depth limit" : ex.Message))
                    {
                        return new(null, reason);
                    }

                    if (ex is HoverLimitException && ex.Message == "Hover work limit exceeded")
                    {
                        return new(this.body.ToString().TrimEnd(), reason, cacheable);
                    }
                }
                catch (Exception ex) when (ex is not (OperationCanceledException or PendingInputException))
                {
                    reason ??= "Documentation rendering failed: " + ex.Message;
                    cacheable = false;
                    if (!this.Notice("Documentation unavailable: internal failure"))
                    {
                        return new(null, reason, false);
                    }
                }
            }
        }

        return new(this.body.ToString().TrimEnd(), reason, cacheable);
    }

    private bool CommitBlock()
    {
        if (this.body.Length + this.block.Length > BodyLimit - NoticeReserve)
        {
            return false;
        }

        this.body.Append(this.block);
        return true;
    }

    private bool Notice(string notice)
    {
        // Notices remain available even after a documentation work budget is exhausted.
        if (this.body.Length + notice.Length + 2 > BodyLimit)
        {
            return false;
        }

        this.body.Append(notice).Append("\n\n");
        return true;
    }

    private HoverRendering Truncated(string? reason, bool cacheable)
    {
        const string Notice = "Documentation truncated: response output limit";
        return this.Notice(Notice) ? new(this.body.ToString().TrimEnd(), reason ?? Notice, cacheable) : new(null, reason ?? Notice, cacheable);
    }

    private void Block(DocumentationMarkdownNode node, string prefix, bool tight = false)
    {
        using var guard = this.budget.Enter();
        if (node.Span.End <= this.descriptionStart)
        {
            return;
        }

        switch (node.Kind)
        {
            case DocumentationMarkdownKind.Paragraph:
                this.Begin(prefix);
                this.Inlines(node, prefix);
                this.End(prefix, tight ? 1 : 2);
                break;
            case DocumentationMarkdownKind.Heading:
                this.Begin(prefix);
                var level = node.HeadingLevel + 2;
                if (markdown && level <= 6)
                {
                    this.Repeat('#', level);
                    this.Append(' ');
                }
                else
                {
                    this.Append(markdown ? "**Heading level " : "Heading level ");
                    this.Append(level.ToString(CultureInfo.InvariantCulture));
                    this.Append(markdown ? ":** " : ": ");
                }

                this.Inlines(node, prefix);
                this.End(prefix);
                break;
            case DocumentationMarkdownKind.BulletList:
            case DocumentationMarkdownKind.OrderedList:
                var number = node.StartNumber;
                foreach (var item in node.Children)
                {
                    this.Begin(prefix);
                    var marker = node.Kind == DocumentationMarkdownKind.BulletList ? "- " : (number++).ToString(CultureInfo.InvariantCulture) + ". ";
                    this.Append(marker);
                    var childPrefix = prefix + new string(' ', marker.Length);
                    foreach (var child in item.Children)
                    {
                        this.Block(child, childPrefix, node.IsTight);
                    }
                }

                this.End(prefix, tight ? 1 : 2);
                break;
            case DocumentationMarkdownKind.Quote:
                foreach (var child in node.Children)
                {
                    this.Block(child, prefix + "> ");
                }

                this.End(prefix);
                break;
            case DocumentationMarkdownKind.CodeBlock:
                var code = node.FirstChild is { } codeText ? codeText.Text.ToString() : string.Empty;
                var language = node.Text;
                foreach (var c in language)
                {
                    if (!char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '+' or '.'))
                    {
                        language = default;
                        break;
                    }
                }

                this.CodeBlock(code, language.ToString(), prefix);
                break;
            default:
                foreach (var child in node.Children)
                {
                    this.Block(child, prefix, tight);
                }

                break;
        }
    }

    private void Inlines(DocumentationMarkdownNode node, string prefix)
    {
        foreach (var child in node.Children)
        {
            this.Inline(child, prefix);
        }
    }

    private void Inline(DocumentationMarkdownNode node, string prefix)
    {
        using var guard = this.budget.Enter();
        if (node.Span.End <= this.descriptionStart)
        {
            return;
        }

        switch (node.Kind)
        {
            case DocumentationMarkdownKind.Text:
                var text = node.Text;
                ref readonly var data = ref node.Document.GetData(node.Id);
                if (this.descriptionStart > data.TextStart && data.TextStart >= 0)
                {
                    text = text[Math.Min(text.Length, this.descriptionStart - data.TextStart)..];
                }

                if (this.trimDescription)
                {
                    text = text.TrimStart(" \t");
                    this.trimDescription = text.IsEmpty;
                }

                this.Text(text);
                break;
            case DocumentationMarkdownKind.Code:
                this.Code(node.Text);
                break;
            case DocumentationMarkdownKind.Emphasis:
            case DocumentationMarkdownKind.Strong:
                var marker = node.Document.Text[node.Span.Start] == '_' ? '_' : '*';
                var markerCount = node.Kind == DocumentationMarkdownKind.Strong ? 2 : 1;
                if (markdown)
                {
                    this.Repeat(marker, markerCount);
                }

                this.Inlines(node, prefix);
                if (markdown)
                {
                    this.Repeat(marker, markerCount);
                }

                break;
            case DocumentationMarkdownKind.Link:
            case DocumentationMarkdownKind.AutoLink:
                var documentation = this.documentation!;
                var placement = documentation.Placement ?? HoverPlacement.Unsupported;
                var destination = DocumentationLinks.ResolveEditor(node.Destination!, documentation.LogicalName, documentation.Project, placement.Map);
                var automatic = node.Kind == DocumentationMarkdownKind.AutoLink;
                if (automatic && destination is null)
                {
                    this.Text(node.Document.Text.AsSpan(node.Span.Start, node.Span.Length));
                    break;
                }

                if (markdown && destination is not null)
                {
                    this.Append('[');
                }

                if (automatic)
                {
                    this.Text(node.Text);
                }
                else
                {
                    this.Inlines(node, prefix);
                }

                if (destination is not null)
                {
                    this.Append(markdown ? "](<" : " (");
                    this.Append(destination);
                    this.Append(markdown ? ">" : ")");
                    if (node.Title is { } title)
                    {
                        this.Append(markdown ? " \"" : " — ");
                        this.Text(title.Replace('\n', ' '));
                        if (markdown)
                        {
                            this.Append('"');
                        }
                    }

                    if (markdown)
                    {
                        this.Append(')');
                    }
                }

                break;
            case DocumentationMarkdownKind.HardBreak:
                this.Append(markdown ? "  \n" : "\n");
                this.Begin(prefix);
                break;
            case DocumentationMarkdownKind.SoftBreak:
                this.Append('\n');
                this.Begin(prefix);
                break;
        }
    }

    private void Text(ReadOnlySpan<char> text)
    {
        if (!markdown)
        {
            this.Append(text);
            return;
        }

        if (!text.IsEmpty && text[0] is ' ' or '\t')
        {
            this.Append(text[0] == ' ' ? "&#32;" : "&#9;");
            text = text[1..];
        }

        foreach (var c in text)
        {
            if ((char.IsAscii(c) && char.IsPunctuation(c)) || c is '<' or '>' or '=' or '+' or '~' or '`' or '|' or '$' or '^')
            {
                this.Append('\\');
            }

            this.Append(c);
        }
    }

    private void Code(ReadOnlySpan<char> text)
    {
        if (!markdown)
        {
            this.Append(text);
            return;
        }

        var count = Fence(text, 1);
        this.Repeat('`', count);
        var padding = text.IndexOfAnyExcept(' ') >= 0;
        if (padding)
        {
            this.Append(' ');
        }

        this.Append(text);
        if (padding)
        {
            this.Append(' ');
        }

        this.Repeat('`', count);
    }

    private void CodeBlock(string text, string language, string prefix)
    {
        var fence = markdown ? Fence(text, 3) : 0;
        if (markdown)
        {
            this.Begin(prefix);
            this.Repeat('`', fence);
            this.Append(language);
            this.Append('\n');
        }

        if (text.Length != 0)
        {
            var content = text.AsSpan();
            if (content[^1] == '\n')
            {
                content = content[..^1];
                if (!content.IsEmpty && content[^1] == '\r')
                {
                    content = content[..^1];
                }
            }
            else if (content[^1] == '\r')
            {
                content = content[..^1];
            }

            foreach (var line in content.EnumerateLines())
            {
                this.Begin(prefix);
                this.Append(line);
                this.Append('\n');
            }
        }

        if (markdown)
        {
            this.Begin(prefix);
            this.Repeat('`', fence);
        }

        this.End(prefix);
    }

    private static int Fence(ReadOnlySpan<char> text, int minimum)
    {
        var longest = 0;
        var current = 0;
        foreach (var c in text)
        {
            current = c == '`' ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }

        return Math.Max(minimum, longest + 1);
    }

    private void Begin(string prefix)
    {
        if (this.output.Length == 0 || this.output[^1] == '\n')
        {
            this.Append(prefix);
        }
    }

    private void End(string prefix, int lines = 2)
    {
        if (this.output.Length == 0 || this.output[^1] != '\n')
        {
            this.Append('\n');
        }

        if (lines > 1)
        {
            this.Append(prefix);
            this.Append('\n');
        }
    }

    private void Append(ReadOnlySpan<char> value)
    {
        this.Reserve(value.Length);
        this.output.Append(value);
    }

    private void Append(char value)
    {
        this.Reserve(1);
        this.output.Append(value);
    }

    private void Repeat(char value, int count)
    {
        this.Reserve(count);
        this.output.Append(value, count);
    }

    private void Reserve(int count)
    {
        this.budget.Charge(count);
        if (count > BodyLimit - this.output.Length)
        {
            throw new HoverLimitException("Hover output limit exceeded");
        }
    }
}
