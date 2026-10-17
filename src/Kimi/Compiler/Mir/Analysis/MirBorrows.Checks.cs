// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Numerics;

namespace Kimi.Compiler.Mir;

// A5's conflict check (A3 5, Check sets and err(conflict), with E1, E3 and E5): at an access of a borrowed root, the Loans of the check set
// by holder, compared with the access syntactically; a guard candidate's EndBorrow against the values that keep its guard-local Origin
// (E7); then the location's activations (MirBorrows.Activations).
internal sealed partial class MirBorrows
{
    private readonly List<int> pending = []; // Slots live at the check whose stores are still to read.
    private readonly List<int> scanning = []; // Slots whose states are still to read.
    private readonly MirFact[] facts = new MirFact[3];
    private Access[] accesses = [];
    private Candidate[] candidates = [];
    private Step[] accessSteps = [];
    private int[] reached = []; // By slot: the check that made it live.
    private int[] scanned = []; // By slot: the check that read its state.
    private int[] loanMarks = []; // By Loan: the check << 2 that saw it, with bit 0 for a shared mode and bit 1 for a unique one.
    private int[] markSeen = []; // By marker (τ_s, γ_H): the check that saw it.
    private int[] markVia = []; // By marker: the holder that check first saw it in (-1: a transit value).
    private int accessCount;
    private int candidateCount;
    private int stamp;

    /// <summary>What an access does (A3 5's access): read, borrow shared or exclusive, Move, write, destroy, or destroy observing what the
    /// Place holds (exclusive and deep, A3 5's obs; E3 leaves it no shared cut); a destruction past the destruction walk's bound is not
    /// classified (Unsupported_Kd, as A4's Limit).</summary>
    private enum AccessKind : byte
    {
        Read,
        SharedBorrow,
        UniqueBorrow,
        Move,
        Write,
        Destroy,
        Observe,
        Unclassified,
    }

    // Two steps after equal prefixes may select the same storage (A3 3): a runtime index meets every index; distinct Fields, constant
    // indices, bases and Downcasts are disjoint.
    private static bool Meets(in Step left, in Step right)
    {
        if (left.Kind == MirProjectionKind.Index || right.Kind == MirProjectionKind.Index)
        {
            return left.Kind is MirProjectionKind.Index or MirProjectionKind.ConstIndex &&
                right.Kind is MirProjectionKind.Index or MirProjectionKind.ConstIndex;
        }

        return left.Kind == right.Kind && (left.Kind is MirProjectionKind.Deref or MirProjectionKind.PairLayer || left.Arg == right.Arg);
    }

    // A check of the accesses at a location of a borrowed root against CS(ℓ) (A3 5): the states before ℓ of the holders live after it but
    // those it overwrites, E's, and the values its plan stores into a holder live after it or made live by the closure, or into E (transit,
    // a borrow's stored value only seeding the closure), closed through the holders their direct Loans name (E5: with the holders its own
    // Loans name); the first conflicting Loan of each access is its problem, and a guard candidate's EndBorrow checks its γ. Then its
    // activations, on their own check sets.
    private void Check(ulong[] state, int block, int index, int location)
    {
        this.accessCount = 0;
        var at = new MirLocation(block, index);
        var visitor = new Accessing(this, at, index < this.body.Blocks[block].Count ? this.body.Statements[location].Kind : MirStatementKind.Assign);
        MirUses.ForEach(cases, at, ref visitor);
        if (this.accessCount > 0)
        {
            this.Gather(state, this.live, -1, this.entryCount);
        }

        for (var a = 0; a < this.accessCount; a++)
        {
            var access = this.accesses[a];
            if (access.Kind == AccessKind.Unclassified)
            {
                this.program.Problems.Add(new(DiagnosticCode.Unsupported_Kd, this.Subject(at), this.views));
                continue;
            }

            var count = this.Walk(access.Place, access.Reborrow, ref this.accessSteps);
            this.Conflict(this.Key(access.Place.Root), this.accessSteps.AsSpan(0, count), access.Kind, DiagnosticCode.ComparisonLoanConflict_Kd, at, -1);
        }

        if (index < this.body.Blocks[block].Count && this.body.Statements[location] is { Kind: MirStatementKind.EndBorrow, Place.Root: var ended } &&
            this.body.Locals[ended].Role == MirLocalRole.GuardCandidate)
        {
            this.Ended(state, at, ended);
        }

        if (index == this.body.Blocks[block].Count && this.Retaining(this.body.Blocks[block].Terminator) is { IsLimit: true })
        {
            this.program.Problems.Add(new(DiagnosticCode.Unsupported_Kd, this.Subject(at), this.views)); // A contract Binding does not instantiate.
        }

        this.Activations(state, at);
    }

