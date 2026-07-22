// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;

namespace Kimi.Compiler;

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

    public string VariableText => this.VariableKind == VariableKind.Var ? "var" : "let";

    public FieldKoto(ref TokenReader reader, ref Token token, Token typeToken, UnresolvedKoto nameKoto, Koto? initializerKoto)
        : base(ref reader, token.Range)
    {
        this.Modifier = reader.ModifierKind;
        this.VariableKind = token.Kind == TokenKind.Let ? VariableKind.Let : VariableKind.Var;
        this.typeToken = typeToken;
        this.NameKoto = nameKoto;
        this.InitializerKoto = initializerKoto;
    }

    public override string ToString()
    {
        var typeText = this.typeToken.Kind == TokenKind.Invalid ? string.Empty : $": {this.typeToken.Text}";
        return $"{this.VariableText} {this.NameKoto.Identifier}{typeText}";
    }

    public override void WriteTo(StringWriter writer)
    {
        writer.Write(this.VariableText);
        writer.Write(' ');

        writer.Write(this.NameKoto.Identifier);

        if (this.typeToken.Kind != TokenKind.Invalid)
        {// ": i32"
            writer.Write(": ");
            writer.Write(this.typeToken.Text);
        }
    }

    public override void UnparseTo(StringWriter writer)
    {// public let x: i32 = 1
        KotoParser.UnparseAttribute(this.AttributeChain, writer);
        writer.WriteLine();

        this.Modifier.WriteTo(writer, true);

        writer.Write(this.VariableText);
        writer.Write(' ');

        writer.Write(this.NameKoto.Identifier);

        if (this.typeToken.Kind != TokenKind.Invalid)
        {// ": i32"
            writer.Write(": ");
            writer.Write(this.typeToken.Text);
        }

        if (this.InitializerKoto != default)
        {// "= 1"
            writer.Write(" = ");
            this.InitializerKoto.UnparseTo(writer);
        }
    }

    public override (string Text, Koto[]? Children) Dump()
    {
        return ($"{this.GetType().Name}()", default);
    }
}
