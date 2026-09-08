// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a tuple type.
/// </summary>
public sealed class TupleTypeKoto : TypeKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.TupleType;

    /// <summary>Gets the tuple element types.</summary>
    public List<Koto> Elements => this.elements is List<Koto> list ? list : (List<Koto>)(this.elements = new List<Koto>(this.elements));

    private IReadOnlyList<Koto> elements;

    /// <summary>Gets element types without materializing a mutable list.</summary>
    public IReadOnlyList<Koto> ElementNodes => this.elements;

    /// <summary>Initializes a new instance of the <see cref="TupleTypeKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="elements">The tuple element types.</param>
    public TupleTypeKoto(ref TokenReader reader, SourceSpan range, IReadOnlyList<Koto> elements)
        : base(ref reader, range)
    {
        this.elements = elements;
        this.Adopt(elements);
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append('(');
        for (var i = 0; i < this.elements.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendCommaAndSpace();
            }

            this.elements[i].WriteTo(ref builder);
        }

        if (this.elements.Count == 1)
        {
            builder.Append(',');
        }

        builder.Append(')');
    }

    protected override IEnumerable<Koto> GetChildNodes()
        => this.elements;

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
        => ReplaceInList(this.elements, oldKoto, newKoto);
}
