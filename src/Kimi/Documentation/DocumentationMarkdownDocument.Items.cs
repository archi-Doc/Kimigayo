// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Diagnostics;

namespace Kimi.Documentation;

#pragma warning disable SA1201, SA1202, SA1204, SA1600, SA1649 // Candidate model and its snapshot-local extraction.

/// <summary>Declaration-independent extraction or declaration-dependent classification.</summary>
public enum DocumentationMarkdownItemKind : byte
{
    Unclassified,
    Standard,
    Parameter,
    Ambiguous,
    Unknown,
}

/// <summary>A name supplied by the declaration layer; receiver exclusion is a role, not a spelling.</summary>
/// <param name = "Name">The exact external or declared parameter name.</param>
/// <param name = "IsReceiver">Whether the declaration defines this parameter as its receiver.</param>
public readonly record struct DocumentationMarkdownParameter(string Name, bool IsReceiver = false);

/// <summary>An item referencing its original node and half-open normalized description range.</summary>
/// <param name = "Name">The decoded name, without trimming, case folding or normalization.</param>
/// <param name = "Node">The original list item or heading.</param>
/// <param name = "DescriptionSpan">The description's normalized range.</param>
/// <param name = "Kind">The classification; list candidates remain unclassified until declaration information is supplied.</param>
/// <param name = "ParameterMatchCount">Number of matches when classified.</param>
public readonly record struct DocumentationMarkdownItem(
    string Name, DocumentationMarkdownNode Node, SourceSpan DescriptionSpan, DocumentationMarkdownItemKind Kind, int ParameterMatchCount = 0)
{
    public SourceSpan SourceDescriptionSpan => this.Node.Document.GetSourceSpan(this.DescriptionSpan);
}

public sealed partial class DocumentationMarkdownDocument
{
    private DocumentationMarkdownItem[]? items;

    /// <summary>Extracts and publishes source-ordered candidates once, without assuming declaration information.</summary>
    /// <param name = "cancellationToken">Cancellation of an unpublished extraction attempt.</param>
    /// <returns>Read-only candidates owned by this snapshot.</returns>
    public ReadOnlySpan<DocumentationMarkdownItem> GetItemCandidates(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var existing = Volatile.Read(ref this.items);
        if (existing is not null)
        {
            return existing;
        }

        // Reuse private snapshot storage as the monitor; allocate no lock per document.
        // Only the first extraction synchronizes. Completed reads stay lock-free.
        lock (this.nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (this.items is not null)
            {
                return this.items;
            }

            var completed = this.ExtractItemCandidates(cancellationToken);
            Volatile.Write(ref this.items, completed);
            return completed;
        }
    }

    private DocumentationMarkdownItem[] ExtractItemCandidates(CancellationToken cancellationToken)
    {
        MarkdownBuffer<DocumentationMarkdownItem> result = default;
        try
        {
            // One forward root traversal. A heading's description ends at the next
            // root heading of the same or a higher level, so keep the unresolved
            // result index per level and patch it when that heading appears.
            Span<int> pending = stackalloc int[7];
            pending.Fill(-1);
            for (var block = this.nodes[0].First; block != 0; block = this.nodes[block].Next)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ref readonly var data = ref this.nodes[block];
                if (data.Kind == DocumentationMarkdownKind.Heading)
                {
                    EndDescriptions(ref result, pending[data.Argument..], data.Start);
                    if (this.HeadingName(data.First) is { } name && IsStandardItem(name))
                    {
                        var description = SourceSpan.FromBounds(data.End, this.Text.Length);
                        pending[data.Argument] = result.Add(new(name, new(this, block), description, DocumentationMarkdownItemKind.Standard));
                    }
                }
                else if (data.Kind is DocumentationMarkdownKind.BulletList or DocumentationMarkdownKind.OrderedList)
                {
                    this.AddListItems(ref result, block, cancellationToken);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return result.Span.ToArray();
        }
        finally
        {
            result.Dispose();
        }
    }

    // A root heading ends the pending descriptions of its level and every deeper level at its start.
    private static void EndDescriptions(ref MarkdownBuffer<DocumentationMarkdownItem> result, scoped Span<int> pending, int end)
    {
        foreach (ref var index in pending)
        {
            if (index >= 0)
            {
                ref var item = ref result[index];
                item = item with { DescriptionSpan = SourceSpan.FromBounds(item.DescriptionSpan.Start, end) };
                index = -1;
            }
        }
    }

    // The list-item walk: an item whose first child is a paragraph starting with a name and a colon is an unclassified candidate.
    private void AddListItems(ref MarkdownBuffer<DocumentationMarkdownItem> result, int list, CancellationToken cancellationToken)
    {
        for (var item = this.nodes[list].First; item != 0; item = this.nodes[item].Next)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paragraph = this.nodes[item].First;
            if (paragraph != 0 && this.nodes[paragraph].Kind == DocumentationMarkdownKind.Paragraph &&
                this.ItemName(this.nodes[paragraph].First, out var start) is { } name)
            {
                result.Add(new(name, new(this, item), SourceSpan.FromBounds(start, this.nodes[item].End), DocumentationMarkdownItemKind.Unclassified));
            }
        }
    }

