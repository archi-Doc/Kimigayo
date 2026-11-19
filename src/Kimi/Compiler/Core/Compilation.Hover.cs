// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Checking;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public partial class Compilation
{
    private List<HoverAnchor>? hoverAnchors;
    private Dictionary<SourceDocument, string>? documentationFailures;
    private string? documentationFailure;

    // The detached library's documentation failures, its first failure and its hover anchors (PLAN G949 Stage 2 U3; design 4.5): read once
    // by ReadLibrary (ReadHoverData) for a compilation given a frozen or refused library; unset for one that made its library, whose own
    // data hold them. The readers below compose the compilation's data with the library's.
    private IReadOnlyDictionary<SourceDocument, string>? libraryFailures;
    private string? libraryFailure;
    private IReadOnlyList<HoverAnchor>? libraryAnchors;

    // The compilation's anchors, then its detached library's (the implementation review's N5): composed by index once, and again only
    // after RecordHover adds an anchor of the compilation's, so a warm read allocates nothing.
    private List<HoverAnchor>? composedAnchors;

    internal string? HoverFailure { get; private set; }

    internal string? DocumentationFailure => this.documentationFailure ?? this.libraryFailure;

    internal IEnumerable<SourceDocument> DocumentationFailureSources
        => this.libraryFailures is not { Count: > 0 } library
            ? this.documentationFailures?.Keys ?? Enumerable.Empty<SourceDocument>()
            : (this.documentationFailures?.Keys ?? Enumerable.Empty<SourceDocument>()).Concat(library.Keys);

    /// <summary>Gets or sets a value indicating whether exact editor token positions are collected during parsing.</summary>
    internal bool CollectHover { get; set; }

    /// <summary>Gets parser-established positions, including every split declaration fragment: the compilation's, then its detached
    /// library's.</summary>
    internal IReadOnlyList<HoverAnchor> HoverAnchors
        => this.libraryAnchors is not { Count: > 0 } library ? this.OwnHoverAnchors : this.composedAnchors ??= Compose(this.OwnHoverAnchors, library);

    private IReadOnlyList<HoverAnchor> OwnHoverAnchors => this.hoverAnchors ?? (IReadOnlyList<HoverAnchor>)Array.Empty<HoverAnchor>();

    internal static bool OptionalHoverFailure(Exception ex) => ex is not (PendingInputException or OperationCanceledException);

    internal void RecordHover(Koto syntax, SourceDocument source, SourceSpan token)
    {
        if (this.CollectHover && this.HoverFailure is null && token.Length != 0)
        {
            try
            {
                if (this.hoverAnchors?.Count >= HoverLimits.Work)
                {
                    throw new HoverLimitException("Hover anchor limit exceeded");
                }

                (this.hoverAnchors ??= []).Add(new(syntax, source, token));
                this.composedAnchors = null;
            }
            catch (Exception ex) when (OptionalHoverFailure(ex))
            {
                this.HoverFailure = ex.Message;
                (this.hoverAnchors, this.composedAnchors) = (null, null);
            }
        }
    }

    internal bool HasDocumentationFailure(SourceDocument source)
        => this.documentationFailures?.ContainsKey(source) == true || this.libraryFailures?.ContainsKey(source) == true;

    /// <summary>Gets a module's documentation failures and hover anchors as data (PLAN G949 Stage 2 U2b; design 5.2: the frozen library
    /// carries its own, which its freeze compilation recorded).</summary>
    /// <param name="module">The module.</param>
    /// <returns>The failures of its documented sources and the anchors of its syntax.</returns>
    internal (IReadOnlyDictionary<SourceDocument, string> Failures, HoverAnchor[] Anchors) HoverDataOf(Kotonoha module)
    {
        var failures = new Dictionary<SourceDocument, string>(ReferenceEqualityComparer.Instance);
        var documentation = module.DocumentationSources;
        for (var i = 0; i < documentation.Count; i++)
        {
            if (this.documentationFailures?.TryGetValue(documentation[i].Source, out var failure) == true)
            {
                failures[documentation[i].Source] = failure;
            }
        }

        var anchors = this.hoverAnchors?.Where(x => ReferenceEquals(x.Syntax.Kotonoha, module)).ToArray() ?? [];
        return (failures, anchors);
    }

    internal void RecordDocumentationFailure(SourceDocument? source, string? failure, bool invalidateSource = true)
    {
        if (failure is not null)
        {
            if (source is not null && invalidateSource)
            {
                (this.documentationFailures ??= new(ReferenceEqualityComparer.Instance)).TryAdd(source, failure);
            }

            this.documentationFailure ??= (source?.Path ?? "Generated source") + ": " + failure;
        }
    }

    // The two anchor lists, the compilation's first, read by index (no interface enumeration).
    private static List<HoverAnchor> Compose(IReadOnlyList<HoverAnchor> own, IReadOnlyList<HoverAnchor> library)
    {
        var all = new List<HoverAnchor>(own.Count + library.Count);
        for (var i = 0; i < own.Count; i++)
        {
            all.Add(own[i]);
        }

        for (var i = 0; i < library.Count; i++)
        {
            all.Add(library[i]);
        }

        return all;
    }

    // ReadLibrary's hover half: the library's failures and anchors, and its first failure in its documentation sources' order (the order
    // its freeze recorded them, as a compilation's first failure is its first recorded), formed once.
    private void ReadHoverData(KimiLibrary library)
    {
        (this.libraryFailures, this.libraryAnchors) = (library.DocumentationFailures, library.HoverAnchors);
        var documentation = library.Kotonoha.DocumentationSources;
        for (var i = 0; i < documentation.Count && this.libraryFailure is null; i++)
        {
            if (this.libraryFailures.TryGetValue(documentation[i].Source, out var failure))
            {
                this.libraryFailure = documentation[i].Source.Path + ": " + failure;
            }
        }
    }

    /// <summary>A temporary parser fact. Only its immutable projection may enter a checking result.</summary>
    /// <param name="Syntax">The exact syntax whose semantic result owns the token.</param>
    /// <param name="Source">The source of this fragment, which may differ from the merged declaration's source.</param>
    /// <param name="Token">The representative token.</param>
    internal readonly record struct HoverAnchor(Koto Syntax, SourceDocument Source, SourceSpan Token);
}
