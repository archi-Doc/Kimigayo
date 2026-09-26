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

    // SPEC 14.6.1: bit i is set when slot i is written "var Name"; a bare Name is a let binding.
    private readonly ulong mutableSlots;

    /// <summary>Gets or sets the borrow through which the loop enumerates its Subject (SPEC 14.6.2): the implicit whole-range Slice of a bare
    /// array Place, or the shared or exclusive view of an array or Dictionary in the Subject mode; null for a Subject acquired by value.</summary>
    internal BoundType? SharedIterable { get; set; }

    /// <summary>Gets or sets the Subject mode selected by the outermost operation of the iterable (SPEC 15.1.6).</summary>
    internal SubjectMode Mode { get; set; }

    /// <summary>Initializes a new instance of the <see cref="ForKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete expression span.</param>
    /// <param name="bindings">The iteration bindings.</param>
    /// <param name="iterable">The expression that supplies values.</param>
    /// <param name="body">The loop body.</param>
    /// <param name="isTupleBinding">Whether the bindings use tuple syntax.</param>
    /// <param name="mutableSlots">The bit set of slots declared with <c>var</c>.</param>
    public ForKoto(
        ref TokenReader reader,
        SourceSpan range,
        List<IdentifierNameKoto> bindings,
        Koto iterable,
        CodeBlockKoto body,
        bool isTupleBinding,
        ulong mutableSlots = 0)
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
    public bool IsMutableSlot(int index) => index >= 0 && index < 64 && ((this.mutableSlots >> index) & 1) != 0;

    /// <summary>Gets whether <paramref name="binding"/> is one of this loop's reassignable iteration locals.</summary>
    /// <param name="binding">A binding declared by this loop.</param>
    /// <returns>Whether the binding was written <c>var Name</c>.</returns>
    public bool IsMutableSlot(IdentifierNameKoto binding)
    {
        if (this.mutableSlots == 0)
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

    protected override void VisitChildrenCore(KotoVisitor visitor)
    {
        for (var bindingIndex = 0; bindingIndex < this.bindings.Count; bindingIndex++)
        {
            var binding = this.bindings[bindingIndex];
            visitor.Visit(binding);
        }

        visitor.Visit(this.Iterable);
        visitor.Visit(this.Body);
    }

    protected override IEnumerable<Koto> GetChildNodes()
    {
        foreach (var binding in this.bindings)
        {
            yield return binding;
        }

        yield return this.Iterable;
        yield return this.Body;
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (oldKoto is IdentifierNameKoto && ReplaceInList(this.bindings, oldKoto, newKoto))
        {
            return true;
        }

        if (this.Iterable == oldKoto)
        {
            this.Iterable = newKoto;
            return true;
        }

        if (this.Body == oldKoto && newKoto is CodeBlockKoto block)
        {
            this.Body = block;
            return true;
        }

        return false;
    }
}