    /// <summary>Classifies candidates using exact declaration names, retaining duplicates and unknown names.</summary>
    /// <param name = "parameters">All parameter namespaces and explicit receiver roles from the declaration.</param>
    /// <param name = "cancellationToken">Cancellation of this independent classification.</param>
    /// <returns>A read-only result; declaration-dependent classifications are not cached on the syntax snapshot.</returns>
    public ReadOnlyMemory<DocumentationMarkdownItem> ClassifyItems(
        ReadOnlySpan<DocumentationMarkdownParameter> parameters, CancellationToken cancellationToken = default)
    {
        var candidates = this.GetItemCandidates(cancellationToken);
        if (candidates.IsEmpty)
        {
            return ReadOnlyMemory<DocumentationMarkdownItem>.Empty;
        }

        // Declarations rarely have many parameters: count matches by scanning
        // them instead of building a table. Large sets use a table once.
        Dictionary<string, int>? counts = parameters.Length > 16 ? new(parameters.Length, StringComparer.Ordinal) : null;
        foreach (var parameter in parameters)
        {
            ArgumentNullException.ThrowIfNull(parameter.Name);
            if (counts is not null && !parameter.IsReceiver)
            {
                counts[parameter.Name] = counts.GetValueOrDefault(parameter.Name) + 1;
            }
        }

        var result = candidates.ToArray();
        for (var i = 0; i < result.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ref var item = ref result[i];
            if (item.Kind != DocumentationMarkdownItemKind.Unclassified)
            {
                continue;
            }

            var count = 0;
            if (counts is not null)
            {
                count = counts.GetValueOrDefault(item.Name);
            }
            else
            {
                foreach (var parameter in parameters)
                {
                    if (!parameter.IsReceiver && string.Equals(parameter.Name, item.Name, StringComparison.Ordinal))
                    {
                        count++;
                    }
                }
            }

            var kind = count switch
            {
                1 => DocumentationMarkdownItemKind.Parameter,
                > 1 => DocumentationMarkdownItemKind.Ambiguous,
                _ => IsStandardItem(item.Name) ? DocumentationMarkdownItemKind.Standard : DocumentationMarkdownItemKind.Unknown,
            };
            item = item with
            {
                Kind = kind,
                ParameterMatchCount = count,
            };
        }

        return result;
    }

    private static bool IsStandardItem(string name) => StandardName(name) is not null;

    // Standard names are shared literals; only other names allocate.
    private static string CreateName(ReadOnlySpan<char> name) => StandardName(name) ?? name.ToString();

    // The standard item names (SPEC 2.3), as shared literals; null for any other name.
    private static string? StandardName(ReadOnlySpan<char> name) => name switch
    {
        "return" => "return",
        "abort" => "abort",
        "safety" => "safety",
        "note" => "note",
        "warning" => "warning",
        "example" => "example",
        _ => null,
    };

    // A heading names an item by its whole text, or by one code span alone; null when it names none.
    private string? HeadingName(int first)
    {
        if (first == 0)
        {
            return null;
        }

        ref readonly var leading = ref this.nodes[first];
        if (leading.Kind == DocumentationMarkdownKind.Code)
        {
            var name = CreateName(this.GetText(leading));
            return leading.Next == 0 && name.Length > 0 ? name : null;
        }

        StringBuilder? builder = null;
        for (var node = first; node != 0; node = this.nodes[node].Next)
        {
            ref readonly var data = ref this.nodes[node];
            if (data.Kind != DocumentationMarkdownKind.Text)
            {
                return null;
            }

            if (data.Next == 0 && builder is null)
            {
                var name = CreateName(this.GetText(data));
                return name.Length > 0 ? name : null;
            }

            (builder ??= new()).Append(this.GetText(data));
        }

        var joined = builder!.ToString();
        return joined.Length > 0 ? joined : null;
    }

    // A list item's paragraph names an item by its text before a colon, or by a code span directly before the colon; whitespace
    // or a line break follows the colon. Null when it names none; otherwise the description starts after the colon.
    private string? ItemName(int first, out int descriptionStart)
    {
        descriptionStart = 0;
        if (first == 0)
        {
            return null;
        }

        var code = this.nodes[first].Kind == DocumentationMarkdownKind.Code;
        var name = code ? CreateName(this.GetText(this.nodes[first])) : string.Empty;
        StringBuilder? builder = null;
        for (var node = code ? this.nodes[first].Next : first; node != 0; node = this.nodes[node].Next)
        {
            ref readonly var data = ref this.nodes[node];
            if (data.Kind != DocumentationMarkdownKind.Text)
            {
                return null;
            }

            var value = this.GetText(data);
            var colon = value.IndexOf(':');
            if (colon >= 0)
            {
                if (!code)
                {
                    name = builder is null ? CreateName(value[..colon]) : builder.Append(value[..colon]).ToString();
                }

                if ((code && colon != 0) || name.Length == 0 || this.AfterColon(data, value, colon) is not (' ' or '\t' or '\n'))
                {
                    return null;
                }

                descriptionStart = data.TextStart >= 0 ? data.TextStart + colon + 1 : data.End;
                return name;
            }

            if (code || data.Next == 0)
            {
                return null;
            }

            (builder ??= new()).Append(value);
        }

        return null;
    }

    // The character after an item name's colon: the rest of its text, a line break at a break or the paragraph's end, or the
    // next text's first character ('\0' if none).
    private char AfterColon(in MarkdownNodeData data, ReadOnlySpan<char> value, int colon)
    {
        if (colon + 1 < value.Length)
        {
            return value[colon + 1];
        }

        var next = data.Next;
        if (next == 0 || this.nodes[next].Kind is DocumentationMarkdownKind.SoftBreak or DocumentationMarkdownKind.HardBreak)
        {
            return '\n';
        }

        return this.nodes[next].Kind == DocumentationMarkdownKind.Text && this.GetText(this.nodes[next]).Length > 0 ? this.GetText(this.nodes[next])[0] : '\0';
    }
}
