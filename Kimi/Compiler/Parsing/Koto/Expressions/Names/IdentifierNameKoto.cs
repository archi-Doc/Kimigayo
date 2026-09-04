// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Kimi.Compiler.Helper;
using Kimi.Compiler.Lexing;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents an identifier expression.
/// </summary>
[TinyhandObject]
public sealed partial class IdentifierNameKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.IdentifierName;

    /// <summary>An invalid identifier node used during error recovery.</summary>
    public static readonly IdentifierNameKoto Error = UnsafeConstructor();

    /// <summary>Attempts to create an identifier node from a token.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="token">The identifier token.</param>
    /// <param name="koto">The created identifier node.</param>
    /// <returns><see langword="true"/> when the token contains a valid identifier.</returns>
    public static bool TryCreate(ref TokenReader reader, Token token, [MaybeNullWhen(false)] out IdentifierNameKoto koto)
    {
        var span = reader.GetSpan(token);
        if (token.Kind.IsIdentifierOrContextualKeyword() &&
            IdentifierHelper.IsValidIdentifier(span))
        {
            koto = new(ref reader, token, reader.GetIdentifier(token));
            return true;
        }

        reader.AddDiagnostic(DiagnosticCode.InvalidIdentifier_Kd, span.ToString());
        koto = default;
        return false;
    }

    /// <summary>Gets the identifier text.</summary>
    [Key(1)]
    public string IdentifierName { get; private set; }

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
