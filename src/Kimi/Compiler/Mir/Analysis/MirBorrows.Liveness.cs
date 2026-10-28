// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's holder liveness (A3 5, Liveness): the holders each location uses and kills, solved backward over the dataflow's chains, and in its
// replay each location's live-after bit of the holders it touches, from which the checks replay rebuilds every location's live holders. A
// Drop whose destruction passes the destruction walk's bound (MirTypes.Bound) may or may not observe what its holder holds, so it is a past
// use (R4-M153): a view with one solves the problem twice, every use counting and past uses not, and the checks replay rebuilds both, so a
// holder live only for a past use is told apart (Past, MirBorrows.CheckSets).
internal sealed partial class MirBorrows
{
    private const byte Uses = 1;
    private const byte Kills = 2;
    private const byte PastUse = 4; // With Uses: a destruction past the destruction walk's bound.

    private (int Start, int Count)[] touched = []; // By location: its touches.
    private Touch[] touches = [];
    private bool[] liveAfter = []; // By touch: whether its holder is live after its location.
    private bool[] usedAfter = []; // By touch, in a view with a past use: whether its holder is live after its location for a use not past.
    private int[] pastAt = []; // By slot, in a view with a past use: its first past use's location, or -1.
    private ulong[] usedBefore = []; // By head, in a view with a past use: the holders live before its chain for a use not past.
    private ulong[] live = []; // The checks replay's live holders.
    private ulong[] used = []; // In a view with a past use: the checks replay's holders live for a use not past.
    private int touchCount;
    private int pastUses; // The view's past uses.

    // Each location's touches of holders (A3 5): a use of a holder its Places read, Move, borrow, write through a reference or destroy
    // observingly (a Drop whose verdict is not Elide, or only the checks reach), past the walk's bound a past use, a kill where it is wholly
    // written, moved, or its storage or its Loans end; an EndBorrow uses its holder, then kills it.
    private void Touches()
    {
        var statements = this.body.Statements;
        MirDataflow.Grow(ref this.touched, statements.Length + this.body.Blocks.Length);
        (this.touchCount, this.pastUses) = (0, 0);
        var visitor = new Touching(this);
        for (var block = 0; block < this.body.Blocks.Length; block++)
        {
            ref readonly var data = ref this.body.Blocks[block];
            for (var i = 0; i <= data.Count; i++)
            {
                var start = this.touchCount;
                visitor.At(block, i, i < data.Count ? statements[data.Start + i].Kind : MirStatementKind.Assign);
                MirUses.ForEach(cases, new(block, i), ref visitor);
                this.touched[this.Location(block, i)] = (start, this.touchCount - start);
            }
        }

        MirDataflow.Grow(ref this.liveAfter, this.touchCount);
        MirDataflow.Grow(ref this.live, (this.holderCount + 63) >> 6);
        this.Work += statements.Length + this.body.Blocks.Length + this.touchCount;
    }

    private void Mark(int local, byte flags)
    {
        if (local >= 0 && this.slots[local] >= 0)
        {
            MirDataflow.Grow(ref this.touches, this.touchCount + 1);
            this.touches[this.touchCount++] = new(this.slots[local], flags);
        }
    }

    // A past use of a local's holder at a location (R4-M153), its slot's first kept for the depth record's subject.
    private void PastTouch(int local, int location)
    {
        if (local < 0 || this.slots[local] < 0)
        {
            return;
        }

        if (this.pastUses++ == 0)
        {
            MirDataflow.Grow(ref this.pastAt, this.holderCount);
            this.pastAt.AsSpan(0, this.holderCount).Fill(-1);
        }

        this.pastAt[this.slots[local]] = this.pastAt[this.slots[local]] < 0 ? location : this.pastAt[this.slots[local]];
        this.Mark(local, Uses | PastUse);
    }

