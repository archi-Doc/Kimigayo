// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Numerics;

namespace Kimi.Compiler.Mir;

// A5's conflict check (A3 5, Check sets and err(conflict), with E3 and E5): at an access of a borrowed root, the Loans of the check set
// by holder, compared with the access syntactically.
internal sealed partial class MirBorrows
{
    private readonly List<int> pending = []; // Slots live at the check whose stores are still to read.
    private readonly List<int> scanning = []; // Slots whose states are still to read.
    private readonly MirFact[] facts = new MirFact[2];
    private Access[] accesses = [];
    private Candidate[] candidates = [];
    private Step[] accessSteps = [];
    private int[] reached = []; // By slot: the check that made it live.
    private int[] scanned = []; // By slot: the check that read its state.
    private int[] loanMarks = []; // By Loan: the check << 2 that saw it, with bit 0 for a shared mode and bit 1 for a unique one.
    private int accessCount;
    private int candidateCount;
    private int stamp;

    /// <summary>What an access does (A3 5's access): read, borrow shared or exclusive, Move, write or destroy.</summary>
    private enum AccessKind : byte
    {
        Read,
        SharedBorrow,
        UniqueBorrow,
        Move,
        Write,
        Destroy,
    }

    // Two steps after equal prefixes may select the same storage (A3 3): a runtime index meets every index; distinct Fields, constant
    // indices, bases and Downcasts are disjoint.
    private static bool Meets(in Step left, in Step right)
    {
        if (left.Kind == MirProjectionKind.Index || right.Kind == MirProjectionKind.Index)
        {
            return left.Kind is MirProjectionKind.Index or MirProjectionKind.ConstIndex && right.Kind is MirProjectionKind.Index or MirProjectionKind.ConstIndex;
        }

        return left.Kind == right.Kind && (left.Kind is MirProjectionKind.Deref or MirProjectionKind.PairLayer || left.Arg == right.Arg);
    }

    // A check of the accesses at a location of a borrowed root against CS(ℓ) (A3 5): the states before ℓ of the holders live after it but
    // those it overwrites, E's, and the values its plan stores into a holder live after it or made live by the closure, or into E (transit,
    // a borrow's stored value only seeding the closure), closed through the holders their direct Loans name (E5: with the holders its own
    // Loans name); the first conflicting Loan of each access is its problem.
    private void Check(ulong[] state, int block, int index, int location)
    {
        this.accessCount = 0;
        var visitor = new Accessing(this, index < this.body.Blocks[block].Count ? this.body.Statements[location].Kind : MirStatementKind.Assign);
        MirUses.ForEach(cases, new(block, index), ref visitor);
        if (this.accessCount == 0)
        {
            return;
        }

        this.Gather(state);
        var subject = this.body.Subject(index < this.body.Blocks[block].Count ? this.body.Statements[location].Source : this.body.Blocks[block].Terminator.Source);
        for (var a = 0; a < this.accessCount; a++)
        {
            var access = this.accesses[a];
            var count = this.Walk(access.Place, access.Reborrow, ref this.accessSteps);
            for (var c = 0; c < this.candidateCount; c++)
            {
                var candidate = this.candidates[c];
                if ((candidate.Unique || access.Kind is AccessKind.Write or AccessKind.Move or AccessKind.UniqueBorrow or AccessKind.Destroy) &&
                    this.Hits(this.Key(access.Place.Root), count, access.Kind, this.loans[candidate.Loan]))
                {
                    // The witness: a holder's next use, none when that lies in a later chain; a transit value's or E's store.
                    var witness = candidate.Via > Escaped ? this.NextUse(candidate.Via, block, index) : subject;
                    this.facts[0] = new(MirFactKind.Anchor, this.loans[candidate.Loan].Site);
                    this.facts[1] = new(candidate.Via > Escaped ? MirFactKind.Use : MirFactKind.Store, witness ?? default);
                    var facts = this.facts.AsSpan(0, witness is null ? 1 : 2);
                    this.program.Problems.Add(new(DiagnosticCode.ComparisonLoanConflict_Kd, subject, this.views), facts);
                    break;
                }
            }
        }
    }

