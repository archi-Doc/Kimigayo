// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

#pragma warning disable SA1402 // File may only contain a single type
#pragma warning disable SA1649 // File name should match first type name

/// <summary>
/// Represents a binary expression. An operator is this class with its <see cref="KotoKind"/>; member access, indexing,
/// conversion, <c>is</c> and Origin relations derive from it for the members of their own.
/// </summary>
/// <remarks>
/// The infix spelling is looked up from a table so every operator shares the same writing and child-management code.
/// </remarks>
public class BinaryKoto : ExpressionKoto
{
    private static readonly string[] InfixTexts = new string[MaxKind];

    private readonly KotoKind kind;

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

    /// <summary>Initializes a new instance of the <see cref="BinaryKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="kind">The node kind.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public BinaryKoto(ref TokenReader reader, SourceSpan range, KotoKind kind, Koto left, Koto right)
        : base(ref reader, range)
    {
        this.kind = kind;
        this.Left = left;
        this.Right = right;
        left.Parent = this;
        right.Parent = this;
    }

    internal BinaryKoto(Koto source, KotoKind kind, Koto left, Koto right)
        : base(source.CodeContext, source.Span)
    {
        this.kind = kind;
        this.Parent = source;
        this.Left = left;
        this.Right = right;
    }

    /// <inheritdoc/>
    public sealed override KotoKind Akind => this.kind;

    /// <summary>Gets the left operand.</summary>
    public Koto Left { get; private set; }

    /// <summary>Gets the right operand.</summary>
    public Koto Right { get; private set; }

    /// <summary>Gets the infix operator spelling, including surrounding spaces.</summary>
    public string InfixText => this is IsKoto { IsNegated: true } ? " is not " : InfixTexts[(int)this.Akind] ?? string.Empty;

    internal BoundArithmetic? ArithmeticStorage { get; set; }

    internal InvocationKoto? ArithmeticCall => this.BindingState == BindingState.Resolved && this.ArithmeticStorage is { Active: true } plan ? plan.Call : null;

    internal InvocationKoto? ComparisonStorage { get; set; }

    internal bool ComparisonActive { get; set; }

    internal InvocationKoto? ComparisonCall => this.ComparisonActive && this.BindingState == BindingState.Resolved ? this.ComparisonStorage : null;

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.Left.WriteTo(ref builder);
        this.WriteAttributeChainTo(ref builder, KotoWriteOptions.None);
        builder.Append(this.InfixText);
        this.Right.WriteTo(ref builder);
    }

    protected override void VisitChildrenCore(KotoVisitor visitor)
    {
        visitor.Visit(this.Left);
        visitor.Visit(this.Right);
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
    /// <summary>Initializes a new instance of the <see cref="MemberAccessKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public MemberAccessKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, KotoKind.MemberAccess, left, right)
    {
    }

    internal MemberAccessKoto(Koto source, Koto left, Koto right)
        : base(source, KotoKind.MemberAccess, left, right)
    {
    }

    /// <summary>Gets the accessed member expression.</summary>
    public Koto Accessor => this.Right;

    internal bool IsDirectStorage { get; set; }

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
    /// <summary>Initializes a new instance of the <see cref="IndexKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="index">The index expression.</param>
    public IndexKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto index)
        : base(ref reader, range, KotoKind.Index, left, index)
    {
    }

    /// <summary>Gets the nonnegative isize index, from-end index, or range expression inside brackets.</summary>
    public Koto Index => this.Right;

    /// <summary>Gets the expression inside brackets.</summary>
    public Koto Argument => this.Right;

    /// <summary>Gets a value indicating whether this subscript produces a slice.</summary>
    public bool IsSlice => this.Right is RangeKoto;

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.Left.WriteTo(ref builder);
        builder.Append(Constants.OpenBracketChar);
        this.Right.WriteTo(ref builder);
        builder.Append(Constants.CloseBracketChar);
    }
}

/// <summary>Represents a conversion expression.</summary>
public sealed class ConversionKoto : BinaryKoto
{
    /// <summary>Initializes a new instance of the <see cref="ConversionKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public ConversionKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, KotoKind.Conversion, left, right)
    {
    }

    internal ConversionKoto(Koto source, Koto left, Koto right)
        : base(source, KotoKind.Conversion, left, right)
    {
    }

    internal ConversionBinding ConversionBinding { get; set; }

    // The syntax owns its retained plan; edits can release it with the source tree.
    internal InvocationKoto? CreationStorage { get; set; }

