// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the Semantics-case views of a MIR body.
// Input: the body's pair binders and TyCtxt. Output: case Types, projection, acquisition and adaptation views.
// Case order is stable; views share the body and reuse their storage.
// Must not copy the body per case or re-bind source syntax.

using System.Numerics;
using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

/// <summary>The Semantics cases of a MIR body (plan 5.6.1, SPEC 8.15) as views of the one body, which is never copied per case: a view
/// gives each pair binder of <see cref="MirBody.Binders"/> one of its admitted Semantics, and under it a Type is its case Type
/// (<see cref="Type"/>), a PairLayer step the Place itself or the referent of its case's reference or handle (<see cref="Layer"/>), a
/// PairAcquire a Copy or a Reborrow (<see cref="Reborrows"/>) and an Adapt the operation its case selects (<see cref="Adapt"/>), the only
/// case-dependent forms. The analyses that depend on the case (R4:
/// A1, A4's local facts, A5) run once per view. Views are numbered as the old analysis numbers its case runs, the last binder advancing
/// fastest, each through its admitted set in SemanticsMask order, so view i is its case bit i. The selected view is kept in reused
/// storage, so selecting views allocates nothing once it has held the widest body's binders.</summary>
/// <param name="types">The Type context of the bodies, which interns the case Types.</param>
internal sealed class MirCases(TyCtxt types)
{
    private PairCase[] view = [];
    private int width;

    /// <summary>Gets the body of the selected view.</summary>
    internal MirBody Body { get; private set; } = null!;

    /// <summary>Gets the number of views of a body: the product of its binders' admitted sets (<see cref="PairBinder.CaseProduct"/>), 1
    /// without a binder, at most <see cref="PairBinder.CaseBound"/>: Binding decides the bound once per definition and binds no part of a
    /// definition beyond it (SPEC 8.15, 23.1.1), and <see cref="MirValidator"/> checks it, so the analyses run every view.</summary>
    /// <param name="body">The body.</param>
    /// <returns>The number of views, saturated at the largest count.</returns>
    internal static long Count(MirBody body) => PairBinder.CaseProduct(body.Binders);

    /// <summary>Selects a view of a body: for each resolved binder, the admitted Semantics its digit of <paramref name="index"/> picks. A
    /// binder without an admitted Semantics (a Binding error) has no case: its entry is empty, so its layers keep their declared Types and
    /// its steps and acquisitions read as the owner case's.</summary>
    /// <param name="body">The body.</param>
    /// <param name="index">The view, below <see cref="Count"/>.</param>
    /// <returns>The view: the case of each binder, by the binder's index.</returns>
    internal ReadOnlySpan<PairCase> Select(MirBody body, long index)
    {
        var binders = body.Binders;
        if (this.view.Length < binders.Count)
        {
            this.view = new PairCase[Math.Max(binders.Count, 2 * this.view.Length)];
        }

        (this.Body, this.width) = (body, binders.Count);
        for (var b = binders.Count - 1; b >= 0; b--)
        {
            this.view[b] = default;
            if (!binders[b].Resolved)
            {
                continue;
            }

            var admitted = (uint)binders[b].Admitted;
            var size = BitOperations.PopCount(admitted);
            for (var digit = index % size; digit > 0; digit--)
            {
                admitted &= admitted - 1;
            }

            index /= size;
            this.view[b] = new(binders[b].Target, (SemanticsKind)BitOperations.TrailingZeroCount(admitted)); // Mask bit i is SemanticsKind i.
        }

        return this.view.AsSpan(0, this.width);
    }

    /// <summary>Selects no view: every pair layer keeps its declared Type, so a Type of a use no view of which is known admits every case
    /// (an obligation Binding publishes for a use, R4-M22).</summary>
    internal void Deselect() => this.width = 0;

    /// <summary>Gets the views of a callee body that a call with a substitution selects under the selected view (SPEC 8.15): for each
    /// resolved binder of the callee, the case of its whole Type in the substitution, the Semantics of that Type's case Type, or every
    /// admitted case while that Type is still abstract. Views are decoded as <see cref="Select"/> numbers them; the selected view is kept.</summary>
    /// <param name="callee">The callee body.</param>
    /// <param name="subst">The call's substitution, or null for the callee's own (every view).</param>
    /// <returns>The views, bit i for view i.</returns>
    internal ulong Views(MirBody callee, Subst? subst)
    {
        var count = Count(callee);
        var views = ulong.MaxValue >> (64 - (int)count);
        var weight = 1L; // A digit's weight in a view's number: the last binder advances fastest.
        for (var b = callee.Binders.Count - 1; b >= 0 && subst is not null; b--)
        {
            var binder = callee.Binders[b];
            var size = binder.Resolved ? BitOperations.PopCount((uint)binder.Admitted) : 1;
            if (binder.Resolved && binder.Target.WholeType is { } whole && types.Apply(whole, subst) is { } actual &&
                this.Type(actual) is { IsAbstract: false } type)
            {
                var bit = 1u << (int)type.Semantics;
                var digit = ((uint)binder.Admitted & bit) == 0 ? -1 : BitOperations.PopCount((uint)binder.Admitted & (bit - 1));
                for (var view = 0L; view < count; view++)
                {
                    views &= (view / weight) % size == digit ? ulong.MaxValue : ~(1UL << (int)view);
                }
            }

            weight *= size;
        }

        return views;
    }

