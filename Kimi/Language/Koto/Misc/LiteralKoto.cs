// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

[TinyhandObject]
public sealed partial class LiteralKoto : Koto
{
    [Key(0)]
    public string Literal { get; private set; }

    public LiteralKoto(Token token)
    {
        this.Literal = token.Text.ToString();
    }

    public override string ToString()
        => $"'{this.Literal}'";
}
