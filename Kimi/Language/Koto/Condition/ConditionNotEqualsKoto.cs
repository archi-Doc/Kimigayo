// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

[TinyhandObject]
public sealed partial class ConditionNotEqualsKoto : ConditionKoto
{
    [Key(0)]
    public ConditionKoto Left { get; private set; }

    [Key(1)]
    public ConditionKoto Right { get; private set; }

    public ConditionNotEqualsKoto(ConditionKoto left, ConditionKoto right)
    {
        this.Left = left;
        this.Right = right;
    }

    public override string ToString()
        => $"({this.Left} != {this.Right})";
}
