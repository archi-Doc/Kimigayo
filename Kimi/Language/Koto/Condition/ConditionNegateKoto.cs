// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

[TinyhandObject]
public sealed partial class ConditionNegateKoto : ConditionKoto
{
    [Key(0)]
    public ConditionKoto ConditionKoto { get; private set; }

    public ConditionNegateKoto(ConditionKoto conditionKoto)
    {
        this.ConditionKoto = conditionKoto;
    }
}
