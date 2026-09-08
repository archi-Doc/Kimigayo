// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Kimi.Compiler.Lexing;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents an identifier expression.
/// </summary>
public sealed class IdentifierNameKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.IdentifierName;

    /// <summary>An invalid identifier node used during error recovery.</summary>
    public static readonly IdentifierNameKoto Error = new();

    /// <summary>Attempts to create an identifier node from a token.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="token">The identifier token.</param>
    /// <param name="koto">The created identifier node.</param>
    /// <returns><see langword="true"/> when the token contains a valid identifier.</returns>
    public static bool TryCreate(ref TokenReader reader, Token token, [MaybeNullWhen(false)] out IdentifierNameKoto koto)
    {
        if (reader.TryGetIdentifier(token, out var identifier))
        {
            koto = new(ref reader, token, identifier);
            return true;
        }

        koto = default;
        return false;
    }

    /// <summary>Gets the identifier text.</summary>
    public string IdentifierName { get; private set; }

    private IdentifierNameKoto()
        : base(null!, default)
    {
        this.IdentifierName = string.Empty;
    }

    private IdentifierNameKoto(ref TokenReader reader, Token token, string identifierName)
        : base(ref reader, token.Span)
    {
        this.IdentifierName = identifierName;
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteAttributeChainTo(ref builder, KotoWriteOptions.AppendSpace);
        builder.Append(this.IdentifierName);
    }
}
