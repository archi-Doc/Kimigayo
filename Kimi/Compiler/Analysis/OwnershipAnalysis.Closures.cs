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
            if (closure.EnvironmentType is not null)
            {
                // SPEC 7.6.2: a bare capture Copies and needs definition-side Copy proof; x@move transfers.
                var transfer = capture.Environment.TransferCapture;
                if (!transfer && this.body.Places[place].Acquisition != AcquisitionKind.Copy)
                {
                    this.body.ReportIssue(new(source, OwnershipFailure.TransferRequired));
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

    private int CallValue(InvocationKoto call, BoundValueCall plan)
    {
        var depth = this.comparisonDepth++;
        var reservationMark = this.body.CallReservations.Count;
        var explicitReceiver = plan.ReceiverKind == SemanticsKind.Uniq && IsDirectExclusiveBorrow(plan.Receiver);
        var receiver = explicitReceiver
            ? this.PrepareCallArgument(call, plan.Receiver, new(plan.Receiver, plan.ReceiverType, plan.ReceiverType, ArgumentOperationKind.Reborrow, ArgumentAdaptation.SameSemanticsReborrow))
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
        if (ReferenceTypes.IsBorrow(plan.ReceiverType) && this.body.Places[receiver].Kind is OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result)
        {
            this.SetValue(read, OwnershipValueKind.Alias, [receiverValue]);
        }

        if (plan.ReceiverKind != SemanticsKind.Owner)
        {
            var loan = this.BeginSharedLoan(receiver);
            if (plan.ReceiverType.Kind != BoundTypeKind.Function)
            {
                this.body.ComparisonLoans[loan] = this.body.ComparisonLoans[loan] with { Callable = call, Mode = plan.ReceiverKind == SemanticsKind.Uniq ? LoanRequirement.Uniq : LoanRequirement.Ref };
            }

            if (reservation >= 0)
            {
                this.CompleteCallReservation(reservation, read, loan: loan);
            }
        }
        else
        {
            this.Emit(OwnershipOperationKind.CallEntry, plan.Receiver, receiver);
        }

        var mark = this.arguments.Count;
        for (var i = 0; i < plan.Arguments.Length; i++)
        {
            this.arguments.Add(this.PrepareCallArgument(call, call.ArgumentNodes[i], plan.Arguments[i]));
        }

        this.ActivateCallReservations(call, reservationMark);
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
        if (ScalarResult(plan.ReturnType))
        {
            this.SetValue(invoke, OwnershipValueKind.Call, []);
            this.SetValue(this.Value(result), OwnershipValueKind.Alias, [invoke]);
        }

        this.EndComparisonLoans(depth, call);
        this.comparisonDepth = depth;
        return result;
    }
}
