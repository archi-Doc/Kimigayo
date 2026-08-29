// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a member-access expression.
/// </summary>
[TinyhandObject]
public partial class MemberAccessKoto : BinaryKoto
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
    public override string ToString()
        => $"{this.Left.ToString()}{Constants.DotChar}{this.Accessor.ToString()}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.Left.WriteTo(ref builder);
        builder.Append(Constants.DotChar);
        this.Right.WriteTo(ref builder);
    }
}

/// <summary>
/// Represents an index expression.
/// </summary>
[TinyhandObject]
public partial class IndexKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Index;

    /// <summary>Gets the index expression.</summary>
    public Koto Index => this.Right;

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
    public override string ToString()
        => $"{this.Left.ToString()}[{this.Index.ToString()}]";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.Left.WriteTo(ref builder);
        builder.Append("[");
        this.Right.WriteTo(ref builder);
        builder.Append("]");
    }
}

/// <summary>
/// Represents a multiplication expression.
/// </summary>
[TinyhandObject]
public partial class AsteriskKoto : BinaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Left} * {this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " * ");
    }
}

/// <summary>
/// Represents a conversion expression.
/// </summary>
[TinyhandObject]
public partial class ConversionKoto : BinaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Left}@{this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, "@");
    }
}

/// <summary>
/// Represents a division expression.
/// </summary>
[TinyhandObject]
public partial class SlashKoto : BinaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Left} / {this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " / ");
    }
}

/// <summary>
/// Represents a remainder expression.
/// </summary>
[TinyhandObject]
public partial class PercentKoto : BinaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Left} % {this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " % ");
    }
}

/// <summary>
/// Represents an addition expression.
/// </summary>
[TinyhandObject]
public partial class PlusKoto : BinaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Left} + {this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " + ");
    }
}

/// <summary>
/// Represents a subtraction expression.
/// </summary>
[TinyhandObject]
public partial class MinusKoto : BinaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Left} - {this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " - ");
    }
}

/// <summary>
/// Represents a left-shift expression.
/// </summary>
[TinyhandObject]
public partial class LessThanLessThanKoto : BinaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Left} << {this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " << ");
    }
}

/// <summary>
/// Represents a right-shift expression.
/// </summary>
[TinyhandObject]
public partial class GreaterThanGreaterThanKoto : BinaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Left} >> {this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " >> ");
    }
}

/// <summary>
/// Represents a less-than expression.
/// </summary>
[TinyhandObject]
public partial class LessThanKoto : BinaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Left} < {this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " < ");
    }
}

/// <summary>
/// Represents a less-than-or-equal expression.
/// </summary>
[TinyhandObject]
public partial class LessThanEqualsKoto : BinaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Left} <= {this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " <= ");
    }
}

/// <summary>
/// Represents a greater-than expression.
/// </summary>
[TinyhandObject]
public partial class GreaterThanKoto : BinaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Left} > {this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " > ");
    }
}

/// <summary>
/// Represents a greater-than-or-equal expression.
/// </summary>
[TinyhandObject]
public partial class GreaterThanEqualsKoto : BinaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Left} >= {this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " >= ");
    }
}

/// <summary>
/// Represents an <c>as</c> expression.
/// </summary>
[TinyhandObject]
public partial class AsKoto : BinaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Left} {Constants.AsKeyword} {this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " " + Constants.AsKeyword + " ");
    }
}

/// <summary>
/// Represents an <c>is</c> expression.
/// </summary>
[TinyhandObject]
public partial class IsKoto : BinaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Is;

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
    public override string ToString()
        => $"{this.Left} {Constants.IsKeyword} {this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " " + Constants.IsKeyword + " ");
    }
}

/// <summary>
/// Represents an equality expression.
/// </summary>
[TinyhandObject]
public partial class EqualsEqualsKoto : BinaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Left} == {this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " == ");
    }
}

/// <summary>
/// Represents an inequality expression.
/// </summary>
[TinyhandObject]
public partial class ExclamationEqualsKoto : BinaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Left} != {this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " != ");
    }
}

/// <summary>
/// Represents a bitwise-and expression.
/// </summary>
[TinyhandObject]
public partial class AmpersandKoto : BinaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Left} & {this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " & ");
    }
}

/// <summary>
/// Represents a bitwise-exclusive-or expression.
/// </summary>
[TinyhandObject]
public partial class CaretKoto : BinaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Left} ^ {this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " ^ ");
    }
}

/// <summary>
/// Represents a bitwise-or expression.
/// </summary>
[TinyhandObject]
public partial class BarKoto : BinaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Left} | {this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " | ");
    }
}

/// <summary>
/// Represents a logical-and expression.
/// </summary>
[TinyhandObject]
public partial class AndKoto : BinaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Left} {Constants.AndKeyword} {this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " " + Constants.AndKeyword + " ");
    }
}

