// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Preserves Origin declaration locations without allocating storage on Origin-free nodes.</summary>
internal sealed class OriginNameList : List<string>
{
    internal List<SourceSpan> Spans { get; } = new();

    internal void Add(string name, SourceSpan span)
    {
        this.Add(name);
        this.Spans.Add(span);
    }
}
