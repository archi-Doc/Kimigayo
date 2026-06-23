// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

[TinyhandObject]
public sealed partial class ConditionAndKoto : Koto
{
    [Key(0)]
    public Koto Left { get; private set; }

    [Key(1)]
    public Koto Right { get; private set; }

    public ConditionAndKoto(Koto left, Koto right)
    {
        this.Left = left;
        this.Right = right;
    }

    public override string ToString()
        => $"({this.Left} and {this.Right})";
}
