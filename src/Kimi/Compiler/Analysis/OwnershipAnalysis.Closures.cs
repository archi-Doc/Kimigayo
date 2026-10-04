// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private int CreateClosure(FunctionKoto source)
    {
        var closure = source.BoundClosure!;
        var mark = this.arguments.Count;
        for (var i = 0; i < closure.Captures.Count; i++)
        {
            var capture = closure.Captures[i];
            if (!this.body.SymbolPlaces.TryGetValue(capture.Source, out var place))
            {
                this.Unsupported(source);
                return -1;
            }

            var read = -1;
            if (closure.EnvironmentType is not null && capture.Environment.CaptureAcquisition is CaptureAcquisition.Reborrow or CaptureAcquisition.SharedSlotBorrow or CaptureAcquisition.ExclusiveSlotBorrow)
            {
                // SPEC 7.6.2: the entry initializes its environment binding as `let x = x` (a Reborrow) or `let x = x@ref`
                // and `x@uniq` (a borrow of the outer slot) would; the closure keeps the borrow's Loan (SPEC 15.8.2).
                var borrowed = this.BorrowCapture(source, place, capture.Environment.Type!);
                var acquired = this.Place(source, capture.Environment.Type, OwnershipPlaceKind.Temporary, false);
                var borrowedValue = this.Value(borrowed);
                read = this.Emit(OwnershipOperationKind.Consume, source, borrowed, acquired, AcquisitionKind.Move);
                this.SetValue(read, OwnershipValueKind.Alias, [borrowedValue]); // The consumed temporary's one prepared value.
                this.Emit(OwnershipOperationKind.CallEntry, source, acquired);
            }
            else if (closure.EnvironmentType is not null)
            {
                // SPEC 7.6.2: a bare capture Copies and needs definition-side Copy proof; x@move transfers.
                var transfer = capture.Environment.TransferCapture;
                if (!transfer && this.body.Places[place].Acquisition != AcquisitionKind.Copy)
                {
                    this.body.ReportIssue(new(source, OwnershipFailure.TransferRequired, Capture: i));
                }

                var acquired = this.Place(source, capture.Environment.Type, OwnershipPlaceKind.Temporary, false);
                read = this.Emit(OwnershipOperationKind.Consume, source, place, acquired, transfer ? AcquisitionKind.Move : this.body.Places[place].Acquisition);
                this.Emit(OwnershipOperationKind.CallEntry, source, acquired);
            }
            else
            {
                read = this.Emit(OwnershipOperationKind.Read, source, place);
            }

            this.arguments.Add(read);
        }

        var result = this.Temporary(source);
        this.SetValue(this.Value(result), OwnershipValueKind.Closure, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(this.arguments).Slice(mark));
        this.arguments.RemoveRange(mark, this.arguments.Count - mark);
        return result;
    }

    // SPEC 7.6.2: the borrow that initializes a capture's environment binding. A stored exclusive reference is Reborrowed
    // through its referent when the environment keeps its Type; a slot borrow adds a reference layer over the binding.
    private int BorrowCapture(FunctionKoto source, int place, BoundType type)
    {
        var stored = this.body.Places[place].Type;
        var borrowValue = ReferenceTypes.IsBorrow(stored) || ObjectTypes.IsBorrow(stored);
        if (borrowValue)
        {
            this.Emit(OwnershipOperationKind.Read, source, place);
        }

        var result = this.Place(source, type, OwnershipPlaceKind.Temporary, false);
        var operation = this.Emit(OwnershipOperationKind.Borrow, source, place, result, loanMode: type.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq ? LoanRequirement.Uniq : LoanRequirement.Ref);
        this.SetValue(operation, OwnershipValueKind.Address, borrowValue ? [this.Value(place)] : [], constant: place);
        return this.RegisterTemporary(result);
    }

    private int CallValue(InvocationKoto call, BoundValueCall plan)
    {
        var depth = this.comparisonDepth++;
        var reservationMark = this.body.CallReservations.Count;
        var explicitReceiver = plan.ReceiverKind == SemanticsKind.Uniq && IsDirectExclusiveBorrow(plan.Receiver);

        // A common Function value stored in a field or element is called through a shared borrow of that part, as `@ref`
        // would take it, since reading the Non-Copy value out would transfer it.
        var direct = KotoHelper.UnwrapParentheses(plan.Receiver);
        var part = plan.ReceiverKind == SemanticsKind.Ref && plan.ReceiverType.Kind == BoundTypeKind.Function &&
            (direct is MemberAccessKoto || (direct is BinaryKoto element && ElementAccess.IsSyntax(element)));
        var receiver = explicitReceiver
            ? this.PrepareCallArgument(call, plan.Receiver, new(plan.Receiver, plan.ReceiverType, plan.ReceiverType, ArgumentOperationKind.Reborrow, ArgumentAdaptation.SameSemanticsReborrow))
            : part ? this.PrepareCallArgument(call, plan.Receiver, new(plan.Receiver, plan.ReceiverType, this.compilation.Binding.Reference(SemanticsKind.Ref, plan.ReceiverType), ArgumentOperationKind.Borrow, ArgumentAdaptation.CrossSemanticsBorrow), immediate: true)
            : this.Expression(plan.Receiver, plan.ReceiverKind == SemanticsKind.Owner ? PlaceUseKind.Consume : PlaceUseKind.Read);
        if (receiver < 0)
        {
            this.comparisonDepth = depth;
            return -1;
        }

        // Even a temporary has a distinct read marking the receiver Loan's start.
        var reservation = plan.ReceiverKind == SemanticsKind.Uniq && !explicitReceiver ? this.NewCallReservation(call) : -1;
        var receiverValue = this.Value(receiver);
        var read = this.Emit(OwnershipOperationKind.Read, plan.Receiver, receiver, reservation: reservation);
        if (ReferenceTypes.IsBorrow(this.body.Places[receiver].Type) && this.body.Places[receiver].Kind is OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result)
        {
            this.SetValue(read, OwnershipValueKind.Alias, [receiverValue]);
        }

        if (plan.ReceiverKind != SemanticsKind.Owner)
        {
            var loan = this.BeginSharedLoan(receiver);
            if (this.body.Places[receiver].Type.Kind != BoundTypeKind.Function)
            {
                this.body.ComparisonLoans[loan] = this.body.ComparisonLoans[loan] with { Callable = call, Mode = plan.ReceiverKind == SemanticsKind.Uniq ? LoanRequirement.Uniq : LoanRequirement.Ref };
            }

            if (reservation >= 0)
            {
                this.CompleteCallReservation(reservation, read, loan: loan);
            }
        }

        var mark = this.arguments.Count;
        for (var i = 0; i < plan.Arguments.Length; i++)
        {
            this.arguments.Add(this.PrepareCallArgument(call, call.ArgumentNodes[i], plan.Arguments[i]));
        }

        this.ActivateCallReservations(call, reservationMark);
        if (!explicitReceiver && reservation < 0 && ReferenceTypes.IsBorrow(this.body.Places[receiver].Type))
        {
            // A reference receiver is used again at the call, so the Place it borrows stays lent while the arguments run; an
            // exclusive receiver is instead held by its call reservation.
            var use = this.Emit(OwnershipOperationKind.Read, plan.Receiver, receiver);
            if (this.body.Places[receiver].Kind is OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result)
            {
                this.SetValue(use, OwnershipValueKind.Alias, [receiverValue]);
            }
        }

        if (plan.ReceiverKind == SemanticsKind.Owner)
        {
            // Preparation owns the receiver until every argument completes. An early return in an argument
            // must still destroy that prepared value; ownership enters the call only at this common boundary.
            // The argument entries follow, contiguous before the call, as an ordinary call's are.
            this.Emit(OwnershipOperationKind.CallEntry, plan.Receiver, receiver);
        }

        var acquired = true;
        for (var i = mark; i < this.arguments.Count; i++)
        {
            if (this.arguments[i] < 0)
            {
                acquired = false;
            }
            else
            {
                this.Emit(OwnershipOperationKind.CallEntry, call, this.arguments[i]);
            }
        }

        this.arguments.RemoveRange(mark, this.arguments.Count - mark);

        var invoke = this.Emit(OwnershipOperationKind.Call, call, input: receiver);
        this.Connect(invoke, this.abortExit, OwnershipEdgeKind.Abort);
        if (!acquired || ReferenceEquals(plan.ReturnType, BoundType.Never))
        {
            this.current = -1;
            this.BeginChecking(invoke);
            this.EndComparisonLoans(depth, call);
            this.comparisonDepth = depth;
            return -1;
        }

        var result = this.Temporary(call);
        this.body.OperationStorage[invoke] = this.body.Operations[invoke] with { Place = result };
        // An instance's `(T) -> T` returns its substituted T.
        if (this.Concrete(plan.ReturnType) is { } returned && ScalarResult(returned))
        {
            this.SetValue(invoke, OwnershipValueKind.Call, []);
            this.SetValue(this.Value(result), OwnershipValueKind.Alias, [invoke]);
        }

        this.EndComparisonLoans(depth, call);
        this.comparisonDepth = depth;
        return result;
    }
}
