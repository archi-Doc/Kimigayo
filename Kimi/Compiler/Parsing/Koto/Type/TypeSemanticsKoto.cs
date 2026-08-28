// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public partial class TypeSemanticsKoto : Koto
{// semantics/Type from origin
    public override KotoKind Akind => KotoKind.TypeSemantics;

    [Key(1)]
    public SemanticsKind SemanticsKind { get; private set; }

    [Key(2)]
    public string? SemanticsParameter { get; private set; }

    [Key(3)]
    private TokenKind typeToken;

    [Key(4)]
    private string? typeName;

    /// <summary>
    /// Gets the type to which the semantics applies when it is a compound type.
    /// </summary>
    [Key(5)]
    public Koto? Type { get; private set; }

    public string Identifier
        => this.Type is TypeSemanticsKoto simpleType
            ? simpleType.Identifier
            : this.typeToken.IsPrimitiveType()
            ? this.typeToken.ToText()
            : this.typeName ?? string.Empty;

    internal TypeSemanticsKoto(
        ref TokenReader reader,
        Token typeToken)
        : base(ref reader, typeToken.Span)
    {
        this.typeToken = typeToken.Kind;
        this.SemanticsKind = SemanticsKind.Owner;

        if (!this.typeToken.IsPrimitiveType())
        {
            this.typeName = reader.GetSpan(typeToken).ToString();
        }
    }

    internal TypeSemanticsKoto(
        ref TokenReader reader,
        SourceSpan range,
        Koto type,
        SemanticsKind semanticsKind,
        string? semanticsParameter)
        : base(ref reader, range)
    {
        this.SemanticsKind = semanticsKind;
        this.SemanticsParameter = semanticsParameter;
        this.Type = type;
        type.Parent = this;
    }

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        if (this.AttributeChain is not null)
        {
            Parser.UnparseAttribute(this.AttributeChain, ref builder, KotoWriteOptions.AppendSpace);
        }

        if (this.Type is not null)
        {
            if (this.SemanticsKind == SemanticsKind.Parameter)
            {
                builder.Append(this.SemanticsParameter);
            }
            else
            {
                builder.Append(this.SemanticsKind.ToText());
            }

            builder.Append(Constants.SlashChar);
            this.Type.WriteTo(ref builder);
            return;
        }

        builder.Append(this.Identifier);
    }

    internal override void RestoreAfterDeserialization(CodeContext codeContext, Koto? parent)
    {
        base.RestoreAfterDeserialization(codeContext, parent);
        this.Type?.RestoreAfterDeserialization(codeContext, this);
    }
}
