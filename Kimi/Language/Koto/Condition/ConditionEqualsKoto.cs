// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

[TinyhandObject]
public sealed partial class ConditionEqualsKoto : ConditionKoto
{
    [Key(0)]
    public ConditionKoto Left { get; private set; }

    [Key(1)]
    public ConditionKoto Right { get; private set; }

    public ConditionEqualsKoto(ConditionKoto left, ConditionKoto right)
    {
        this.Left = left;
        this.Right = right;
    }
}
