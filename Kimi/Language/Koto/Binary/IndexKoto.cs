// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimigayo.Diagnostics;

namespace Kimigayo.Language;

[TinyhandObject]
public partial class IndexKoto : Koto
{
    [Key(1)]
    public Koto Left { get; private set; }

    [Key(2)]
    public Koto Index { get; private set; }

    public IndexKoto(ref TokenReader reader, SourceRange range, Koto left, Koto index)
        : base(ref reader, range)
    {
        this.Left = left;
        this.Index = index;
        this.Left.Parent = this;
        this.Index.Parent = this;
    }

    public override string ToString()
        => $"{this.Left.ToString()}[{this.Index.ToString()}]";
}
