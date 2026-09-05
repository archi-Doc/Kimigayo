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
    public List<Koto> Elements { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="TupleTypeKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="elements">The tuple element types.</param>
    public TupleTypeKoto(ref TokenReader reader, SourceSpan range, List<Koto> elements)
        : base(ref reader, range)
    {
        this.Elements = elements;
        this.Adopt(elements);
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append('(');
        for (var i = 0; i < this.Elements.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendCommaAndSpace();
            }

            this.Elements[i].WriteTo(ref builder);
        }

        builder.Append(')');
    }

    protected override IEnumerable<Koto> GetChildNodes()
        => this.Elements;

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
        => ReplaceInList(this.Elements, oldKoto, newKoto);
}
