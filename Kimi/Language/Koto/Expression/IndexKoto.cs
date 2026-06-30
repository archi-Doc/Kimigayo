// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

[TinyhandObject]
public partial class IndexKoto : Koto
{
    [Key(1)]
    public Koto Left { get; private set; }

    [Key(2)]
    public Koto Index { get; private set; }

    public IndexKoto(ref TokenReader reader, Token token, Koto left, Koto index)
        : base(ref reader, token.Range)
    {
        this.Left = left;
        this.Index = index;
        this.Left.Parent = this;
        this.Index.Parent = this;
    }

    public override string ToString()
        => $"{this.Left.ToString()}[{this.Index.ToString()}]";
}
