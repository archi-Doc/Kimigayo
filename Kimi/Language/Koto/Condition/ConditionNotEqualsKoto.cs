// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

[TinyhandObject]
public sealed partial class ConditionNotEqualsKoto : Koto
{
    [Key(1)]
    public Koto Left { get; private set; }

    [Key(2)]
    public Koto Right { get; private set; }

    public ConditionNotEqualsKoto(Koto left, Koto right)
    {
        this.Left = left;
        this.Right = right;
    }

    public override string ToString()
        => $"({this.Left} != {this.Right})";

    [TinyhandOnDeserialized]
    private void OnDeserialized()
    {
        this.Left.Parent = this;
        this.Right.Parent = this;
    }
}
