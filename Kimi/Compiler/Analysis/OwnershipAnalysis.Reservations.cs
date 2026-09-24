// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private static bool IsDirectExclusiveBorrow(Koto source)
        => KotoHelper.UnwrapParentheses(source) is ConversionKoto { ConversionBinding: ConversionBinding.Borrow or ConversionBinding.PayloadBorrow, BoundType.Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq };

    private bool HasCallInspection(int place)
    {
        var value = this.Value(place);
        // Address values use Origin liveness; reads of guard candidates retain their
        // enclosing guard Loan. Only a new inspection has its own call-wide extent.
        return value >= 0 && this.body.Values[value].Kind == OwnershipValueKind.Borrow &&
            this.body.Operations[value].Kind == OwnershipOperationKind.Borrow;
    }

    private int PrepareCallArgument(InvocationKoto call, Koto source, BoundArgumentOperation argument, bool immediate = false)
    {
        var stringElement = BorrowedArgumentSource(source) is IndexKoto index &&
            (index.Left.BoundType?.Kind is BoundTypeKind.Slice or BoundTypeKind.Array || ReferenceTypes.IsDynamicArray(index.Left.BoundType));
        if (argument.Kind == ArgumentOperationKind.Borrow && ReferenceTypes.IsString(argument.ParameterType) && !stringElement)
        {
            // Preserve the call-wide inspection plan for implicit string arguments.
            // Stored references use the same Origin-based liveness as all other borrows.
            return this.BorrowArgument(call, argument);
        }

        if (ReferenceTypes.IsBorrow(argument.ParameterType) &&
            (argument.Kind is ArgumentOperationKind.Borrow or ArgumentOperationKind.Reborrow or ArgumentOperationKind.PayloadProjection ||
                (argument.Kind == ArgumentOperationKind.Value && IsDirectExclusiveBorrow(source))))
        {
            // Lifetime fitting may shorten several arguments to the same Origin.
            // Keep each prepared borrow's actual source dependency and Loan footprint.
            var type = this.compilation.Binding.PreparedBorrowType(source, argument.ParameterType!);
            var direct = KotoHelper.UnwrapParentheses(source);
            // An explicit adaptation and its call-only reborrow are one preparation.
            // Never peel a call, selection, capture or storage boundary.
            if (!this.compilation.Binding.ReadsReferent(source) && direct is ConversionKoto { ConversionBinding: ConversionBinding.Borrow or ConversionBinding.PayloadBorrow } conversion &&
                conversion.BoundType?.Semantics == type.Semantics &&
                ReferenceEquals(conversion.BoundType.Components[0], type.Components[0]))
            {
                source = conversion.Left;
            }

            var reservation = !immediate && type.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq ? this.NewCallReservation(call) : -1;
            var result = this.BorrowStruct(source, type, reservation);
            if (reservation >= 0 && result >= 0)
            {
                this.CompleteCallReservation(reservation, this.Value(result), result);
            }

            return result;
        }

        return this.Argument(source, argument.Kind);
    }

    private int NewCallReservation(InvocationKoto call)
    {
        var id = this.body.CallReservations.Count;
        this.body.CallReservations.Add(new(call));
        return id;
    }

    private void CompleteCallReservation(int id, int borrow, int place = -1, int loan = -1)
    {
        var reservation = this.body.CallReservations[id];
        if (loan < 0)
        {
            loan = this.BeginSharedLoan(this.body.Operations[borrow].Place, reservation.Call);
        }

        this.body.ComparisonLoans[loan] = this.body.ComparisonLoans[loan] with { Mode = LoanRequirement.Uniq, Reservation = id };
        this.body.CallReservations[id] = reservation with { Borrow = borrow, Place = place, Loan = loan };
    }

    private void ActivateCallReservations(InvocationKoto call, int mark)
    {
        var activation = this.body.Operations.Count;
        var head = -1;
        for (var i = this.body.CallReservations.Count - 1; i >= mark; i--)
        {
            var reservation = this.body.CallReservations[i];
            if (ReferenceEquals(reservation.Call, call) && reservation.Borrow >= 0)
            {
                this.body.CallReservations[i] = reservation with { Activation = activation, Next = head };
                head = i;
            }
        }

        if (head >= 0)
        {
            this.Emit(OwnershipOperationKind.ActivateCallBorrows, call, reservation: head);
        }
    }
}
