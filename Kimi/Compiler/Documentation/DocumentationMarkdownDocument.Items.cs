// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Documentation;

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
public readonly record struct DocumentationMarkdownItem(string Name, DocumentationMarkdownNode Node, SourceSpan DescriptionSpan, DocumentationMarkdownItemKind Kind, int ParameterMatchCount = 0)
{
    public SourceSpan SourceDescriptionSpan => this.Node.Document.GetSourceSpan(this.DescriptionSpan);
}

public sealed partial class DocumentationMarkdownDocument
{
    private DocumentationMarkdownItem[]? items;

    /// <summary>Extracts and atomically caches source-ordered candidates, without assuming declaration information.</summary>
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

        MarkdownBuffer<DocumentationMarkdownItem> result = default;
        try
        {
            // Reverse root traversal resolves every heading's end in constant work.
            // Headings have six possible levels; overlapping descriptions are only ranges.
            Span<int> headingEnds = stackalloc int[7];
            headingEnds.Fill(this.Text.Length);
            for (var block = this.nodes[0].Last; block != 0; block = this.nodes[block].Previous)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ref readonly var data = ref this.nodes[block];
                if (data.Kind == DocumentationMarkdownKind.Heading)
                {
                    var end = headingEnds[data.Argument];
                    for (var level = data.Argument; level <= 6; level++)
                    {
                        headingEnds[level] = data.Start;
                    }

                    if (this.TryName(data.First, heading: true, out var name, out _) && IsStandardItem(name))
                    {
                        result.Add(new(name, new(this, block), SourceSpan.FromBounds(data.End, end), DocumentationMarkdownItemKind.Standard));
                    }
                }
                else if (data.Kind is DocumentationMarkdownKind.BulletList or DocumentationMarkdownKind.OrderedList)
                {
                    for (var item = data.Last; item != 0; item = this.nodes[item].Previous)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var paragraph = this.nodes[item].First;
                        if (paragraph != 0 && this.nodes[paragraph].Kind == DocumentationMarkdownKind.Paragraph && this.TryName(this.nodes[paragraph].First, heading: false, out var name, out var start))
                        {
                            result.Add(new(name, new(this, item), SourceSpan.FromBounds(start, this.nodes[item].End), DocumentationMarkdownItemKind.Unclassified));
                        }
                    }
                }
            }

            var completed = result.Span.ToArray();
            Array.Reverse(completed);
            cancellationToken.ThrowIfCancellationRequested();
            return Interlocked.CompareExchange(ref this.items, completed, null) ?? completed;
        }
        finally
        {
            result.Dispose();
        }
    }

    /// <summary>Classifies candidates using exact declaration names, retaining duplicates and unknown names.</summary>
    /// <param name = "parameters">All parameter namespaces and explicit receiver roles from the declaration.</param>
    /// <param name = "cancellationToken">Cancellation of this independent classification.</param>
    /// <returns>A read-only result; declaration-dependent classifications are not cached on the syntax snapshot.</returns>
    public ReadOnlyMemory<DocumentationMarkdownItem> ClassifyItems(ReadOnlySpan<DocumentationMarkdownParameter> parameters, CancellationToken cancellationToken = default)
    {
        var candidates = this.GetItemCandidates(cancellationToken);
        if (candidates.IsEmpty)
        {
            return ReadOnlyMemory<DocumentationMarkdownItem>.Empty;
        }

        var counts = new Dictionary<string, int>(parameters.Length, StringComparer.Ordinal);
        foreach (var parameter in parameters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(parameter.Name);
            if (!parameter.IsReceiver)
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

            var count = counts.GetValueOrDefault(item.Name);
            var kind = count == 1 ? DocumentationMarkdownItemKind.Parameter : count > 1 ? DocumentationMarkdownItemKind.Ambiguous : IsStandardItem(item.Name) ? DocumentationMarkdownItemKind.Standard : DocumentationMarkdownItemKind.Unknown;
            item = item with
            {
                Kind = kind,
                ParameterMatchCount = count,
            };
        }

        return result;
    }

    private static bool IsStandardItem(string name) => name is "return" or "abort" or "safety" or "note" or "warning" or "example";

    private bool TryName(int first, bool heading, out string name, out int descriptionStart)
    {
        name = string.Empty;
        descriptionStart = 0;
        if (first == 0)
        {
            return false;
        }

        var code = this.nodes[first].Kind == DocumentationMarkdownKind.Code;
        var node = first;
        StringBuilder? builder = null;
        if (code)
        {
            name = this.GetText(this.nodes[first]).ToString();
            node = this.nodes[first].Next;
            if (heading)
            {
                return node == 0 && name.Length > 0;
            }
        }

        while (node != 0)
        {
            ref readonly var data = ref this.nodes[node];
            if (data.Kind != DocumentationMarkdownKind.Text)
            {
                return false;
            }

            var value = this.GetText(data);
            var colon = heading ? -1 : value.IndexOf(':');
            if (colon >= 0)
            {
                if (code && colon != 0)
                {
                    return false;
                }

                if (!code)
                {
                    name = builder is null ? value[..colon].ToString() : builder.Append(value[..colon]).ToString();
                }

                if (name.Length == 0)
                {
                    return false;
                }

                var next = data.Next;
                var following = colon + 1 < value.Length ? value[colon + 1] : next == 0 || this.nodes[next].Kind is DocumentationMarkdownKind.SoftBreak or DocumentationMarkdownKind.HardBreak ? '\n' : this.nodes[next].Kind == DocumentationMarkdownKind.Text && this.GetText(this.nodes[next]).Length > 0 ? this.GetText(this.nodes[next])[0] : '\0';
                if (following is not (' ' or '\t' or '\n'))
                {
                    return false;
                }

                descriptionStart = data.TextStart >= 0 ? data.TextStart + colon + 1 : data.End;
                return true;
            }

            if (code)
            {
                return false;
            }

            if (!heading && data.Next == 0)
            {
                return false;
            }

            if (heading && data.Next == 0 && builder is null)
            {
                name = value.ToString();
                return name.Length > 0;
            }

            (builder ??= new()).Append(value);
            node = data.Next;
        }

        if (!heading || builder is null)
        {
            return false;
        }

        name = builder.ToString();
        return name.Length > 0;
    }
}
