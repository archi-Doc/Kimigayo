// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a tuple type.
/// </summary>
[TinyhandObject]
public sealed partial class TupleTypeKoto : TypeKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.TupleType;

    /// <summary>Gets the tuple element types.</summary>
    [Key(1)]
    public List<Koto> Elements { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="TupleTypeKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="elements">The tuple element types.</param>
    public TupleTypeKoto(ref TokenReader reader, SourceSpan range, List<Koto> elements)
        : base(ref reader, range)
    {
        this.Elements = elements;
        foreach (var element in elements)
        {
            element.Parent = this;
        }
    }

    /// <inheritdoc/>
    public override void Bind(Compilation compilation)
    {
        foreach (var element in this.Elements)
        {
            element.Bind(compilation);
        }
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
    {
        var index = this.Elements.IndexOf(oldKoto);
        if (index < 0)
        {
            return false;
        }

        this.Elements[index] = newKoto;
        return true;
    }
}
