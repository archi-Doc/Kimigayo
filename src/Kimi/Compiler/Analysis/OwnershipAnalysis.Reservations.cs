// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private static bool IsDirectExclusiveBorrow(Koto source)
        => KotoHelper.UnwrapParentheses(source) is ConversionKoto { ConversionBinding: ConversionBinding.Borrow, BoundType.Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq };

    // SPEC 9.6.1: a shared receiver selected through a base path is the whole receiver, borrowed at its own Type (`whole`), whose base
    // prefix is then lent at the parameter Type (`type`). Binding rejects an exclusive or unproven projection first (OCC-X).
    private bool BaseBorrowTypes(Koto source, in BoundArgumentOperation argument, out BoundType type, out BoundType whole)
    {
        type = whole = null!;
        if (argument.ObjectCompatibility != ConstraintProof.Proven || argument.ParameterType?.Semantics is not (SemanticsKind.Ref or SemanticsKind.ObjRef) ||
            argument.SourceType is not { } receiverType || argument.BasePath is null)
        {
            this.Internal(source);
            return false;
        }

        type = argument.AdaptedType ?? this.compilation.Binding.PreparedBorrowType(source, argument.ParameterType);
        var core = type.IsObjectBorrow ? ReferenceTypes.ViewTarget(receiverType)! : ReferenceTypes.IsReference(receiverType) || receiverType.HandleMode is not null || receiverType.IsObjectBorrow ? receiverType.Components[0] : receiverType;
        whole = this.compilation.Types.Reference(type.Semantics, core, type.Origin);
        return true;
    }

    private int PrepareCallArgument(InvocationKoto call, Koto source, BoundArgumentOperation argument, bool immediate = false)
    {
        if (argument.Kind == ArgumentOperationKind.BaseBorrow)
        {
            // Acquire the complete source once by the ordinary Place path, then lend its base prefix.
            // Keeping the parent reference retains the original storage/Loan anchor through the call and its result.
            if (!this.BaseBorrowTypes(source, argument, out var type, out var whole))
            {
                return -1;
            }

            var reference = this.BorrowStruct(source, whole);
            return reference < 0 ? -1 : this.BorrowThrough(call, reference, type, -1);
        }

        // SPEC 4.7.1: a string element reached through a sequence or a reference is borrowed through the common element borrow.
        var stringElement = ElementAccess.IsBorrowedSelection(BorrowedArgumentSource(source)) || ElementAccess.IsIndexerCall(BorrowedArgumentSource(source)) || ElementAccess.IsUserIndex(BorrowedArgumentSource(source));
        if (argument.Kind == ArgumentOperationKind.Borrow && ReferenceTypes.IsString(argument.ParameterType) && !stringElement && !this.SpecialField(BorrowedArgumentSource(source)))
        {
            // Preserve the call-wide inspection plan for implicit string arguments.
            // Stored references use the same Origin-based liveness as all other borrows.
            return this.BorrowArgument(call, argument);
        }

        if (ReferenceTypes.IsBorrow(argument.ParameterType) &&
            (argument.Kind is ArgumentOperationKind.Borrow or ArgumentOperationKind.Reborrow or ArgumentOperationKind.PayloadProjection ||
                (argument.Kind == ArgumentOperationKind.Value && IsDirectExclusiveBorrow(source))))
        {
            // Binding's acquisition keeps the source dependency before parameter lifetime fitting. In particular,
            // Reborrowing a reference stored in a subscript's element keeps the referent's Origin, not the slot's Origin.
            // The common borrowing path still records the actual Loan footprint and call reservation.
            var type = argument.AdaptedType ?? this.compilation.Binding.PreparedBorrowType(source, argument.ParameterType!);
            var direct = KotoHelper.UnwrapParentheses(source);
            // An explicit adaptation and its call-only reborrow are one preparation.
            // Never peel a call, selection, capture or storage boundary.
            if (!this.compilation.Binding.ReadsReferent(source) && direct is ConversionKoto { ConversionBinding: ConversionBinding.Borrow } conversion &&
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

        if (argument.Kind == ArgumentOperationKind.ReferenceRead && argument.SourceType is { } actual && argument.ParameterType is { Components.Count: 1 } parameter &&
            this.compilation.Binding.SharedReferenceThroughLayers(actual, parameter.Components[0], out _) is { } shared)
        {
            return this.ReadReference(source, shared);
        }

        return this.Argument(source, argument.Kind);
    }

    // SPEC 15.6.3, 15.6.7 (PLAN G53): an exclusive reference moved into a literal is acquired exclusively there, as a call
    // argument is: a reserved Reborrow of the placed reference activates with the literal and conflicts with every live Loan on
    // its referent, such as its own Reborrow child placed in the same literal. A direct borrow written in the literal is a new
    // exclusive borrow, already checked where it is taken. The Reborrow is of the acquired value before it is placed (a
    // payload slot takes only its placement) and is never used, so it ends at the activation.
    private void ReservePlacedReference(Koto literal, Koto element, int placed, int source)
    {
        var type = this.body.Places[placed].Type;
        if (type is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq, Components.Count: 1 } || IsDirectExclusiveBorrow(element))
        {
            return;
        }

        var reservation = this.NewCallReservation(literal);
        var reborrow = this.Place(element, type, OwnershipPlaceKind.Temporary, false);
        var borrow = this.Emit(OwnershipOperationKind.Borrow, element, source, reborrow, loanMode: LoanRequirement.Uniq, reservation: reservation);
        this.SetValue(borrow, OwnershipValueKind.Address, [this.Value(source)], constant: source);
        this.CompleteCallReservation(reservation, this.Value(reborrow), reborrow);

        // The placed reference is held in the reservation's mode, so the Reborrow never conflicts with that reference's own Loan.
        this.body.CallReservations[reservation] = this.body.CallReservations[reservation] with { Loaded = placed };
    }

    // Intermediate references in one argument's acquisition path share its activation, but each has its own checked
    // reservation. Reusing the final reservation would leave an intermediate exclusive borrow active during evaluation.
    private int BorrowIntermediate(Koto source, BoundType type, int argument, int through = -1)
    {
        var reservation = argument >= 0 && type.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq
            ? this.NewCallReservation(this.body.CallReservations[argument].Call, argument) : -1;
        var result = through < 0 ? this.BorrowStruct(source, type, reservation) : this.BorrowThrough(source, through, type, reservation);
        if (result >= 0 && reservation >= 0)
        {
            this.CompleteCallReservation(reservation, this.Value(result), result);
        }

        return result;
    }

    private int NewCallReservation(Koto call, int argument = -1)
    {
        var id = this.body.CallReservations.Count;
        this.body.CallReservations.Add(new(call, Argument: argument));
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

    private void ActivateCallReservations(Koto call, int mark)
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
