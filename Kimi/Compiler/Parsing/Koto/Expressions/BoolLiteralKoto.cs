// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a Boolean literal expression.
/// </summary>
public sealed class BoolLiteralKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.BoolLiteral;

    /// <summary>Gets a value indicating whether the literal is <see langword="true"/>.</summary>
    public bool Value { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="BoolLiteralKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="token">The Boolean literal token.</param>
    public BoolLiteralKoto(ref TokenReader reader, Token token)
        : base(ref reader, token.Span)
    {
        this.Value = token.Kind == TokenKind.True;
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteAttributeChainTo(ref builder, KotoWriteOptions.AppendSpace);
        builder.Append(this.Value ? Constants.TrueKeyword : Constants.FalseKeyword);
    }
}
