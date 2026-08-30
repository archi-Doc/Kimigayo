// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a type together with ownership semantics and an optional origin.
/// </summary>
[TinyhandObject]
public sealed partial class TypeSemanticsKoto : TypeKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.TypeSemantics;

    /// <summary>Gets the ownership semantics.</summary>
    [Key(1)]
    private SemanticsKind semanticsKind;

    /// <inheritdoc/>
    [IgnoreMember]
    public override SemanticsKind SemanticsKind => this.semanticsKind;

    /// <summary>Gets the custom semantics parameter, if present.</summary>
    [Key(2)]
    private string? semanticsParameter;

    /// <inheritdoc/>
    [IgnoreMember]
    public override string? SemanticsParameter => this.semanticsParameter;

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
    private string? originName;

    /// <inheritdoc/>
    [IgnoreMember]
    public override string? OriginName => this.originName;

    [Key(7)]
    private bool isTransparentWrapper;

    /// <summary>Gets the underlying type identifier.</summary>
    [IgnoreMember]
    public override string Identifier
        => this.Type is TypeSemanticsKoto simpleType
            ? simpleType.Identifier
            : this.coreTypeToken.IsPrimitiveType()
            ? this.coreTypeToken.ToText()
            : this.coreTypeName ?? string.Empty;

    internal bool IsTransparentWrapper => this.isTransparentWrapper;

    internal TypeSemanticsKoto(
        ref TokenReader reader,
        Token typeToken)
        : base(ref reader, typeToken.Span)
    {
        this.coreTypeToken = typeToken.Kind;
        this.semanticsKind = SemanticsKind.Owner;

        if (!this.coreTypeToken.IsPrimitiveType())
        {
            this.coreTypeName = reader.GetSpan(typeToken).ToString();
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
        this.semanticsKind = semanticsKind;
        this.semanticsParameter = semanticsParameter;
        this.Type = type;
        type.Parent = this;
    }

    internal TypeSemanticsKoto(
        ref TokenReader reader,
        SourceSpan range,
        Koto type,
        string originName)
        : base(ref reader, range)
    {
        this.semanticsKind = SemanticsKind.Owner;
        this.Type = type;
        this.originName = originName;
        this.isTransparentWrapper = true;
        type.Parent = this;
    }

    internal TypeSemanticsKoto(ref TokenReader reader, SourceSpan range, Koto type)
        : base(ref reader, range)
    {
        this.semanticsKind = SemanticsKind.Owner;
        this.Type = type;
        this.isTransparentWrapper = true;
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
            if (!this.isTransparentWrapper)
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

        if (this.originName is not null)
        {
            builder.AppendSpace();
            builder.Append(Constants.FromKeyword);
            builder.AppendSpace();
            builder.Append(this.originName);
        }
    }

    /// <inheritdoc/>
    public override void Bind(Compilation compilation)
        => this.Type?.Bind(compilation);

    internal void SetOrigin(string originName, int end)
    {
        this.originName = originName;
        this.Span = SourceSpan.FromBounds(this.Span.Start, end);
    }

    protected override IEnumerable<Koto> GetChildNodes()
    {
        if (this.Type is not null)
        {
            yield return this.Type;
        }
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (this.Type != oldKoto)
        {
            return false;
        }

        this.Type = newKoto;
        return true;
    }
}
