// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler.Helper;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Represents a char literal containing one Unicode scalar value.</summary>
public sealed class CharLiteralKoto : ExpressionKoto
{
    private readonly string rawLiteral;

    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.CharLiteral;

    /// <summary>Gets the scalar value, or null for a malformed literal.</summary>
    public Rune? Value { get; }

    /// <summary>Gets or sets the key of the lexical Error that rejected a malformed literal's value, its own record
    /// (<c>InvalidCharLiteral_Kd</c>, or a malformed escape's, the last one); null for a readable literal. A check reading the value
    /// rests on it (Binding's RestingCauses).</summary>
    internal DiagnosticKey? Cause { get; set; }

    /// <summary>Initializes a new instance of the <see cref="CharLiteralKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="token">The complete char literal token.</param>
    public CharLiteralKoto(ref TokenReader reader, Token token)
        : base(ref reader, token.Span)
    {
        this.rawLiteral = reader.GetSpan(token).ToString();
        this.Value = CharLiteralHelper.Decode(this.rawLiteral, this);
        this.Cause = this.Value is null ? this.DiagnosticCollection?.LastError : null;
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteAttributeChainTo(ref builder, KotoWriteOptions.AppendSpace);
        builder.AppendVerbatim(this.rawLiteral);
    }
}
