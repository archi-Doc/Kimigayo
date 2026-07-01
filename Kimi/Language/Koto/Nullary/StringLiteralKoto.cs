// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimigayo.Diagnostics;

namespace Kimigayo.Language;

[TinyhandObject]
public sealed partial class StringLiteralKoto : Koto
{
    [Key(1)]
    public string Literal { get; private set; }

    public StringLiteralKoto(ref TokenReader reader, Token token)
        : base(ref reader, token.Range)
    {
        this.Literal = token.Text.ToString();
    }

    public override string ToString()
        => $"\"{this.Literal}\"";
}
