// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Kimi.Compiler.Lexing;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public partial class IdentifierNameKoto : Koto
{
    public override KotoKind Akind => KotoKind.IdentifierName;

    public static readonly IdentifierNameKoto Error;

    static IdentifierNameKoto()
    {
        Error = IdentifierNameKoto.UnsafeConstructor();
    }

    public static bool TryCreate(ref TokenReader reader, Token token, [MaybeNullWhen(false)] out IdentifierNameKoto koto)
    {
        if (KotoHelper.IsValidIdentifier(reader.GetSpan(token)))
        {
            koto = new(ref reader, token);
            return true;
        }
        else
        {
            koto = default;
            return false;
        }
    }

    [Key(1)]
    public string Identifier { get; private set; }

    public IdentifierNameKoto(ref TokenReader reader, Token token)
        : base(ref reader, token.Range)
    {
        if (token.Kind == TokenKind.Identifier)
        {
            this.Identifier = reader.GetSpan(token).ToString();
        }
        else
        {
            this.Identifier = token.Kind.ToText();
        }
    }

    public override string ToString()
        => $"{this.Identifier}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        if (this.AttributeChain is not null)
        {
            KotoParser.UnparseAttribute(this.AttributeChain, ref builder, KotoWriteOptions.AppendSpace);
        }

        builder.Append(this.Identifier);
    }

    public override (string Text, Koto[]? Children) Dump()
    {
        return ($"{this.GetType().Name}({this.Identifier})", default);
    }
}
