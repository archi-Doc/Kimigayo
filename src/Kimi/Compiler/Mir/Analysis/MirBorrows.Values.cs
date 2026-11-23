// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's values on a state (A3 5's val and issued, with E3; SPEC 15.6 Addresses, 5.2.2, 5.7): what an rvalue, an operand or a Place carries,
// a root's state, and a borrow's parents and Loan, over the one step walk of MirSteps' steps (Prefix), which a store's destination also
// reads (MirBorrows.Flow), and the value a Deref goes through (PointerOf); a raw Ref issues its address, a `during self` layer's read
// carries what `@raw` of its storage carries (Layered); a Case payload of a holder that holds only calls' results carries nothing where
// the Case's payload Type holds no Loan but what a call's write source stored there, its holder's write slot's (Sources; SPEC 15.6
// Holders' Case-payload sentence, the owner's O-383 (b); the Middle's phase-6 decision 2), and so does a read through such a holder's
// reference where the referent Type holds no Loan (the referent bound, the owner's O-437 (b)). A borrow input's referent (Referent): the
// value its last borrow or raw Deref goes through, by a Place operand's own steps or the Places its local's referring stores lend; none
// where it passes no Deref or a store lends no Place. MirBorrows.Calls' Retained keeps what a Value-less row's reads carry less the
// input's own Loans and addresses that the referent's read does not carry (SPEC 15.6.4, 15.6.3; D-935). Must not decide which
// holders a set reaches or a call's store writes (MirBorrows.Reach), which locals hold only calls' results (MirBorrows.Holders) or which
// Places a local's reference refers to (MirBorrows.Loans).
internal sealed partial class MirBorrows
{
    private const byte ReadsPlace = 1; // Sources: a Place the read reads is one no bound (the Case or the referent bound) empties.
    private const byte ReadsHolder = 2; // Sources: such a Place is a holder's or a static's.
    private const byte ReadsWritten = 4; // Sources: such a Place is one a bound empties but for a call's write source.

