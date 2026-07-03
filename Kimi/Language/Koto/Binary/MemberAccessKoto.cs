// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimigayo.Diagnostics;

namespace Kimigayo.Language;

[TinyhandObject]
public partial class MemberAccessKoto : Koto
{
    [Key(1)]
    public Koto Left { get; private set; }

    [Key(2)]
    public Koto Accessor { get; private set; }

    public MemberAccessKoto(ref TokenReader reader, SourceRange range, Koto left, Koto accessor)
        : base(ref reader, range)
    {
        this.Left = left;
        this.Accessor = accessor;
        left.Parent = this;
        accessor.Parent = this;
    }

    public override string ToString()
        => $"{this.Left.ToString()}{Constants.DotChar}{this.Accessor.ToString()}";
}