/// <summary>
/// Represents a logical-or expression.
/// </summary>
[TinyhandObject]
public partial class OrKoto : BinaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Left} {Constants.OrKeyword} {this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " " + Constants.OrKeyword + " ");
    }
}

/// <summary>
/// Represents an assignment expression.
/// </summary>
[TinyhandObject]
public partial class EqualsKoto : BinaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Left} = {this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " = ");
    }
}

/// <summary>
/// Represents an addition-assignment expression.
/// </summary>
[TinyhandObject]
public partial class PlusEqualsKoto : BinaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Left} += {this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " += ");
    }
}

/// <summary>
/// Represents a subtraction-assignment expression.
/// </summary>
[TinyhandObject]
public partial class MinusEqualsKoto : BinaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Left} -= {this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " -= ");
    }
}

/// <summary>
/// Represents a multiplication-assignment expression.
/// </summary>
[TinyhandObject]
public partial class AsteriskEqualsKoto : BinaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Left} *= {this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " *= ");
    }
}

/// <summary>
/// Represents a division-assignment expression.
/// </summary>
[TinyhandObject]
public partial class SlashEqualsKoto : BinaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Left} /= {this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " /= ");
    }
}

/// <summary>
/// Represents a remainder-assignment expression.
/// </summary>
[TinyhandObject]
public partial class PercentEqualsKoto : BinaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Left} %= {this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " %= ");
    }
}

/// <summary>
/// Represents a bitwise-and-assignment expression.
/// </summary>
[TinyhandObject]
public partial class AmpersandEqualsKoto : BinaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Left} &= {this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " &= ");
    }
}

/// <summary>
/// Represents a bitwise-exclusive-or-assignment expression.
/// </summary>
[TinyhandObject]
public partial class CaretEqualsKoto : BinaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Left} ^= {this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " ^= ");
    }
}

/// <summary>
/// Represents a bitwise-or-assignment expression.
/// </summary>
[TinyhandObject]
public partial class BarEqualsKoto : BinaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Left} |= {this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " |= ");
    }
}

/// <summary>
/// Represents a left-shift-assignment expression.
/// </summary>
[TinyhandObject]
public partial class LessThanLessThanEqualsKoto : BinaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Left} <<= {this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " <<= ");
    }
}

/// <summary>
/// Represents a right-shift-assignment expression.
/// </summary>
[TinyhandObject]
public partial class GreaterThanGreaterThanEqualsKoto : BinaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Left} >>= {this.Right}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteBinaryKoto(ref builder, " >>= ");
    }
}

/// <summary>
/// Provides the base representation of a binary expression.
/// </summary>
public abstract partial class BinaryKoto : Koto
{
    /// <summary>Gets the left operand.</summary>
    [Key(1)]
    public Koto Left { get; private set; }

    /// <summary>Gets the right operand.</summary>
    [Key(2)]
    public Koto Right { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="BinaryKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public BinaryKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range)
    {
        this.Left = left;
        this.Right = right;
        this.Left.Parent = this;
        this.Right.Parent = this;
    }

    internal BinaryKoto(CodeContext codeContext)
        : base(codeContext, default)
    {
        this.Left = default!;
        this.Right = default!;
    }

    /// <inheritdoc/>
    public override void Bind(Compilation compilation)
    {
        this.Left.Bind(compilation);
        this.Right.Bind(compilation);
    }

    /// <inheritdoc/>
    public override string ToString()
        => $"BinaryKoto: {this.Right.ToString()}";

    internal override bool ReplaceChild(Koto oldKoto, Koto newKoto)
    {
        if (oldKoto == this.Left)
        {
            this.Left = newKoto;
            return true;
        }
        else if (oldKoto == this.Right)
        {
            this.Right = newKoto;
            return true;
        }

        return false;
    }

    internal override void RestoreAfterDeserialization(CodeContext codeContext, Koto? parent)
    {
        base.RestoreAfterDeserialization(codeContext, parent);
        this.Left.RestoreAfterDeserialization(codeContext, this);
        this.Right.RestoreAfterDeserialization(codeContext, this);
    }

    protected void WriteBinaryKoto(ref IndentedStringBuilder builder, string infix)
    {
        this.Left.WriteTo(ref builder);

        if (this.AttributeChain is not null)
        {
            Parser.UnparseAttribute(this.AttributeChain, ref builder, KotoWriteOptions.None);
        }

        builder.Append(infix);

        this.Right.WriteTo(ref builder);
    }

    [TinyhandOnDeserialized]
    protected void OnDeserialized()
    {
        this.Left.Parent = this;
        this.Left.CodeContext = this.CodeContext;
        this.Right.Parent = this;
        this.Right.CodeContext = this.CodeContext;
    }
}
