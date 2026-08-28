// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler.Lexing;

namespace Kimi.Compiler.Parsing;

public enum VariableKind
{
    Var,
    Let,
}

[TinyhandObject]
public partial class FieldKoto : Koto
{// var x = 1
    public override KotoKind Akind => KotoKind.Field;

    [Key(1)]
    public ModifierKind Modifier { get; private set; }

    [Key(2)]
    public VariableKind VariableKind { get; private set; }

    [Key(3)]
    public IdentifierNameKoto NameKoto { get; private set; }

    [Key(4)]
    public Koto? TypeKoto { get; private set; }

    [Key(5)]
    public Koto? InitializerKoto { get; private set; }

    public string VariableText => this.VariableKind == VariableKind.Var ? Constants.VarKeyword : Constants.LetKeyword;

    public FieldKoto(ref TokenReader reader, ref Token token, IdentifierNameKoto nameKoto, Koto? typeKoto, Koto? initializerKoto)
        : base(ref reader, token.Span)
    {
        this.Modifier = reader.ModifierKind;
        this.VariableKind = token.Kind == TokenKind.Let ? VariableKind.Let : VariableKind.Var;
        this.TypeKoto = typeKoto;
        this.NameKoto = nameKoto;
        this.InitializerKoto = initializerKoto;
    }

    public override void WriteTo(ref IndentedStringBuilder builder)
    {// public let x: i32 = 1
        if (this.AttributeChain is not null)
        {
            Parser.UnparseAttribute(this.AttributeChain, ref builder, KotoWriteOptions.AppendLineFeed);
        }

        this.Modifier.WriteTo(ref builder, KotoWriteOptions.AppendSpace);

        builder.Append(this.VariableText);
        builder.AppendSpace();

        this.NameKoto.WriteTo(ref builder);

        if (this.TypeKoto is not null)
        {
            builder.Append(": ");
            this.TypeKoto.WriteTo(ref builder);
        }

        if (this.InitializerKoto != default)
        {// "= 1"
            builder.Append(" = ");
            this.InitializerKoto.WriteTo(ref builder);
        }
    }

    internal override void RestoreAfterDeserialization(CodeContext codeContext, Koto? parent)
    {
        base.RestoreAfterDeserialization(codeContext, parent);
        this.NameKoto.RestoreAfterDeserialization(codeContext, this);
        this.TypeKoto?.RestoreAfterDeserialization(codeContext, this);
        this.InitializerKoto?.RestoreAfterDeserialization(codeContext, this);
    }
}