    // A check set's Loans, each with its mode by the holder it was found in (a transit value is no reservation holder): the states before ℓ
    // of the given live holders, E's, and of the values the location's first stores put into a holder made live (transit, whose stored
    // borrows and own Loans seed the closure, E5), closed through the holders their direct Loans name. An activation's reservation holder
    // (own) is not read; its Loans seed the closure (CS_call, E5).
    private void Gather(ulong[] state, ulong[] holders, int own, int stores)
    {
        this.stamp++;
        MirDataflow.Grow(ref this.reached, this.holderCount);
        MirDataflow.Grow(ref this.scanned, this.holderCount);
        MirDataflow.Grow(ref this.loanMarks, this.loanCount);
        MirDataflow.Grow(ref this.markSeen, this.loanBase);
        MirDataflow.Grow(ref this.markVia, this.loanBase);
        this.candidateCount = 0;
        if (own >= 0)
        {
            (this.reached[own], this.scanned[own]) = (this.stamp, this.stamp);
            this.Seed((int)state[own]);
        }

        this.Reach(Escaped, scan: true);
        this.Work += (this.holderCount + 63) >> 6;
        for (var word = 0; word < (this.holderCount + 63) >> 6; word++)
        {
            for (var bits = holders[word]; bits != 0; bits &= bits - 1)
            {
                var slot = (word << 6) + BitOperations.TrailingZeroCount(bits);
                this.Reach(slot, scan: !this.Overwritten(slot, stores));
            }
        }

        while (this.pending.Count > 0 || this.scanning.Count > 0)
        {
            if (this.pending.Count > 0)
            {
                var slot = this.pending[^1];
                this.pending.RemoveAt(this.pending.Count - 1);
                this.Work += stores;
                for (var e = 0; e < stores; e++)
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

    private bool Overwritten(int slot, int stores)
    {
        this.Work += stores;
        for (var e = 0; e < stores; e++)
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
            this.Work += this.sets.Count(entry.Value);
            foreach (var element in this.sets.Elements(entry.Value))
            {
                this.Consider(element, -1);
            }
        }

        this.Seed(entry.Issue ? this.sets.Union(entry.Value, entry.Own) : entry.Own);
    }

    // E5's seed: the holders a set's direct Loans name, and E where it carries Φ_E, are made live.
    private void Seed(int set)
    {
        this.Work += this.sets.Count(set);
        foreach (var element in this.sets.Elements(set))
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
            // A marker never conflicts; the holder it is first seen in witnesses err(ended) for a guard-local γ_H.
            if (this.markSeen[element] != this.stamp)
            {
                (this.markSeen[element], this.markVia[element]) = (this.stamp, via);
            }

            return;
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

    // The first Loan of the check set an access conflicts with (A3 5's conflicts and hits), as a problem at the location: a write, Move,
    // exclusive borrow or destruction with any mode, another access with a unique one.
    private void Conflict(int root, ReadOnlySpan<Step> steps, AccessKind kind, DiagnosticCode code, MirLocation at, int reservation)
    {
        for (var c = 0; c < this.candidateCount; c++)
        {
            var candidate = this.candidates[c];
            this.Work++;
            if ((candidate.Unique || kind is not (AccessKind.Read or AccessKind.SharedBorrow)) && this.Hits(root, steps, kind, candidate))
            {
                this.Report(this.Code(candidate, code), this.loans[candidate.Loan].Site, candidate.Via, at, reservation);
                return;
            }
        }
    }

    // hits(A, X, k) (A3 5, E1, E3): within(A, X), or within(X, A) where the access is deep, or no borrow or raw Deref of X lies below A, or
    // it writes or destroys a prefix of a Loan that sits in its reservation holder across borrow Derefs (E1, SPEC 15.6.7).
    private bool Hits(int root, ReadOnlySpan<Step> steps, AccessKind kind, in Candidate candidate)
    {
        ref readonly var loan = ref this.loans[candidate.Loan];
        if (root != loan.Root)
        {
            return false;
        }

        for (var i = 0; i < Math.Min(steps.Length, loan.Count); i++)
        {
            if (!Meets(steps[i], this.loanSteps[loan.Start + i]))
            {
                return false;
            }
        }

        if (steps.Length >= loan.Count || kind is AccessKind.Move or AccessKind.SharedBorrow or AccessKind.UniqueBorrow or AccessKind.Observe)
        {
            return true;
        }

        var route = kind is AccessKind.Write or AccessKind.Destroy && this.Reserved(candidate);
        for (var i = steps.Length; i < loan.Count; i++)
        {
            if (this.loanSteps[loan.Start + i].Class is var step && (step == StepClass.Raw || (!route && step is StepClass.Shared or StepClass.Uniq)))
            {
                return false;
            }
        }

        return true;
    }

    // Whether a Loan of the check set sits in its reservation holder, where its two-phase mode is shared.
    private bool Reserved(in Candidate candidate) => this.loans[candidate.Loan].Mode == MirRefKind.TwoPhaseUniq && !candidate.Unique;

    // A conflict's code (A3 5's A6 table): a Loan found in its reservation holder is a reservation conflict, else the check's code.
    private DiagnosticCode Code(in Candidate candidate, DiagnosticCode code) => this.Reserved(candidate) ? DiagnosticCode.CallReservationConflict_Kd : code;

    // err(ended) (A3 E7, SPEC 14.8.3): at a guard candidate's EndBorrow its guard-local Origin ends, so a value live after it that carries its
    // γ (CS(ℓ), transit included) is a borrow that escapes the guard: ComparisonLoanConflict_Kd at the cleanup (DIAGNOSTICS, escaped
    // borrows), anchored at the candidate.
    private void Ended(ulong[] state, MirLocation at, int candidate)
    {
        this.Gather(state, this.live, -1, this.entryCount);
        var mark = this.guardBase + candidate;
        if (this.markSeen[mark] == this.stamp)
        {
            var anchor = this.body.Subject(this.body.Locals[candidate].Source);
            this.Report(DiagnosticCode.ComparisonLoanConflict_Kd, anchor, this.markVia[mark], at, -1);
        }
    }

    // A problem at a location with its facts (A3 6.4): the anchor of the Loan or guard candidate; its witness, a holder's next use (none
    // when that lies in a later chain), from the location itself for a reservation's check set (CS_call), or a transit value's or E's store;
    // and the reservation's anchor.
    private void Report(DiagnosticCode code, SourceRef anchor, int via, MirLocation at, int reservation)
    {
        var (subject, from) = (this.Subject(at), reservation >= 0 ? at : at with { Index = at.Index + 1 });
        var witness = via > Escaped ? this.NextUse(via, from) : subject;
        var count = 0;
        this.facts[count++] = new(MirFactKind.Anchor, anchor);
        if (witness is { } site)
        {
            this.facts[count++] = new(via > Escaped ? MirFactKind.Use : MirFactKind.Store, site);
        }

        if (reservation >= 0)
        {
            this.facts[count++] = new(MirFactKind.Reservation, this.loans[reservation].Site);
        }

        this.program.Problems.Add(new(code, subject, this.views), this.facts.AsSpan(0, count));
    }

    // A location's subject: its statement's or terminator's.
    private SourceRef Subject(MirLocation at)
    {
        ref readonly var data = ref this.body.Blocks[at.Block];
        return this.body.Subject(at.Index < data.Count ? this.body.Statements[data.Start + at.Index].Source : data.Terminator.Source);
    }

    // The witness holder's first use at or after a location in its chain (A3 6.4), or null where it is used only in a later chain.
    private SourceRef? NextUse(int slot, MirLocation from)
    {
        var (block, first) = (from.Block, from.Index);
        for (var visited = 0; visited < this.body.Blocks.Length; visited++)
        {
            ref readonly var data = ref this.body.Blocks[block];
            for (var i = first; i <= data.Count; i++)
            {
                var (start, count) = this.touched[this.Location(block, i)];
                for (var t = start; t < start + count; t++)
                {
                    if (this.touches[t].Slot == slot && (this.touches[t].Flags & Uses) != 0)
                    {
                        return this.Subject(new(block, i));
                    }
                }
            }

            var terminator = data.Terminator;
            var next = MirUses.Successors(terminator) == 1 && terminator.CheckTarget < 0 ? MirUses.Successor(this.body, terminator, 0) : -1;
            if (next < 0 || next == this.chainFirst || flow.Head(next) != flow.Head(block))
            {
                return null;
            }

            (block, first) = (next, 0);
        }

        return null;
    }

    // How a Drop under the view destroys its Place (A3 5's access, R4-M6): observing what it holds where its case Type observes and A2 keeps
    // some of it (no group: only the checks reach it), else shallowly.
    private AccessKind Dropping(int block, MirPlace place)
    {
        var group = this.program.Drops.Group(this.body.Ordinal, block, this.view);
        if (group >= 0 && this.program.Drops.RowsOf(this.body.Ordinal)[group].Verdict == MirDropVerdict.Elide)
        {
            return AccessKind.Destroy;
        }

        return this.Destruction(cases.Type(this.body.TypeOf(place)!));
    }

    // How destroying a value of a case Type accesses it (MirTypes.Observes): observing, unclassified past the walk's bound, or shallowly.
    private AccessKind Destruction(BoundType type)
    {
        if (!MirTypes.Observes(type, types, this.body.Key.Decl, this.seen, out var deep))
        {
            return AccessKind.Destroy;
        }

        return deep ? AccessKind.Unclassified : AccessKind.Observe;
    }

    // A Ref's access: a unique one borrows exclusively, the destroying reservation reads its target (A3 4's destroying(site)), another
    // borrows shared.
    private AccessKind Borrowing(MirLocation at, MirRefKind kind)
    {
        if (kind == MirRefKind.Uniq)
        {
            return AccessKind.UniqueBorrow;
        }

        var loan = this.loanAt[this.Location(at.Block, at.Index)];
        return loan >= 0 && this.loans[loan].Destroying ? AccessKind.Read : AccessKind.SharedBorrow;
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

    // The accesses of one location (A3 5's access): Copy, FakeRead, Discriminant, Len and an index read; Move; a Ref borrows (Borrowing), a
    // Reborrow uniquely; a destination is written; StorageDead destroys, and a Drop as Dropping says. An Opaque site's roots are its
    // problems' (derived, R4-M10).
    private struct Accessing(MirBorrows owner, MirLocation at, MirStatementKind statement) : IMirUses
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
            var opaque = (access & (MirAccess.Reads | MirAccess.Writes)) == (MirAccess.Reads | MirAccess.Writes);
            if (!opaque && this.Kind(place, access, kind, reborrow) is { } known)
            {
                owner.Accessed(place, known, reborrow);
            }
        }

        private readonly AccessKind? Kind(MirPlace place, MirAccess access, MirRefKind kind, bool reborrow)
        {
            if ((access & MirAccess.Kills) != 0)
            {
                return statement == MirStatementKind.StorageDead ? AccessKind.Destroy : null;
            }

            if ((access & MirAccess.DropUse) != 0)
            {
                return owner.Dropping(at.Block, place);
            }

            if ((access & MirAccess.Writes) != 0)
            {
                return AccessKind.Write;
            }

            if ((access & MirAccess.Borrows) != 0)
            {
                return owner.Borrowing(at, kind);
            }

            if ((access & MirAccess.Moves) != 0)
            {
                return AccessKind.Move;
            }

            if (reborrow)
            {
                return AccessKind.UniqueBorrow;
            }

            return (access & MirAccess.Reads) != 0 ? AccessKind.Read : null;
        }
    }
}