    private readonly List<(int Root, int At, bool Bound)> following = []; // Sources' worklist: the walks still to make.
    private readonly List<int> writtenRoots = []; // Sources: the roots whose write slots the read reads (ReadsWritten).
    private readonly List<int> keptRoots = []; // The kept answers' write-slot roots, each answer's run ended by -1 (followed).
    private int[] followedBy = []; // By local and incoming bound: the Sources walk that followed its reference.
    private int[] followed = []; // By local and incoming bound: 1 + the Sources answer of following its reference in the low 4 bits, with its roots' run in keptRoots above; 0 for none yet.
    private int sourceWalks = 1; // The Sources walks that followed a reference, and one.

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
    // An operand of no Type, a derived temp's read (PLAN G1073, MirCases.KnownTypeOf), is no raw pointer (rule W).
    private bool Keeps(in MirRvalue rvalue) => rvalue.Kind switch
    {
        MirRvalueKind.Use or MirRvalueKind.Adapt => true,
        MirRvalueKind.Cast => (MirCastKind)rvalue.Op is MirCastKind.Erasure or MirCastKind.Upcast ||
            ((MirCastKind)rvalue.Op == MirCastKind.Pointer && rvalue.Type is { IsPointer: true }),
        MirRvalueKind.BinaryOp => (MirBinaryOp)rvalue.Op is MirBinaryOp.Add or MirBinaryOp.Sub &&
            cases.KnownTypeOf(this.body.Operands[rvalue.Start]) is { IsPointer: true },
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

    // val(X) = content^k(σ(root(X))) over its k Derefs: the step walk's layer over all its steps, read as Layered reads it; where no
    // Place the read reads (Sources) is a holder's or a static's, what the write slots of the roots whose payloads it reads carry: the
    // Case bound or the referent bound empties the others but for what a call's write source stored there (MirBorrows.Holders' WriteSlots), and a local that
    // is no holder carries nothing at every point (MirBorrows.Holders' Flowing).
    private int Read(ulong[] state, MirPlace place)
    {
        if ((this.Sources(place) & ReadsHolder) == 0)
        {
            var written = 0;
            foreach (var root in this.writtenRoots)
            {
                written = this.sets.Union(written, this.Set(state[this.writeSlots[root]]));
            }

            return written;
        }

        var (layer, parents) = this.Prefix(state, place.Root, this.placeSteps.Walk(place, reborrow: false, ref this.walk));
        return this.Layered(place.Root, place.Projection, layer, parents);
    }

    // Whether a bound (the Case or the referent bound) empties every Place a read reads (Sources): MirBorrows.Holders' Flowing asks it of an edge's operand.
    private bool Unheld(MirPlace place) => (this.Sources(place) & ReadsPlace) == 0;

    // Forgets the answers of following references (Sources), at a view's start and once Flowing has decided its holders, which the
    // answers read (MirBorrows.Holders).
    private void Unfollow()
    {
        MirDataflow.Grow(ref this.followed, 2 * this.body.Locals.Length);
        MirDataflow.Grow(ref this.followedBy, 2 * this.body.Locals.Length);
        Array.Clear(this.followed, 0, 2 * this.body.Locals.Length);
        this.keptRoots.Clear();
    }

    // What a read of a Place reads, less the Places the Case and referent bounds empty: ReadsPlace where one remains, with ReadsHolder where one is
    // a holder's or a static's (once Flowing has decided the holders). The Case bound (SPEC 15.6
    // Holders' Case-payload sentence; the owner's O-383 (b)): a Place that is, or lies in, a Case payload whose Type, the Case's in the
    // result's Type as the view instantiates it, holds no Loan (Holds), reached from a local that holds only calls' results
    // (MirBorrows.Holders' Returned) through Case payloads alone (Payload), a payload of a payload included, carries nothing: a
    // Loan-free Case payload is bounded by its Case's Type (O-383 (b)): it carries no Loan whatever reached it: a part an input holds by
    // value, a value read through a borrowed input, or reference bits written through a raw pointer (the rows rvResPartBad, xchgFieldD,
    // xchgOldD); so a value Moved or Copied out of the payload carries none, whatever the result holds through another Case; where a
    // call's write source may store in the local, it reads the local's write slot instead (Ends). The referent bound (the owner's
    // O-437 (b)), beside it: a read through the reference such a local holds whole or as a part (a Deref of the root's own reference, or
    // of a reference Field, Tuple element or Case payload of it, HeldReference: `w.r@follow`, `s.wrap().r@follow`) carries only the
    // Loans its referent Type can hold, so where that Type holds no Loan (Holds) it is bounded as a Loan-free payload is, a part of
    // the referent read below the Deref included (arView's `inner[0]`, c84's `res.v`); an element that stores a reference keeps its
    // Loan (its Type holds one). It starts only on the read's own root, before any reference is followed: a read through a reference
    // the body forms (Follows), one formed through a call's reference included (`let q = r@follow@ref`), keeps O-364 (a). A read through a local's shared reference reads the Places it refers to (MirBorrows.Loans' Referred), one per
    // referring store: a Ref's Place, or what the local a whole Move or Copy reads refers to; so a Copy out of the payload through a
    // shared match Subject is bounded like a Move, and one through a reference stored more than once where every Place it refers to
    // is. A Field or an element of the result or of a payload, and any other holder's parts, carry what the holder carries (O-364 (a)).
    // Each walk runs from a Place's last step to its root: a step that is no payload step drops the bound of the steps after it. A
    // reference is followed once per walk and incoming bound, so a cycle of Copies ends; each store followed is counted as work. What
    // follows the first reference a read follows depends on that reference and its incoming bound alone, so it is kept (followed)
    // and a later read through the same reference with the same bound reuses it, linear in the reads and the stores; an answer that
    // reads write slots keeps their roots with it (keptRoots), which a reuse adds to the read's (Kept).
    private byte Sources(MirPlace place)
    {
        var (root, at, bound, reads, first) = (place.Root, place.Projection, false, (byte)0, -1);
        this.writtenRoots.Clear();
        while (true)
        {
            var hopped = false;
            while (at != 0 && root >= 0 && !(hopped = this.Follows(root, at, bound, ref reads)))
            {
                var step = this.body.Projections[at];
                var referent = first < 0 && step.Kind == MirProjectionKind.Deref && this.HeldReference(new MirPlace(root, step.Prefix));
                bound = (this.Payload(at) || referent) && (bound || ((step.Kind == MirProjectionKind.Downcast || referent) && !this.Holds(cases.TypeOf(new MirPlace(root, at)), out _)));
                at = referent && bound ? 0 : step.Prefix; // The held reference's Field and payload steps keep the bound to its root (Ends).
            }

            first = hopped && first < 0 ? (2 * root) + (bound ? 1 : 0) : first;
            reads |= hopped ? (byte)0 : this.Ends(root, bound);

            if (this.following.Count == 0)
            {
                break;
            }

            (root, at, bound) = this.following[^1];
            this.following.RemoveAt(this.following.Count - 1);
        }

        if (first >= 0 && this.followed[first] == 0)
        {
            this.followed[first] = (this.keptRoots.Count << 4) | (reads + 1);
            if ((reads & ReadsWritten) != 0)
            {
                this.keptRoots.AddRange(this.writtenRoots);
                this.keptRoots.Add(-1);
            }
        }

        this.sourceWalks += first >= 0 ? 1 : 0;
        return reads;
    }

    // A kept answer of following a reference with an incoming bound (Sources): its reads, with the roots whose write slots it reads
    // added to the read's (writtenRoots); none for no answer yet.
    private byte Kept(int key)
    {
        var reads = this.followed[key] > 0 ? (byte)((this.followed[key] & 15) - 1) : (byte)0;
        for (var r = this.followed[key] >> 4; (reads & ReadsWritten) != 0 && this.keptRoots[r] >= 0; r++)
        {
            this.writtenRoots.Add(this.keptRoots[r]);
        }

        return reads;
    }

    // What a walk that ends at a root reads: nothing where a bound (the Case or the referent bound) empties the Place, but the root's write slot where a call's
    // write source may store in it (MirBorrows.Holders' written; before the slots are numbered, that it is read); else the Place, a
    // holder's or a static's.
    private byte Ends(int root, bool bound)
    {
        if (!(bound && root >= 0 && this.Returned(root)))
        {
            return root < 0 || this.slots[root] >= 0 ? (byte)(ReadsPlace | ReadsHolder) : ReadsPlace;
        }

        if (this.written[root] && this.writeCount > 0)
        {
            this.writtenRoots.Add(root);
        }

        return this.written[root] ? (byte)(ReadsPlace | ReadsWritten) : (byte)0;
    }

    // Whether a walk of Sources at a local's Deref follows the local's reference (MirBorrows.Loans' Referred): with its kept answer
    // where a read followed it with that bound before (Sources), else queueing a walk per referring store unless this Sources walk
    // followed the local with that bound already: from a Ref's Place on, or at the same Deref of the local whose whole value the store
    // Moves or Copies.
    private bool Follows(int root, int at, bool bound, ref byte reads)
    {
        if (this.body.Projections[at] is not { Kind: MirProjectionKind.Deref, Prefix: 0 } || !this.Referred(root, out var store))
        {
            return false;
        }

        var key = (2 * root) + (bound ? 1 : 0);
        reads |= this.Kept(key);
        for (; store >= 0 && this.followed[key] == 0 && this.followedBy[key] != this.sourceWalks; store = this.referStores[(2 * store) + 1])
        {
            var assigned = this.body.Statements[this.referStores[2 * store]].Rvalue;
            var rvalue = assigned.Kind == MirRvalueKind.Adapt ? cases.Adapt(assigned, out _) : assigned;
            this.following.Add(rvalue.Kind == MirRvalueKind.Ref
                ? (rvalue.Place.Root, rvalue.Place.Projection, bound) : (this.body.Operands[rvalue.Start].Place.Root, at, bound));
            this.Work++;
        }

        this.followedBy[key] = this.sourceWalks;
        return true;
    }

    // Whether a Place is a reference its root holds whole or as a part (the root itself, or a path of Field and payload steps from it: a
    // Field, a Tuple element or a Case payload; no Deref or Index on the way), where the referent bound may start (Sources; the owner's
    // O-437 (b): a reference the call returns, as a part of its result too, MirBorrows.Holders' Whole).
    private bool HeldReference(MirPlace place)
    {
        for (var at = place.Projection; at != 0; at = this.body.Projections[at].Prefix)
        {
            if (this.body.Projections[at].Kind is not (MirProjectionKind.Field or MirProjectionKind.Downcast))
            {
                return false;
            }
        }

        return cases.TypeOf(place).SafeReferent is not null;
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

    // A borrow input's referent (SPEC 15.6.4's read through the argument, 15.6.3): the value its last borrow or raw Deref goes through on
    // the state, by a Place operand's own steps (a Drop's destroyed Place), or for a whole local by the Places the Loans of its referring
    // stores lend (MirBorrows.Loans' Referring), joined; -1 for none, where it passes no Deref or a store into it lends no Place (a stored
    // reference's Copy, a parameter, a call's result, a borrow past a shared Deref). Its own stores, not the Loans in its value: a
    // value built through a stored Reborrow also carries the children that name the intermediate holder, and a Drop's receiver has none.
    private int Referent(ulong[] state, in MirOperand operand)
    {
        if (operand.Kind == MirOperandKind.Const)
        {
            return -1;
        }

        var place = operand.Place;
        if (place.Projection != 0)
        {
            return this.Referent(state, place.Root, this.placeSteps.Walk(place, reborrow: false, ref this.walk));
        }

        if (place.Root < 0 || !this.Referring(place.Root, out var store) || store < 0)
        {
            return -1;
        }

        var referent = 0;
        for (; store >= 0; store = this.referStores[(2 * store) + 1])
        {
            var loan = this.loanAt[this.referStores[2 * store]];
            if (loan < 0)
            {
                return -1;
            }

            var lent = this.loans[loan];
            MirDataflow.Grow(ref this.walk, lent.Count);
            this.loanSteps.AsSpan(lent.Start, lent.Count).CopyTo(this.walk);
            var through = this.Referent(state, lent.Place.Root, lent.Count);
            if (through < 0)
            {
                return -1;
            }

            referent = this.sets.Union(referent, through);
        }

        return referent;
    }

    // Over the walk buffer's first `count` steps from a root, the value the last borrow or raw Deref goes through (PointerOf on the
    // prefix's layer and parents), -1 for none.
    private int Referent(ulong[] state, int root, int count)
    {
        var last = this.LastDeref(count);
        if (last < 0)
        {
            return -1;
        }

        var (layer, parents) = this.Prefix(state, root, last);
        return this.PointerOf(root, layer, parents, last);
    }

    // A Place's read on its layer and parents (SPEC 5.7 Reading a Field; B §5.4): a `during self` layer's, or a Tuple's or fixed array's
    // enclosing one inline (read element by element, a Copy and a Move alike), adds what `@raw` of the layer's storage carries, the
    // parents and its own-step entry; another Place's is its layer.
    // R-C (SPEC 15.6 Holders, 15.6 Addresses): the read carries its Place's one own-step row (MirBorrows.Addresses: the owner's entry at
    // its exact lent position, the base's past a dereference, or none) with its parents whole; an Unplaced one carries its layer and is the
    // location's limit where a borrow of the view lends a layer's or enclosing Place (PQ-10, narrowed; MirBorrows.Locations records it).
    private int Layered(int root, int projection, int layer, int parents)
    {
        var entry = this.OwnStepAt(root, projection);
        this.limit |= entry == Unplaced && this.replaying && this.tabling;
        return entry < 0 ? layer : this.sets.Union(layer, this.Indirect(this.sets.Add(parents, this.loanBase + entry)));
    }
}
