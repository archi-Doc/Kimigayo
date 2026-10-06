// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Documentation;
using Kimi.Diagnostics;

namespace Kimi.Checking;

#pragma warning disable SA1402, SA1600, SA1649 // The internal, detached Hover vocabulary is kept together.

// Arrays are created and owned by the projection. No compiler node or declaration identity crosses this boundary.
internal sealed record HoverOrigin(string Project, string Source, SourceSpan Name);

internal sealed record HoverDocumentation(
    SourceDocument Source,
    SourceSpan Span,
    int Indent,
    string Project,
    string? LogicalName,
    string? ModId,
    int AdditionOrder,
    SourceSpan DeclarationSpan,
    DocumentationMarkdownParameter[] Parameters);

internal sealed record HoverDeclaration(
    string Kind,
    string Owner,
    string Header,
    HoverOrigin[] Origins,
    HoverDocumentation[] Documentation,
    string? DocumentationNotice = null,
    string? Parameter = null,
    string? Details = null,
    bool ImplementationNote = false,
    HoverKey? Identity = null);

internal sealed record HoverInfo(
    HoverDeclaration[] Declarations,
    string? Use = null,
    string? CopyType = null,
    ConstraintProof? Copy = null,
    string? Effects = null,
    HoverKey? TypeIdentity = null);

/// <summary>A detached structural identity DAG. Equal hashes or equal presentation never replace exact comparison.</summary>
internal sealed record HoverKey(string Value, HoverKey[] Parts)
{
    internal static readonly HoverKey Missing = new("missing", []);
}

internal readonly record struct HoverEntry(SourceSpan Span, HoverInfo Info);

/// <summary>One own-project source's immutable token index, also usable before the editor opens the source.</summary>
internal sealed class HoverDocument(SourceDocument source, HoverEntry[] entries)
{
    internal SourceDocument Source { get; } = source;

    internal ReadOnlySpan<HoverEntry> Entries => entries;

    internal int Find(int offset)
    {
        var low = 0;
        var high = entries.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            var span = entries[middle].Span;
            if (offset < span.Start)
            {
                high = middle - 1;
            }
            else if (offset >= span.End)
            {
                low = middle + 1;
            }
            else
            {
                return middle;
            }
        }

        return -1;
    }
}
