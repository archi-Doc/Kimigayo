// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's location driver (A3 5, Check sets and err(conflict), with E5 and E13 (1)): at each location of the checks replay, its accesses of
// borrowed roots, each classified by what it does (MirBorrows.Checks' AccessKind) and with the check set it takes, a Call's or Drop's
// effect access after them (MirBorrows.Effects), and the reservations it activates; one gather of their check sets (MirBorrows.CheckSets);
// each access compared with the Loans on its root by the conflicts relation, its first conflict recorded by MirBorrows.Reports; then a
// guard candidate's EndBorrow (err(ended)), a call's retention Limit, the location's activations (MirBorrows.Activations) and last its
// effects (MirBorrows.Effects, whose keys select other case views). Its classifiers Dropping and Destruction are also read by
// MirBorrows.Liveness and MirBorrows.Activations.
// Must not decide whether an access meets a Loan (MirBorrows.Checks) or which problem a conflict becomes (MirBorrows.Reports).
internal sealed partial class MirBorrows
{
    private Access[] accesses = [];
    private Step[] accessSteps = [];
    private int accessCount;

    // The checks at a location (A3 5, E13 (1)): its accesses of borrowed roots, a guard candidate's EndBorrow, its activations and its
    // effects, on the check sets of one gather; the first conflicting Loan of each access is its problem.
    private void Check(ulong[] state, int block, int index, int location)
    {
        this.accessCount = 0;
        var at = new MirLocation(block, index);
        var statement = index < this.body.Blocks[block].Count ? this.body.Statements[location] : default;
        var visitor = new Accessing(this, at, index < this.body.Blocks[block].Count ? statement.Kind : MirStatementKind.Assign, this.Reborrowing(statement));
        MirUses.ForEach(cases, at, ref visitor);
        var accessed = this.accessCount; // The location's own accesses; its effect access follows them.
        this.Effecting(block, index);
        this.Reserving(state, at);
        var ended = statement is { Kind: MirStatementKind.EndBorrow, Place.Root: var guard } && this.MarkerOf(guard) == Marker.Guard;
        if (this.accessCount + this.reserving.Count > 0 || ended)
        {
            this.Gather(state, at, this.entryCount);
        }

        for (var a = 0; a < accessed; a++)
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
                this.Report(this.Code(this.candidates[c], access.Kind, DiagnosticCode.ComparisonLoanConflict_Kd), c, access.Taking, at, -1, null);
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
        if (accessed < this.accessCount)
        {
            this.Effects(at, this.accesses[accessed].Taking);
        }
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
