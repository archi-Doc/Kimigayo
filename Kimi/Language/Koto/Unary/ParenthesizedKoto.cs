// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimigayo.Diagnostics;

namespace Kimigayo.Language;

[TinyhandObject]
public partial class ParenthesizedKoto : Koto
{
    [Key(1)]
    public Koto Operand { get; private set; }

    public ParenthesizedKoto(ref TokenReader reader, SourceRange range, Koto koto)
        : base(ref reader, range)
    {
        this.Operand = koto;
        koto.Parent = this;
    }

    public override string ToString()
        => $"({this.Operand.ToString()})";

    public override (string Text, Koto[]? Children) Dump()
    {
        return ($"{this.GetType().Name}", [this.Operand,]);
    }

    internal override bool ReplaceChild(Koto oldKoto, Koto newKoto)
    {
        if (oldKoto == this.Operand)
        {
            this.Operand = newKoto;
            newKoto.Parent = this;
            oldKoto.Parent = default;
            return true;
        }

        return false;
    }
}
