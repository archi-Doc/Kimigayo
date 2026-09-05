// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

#pragma warning disable SA1402 // File may only contain a single type
#pragma warning disable SA1649 // File name should match first type name

/// <summary>
/// Provides the base representation of a unary expression.
/// </summary>
/// <remarks>
/// Concrete operators only contribute their <see cref="KotoKind"/>; the prefix and postfix
/// spellings are looked up from a table so every operator shares the same writing code.
/// </remarks>
public abstract class UnaryKoto : ExpressionKoto
{
    private static readonly string?[] PrefixTexts = new string?[MaxKind];
    private static readonly string?[] PostfixTexts = new string?[MaxKind];

    static UnaryKoto()
    {
        PrefixTexts[(int)KotoKind.Attribute] = "#";
        PrefixTexts[(int)KotoKind.Macro] = "$";
        PrefixTexts[(int)KotoKind.Unwrap] = "*";
        PrefixTexts[(int)KotoKind.FromEndIndex] = "^";
        PrefixTexts[(int)KotoKind.PrefixPlus] = "+";
        PrefixTexts[(int)KotoKind.PrefixPlusPlus] = "++";
        PrefixTexts[(int)KotoKind.PrefixMinus] = "-";
        PrefixTexts[(int)KotoKind.PrefixMinusMinus] = "--";
        PrefixTexts[(int)KotoKind.Not] = Constants.NotKeyword + " ";
        PrefixTexts[(int)KotoKind.Parenthesized] = "(";
        PostfixTexts[(int)KotoKind.Parenthesized] = ")";
        PostfixTexts[(int)KotoKind.PostfixIncrement] = "++";
        PostfixTexts[(int)KotoKind.PostfixDecrement] = "--";
    }

    /// <summary>Gets or sets the operand.</summary>
    public Koto Operand { get; protected set; }

    /// <summary>Initializes a new instance of the <see cref="UnaryKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="operand">The operand.</param>
    protected UnaryKoto(ref TokenReader reader, SourceSpan range, Koto operand)
        : base(ref reader, range)
    {
        this.Operand = operand;
        operand.Parent = this;
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        if (PrefixTexts[(int)this.Akind] is { } prefix)
        {
            builder.Append(prefix);
        }

        this.Operand.WriteTo(ref builder);

        if (PostfixTexts[(int)this.Akind] is { } postfix)
        {
            builder.Append(postfix);
        }
    }

    protected override IEnumerable<Koto> GetChildNodes()
        => [this.Operand];

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (oldKoto != this.Operand)
        {
            return false;
        }

        this.Operand = newKoto;
        return true;
    }
}

/// <summary>Represents an attribute expression.</summary>
public sealed class AttributeKoto : UnaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Attribute;

    /// <summary>Gets the attribute identifier.</summary>
    public Koto IdentifierKoto
        => this.Operand is InvocationKoto { Method: IdentifierNameKoto identifier } ? identifier : this.Operand;

    /// <summary>Gets the attribute arguments.</summary>
    public List<Koto> Arguments
        => this.Operand is InvocationKoto { Method: IdentifierNameKoto } invocation ? invocation.Arguments : field ??= [];

    /// <summary>Initializes a new instance of the <see cref="AttributeKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="operand">The operand.</param>
    public AttributeKoto(ref TokenReader reader, SourceSpan range, Koto operand)
        : base(ref reader, range, operand)
    {
    }
}

/// <summary>Represents a macro expression.</summary>
public sealed class MacroKoto : UnaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Macro;

    /// <summary>Initializes a new instance of the <see cref="MacroKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="operand">The operand.</param>
    public MacroKoto(ref TokenReader reader, SourceSpan range, Koto operand)
        : base(ref reader, range, operand)
    {
    }
}

/// <summary>Represents an unwrap expression.</summary>
public sealed class UnwrapKoto : UnaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Unwrap;

    /// <summary>Initializes a new instance of the <see cref="UnwrapKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="operand">The operand.</param>
    public UnwrapKoto(ref TokenReader reader, SourceSpan range, Koto operand)
        : base(ref reader, range, operand)
    {
    }
}

