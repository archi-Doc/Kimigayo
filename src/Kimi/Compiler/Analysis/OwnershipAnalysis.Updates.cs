// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private int WholeValueUpdate(InvocationKoto call, BoundCall plan)
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
        // SPEC 15.7: no Owned/Copy constraint. Content transfers and their operation order are checked with ordinary stores.
        if (!ReferenceTypes.IsStorage(this.body.Places[first].Type))
        {
            this.Internal(call);
            this.EndComparisonLoans(depth, call);
            this.comparisonDepth = depth;
            return -1;
        }

        var result = this.Place(call, plan.Target.CompilerFunction == CompilerFunctionKind.Replace ? BoundType.Unit : type, OwnershipPlaceKind.Temporary, false);
        var kind = plan.Target.CompilerFunction switch
        {
            CompilerFunctionKind.Replace => OwnershipOperationKind.ReplaceBorrowed,
            CompilerFunctionKind.Exchange => OwnershipOperationKind.ExchangeBorrowed,
            CompilerFunctionKind.Swap => OwnershipOperationKind.SwapBorrowed,
            _ => throw new InvalidOperationException("Expected a whole-value update."),
        };
        var update = this.Emit(kind, call, result, second);
        this.SetValue(update, OwnershipValueKind.BorrowedUpdate, [this.Value(first), this.Value(second)], constant: first);
        this.placeValues[result] = update;
        this.RegisterTemporary(result);
        this.EndComparisonLoans(depth, call);
        this.comparisonDepth = depth;
        return plan.Target.CompilerFunction == CompilerFunctionKind.Exchange ? result : this.Temporary(call);
    }
}
