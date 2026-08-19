// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public sealed partial class BoolLiteralKoto : Koto
{
    public override KotoKind Akind => KotoKind.BoolLiteral;

    [Key(1)]
    public bool Value { get; private set; }

    public BoolLiteralKoto(ref TokenReader reader, Token token)
        : base(ref reader, token.Span)
    {
        if (token.Kind == TokenKind.True)
        {
            this.Value = true;
        }
    }

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

    public override (string Text, Koto[]? Children) Dump()
    {
        return ($"{this.GetType().Name}({this.ToString()})", default);
    }
}
