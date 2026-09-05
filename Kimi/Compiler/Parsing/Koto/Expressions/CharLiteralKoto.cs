// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler.Helper;
using Kimi.Compiler.Lexing;

namespace Kimi.Compiler.Parsing;

/// <summary>Represents a char literal containing one Unicode scalar value.</summary>
public sealed class CharLiteralKoto : ExpressionKoto
{
    private readonly string rawLiteral;

    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.CharLiteral;

    /// <summary>Gets the scalar value, or null for a malformed literal.</summary>
    public Rune? Value { get; }

    /// <summary>Initializes a new instance of the <see cref="CharLiteralKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="token">The complete char literal token.</param>
    public CharLiteralKoto(ref TokenReader reader, Token token)
        : base(ref reader, token.Span)
    {
        this.rawLiteral = reader.GetSpan(token).ToString();
        this.Value = CharLiteralHelper.Decode(this.rawLiteral, this);
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteAttributeChainTo(ref builder, KotoWriteOptions.AppendSpace);
        builder.AppendVerbatim(this.rawLiteral);
    }
}
