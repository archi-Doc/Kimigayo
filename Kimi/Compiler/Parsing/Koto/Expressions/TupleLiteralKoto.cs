// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Represents an tuple literal expression.</summary>
[TinyhandObject]
public sealed partial class TupleLiteralKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.TupleLiteral;

    /// <summary>Gets the tuple elements in source order.</summary>
    [Key(1)]
    public List<Koto> Elements { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="TupleLiteralKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete literal span.</param>
    /// <param name="elements">The tuple elements.</param>
    public TupleLiteralKoto(ref TokenReader reader, SourceSpan range, List<Koto> elements)
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

        if (this.Elements.Count == 1)
        {
            builder.Append(',');
        }

        builder.Append(')');
    }

    protected override IEnumerable<Koto> GetChildNodes()
        => this.Elements;

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
        => ReplaceInList(this.Elements, oldKoto, newKoto);
}
