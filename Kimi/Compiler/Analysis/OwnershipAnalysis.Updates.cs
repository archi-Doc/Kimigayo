// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private int WholeValueUpdate(InvocationKoto call, BoundCall plan)
    {
        for (var i = 0; i < call.ArgumentNodes.Count; i++)
        {
            if ((plan.Target.CompilerFunction == CompilerFunctionKind.Swap || plan.ArgumentOperations[i].ParameterIndex == 0) &&
                ReferenceTypes.IsStorage(call.ArgumentNodes[i].BoundType))
            {
                return this.BorrowedWholeValueUpdate(call, plan);
            }
        }

        var depth = this.comparisonDepth++;
        var reservationMark = this.body.CallReservations.Count;
        var first = -1;
        var second = -1;
        Koto firstSource = call;
        Koto secondSource = call;
        var swap = plan.Target.CompilerFunction == CompilerFunctionKind.Swap;
        for (var i = 0; i < call.ArgumentNodes.Count; i++)
        {
            var argument = plan.ArgumentOperations[i];
            var target = swap || argument.ParameterIndex == 0;
            var source = KotoHelper.UnwrapParentheses(call.ArgumentNodes[i]);
            int place;
            if (target)
            {
                if (source is ConversionKoto { ConversionBinding: ConversionBinding.Borrow } conversion &&
                    ReferenceEquals(conversion.Left.BoundType, argument.ParameterType?.Components[0]))
                {
                    source = KotoHelper.UnwrapParentheses(conversion.Left);
                }

                // A direct complete owner Place uses the ordinary write/transfer plans.
                // Other storage paths need their own verified physical address plan.
                if (source is not IdentifierNameKoto || source.BoundType?.Semantics != SemanticsKind.Owner ||
                    (place = this.Local(source)) < 0 || !this.body.Places[place].Mutable ||
                    !ReferenceEquals(this.body.Places[place].Type, argument.ParameterType?.Components[0]))
                {
                    this.Unsupported(call);
                    place = -1;
                }
                else
                {
                    var reservation = this.NewCallReservation(call);
                    var borrow = this.Emit(OwnershipOperationKind.UpdateTarget, call.ArgumentNodes[i], place, loanMode: LoanRequirement.Uniq, reservation: reservation);
                    var loan = this.BeginSharedLoan(place, call);
                    this.CompleteCallReservation(reservation, borrow, loan: loan);
                }
            }
            else
            {
                place = this.Expression(call.ArgumentNodes[i]);
            }

            if (argument.ParameterIndex == 0)
            {
                first = place;
                firstSource = source;
            }
            else
            {
                second = place;
                secondSource = source;
            }
        }

        this.ActivateCallReservations(call, reservationMark);
        var result = -1;
        if (first >= 0 && second >= 0)
        {
            var type = this.body.Places[first].Type;
            if (!ReferenceEquals(type, this.body.Places[second].Type))
            {
                this.Unsupported(call);
            }
            else
            {
                if (plan.Target.CompilerFunction != CompilerFunctionKind.Replace)
                {
                    result = this.Place(firstSource, type, OwnershipPlaceKind.Temporary, false);
                    this.Emit(OwnershipOperationKind.Consume, call, first, result, AcquisitionKind.Move);
                    this.RegisterTemporary(result);
                }

                if (swap)
                {
                    var incoming = this.Place(secondSource, type, OwnershipPlaceKind.Temporary, false);
                    this.Emit(OwnershipOperationKind.Consume, call, second, incoming, AcquisitionKind.Move);
                    this.RegisterTemporary(incoming);
                    this.Emit(OwnershipOperationKind.Write, call, first, incoming);
                    this.Emit(OwnershipOperationKind.Write, call, second, result);
                }
                else
                {
                    this.Emit(OwnershipOperationKind.Write, call, first, second);
                }

                if (plan.Target.CompilerFunction != CompilerFunctionKind.Exchange)
                {
                    result = this.Temporary(call);
                }
            }
        }

        this.EndComparisonLoans(depth, call);
        this.comparisonDepth = depth;
        return result;
    }

    private int BorrowedWholeValueUpdate(InvocationKoto call, BoundCall plan)
    {
        var depth = this.comparisonDepth++;
        var reservationMark = this.body.CallReservations.Count;
        var first = -1;
        var second = -1;
        var swap = plan.Target.CompilerFunction == CompilerFunctionKind.Swap;
        for (var i = 0; i < call.ArgumentNodes.Count; i++)
        {
            var argument = plan.ArgumentOperations[i];
            var place = swap || argument.ParameterIndex == 0
                ? this.PrepareCallArgument(call, call.ArgumentNodes[i], argument) : this.Expression(call.ArgumentNodes[i]);
            if (argument.ParameterIndex == 0)
            {
                first = place;
            }
            else
            {
                second = place;
            }
        }

        this.ActivateCallReservations(call, reservationMark);
        if (first < 0 || second < 0)
        {
            this.EndComparisonLoans(depth, call);
            this.comparisonDepth = depth;
            return -1;
        }

        var type = this.body.Places[first].Type.Components[0];
        // Complete Types with internal dependencies need a content-sensitive effect plan.
        if (!ReferenceTypes.IsStorage(this.body.Places[first].Type) ||
            this.compilation.Binding.ProveOwned(type, call) != ConstraintProof.Proven)
        {
            this.Unsupported(call);
            this.EndComparisonLoans(depth, call);
            this.comparisonDepth = depth;
            return -1;
        }

        var result = this.Place(call, plan.Target.CompilerFunction == CompilerFunctionKind.Replace ? BoundType.Unit : type, OwnershipPlaceKind.Temporary, false);
        var update = this.Emit(OwnershipOperationKind.UpdateBorrowed, call, result, second);
        this.SetValue(update, OwnershipValueKind.BorrowedUpdate, [this.Value(first), this.Value(second)], constant: first);
        this.placeValues[result] = update;
        this.RegisterTemporary(result);
        this.EndComparisonLoans(depth, call);
        this.comparisonDepth = depth;
        return plan.Target.CompilerFunction == CompilerFunctionKind.Exchange ? result : this.Temporary(call);
    }
}
