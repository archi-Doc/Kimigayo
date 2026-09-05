// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Represents the Unit value ().</summary>
public sealed class UnitLiteralKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.UnitLiteral;

    /// <summary>Initializes a new instance of the <see cref="UnitLiteralKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="span">The complete literal span.</param>
    public UnitLiteralKoto(ref TokenReader reader, SourceSpan span)
        : base(ref reader, span)
    {
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder) => builder.Append("()");
}
