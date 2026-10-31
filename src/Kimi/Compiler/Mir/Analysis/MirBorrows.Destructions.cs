// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;

namespace Kimi.Compiler.Mir;

// A5's destruction composition (SPEC 15.6.4, 16.3.1-16.3.3, 16.4; DECISIONS BU-47, R4-M150): a destruction retains as a call of each
// `drop` it runs, whose `self` is a `uniq/Self` receiver referring to the destroyed part. At a Drop, each part A2 publishes it destroys in
// the view (MirDropTable.Destroyed) composes the rows of its case Type (Rows): the destruction walk classified once by MirTypes
// (MirTypes.Destroys), a declared drop Binding's drop rows (TyCtxt.Retains over the destructor and the part's Synthesized Subst), an
// erased destruction TyCtxt.RetainsErasedDestruction, a Sealed handle's payload walked on, a release nothing. At a published destroying
// call (SPEC 4.9.6: TyCtxt.Published's Destroys), after the call's own stores, the rows of the Type below the destroyed parameter
// (MirCases.Below), through the call's own reading of that argument: a borrow's value, a by-value argument's operand Place.
// Input: the state before the Drop, its terminator and block, whether the checks replay runs; the rows memo (MirBorrows.Accesses'
// destructions, per definition and program version).
// Output: the Drop's entries: through each row's outputs, into each holder W reaches from the receiver's seed, the record of the storage
// it is (Own at the seed, the PastEntry past it, All for E, {Φ_E} besides for an untied store), joined; in the checks replay, the
// store's two undecided facts as a Call's (R4-M147, R4-M153), a row's CallRetention.Limit as a call's (R4-M51), and a Value row at the
// receiver a broken contract (InternalInvariant_Kd at the Drop, BU-47).
// Invariants: the receiver is one seed shared by every drop of the destruction: an inline Place's root holder (E for a static), whole
// for a whole root at depth 0, or past a last borrow or raw Deref the pointer's value (Store's destination); Content of the receiver is
// the read through the seed, Chain its closure, Value none (it has no Loan; no Loan is issued, R4-M150 rejected A1); every row reads the
// state before the Drop (one application), so 16.3.2's order changes no row; a seed holder that holds nothing or only {Φ_E} composes
// nothing. Staging (design §9, removal unit U1c): an abstract part not proven Copy composes nothing at a Drop.
// Must not choose a row (Binding's), classify a part (MirTypes'), read A2's verdict rows (MirDropTable's) or derive retention.
internal sealed partial class MirBorrows
{
    private readonly List<(CallRetention Retention, bool Own)> destroyingRows = []; // The rows memo's ranges (Rows), cleared with it.
    private readonly List<(BoundType Type, int Depth)> destroyedParts = []; // A Drop's parts in the view (A2's), scratch.
    private bool brokenDrop; // Whether the replay's Drop at the location has a Value row at its receiver (BU-47), read and reset by Check.
    private int dropContent; // The receiver's content and chain at the Drop (Seed), the chain read once when a row needs it.
    private int dropChain;
    private int dropSeed; // The seed holder, -1 for a pointer seed (dropPointer).
    private int dropPointer;
    private bool dropWhole;
    private bool seedWhole; // Whether the seed holder is a whole root (Destination's whole).

    // A Drop (SPEC 15.6.4): the rows of each part A2 publishes it destroys, applied through the receiver's seed on the state before it.
    private void ComposeDrop(ulong[] state, in MirTerminator drop, int block, bool checks)
    {
        this.destroyedParts.Clear();
        var parts = new Parts(this);
        this.program.Drops.Destroyed(this.body, block, this.view, cases.Type(this.body.TypeOf(drop.Place)!), flow, ref parts);
        if (this.destroyedParts.Count == 0 || !this.Seed(state, this.Destination(state, drop.Place)))
        {
            return;
        }

        foreach (var (type, depth) in this.destroyedParts)
        {
            this.Compose(state, drop.Place.Root, type, depth, checks);
        }
    }

