// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's transfer per statement kind (A3 5, Carrying, with E3): a location's effect planned on the state before it as entries, which the
// checks replay reads, then committed (MirBorrows.Solve): a storage statement or EndBorrow clears its root, an Assign stores its value,
// its issued Reborrow's or its rvalue's, into its Place after the Moves it makes, a Call's are MirBorrows.Calls' and a Drop's
// MirBorrows.Destructions'. A store into a holder's own Place changes its Placed part, one through a reference joins its Shared part
// (MirBorrows.States, DECISIONS R4-M160). A store adds the markers its destination's role gives (MarkerOf): γ_H to the Loans issued
// into a guard candidate H (E7); and Φ_N where it stores into an exposed holder (MirBorrows.Loans' Exposed; PLAN G598), into its own
// Place or through a reference (a holder a shared Loan alone names is no exposed holder, and E carries Φ_E), since a pointer the body
// formed may change the holder at any later point without a store A5 reads (a callee's scalar store, a pun); a call's W, MirBorrows.Calls.
internal sealed partial class MirBorrows
{
    private const byte Cleared = 0;
    private const byte Replaced = 1;
    private const byte Joined = 2;
    private const byte JoinedThrough = 3; // Joined into the Shared part: a store through a reference (P10, W).

    private Entry[] entries = [];
    private int entryCount;

    // The entries of a location on the state: a statement's storage end, or its rvalue's value stored into its Place after the Moves it
    // makes; a Call's.
    private void Plan(ulong[] state, int block, int index, bool checks)
    {
        (this.entryCount, this.planLocation, this.mustValue, this.mustKept) = (0, this.Location(block, index), 0, 0);
        foreach (var set in this.read)
        {
            this.reads[set] = default;
        }

        this.read.Clear();
        ref readonly var data = ref this.body.Blocks[block];
        if (index == data.Count)
        {
            if (data.Terminator.Kind == MirTerminatorKind.Call)
            {
                this.Call(state, data.Terminator, checks);
            }
            else if (data.Terminator.Kind == MirTerminatorKind.Drop)
            {
                this.Destroyed(state, data.Terminator, block, checks);
            }

            return;
        }

        ref readonly var statement = ref this.body.Statements[data.Start + index];
        if (statement.Kind is MirStatementKind.StorageLive or MirStatementKind.StorageDead or MirStatementKind.EndBorrow)
        {
            this.Add(statement.Place.Root, 0, 0, Cleared, issue: false);
        }
        else if (statement.Kind == MirStatementKind.Assign)
        {
            var rvalue = statement.Rvalue.Kind == MirRvalueKind.Adapt ? cases.Adapt(statement.Rvalue, out _) : statement.Rvalue;
            var (value, own) = this.Reborrowing(statement) != MirRefKind.Raw
                ? this.Issued(state, this.body.Operands[rvalue.Start].Place, reborrow: true, this.loanAt[data.Start + index])
                : this.Value(state, rvalue, data.Start + index);
            if (statement.Place is { Projection: 0, Root: >= 0 } into && this.MarkerOf(into.Root) == Marker.Guard)
            {
                own = this.sets.Add(own, Markers + into.Root); // E7: a value issued into a guard candidate H carries γ_H.
            }

            if (this.replaying && this.contract is not null)
            {
                // The must-set (MirBorrows.Storing): a Reborrow's is the pointer's it reborrows, else the rvalue's.
                this.mustValue = this.Reborrowing(statement) != MirRefKind.Raw ? this.MustOperand(rvalue.Start) : this.MustOf(rvalue);
                this.mustKept = statement.Place is { Projection: 0, Root: >= 0 } whole && !this.Exposed(whole.Root) ? this.mustValue : 0;
            }

            this.Moves(rvalue.Start, rvalue.Count);
            var issue = rvalue.Kind is MirRvalueKind.Ref or MirRvalueKind.Storage || this.Reborrowing(statement) != MirRefKind.Raw;
            this.Store(state, statement.Place, value, own, issue);
        }
    }

