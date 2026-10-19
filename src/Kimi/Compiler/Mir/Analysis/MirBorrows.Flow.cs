// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's carrying (A3 5, Carrying, with E3 and E8): each holder's set forward over the dataflow's chains, joined by node union. A location's
// effect is planned on the state before it as entries (a Call's by MirBorrows.Calls), which the checks replay reads, then committed; at a
// destruction in place the Loans it ends are then removed from every holder (`step`: σ_out \ ended, so a call's transfer still reads its
// destroyed target). A store adds the markers its destination's role gives (MarkerOf): γ_H to the Loans issued into a guard candidate H
// (E7), and a pending slot's default taint to the value a default's evaluation places into it (Placed, R4-M70).
internal sealed partial class MirBorrows
{
    private const byte Cleared = 0;
    private const byte Replaced = 1;
    private const byte Joined = 2;

    private Entry[] entries = [];
    private int entryCount;
    private int chainHead = -1; // The checks replay's chain: its head and first block.
    private int chainFirst;

    // σ at the entry: a Loan-capable argument refers to storage the body does not own (Φ_E).
    private void Initial(ulong[] state)
    {
        state.AsSpan(0, this.holderCount).Clear();
        for (var i = 1; i < this.body.Locals.Length && this.body.Locals[i].Kind == MirLocalKind.Arg; i++)
        {
            if (this.slots[i] >= 0)
            {
                state[this.slots[i]] = (ulong)this.escapedSet;
            }
        }
    }

    // A block's locations: in the checks replay, the live holders after each, its plan on the state before it and its checks first.
    private void Carry(ulong[] state, int block, bool checks)
    {
        if (checks && flow.Head(block) != this.chainHead)
        {
            (this.chainHead, this.chainFirst) = (flow.Head(block), block);
            this.Enter(this.chainHead);
        }

        for (var i = 0; i <= this.body.Blocks[block].Count; i++)
        {
            var location = this.Location(block, i);
            this.Work++;
            this.Plan(state, block, i);
            if (checks)
            {
                this.Advance(location);
                this.Check(state, block, i, location);
            }

            this.Commit(state);
            if (this.ended[location] != 0)
            {
                for (var s = 0; s < this.holderCount; s++)
                {
                    state[s] = (ulong)this.sets.Remove((int)state[s], this.ended[location]);
                }

                this.Work += this.holderCount;
            }
        }
    }

    // The node-union join (R4-M34).
    private bool Join(Span<ulong> target, ReadOnlySpan<ulong> incoming)
    {
        var grew = false;
        for (var s = 0; s < target.Length; s++)
        {
            var union = (ulong)this.sets.Union((int)target[s], (int)incoming[s]);
            grew |= union != target[s];
            target[s] = union;
        }

        this.Work += target.Length;
        return grew;
    }

    // The entries of a location on the state: a statement's storage end, or its rvalue's value stored into its Place after the Moves it
    // makes; a Call's.
    private void Plan(ulong[] state, int block, int index)
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
                this.Call(state, new(block, index), data.Terminator);
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
            this.Store(state, statement.Place, this.Placed(value, statement.Place, new(block, index)), own, issue);
        }
    }

    // A value a location places into a Place, with the default taint of the pending slot D it fills where the location is a default's
    // evaluation (R4-M70, until A5-F9, unit 2e): a default template's Call into D, or a store into D from inside an inline default copy,
    // D lying outside the copy's Inlined scope (the slots of a call inside the copy lie inside it).
    private int Placed(int value, MirPlace place, MirLocation at)
    {
        if (place is not { Projection: 0, Root: >= 0 } || this.MarkerOf(place.Root) != Marker.Taint)
        {
            return value;
        }

        ref readonly var data = ref this.body.Blocks[at.Block];
        this.Work += 2; // The two scope walks (A3 6.2).
        var copy = this.body.Inlining(this.Source(at).Scope);
        var evaluates = (at.Index == data.Count && data.Terminator.Callee is { Kind: MirCalleeKind.Fn, Arg: > 0 }) ||
            (copy >= 0 && copy != this.body.Inlining(this.body.Locals[place.Root].Source.Scope));
        return evaluates ? this.sets.Add(value, Markers + place.Root) : value;
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
        var (count, last) = (this.Walk(place, reborrow: false, ref this.walk), -1);
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

    // The forward problem over holder slots: E and the flow holders stored at heads, each slot a set.
    private struct Carrying(MirBorrows owner, ulong[] state) : IMirFlow
    {
        internal bool Checks { get; set; }

        public readonly void Entry() => owner.Initial(state);

        public readonly void Block(int block) => owner.Carry(state, block, this.Checks);

        public readonly bool Join(Span<ulong> target, ReadOnlySpan<ulong> incoming) => owner.Join(target, incoming);
    }
}
