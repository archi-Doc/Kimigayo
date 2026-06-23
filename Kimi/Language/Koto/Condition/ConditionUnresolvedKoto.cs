// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

[TinyhandObject]
public sealed partial class ConditionUnresolvedKoto : ConditionKoto
{
    [Key(0)]
    public string Identifier { get; private set; }

    public ConditionUnresolvedKoto(Token token)
    {
        this.Identifier = token.Text.ToString();
    }

    public override string ToString()
        => $"({this.Identifier})";
}
