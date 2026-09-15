// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

public sealed partial class OwnershipBody
{
    internal static bool ConflictsWithComparison(OwnershipOperationKind kind, int place, int input, AcquisitionKind acquisition, int borrowed, LoanRequirement mode = LoanRequirement.Ref, LoanRequirement access = LoanRequirement.None) => kind switch
    {
        OwnershipOperationKind.Consume or OwnershipOperationKind.AcquirePattern => place == borrowed && (mode == LoanRequirement.Uniq || acquisition is AcquisitionKind.Move or AcquisitionKind.CopyOrMove),
        OwnershipOperationKind.Write or OwnershipOperationKind.WriteElement or OwnershipOperationKind.InitializeSubject => place == borrowed || input == borrowed,
        OwnershipOperationKind.Cleanup or OwnershipOperationKind.CallEntry or OwnershipOperationKind.Deliver or OwnershipOperationKind.Declare or OwnershipOperationKind.Produce => place == borrowed,
        OwnershipOperationKind.Borrow => place == borrowed && (mode != LoanRequirement.Ref || access != LoanRequirement.Ref),
        OwnershipOperationKind.Read => place == borrowed && mode == LoanRequirement.Uniq,
        _ => false,
    };

    internal bool ConflictsWithLoan(int id, int loanId)
    {
        var operation = this.Operations[id];
        var loan = this.ComparisonLoans[loanId];
        if (operation.Projection >= 0 && operation.Kind is OwnershipOperationKind.Produce or OwnershipOperationKind.WriteElement)
        {
            var access = this.Projections[operation.Projection];
            if (access.Root != loan.Place)
            {
                return false;
            }

            if (loan.Mode == LoanRequirement.Uniq)
            {
                return access.Exclusive != loanId && this.ElementPathsOverlap(operation.Projection, loan.Projection);
            }

            // Receiver stability forbids any write to its root during index evaluation.
            return operation.Kind == OwnershipOperationKind.WriteElement;
        }

        return ConflictsWithComparison(operation.Kind, operation.Place, operation.Input, operation.Acquisition, loan.Place, loan.Mode, operation.LoanMode);
    }

    internal bool ElementUpdateLoanConflicts(int loanId)
    {
        var loan = this.ComparisonLoans[loanId];
        for (var head = loan.Parent; head >= 0; head = this.ComparisonLoans[head].Parent)
        {
            var existing = this.ComparisonLoans[head];
            if (existing.Place == loan.Place &&
                (existing.Mode != LoanRequirement.Uniq || this.ElementPathsOverlap(loan.Projection, existing.Projection)))
            {
                return true;
            }
        }

        return false;
    }

    internal bool HasComparisonLoan(int operation, int loan)
    {
        if ((uint)operation >= (uint)this.LoanInputs.Count || (uint)loan >= (uint)this.ComparisonLoans.Count)
        {
            return false;
        }

        for (var head = this.LoanInputs[operation]; head >= 0; head = this.ComparisonLoans[head].Parent)
        {
            if (head == loan)
            {
                return true;
            }
        }

        return false;
    }

