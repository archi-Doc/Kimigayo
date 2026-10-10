// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

#pragma warning disable SA1402 // File may only contain a single type
#pragma warning disable SA1649 // File name should match first type name

/// <summary>
/// Represents a unary expression. An operator is this class with its <see cref="KotoKind"/>; attributes, macros,
/// dereferences, from-end indexes and parentheses derive from it for the members of their own.
/// </summary>
/// <remarks>
/// The prefix and postfix spellings are looked up from a table so every operator shares the same writing code.
/// </remarks>
public class UnaryKoto : ExpressionKoto
{
    private static readonly string?[] PrefixTexts = new string?[MaxKind];
    private static readonly string?[] PostfixTexts = new string?[MaxKind];

    private readonly KotoKind kind;

    static UnaryKoto()
    {
        PrefixTexts[(int)KotoKind.Attribute] = "#";
        PrefixTexts[(int)KotoKind.Macro] = "$";
        PrefixTexts[(int)KotoKind.Dereference] = "*";
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

    /// <summary>Initializes a new instance of the <see cref="UnaryKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="kind">The node kind.</param>
    /// <param name="operand">The operand.</param>
    public UnaryKoto(ref TokenReader reader, SourceSpan range, KotoKind kind, Koto operand)
        : base(ref reader, range)
    {
        this.kind = kind;
        this.Operand = operand;
        operand.Parent = this;
    }

    /// <inheritdoc/>
    public sealed override KotoKind Akind => this.kind;

    /// <summary>Gets or sets the operand.</summary>
    public Koto Operand { get; protected set; }

    /// <summary>Gets the spelling of a prefix or postfix operator, without the operand or spaces.</summary>
    public string OperatorText => (PrefixTexts[(int)this.Akind] ?? PostfixTexts[(int)this.Akind] ?? string.Empty).Trim();

    internal BoundArithmetic? ArithmeticStorage { get; set; }

    internal InvocationKoto? ArithmeticCall => this.BindingState == BindingState.Resolved && this.ArithmeticStorage is { Active: true } plan ? plan.Call : null;

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

    protected override void ForEachChildSlot(ref ChildSlots slots)
    {
        this.Operand = slots.Slot(this.Operand);
    }
}

/// <summary>Represents an attribute expression.</summary>
public sealed class AttributeKoto : UnaryKoto
{
    /// <summary>Initializes a new instance of the <see cref="AttributeKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="operand">The operand.</param>
    public AttributeKoto(ref TokenReader reader, SourceSpan range, Koto operand)
        : base(ref reader, range, KotoKind.Attribute, operand)
    {
    }

    /// <summary>Gets the attribute identifier.</summary>
    public Koto IdentifierKoto
        => this.Operand is InvocationKoto { Method: IdentifierNameKoto identifier } ? identifier : this.Operand;

    /// <summary>Gets the attribute arguments.</summary>
    public List<Koto> Arguments
        => this.Operand is InvocationKoto { Method: IdentifierNameKoto } invocation ? invocation.Arguments : field ??= [];

    // Parser-local fragment identity; distinct from source-file or physical layout order.
    internal int FragmentOrdinal { get; set; }

    internal string? LayoutMode
        => this.IdentifierKoto is IdentifierNameKoto { IdentifierName: "Layout" } &&
            this.Operand is InvocationKoto { ArgumentNodes.Count: 1 } call && call.GetArgumentLabel(0) is null &&
            call.ArgumentNodes[0] is StringLiteralKoto literal && literal.Literal is "C" or "Kimigayo" ? literal.Literal : null;
}

/// <summary>Represents a macro expression.</summary>
public sealed class MacroKoto : UnaryKoto
{
    /// <summary>Initializes a new instance of the <see cref="MacroKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="operand">The operand.</param>
    public MacroKoto(ref TokenReader reader, SourceSpan range, Koto operand)
        : base(ref reader, range, KotoKind.Macro, operand)
    {
    }
}

/// <summary>Represents a dereference expression.</summary>
public sealed class DereferenceKoto : UnaryKoto
{
    /// <summary>Initializes a new instance of the <see cref="DereferenceKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="operand">The operand.</param>
    public DereferenceKoto(ref TokenReader reader, SourceSpan range, Koto operand)
        : base(ref reader, range, KotoKind.Dereference, operand)
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
    /// <summary>Initializes a new instance of the <see cref="FromEndIndexKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="operand">The operand.</param>
    public FromEndIndexKoto(ref TokenReader reader, SourceSpan range, Koto operand)
        : base(ref reader, range, KotoKind.FromEndIndex, operand)
    {
    }

    /// <summary>Gets the nonnegative distance from the end.</summary>
    public Koto Value => this.Operand;
}

/// <summary>Represents a parenthesized expression.</summary>
public sealed class ParenthesizedKoto : UnaryKoto
{
    /// <summary>Initializes a new instance of the <see cref="ParenthesizedKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="operand">The operand.</param>
    public ParenthesizedKoto(ref TokenReader reader, SourceSpan range, Koto operand)
        : base(ref reader, range, KotoKind.Parenthesized, operand)
    {
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
        => WriteGroupedTo(this.Operand, ref builder);

    internal static bool NeedsMultilineGrouping(Koto operand)
        => operand is DoKoto or IfKoto or MatchKoto or ForKoto or WhileKoto or LoopKoto or LabeledKoto or FunctionKoto;

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
