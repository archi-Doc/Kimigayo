// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

[TinyhandObject]
public partial class MemberAccessKoto : Koto
{
    [Key(1)]
    public Koto Accessor { get; private set; }

    public MemberAccessKoto(ref TokenReader reader, Token token, Koto accessor)
        : base(ref reader, token.Range)
    {
        this.Accessor = accessor;
        accessor.Parent = this;
    }

    public override string ToString()
        => $"{TokenHelper.Dot}{this.Operand.ToString()}";
}
