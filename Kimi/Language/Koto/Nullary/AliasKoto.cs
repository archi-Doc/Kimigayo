// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimigayo.Diagnostics;

namespace Kimigayo.Language;

[TinyhandObject]
public sealed partial class AliasKoto : Koto
{
    [Key(1)]
    public List<string> Alias { get; private set; }

    public AliasKoto(ref TokenReader reader, List<string> alias)
        : base(ref reader, default)
    {
        this.Alias = alias;
    }

    public override string ToString()
        => $"alias {string.Join(Constants.DotChar, this.Alias)}";

    public override (string Text, Koto[]? Children) Dump()
    {
        return ($"{this.GetType().Name}({string.Join(Constants.DotChar, this.Alias)})", default);
    }
}
