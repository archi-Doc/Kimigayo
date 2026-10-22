// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// A requirement effect bound (SPEC 8.11.1): an effect clause in the Constraint region of a requirement, or an effect
/// specification that names a requirement inherited from an ancestor through its Contract selector. Its bound is
/// <c>confined</c>, the closed vocabulary's one word (SPEC 8.11); any other word is a syntax error whose item is a recovery.
/// </summary>
public sealed class EffectBoundKoto : Koto
{
    internal EffectBoundKoto(ref TokenReader reader, SourceSpan span, Koto? selector, Koto? name)
        : base(ref reader, span)
    {
        this.Selector = selector;
        this.Name = name;
        this.Adopt(selector);
        this.Adopt(name);
    }

    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.EffectBound;

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

        builder.Append(Constants.ConfinedKeyword);
    }

    protected override void ForEachChildSlot(ref ChildSlots slots)
    {
        this.Selector = slots.Slot(this.Selector);
        this.Name = slots.Slot(this.Name);
    }
}
