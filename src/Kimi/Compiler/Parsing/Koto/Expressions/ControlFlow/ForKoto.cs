// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Represents a <c>for</c> expression whose value is Unit.</summary>
public sealed class ForKoto : ExpressionKoto
{
    private List<IdentifierNameKoto> bindings;

    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.For;

    /// <summary>Gets the iteration bindings in source order.</summary>
    public IReadOnlyList<IdentifierNameKoto> Bindings => this.bindings;

    /// <summary>Gets the expression that supplies the values to iterate.</summary>
    public Koto Iterable { get; private set; }

    /// <summary>Gets the loop body.</summary>
    public CodeBlockKoto Body { get; private set; }

    /// <summary>Gets a value indicating whether the bindings use tuple syntax.</summary>
    public bool IsTupleBinding { get; private set; }

    // SPEC 14.6.1: element i is set when slot i is written "var Name"; a bare Name is a let binding. Null when no slot is.
    private readonly bool[]? mutableSlots;

    /// <summary>Gets or sets the Subject mode selected by the outermost operation of the iterable (SPEC 15.1.6).</summary>
    internal SubjectMode Mode
    {
        get => (SubjectMode)HirTables.PlanOf(this).Mode;
        set => HirTables.PlanFor(this, value != SubjectMode.Shared).Mode = (byte)value;
    }

    /// <summary>Gets or sets the owning user protocol entry, outside the source tree.</summary>
    internal InvocationKoto? EntryCall
    {
        get => HirTables.PlanOf(this).Second as InvocationKoto;
        set => HirTables.PlanFor(this, value is not null).Second = value;
    }

    /// <summary>Gets or sets the calls and item decomposition of a user protocol loop.</summary>
    internal BoundIteration? Iteration
    {
        get => HirTables.PlanOf(this).Plan as BoundIteration;
        set => HirTables.PlanFor(this, value is not null).Plan = value;
    }

    /// <summary>Initializes a new instance of the <see cref="ForKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete expression span.</param>
    /// <param name="bindings">The iteration bindings.</param>
    /// <param name="iterable">The expression that supplies values.</param>
    /// <param name="body">The loop body.</param>
    /// <param name="isTupleBinding">Whether the bindings use tuple syntax.</param>
    /// <param name="mutableSlots">Per slot, whether it is declared with <c>var</c>; null when none is.</param>
    public ForKoto(
        ref TokenReader reader,
        SourceSpan range,
        List<IdentifierNameKoto> bindings,
        Koto iterable,
        CodeBlockKoto body,
        bool isTupleBinding,
        bool[]? mutableSlots = null)
        : base(ref reader, range)
    {
        this.bindings = bindings;
        this.Iterable = iterable;
        this.Body = body;
        this.IsTupleBinding = isTupleBinding;
        this.mutableSlots = mutableSlots;

        this.Adopt(bindings);
        iterable.Parent = this;
        body.Parent = this;
    }

    /// <summary>Gets whether the slot at <paramref name="index"/> is a reassignable iteration local (SPEC 14.6.1).</summary>
    /// <param name="index">The slot index.</param>
    /// <returns>Whether the slot was written <c>var Name</c>.</returns>
    public bool IsMutableSlot(int index) => this.mutableSlots is { } slots && (uint)index < (uint)slots.Length && slots[index];

    /// <summary>Gets whether <paramref name="binding"/> is one of this loop's reassignable iteration locals.</summary>
    /// <param name="binding">A binding declared by this loop.</param>
    /// <returns>Whether the binding was written <c>var Name</c>.</returns>
    public bool IsMutableSlot(IdentifierNameKoto binding)
    {
        if (this.mutableSlots is null)
        {
            return false;
        }

        for (var i = 0; i < this.bindings.Count; i++)
        {
            if (ReferenceEquals(this.bindings[i], binding))
            {
                return this.IsMutableSlot(i);
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.ForKeyword);
        builder.AppendSpace();
        if (this.IsTupleBinding)
        {
            builder.Append(Constants.OpenParenthesisChar);
        }

        for (var i = 0; i < this.bindings.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendCommaAndSpace();
            }

            if (this.IsMutableSlot(i))
            {
                builder.Append(Constants.VarKeyword);
                builder.AppendSpace();
            }

            this.bindings[i].WriteTo(ref builder);
        }

        if (this.IsTupleBinding)
        {
            builder.Append(Constants.CloseParenthesisChar);
        }

        builder.AppendSpace();
        builder.Append(Constants.InKeyword);
        builder.AppendSpace();
        this.Iterable.WriteTo(ref builder);
        this.Body.WriteBranchTo(ref builder);
    }

    protected override void ForEachChildSlot(ref ChildSlots slots)
    {
        slots.List(this.bindings);
        this.Iterable = slots.Slot(this.Iterable);
        this.Body = slots.Slot(this.Body);
        if (slots.Replaced)
        {
            // Synthetic calls and arms retain source children; a syntax edit must not reuse the old entry or body.
            this.Iteration?.Decomposition.Reset(null);
            this.EntryCall = null;
            this.Iteration = null;
        }
    }
}
