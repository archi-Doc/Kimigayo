// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimigayo.Diagnostics;

namespace Kimigayo.Language;

[TinyhandObject]
public partial class AttributeKoto : PrefixUnaryKoto
{
    public AttributeKoto(ref TokenReader reader, SourceRange range, Koto operand)
        : base(ref reader, range, operand)
    {
    }

    public AttributeKoto()
    {
    }

    public override string ToString()
        => $"#{this.Operand.ToString()}";
}

[TinyhandObject]
public partial class PrefixUnaryKoto : Koto
{// + - not ~ ++ -- * &
    // [Key(1)]
    // public TokenKind Kind { get; private set; }

    [Key(1)]
    public Koto Operand { get; private set; }

    public PrefixUnaryKoto(ref TokenReader reader, SourceRange range, Koto operand)
        : base(ref reader, range)
    {
        // this.Kind = token.Kind;
        this.Operand = operand;
        operand.Parent = this;
    }

    public PrefixUnaryKoto()
    {
    }

    public override string ToString()
        => $"Unary:{this.Operand.ToString()}";

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
