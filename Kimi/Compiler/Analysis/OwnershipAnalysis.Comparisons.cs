// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private int comparisonDepth;

    private int CurrentLoanHead
    {
        get
        {
            var point = this.current;
            if (point < 0 && this.checkingRegion > 0)
            {
                var region = this.body.CheckingRegions[this.checkingRegion];
                var replay = region.Replay;
                point = region.Seed;
                if (region.SeedCount > 0)
                {
                    var seed = this.body.CheckingSeeds[region.SeedStart];
                    point = seed.Operation;
                    replay = seed.Replay;
                }

                if (replay >= 0)
                {
                    point = this.body.CheckingReplays[replay].End;
                }
            }

            return (uint)point < (uint)this.body.LoanStates.Count ? this.body.LoanStates[point] : -1;
        }
    }

    private int InspectString(Koto source, out int loan)
    {
        if (KotoHelper.UnwrapParentheses(source) is BinaryKoto element && ElementAccess.IsSyntax(element))
        {
            return this.BorrowStringElement(element, null, null, out loan);
        }

        var place = this.Expression(source, PlaceUseKind.Read);
        loan = -1;
        if (place < 0 || ReferenceTypes.IsString(this.body.Places[place].Type) || this.body.Places[place].Kind is not (OwnershipPlaceKind.Local or OwnershipPlaceKind.Parameter))
        {
            return place;
        }

        loan = this.BeginSharedLoan(place);
        return place;
    }

    private int BeginSharedLoan(int place, InvocationKoto? call = null, int guard = -1, bool access = false)
    {
        var parent = this.CurrentLoanHead;
        while (this.body.LoanStates.Count < this.body.Operations.Count)
        {
            this.body.LoanInputs.Add(-1);
            this.body.LoanStates.Add(-1);
        }

        var loan = this.body.ComparisonLoans.Count;
        this.body.ComparisonLoans.Add(new(this.current, place, parent, this.comparisonDepth, Call: call, Guard: guard, Access: access));
        this.body.LoanStates[this.current] = loan;
        return loan;
    }

    private int BorrowArgument(InvocationKoto call, BoundArgumentOperation argument)
    {
        var source = KotoHelper.UnwrapParentheses(argument.Source!);
        if (source is BinaryKoto element && ElementAccess.IsSyntax(element))
        {
            return this.BorrowStringElement(element, call, argument.ParameterType, out _);
        }

        if (!ReferenceEquals(source.BoundType, BoundType.String))
        {
            this.Expression(source, PlaceUseKind.Read);
            this.Unsupported(source);
            return -1;
        }

        // An owned expression already materializes its result and registers its
        // enclosing-expression cleanup. Borrow that storage without another Move.
        var place = source is IdentifierNameKoto && source.BoundSymbol?.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter
            ? this.Local(source) : this.Expression(source, PlaceUseKind.Read);
        if (place < 0)
        {
            return -1;
        }

        var result = this.Place(source, argument.ParameterType, OwnershipPlaceKind.Temporary, false, AcquisitionKind.Copy);
        this.Emit(OwnershipOperationKind.Borrow, source, place, result, loanMode: LoanRequirement.Ref);
        this.BeginSharedLoan(place, call);
        return this.RegisterTemporary(result);
    }

    private int StringComparison(BinaryKoto comparison)
    {
        var depth = this.comparisonDepth++;
        var left = this.InspectString(comparison.Left, out var leftLoan);
        var leftValue = ReferenceTypes.IsString(comparison.Left.BoundType) ? this.Value(left) : -1;
        var right = this.InspectString(comparison.Right, out var rightLoan);
        var rightValue = ReferenceTypes.IsString(comparison.Right.BoundType) ? this.Value(right) : -1;
        var result = -1;
        if (left >= 0 && right >= 0 && this.flow!.Nodes[comparison].CanCompleteNormally)
        {
            result = this.Temporary(comparison);
            var operation = this.Value(result);
            this.SetValue(operation, OwnershipValueKind.StringComparison, [], comparison.Akind);
            this.body.StringComparisons.Add(new(operation, left, right, leftLoan, rightLoan, leftValue, rightValue));
        }

        this.EndComparisonLoans(depth, comparison);
        this.comparisonDepth = depth;
        return result;
    }

    private int EndComparisonLoans(int depth, Koto source)
    {
        var before = this.CurrentLoanHead;
        var after = before;
        while (after >= 0 && this.body.ComparisonLoans[after].Depth > depth)
        {
            after = this.body.ComparisonLoans[after].Parent;
        }

        if (after != before)
        {
            var id = this.Emit(OwnershipOperationKind.EndComparisonLoans, source);
            this.body.LoanStates[id] = after;
            return id;
        }

        return -1;
    }

    private void RecordComparisonState(Koto source)
    {
        if (this.body.LoanStates.Count == 0)
        {
            return;
        }

        var head = this.CurrentLoanHead;
        this.body.LoanInputs.Add(head);
        this.body.LoanStates.Add(head);
        for (var loan = head; loan >= 0; loan = this.body.ComparisonLoans[loan].Parent)
        {
            if (this.body.ComparisonLoans[loan].Reservation >= 0 || this.body.Operations[^1].Reservation >= 0)
            {
                continue; // Reservation phases and static subpaths are checked on the completed plan.
            }

            if (this.body.ConflictsWithLoan(this.body.Operations.Count - 1, loan))
            {
                // The diagnostic is deliberately Place-independent, matching ReportIssue's key.
                this.body.ReportIssue(new(source, OwnershipFailure.ComparisonLoanConflict));
                break;
            }
        }
    }
}
