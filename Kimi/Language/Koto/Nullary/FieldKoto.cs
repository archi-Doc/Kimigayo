// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

public enum VariableKind
{
    Var,
    Let,
}

[TinyhandObject]
public partial class FieldKoto : Koto
{// var x = 1
    [Key(1)]
    public KotoModifierKind Modifier { get; private set; }

    [Key(2)]
    public VariableKind VariableKind { get; private set; }

    [Key(3)]
    public Koto? Initializer { get; private set; }

    [IgnoreMember]
    private Token typeToken;

    public FieldKoto(ref TokenReader reader, Token token, Token typeToken, Koto? initializer)
        : base(ref reader, token.Range)
    {
        this.typeToken = typeToken;
        this.Initializer = initializer;
    }

    public override string ToString()
        => $"{this.Modifier.ToText()}";

    public override (string Text, Koto[]? Children) Dump()
    {
        return ($"{this.GetType().Name}()", default);
    }
}