    // CS(ℓ)'s Loans, each with its mode by the holder it was found in (a transit value is no reservation holder).
    private void Gather(ulong[] state)
    {
        this.stamp++;
        MirDataflow.Grow(ref this.reached, this.holderCount);
        MirDataflow.Grow(ref this.scanned, this.holderCount);
        MirDataflow.Grow(ref this.loanMarks, this.loanCount);
        this.candidateCount = 0;
        this.Reach(Escaped, scan: true);
        for (var word = 0; word < (this.holderCount + 63) >> 6; word++)
        {
            for (var bits = this.live[word]; bits != 0; bits &= bits - 1)
            {
                var slot = (word << 6) + BitOperations.TrailingZeroCount(bits);
                this.Reach(slot, scan: !this.Overwritten(slot));
            }
        }

        while (this.pending.Count > 0 || this.scanning.Count > 0)
        {
            if (this.pending.Count > 0)
            {
                var slot = this.pending[^1];
                this.pending.RemoveAt(this.pending.Count - 1);
                for (var e = 0; e < this.entryCount; e++)
                {
                    if (this.entries[e] is { Op: not Cleared } entry && entry.Slot == slot)
                    {
                        this.Stored(entry);
                    }
                }

                continue;
            }

            var scan = this.scanning[^1];
            this.scanning.RemoveAt(this.scanning.Count - 1);
            foreach (var element in this.sets.Elements((int)state[scan]))
            {
                this.Consider(element, scan);
            }

            this.Work += this.sets.Count((int)state[scan]) + 1;
        }
    }

    // A holder made live at the check: its stores count, and its state is read unless the location overwrites it.
    private void Reach(int slot, bool scan)
    {
        if (this.reached[slot] != this.stamp)
        {
            this.reached[slot] = this.stamp;
            this.pending.Add(slot);
        }

        if (scan && this.scanned[slot] != this.stamp)
        {
            this.scanned[slot] = this.stamp;
            this.scanning.Add(slot);
        }
    }

    private bool Overwritten(int slot)
    {
        for (var e = 0; e < this.entryCount; e++)
        {
            if (this.entries[e] is { Op: Replaced } entry && entry.Slot == slot)
            {
                return true;
            }
        }

        return false;
    }

    // A stored value at the check (transit): a flow's Loans are candidates; the holders a borrow's parents and every issued Loan name seed the
    // closure (E5).
    private void Stored(in Entry entry)
    {
        if (!entry.Issue)
        {
            foreach (var element in this.sets.Elements(entry.Value))
            {
                this.Consider(element, -1);
            }
        }

        foreach (var element in this.sets.Elements(entry.Issue ? this.sets.Union(entry.Value, entry.Own) : entry.Own))
        {
            if (element == Escaped || (element >= this.loanBase && this.loans[element - this.loanBase] is { Direct: true, Named: >= 0 }))
            {
                this.Reach(element == Escaped ? Escaped : this.loans[element - this.loanBase].Named, scan: true);
            }
        }
    }

    // An element of CS(ℓ) found in a holder (-1: a transit value): a Loan once per mode (A3 5's mode: a two-phase Loan is shared only in its
    // reservation holder), whose named holder the closure reads; Φ_E makes E live.
    private void Consider(int element, int via)
    {
        if (element == Escaped)
        {
            this.Reach(Escaped, scan: true);
            return;
        }

        if (element < this.loanBase)
        {
            return; // A τ_s marker never conflicts.
        }

        var index = element - this.loanBase;
        ref readonly var loan = ref this.loans[index];
        var unique = loan.Mode == MirRefKind.Uniq || (loan.Mode == MirRefKind.TwoPhaseUniq && via != loan.Holder);
        var bit = unique ? 2 : 1;
        if (this.loanMarks[index] >> 2 != this.stamp)
        {
            this.loanMarks[index] = this.stamp << 2;
        }

        if ((this.loanMarks[index] & bit) == 0)
        {
            this.loanMarks[index] |= bit;
            MirDataflow.Grow(ref this.candidates, this.candidateCount + 1);
            this.candidates[this.candidateCount++] = new(index, unique, via);
        }

        if (loan is { Direct: true, Named: >= 0 })
        {
            this.Reach(loan.Named, scan: true);
        }
    }

