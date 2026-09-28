// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a Place result <c>place ref/T</c> or <c>place uniq/T</c> in result position (SPEC §7.1.1).
/// </summary>
/// <remarks>
/// The enclosed Type is the reference that <c>@ref</c> or <c>@uniq</c> on the published Place produces; its outer
/// <c>during</c> is the Place result's Origin and its referent is the stored Type. A Place result is a result
/// category, never a value Type.
/// </remarks>
public sealed class PlaceResultKoto : TypeKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.PlaceResult;

    /// <summary>Gets the reference Type written after <c>place</c>, including its outer Origin.</summary>
    public Koto Type { get; private set; }

    /// <summary>Gets a value indicating whether the Place is published exclusively (<c>place uniq/T</c>).</summary>
    public bool IsExclusive => (this.Type as TypeKoto)?.SemanticsKind == SemanticsKind.Uniq;

    /// <inheritdoc/>
    public override SemanticsKind SemanticsKind => (this.Type as TypeKoto)?.SemanticsKind ?? SemanticsKind.Owner;

    /// <inheritdoc/>
    public override string Identifier => (this.Type as TypeKoto)?.Identifier ?? string.Empty;

    /// <inheritdoc/>
    public override string? OriginName => (this.Type as TypeKoto)?.OriginName;

    /// <summary>Initializes a new instance of the <see cref="PlaceResultKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The span from <c>place</c> through the Type.</param>
    /// <param name="type">The reference Type after <c>place</c>.</param>
    internal PlaceResultKoto(ref TokenReader reader, SourceSpan range, Koto type)
        : base(ref reader, range)
    {
        this.Type = type;
        this.Adopt(type);
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append("place ");
        this.Type.WriteTo(ref builder);
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
