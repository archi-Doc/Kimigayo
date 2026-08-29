// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents an attribute expression.
/// </summary>
[TinyhandObject]
public partial class AttributeKoto : UnaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Attribute;

    /// <summary>Gets the attribute identifier.</summary>
    [IgnoreMember]
    public Koto IdentifierKoto { get; private set; }

    /// <summary>Gets the attribute arguments.</summary>
    [IgnoreMember]
    public List<Koto> Arguments { get; private set; } = [];

    /// <summary>Gets a value indicating whether this is an <c>#if</c> attribute.</summary>
    public bool IsIfAttribute
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (this.IdentifierKoto is IdentifierNameKoto unresolvedKoto)
            {
                return unresolvedKoto.IdentifierName.SequenceEqual(Constants.IfAttribute) == true;
            }
            else
            {
                return false;
            }
        }
    }

    /// <summary>Initializes a new instance of the <see cref="AttributeKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="operand">The operand.</param>
    public AttributeKoto(ref TokenReader reader, SourceSpan range, Koto operand)
        : base(ref reader, range, operand)
    {
        // Expose invocation components for efficient attribute evaluation.
        if (operand is InvocationKoto invocationKoto &&
            invocationKoto.Method is IdentifierNameKoto unresolvedKoto)
        {
            this.IdentifierKoto = unresolvedKoto;
            this.Arguments = invocationKoto.Arguments;
        }
        else
        {
            this.IdentifierKoto = operand;
        }
    }

    /// <inheritdoc/>
    public override string ToString()
        => $"#{this.Operand.ToString()}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.SharpChar);
        builder.Append(this.Operand.ToString());
    }

    internal override void RestoreAfterDeserialization(CodeContext codeContext, Koto? parent)
    {
        base.RestoreAfterDeserialization(codeContext, parent);
        if (this.Operand is InvocationKoto invocationKoto &&
            invocationKoto.Method is IdentifierNameKoto identifierKoto)
        {
            this.IdentifierKoto = identifierKoto;
            this.Arguments = invocationKoto.Arguments;
        }
        else
        {
            this.IdentifierKoto = this.Operand;
            this.Arguments = [];
        }
    }
}

/// <summary>
/// Represents a macro expression.
/// </summary>
[TinyhandObject]
public partial class MacroKoto : UnaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"${this.Operand.ToString()}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.DollarChar);
        builder.Append(this.Operand.ToString());
    }
}

/// <summary>
/// Represents an unwrap expression.
/// </summary>
[TinyhandObject]
public partial class UnwrapKoto : UnaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"*{this.Operand.ToString()}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.AsteriskChar);
        builder.Append(this.Operand.ToString());
    }
}

/// <summary>
/// Represents a prefix caret expression.
/// </summary>
[TinyhandObject]
public partial class PrefixCaretKoto : UnaryKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.PrefixCaret;

    /// <summary>Initializes a new instance of the <see cref="PrefixCaretKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="operand">The operand.</param>
    public PrefixCaretKoto(ref TokenReader reader, SourceSpan range, Koto operand)
        : base(ref reader, range, operand)
    {
    }

    /// <inheritdoc/>
    public override string ToString()
        => $"^{this.Operand.ToString()}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.CaretChar);
        builder.Append(this.Operand.ToString());
    }
}

/// <summary>
/// Represents a unary plus expression.
/// </summary>
[TinyhandObject]
public partial class PrefixPlusKoto : UnaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"+{this.Operand.ToString()}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.PlusChar);
        builder.Append(this.Operand.ToString());
    }
}

/// <summary>
/// Represents a prefix increment expression.
/// </summary>
[TinyhandObject]
public partial class PrefixPlusPlusKoto : UnaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"++{this.Operand.ToString()}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.PlusChar);
        builder.Append(Constants.PlusChar);
        builder.Append(this.Operand.ToString());
    }
}

/// <summary>
/// Represents a unary minus expression.
/// </summary>
[TinyhandObject]
public partial class PrefixMinusKoto : UnaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"-{this.Operand.ToString()}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.MinusChar);
        builder.Append(this.Operand.ToString());
    }
}

/// <summary>
/// Represents a prefix decrement expression.
/// </summary>
[TinyhandObject]
public partial class PrefixMinusMinusKoto : UnaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"--{this.Operand.ToString()}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.MinusChar);
        builder.Append(Constants.MinusChar);
        builder.Append(this.Operand.ToString());
    }
}

/// <summary>
/// Represents a postfix increment expression.
/// </summary>
[TinyhandObject]
public partial class PostfixIncrementKoto : UnaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Operand.ToString()}++";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(this.Operand.ToString());
        builder.Append(Constants.PlusChar);
        builder.Append(Constants.PlusChar);
    }
}

/// <summary>
/// Represents a postfix decrement expression.
/// </summary>
[TinyhandObject]
public partial class PostfixDecrementKoto : UnaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{this.Operand.ToString()}--";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(this.Operand.ToString());
        builder.Append(Constants.MinusChar);
        builder.Append(Constants.MinusChar);
    }
}

/// <summary>
/// Represents a logical negation expression.
/// </summary>
[TinyhandObject]
public partial class NotKoto : UnaryKoto
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

    /// <inheritdoc/>
    public override string ToString()
        => $"{Constants.NotKeyword} {this.Operand.ToString()}";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.NotKeyword);
        builder.AppendSpace();
        builder.Append(this.Operand.ToString());
    }
}

/// <summary>
/// Represents a parenthesized expression.
/// </summary>
[TinyhandObject]
public partial class ParenthesizedKoto : UnaryKoto
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
    public override string ToString()
        => $"({this.Operand.ToString()})";

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.OpenParenthesisChar);
        builder.Append(this.Operand.ToString());
        builder.Append(Constants.CloseParenthesisChar);
    }
}

/// <summary>
/// Provides the base representation of a unary expression.
/// </summary>
[TinyhandObject(ReservedKeyCount = 2)]
public abstract partial class UnaryKoto : Koto
{
    /// <summary>Gets or sets the operand.</summary>
    [Key(1)]
    public Koto Operand { get; protected set; }

    /// <summary>Initializes a new instance of the <see cref="UnaryKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="operand">The operand.</param>
    public UnaryKoto(ref TokenReader reader, SourceSpan range, Koto operand)
        : base(ref reader, range)
    {
        this.Operand = operand;
        operand.Parent = this;
    }

    internal UnaryKoto(CodeContext codeContext)
        : base(codeContext, default)
    {
        this.Operand = default!;
    }

    /// <inheritdoc/>
    public override string ToString()
        => $"Unary:{this.Operand.ToString()}";

    internal override bool ReplaceChild(Koto oldKoto, Koto newKoto)
    {
        if (oldKoto == this.Operand)
        {
            this.Operand = newKoto;
            newKoto.Parent = this;
            oldKoto.Parent = default;
            return true;
        }

        return false;
    }

    internal override void RestoreAfterDeserialization(CodeContext codeContext, Koto? parent)
    {
        base.RestoreAfterDeserialization(codeContext, parent);
        this.Operand.RestoreAfterDeserialization(codeContext, this);
    }

    [TinyhandOnDeserialized]
    protected void OnDeserialized()
    {
        this.Operand.Parent = this;
        this.Operand.CodeContext = this.CodeContext;
    }
}
