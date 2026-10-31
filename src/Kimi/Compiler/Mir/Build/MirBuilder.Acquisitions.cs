// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Acquisitions (SPEC 3.5, 10.2, 13.5.6, 15.1.5): a Place as its consumer's acquisition and adaptation take it (Acquire), by the pair
// layer's case (ByCase), and the one borrow rule (Ref, Lent), each formed in a temp or the place given (Temporary, Borrow); a Place in an
// Opaque temp is lent by none (Unlent; DECISIONS R4-M149).
internal sealed partial class MirBuilder
{
    // The body's Opaque temps (DECISIONS R4-M149): the temps the builder holds an Opaque value in for a consumer, the derived value of a
    // node Binding rejected (Value) and an unsupported or derived form's value (Unsupported); a place a consumer gave is none.
    private readonly HashSet<int> opaqueTemps = [];

    // A Place as an acquisition and an adaptation take it: Copied, moved, acquired by its pair layer's case (ByCase; a PairAcquire is the
    // whole rvalue of an Assign to the local given or a temp) or, a stored exclusive reference, Reborrowed in its own mode (SPEC 15.1.5).
    // SPEC 10.2: adapted, the Scalar its reference layers end in, Copied; a Closure or
    // function item erased to its Function Type; else a reference, of the expected Type or the one its mode forms, to the Place itself (a
    // shared or exclusive borrow), to the referent its layers reach (ReferenceRead), to an object handle's payload or to the base the
    // expected Type names. An implicit exclusive borrow of a call's input is two-phase: reserved here, activated at the Call (SPEC 15.6.7).
    // SPEC 3.8, 14.10.3 (DECISIONS R4-M62, R5-M38): any acquisition takes the Never value as it is, which fits any operand position; a
    // named Place of Type Never that Binding acquires with no adaptation, a Copy or a Move, is acquired so into a fresh Never temp and gives
    // that Never value, so whatever takes it places nothing; an adapted one (a borrow) is built as Binding publishes it and keeps its Loan.
    private MirOperand Acquire(HirAdjust adjust, MirPlace place, BoundType? expected, bool input, Koto node, MirPlace? into = null)
    {
        if (this.IsNeverValue(place))
        {
            return MirOperand.Move(place);
        }

        if (!this.IsNever(place) || adjust.Adaptation != HirAdaptation.None)
        {
            return this.Acquired(adjust, place, expected, input, node, into);
        }

        var value = this.Hold(BoundType.Never, node);
        var acquired = this.body.AddOperand(this.Acquired(adjust, place, expected, input, node));
        this.body.Push(this.current, MirStatement.Assign(value, MirRvalue.Use(acquired), this.Info(node)));
        return MirOperand.Move(this.Made(value));
    }

    // A Place other than the Never value as an acquisition and an adaptation take it (Acquire).
    private MirOperand Acquired(HirAdjust adjust, MirPlace place, BoundType? expected, bool input, Koto node, MirPlace? into = null)
    {
        var adaptation = adjust.Adaptation;
        var type = this.body.TypeOf(place) ?? BoundType.Unit;
        var target = expected?.Components is [var referent] ? referent : null;
        switch (adaptation)
        {
            case HirAdaptation.None when adjust.Acquire == HirAcquire.ByCase:
                if (this.ByCase(place, type, input) is not { } formed)
                {
                    return MirOperand.Copy(place);
                }

                // A PairAcquire, a marked Use, goes only into the whole Place of a local; a Reborrow into any place of its Type.
                var destination = formed.Kind == MirRvalueKind.Use && into is not { Projection: 0, Root: >= 0 } ? null : into;
                return this.Temporary(type, formed, node, destination);
            case HirAdaptation.None:
                return adjust.Acquire switch
                {
                    HirAcquire.Reborrow => this.Reborrowed(place, type, expected, input, node, into),
                    HirAcquire.Copy => MirOperand.Copy(place),
                    _ => MirOperand.Move(place),
                };
            case HirAdaptation.ReferentRead:
                return MirOperand.Copy(this.Follow(place, 16, expected));
            case HirAdaptation.Erasure: // SPEC 7.7.4: of the value its ordinary acquisition gives.
                var source = this.Acquire(adjust with { Adaptation = HirAdaptation.None }, place, null, input, node);
                return this.Temporary(
                    expected!,
                    MirRvalue.Cast(MirCastKind.Erasure, this.body.AddOperand(source), expected!) with { Proven = true },
                    node,
                    into);
            case HirAdaptation.ReferenceRead:
                place = this.Follow(place, 16, target);
                break;
            case HirAdaptation.PayloadProjection:
                place = this.body.Project(place, MirProjectionKind.Deref, 0);
                break;
        }

        var reference = expected ?? compilation.Types.Reference(
            adaptation == HirAdaptation.ExclusiveBorrow ? SemanticsKind.Uniq : SemanticsKind.Ref,
            this.body.TypeOf(place) ?? BoundType.Unit);
        return this.Temporary(reference, this.Ref(place, reference, input), node, into);
    }

