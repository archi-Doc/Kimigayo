// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a named set of semantics in a type-constraint expression.
/// </summary>
[TinyhandObject]
public sealed partial class SemanticsMaskKoto : Koto
{
    public override KotoKind Akind => KotoKind.SemanticsMask;

    [Key(1)]
    public SemanticsMask Mask { get; private set; }

    internal SemanticsMaskKoto(ref TokenReader reader, SourceSpan range, SemanticsMask mask)
        : base(ref reader, range)
    {
        this.Mask = mask;
    }

    public override void WriteTo(ref IndentedStringBuilder builder)
        => builder.Append(this.Mask.ToText());
}
