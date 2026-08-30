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
[TinyhandObject]
public sealed partial class RangeKoto : Koto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Range;

    /// <summary>Gets the start endpoint, or <see langword="null"/> for the beginning.</summary>
    [Key(1)]
    public Koto? Start { get; private set; }

    /// <summary>Gets the end endpoint, or <see langword="null"/> for the end of the indexed value.</summary>
    [Key(2)]
    public Koto? End { get; private set; }

    /// <summary>Gets a value indicating whether the end endpoint is included.</summary>
    [Key(3)]
    public bool IsInclusive { get; private set; }

    /// <summary>Gets a value indicating whether neither endpoint was specified.</summary>
    [IgnoreMember]
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
                end?.Span.End ?? operatorRange.End))
    {
        this.Start = start;
        this.End = end;
        this.IsInclusive = isInclusive;

        if (start is not null)
        {
            start.Parent = this;
        }

        if (end is not null)
        {
            end.Parent = this;
        }
    }

    /// <inheritdoc/>
    public override void Bind(Compilation compilation)
    {
        this.Start?.Bind(compilation);
        this.End?.Bind(compilation);
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.Start?.WriteTo(ref builder);
        builder.Append(this.IsInclusive ? "..=" : "..");
        this.End?.WriteTo(ref builder);
    }

    internal override bool ReplaceChild(Koto oldKoto, Koto newKoto)
    {
        if (this.Start == oldKoto)
        {
            this.Start = newKoto;
            newKoto.Parent = this;
            oldKoto.Parent = default;
            return true;
        }

        if (this.End == oldKoto)
        {
            this.End = newKoto;
            newKoto.Parent = this;
            oldKoto.Parent = default;
            return true;
        }

        return false;
    }

    internal override void RestoreAfterDeserialization(CodeContext codeContext, Koto? parent)
    {
        base.RestoreAfterDeserialization(codeContext, parent);
        this.Start?.RestoreAfterDeserialization(codeContext, this);
        this.End?.RestoreAfterDeserialization(codeContext, this);
    }

    [TinyhandOnDeserialized]
    private void OnDeserialized()
    {
        if (this.Start is not null)
        {
            this.Start.Parent = this;
        }

        if (this.End is not null)
        {
            this.End.Parent = this;
        }
    }
}
