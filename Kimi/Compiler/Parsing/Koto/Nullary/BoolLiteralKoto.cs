// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a Boolean literal expression.
/// </summary>
[TinyhandObject]
public sealed partial class BoolLiteralKoto : Koto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.BoolLiteral;

    /// <summary>Gets a value indicating whether the literal is <see langword="true"/>.</summary>
    [Key(1)]
    public bool Value { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="BoolLiteralKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="token">The Boolean literal token.</param>
    public BoolLiteralKoto(ref TokenReader reader, Token token)
        : base(ref reader, token.Span)
    {
        if (token.Kind == TokenKind.True)
        {
            this.Value = true;
        }
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        if (this.AttributeChain is not null)
        {
            Parser.UnparseAttribute(this.AttributeChain, ref builder, KotoWriteOptions.AppendSpace);
        }

        if (this.Value)
        {
            builder.Append(TokenKind.True.ToText());
        }
        else
        {
            builder.Append(TokenKind.False.ToText());
        }
    }
}
