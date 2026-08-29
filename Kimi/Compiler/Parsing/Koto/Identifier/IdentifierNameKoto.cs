// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Kimi.Compiler.Helper;
using Kimi.Compiler.Lexing;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents an identifier expression.
/// </summary>
[TinyhandObject]
public partial class IdentifierNameKoto : Koto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.IdentifierName;

    /// <summary>An invalid identifier node used during error recovery.</summary>
    public static readonly IdentifierNameKoto Error;

    static IdentifierNameKoto()
    {
        Error = IdentifierNameKoto.UnsafeConstructor();
    }

    /// <summary>Attempts to create an identifier node from a token.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="token">The identifier token.</param>
    /// <param name="koto">The created identifier node.</param>
    /// <returns><see langword="true"/> when the token contains a valid identifier.</returns>
    public static bool TryCreate(ref TokenReader reader, Token token, [MaybeNullWhen(false)] out IdentifierNameKoto koto)
    {
        var identifierName = reader.GetSpan(token).ToString();
        if (token.Kind.IsIdentifierOrContextualKeyword() &&
            IdentifierHelper.IsValidIdentifier(identifierName))
        {
            koto = new(ref reader, token, identifierName);
            return true;
        }
        else
        {
            reader.AddDiagnostic(DiagnosticCode.InvalidIdentifier_Kd, identifierName);

            koto = default;
            return false;
        }
    }

    /// <summary>Gets the identifier text.</summary>
    [Key(1)]
    public string IdentifierName { get; private set; }

    protected IdentifierNameKoto(ref TokenReader reader, Token token, string identifierName)
        : base(ref reader, token.Span)
    {
        this.IdentifierName = identifierName;
    }

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.IdentifierName}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        if (this.AttributeChain is not null)
        {
            Parser.UnparseAttribute(this.AttributeChain, ref builder, KotoWriteOptions.AppendSpace);
        }

        builder.Append(this.IdentifierName);
    }
}