/// <summary>
/// Represents a nonnegative isize index measured backward from the end of a collection.
/// </summary>
/// <remarks>
/// <c>^n</c> resolves to <c>length - n</c>. Consequently, <c>^0</c> is a valid range boundary,
/// but it is outside the valid positions for an element index.
/// </remarks>
public sealed class FromEndIndexKoto : UnaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.FromEndIndex;

    /// <summary>Gets the nonnegative distance from the end.</summary>
    public Koto Value => this.Operand;

    /// <summary>Initializes a new instance of the <see cref="FromEndIndexKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="operand">The operand.</param>
    public FromEndIndexKoto(ref TokenReader reader, SourceSpan range, Koto operand)
        : base(ref reader, range, operand)
    {
    }
}

/// <summary>Represents a unary plus expression.</summary>
public sealed class PrefixPlusKoto : UnaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.PrefixPlus;

    /// <summary>Initializes a new instance of the <see cref="PrefixPlusKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="operand">The operand.</param>
    public PrefixPlusKoto(ref TokenReader reader, SourceSpan range, Koto operand)
        : base(ref reader, range, operand)
    {
    }
}

/// <summary>Represents a prefix increment expression.</summary>
public sealed class PrefixPlusPlusKoto : UnaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.PrefixPlusPlus;

    /// <summary>Initializes a new instance of the <see cref="PrefixPlusPlusKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="operand">The operand.</param>
    public PrefixPlusPlusKoto(ref TokenReader reader, SourceSpan range, Koto operand)
        : base(ref reader, range, operand)
    {
    }
}

/// <summary>Represents a unary minus expression.</summary>
public sealed class PrefixMinusKoto : UnaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.PrefixMinus;

    /// <summary>Initializes a new instance of the <see cref="PrefixMinusKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="operand">The operand.</param>
    public PrefixMinusKoto(ref TokenReader reader, SourceSpan range, Koto operand)
        : base(ref reader, range, operand)
    {
    }
}

/// <summary>Represents a prefix decrement expression.</summary>
public sealed class PrefixMinusMinusKoto : UnaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.PrefixMinusMinus;

    /// <summary>Initializes a new instance of the <see cref="PrefixMinusMinusKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="operand">The operand.</param>
    public PrefixMinusMinusKoto(ref TokenReader reader, SourceSpan range, Koto operand)
        : base(ref reader, range, operand)
    {
    }
}

/// <summary>Represents a postfix increment expression.</summary>
public sealed class PostfixIncrementKoto : UnaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.PostfixIncrement;

    /// <summary>Initializes a new instance of the <see cref="PostfixIncrementKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="operand">The operand.</param>
    public PostfixIncrementKoto(ref TokenReader reader, SourceSpan range, Koto operand)
        : base(ref reader, range, operand)
    {
    }
}

/// <summary>Represents a postfix decrement expression.</summary>
public sealed class PostfixDecrementKoto : UnaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.PostfixDecrement;

    /// <summary>Initializes a new instance of the <see cref="PostfixDecrementKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="operand">The operand.</param>
    public PostfixDecrementKoto(ref TokenReader reader, SourceSpan range, Koto operand)
        : base(ref reader, range, operand)
    {
    }
}

/// <summary>Represents a logical negation expression.</summary>
public sealed class NotKoto : UnaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Not;

    /// <summary>Initializes a new instance of the <see cref="NotKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="operand">The operand.</param>
    public NotKoto(ref TokenReader reader, SourceSpan range, Koto operand)
        : base(ref reader, range, operand)
    {
    }
}

/// <summary>Represents a parenthesized expression.</summary>
public sealed class ParenthesizedKoto : UnaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Parenthesized;

    /// <summary>Initializes a new instance of the <see cref="ParenthesizedKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="operand">The operand.</param>
    public ParenthesizedKoto(ref TokenReader reader, SourceSpan range, Koto operand)
        : base(ref reader, range, operand)
    {
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
        => WriteGroupedTo(this.Operand, ref builder);

    internal static bool NeedsMultilineGrouping(Koto operand)
        => operand is IfKoto or MatchKoto or ForKoto or WhileKoto or LoopKoto or LabeledKoto or FunctionKoto;

    internal static void WriteGroupedTo(Koto operand, ref IndentedStringBuilder builder)
    {
        if (!NeedsMultilineGrouping(operand))
        {
            builder.Append('(');
            operand.WriteTo(ref builder);
            builder.Append(')');
            return;
        }

        builder.Append('(');
        builder.AppendLine();
        builder.IncrementIndent();
        operand.WriteTo(ref builder);
        builder.AppendLine();
        builder.Append(')');
        builder.DecrementIndent();
    }
}
