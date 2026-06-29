// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

[TinyhandObject]
public sealed partial class ConditionNegateKoto : Koto
{
    [Key(1)]
    public Koto Koto { get; private set; }

    public ConditionNegateKoto(Koto conditionKoto)
    {
        this.Koto = conditionKoto;
    }

    [TinyhandOnDeserialized]
    private void OnDeserialized()
    {
        this.Koto.Parent = this;
    }
}
