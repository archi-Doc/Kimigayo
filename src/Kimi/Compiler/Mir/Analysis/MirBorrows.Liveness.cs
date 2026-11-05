// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's holder liveness (A3 5, Liveness): the holders each location uses and kills, solved backward over the dataflow's chains, and in its
// replay each location's live-after bit of the holders it touches, from which the checks replay rebuilds every location's live holders.
// After a Return local 0's holder is live, as the caller uses the value it delivers (the exit's fact, R4-M156), so every reader of a live
// holder counts the delivered value: the Loan check, the own-step departures and rule W's escape holder. A kept Drop past the destruction
// walk's bound, whose class is unknown, uses its root's drop slot instead of the root, and every kill of the root kills that slot too, so
// the slot is live where such a Drop may come before the holder's next kill (drop-liveness, DECISIONS R4-M153); the check sets read it
// where the holder itself is not live (MirBorrows.CheckSets).
internal sealed partial class MirBorrows
{
    private const byte Uses = 1;
    private const byte Kills = 2;

    private (int Start, int Count)[] touched = []; // By location: its touches.
    private Touch[] touches = [];
    private bool[] liveAfter = []; // By touch: whether its holder is live after its location.
    private ulong[] live = []; // The checks replay's live holders.
    private int touchCount;

    // Each location's touches of holders (A3 5): a use of a holder its Places read, Move, borrow, write through a reference or destroy
    // observingly (a Drop whose verdict is not Elide, or only the checks reach), a kill where it is wholly written, moved, or its storage
    // or its Loans end; an EndBorrow uses its holder, then kills it; a Drop past the destruction walk's bound uses the drop slot.
    private void Touches()
    {
        var statements = this.body.Statements;
        MirDataflow.Grow(ref this.touched, statements.Length + this.body.Blocks.Length);
        this.touchCount = 0;
        var visitor = new Touching(this);
        for (var block = 0; block < this.body.Blocks.Length; block++)
        {
            ref readonly var data = ref this.body.Blocks[block];
            for (var i = 0; i <= data.Count; i++)
            {
                var start = this.touchCount;
                visitor.At(block, i < data.Count ? statements[data.Start + i].Kind : MirStatementKind.Assign);
                MirUses.ForEach(cases, new(block, i), ref visitor);
                this.touched[this.Location(block, i)] = (start, this.touchCount - start);
            }
        }

        MirDataflow.Grow(ref this.liveAfter, this.touchCount);
        MirDataflow.Grow(ref this.live, (this.holderCount + 63) >> 6);
        this.Work += statements.Length + this.body.Blocks.Length + this.touchCount;
    }

    // A touch of a local's holder, a kill also of its drop slot.
    private void Mark(int local, byte flags)
    {
        if (local >= 0 && this.slots[local] >= 0)
        {
            this.Touched(this.slots[local], flags);
            if ((flags & Kills) != 0 && this.drops.Count > 0 && this.dropSlots[local] >= 0)
            {
                this.Touched(this.dropSlots[local], Kills);
            }
        }
    }

    // A Drop's touch of its root's holder (A3 5, R4-M6): an observing destruction uses it, a shallow one touches nothing, and one past the
    // destruction walk's bound uses its drop slot (R4-M153; MirBorrows.Holders gives every such holder one).
    private void Dropped(int local, AccessKind kind)
    {
        if (kind != AccessKind.Unclassified)
        {
            this.Mark(local, kind != AccessKind.Destroy ? Uses : (byte)0);
        }
        else if (local >= 0 && this.slots[local] >= 0)
        {
            this.Touched(this.dropSlots[local], Uses);
        }
    }

    private void Touched(int slot, byte flags)
    {
        MirDataflow.Grow(ref this.touches, this.touchCount + 1);
        this.touches[this.touchCount++] = new(slot, flags);
    }

    // The checks replay: the live holders before a chain's head, then after each location.
    private void Enter(int head)
    {
        var stored = flow.Before(head);
        this.live.AsSpan(0, (this.holderCount + 63) >> 6).Clear();
        stored.CopyTo(this.live);
    }

