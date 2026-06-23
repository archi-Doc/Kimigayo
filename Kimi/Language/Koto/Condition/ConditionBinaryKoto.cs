// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

[TinyhandObject]
public sealed partial class ConditionBinaryKoto : ConditionKoto
{
    [Key(0)]
    public KotoKind Operator { get; private set; }

    [Key(1)]
    public ConditionKoto Left { get; private set; }

    [Key(2)]
    public ConditionKoto Right { get; private set; }

    public ConditionBinaryKoto(ConditionKoto left, ConditionKoto right)
    {
        this.Left = left;
        this.Right = right;
    }
}
