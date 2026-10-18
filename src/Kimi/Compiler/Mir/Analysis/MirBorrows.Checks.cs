// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

// A5's conflict check (A3 5, Check sets and err(conflict), with E1, E3, E5 and E13 (1)): at an access of a borrowed root, the Loans of its
// check set (MirBorrows.CheckSets) on that root, by holder, compared with the access syntactically; a guard candidate's EndBorrow against
// the values that keep its guard-local Origin (E7); then the location's activations (MirBorrows.Activations).
internal sealed partial class MirBorrows
{
    private readonly MirFact[] facts = new MirFact[3];
    private Access[] accesses = [];
    private Step[] accessSteps = [];
    private int accessCount;

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

        return left.Kind == right.Kind && (left.Kind == MirProjectionKind.Deref || left.Arg == right.Arg);
    }

    // The checks at a location (A3 5, E13 (1)): its accesses of borrowed roots, a guard candidate's EndBorrow and its activations, on the
    // check sets of one gather; the first conflicting Loan of each access is its problem.
    private void Check(ulong[] state, int block, int index, int location)
    {
        this.accessCount = 0;
        var at = new MirLocation(block, index);
        var statement = index < this.body.Blocks[block].Count ? this.body.Statements[location] : default;
        var visitor = new Accessing(this, at, index < this.body.Blocks[block].Count ? statement.Kind : MirStatementKind.Assign, this.Reborrowing(statement));
        MirUses.ForEach(cases, at, ref visitor);
        this.Reserving(state, at);
        var ended = statement is { Kind: MirStatementKind.EndBorrow, Place.Root: var guard } && this.body.Locals[guard].Role == MirLocalRole.GuardCandidate;
        if (this.accessCount + this.reserving.Count > 0 || ended)
        {
            this.Gather(state, at, this.entryCount);
        }

        for (var a = 0; a < this.accessCount; a++)
        {
            var access = this.accesses[a];
            if (access.Kind == AccessKind.Unclassified)
            {
                this.Unsupported(at);
                continue;
            }

            var count = this.Walk(access.Place, access.Reborrow, ref this.accessSteps);
            if (this.Conflict(this.Key(access.Place.Root), this.accessSteps.AsSpan(0, count), access.Kind, access.Taking) is var c and not None)
            {
                this.Report(this.Code(this.candidates[c], DiagnosticCode.ComparisonLoanConflict_Kd), c, access.Taking, at, -1);
            }
        }

        if (ended)
        {
            this.Ended(at, statement.Place.Root);
        }

        if (index == this.body.Blocks[block].Count && this.Retaining(this.body.Blocks[block].Terminator) is { IsLimit: true })
        {
            this.Unsupported(at); // A contract Binding does not instantiate.
        }

        this.Activations(at);
    }

    // The first Loan on an access's root in its check set (Takes) it conflicts with (A3 5's conflicts and hits), None for none: a write,
    // Move, exclusive borrow or destruction with any mode, another access with a unique one.
    private int Conflict(int root, ReadOnlySpan<Step> steps, AccessKind kind, int taking)
    {
        for (var c = this.First(root); c != None; c = this.candidates[c].Next)
        {
            ref readonly var candidate = ref this.candidates[c];
            this.Work++;
            if (candidate.Takes(taking) && (candidate.Unique || kind is not (AccessKind.Read or AccessKind.SharedBorrow)) &&
                this.Hits(root, steps, kind, candidate))
            {
                return c;
            }
        }

        return None;
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
    private void Ended(MirLocation at, int candidate)
    {
        var mark = this.guardBase + candidate;
        if (this.markSeen[mark] == this.stamp)
        {
            var anchor = this.body.Subject(this.body.Locals[candidate].Source);
            this.Report(DiagnosticCode.ComparisonLoanConflict_Kd, anchor, this.markVia[mark], at, -1);
        }
    }

    // A conflicting candidate's problem: its Loan's anchor, and its witness for the check set (E13 (6)): a holder it was found live in (for
    // an activation, other than its own), else the location, whose operation uses the value that carries it.
    private void Report(DiagnosticCode code, int candidate, int taking, MirLocation at, int reservation)
    {
        ref readonly var found = ref this.candidates[candidate];
        var live = taking <= Activating ? (found.Flags & Based) != 0 && Activation(found.Via) != taking : (found.Flags & (Based | Stored)) != 0;
        this.Report(code, this.loans[found.Loan].Site, live ? found.Via : Taken, at, reservation);
    }

    // A Loan problem at a location with its facts (A3 6.4): the anchor of the Loan or guard candidate; its witness, a holder's next use (none
    // when that lies in a later chain), from the location itself for a reservation's check set, the location for a value it takes, or a
    // transit value's or E's store; and the reservation's anchor.
    private void Report(DiagnosticCode code, SourceRef anchor, int via, MirLocation at, int reservation)
    {
        var (subject, from) = (this.Subject(at), reservation >= 0 ? at : at with { Index = at.Index + 1 });
        var witness = via > Escaped ? this.NextUse(via, from) : subject;
        var count = 0;
        this.facts[count++] = new(MirFactKind.Anchor, anchor);
        if (witness is { } site)
        {
            this.facts[count++] = new(via > Escaped || via == Taken ? MirFactKind.Use : MirFactKind.Store, site);
        }

        if (reservation >= 0)
        {
            this.facts[count++] = new(MirFactKind.Reservation, this.loans[reservation].Site);
        }

        var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.ComparisonLoanConflict); // The four Loan codes' requirement.
        this.program.Problems.Add(new(code, requirement, subject, this.views), this.facts.AsSpan(0, count));
    }

    // A located Unsupported_Kd at a location whose access or call the stratum does not classify.
    private void Unsupported(MirLocation at)
    {
        var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.Unsupported);
        this.program.Problems.Add(new(DiagnosticCode.Unsupported_Kd, requirement, this.Subject(at), this.views));
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

    private void Accessed(in Access access)
    {
        if (this.borrowed[this.Key(access.Place.Root)])
        {
            MirDataflow.Grow(ref this.accesses, this.accessCount + 1);
            this.accesses[this.accessCount++] = access;
        }
    }

    /// <summary>An access of a borrowed root: its Place, a Reborrow's of its Place's referent, its kind and its check set (A3 E13 (1)):
    /// its operand's index from 0, TakesParents for a borrow, TakesValue for an observing destruction, else TakesStores.</summary>
    private readonly record struct Access(MirPlace Place, AccessKind Kind, bool Reborrow, int Taking);

    // The accesses of one location (A3 5's access, E13 (1)): Copy, FakeRead, Discriminant, Len and an index read; Move; a Ref borrows
    // (Borrowing), a Reborrowing PairAcquire too, as its Use's marker says (reborrow; TwoPhaseUniq reserves, a shared access); a
    // destination is written; StorageDead destroys, and a Drop as Dropping says. Each operand's accesses, its index locals' and its own,
    // take its operand's check set. An Opaque site's roots are its problems' (derived, R4-M10).
    private struct Accessing(MirBorrows owner, MirLocation at, MirStatementKind statement, MirRefKind reborrow) : IMirUses
    {
        private MirOperand operand;
        private int operands; // The operands reported: the current one's index plus 1.
        private bool inOperand; // Whether the Places reported are the current operand's.

        public void Operand(in MirOperand operand)
            => (this.operand, this.operands, this.inOperand) = (operand, this.operands + 1, operand.Kind != MirOperandKind.Const);

        public readonly void Effect(MirEffect effects, int site)
        {
        }

        public void Place(MirPlace place, MirAccess access, MirRefKind kind)
        {
            var (taking, reborrows) = (this.inOperand ? this.operands - 1 : TakesStores, this.inOperand && this.operand.Kind == MirOperandKind.PairAcquire &&
                reborrow != MirRefKind.Raw && this.operand.Place == place);
            this.inOperand &= this.operand.Place != place; // An operand's index locals precede its Place.
            var opaque = (access & (MirAccess.Reads | MirAccess.Writes)) == (MirAccess.Reads | MirAccess.Writes);
            if (!opaque && this.Kind(place, access, reborrows ? reborrow : kind, reborrows) is { } known)
            {
                taking = known == AccessKind.Observe ? TakesValue : reborrows || (access & MirAccess.Borrows) != 0 ? TakesParents : taking;
                owner.Accessed(new(place, known, reborrows, taking));
            }
        }

        private readonly AccessKind? Kind(MirPlace place, MirAccess access, MirRefKind kind, bool reborrows)
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

            if (reborrows || (access & MirAccess.Borrows) != 0)
            {
                return owner.Borrowing(at, kind);
            }

            if ((access & MirAccess.Moves) != 0)
            {
                return AccessKind.Move;
            }

            return (access & MirAccess.Reads) != 0 ? AccessKind.Read : null;
        }
    }
}
