// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipBody
{
    private int[] reservationPlaces = [];

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
                if ((this.ComparisonLoans[loan].Reservation >= 0 || this.Operations[op].Reservation >= 0) && this.ConflictsWithLoan(op, loan))
                {
                    var reservation = this.ComparisonLoans[loan].Reservation >= 0 ? this.ComparisonLoans[loan].Reservation : this.Operations[op].Reservation;
                    this.ReportIssue(new(this.Operations[op].Source, OwnershipFailure.ComparisonLoanConflict, Reservation: reservation));
                }

                if (this.Operations[op].Kind == OwnershipOperationKind.ActivateCallBorrows)
                {
                    for (var r = this.Operations[op].Reservation; r >= 0; r = this.CallReservations[r].Next)
                    {
                        var reservation = this.CallReservations[r];
                        if (reservation.Activation == op && reservation.Loan != loan && this.ConflictsWithLoan(reservation.Borrow, loan, op))
                        {
                            this.ReportIssue(new(reservation.Call, OwnershipFailure.ComparisonLoanConflict, Reservation: r, Activation: true));
                        }
                    }
                }
            }
        }
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

                if (entry == this.Operations.Count || !ReferenceEquals(this.Operations[entry].Source, operation.Source) ||
                    (operation.Source is InvocationKoto { BoundCall.Target.CompilerFunction: CompilerFunctionKind.Replace or CompilerFunctionKind.Exchange or CompilerFunctionKind.Swap }
                        ? this.Operations[entry].Kind is not (OwnershipOperationKind.Consume or OwnershipOperationKind.Write or OwnershipOperationKind.UpdateBorrowed)
                        : this.Operations[entry].Kind != OwnershipOperationKind.Call))
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
        var reservation = this.reservationPlaces[place];
        return mode == LoanRequirement.Uniq && reservation >= 0 && this.ReservationMode(reservation, point) == LoanRequirement.Ref &&
            this.Places[place].Type.Origin is { } origin && this.OriginNamesRoot(origin, root) ? LoanRequirement.Ref : mode;
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
