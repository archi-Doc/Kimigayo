// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Kimi.Compiler.Helper;
using Kimi.Compiler.Lexing;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public partial class GenericSemanticsKoto : IdentifierNameKoto
{
    public static bool TryCreate(ref TokenReader reader, Token token, [MaybeNullWhen(false)] out GenericSemanticsKoto koto)
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
            reader.AddDiagnostic(Hashed.Kimi.InvalidIdentifier, identifierName);

            koto = default;
            return false;
        }
    }

    private GenericSemanticsKoto(ref TokenReader reader, Token token, string identifierName)
        : base(ref reader, token, identifierName)
    {
    }

    public override string ToString()
        => $"{Constants.AmpersandChar}{this.IdentifierName}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        if (this.AttributeChain is not null)
        {
            KotoParser.UnparseAttribute(this.AttributeChain, ref builder, KotoWriteOptions.AppendSpace);
        }

        builder.Append(Constants.AmpersandChar);
        builder.Append(this.IdentifierName);
    }
}
