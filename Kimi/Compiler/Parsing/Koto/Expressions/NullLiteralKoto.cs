// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>A null raw pointer whose Type must be supplied by its context during Binding.</summary>
public sealed class NullLiteralKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.NullLiteral;

    /// <summary>Initializes a new instance of the <see cref="NullLiteralKoto"/> class.</summary>
    /// <param name="reader">The owning token reader.</param>
    /// <param name="span">The literal's source span.</param>
    public NullLiteralKoto(ref TokenReader reader, SourceSpan span)
        : base(ref reader, span)
    {
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteAttributeChainTo(ref builder, KotoWriteOptions.AppendSpace);
        builder.Append("null");
    }
}
