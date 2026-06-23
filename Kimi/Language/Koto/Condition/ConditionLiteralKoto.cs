// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

[TinyhandObject]
public sealed partial class ConditionLiteralKoto : ConditionKoto
{
    [Key(0)]
    public string Literal { get; private set; }

    public ConditionLiteralKoto(Token token)
    {
        this.Literal = token.Text.ToString();
    }

    public override string ToString()
        => $"'{this.Literal}'";
}
