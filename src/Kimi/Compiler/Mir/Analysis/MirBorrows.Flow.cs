// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's transfer per statement kind (A3 5, Carrying, with E3): a location's effect planned on the state before it as entries, which the
// checks replay reads, then committed (MirBorrows.Solve): a storage statement or EndBorrow clears its root, an Assign stores its value,
// its issued Reborrow's or its rvalue's, into its Place after the Moves it makes, and a Call's are MirBorrows.Calls'. A store adds the
// markers its destination's role gives (MarkerOf): γ_H to the Loans issued into a guard candidate H (E7).
internal sealed partial class MirBorrows
{
    private const byte Cleared = 0;
    private const byte Replaced = 1;
    private const byte Joined = 2;

    private Entry[] entries = [];
    private int entryCount;

    // The entries of a location on the state: a statement's storage end, or its rvalue's value stored into its Place after the Moves it
    // makes; a Call's.
    private void Plan(ulong[] state, int block, int index, bool checks)
    {
        this.entryCount = 0;
        foreach (var set in this.contentsRead)
        {
            this.contents[set] = 0;
        }

        this.contentsRead.Clear();
        ref readonly var data = ref this.body.Blocks[block];
        if (index == data.Count)
        {
            if (data.Terminator.Kind == MirTerminatorKind.Call)
            {
                this.Call(state, new(block, index), data.Terminator, checks);
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

            this.Moves(rvalue.Start, rvalue.Count);
            var issue = rvalue.Kind == MirRvalueKind.Ref || this.Reborrowing(statement) != MirRefKind.Raw;
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
                Replaced => (ulong)value,
                _ => (ulong)this.sets.Union((int)state[slot], value),
            };
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

    // assign D := v (A3 5): a whole root replaced, a direct part joined (P1), through k borrow Derefs every holder refs^k names joined
    // (P10), through a raw pointer or into a static E joined.
    private void Store(ulong[] state, MirPlace place, int value, int own, bool issue)
    {
        var (count, last) = (this.placeSteps.Walk(this.body, place, reborrow: false, ref this.walk), -1);
        for (var i = 0; i < count; i++)
        {
            last = this.walk[i].Class is StepClass.Shared or StepClass.Uniq or StepClass.Raw ? i : last;
        }

        if (last < 0)
        {
            if (place.Root < 0)
            {
                this.AddSlot(Escaped, value, own, Joined, issue);
            }

            this.Add(place.Root, value, own, count == 0 ? Replaced : Joined, issue);
            return;
        }

        var (layer, raw) = (this.Root(state, place.Root), this.walk[last].Class == StepClass.Raw);
        for (var i = 0; i < last; i++)
        {
            layer = this.walk[i].Class switch
            {
                StepClass.Shared or StepClass.Uniq => this.Content(state, layer),
                StepClass.Raw => this.escapedSet,
                _ => layer,
            };
        }

        this.Refs(raw ? this.escapedSet : layer);
        foreach (var target in this.targets)
        {
            this.AddSlot(target, value, own, Joined, issue);
        }
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
    /// location, so in no state before it), from a Ref (Issue) or a flow.</summary>
    private readonly record struct Entry(int Slot, int Value, int Own, byte Op, bool Issue);
}
