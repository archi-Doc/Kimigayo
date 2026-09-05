// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Represents escaped text alternating with embedded expressions.</summary>
[TinyhandObject]
public sealed partial class InterpolatedStringKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.InterpolatedString;

    /// <summary>Gets the text segments, including the leading and trailing segments.</summary>
    [Key(1)]
    public StringLiteralKoto[] Segments { get; private set; }

    /// <summary>Gets the embedded expressions in evaluation order.</summary>
    [Key(2)]
    public Koto[] Expressions { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="InterpolatedStringKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="span">The complete string span.</param>
    /// <param name="segments">The text segments.</param>
    /// <param name="expressions">The embedded expressions.</param>
    public InterpolatedStringKoto(ref TokenReader reader, SourceSpan span, StringLiteralKoto[] segments, Koto[] expressions)
        : base(ref reader, span)
    {
        this.Segments = segments;
        this.Expressions = expressions;
        this.Adopt(segments);
        this.Adopt(expressions);
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteAttributeChainTo(ref builder, KotoWriteOptions.AppendSpace);
        builder.Append('"');
        for (var i = 0; i < this.Segments.Length; i++)
        {
            this.Segments[i].WriteContentTo(ref builder);
            if (i < this.Expressions.Length)
            {
                builder.AppendVerbatim("\\(");
                this.Expressions[i].WriteTo(ref builder);
                builder.AppendVerbatim(")");
            }
        }

        builder.AppendVerbatim("\"");
    }

    protected override IEnumerable<Koto> GetChildNodes()
    {
        for (var i = 0; i < this.Segments.Length; i++)
        {
            yield return this.Segments[i];
            if (i < this.Expressions.Length)
            {
                yield return this.Expressions[i];
            }
        }
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
        => (newKoto is StringLiteralKoto && ReplaceInList(this.Segments, oldKoto, newKoto)) ||
            ReplaceInList(this.Expressions, oldKoto, newKoto);
}