    // hits(A, X, k) (A3 5, E3): within(A, X), or within(X, A) where no borrow or raw Deref of X lies below A or the access is deep.
    private bool Hits(int root, int count, AccessKind kind, in Loan loan)
    {
        if (root != loan.Root)
        {
            return false;
        }

        for (var i = 0; i < Math.Min(count, loan.Count); i++)
        {
            if (!Meets(this.accessSteps[i], this.loanSteps[loan.Start + i]))
            {
                return false;
            }
        }

        if (count >= loan.Count || kind is AccessKind.Move or AccessKind.SharedBorrow or AccessKind.UniqueBorrow)
        {
            return true;
        }

        for (var i = count; i < loan.Count; i++)
        {
            if (this.loanSteps[loan.Start + i].Class is StepClass.Shared or StepClass.Uniq or StepClass.Raw)
            {
                return false;
            }
        }

        return true;
    }

    // The witness holder's next use after the location in its chain (A3 6.4), or null where it is used only in a later chain.
    private SourceRef? NextUse(int slot, int block, int index)
    {
        for (var visited = 0; visited < this.body.Blocks.Length; visited++)
        {
            ref readonly var data = ref this.body.Blocks[block];
            for (var i = index + 1; i <= data.Count; i++)
            {
                var (start, count) = this.touched[this.Location(block, i)];
                for (var t = start; t < start + count; t++)
                {
                    if (this.touches[t].Slot == slot && (this.touches[t].Flags & Uses) != 0)
                    {
                        return this.body.Subject(i < data.Count ? this.body.Statements[data.Start + i].Source : data.Terminator.Source);
                    }
                }
            }

            var terminator = data.Terminator;
            var next = MirUses.Successors(terminator) == 1 && terminator.CheckTarget < 0 ? MirUses.Successor(this.body, terminator, 0) : -1;
            if (next < 0 || next == this.chainFirst || flow.Head(next) != flow.Head(block))
            {
                return null;
            }

            (block, index) = (next, -1);
        }

        return null;
    }

    private void Accessed(MirPlace place, AccessKind kind, bool reborrow)
    {
        if (this.borrowed[this.Key(place.Root)])
        {
            MirDataflow.Grow(ref this.accesses, this.accessCount + 1);
            this.accesses[this.accessCount++] = new(place, kind, reborrow);
        }
    }

    /// <summary>An access of a borrowed root: its Place, a Reborrow's of its Place's referent, and its kind.</summary>
    private readonly record struct Access(MirPlace Place, AccessKind Kind, bool Reborrow);

    /// <summary>A Loan of the check set: its index, whether its mode in the holder it was found in is unique, and that holder (-1: a
    /// transit value).</summary>
    private readonly record struct Candidate(int Loan, bool Unique, int Via);

    // The accesses of one location (A3 5's access): Copy, FakeRead, Discriminant, Len and an index read; Move; a shared, two-phase or raw
    // borrow borrows shared, a unique one or a Reborrow uniquely; a destination is written; StorageDead and a Drop destroy. An Opaque site's
    // roots are its problems' (derived, R4-M10).
    private struct Accessing(MirBorrows owner, MirStatementKind statement) : IMirUses
    {
        private MirOperand operand;

        public void Operand(in MirOperand operand) => this.operand = operand;

        public readonly void Effect(MirEffect effects, int site)
        {
        }

        public void Place(MirPlace place, MirAccess access, MirRefKind kind)
        {
            var reborrow = this.operand is { Kind: MirOperandKind.PairAcquire } acquire && acquire.Place == place && owner.Reborrowing(acquire);
            this.operand = this.operand.Place == place ? default : this.operand; // An operand's index locals precede its Place.
            if ((access & (MirAccess.Reads | MirAccess.Writes)) == (MirAccess.Reads | MirAccess.Writes))
            {
                return;
            }

            AccessKind? accessed = (access & MirAccess.Kills) != 0 ? statement == MirStatementKind.StorageDead ? AccessKind.Destroy : null
                : (access & MirAccess.DropUse) != 0 ? AccessKind.Destroy
                : (access & MirAccess.Writes) != 0 ? AccessKind.Write
                : (access & MirAccess.Borrows) != 0 ? kind == MirRefKind.Uniq ? AccessKind.UniqueBorrow : AccessKind.SharedBorrow
                : (access & MirAccess.Moves) != 0 ? AccessKind.Move
                : reborrow ? AccessKind.UniqueBorrow
                : (access & MirAccess.Reads) != 0 ? AccessKind.Read : null;
            if (accessed is { } known)
            {
                owner.Accessed(place, known, reborrow);
            }
        }
    }
}
