// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

[TinyhandObject]
public partial class PrefixUnaryKoto : Koto
{
    [Key(1)]
    public TokenKind Kind { get; private set; }

    [Key(2)]
    public Koto Operand { get; private set; }

    public PrefixUnaryKoto(ref TokenReader reader, Token token, Koto operand)
        : base(ref reader, token.Range)
    {
        this.Kind = token.Kind;
        this.Operand = operand;
    }

    public override string ToString()
        => $"{this.Kind.ToText()}{this.Operand.ToString()}";
}
