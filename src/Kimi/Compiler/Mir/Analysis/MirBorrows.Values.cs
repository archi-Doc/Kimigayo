// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

// A5's values on a state (A3 5's val and issued, with E3; SPEC 15.6 Addresses, 5.2.2, 5.7): what an rvalue, an operand or a Place carries,
// a root's state, and a borrow's parents and Loan, over the one step walk of MirSteps' steps (Prefix), which a store's destination also
// reads (MirBorrows.Flow), and the value a Deref goes through (PointerOf); a raw Ref issues its address, a `during self` layer's read
// carries what `@raw` of its storage carries (Layered); a Case payload of a holder that holds only calls' results carries nothing where
// the Case's payload Type holds no Loan (Unheld; SPEC 15.6 Holders' Case-payload sentence, the owner's O-383 (b)). Must not decide which
// holders a set reaches or a call's store writes (MirBorrows.Reach), which locals hold only calls' results (MirBorrows.Holders) or which
// Place a local's reference refers to (MirBorrows.Loans).
internal sealed partial class MirBorrows
{
    // val(rv) (A3 5) and the Loans it issues: the union of the operands whose values it is (ValueOperands: an operand's, a value-keeping
    // Cast's operand's, between raw pointer Types, 5.4; none to usize, 5.5; a displacement's pointer's, 5.3; an aggregate's), a Ref's (a
    // raw one's and a Storage's its parents and address), an integer's pointer {Φ_E} (D8), an Opaque site's {Φ_E} (R4-M70: the site
    // rests on a recorded error, so a problem only its roots' Loans would give never arises); under the view an Adapt is the rvalue its
    // case selects (none: its operand's value).
    private (int Value, int Own) Value(ulong[] state, in MirRvalue rvalue, int location)
    {
        var (value, carried) = (0, this.ValueOperands(rvalue));
        for (var o = rvalue.Start; o < rvalue.Start + carried; o++)
        {
            value = this.sets.Union(value, this.Operand(state, o));
        }

        switch (rvalue.Kind)
        {
            case MirRvalueKind.Cast when (MirCastKind)rvalue.Op == MirCastKind.IntToPointer:
                return (this.escapedSet, 0);
            case MirRvalueKind.Ref or MirRvalueKind.Storage when (MirRefKind)rvalue.Op == MirRefKind.Raw:
                return (this.RawAddress(state, rvalue.Place, location), 0);
            case MirRvalueKind.Ref:
                return this.Issued(state, rvalue.Place, reborrow: false, this.loanAt[location]);
            case MirRvalueKind.Aggregate:
                return (this.Indirect(value), 0); // Rule W: an aggregate holding a borrow is not the borrow.
            case MirRvalueKind.BinaryOp when carried > 0:
                return (this.Displaced(value, cases.TypeOf(this.body.Operands[rvalue.Start])), 0); // Δ off a grid (MirBorrows.Puns).
            case MirRvalueKind.Opaque:
                return (this.escapedSet, 0);
            default:
                return (value, 0);
        }
    }

    // The leading operands whose values an rvalue's value is (val, A3 5), the one statement of that rule: its first where it keeps it
    // (Keeps), all of an aggregate's (a Closure's environment included), none of another's. Value unions them on a state and
    // MirBorrows.Holders' Flowing follows them.
    private int ValueOperands(in MirRvalue rvalue) => this.Keeps(rvalue) ? 1 : rvalue.Kind == MirRvalueKind.Aggregate ? rvalue.Count : 0;

    // Whether an rvalue's value is its first operand's (val, A3 5): a Use, an Adapt, a value-keeping Cast (Erasure, Upcast, between raw
    // pointer Types, 5.4) or a raw pointer's displacement (5.3); the may-set (ValueOperands) and the must-set (MirBorrows.Storing) read it.
    private bool Keeps(in MirRvalue rvalue) => rvalue.Kind switch
    {
        MirRvalueKind.Use or MirRvalueKind.Adapt => true,
        MirRvalueKind.Cast => (MirCastKind)rvalue.Op is MirCastKind.Erasure or MirCastKind.Upcast ||
            ((MirCastKind)rvalue.Op == MirCastKind.Pointer && rvalue.Type?.Semantics == SemanticsKind.Raw),
        MirRvalueKind.BinaryOp => (MirBinaryOp)rvalue.Op is MirBinaryOp.Add or MirBinaryOp.Sub &&
            cases.TypeOf(this.body.Operands[rvalue.Start]).Semantics == SemanticsKind.Raw,
        _ => false,
    };

    // An operand's value: a constant's none, a Place's read (a PairAcquire's that Copies; one that Reborrows is its Use's issue, Plan).
    private int Operand(ulong[] state, int index)
        => this.body.Operands[index] is { Kind: not MirOperandKind.Const } operand ? this.Read(state, operand.Place) : 0;

    // A root's state: a holder's, a static's {Φ_E}, none for another local.
    private int Root(ulong[] state, int root)
    {
        if (root < 0)
        {
            return this.escapedSet;
        }

        return this.slots[root] >= 0 ? this.Set(state[this.slots[root]]) : 0;
    }

    // val(X) = content^k(σ(root(X))) over its k Derefs: the step walk's layer over all its steps, read as Layered reads it; none where the
    // Case bound empties it (Unheld).
    private int Read(ulong[] state, MirPlace place)
    {
        if (this.Unheld(place))
        {
            return 0;
        }

        var (layer, parents) = this.Prefix(state, place.Root, this.placeSteps.Walk(place, reborrow: false, ref this.walk));
        return this.Layered(place.Root, place.Projection, layer, parents);
    }

