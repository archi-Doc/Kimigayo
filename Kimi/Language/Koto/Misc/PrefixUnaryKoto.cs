// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimigayo.Diagnostics;

namespace Kimigayo.Language;

[TinyhandObject]
public partial class ParenthesizedKoto : Koto
{
    [Key(1)]
    public Koto Koto { get; private set; }

    public ParenthesizedKoto(ref TokenReader reader, SourceRange range, Koto koto)
        : base(ref reader, range)
    {
        this.Koto = koto;
        koto.Parent = this;
    }

    public override string ToString()
        => $"({this.Koto.ToString()})";
}