    // SPEC 15.1.5, 15.6.3 (DECISIONS BU-27): a stored exclusive reference Reborrowed in its own mode, of the Type its consumer expects,
    // which is Binding's name for the Reborrow (a receiver's, an argument's or a payload's AdaptedType, a Subject's borrow, a destination's
    // Type) and a same-mode reference to the stored referent; as Acquired types every other borrow by `expected`. Any other expected Type is
    // a broken contract: InternalInvariant_Kd, the value a derived Opaque of the stored Type in an Opaque temp, whose site names only the
    // Place's root, built already (R5-M63, R5-M84).
    private MirOperand Reborrowed(MirPlace place, BoundType stored, BoundType? expected, bool input, Koto node, MirPlace? into)
    {
        if (expected is not { Kind: BoundTypeKind.Semantics, Components: [var named] } || expected.Semantics != stored.Semantics ||
            stored.Components is not [var referent] || !MirTypes.Agree(named, referent))
        {
            this.program.Report(SourceRef.Of(node), DiagnosticCode.InternalInvariant_Kd);
            var temp = this.Temp(stored, node);
            this.opaqueTemps.Add(temp.Root);
            this.roots.Clear();
            this.roots.Add(place.Root);
            this.Site(node, temp, Completes(node));
            return MirOperand.Move(temp);
        }

        return this.Temporary(expected, this.Ref(place, expected, input), node, into);
    }

    // SPEC 8.14, 15.1.5: the rvalue a Place's acquisition by its pair layer's case forms where its Type decides the case: a PairAcquire
    // marked with the Reborrow its exclusive case makes (A3 E13 (4)) where the Type keeps a binder of the body, else a Reborrow of a Type
    // that is exclusive; none where the acquisition Copies the Place. Acquire and a default's copy (Inline) take it.
    private MirRvalue? ByCase(MirPlace place, BoundType type, bool input)
    {
        if (MirCases.Binder(this.body, type) is var binder and >= 0)
        {
            return this.Unlent(place, type) ??
                MirRvalue.Acquire(this.body.AddOperand(MirOperand.PairAcquire(binder, place)), input ? MirRefKind.TwoPhaseUniq : MirRefKind.Uniq);
        }

        return MirTypes.IsExclusive(type) ? this.Ref(place, type, input) : null;
    }

    // SPEC 13.5.6, 5.4: a reference of a Type to a Place, the one rule of every borrow: of the Place it lends (Lent), in its Type's mode
    // (MirTypes.RefKind; an exclusive reservation, a call's input or an update's destination, is two-phase); none of an Opaque temp.
    private MirRvalue Ref(MirPlace place, BoundType reference, bool input)
        => this.Unlent(place, reference) ?? MirRvalue.Ref(MirTypes.RefKind(reference, input), this.Lent(place, reference), reference);

    // SPEC 23.1.2, 3.6 (DECISIONS R4-M149): an Opaque temp is no storage, so an rvalue of a Type that would lend a Place in one (a Ref, a
    // pair-case acquisition, a case adaptation) is its consumer's derived value instead: an Opaque located at the temp's node whose site
    // names only the temp, built already; null for a Place in other storage.
    private MirRvalue? Unlent(MirPlace place, BoundType type) => this.opaqueTemps.Contains(place.Root)
        ? MirRvalue.Opaque(type, this.body.AddOpaque(this.body.Locals[place.Root].Source.Syntax, [place.Root])) : null;

    // SPEC 13.5.6, 6.2.2: the Place a reference of a Type to a Place lends: the Place's layer whose Type is the reference's referent (an
    // object borrow's payload, a reborrow's referent) or its base that is (a base view).
    private MirPlace Lent(MirPlace place, BoundType reference)
    {
        var referent = reference.Components is [var target] ? target : null;
        var depth = 0;
        place = referent is null ? place : this.Follow(place, 16, referent);
        for (var layer = this.body.TypeOf(place); referent is not null && layer is not null && !MirTypes.Agree(layer, referent); layer = AdtDef.Base(layer))
        {
            depth = AdtDef.Base(layer) is null ? 0 : depth + 1;
        }

        return depth == 0 ? place : this.body.Project(place, MirProjectionKind.Base, depth);
    }

    // An rvalue in a new temp of its Type, or in the place given where that has its Type, moved.
    private MirOperand Temporary(BoundType type, in MirRvalue rvalue, Koto node, MirPlace? into = null)
    {
        var temp = this.Temp(type, node, into);
        this.Set(temp, rvalue, node);
        return MirOperand.Move(temp);
    }

    // A borrow of a Place in a new temp of the reference a Semantics kind forms to it (uniq: exclusive; ref, objref: shared), moved: a
    // synthesized call's input, so an exclusive one is two-phase (SPEC 15.6.7).
    private MirOperand Borrow(SemanticsKind semantics, MirPlace place, Koto node)
    {
        var view = compilation.Types.Reference(semantics, this.body.TypeOf(place) ?? BoundType.Unit);
        var temp = this.Temp(view, node);
        this.Set(temp, this.Ref(place, view, input: true), node);
        return MirOperand.Move(temp);
    }
}
