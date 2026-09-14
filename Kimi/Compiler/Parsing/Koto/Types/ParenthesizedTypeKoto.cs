// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Groups a type without adding a tuple element or a semantics layer.</summary>
public sealed class ParenthesizedTypeKoto : TypeKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.ParenthesizedType;

    /// <summary>Gets the complete type enclosed by the parentheses.</summary>
    public Koto Type { get; private set; }

    /// <inheritdoc/>
    public override SemanticsKind SemanticsKind => (this.Type as TypeKoto)?.SemanticsKind ?? SemanticsKind.Owner;

    /// <inheritdoc/>
    public override string? SemanticsParameter => (this.Type as TypeKoto)?.SemanticsParameter;

    /// <inheritdoc/>
    public override string Identifier => (this.Type as TypeKoto)?.Identifier ?? string.Empty;

    /// <inheritdoc/>
    public override string? OriginName => (this.Type as TypeKoto)?.OriginName;

    /// <summary>Initializes a new instance of the <see cref="ParenthesizedTypeKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The span including both parentheses.</param>
    /// <param name="type">The enclosed type.</param>
    internal ParenthesizedTypeKoto(ref TokenReader reader, SourceSpan range, Koto type)
        : base(ref reader, range)
    {
        this.Type = type;
        this.Adopt(type);
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteAttributeChainTo(ref builder, KotoWriteOptions.AppendSpace);
        builder.Append('(');
        this.Type.WriteTo(ref builder);
        builder.Append(')');
    }

    protected override void VisitChildrenCore(KotoVisitor visitor)
    {
        visitor.Visit(this.Type);
    }

    protected override IEnumerable<Koto> GetChildNodes() => [this.Type];

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
