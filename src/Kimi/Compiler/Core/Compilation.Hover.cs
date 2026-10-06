// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public partial class Compilation
{
    private List<HoverAnchor>? hoverAnchors;

    /// <summary>Gets or sets a value indicating whether exact editor token positions are collected during parsing.</summary>
    internal bool CollectHover { get; set; }

    /// <summary>Gets parser-established positions, including every split declaration fragment.</summary>
    internal IReadOnlyList<HoverAnchor> HoverAnchors => this.hoverAnchors ?? (IReadOnlyList<HoverAnchor>)Array.Empty<HoverAnchor>();

    internal void RecordHover(Koto syntax, SourceDocument source, SourceSpan token)
    {
        if (this.CollectHover && token.Length != 0)
        {
            (this.hoverAnchors ??= []).Add(new(syntax, source, token));
        }
    }

    /// <summary>A temporary parser fact. Only its immutable projection may enter a checking result.</summary>
    /// <param name="Syntax">The exact syntax whose semantic result owns the token.</param>
    /// <param name="Source">The source of this fragment, which may differ from the merged declaration's source.</param>
    /// <param name="Token">The representative token.</param>
    internal readonly record struct HoverAnchor(Koto Syntax, SourceDocument Source, SourceSpan Token);
}
