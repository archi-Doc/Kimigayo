// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

[TinyhandObject]
public partial class BinaryKoto : Koto
{
    [Key(1)]
    public TokenKind Kind { get; private set; }

    [Key(2)]
    public Koto Left { get; private set; }

    [Key(3)]
    public Koto Right { get; private set; }

    public BinaryKoto(ref TokenReader reader, Token token, Koto left, Koto right)
        : base(ref reader, token.Range)
    {
        this.Kind = token.Kind;
        this.Left = left;
        this.Right = right;
        this.Left.Parent = this;
        this.Right.Parent = this;
    }

    public override string ToString()
        => $"{this.Left.ToString()}{this.Kind.ToText()}{this.Right.ToString()}";
}