    internal InvocationKoto? CreationCall => this.HasCurrentBinding && this.ConversionBinding == ConversionBinding.ObjectCreation ? this.CreationStorage : null;

    internal ExplicitAdaptationPlan? AdaptationStorage { get; set; }

    internal ExplicitAdaptationPlan? Adaptation => this.HasCurrentBinding && this.ConversionBinding == ConversionBinding.CaseAdaptation ? this.AdaptationStorage : null;

    /// <summary>Gets or sets the result of a direct-literal conversion folded at compile time (SPEC 13.5.4.2), as the
    /// sign-extended N-bit payload of the target Type, or null when the conversion runs on a value.</summary>
    internal Int128? FoldedConstant { get; set; }
}

/// <summary>Represents an <c>is</c> expression.</summary>
public sealed class IsKoto : BinaryKoto, IOriginClauseOwner
{
    private List<EffectBoundKoto>? effectBounds;

    /// <summary>Initializes a new instance of the <see cref="IsKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public IsKoto(ref TokenReader reader, SourceSpan range, Koto left, Koto right)
        : base(ref reader, range, KotoKind.Is, left, right)
    {
    }

    /// <summary>Gets the effect clauses attached to this Callable Constraint (SPEC 8.4.10.7).</summary>
    public IReadOnlyList<EffectBoundKoto> EffectBounds => (IReadOnlyList<EffectBoundKoto>?)this.effectBounds ?? [];

    /// <summary>Gets a value indicating whether this is an associated-type constraint.</summary>
    public bool IsAssociatedConstraint { get; internal set; }

    /// <summary>Gets the formation Type of an Origin-parameterized associated requirement.</summary>
    public Koto? FormationType { get; internal set; }

    /// <summary>Gets the bound compile-time proposition; ordinary runtime tests leave this null.</summary>
    public BoundConstraint? BoundConstraint { get; internal set; }

    /// <summary>Gets a value indicating whether syntax selected a runtime test rather than a Requirement Test.</summary>
    public bool IsRuntimeTest { get; internal set; }

    /// <summary>Gets a value indicating whether the runtime test uses <c>is not</c>. Its right child remains Type syntax.</summary>
    public bool IsNegated { get; internal set; }

    /// <summary>Gets this binding pass's runtime test, mutually exclusive with BoundConstraint.</summary>
    public BoundRuntimeTypeTest? BoundRuntimeTest { get; internal set; }

    List<OriginRelationKoto>? IOriginClauseOwner.OriginClauses { get; set; }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        if (this.IsAssociatedConstraint)
        {
            builder.Append("associate ");
        }

        base.WriteTo(ref builder);
        if (this.FormationType is { } formation)
        {
            builder.Append(" for ");
            formation.WriteTo(ref builder);
        }

        OriginClauses.Write(this, ref builder);
        if (this.EffectBounds.Count != 0)
        {
            builder.IncrementIndent();
            for (var i = 0; i < this.EffectBounds.Count; i++)
            {
                builder.AppendLine();
                this.EffectBounds[i].WriteTo(ref builder);
            }

            builder.DecrementIndent();
        }
    }

    internal void AddEffectBound(EffectBoundKoto effect)
    {
        (this.effectBounds ??= []).Add(effect);
        this.Adopt(effect);
    }

    protected override void VisitChildrenCore(KotoVisitor visitor)
    {
        base.VisitChildrenCore(visitor);
        if (this.FormationType is { } formation)
        {
            visitor.Visit(formation);
        }

        for (var i = 0; i < this.EffectBounds.Count; i++)
        {
            visitor.Visit(this.EffectBounds[i]);
        }
    }

    protected override IEnumerable<Koto> GetChildNodes()
    {
        yield return this.Left;
        yield return this.Right;
        if (this.FormationType is { } formation)
        {
            yield return formation;
        }

        foreach (var effect in this.EffectBounds)
        {
            yield return effect;
        }
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (newKoto is EffectBoundKoto replacement && this.effectBounds is { } bounds)
        {
            for (var i = 0; i < bounds.Count; i++)
            {
                if (ReferenceEquals(bounds[i], oldKoto))
                {
                    bounds[i] = replacement;
                    return true;
                }
            }
        }

        if (ReferenceEquals(oldKoto, this.FormationType))
        {
            this.FormationType = newKoto;
            return true;
        }

        return base.ReplaceChildCore(oldKoto, newKoto);
    }
}