    /// <summary>Gets the Type a Type of the body has under the selected view: every pair layer of a binder the view's case Type
    /// (<see cref="TyCtxt.CaseType"/>), as a Place's, a local's or a projection's Type is in that case.</summary>
    /// <param name="type">The Type.</param>
    /// <returns>The case Type.</returns>
    internal BoundType Type(BoundType type) => types.CaseType(type, this.view.AsSpan(0, this.width));

    /// <summary>Gets what a PairLayer step of the selected body is under the selected view (SPEC 13.5.5): the Place it applies to itself
    /// in the owner case, else the referent of the reference or object handle that Place stores in the case. The Place selected has the
    /// case Type of the step's Type either way.</summary>
    /// <param name="step">A Place whose last projection is a PairLayer.</param>
    /// <returns>Null in the owner case, else the case Type of the layer, whose referent the step selects.</returns>
    internal BoundType? Layer(MirPlace step)
    {
        var row = this.Body.Projections[step.Projection];
        return this.view[row.Arg].Semantics == SemanticsKind.Owner ? null : this.Type(this.Body.TypeOf(step with { Projection = row.Prefix })!);
    }

    /// <summary>Gets whether a PairAcquire of the selected body Reborrows under the selected view (SPEC 8.14): its Place's case Type is an
    /// exclusive reference (<see cref="MirTypes.IsExclusive"/>, the builder's rule for a concrete Type), whose referent it borrows
    /// exclusively in that Semantics; in any other case it Copies its Place, a reference or a value Binding proved Copy.</summary>
    /// <param name="acquire">A PairAcquire operand.</param>
    /// <returns>Whether it Reborrows.</returns>
    internal bool Reborrows(in MirOperand acquire) => MirTypes.IsExclusive(this.Type(this.Body.TypeOf(acquire.Place)!));

    /// <summary>Gets what an Adapt of the selected body is under the selected view (SPEC 8.14, 13.5.3): the operation Binding selects
    /// (<see cref="TyCtxt.Adaptation"/>) from the case Type of its source token to the case Type of its target, as that operation's
    /// ordinary rvalue: an Identity or an object creation its operand (a creation also allocates), a pointer conversion or an upcast
    /// keeping a non-exclusive Semantics a Proven Cast of it, any other a borrow of the payload for an object reference, else of the
    /// operand's Place (a value borrow, an address), two-phase where it is exclusive and produces an invocation's input (its input bit,
    /// SPEC 15.6.7).</summary>
    /// <param name="adapt">An Adapt of the selected body.</param>
    /// <param name="operation">The operation; None where the case selects none.</param>
    /// <returns>The rvalue, a Cast or a Ref of the case's target Type; the Adapt itself where the case selects none.</returns>
    internal MirRvalue Adapt(in MirRvalue adapt, out ConversionBinding operation)
    {
        var operands = this.Body.Operands;
        var source = this.Type(this.Body.Consts[operands[adapt.Start + 1].Arg].Type);
        var target = this.Type(adapt.Type!);
        operation = TyCtxt.Adaptation(source, target, (adapt.Op & MirRvalue.AdaptShorthand) != 0);
        return operation switch
        {
            ConversionBinding.None => adapt,
            ConversionBinding.Identity or ConversionBinding.ObjectCreation => MirRvalue.Use(adapt.Start),
            ConversionBinding.Pointer => MirRvalue.Cast(MirTypes.PointerCast(source), adapt.Start, target) with { Proven = true },
            ConversionBinding.ObjectUpcast when source.Semantics == target.Semantics && !MirTypes.IsExclusive(target) =>
                MirRvalue.Cast(MirCastKind.Upcast, adapt.Start, target) with { Proven = true },
            _ => MirRvalue.Ref(
                MirTypes.RefKind(target, (adapt.Op & MirRvalue.AdaptInput) != 0),
                target.IsObjectBorrow ? adapt.Place : operands[adapt.Start].Place,
                target),
        };
    }
}