    private void Advance(int location)
    {
        var (start, count) = this.touched[location];
        for (var t = start; t < start + count; t++)
        {
            var slot = this.touches[t].Slot;
            this.live[slot >> 6] = this.liveAfter[t] ? this.live[slot >> 6] | (1UL << slot) : this.live[slot >> 6] & ~(1UL << slot);
        }
    }

    /// <summary>A touch of a holder at a location: its slot and whether it uses and kills it.</summary>
    private readonly record struct Touch(int Slot, byte Flags);

    // The backward problem over holder bits: before each location, its kills cleared and its uses set; recording, the bits after it.
    private struct Liveness(MirBorrows owner, ulong[] state) : IMirFlow
    {
        internal bool Recording { get; set; }

        public readonly void Entry() => state.AsSpan(0, (owner.holderCount + 63) >> 6).Clear();

        public void Block(int block)
        {
            // The exit's fact (IMirFlow.Entry): a Return delivers local 0 to its caller, which uses it, so its holder is live after the
            // Return (SPEC 16.2.2, 6.3.4; DECISIONS R4-M156); a diverging exit delivers nothing.
            if (owner.body.Blocks[block].Terminator.Kind == MirTerminatorKind.Return && owner.slots[0] >= 0)
            {
                state[owner.slots[0] >> 6] |= 1UL << owner.slots[0];
            }

            for (var i = owner.body.Blocks[block].Count; i >= 0; i--)
            {
                var (start, count) = owner.touched[owner.Location(block, i)];
                for (var t = start; t < start + count; t++)
                {
                    var slot = owner.touches[t].Slot;
                    if (this.Recording)
                    {
                        owner.liveAfter[t] = (state[slot >> 6] & (1UL << slot)) != 0;
                    }
                }

                for (var t = start; t < start + count; t++)
                {
                    var slot = owner.touches[t].Slot;
                    state[slot >> 6] &= (owner.touches[t].Flags & Kills) != 0 ? ~(1UL << slot) : ulong.MaxValue;
                }

                for (var t = start; t < start + count; t++)
                {
                    var slot = owner.touches[t].Slot;
                    state[slot >> 6] |= (owner.touches[t].Flags & Uses) != 0 ? 1UL << slot : 0;
                }

                owner.Work += count + 1;
            }
        }

        public readonly bool Join(Span<ulong> target, ReadOnlySpan<ulong> incoming)
        {
            owner.Work += target.Length;
            return MirDataflow.Union(target, incoming);
        }
    }

    // The touches of one location, from its uses under the view.
    private struct Touching(MirBorrows owner) : IMirUses
    {
        private int block;
        private MirStatementKind kind;

        public readonly void Operand(in MirOperand operand)
        {
        }

        public readonly void Effect(MirEffect effects, int site)
        {
        }

        // An Opaque site's roots, read, written and borrowed, are used.
        public readonly void Place(MirPlace place, MirAccess access, MirRefKind kind)
        {
            var whole = place.Projection == 0;
            if ((access & MirAccess.Kills) != 0)
            {
                owner.Mark(place.Root, this.kind == MirStatementKind.EndBorrow ? (byte)(Uses | Kills) : Kills);
            }
            else if ((access & MirAccess.Writes) != 0 && (access & MirAccess.Reads) == 0)
            {
                var through = owner.Through(place) ? Uses : (byte)0;
                owner.Mark(place.Root, whole ? Kills : through);
            }
            else if ((access & MirAccess.DropUse) != 0)
            {
                owner.Dropped(place.Root, owner.Dropping(this.block, place));
            }
            else if ((access & (MirAccess.Reads | MirAccess.Moves | MirAccess.Borrows)) != 0)
            {
                owner.Mark(place.Root, (access & MirAccess.Moves) != 0 && whole ? (byte)(Uses | Kills) : Uses);
            }
        }

        // A location of a block; a terminator's kind is Assign.
        internal void At(int block, MirStatementKind kind) => (this.block, this.kind) = (block, kind);
    }
}
