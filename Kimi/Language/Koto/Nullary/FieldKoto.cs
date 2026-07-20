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
    public UnresolvedKoto NameKoto { get; private set; }

    [Key(4)]
    public Koto? InitializerKoto { get; private set; }

    [IgnoreMember]
    private Token typeToken;

    public FieldKoto(ref TokenReader reader, Token token, Token typeToken, UnresolvedKoto nameKoto, Koto? initializerKoto)
        : base(ref reader, token.Range)
    {
        this.Modifier = reader.ModifierKind;
        this.VariableKind = token.Kind == TokenKind.Let ? VariableKind.Let : VariableKind.Var;
        this.typeToken = typeToken;
        this.NameKoto = nameKoto;
        this.InitializerKoto = initializerKoto;
    }

    public override string ToString()
        => $"{this.Modifier.ToText()}";

    public override (string Text, Koto[]? Children) Dump()
    {
        return ($"{this.GetType().Name}()", default);
    }
}
