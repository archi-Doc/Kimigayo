// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

#pragma warning disable SA1402 // The closed bound vocabulary and its syntax.

/// <summary>The closed vocabulary of requirement effect bounds (SPEC 8.4.10).</summary>
public enum EffectBoundKind : byte
{
    /// <summary><c>confined</c>: the transitive effects of an implementation include no environment effect (SPEC 8.4.10.2).</summary>
    Confined,

    /// <summary><c>preserves results</c>: a call conflicts with no Loan that an earlier result of the same requirement on the same value keeps (SPEC 8.4.10.3).</summary>
    PreservesResults,
}

/// <summary>
/// A requirement effect bound (SPEC 8.4.10.1): an effect clause in the Constraint region of a requirement, or an effect
/// specification that names a requirement inherited from an ancestor through its Contract selector.
/// </summary>
public sealed class EffectBoundKoto : Koto
{
    internal EffectBoundKoto(ref TokenReader reader, SourceSpan span, EffectBoundKind bound, Koto? selector, Koto? name)
        : base(ref reader, span)
    {
        this.Bound = bound;
        this.Selector = selector;
        this.Name = name;
        this.Adopt(selector);
        this.Adopt(name);
    }

    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.EffectBound;

    /// <summary>Gets the declared bound.</summary>
    public EffectBoundKind Bound { get; }

    /// <summary>Gets the Contract selector of an effect specification, or <see langword="null"/> for an effect clause.</summary>
    public Koto? Selector { get; private set; }

    /// <summary>Gets the requirement Name of an effect specification, or <see langword="null"/> for an effect clause.</summary>
    public Koto? Name { get; private set; }

    /// <summary>Gets a value indicating whether this is an effect specification rather than an effect clause.</summary>
    public bool IsSpecification => this.Name is not null;

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append("effect ");
        if (this.Selector is not null)
        {
            this.Selector.WriteTo(ref builder);
            builder.Append('.');
            this.Name?.WriteTo(ref builder);
            builder.Append(' ');
        }

        builder.Append(Spelling(this.Bound));
    }

    /// <summary>Gets the source spelling of a bound.</summary>
    /// <param name="bound">The bound.</param>
    /// <returns><c>confined</c> or <c>preserves results</c>.</returns>
    internal static string Spelling(EffectBoundKind bound)
        => bound == EffectBoundKind.Confined ? Constants.ConfinedKeyword : Constants.PreservesKeyword + " " + Constants.ResultsKeyword;

    protected override void ForEachChildSlot(ref ChildSlots slots)
    {
        this.Selector = slots.Slot(this.Selector);
        this.Name = slots.Slot(this.Name);
    }
}
