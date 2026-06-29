// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimigayo.Diagnostics;

namespace Kimigayo.Language;

[TinyhandObject]
public sealed partial class ConditionNegateKoto : Koto
{
    [Key(1)]
    public Koto Koto { get; private set; }

    public ConditionNegateKoto(ref TokenReader reader, SourceRange range, Koto conditionKoto)
        : base(ref reader, range)
    {
        this.Koto = conditionKoto;
        this.Koto.Parent = this;
    }

    [TinyhandOnDeserialized]
    private void OnDeserialized()
    {
        this.Koto.Parent = this;
    }
}
