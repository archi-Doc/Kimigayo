// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimigayo.Diagnostics;

namespace Kimigayo.Language;

[TinyhandObject]
public partial class PrefixUnaryKoto : Koto
{
    [Key(1)]
    public Koto Koto { get; private set; }

    public PrefixUnaryKoto(ref TokenReader reader, Token token, Koto koto)
        : base(ref reader, token.Range)
    {
        this.Koto = koto;
    }

    public override string ToString()
        => $"/*{this.Comment}*/";
}
