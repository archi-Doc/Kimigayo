// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's location driver (A3 5, Check sets and err(conflict), with E5 and E13 (1)): at each location of the checks replay, its accesses of
// borrowed roots (MirBorrows.Accesses) with the check sets they take, a Call's or Drop's effect access after them, and the reservations it
// activates; one gather of their check sets (MirBorrows.CheckSets); each access compared with the Loans on its root by the conflicts
// relation, its first conflict recorded by MirBorrows.Reports; then a guard candidate's EndBorrow (err(ended)), W's undecided store
// (MirWrites.Limit, R4-M147, MirBorrows.Calls) and a Borrow or read the addresses do not classify (PQ-10, MirBorrows.Addresses), each a
// located Unsupported_Kd, a borrowed root's destruction past the destruction walk's bound, a call's retention Limit (R4-M51) and a store
// undecided past a walk's bound (MirBorrows.Calls), each OwnershipDepthLimit_Kd (R4-M153), a Value call whose contract is broken (G136,
// MirBorrows.CallRows' Broken) InternalInvariant_Kd, the
// location's activations (MirBorrows.Activations), its own-step departures (MirBorrows.OwnSteps), its
// effects (MirBorrows.Effects, whose keys select other case views) and at a default template's Return R26-borrow.
// Must not decide whether an access meets a Loan (MirBorrows.Checks) or which problem a conflict becomes (MirBorrows.Reports).
internal sealed partial class MirBorrows
{
    private MirStep[] accessSteps = [];

    // The checks at a location (A3 5, E13 (1)): its accesses of borrowed roots, a guard candidate's EndBorrow, its activations, its
    // own-step departures and its effects, on the check sets of one gather; the first conflicting Loan of each access is its problem; at a
    // Return, R26-borrow on its own gather.
    private void Check(ulong[] state, int block, int index, int location)
    {
        this.accessCount = 0;
        var at = new MirLocation(block, index);
        var statement = index < this.body.Blocks[block].Count ? this.body.Statements[location] : default;
        var visitor = new Accessing(this, at, this.Reborrowing(statement));
        MirUses.ForEach(cases, at, ref visitor);
        var (accessed, scope) = (this.accessCount, this.Source(location).Scope); // The location's own accesses; its effect access follows them.
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
                this.Limit(this.Subject(at), MirOutcome.Deep); // A borrowed root's destruction past the destruction walk's bound (R4-M153).
                continue;
            }

            var count = this.placeSteps.Walk(this.body, access.Place, access.Reborrow, ref this.accessSteps);
            if (this.Conflict(this.Key(access.Place.Root), this.accessSteps.AsSpan(0, count), access.Kind, access.Taking, scope, -1) is var c and not None)
            {
                this.Report(this.Code(this.candidates[c], access.Kind, DiagnosticCode.ComparisonLoanConflict_Kd), c, access.Taking, at, -1, null);
            }
        }

        if (ended)
        {
            this.Ended(at, statement.Place.Root);
        }

        var terminator = index == this.body.Blocks[block].Count;
        if (this.layerBorrowed[location])
        {
            this.Limit(this.Subject(at), MirOutcome.Limit); // A `during self` layer's enclosing Place (PQ-10).
        }

        if (terminator)
        {
            // The call's store, one verdict: W's undecided store (R4-M147), a store undecided past a walk's bound and a retention limit
            // (R4-M51, R4-M153), joined.
            var deep = this.deep || this.Retaining(this.body.Blocks[block].Terminator) is { IsLimit: true };
            this.Limit(this.Subject(at), (this.limit ? MirOutcome.Limit : MirOutcome.None).Join(deep ? MirOutcome.Deep : MirOutcome.None));
        }

        if (terminator && (this.Broken(this.body.Blocks[block].Terminator) || this.brokenDrop))
        {
            this.Internal(at); // G136 (R4-M148); a destruction's Value row at its receiver (BU-47, MirBorrows.Destructions).
        }

        (this.limit, this.deep, this.brokenDrop) = (false, false, false);

        this.Activations(at);
        this.Departures(at, accessed);
        if (accessed < this.accessCount)
        {
            this.Effects(at, this.accesses[accessed].Taking);
        }

        if (index == this.body.Blocks[block].Count && this.body.Blocks[block].Terminator.Kind == MirTerminatorKind.Return)
        {
            this.DefaultBorrows(state);
        }
    }
}
