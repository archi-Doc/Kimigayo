// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public sealed partial class BoolLiteralKoto : Koto
{
    [Key(1)]
    public bool Value { get; private set; }

    public BoolLiteralKoto(ref TokenReader reader, Token token)
        : base(ref reader, token.Range)
    {
        if (token.Kind == TokenKind.True)
        {
            this.Value = true;
        }
    }

    public override string ToString()
    {
        if (this.Value)
        {
            return TokenKind.True.ToText();
        }
        else
        {
            return TokenKind.False.ToText();
        }
    }

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
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