    // A published destroying call (SPEC 4.9.6, 15.6.4; rule 3): the rows of the Type below its destroyed parameter, read as the call reads
    // that argument on the state before it: a borrow's value (refs(S)), a by-value argument's operand Place (insertOrReplace's key).
    private void ComposeCleanup(ulong[] state, in MirTerminator call, bool checks)
    {
        if (call.Callee is not { Kind: MirCalleeKind.Fn, Arg: 0, Symbol: { } symbol } || types.Published(symbol) is not { Destroys: >= 0 } row ||
            cases.Below(symbol, call.Callee.Subst, row.Destroys) is not { } type)
        {
            return;
        }

        var position = MirCallee.Position(symbol, row.Destroys);
        var operand = this.body.Operands[call.Start + position];
        var at = Borrows(this.TypeOf(operand)) ? (false, -1, false, this.argValues[position], 0)
            : operand.Kind == MirOperandKind.Const ? (true, -1, false, 0, 0) : this.Destination(state, operand.Place);
        if (this.Seed(state, at))
        {
            this.Compose(state, operand.Kind == MirOperandKind.Const ? -1 : operand.Place.Root, type, 0, checks);
        }
    }

    // The receiver's seed (rule 2): an inline Place's holder or a pointer's value; false where there is no holder, or one whose drops can
    // store only E's own content (it holds nothing or only {Φ_E}: the skip).
    private bool Seed(ulong[] state, (bool Inline, int Slot, bool Whole, int Pointer, int Through) at)
    {
        if (at.Inline && (at.Slot < 0 || (int)state[at.Slot] is 0 || (int)state[at.Slot] == this.escapedSet))
        {
            return false;
        }

        (this.dropSeed, this.dropPointer, this.dropChain, this.seedWhole) = (at.Inline ? at.Slot : -1, at.Pointer, -1, at.Whole);
        return true;
    }

    // The rows of one destroyed part at its depth below the Place, each applied through the seed.
    private void Compose(ulong[] state, int root, BoundType type, int depth, bool checks)
    {
        var (start, count) = this.Rows(type);
        for (var r = start; r < start + count; r++)
        {
            var (retention, own) = this.destroyingRows[r];
            this.dropWhole = this.dropSeed >= 0 && this.seedWhole && depth == 0 && own;
            this.Apply(state, root, type, retention, checks);
        }
    }

    // One row of a destruction: its Limit as a call's (R4-M51), else each output's store from the seed (W) by its records.
    private void Apply(ulong[] state, int root, BoundType part, CallRetention retention, bool checks)
    {
        if (retention.IsLimit)
        {
            this.deep |= checks;
            return;
        }

        this.dropContent = this.dropSeed >= 0 ? this.ReadThrough(state, this.dropSeed, this.dropWhole) : this.Content(state, this.dropPointer);
        foreach (var written in retention.Writes)
        {
            if (this.dropSeed >= 0)
            {
                this.targets.Clear();
                this.targets.Add(this.dropSeed);
                _ = this.Walking();
                this.Written(this.dropSeed, this.dropSeed == Escaped || root < 0 ? MirWrites.None : this.writable[root], ViaRoot, null);
            }
            else
            {
                _ = this.Referents(this.dropPointer);
                this.Write(this.dropPointer, ViaRoot);
            }

            var (limited, deepHolder) = this.Passes(state);
            this.limit |= limited && checks;
            this.deep |= deepHolder && checks;
            for (var t = 0; t < this.targets.Count; t++)
            {
                var target = this.targets[t];
                var via = this.writingVia[target];
                var own = (via & ViaRoot) != 0 ? written.Own : null;
                var past = target == Escaped ? written.All : (via & ViaPast) != 0 ? this.PastEntry(written, target) : null;
                var value = this.sets.Union(this.Receiver(state, own, checks), this.Receiver(state, past, checks));
                value = (own ?? past)!.Untied ? this.sets.Union(value, this.escapedSet) : value;
                this.AddSlot(target, this.Arrive(target, part, false, 0, value), 0, Joined, issue: false);
            }
        }
    }

