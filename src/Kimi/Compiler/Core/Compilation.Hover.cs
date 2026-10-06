// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Checking;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public partial class Compilation
{
    private List<HoverAnchor>? hoverAnchors;
    private Dictionary<SourceDocument, string>? documentationFailures;

    internal string? HoverFailure { get; private set; }

    internal string? DocumentationFailure { get; private set; }

    internal IEnumerable<SourceDocument> DocumentationFailureSources => this.documentationFailures?.Keys ?? Enumerable.Empty<SourceDocument>();

    /// <summary>Gets or sets a value indicating whether exact editor token positions are collected during parsing.</summary>
    internal bool CollectHover { get; set; }

    /// <summary>Gets parser-established positions, including every split declaration fragment.</summary>
    internal IReadOnlyList<HoverAnchor> HoverAnchors => this.hoverAnchors ?? (IReadOnlyList<HoverAnchor>)Array.Empty<HoverAnchor>();

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
            }
            catch (Exception ex) when (OptionalHoverFailure(ex))
            {
                this.HoverFailure = ex.Message;
                this.hoverAnchors = null;
            }
        }
    }

    internal bool HasDocumentationFailure(SourceDocument source) => this.documentationFailures?.ContainsKey(source) == true;

    internal void RecordDocumentationFailure(SourceDocument? source, string? failure, bool invalidateSource = true)
    {
        if (failure is not null)
        {
            if (source is not null && invalidateSource)
            {
                (this.documentationFailures ??= new(ReferenceEqualityComparer.Instance)).TryAdd(source, failure);
            }

            this.DocumentationFailure ??= (source?.Path ?? "Generated source") + ": " + failure;
        }
    }

    /// <summary>A temporary parser fact. Only its immutable projection may enter a checking result.</summary>
    /// <param name="Syntax">The exact syntax whose semantic result owns the token.</param>
    /// <param name="Source">The source of this fragment, which may differ from the merged declaration's source.</param>
    /// <param name="Token">The representative token.</param>
    internal readonly record struct HoverAnchor(Koto Syntax, SourceDocument Source, SourceSpan Token);
}
