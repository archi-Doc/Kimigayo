// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a type together with ownership semantics and an optional origin.
/// </summary>
[TinyhandObject]
public partial class TypeKoto : Koto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.TypeSemantics;

    /// <summary>Gets the ownership semantics.</summary>
    [Key(1)]
    public SemanticsKind SemanticsKind { get; private set; }

    /// <summary>Gets the custom semantics parameter, if present.</summary>
    [Key(2)]
    public string? SemanticsParameter { get; private set; }

    [Key(3)]
    private TokenKind coreTypeToken;

    [Key(4)]
    private string? coreTypeName;

    /// <summary>
    /// Gets the type to which the semantics applies when it is a compound type.
    /// </summary>
    [Key(5)]
    public Koto? Type { get; private set; }

    /// <summary>
    /// Gets the name of the origin from which this type derives.
    /// </summary>
    [Key(6)]
    public string? OriginName { get; private set; }

    [Key(7)]
    private bool isOriginWrapper;

    /// <summary>Gets the underlying type identifier.</summary>
    public string Identifier
        => this.Type is TypeKoto simpleType
            ? simpleType.Identifier
            : this.coreTypeToken.IsPrimitiveType()
            ? this.coreTypeToken.ToText()
            : this.coreTypeName ?? string.Empty;

    internal TypeKoto(
        ref TokenReader reader,
        Token typeToken)
        : base(ref reader, typeToken.Span)
    {
        this.coreTypeToken = typeToken.Kind;
        this.SemanticsKind = SemanticsKind.Owner;

        if (!this.coreTypeToken.IsPrimitiveType())
        {
            this.coreTypeName = reader.GetSpan(typeToken).ToString();
        }
    }

    internal TypeKoto(
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

    internal TypeKoto(
        ref TokenReader reader,
        SourceSpan range,
        Koto type,
        string originName)
        : base(ref reader, range)
    {
        this.SemanticsKind = SemanticsKind.Owner;
        this.Type = type;
        this.OriginName = originName;
        this.isOriginWrapper = true;
        type.Parent = this;
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        if (this.AttributeChain is not null)
        {
            Parser.UnparseAttribute(this.AttributeChain, ref builder, KotoWriteOptions.AppendSpace);
        }

        if (this.Type is not null)
        {
            if (!this.isOriginWrapper)
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
            }

            this.Type.WriteTo(ref builder);
        }
        else
        {
            builder.Append(this.Identifier);
        }

        if (this.OriginName is not null)
        {
            builder.AppendSpace();
            builder.Append(Constants.FromKeyword);
            builder.AppendSpace();
            builder.Append(this.OriginName);
        }
    }

    /// <inheritdoc/>
    public override void Bind(Compilation compilation)
        => this.Type?.Bind(compilation);

    internal void SetOrigin(string originName, int end)
    {
        this.OriginName = originName;
        this.Span = SourceSpan.FromBounds(this.Span.Start, end);
    }

    internal override bool ReplaceChild(Koto oldKoto, Koto newKoto)
    {
        if (this.Type != oldKoto)
        {
            return false;
        }

        this.Type = newKoto;
        newKoto.Parent = this;
        oldKoto.Parent = default;
        return true;
    }

    internal override void RestoreAfterDeserialization(CodeContext codeContext, Koto? parent)
    {
        base.RestoreAfterDeserialization(codeContext, parent);
        this.Type?.RestoreAfterDeserialization(codeContext, this);
    }
}
