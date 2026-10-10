// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Represents escaped text alternating with embedded expressions.</summary>
public sealed class InterpolatedStringKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.InterpolatedString;

    /// <summary>Gets the text segments, including the leading and trailing segments.</summary>
    public StringLiteralKoto[] Segments { get; private set; }

    /// <summary>Gets the embedded expressions in evaluation order.</summary>
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
        this.WriteContentTo(ref builder);
        builder.AppendVerbatim("\"");
    }

    /// <summary>Writes the segments and embedded expressions as written, without the delimiters.</summary>
    /// <param name="builder">The destination builder.</param>
    internal void WriteContentTo(ref IndentedStringBuilder builder)
    {
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
    }

    protected override void ForEachChildSlot(ref ChildSlots slots)
    {
        for (var i = 0; i < this.Segments.Length; i++)
        {
            this.Segments[i] = slots.Slot(this.Segments[i]);
            if (i < this.Expressions.Length)
            {
                this.Expressions[i] = slots.Slot(this.Expressions[i]);
            }
        }
    }
}
