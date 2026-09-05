// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

#pragma warning disable SA1402 // File may only contain a single type
#pragma warning disable SA1649 // File name should match first type name

/// <summary>
/// Provides the base representation of a binary expression.
/// </summary>
/// <remarks>
/// Concrete operators only contribute their <see cref="KotoKind"/>; the infix spelling is looked up
/// from a table so every operator shares the same writing and child-management code.
/// </remarks>
public abstract class BinaryKoto : ExpressionKoto
{
    private static readonly string[] InfixTexts = new string[MaxKind];

    static BinaryKoto()
    {
        Set(KotoKind.MemberAccess, ".");
        Set(KotoKind.Conversion, "@");
        Set(KotoKind.Asterisk, " * ");
        Set(KotoKind.Slash, " / ");
        Set(KotoKind.Percent, " % ");
        Set(KotoKind.Plus, " + ");
        Set(KotoKind.Minus, " - ");
        Set(KotoKind.LessThanLessThan, " << ");
        Set(KotoKind.GreaterThanGreaterThan, " >> ");
        Set(KotoKind.LessThan, " < ");
        Set(KotoKind.LessThanEquals, " <= ");
        Set(KotoKind.GreaterThan, " > ");
        Set(KotoKind.GreaterThanEquals, " >= ");
        Set(KotoKind.As, " " + Constants.AsKeyword + " ");
        Set(KotoKind.Is, " " + Constants.IsKeyword + " ");
        Set(KotoKind.EqualsEquals, " == ");
        Set(KotoKind.ExclamationEquals, " != ");
        Set(KotoKind.Ampersand, " & ");
        Set(KotoKind.Caret, " ^ ");
        Set(KotoKind.Bar, " | ");
        Set(KotoKind.And, " " + Constants.AndKeyword + " ");
        Set(KotoKind.Or, " " + Constants.OrKeyword + " ");
        Set(KotoKind.Equals, " = ");
        Set(KotoKind.PlusEquals, " += ");
        Set(KotoKind.MinusEquals, " -= ");
        Set(KotoKind.AsteriskEquals, " *= ");
        Set(KotoKind.SlashEquals, " /= ");
        Set(KotoKind.PercentEquals, " %= ");
        Set(KotoKind.AmpersandEquals, " &= ");
        Set(KotoKind.CaretEquals, " ^= ");
        Set(KotoKind.BarEquals, " |= ");
        Set(KotoKind.LessThanLessThanEquals, " <<= ");
        Set(KotoKind.GreaterThanGreaterThanEquals, " >>= ");

        static void Set(KotoKind kind, string text)
            => InfixTexts[(int)kind] = text;
    }

    /// <summary>Gets the left operand.</summary>
    public Koto Left { get; private set; }

    /// <summary>Gets the right operand.</summary>
    public Koto Right { get; private set; }

    /// <summary>Gets the infix operator spelling, including surrounding spaces.</summary>
    public string InfixText => InfixTexts[(int)this.Akind] ?? string.Empty;

    /// <summary>Initializes a new instance of the <see cref="BinaryKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    protected BinaryKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range)
    {
        this.Left = left;
        this.Right = right;
        left.Parent = this;
        right.Parent = this;
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.Left.WriteTo(ref builder);
        this.WriteAttributeChainTo(ref builder, KotoWriteOptions.None);
        builder.Append(this.InfixText);
        this.Right.WriteTo(ref builder);
    }

    protected override IEnumerable<Koto> GetChildNodes()
        => [this.Left, this.Right];

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (oldKoto == this.Left)
        {
            this.Left = newKoto;
        }
        else if (oldKoto == this.Right)
        {
            this.Right = newKoto;
        }
        else
        {
            return false;
        }

        return true;
    }
}

/// <summary>Represents a member-access expression.</summary>
public sealed class MemberAccessKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.MemberAccess;

    /// <summary>Gets the accessed member expression.</summary>
    public Koto Accessor => this.Right;

    /// <summary>Initializes a new instance of the <see cref="MemberAccessKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public MemberAccessKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.Left.WriteTo(ref builder);
        builder.Append(Constants.DotChar);
        this.Right.WriteTo(ref builder);
    }
}

