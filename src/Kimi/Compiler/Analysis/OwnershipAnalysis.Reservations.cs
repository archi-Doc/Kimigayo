// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private const string RetainedLoanLabel = "value retaining the conflicting loan";
    private const string BorrowLabel = "borrow that created the loan";
    private const string ReservationLabel = "conflicting exclusive call reservation";

    private static bool IsDirectExclusiveBorrow(Koto source)
        => KotoHelper.UnwrapParentheses(source) is ConversionKoto { ConversionBinding: ConversionBinding.Borrow, BoundType.Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq };

    // SPEC 15.6.7: the value retaining a conflicting Loan and the lending point of a conflicting reservation are shown. An
    // unsupported Place borrow relates the path segment that chose its missing route (SPEC 23.3.6.4, PLAN G59).
    private static (string Role, Koto At, string? Label)[]? RelatedLocations(OwnershipIssue issue) => (issue.LoanSource, issue.ConflictingReservation) switch
    {
        ({ } loan, { } reserved) => [("loan", loan, RetainedLoanLabel), ("reservation", reserved.Input, ReservationLabel)],
        ({ } loan, null) => [("loan", loan, RetainedLoanLabel)],
        (null, { } reserved) => [("reservation", reserved.Input, ReservationLabel)],
        _ => issue is { Feature: not OwnershipFeature.None, Related: { } selector } ? [("selector", selector, FeatureLabel(issue.Feature))] : null,
    };

    // PLAN G59: the Note and selector label of an unsupported Place borrow, by the route it lacks.
    private static string? FeatureNote(OwnershipFeature feature) => feature switch
    {
        OwnershipFeature.RuntimeElementBorrow => "Borrowing a Place below a runtime-selected element is not implemented yet; the borrow would take a temporary copy instead of the element (PLAN G59)",
        OwnershipFeature.ReferencedElementBorrow => "Borrowing a Place below a collection reached through a reference is not implemented yet; the borrow would take a temporary copy instead of the element (PLAN G59)",
        OwnershipFeature.TemporaryPartBorrow => "Borrowing a part of a temporary value is not implemented yet; the borrow would take a copy of the part (PLAN G59)",
        _ => null,
    };

    private static string FeatureLabel(OwnershipFeature feature) => feature switch
    {
        OwnershipFeature.RuntimeElementBorrow => "runtime-selected element",
        OwnershipFeature.ReferencedElementBorrow => "collection reached through a reference",
        _ => "temporary value",
    };

    // SPEC 15.6.7: a conflict at an implicit lending point names the acquisition; when two implicit acquisitions overlap, both,
    // once when they read alike. Formatted only for a published record.
    private static string? AcquisitionNote(OwnershipIssue issue)
    {
        var input = ImplicitAcquisition(issue.Input);
        var reserved = ImplicitAcquisition(issue.ConflictingReservation);
        return input is null || input == reserved ? reserved : reserved is null ? input : input + "; " + reserved;
    }

    private static string? ImplicitAcquisition(OwnershipLending? lending)
    {
        if (lending is not { Input: var input, Call: var consumer })
        {
            return null;
        }

        if (consumer is not InvocationKoto call)
        {
            return $"`{input}` is acquired exclusively where the literal places it (PLAN G53, SPEC 15.6.3)";
        }

        // A borrow value is Reborrowed; an owned Place, including an object payload reached by a follow, is borrowed.
        var acquired = input.BoundType?.Semantics is SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.ObjRef or SemanticsKind.ObjUniq ? "reborrowed" : "borrowed";
        if (call.BoundValueCall is { } value)
        {
            return Contains(value.Receiver, input) && !IsDirectExclusiveBorrow(value.Receiver) ? $"`{input}` implicitly {acquired} exclusively as the receiver of its call" : null;
        }

        if (call.BoundCall is not { } bound)
        {
            return null;
        }

        // A linked Kimi constructor is named as the public declaration the call selected (SPEC 22.1).
        var target = bound.Target.LibraryDeclaration is { } id && KimiLibraryCatalog.PresentationOf(id) is not null && call.BoundSymbol is { } selected ? selected : bound.Target;
        if (bound.Receiver is { } receiver && Contains(receiver, input))
        {
            return IsDirectExclusiveBorrow(receiver) ? null : $"`{input}` implicitly {acquired} exclusively as the receiver of `{target.Name}`";
        }

        for (var i = 0; i < call.ArgumentNodes.Count && i < bound.ArgumentOperations.Length; i++)
        {
            if (Contains(call.ArgumentNodes[i], input))
            {
                if (IsDirectExclusiveBorrow(call.ArgumentNodes[i]))
                {
                    return null;
                }

                var index = bound.ArgumentOperations[i].ParameterIndex;
                return target.Declaration is FunctionKoto function && (uint)index < (uint)function.Parameters.Count && function.Parameters[index] is { } parameter
                    ? $"`{input}` implicitly {acquired} exclusively for parameter `{(parameter.ExternalName.Length > 0 ? parameter.ExternalName : parameter.InternalName)}` of `{target.Name}`"
                    : $"`{input}` implicitly {acquired} exclusively as an argument of `{target.Name}`";
            }
        }

        return null;

        static bool Contains(Koto node, Koto input)
            => node.Span.Start <= input.Span.Start && input.Span.Start + input.Span.Length <= node.Span.Start + node.Span.Length &&
                ReferenceEquals(node.CodeContext.SourceDocument, input.CodeContext.SourceDocument);
    }

    private bool HasCallInspection(int place)
    {
        var value = this.Value(place);
        // Address values use Origin liveness; reads of guard candidates retain their
        // enclosing guard Loan. Only a new inspection has its own call-wide extent.
        return value >= 0 && this.body.Values[value].Kind == OwnershipValueKind.Borrow &&
            this.body.Operations[value].Kind == OwnershipOperationKind.Borrow;
    }

    // SPEC 9.5.1: a shared receiver selected through a base path is the whole receiver, borrowed at its own Type (`whole`), whose base
    // prefix is then lent at the parameter Type (`type`); an exclusive or unproven projection is a located limit (OCC-X).
    private bool BaseBorrowTypes(Koto source, in BoundArgumentOperation argument, out BoundType type, out BoundType whole)
    {
        type = whole = null!;
        if (argument.ObjectCompatibility != ConstraintProof.Proven || argument.ParameterType?.Semantics is not (SemanticsKind.Ref or SemanticsKind.ObjRef) ||
            argument.SourceType is not { } receiverType || argument.BasePath is null)
        {
            this.Unsupported(source);
            return false;
        }

        type = argument.AdaptedType ?? this.compilation.Binding.PreparedBorrowType(source, argument.ParameterType);
        var core = ObjectTypes.IsBorrow(type) ? ObjectTypes.ViewTarget(receiverType)! : ReferenceTypes.IsReference(receiverType) || ObjectTypes.HandleMode(receiverType) is not null || ObjectTypes.IsBorrow(receiverType) ? receiverType.Components[0] : receiverType;
        whole = this.compilation.Binding.Reference(type.Semantics, core, type.Origin);
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

        // SPEC 4.6.9: a string element reached through a sequence or a reference is borrowed through the common element borrow.
        var stringElement = ElementAccess.IsBorrowedSelection(BorrowedArgumentSource(source)) || ElementAccess.IsPlaceCall(BorrowedArgumentSource(source)) || ElementAccess.IsUserIndex(BorrowedArgumentSource(source));
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
            // Reborrowing a reference stored in a published Place keeps the referent's Origin, not the slot's Origin.
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