    private void Commit(ulong[] state)
    {
        for (var e = 0; e < this.entryCount; e++)
        {
            var (slot, value, own, op, _) = this.entries[e];
            value = own != 0 ? this.sets.Union(value, own) : value;
            state[slot] = op switch
            {
                Cleared => 0,
                Replaced => State(value, 0),
                JoinedThrough => State(Placed(state[slot]), this.sets.Union(Shared(state[slot]), value)),
                _ => State(this.sets.Union(Placed(state[slot]), value), Shared(state[slot])),
            };
            if (this.replaying && this.contract is not null)
            {
                this.must[slot] = op == Replaced ? this.mustKept : 0; // M (MirBorrows.Storing): a whole store keeps its value's, any other change none.
            }
        }
    }

    // A local's entry (none for a local that holds no Loan) or a slot's.
    private void Add(int local, int value, int own, byte op, bool issue)
    {
        if (local >= 0 && this.slots[local] >= 0)
        {
            this.AddSlot(this.slots[local], value, own, op, issue);
        }
    }

    private void AddSlot(int slot, int value, int own, byte op, bool issue)
    {
        MirDataflow.Grow(ref this.entries, this.entryCount + 1);
        this.entries[this.entryCount++] = new(slot, value, own, op, issue);
    }

    // assign D := v (A3 5): a whole root replaced, a direct part joined (P1), through k Derefs every holder refs^k names joined (P10; past a
    // raw pointer, the holders it addresses, and E where it carries Φ_E), into a static E joined; Φ_N into each exposed holder it writes.
    private void Store(ulong[] state, MirPlace place, int value, int own, bool issue)
    {
        this.Storing(place); // The storing rule (MirBorrows.Storing), before the walk buffer is filled.
        var (count, last) = (this.placeSteps.Walk(this.body, place, reborrow: false, ref this.walk), -1);
        for (var i = 0; i < count; i++)
        {
            last = this.walk[i].Class is MirStepClass.Shared or MirStepClass.Uniq or MirStepClass.Raw ? i : last;
        }

        var receiving = this.wCount == 0 ? null : cases.Type(this.body.TypeOf(place)!); // Rule W's receiving part (MirBorrows.Causes).
        if (last < 0)
        {
            var slot = place.Root < 0 ? Escaped : this.slots[place.Root];
            var whole = count == 0 && place.Root >= 0;
            if (slot >= 0)
            {
                var (arrived, issued) = (this.Arrive(slot, receiving, whole, 0, value), this.Arrive(slot, receiving, whole, 0, own));
                arrived = place.Root >= 0 && this.Exposed(place.Root) ? this.sets.Union(arrived, this.unformedSet) : arrived;
                this.AddSlot(slot, arrived, issued, whole ? Replaced : Joined, issue);
            }

            return;
        }

        var (layer, parents) = this.Prefix(state, place.Root, last);
        var pointer = this.PointerOf(place.Root, layer, parents, last); // What its last Deref goes through.
        var through = last == count - 1 ? pointer : 0; // A write of the whole referent (rule W's write-through).
        this.Refs(pointer);
        for (var t = 0; t < this.targets.Count; t++)
        {
            var (target, root) = (this.targets[t], this.targetRoots[t]);
            var arrived = this.Arrive(target, receiving, false, through, value);
            arrived = root >= 0 && this.Exposed(root) ? this.sets.Union(arrived, this.unformedSet) : arrived;
            arrived = target == Escaped ? this.Rested(arrived, pointer) : arrived; // An undecided write's E (MirBorrows.Puns).
            this.AddSlot(target, arrived, this.Arrive(target, receiving, false, through, own), JoinedThrough, issue);
        }

        this.Punning(pointer, place); // The pun rule (MirBorrows.Puns).
    }

    // A Move of a whole root empties it.
    private void Moves(int start, int count)
    {
        for (var o = start; o < start + count; o++)
        {
            if (this.body.Operands[o] is { Kind: MirOperandKind.Move, Place: { Projection: 0, Root: >= 0 } moved })
            {
                this.Add(moved.Root, 0, 0, Cleared, issue: false);
            }
        }
    }

    /// <summary>A planned change of a holder's state: cleared, replaced or joined with a value and the Loans issued with it (born at the
    /// location, so in no state before it), from a Ref (Issue) or a flow; joined into its Placed part, or through a reference into its
    /// Shared part.</summary>
    private readonly record struct Entry(int Slot, int Value, int Own, byte Op, bool Issue);
}
