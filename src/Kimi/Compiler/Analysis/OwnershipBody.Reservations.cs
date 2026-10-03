// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipBody
{
    private int[] reservationPlaces = [];

    // A reserved input whose own preparation meets a retained Loan, held until activation is decided (SPEC 15.6.7).
    private List<(int Reservation, OwnershipIssue Issue)>? preparedLoanConflicts;

    // Each call whose activation meets a retained Loan, with the Loan's holder, also where one record states several Loans.
    private List<(Koto Call, Koto? Loan)>? activatedLoans;

    // Pairs of reservations whose overlap a preparation record states, and the activation records that pair would repeat.
    private List<(int First, int Second)>? reservationOverlaps;
    private List<(int Reservation, int Other, OwnershipIssue Issue)>? overlapActivations;

    // An element write that only enclosing reservation Loans reject while the plan is built, held until the plan is complete.
    private List<(int Projection, OwnershipIssue Issue)>? reservedElementWrites;

    internal void HoldReservedElementWrite(int projection, OwnershipIssue issue)
        => (this.reservedElementWrites ??= new()).Add((projection, issue));

    // SPEC 15.6.7: the completed plan reports a write during preparation once, as a conflict with the reservation it relates. The
    // held record stands when no such record exists for the write, such as for an incomplete plan or a write the plan accepts.
    internal void ReportReservedElementWrites(bool completed)
    {
        if (this.reservedElementWrites is not { Count: > 0 } held)
        {
            return;
        }

        for (var i = 0; i < held.Count; i++)
        {
            var (projection, issue) = held[i];
            var write = completed ? this.Projections[projection].Write : -1;
            var stated = false;
            for (var j = 0; write >= 0 && j < this.IssueStorage.Count && !stated; j++)
            {
                var other = this.IssueStorage[j];
                stated = other is { Failure: OwnershipFailure.ComparisonLoanConflict, Activation: false, Reservation: >= 0 } && ReferenceEquals(other.Source, this.Operations[write].Source);
            }

            if (!stated)
            {
                this.ReportIssue(issue);
            }
        }

        held.Clear();
    }

    internal void PrepareCallReservations()
    {
        Grow(ref this.reservationPlaces, this.Places.Count);
        this.reservationPlaces.AsSpan(0, this.Places.Count).Fill(-1);
        for (var i = 0; i < this.CallReservations.Count; i++)
        {
            if (this.CallReservations[i].Place >= 0)
            {
                this.reservationPlaces[this.CallReservations[i].Place] = i;
            }

            if (this.CallReservations[i].Loaded >= 0)
            {
                // The stored reference loaded for the reserved Reborrow is held in the reservation's mode.
                this.reservationPlaces[this.CallReservations[i].Loaded] = i;
            }
        }
    }

    internal void VerifyCallReservations()
    {
        if (this.CallReservations.Count == 0)
        {
            return;
        }

        for (var op = 0; op < this.Operations.Count; op++)
        {
            for (var loan = this.LoanInputs[op]; loan >= 0; loan = this.ComparisonLoans[loan].Parent)
            {
                var reserved = this.ComparisonLoans[loan].Reservation;
                var own = this.Operations[op].Reservation;
                if ((reserved >= 0 || own >= 0) && this.ConflictsWithLoan(op, loan))
                {
                    var issue = new OwnershipIssue(this.Operations[op].Source, OwnershipFailure.ComparisonLoanConflict, Reservation: reserved >= 0 ? reserved : own);
                    if (reserved >= 0)
                    {
                        this.ReportReservationConflict(issue, own, reserved);
                    }
                    else
                    {
                        this.ReportIssue(issue with { Input = this.Lending(own) });
                    }
                }

                if (this.Operations[op].Kind == OwnershipOperationKind.ActivateCallBorrows)
                {
                    for (var r = this.Operations[op].Reservation; r >= 0; r = this.CallReservations[r].Next)
                    {
                        var reservation = this.CallReservations[r];
                        if (reservation.Activation == op && reservation.Loan != loan && this.ConflictsWithLoan(reservation.Borrow, loan, op))
                        {
                            var issue = new OwnershipIssue(reservation.Call, OwnershipFailure.ComparisonLoanConflict, Reservation: r, Activation: true, Input: this.Lending(r));
                            if (reserved >= 0)
                            {
                                this.HoldOverlapActivation(r, reserved, issue);
                            }
                            else
                            {
                                this.ReportIssue(issue);
                            }
                        }
                    }
                }
            }
        }

        this.ReportOverlapActivations();
    }

    // A reservation whose Borrow was never completed has no lending point to show.
    private OwnershipLending? Lending(int reservation)
        => (uint)reservation < (uint)this.CallReservations.Count && this.CallReservations[reservation] is { Borrow: >= 0 } prepared ? new(this.Operations[prepared.Borrow].Source, prepared.Call) : null;

    // SPEC 15.6.7: an operation that conflicts with a reservation during preparation is the conflicting use; the reservation's
    // lending point is related. A reserved input that meets another reservation states their overlap once, here.
    private void ReportReservationConflict(OwnershipIssue issue, int own, int reserved)
    {
        if (own >= 0)
        {
            (this.reservationOverlaps ??= new()).Add((Math.Min(own, reserved), Math.Max(own, reserved)));
        }

        this.ReportIssue(issue with { Input = this.Lending(own), ConflictingReservation = this.Lending(reserved) });
    }

    private void HoldOverlapActivation(int reservation, int other, OwnershipIssue issue)
        => (this.overlapActivations ??= new()).Add((reservation, other, issue));

    // Two overlapping reservations cannot activate together. When the later one already conflicted with the earlier one during
    // preparation, that record relates both lending points and the activation repeats it; otherwise the activation stands. An
    // Other of -1 is the call's own effect on a reserved Place, repeated by any stated overlap of that reservation.
    private void ReportOverlapActivations()
    {
        if (this.overlapActivations is not { Count: > 0 } held)
        {
            return;
        }

        for (var i = 0; i < held.Count; i++)
        {
            var (reservation, other, issue) = held[i];
            var stated = false;
            for (var j = 0; this.reservationOverlaps is { } overlaps && j < overlaps.Count && !stated; j++)
            {
                var (first, second) = overlaps[j];
                stated = other < 0 ? first == reservation || second == reservation : first == Math.Min(reservation, other) && second == Math.Max(reservation, other);
            }

            if (!stated)
            {
                this.ReportIssue(issue);
            }
        }

        held.Clear();
    }

    // SPEC 15.6.7: the target is evaluated and located once. An unreserved Read of the Place that the reserved Borrow at the
    // same lending point directly follows is that location, so its conflicts are the reserved input's own.
    private int LocatedReservation(int op)
        => op + 1 < this.Operations.Count && this.Operations[op] is { Kind: OwnershipOperationKind.Read, Reservation: < 0 } read &&
            this.Operations[op + 1] is { Kind: OwnershipOperationKind.Borrow, Reservation: >= 0 } borrow && borrow.Place == read.Place &&
            ReferenceEquals(KotoHelper.UnwrapParentheses(read.Source), KotoHelper.UnwrapParentheses(borrow.Source)) ? borrow.Reservation : -1;

    // SPEC 15.6.7: a reservation protects its target, not the Loans an owned target retains on other roots. A reserved input's
    // Place that depends on such a root only through its referent's Type carries the target's retained Loan.
    private int RetainingTarget(int reservation, int root, int count)
    {
        var borrow = this.CallReservations[reservation].Borrow;
        var target = borrow >= 0 ? this.Operations[borrow].Place : -1;
        return target >= 0 && target != root && !ReferenceTypes.IsBorrow(this.Places[target].Type) && !ReferenceTypes.IsString(this.Places[target].Type) &&
            this.borrowDependencies[(target * count) + root] != LoanRequirement.None ? target : -1;
    }

    // SPEC 15.6.7: a reserved input that meets a retained Loan conflicts with that Loan, not with a call reservation. When the
    // Loan still conflicts at the call's activation, the activation record states the problem, also when it relates another
    // Loan that conflicts there; otherwise the Loan ended during preparation and this record stands alone. Matching by source
    // decides every deferred expansion of one call alike.
    private void ReportPreparedLoanConflicts()
    {
        if (this.preparedLoanConflicts is not { Count: > 0 } conflicts)
        {
            return;
        }

        for (var i = 0; i < conflicts.Count; i++)
        {
            var (reservation, issue) = conflicts[i];
            var call = this.CallReservations[reservation].Call;
            var activated = false;
            for (var j = 0; this.activatedLoans is { } loans && j < loans.Count && !activated; j++)
            {
                activated = ReferenceEquals(loans[j].Call, call) && ReferenceEquals(loans[j].Loan, issue.LoanSource);
            }

            if (!activated)
            {
                this.ReportIssue(issue);
            }
        }

        conflicts.Clear();
    }

    private bool ValidateCallReservations()
    {
        var count = 0;
        for (var op = 0; op < this.Operations.Count; op++)
        {
            var operation = this.Operations[op];
            if (operation.Kind == OwnershipOperationKind.ActivateCallBorrows)
            {
                if ((uint)operation.Reservation >= (uint)this.CallReservations.Count)
                {
                    return false;
                }

                var entry = op + 1;
                while (entry < this.Operations.Count && this.Operations[entry].Kind == OwnershipOperationKind.CallEntry && ReferenceEquals(this.Operations[entry].Source, operation.Source))
                {
                    entry++;
                }

                // A call activates its reserved arguments right before it is entered; a literal activates each placed exclusive
                // reference right before it places or stores it (PLAN G53).
                if (entry == this.Operations.Count ||
                    (operation.Source is not InvocationKoto ? this.Operations[entry].Kind is not (OwnershipOperationKind.PayloadPlacement or OwnershipOperationKind.StoreDictionaryEntry)
                        : !ReferenceEquals(this.Operations[entry].Source, operation.Source) ||
                        (operation.Source is InvocationKoto { BoundCall.Target.CompilerFunction: CompilerFunctionKind.Replace or CompilerFunctionKind.Exchange or CompilerFunctionKind.Swap }
                            ? this.Operations[entry].Kind is not (OwnershipOperationKind.Consume or OwnershipOperationKind.Write or OwnershipOperationKind.UpdateBorrowed)
                            : this.Operations[entry].Kind != OwnershipOperationKind.Call)))
                {
                    return false;
                }

                for (var r = operation.Reservation; r >= 0; r = this.CallReservations[r].Next)
                {
                    if ((uint)r >= (uint)this.CallReservations.Count || this.CallReservations[r].Activation != op ||
                        (this.CallReservations[r].Next >= 0 && this.CallReservations[r].Next <= r))
                    {
                        return false;
                    }

                    count++;
                }
            }
            else if (operation.Reservation >= 0 && ((uint)operation.Reservation >= (uint)this.CallReservations.Count || this.CallReservations[operation.Reservation].Borrow != op))
            {
                return false;
            }
        }

        return count == this.CallReservations.Count;
    }

    private LoanRequirement ReservationMode(int reservation, int point)
        => this.CallReservations[reservation].Activation is >= 0 and var activation && point >= activation ? LoanRequirement.Uniq : LoanRequirement.Ref;

    private LoanRequirement BorrowModeAt(int place, int root, int point, LoanRequirement mode)
    {
        var recorded = this.HasStoredBorrowRecord(place, root);
        if (mode != LoanRequirement.None && recorded && !this.StoredBorrowActive(place, root, point))
        {
            return LoanRequirement.None;
        }

        if (mode != LoanRequirement.None && this.Places[place].Source is DictionaryLiteralKoto)
        {
            // A literal's handle is initialized while still empty. Its final Type can name later entries' Origins;
            // only a completed placement starts the handle's dependency on a particular referent.
            if (!recorded)
            {
                return LoanRequirement.None;
            }
        }

        // The slot borrow and the loaded reference of a reserved stored-reference argument reserve every Loan they hold.
        var reservation = this.reservationPlaces[place];
        return mode == LoanRequirement.Uniq && reservation >= 0 && this.ReservationMode(reservation, point) == LoanRequirement.Ref &&
            (this.CallReservations[reservation].Argument >= 0 || place == this.CallReservations[reservation].Loaded ||
                (this.Places[place].Type.Origin is { } origin && this.OriginNamesRoot(origin, root))) ? LoanRequirement.Ref : mode;
    }

    private bool OriginNamesRoot(BoundOrigin origin, int root)
    {
        if (origin.Kind == OriginKind.Intersection)
        {
            foreach (var operand in origin.Operands)
            {
                if (this.OriginNamesRoot(operand, root))
                {
                    return true;
                }
            }

            return false;
        }

        if (origin.Kind == OriginKind.Anchor)
        {
            return this.Places[root].Kind == OwnershipPlaceKind.Anchor && ReferenceEquals(this.Places[root].Source, origin.Binder);
        }

        if (origin.Kind is OriginKind.Projection or OriginKind.Input)
        {
            foreach (var pair in this.SymbolPlaces)
            {
                if (pair.Value == root && ReferenceEquals(pair.Key.Declaration, origin.Binder) &&
                    pair.Key.Slot == (origin.Kind == OriginKind.Input ? origin.InputIndex : origin.Slot) &&
                    (origin.Kind != OriginKind.Input || pair.Key.Kind == BindingSymbolKind.Parameter))
                {
                    return true;
                }
            }

            return origin.Kind == OriginKind.Projection && this.Places[root].Kind is OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result &&
                ReferenceEquals(this.Places[root].Source, origin.Binder);
        }

        return false;
    }

    private bool ValidCallReservation(int id, int loan)
    {
        if ((uint)id >= (uint)this.CallReservations.Count)
        {
            return false;
        }

        var reservation = this.CallReservations[id];
        if ((uint)reservation.Borrow >= (uint)this.Operations.Count || (uint)reservation.Activation >= (uint)this.Operations.Count ||
            reservation.Loan != loan || reservation.Activation <= reservation.Borrow ||
            reservation.Next < -1 || reservation.Next >= this.CallReservations.Count ||
            (reservation.Next >= 0 && (reservation.Next <= id || this.CallReservations[reservation.Next].Activation != reservation.Activation)))
        {
            return false;
        }

        var borrow = this.Operations[reservation.Borrow];
        var state = this.ComparisonLoans[loan];
        return borrow.Reservation == id && borrow.Kind is OwnershipOperationKind.Borrow or OwnershipOperationKind.UpdateTarget or OwnershipOperationKind.Read &&
            (uint)borrow.Place < (uint)this.Places.Count && state.Parent >= -1 && state.Parent < loan &&
            !state.Access && state.Guard == -1 && state.Projection == -1 &&
            (borrow.Kind == OwnershipOperationKind.Borrow
                ? reservation.Place == borrow.Input && (uint)borrow.Input < (uint)this.Places.Count && this.Places[borrow.Input].Type.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq && ReferenceEquals(state.Call, reservation.Call)
                : reservation.Place == -1 && (borrow.Kind == OwnershipOperationKind.Read ? this.ValidCallableLoan(loan) : this.ValidElementWriteLoan(loan))) &&
            (borrow.Kind == OwnershipOperationKind.Read || borrow.LoanMode == LoanRequirement.Uniq) && state.Mode == LoanRequirement.Uniq &&
            state.Read == reservation.Borrow && state.Place == borrow.Place && state.Parent < loan && state.Depth > 0 &&
            this.LoanStates[reservation.Borrow] == loan && this.LoanInputs[reservation.Borrow] == state.Parent &&
            this.Operations[reservation.Activation].Kind == OwnershipOperationKind.ActivateCallBorrows &&
            ReferenceEquals(this.Operations[reservation.Activation].Source, reservation.Call);
    }
}
