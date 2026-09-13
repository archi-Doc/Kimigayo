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
                point = this.body.CheckingRegions[this.checkingRegion].Seed;
            }

            return (uint)point < (uint)this.body.LoanStates.Count ? this.body.LoanStates[point] : -1;
        }
    }

    private int InspectString(Koto source, out int loan)
    {
        var place = this.Expression(source, PlaceUseKind.Read);
        loan = -1;
        if (place < 0 || this.body.Places[place].Kind is not (OwnershipPlaceKind.Local or OwnershipPlaceKind.Parameter))
        {
            return place;
        }

        var parent = this.CurrentLoanHead;
        while (this.body.LoanStates.Count < this.body.Operations.Count)
        {
            this.body.LoanInputs.Add(-1);
            this.body.LoanStates.Add(-1);
        }

        loan = this.body.ComparisonLoans.Count;
        this.body.ComparisonLoans.Add(new(this.current, place, parent, this.comparisonDepth));
        this.body.LoanStates[this.current] = loan;
        return place;
    }

    private int StringComparison(BinaryKoto comparison)
    {
        var depth = this.comparisonDepth++;
        var left = this.InspectString(comparison.Left, out var leftLoan);
        var right = this.InspectString(comparison.Right, out var rightLoan);
        var result = -1;
        if (left >= 0 && right >= 0 && this.flow!.Nodes[comparison].CanCompleteNormally)
        {
            result = this.Temporary(comparison);
            var operation = this.Value(result);
            this.SetValue(operation, OwnershipValueKind.StringComparison, [], comparison.Akind);
            this.body.StringComparisons.Add(new(operation, left, right, leftLoan, rightLoan));
        }

        this.EndComparisonLoans(depth, comparison);
        this.comparisonDepth = depth;
        return result;
    }

    private void EndComparisonLoans(int depth, Koto source)
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
        }
    }

    private void RecordComparisonState(OwnershipOperationKind kind, Koto source, int place, int input, AcquisitionKind acquisition)
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
            if (OwnershipBody.ConflictsWithComparison(kind, place, input, acquisition, this.body.ComparisonLoans[loan].Place))
            {
                // The diagnostic is deliberately Place-independent, matching ReportIssue's key.
                this.body.ReportIssue(new(source, OwnershipFailure.ComparisonLoanConflict));
                break;
            }
        }
    }
}
