// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

[TinyhandObject]
public partial class AliasKoto : Koto
{// alias Kimi.Base
    [Key(1)]
    public string Alias { get; private set; }

    public AliasKoto(ref TokenReader reader, Token token)
        : base(ref reader, token.Range)
    {
        this.Alias = token.Text.ToString();
    }

    public override string ToString()
        => $"{Constants.AliasKeyword} {this.Alias}";
}
