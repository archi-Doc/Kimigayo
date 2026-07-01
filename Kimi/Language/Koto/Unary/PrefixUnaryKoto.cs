// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

[TinyhandObject]
public partial class PrefixUnaryKoto : Koto
{// + - not ~ ++ -- * &
    [Key(1)]
    public TokenKind Kind { get; private set; }

    [Key(2)]
    public Koto Operand { get; private set; }

    public PrefixUnaryKoto(ref TokenReader reader, Token token, Koto operand)
        : base(ref reader, token.Range)
    {
        this.Kind = token.Kind;
        this.Operand = operand;
        operand.Parent = this;
    }

    public override string ToString()
        => $"{this.Kind.ToText()}{this.Operand.ToString()}";

    public override (string Text, Koto[]? Children) Dump()
    {
        return ($"{this.GetType().Name}({this.Kind.ToText()})", [this.Operand,]);
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
