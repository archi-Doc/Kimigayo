// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Represents a source-level Property declaration.</summary>
[TinyhandObject]
public sealed partial class PropertyKoto : DeclarationKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Property;

    /// <summary>Gets the declaration modifiers.</summary>
    [Key(1)]
    public ModifierKind Modifier { get; private set; }

    /// <summary>Gets the Property binding kind.</summary>
    [Key(2)]
    public VariableKind VariableKind { get; private set; }

    /// <summary>Gets the declared name.</summary>
    [Key(3)]
    public IdentifierNameKoto NameKoto { get; private set; }

    /// <summary>Gets the declared Type, if specified.</summary>
    [Key(4)]
    public Koto? TypeKoto { get; private set; }

    /// <summary>Gets the initializer expression, if present.</summary>
    [Key(5)]
    public Koto? InitializerKoto { get; private set; }

    [Key(6)]
    private List<PropertyAccessorKoto> accessors = [];

    /// <summary>Gets a value indicating whether the accessors use the inline <c>has</c> form.</summary>
    [Key(7)]
    public bool HasInlineAccessors { get; private set; }

    /// <summary>Gets the explicit accessors in source order.</summary>
    [IgnoreMember]
    public IReadOnlyList<PropertyAccessorKoto> Accessors => this.accessors;

    /// <summary>Gets the source keyword for the binding kind.</summary>
    public string VariableText => this.VariableKind == VariableKind.Var ? Constants.VarKeyword : Constants.LetKeyword;

    /// <summary>Initializes a new instance of the <see cref="PropertyKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="context">The declaration attributes and modifiers.</param>
    /// <param name="token">The <c>let</c> or <c>var</c> token.</param>
    /// <param name="nameKoto">The declared name.</param>
    /// <param name="typeKoto">The declared Type, if specified.</param>
    /// <param name="initializerKoto">The initializer expression, if present.</param>
    /// <param name="hasInlineAccessors">Whether the declaration uses the inline <c>has</c> form.</param>
    public PropertyKoto(
        ref TokenReader reader,
        TokenContext context,
        ref Token token,
        IdentifierNameKoto nameKoto,
        Koto? typeKoto,
        Koto? initializerKoto,
        bool hasInlineAccessors)
        : base(
            ref reader,
            SourceSpan.FromBounds(
                token.Span.Start,
                Math.Max(
                    nameKoto.Span.End,
                    Math.Max(typeKoto?.Span.End ?? 0, initializerKoto?.Span.End ?? 0))))
    {
        this.SetAttributeChain(context.AttributeKoto);
        this.Modifier = context.ModifierKind;
        this.VariableKind = token.Kind == TokenKind.Let ? VariableKind.Let : VariableKind.Var;
        this.NameKoto = nameKoto;
        this.TypeKoto = typeKoto;
        this.InitializerKoto = initializerKoto;
        this.HasInlineAccessors = hasInlineAccessors;

        nameKoto.Parent = this;
        if (typeKoto is not null)
        {
            typeKoto.Parent = this;
        }

        if (initializerKoto is not null)
        {
            initializerKoto.Parent = this;
        }
    }

    /// <summary>Gets the explicit accessor of the requested kind, if present.</summary>
    /// <param name="kind">The accessor kind.</param>
    /// <returns>The accessor, or <see langword="null"/> when it is not declared.</returns>
    public PropertyAccessorKoto? GetAccessor(PropertyAccessorKind kind)
        => this.accessors.FirstOrDefault(x => x.AccessorKind == kind);

    /// <inheritdoc/>
    public override void Bind(Compilation compilation)
    {
        this.TypeKoto?.Bind(compilation);
        this.InitializerKoto?.Bind(compilation);
        foreach (var accessor in this.accessors)
        {
            accessor.Bind(compilation);
        }
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
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

        if (this.InitializerKoto is not null)
        {
            builder.Append(" = ");
            this.InitializerKoto.WriteTo(ref builder);
        }

        if (this.HasInlineAccessors)
        {
            builder.AppendSpace();
            builder.Append(Constants.HasKeyword);
            builder.AppendSpace();
            for (var i = 0; i < this.accessors.Count; i++)
            {
                if (i > 0)
                {
                    builder.AppendCommaAndSpace();
                }

                this.accessors[i].WriteTo(ref builder);
            }

            return;
        }

        if (this.accessors.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.IncrementIndent();
        for (var i = 0; i < this.accessors.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
            }

            this.accessors[i].WriteTo(ref builder);
        }

        builder.DecrementIndent();
    }

    /// <summary>Adds an explicit accessor unless that kind is already present.</summary>
    /// <param name="accessor">The accessor to add.</param>
    /// <returns><see langword="true"/> when the accessor was added.</returns>
    internal bool TryAddAccessor(PropertyAccessorKoto accessor)
    {
        if (this.GetAccessor(accessor.AccessorKind) is not null)
        {
            return false;
        }

        this.accessors.Add(accessor);
        accessor.Parent = this;
        this.Span = SourceSpan.FromBounds(this.Span.Start, Math.Max(this.Span.End, accessor.Span.End));
        return true;
    }

    /// <summary>Extends the Property span through its accessor block.</summary>
    /// <param name="end">The end of the accessor block.</param>
    internal void CompleteSpan(int end)
        => this.Span = SourceSpan.FromBounds(this.Span.Start, Math.Max(this.Span.End, end));

    protected override IEnumerable<Koto> GetChildNodes()
    {
        yield return this.NameKoto;
        if (this.TypeKoto is not null)
        {
            yield return this.TypeKoto;
        }

        if (this.InitializerKoto is not null)
        {
            yield return this.InitializerKoto;
        }

        foreach (var accessor in this.accessors)
        {
            yield return accessor;
        }
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (this.NameKoto == oldKoto && newKoto is IdentifierNameKoto name)
        {
            this.NameKoto = name;
            return true;
        }

        if (this.TypeKoto == oldKoto)
        {
            this.TypeKoto = newKoto;
            return true;
        }

        if (this.InitializerKoto == oldKoto)
        {
            this.InitializerKoto = newKoto;
            return true;
        }

        if (oldKoto is PropertyAccessorKoto oldAccessor && newKoto is PropertyAccessorKoto newAccessor)
        {
            var index = this.accessors.IndexOf(oldAccessor);
            if (index >= 0)
            {
                this.accessors[index] = newAccessor;
                return true;
            }
        }

        return false;
    }
}
