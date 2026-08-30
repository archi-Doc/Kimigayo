// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Helper;
using Kimi.Compiler.Lexing;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a string literal expression.
/// </summary>
[TinyhandObject]
public sealed partial class StringLiteralKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.StringLiteral;

    [Key(1)]
    private string rawLiteral;

    /// <summary>Gets the decoded string value.</summary>
    public string Literal
    {
        get
        {
            if (field is not null)
            {
                return field;
            }

            // Decode lazily and cache the result because diagnostics are reported during decoding.
            field = StringLiteralHelper.GetStringLiteralValue(this.rawLiteral, this);
            return field;
        }
    }

    /// <summary>Initializes a new instance of the <see cref="StringLiteralKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="token">The string literal token.</param>
    public StringLiteralKoto(ref TokenReader reader, Token token)
        : base(ref reader, token.Span)
    {
        this.rawLiteral = reader.GetSpan(token).ToString();
    }

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.rawLiteral}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        if (this.AttributeChain is not null)
        {
            Parser.UnparseAttribute(this.AttributeChain, ref builder, KotoWriteOptions.AppendSpace);
        }

        builder.Append(this.rawLiteral);
    }
}