/// <summary>Represents an element-index or slice-subscript expression.</summary>
public sealed class IndexKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Index;

    /// <summary>Gets the nonnegative isize index, from-end index, or range expression inside brackets.</summary>
    public Koto Index => this.Right;

    /// <summary>Gets the expression inside brackets.</summary>
    public Koto Argument => this.Right;

    /// <summary>Gets a value indicating whether this subscript produces a slice.</summary>
    public bool IsSlice => this.Right is RangeKoto;

    /// <summary>Initializes a new instance of the <see cref="IndexKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="index">The index expression.</param>
    public IndexKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto index)
        : base(ref reader, range, left, index)
    {
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.Left.WriteTo(ref builder);
        builder.Append(Constants.OpenBracketChar);
        this.Right.WriteTo(ref builder);
        builder.Append(Constants.CloseBracketChar);
    }
}

/// <summary>Represents a multiplication expression.</summary>
public sealed class AsteriskKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Asterisk;

    /// <summary>Initializes a new instance of the <see cref="AsteriskKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public AsteriskKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }
}

/// <summary>Represents a conversion expression.</summary>
public sealed class ConversionKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Conversion;

    /// <summary>Initializes a new instance of the <see cref="ConversionKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public ConversionKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }
}

/// <summary>Represents a division expression.</summary>
public sealed class SlashKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Slash;

    /// <summary>Initializes a new instance of the <see cref="SlashKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public SlashKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }
}

/// <summary>Represents a remainder expression.</summary>
public sealed class PercentKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Percent;

    /// <summary>Initializes a new instance of the <see cref="PercentKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public PercentKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }
}

/// <summary>Represents an addition expression.</summary>
public sealed class PlusKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Plus;

    /// <summary>Initializes a new instance of the <see cref="PlusKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public PlusKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }
}

/// <summary>Represents a subtraction expression.</summary>
public sealed class MinusKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Minus;

    /// <summary>Initializes a new instance of the <see cref="MinusKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public MinusKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }
}

/// <summary>Represents a left-shift expression.</summary>
public sealed class LessThanLessThanKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.LessThanLessThan;

    /// <summary>Initializes a new instance of the <see cref="LessThanLessThanKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public LessThanLessThanKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }
}

/// <summary>Represents a right-shift expression.</summary>
public sealed class GreaterThanGreaterThanKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.GreaterThanGreaterThan;

    /// <summary>Initializes a new instance of the <see cref="GreaterThanGreaterThanKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public GreaterThanGreaterThanKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }
}

/// <summary>Represents a less-than expression.</summary>
public sealed class LessThanKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.LessThan;

    /// <summary>Initializes a new instance of the <see cref="LessThanKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public LessThanKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }
}

/// <summary>Represents a less-than-or-equal expression.</summary>
public sealed class LessThanEqualsKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.LessThanEquals;

    /// <summary>Initializes a new instance of the <see cref="LessThanEqualsKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public LessThanEqualsKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }
}

/// <summary>Represents a greater-than expression.</summary>
public sealed class GreaterThanKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.GreaterThan;

    /// <summary>Initializes a new instance of the <see cref="GreaterThanKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public GreaterThanKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }
}

/// <summary>Represents a greater-than-or-equal expression.</summary>
public sealed class GreaterThanEqualsKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.GreaterThanEquals;

    /// <summary>Initializes a new instance of the <see cref="GreaterThanEqualsKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public GreaterThanEqualsKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }
}

/// <summary>Represents an <c>as</c> expression.</summary>
public sealed class AsKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.As;

    /// <summary>Initializes a new instance of the <see cref="AsKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public AsKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }
}

/// <summary>Represents an <c>is</c> expression.</summary>
public sealed class IsKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Is;

    /// <summary>Gets a value indicating whether this is an associated-type constraint.</summary>
    public bool IsAssociatedConstraint { get; internal set; }

    /// <summary>Initializes a new instance of the <see cref="IsKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public IsKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        if (this.IsAssociatedConstraint)
        {
            builder.Append("associate ");
        }

        base.WriteTo(ref builder);
    }
}