    // What one record of a destruction stores: its receiver's (input 0) Content, the read through the seed, and Chain, its closure; a
    // Value row or another input is a broken contract (BU-47: the receiver publishes no Value row).
    private int Receiver(ulong[] state, StorageRetention? record, bool checks)
    {
        var value = 0;
        for (var i = 0; record is not null && i < record.Kinds.Length; i++)
        {
            var kind = record.Kinds[i];
            if (kind == RetentionKind.None)
            {
                continue;
            }

            if (i > 0 || (kind & RetentionKind.Value) != 0)
            {
                this.brokenDrop |= checks;
                continue;
            }

            value = (kind & RetentionKind.Content) != 0 ? this.sets.Union(value, this.dropContent) : value;
            value = (kind & RetentionKind.Chain) != 0 ? this.sets.Union(value, this.DropChain(state)) : value;
        }

        return value;
    }

    // The receiver's chain (SPEC 15.6.4; R4-M138): the closure from the seed, read once per Drop.
    private int DropChain(ulong[] state)
    {
        if (this.dropChain < 0)
        {
            if (this.dropSeed >= 0)
            {
                this.Closing();
                this.Queued(this.dropSeed, this.dropWhole);
                this.dropChain = this.Closure(state);
            }
            else
            {
                this.dropChain = this.Chain(state, this.dropPointer);
            }
        }

        return this.dropChain;
    }

    // The rows of a destroyed case Type (rule 1): its range in destroyingRows, memoized with its observation (MirBorrows.Accesses).
    private (int Start, int Count) Rows(BoundType type)
    {
        ref readonly var entry = ref this.Destructed(type);
        return (entry.Start, entry.Count);
    }

    // The rows of a destroyed Type into destroyingRows (rule 1): the destruction walk, each part classified once (MirTypes.Destroys): a
    // declared drop's rows, an erased destruction's, a Sealed payload walked on; a release nothing; an abstract part not proven Copy
    // nothing (the staging, design §9, removal unit U1c); past the walk's bound nothing (the access is Unclassified there). Own marks
    // the Type's own layer (depth 0), whose receiver read is whole where the Place is.
    private void Collect(BoundType type)
    {
        this.entered.Clear();
        var rows = new Collecting(this, types);
        MirTypes.Destruction(type, ref rows);
    }

    // A2's destroyed parts of a Drop (MirDropTable.Destroyed), each listed; a part with no Type (one Binding rejected) composes nothing.
    private readonly struct Parts(MirBorrows owner) : IMirDropped
    {
        public void Part(BoundType? type, int depth)
        {
            if (type is not null)
            {
                owner.destroyedParts.Add((type, depth));
            }
        }
    }

    // Collect's visitor: each part's rows by its classification.
    private readonly struct Collecting(MirBorrows owner, TyCtxt types) : IMirDestruction
    {
        public bool First(BoundType type, int depth)
            => MirDepths.Enter(ref CollectionsMarshal.GetValueRefOrAddDefault(owner.entered, type, out _), depth);

        public bool Visit(BoundType type, int depth)
        {
            var definition = owner.body.Key.Decl;
            switch (MirTypes.Destroys(type, types, definition))
            {
                case MirDestroyedPart.Drop when AdtDef.DestructorSymbol(type) is { } destructor:
                    owner.destroyingRows.Add((types.Retains(destructor, types.Synthesized(destructor, type, []), definition), depth == 0));
                    break;
                case MirDestroyedPart.Erased:
                    owner.destroyingRows.Add((types.RetainsErasedDestruction(type, definition), depth == 0));
                    break;
                case MirDestroyedPart.Payload:
                    var self = this;
                    return MirTypes.Destruction(type.Components[0], ref self, depth + 1);
            }

            return false;
        }

        public bool Deep(BoundType type)
        {
            MirDepths.Meet(ref CollectionsMarshal.GetValueRefOrAddDefault(owner.entered, type, out _));
            return false;
        }
    }
}