    // The two solves (R4-M153): in a view with a past use, first the holders live for a use not past, their bits after each touch and
    // before each chain kept; then every use's, as in any view, whose state at the heads the checks replay enters.
    private void SolveLiveness(ulong[] state)
    {
        if (this.pastUses > 0)
        {
            var heads = flow.HeadCount;
            var words = flow.Before(0).Length;
            MirDataflow.Grow(ref this.usedAfter, this.touchCount);
            MirDataflow.Grow(ref this.usedBefore, heads * words);
            MirDataflow.Grow(ref this.used, (this.holderCount + 63) >> 6);
            var real = new Liveness(this, state, PastUse, this.usedAfter);
            flow.Backward(ref real);
            real.Recording = true;
            flow.ReplayBackward(ref real);
            for (var h = 0; h < heads; h++)
            {
                flow.Before(h).CopyTo(this.usedBefore.AsSpan(h * words, words));
            }
        }

        var liveness = new Liveness(this, state, 0, this.liveAfter);
        flow.Backward(ref liveness);
        liveness.Recording = true;
        flow.ReplayBackward(ref liveness);
    }

    // The checks replay: the live holders before a chain's head, then after each location.
    private void Enter(int head)
    {
        var words = (this.holderCount + 63) >> 6;
        var stored = flow.Before(head);
        this.live.AsSpan(0, words).Clear();
        stored.CopyTo(this.live);
        if (this.pastUses > 0)
        {
            this.used.AsSpan(0, words).Clear();
            this.usedBefore.AsSpan(head * stored.Length, stored.Length).CopyTo(this.used);
        }
    }

    private void Advance(int location)
    {
        var (start, count) = this.touched[location];
        for (var t = start; t < start + count; t++)
        {
            var slot = this.touches[t].Slot;
            this.live[slot >> 6] = this.liveAfter[t] ? this.live[slot >> 6] | (1UL << slot) : this.live[slot >> 6] & ~(1UL << slot);
            if (this.pastUses > 0)
            {
                this.used[slot >> 6] = this.usedAfter[t] ? this.used[slot >> 6] | (1UL << slot) : this.used[slot >> 6] & ~(1UL << slot);
            }
        }
    }

    /// <summary>A touch of a holder at a location: its slot and whether it uses and kills it.</summary>
    private readonly record struct Touch(int Slot, byte Flags);

    // The backward problem over holder bits: before each location, its kills cleared and its uses set but those with a flag ignored (a past
    // use, for the uses not past); recording, the bits after it.
    private struct Liveness(MirBorrows owner, ulong[] state, byte ignored, bool[] after) : IMirFlow
    {
        internal bool Recording { get; set; }

        public readonly void Entry() => state.AsSpan(0, (owner.holderCount + 63) >> 6).Clear();

        public void Block(int block)
        {
            for (var i = owner.body.Blocks[block].Count; i >= 0; i--)
            {
                var (start, count) = owner.touched[owner.Location(block, i)];
                for (var t = start; t < start + count; t++)
                {
                    var slot = owner.touches[t].Slot;
                    if (this.Recording)
                    {
                        after[t] = (state[slot >> 6] & (1UL << slot)) != 0;
                    }
                }

                for (var t = start; t < start + count; t++)
                {
                    var slot = owner.touches[t].Slot;
                    state[slot >> 6] &= (owner.touches[t].Flags & Kills) != 0 ? ~(1UL << slot) : ulong.MaxValue;
                }

                for (var t = start; t < start + count; t++)
                {
                    var (slot, flags) = (owner.touches[t].Slot, owner.touches[t].Flags);
                    state[slot >> 6] |= (flags & Uses) != 0 && (flags & ignored) == 0 ? 1UL << slot : 0;
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
        private int index;
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
                var destroys = owner.Dropping(this.block, place);
                if (destroys == AccessKind.Unclassified)
                {
                    owner.PastTouch(place.Root, owner.Location(this.block, this.index));
                }
                else
                {
                    owner.Mark(place.Root, destroys != AccessKind.Destroy ? Uses : (byte)0);
                }
            }
            else if ((access & (MirAccess.Reads | MirAccess.Moves | MirAccess.Borrows)) != 0)
            {
                owner.Mark(place.Root, (access & MirAccess.Moves) != 0 && whole ? (byte)(Uses | Kills) : Uses);
            }
        }

        // A location of a block; a terminator's kind is Assign.
        internal void At(int block, int index, MirStatementKind kind) => (this.block, this.index, this.kind) = (block, index, kind);
    }
}
