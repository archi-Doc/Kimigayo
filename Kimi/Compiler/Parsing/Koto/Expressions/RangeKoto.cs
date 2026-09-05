// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a range expression whose endpoints are nonnegative isize values or
/// <see cref="FromEndIndexKoto"/> values.
/// </summary>
/// <remarks>
/// An omitted start means the beginning and an omitted end means the end of the indexed value.
/// Exclusive ranges use <c>..</c>; inclusive ranges use <c>..=</c> and require an end expression.
/// </remarks>
public sealed class RangeKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Range;

    /// <summary>Gets the start endpoint, or <see langword="null"/> for the beginning.</summary>
    public Koto? Start { get; private set; }

    /// <summary>Gets the end endpoint, or <see langword="null"/> for the end of the indexed value.</summary>
    public Koto? End { get; private set; }

    /// <summary>Gets a value indicating whether the end endpoint is included.</summary>
    public bool IsInclusive { get; private set; }

    /// <summary>Gets a value indicating whether neither endpoint was specified.</summary>
    public bool IsFull => this.Start is null && this.End is null;

    /// <summary>Initializes a new instance of the <see cref="RangeKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="operatorRange">The <c>..</c> or <c>..=</c> token span.</param>
    /// <param name="start">The optional start endpoint.</param>
    /// <param name="end">The optional end endpoint.</param>
    /// <param name="isInclusive">Whether the end endpoint is included.</param>
    public RangeKoto(ref TokenReader reader, SourceSpan operatorRange, Koto? start, Koto? end, bool isInclusive)
        : base(
            ref reader,
            SourceSpan.FromBounds(
                start?.Span.Start ?? operatorRange.Start,
                Math.Max(operatorRange.End, end?.Span.End ?? 0)))
    {
        this.Start = start;
        this.End = end;
        this.IsInclusive = isInclusive;
        this.Adopt(start);
        this.Adopt(end);
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.Start?.WriteTo(ref builder);
        builder.Append(this.IsInclusive ? "..=" : "..");
        this.End?.WriteTo(ref builder);
    }

    protected override IEnumerable<Koto> GetChildNodes()
    {
        if (this.Start is not null)
        {
            yield return this.Start;
        }

        if (this.End is not null)
        {
            yield return this.End;
        }
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (this.Start == oldKoto)
        {
            this.Start = newKoto;
            return true;
        }

        if (this.End == oldKoto)
        {
            this.End = newKoto;
            return true;
        }

        return false;
    }
}
