// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's values on a state (A3 5's val and issued, with E3): what an rvalue, an operand or a Place carries, a root's state, and a borrow's
// parents and Loan, over the one step walk of MirSteps' steps (Prefix), which a store's destination also reads (MirBorrows.Flow), and the
// value a Deref goes through (PointerOf). Must not decide which holders a set reaches or a call's store writes (MirBorrows.Reach).
internal sealed partial class MirBorrows
{
    // val(rv) (A3 5) and the Loans it issues: an operand's, a Ref's, the union of an aggregate's or a value-keeping Cast's operands, an Opaque
    // site's {Φ_E} (R4-M70: the site rests on a recorded error, so a problem only its roots' Loans would give never arises); under the view
    // an Adapt is the rvalue its case selects (none: its operand's value).
    private (int Value, int Own) Value(ulong[] state, in MirRvalue rvalue, int location)
    {
        switch (rvalue.Kind)
        {
            case MirRvalueKind.Use:
            case MirRvalueKind.Adapt:
            case MirRvalueKind.Cast when (MirCastKind)rvalue.Op is MirCastKind.Erasure or MirCastKind.Upcast or MirCastKind.Pointer:
                return (this.Operand(state, rvalue.Start), 0);
            case MirRvalueKind.Ref when (MirRefKind)rvalue.Op != MirRefKind.Raw:
                return this.Issued(state, rvalue.Place, reborrow: false, this.loanAt[location]);
            case MirRvalueKind.Aggregate:
                var value = 0;
                for (var o = rvalue.Start; o < rvalue.Start + rvalue.Count; o++)
                {
                    value = this.sets.Union(value, this.Operand(state, o));
                }

                return (value, 0);
            case MirRvalueKind.Opaque:
                return (this.escapedSet, 0);
            default:
                return (0, 0);
        }
    }

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

        return this.slots[root] >= 0 ? (int)state[this.slots[root]] : 0;
    }

    // val(X) = content^k(σ(root(X))) over its k borrow Derefs, a raw pointer's referent {Φ_E}: the step walk's layer over all its steps.
    private int Read(ulong[] state, MirPlace place)
        => this.Prefix(state, place.Root, this.placeSteps.Walk(this.body, place, reborrow: false, ref this.walk)).Layer;

    // A borrow's value (A3 5 issued, E3): its Loan with the parents of every borrow layer it follows, from the last shared layer on (the
    // step walk's parents).
    private (int Value, int Own) Issued(ulong[] state, MirPlace place, bool reborrow, int loan)
    {
        var parents = this.Prefix(state, place.Root, this.placeSteps.Walk(this.body, place, reborrow, ref this.walk)).Parents;
        return (parents, loan >= 0 ? this.sets.Add(0, this.loanBase + loan) : 0);
    }

    // The one step walk (A3 5, E3) over a Place's first end steps in the walk buffer: val of that prefix (its layer) and the parents a
    // borrow of it follows, from the last shared layer on the values its Derefs go through. Read, Issued and Store's prefix read it.
    private (int Layer, int Parents) Prefix(ulong[] state, int root, int end)
    {
        var (layer, parents) = (this.Root(state, root), 0);
        for (var i = 0; i < end; i++)
        {
            var pointer = this.PointerOf(layer, i);
            (parents, layer) = this.walk[i].Class switch
            {
                MirStepClass.Shared => (pointer, this.Content(state, pointer)),
                MirStepClass.Uniq => (this.sets.Union(parents, pointer), this.Content(state, pointer)),
                MirStepClass.Raw => (this.sets.Union(parents, pointer), pointer), // What is read through it is its value.
                _ => (parents, layer),
            };
        }

        return (layer, parents);
    }

    // The value a Deref step of the walk buffer goes through, on the layer it applies to: a borrow's, that layer; a raw pointer's, {Φ_E}
    // (A5 traces no raw value).
    private int PointerOf(int layer, int step) => this.walk[step].Class == MirStepClass.Raw ? this.escapedSet : layer;
}
