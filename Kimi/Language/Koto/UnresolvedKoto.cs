// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

[TinyhandObject]
public partial class UnresolvedKoto : Koto
{
    [Key(0)]
    public string Identifier { get; private set; }

    public UnresolvedKoto(Token token)
    {
        this.Identifier = token.Text.ToString();
    }
}