    internal bool ValidateComparisonLoans()
    {
        if (!this.ValidateElementPaths())
        {
            return false;
        }

        if (this.ComparisonLoans.Count == 0)
        {
            return this.LoanInputs.Count == 0 && this.LoanStates.Count == 0;
        }

        if (this.LoanInputs.Count != this.Operations.Count || this.LoanStates.Count != this.Operations.Count)
        {
            return false;
        }

        for (var i = 0; i < this.ComparisonLoans.Count; i++)
        {
            var loan = this.ComparisonLoans[i];
            if (loan.Mode == LoanRequirement.Uniq)
            {
                if ((uint)loan.Read >= (uint)this.Operations.Count || !this.ValidElementUpdateLoan(i))
                {
                    return false;
                }

                continue;
            }

            if ((uint)loan.Read >= (uint)this.Operations.Count || (uint)loan.Place >= (uint)this.Places.Count || loan.Parent < -1 || loan.Parent >= i || loan.Depth <= 0 || loan.Guard < -1 ||
                this.Operations[loan.Read].Kind != (loan.Access ? OwnershipOperationKind.LocateReceiver : loan.Call is null ? OwnershipOperationKind.Read : OwnershipOperationKind.Borrow) ||
                loan.Mode != LoanRequirement.Ref || loan.Projection != -1 || this.Operations[loan.Read].Place != loan.Place ||
                (loan.Guard < 0 && this.Places[loan.Place].Kind is not (OwnershipPlaceKind.Local or OwnershipPlaceKind.Parameter) &&
                    ((!loan.Access && loan.Call is null) || this.Places[loan.Place].Kind is not (OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result))) ||
                (loan.Access ? loan.Call is not null || loan.Guard != -1 || this.Places[loan.Place].Type.Kind is not (BoundTypeKind.Tuple or BoundTypeKind.FixedArray)
                    : !ReferenceEquals(this.Places[loan.Place].Type, BoundType.String)) ||
                this.LoanStates[loan.Read] != i || this.LoanInputs[loan.Read] != loan.Parent)
            {
                return false;
            }

            if (loan.Guard >= 0 && ((uint)loan.Guard >= (uint)this.MatchArms.Count || loan.Call is not null ||
                (uint)this.MatchArms[loan.Guard].Match >= (uint)this.Matches.Count ||
                (uint)this.MatchArms[loan.Guard].GuardEntry >= (uint)this.Operations.Count ||
                this.Matches[this.MatchArms[loan.Guard].Match].Subject != loan.Place || this.Places[loan.Place].Kind != OwnershipPlaceKind.Subject ||
                !this.ValidGuardLoan(i)))
            {
                return false;
            }

            if (loan.Parent >= 0 && this.ComparisonLoans[loan.Parent].Depth > loan.Depth)
            {
                return false;
            }
        }

        for (var id = 0; id < this.Operations.Count; id++)
        {
            var input = this.LoanInputs[id];
            var output = this.LoanStates[id];
            if (input < -1 || input >= this.ComparisonLoans.Count || output < -1 || output >= this.ComparisonLoans.Count)
            {
                return false;
            }

            var operation = this.Operations[id];
            if (input != output)
            {
                if (operation.Kind == OwnershipOperationKind.EndComparisonLoans)
                {
                    var head = input;
                    while (head >= 0 && head != output)
                    {
                        head = this.ComparisonLoans[head].Parent;
                    }

                    if (head != output)
                    {
                        return false;
                    }
                }
                else if (output < 0 || this.ComparisonLoans[output].Read != id ||
                    (this.ComparisonLoans[output].Mode == LoanRequirement.Uniq ? !this.ValidElementUpdateLoan(output) : this.ComparisonLoans[output].Parent != input))
                {
                    return false;
                }
            }

            for (var head = input; head >= 0; head = this.ComparisonLoans[head].Parent)
            {
                if (this.ConflictsWithLoan(id, head))
                {
                    return false;
                }
            }
        }

        foreach (var edge in this.EdgeStorage)
        {
            // A checking-only tail can point at a runtime join without arriving there.
            if (edge.Kind != OwnershipEdgeKind.Abort && (this.IsReachable(edge.From) || (!this.IsReachable(edge.To) && this.OperationRegions[edge.From] == this.OperationRegions[edge.To])) &&
                this.LoanStates[edge.From] != this.LoanInputs[edge.To])
            {
                return false;
            }
        }

        foreach (var region in this.CheckingRegions)
        {
            if (region.Seed >= 0 && region.Entry >= 0 && this.LoanStates[region.Seed] != this.LoanInputs[region.Entry])
            {
                return false;
            }
        }

        return true;
    }

    private bool ValidElementUpdateLoan(int id)
    {
        var loan = this.ComparisonLoans[id];
        if (loan.Mode != LoanRequirement.Uniq || !loan.Access || loan.Call is not null || loan.Guard != -1 ||
            (uint)loan.Projection >= (uint)this.Projections.Count)
        {
            return false;
        }

        var plan = this.Projections[loan.Projection];
        if ((uint)plan.Loan >= (uint)id)
        {
            return false;
        }

        var access = this.ComparisonLoans[plan.Loan];
        if (plan.Exclusive != id || plan.Operation != loan.Read || plan.Root != loan.Place ||
            access.Mode != LoanRequirement.Ref || !access.Access || access.Place != loan.Place ||
            access.Parent != loan.Parent || access.Depth != loan.Depth || this.LoanInputs[loan.Read] != plan.Loan ||
            this.LoanStates[loan.Read] != id || this.Operations[loan.Read].Kind != OwnershipOperationKind.ProjectElement)
        {
            return false;
        }

        return !this.ElementUpdateLoanConflicts(id);
    }

    private bool ValidGuardLoan(int index)
    {
        var loan = this.ComparisonLoans[index];
        var arm = this.MatchArms[loan.Guard];
        if (arm.GuardLoan == index)
        {
            return arm.GuardEntry + 1 == loan.Read && ReferenceEquals(this.Operations[loan.Read].Source, this.Operations[arm.GuardEntry].Source);
        }

        // Only a new candidate read in a transfer-seeded checking region may
        // establish replacement protection after the original Loan has ended.
        return !this.IsReachable(loan.Read) && this.OperationRegions[loan.Read] > 0 &&
            (uint)arm.GuardLoan < (uint)index && this.ComparisonLoans[arm.GuardLoan].Depth == loan.Depth &&
            this.OperationSteps[loan.Read] == loan.Guard && this.Operations[loan.Read].Input >= 0 &&
            this.Values[loan.Read].Kind == OwnershipValueKind.Borrow;
    }
}