/// <summary>Represents an equality expression.</summary>
public sealed class EqualsEqualsKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.EqualsEquals;

    /// <summary>Initializes a new instance of the <see cref="EqualsEqualsKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public EqualsEqualsKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }
}

/// <summary>Represents an inequality expression.</summary>
public sealed class ExclamationEqualsKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.ExclamationEquals;

    /// <summary>Initializes a new instance of the <see cref="ExclamationEqualsKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public ExclamationEqualsKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }
}

/// <summary>Represents a bitwise-and expression.</summary>
public sealed class AmpersandKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Ampersand;

    /// <summary>Initializes a new instance of the <see cref="AmpersandKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public AmpersandKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }
}

/// <summary>Represents a bitwise-exclusive-or expression.</summary>
public sealed class CaretKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Caret;

    /// <summary>Initializes a new instance of the <see cref="CaretKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public CaretKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }
}

/// <summary>Represents a bitwise-or expression.</summary>
public sealed class BarKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Bar;

    /// <summary>Initializes a new instance of the <see cref="BarKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public BarKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }
}

/// <summary>Represents a logical-and expression.</summary>
public sealed class AndKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.And;

    /// <summary>Initializes a new instance of the <see cref="AndKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public AndKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }
}

/// <summary>Represents a logical-or expression.</summary>
public sealed class OrKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Or;

    /// <summary>Initializes a new instance of the <see cref="OrKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public OrKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }
}

/// <summary>Represents an assignment expression.</summary>
public sealed class EqualsKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Equals;

    /// <summary>Initializes a new instance of the <see cref="EqualsKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public EqualsKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }
}

/// <summary>Represents an addition-assignment expression.</summary>
public sealed class PlusEqualsKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.PlusEquals;

    /// <summary>Initializes a new instance of the <see cref="PlusEqualsKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public PlusEqualsKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }
}

/// <summary>Represents a subtraction-assignment expression.</summary>
public sealed class MinusEqualsKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.MinusEquals;

    /// <summary>Initializes a new instance of the <see cref="MinusEqualsKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public MinusEqualsKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }
}

/// <summary>Represents a multiplication-assignment expression.</summary>
public sealed class AsteriskEqualsKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.AsteriskEquals;

    /// <summary>Initializes a new instance of the <see cref="AsteriskEqualsKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public AsteriskEqualsKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }
}

/// <summary>Represents a division-assignment expression.</summary>
public sealed class SlashEqualsKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.SlashEquals;

    /// <summary>Initializes a new instance of the <see cref="SlashEqualsKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public SlashEqualsKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }
}

/// <summary>Represents a remainder-assignment expression.</summary>
public sealed class PercentEqualsKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.PercentEquals;

    /// <summary>Initializes a new instance of the <see cref="PercentEqualsKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public PercentEqualsKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }
}

/// <summary>Represents a bitwise-and-assignment expression.</summary>
public sealed class AmpersandEqualsKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.AmpersandEquals;

    /// <summary>Initializes a new instance of the <see cref="AmpersandEqualsKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public AmpersandEqualsKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }
}

/// <summary>Represents a bitwise-exclusive-or-assignment expression.</summary>
public sealed class CaretEqualsKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.CaretEquals;

    /// <summary>Initializes a new instance of the <see cref="CaretEqualsKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public CaretEqualsKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }
}

/// <summary>Represents a bitwise-or-assignment expression.</summary>
public sealed class BarEqualsKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.BarEquals;

    /// <summary>Initializes a new instance of the <see cref="BarEqualsKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public BarEqualsKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }
}

/// <summary>Represents a left-shift-assignment expression.</summary>
public sealed class LessThanLessThanEqualsKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.LessThanLessThanEquals;

    /// <summary>Initializes a new instance of the <see cref="LessThanLessThanEqualsKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public LessThanLessThanEqualsKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }
}

/// <summary>Represents a right-shift-assignment expression.</summary>
public sealed class GreaterThanGreaterThanEqualsKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.GreaterThanGreaterThanEquals;

    /// <summary>Initializes a new instance of the <see cref="GreaterThanGreaterThanEqualsKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public GreaterThanGreaterThanEqualsKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, left, right)
    {
    }
}
