// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

[TinyhandObject]
public partial class UnresolvedKoto : Koto
{
    [Key(1)]
    public string Identifier { get; private set; }

    public UnresolvedKoto(ref TokenReader reader, Token token)
        : base(ref reader, token.Range)
    {
        this.Identifier = token.Text.ToString();
    }

    public override string ToString()
        => $"?{this.Identifier}?";
}
