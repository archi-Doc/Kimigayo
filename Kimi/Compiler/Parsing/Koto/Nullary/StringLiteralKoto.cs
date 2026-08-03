// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public sealed partial class StringLiteralKoto : Koto
{
    [Key(1)]
    private string rawLiteral;

    public string Literal
    {
        get
        {
            if (field is not null)
            {
                return field;
            }

            field = KotoHelper.ParseLiteral(this.rawLiteral, this);
            return field;
        }
    }

    public StringLiteralKoto(ref TokenReader reader, Token token)
        : base(ref reader, token.Range)
    {
        this.rawLiteral = token.Text.ToString();
    }

    public override string ToString()
        => $"{this.rawLiteral}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        if (this.AttributeChain is not null)
        {
            KotoParser.UnparseAttribute(this.AttributeChain, ref builder, KotoWriteOptions.AppendSpace);
        }

        builder.Append(this.rawLiteral);
    }

    public override (string Text, Koto[]? Children) Dump()
    {
        return ($"{this.GetType().Name}({this.rawLiteral})", default);
    }
}