    // The Case bound (SPEC 15.6 Holders' Case-payload sentence; the owner's O-383 (b)): whether a Place is, or lies in, a Case payload
    // whose Type, the Case's in the result's Type as the view instantiates it, holds no Loan (Holds), reached from a local that holds
    // only calls' results (MirBorrows.Holders' Returned) through Case payloads alone (Payload), a payload of a payload included: the
    // result holds none through that Case, so a value Moved or Copied out of the payload carries none, whatever the result holds through
    // another Case. A read through a local's shared reference reads the Place it refers to (MirBorrows.Loans' Referred), so a Copy out of
    // the payload through a shared match Subject is bounded like a Move. A Field or an element of the result or of a payload, and any
    // other holder's parts, carry what the holder carries (O-364 (a)). The walk runs from the Place's last step to its root: a step that
    // is no payload step drops the bound of the steps after it; each reference followed is counted as work. Flowing asks it of an edge's
    // operand and Read of every Place.
    private bool Unheld(MirPlace place)
    {
        var (root, at, bound, hops) = (place.Root, place.Projection, false, 0);
        while (at != 0 && root >= 0)
        {
            var step = this.body.Projections[at];
            if (step is { Kind: MirProjectionKind.Deref, Prefix: 0 } && hops < this.body.Locals.Length && this.Referred(root, out var referent))
            {
                (root, at, hops) = (referent.Root, referent.Projection, hops + 1);
                continue;
            }

            bound = this.Payload(at) && (bound || (step.Kind == MirProjectionKind.Downcast && !this.Holds(cases.TypeOf(new MirPlace(root, at)), out _)));
            at = step.Prefix;
        }

        this.Work += hops;
        return bound && root >= 0 && this.Returned(root);
    }

    // Whether a projection is a Case payload step: a Downcast, the payload Tuple of a Case, or the Field of that Tuple, a payload.
    private bool Payload(int projection)
    {
        var step = this.body.Projections[projection];
        return step.Kind == MirProjectionKind.Downcast ||
            (step.Kind == MirProjectionKind.Field && this.body.Projections[step.Prefix].Kind == MirProjectionKind.Downcast);
    }

    // A raw address's value, a raw Ref's and a Storage's alike (O-92 (a)): the parents of its Place's step walk (a Storage's with the
    // element step, AddressWalk) with the address entry its location issued.
    private int RawAddress(ulong[] state, MirPlace place, int location)
    {
        var count = this.AddressWalk(place, location);
        var parents = this.Interior(this.Prefix(state, place.Root, count).Parents, place, count); // Δ past a raw Deref (MirBorrows.Puns).
        return this.loanAt[location] >= 0 ? this.sets.Add(parents, this.loanBase + this.loanAt[location]) : parents;
    }

    // A borrow's value (A3 5 issued, E3): its Loan with the parents of every borrow layer it follows, from the last shared layer on (the
    // step walk's parents).
    private (int Value, int Own) Issued(ulong[] state, MirPlace place, bool reborrow, int loan)
    {
        var count = this.placeSteps.Walk(place, reborrow, ref this.walk);
        var parents = this.Interior(this.Prefix(state, place.Root, count).Parents, place, count); // Δ past a raw Deref (MirBorrows.Puns).
        return (parents, loan >= 0 ? this.sets.Add(0, this.loanBase + loan) : 0);
    }

    // The one step walk (A3 5, E3) over a Place's first end steps in the walk buffer: val of that prefix (its layer) and the parents a
    // borrow of it follows, from the last shared layer on the values its Derefs go through. Read, Issued and Store's prefix read it.
    private (int Layer, int Parents) Prefix(ulong[] state, int root, int end)
    {
        var (layer, parents) = (this.Root(state, root), 0);
        for (var i = 0; i < end; i++)
        {
            var pointer = this.PointerOf(root, layer, parents, i);
            (parents, layer) = this.walk[i].Class switch
            {
                MirStepClass.Shared => (pointer, this.Content(state, pointer)),
                MirStepClass.Raw => (pointer, this.Indirect(this.Content(state, pointer))), // A fresh anchor (5.2.2); no reference (rule W).
                MirStepClass.Uniq => (this.sets.Union(parents, pointer), this.Content(state, pointer)),
                _ => (parents, this.Indirect(layer)), // Rule W: a part of a holder is no Copy of a borrow it holds.
            };
        }

        return (layer, parents);
    }

    // The value a Deref step of the walk buffer goes through, on the layer and parents of the prefix it applies to: a borrow's, that layer; a
    // raw pointer's, the prefix's read (Layered): the addresses and Loans it carries.
    private int PointerOf(int root, int layer, int parents, int step)
        => this.walk[step].Class == MirStepClass.Raw ? this.Layered(root, this.walk[step].Prefix, layer, parents) : layer;

    // A Place's read on its layer and parents (SPEC 5.7 Reading a Field; B §5.4): a `during self` layer's adds what `@raw` of the layer's
    // storage carries, the parents and its own-step entry; another Place's is its layer.
    private int Layered(int root, int projection, int layer, int parents)
    {
        var entry = this.OwnStepAt(root, projection);
        return entry < 0 ? layer : this.sets.Union(layer, this.Indirect(this.sets.Add(parents, this.loanBase + entry)));
    }
}
