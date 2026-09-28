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
                    for (var level = data.Argument; level <= 6; level++)
                    {
                        if (pending[level] >= 0)
                        {
                            ref var item = ref result[pending[level]];
                            item = item with { DescriptionSpan = SourceSpan.FromBounds(item.DescriptionSpan.Start, data.Start) };
                            pending[level] = -1;
                        }
                    }

                    if (this.TryName(data.First, heading: true, out var name, out _) && IsStandardItem(name))
                    {
                        pending[data.Argument] = result.Add(new(name, new(this, block), SourceSpan.FromBounds(data.End, this.Text.Length), DocumentationMarkdownItemKind.Standard));
                    }
                }
                else if (data.Kind is DocumentationMarkdownKind.BulletList or DocumentationMarkdownKind.OrderedList)
                {
                    for (var item = data.First; item != 0; item = this.nodes[item].Next)
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

            cancellationToken.ThrowIfCancellationRequested();
            return result.Span.ToArray();
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

    // Standard names are shared literals; only other names allocate.
    private static string CreateName(ReadOnlySpan<char> name) => name switch
    {
        "return" => "return",
        "abort" => "abort",
        "safety" => "safety",
        "note" => "note",
        "warning" => "warning",
        "example" => "example",
        _ => name.ToString(),
    };

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
            name = CreateName(this.GetText(this.nodes[first]));
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
                    name = builder is null ? CreateName(value[..colon]) : builder.Append(value[..colon]).ToString();
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
                name = CreateName(value);
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
