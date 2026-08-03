// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public partial class TypeKoto : Koto
{
    public override KotoKind _Kind => KotoKind.Type;

    static TypeKoto()
    {
    }

    private TokenKind tokenKind;
    private string? identifier;

    public string Identifier
    {
        get
        {
            if (this.tokenKind.IsPrimitiveType())
            {
                return this.tokenKind.ToText();
            }
            else
            {
                return this.identifier ?? string.Empty;
            }
        }
    }

    public TypeKoto(ref TokenReader reader, Token token)
        : base(ref reader, token.Range)
    {
        this.tokenKind = token.Kind;
        if (!this.tokenKind.IsPrimitiveType())
        {
            this.identifier = token.Text.ToString();
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
