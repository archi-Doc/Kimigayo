// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipBody
{
    internal static bool ConflictsWithComparison(OwnershipOperationKind kind, int place, int input, AcquisitionKind acquisition, int borrowed, LoanRequirement mode = LoanRequirement.Ref, LoanRequirement access = LoanRequirement.None) => kind switch
    {
        OwnershipOperationKind.Consume or OwnershipOperationKind.AcquirePattern => place == borrowed && (mode == LoanRequirement.Uniq || acquisition is AcquisitionKind.Move or AcquisitionKind.CopyOrMove),
        OwnershipOperationKind.Write or OwnershipOperationKind.WriteElement or OwnershipOperationKind.InitializeSubject => place == borrowed || input == borrowed,
        OwnershipOperationKind.Cleanup or OwnershipOperationKind.CallEntry or OwnershipOperationKind.Deliver or OwnershipOperationKind.Declare or OwnershipOperationKind.Produce or OwnershipOperationKind.StorePointer => place == borrowed,
        OwnershipOperationKind.Borrow or OwnershipOperationKind.UpdateTarget => place == borrowed && (mode != LoanRequirement.Ref || access != LoanRequirement.Ref),
        OwnershipOperationKind.Read => place == borrowed && mode == LoanRequirement.Uniq,
        _ => false,
    };

    internal bool ConflictsWithLoan(int id, int loanId, int activation = -1)
    {
        var operation = this.Operations[id];
        var loan = this.ComparisonLoans[loanId];
        if (operation.Reservation >= 0 && operation.Kind == OwnershipOperationKind.Read)
        {
            operation = operation with { Kind = OwnershipOperationKind.UpdateTarget, LoanMode = LoanRequirement.Uniq };
        }

        if (loan.Reservation >= 0)
        {
            var reservation = this.CallReservations[loan.Reservation];
            if (reservation.Place >= 0 && this.borrowDefinitions.Length >= this.Places.Count && this.IsDisjointProjection(id, reservation.Place))
            {
                return false;
            }

            loan = loan with { Mode = this.ReservationMode(loan.Reservation, activation >= 0 ? activation : id) };
        }

        if (operation.Reservation >= 0 && activation < 0)
        {
            // Independent overlapping reservations are never permitted.
            operation = operation with { LoanMode = loan.Reservation >= 0 ? LoanRequirement.Uniq : LoanRequirement.Ref };
        }

        if (loan.Call is { BoundCall.Target.CompilerFunction: CompilerFunctionKind.Replace or CompilerFunctionKind.Exchange or CompilerFunctionKind.Swap } &&
            ReferenceEquals(operation.Source, loan.Call) && operation.Kind is OwnershipOperationKind.Consume or OwnershipOperationKind.Write)
        {
            return false; // The selected intrinsic operates through its acquired target Loan.
        }

        if (operation.Projection >= 0 && operation.Kind is OwnershipOperationKind.Produce or OwnershipOperationKind.WriteElement or OwnershipOperationKind.Read or OwnershipOperationKind.Borrow)
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

            // Root protection during location is wider than a completed element borrow.
            return (operation.Kind == OwnershipOperationKind.WriteElement ||
                (operation.Acquisition is AcquisitionKind.Move or AcquisitionKind.CopyOrMove && loanId != access.Loan)) &&
                (loan.Projection < 0 || this.ElementPathsOverlap(operation.Projection, loan.Projection));
        }

        return ConflictsWithComparison(operation.Kind, operation.Place, operation.Input, operation.Acquisition, loan.Place, loan.Mode, operation.LoanMode);
    }

    internal bool ElementWriteLoanConflicts(int loanId)
    {
        var loan = this.ComparisonLoans[loanId];
        for (var head = loan.Parent; head >= 0; head = this.ComparisonLoans[head].Parent)
        {
            var existing = this.ComparisonLoans[head];
            if (existing.Place == loan.Place &&
                (existing.Projection < 0 || this.ElementPathsOverlap(loan.Projection, existing.Projection)))
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
        if (!this.ValidateElementPaths() || !this.ValidateCallReservations())
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
            if (loan.Reservation >= 0)
            {
                if (!this.ValidCallReservation(loan.Reservation, i))
                {
                    return false;
                }

                continue;
            }

            if (loan.Callable is not null)
            {
                if (!this.ValidCallableLoan(i))
                {
                    return false;
                }

                continue;
            }

            if (loan.Mode == LoanRequirement.Uniq)
            {
                if ((uint)loan.Read >= (uint)this.Operations.Count || !this.ValidElementWriteLoan(i))
                {
                    return false;
                }

                continue;
            }

            if (loan.Projection >= 0)
            {
                if (!this.ValidElementBorrowLoan(i))
                {
                    return false;
                }

                continue;
            }

            if ((uint)loan.Read >= (uint)this.Operations.Count || (uint)loan.Place >= (uint)this.Places.Count || loan.Parent < -1 || loan.Parent >= i || loan.Depth <= 0 || loan.Guard < -1 ||
                this.Operations[loan.Read].Kind != (loan.Access ? OwnershipOperationKind.LocateReceiver : loan.Call is null ? OwnershipOperationKind.Read : OwnershipOperationKind.Borrow) ||
                loan.Mode != LoanRequirement.Ref || loan.Projection != -1 || this.Operations[loan.Read].Place != loan.Place ||
                (loan.Guard < 0 && this.Places[loan.Place].Kind is not (OwnershipPlaceKind.Local or OwnershipPlaceKind.Parameter) &&
                    ((!loan.Access && loan.Call is null && this.Places[loan.Place].Type.Kind != BoundTypeKind.Function) || this.Places[loan.Place].Kind is not (OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result))) ||
                (loan.Access ? loan.Call is not null || loan.Guard != -1 || (this.Places[loan.Place].Type.Kind is not (BoundTypeKind.Tuple or BoundTypeKind.FixedArray or BoundTypeKind.Array or BoundTypeKind.Dictionary) && !StructStorage.IsStruct(this.Places[loan.Place].Type))
                    : loan.Guard < 0 && !ReferenceEquals(this.Places[loan.Place].Type, BoundType.String) && this.Places[loan.Place].Type.Kind != BoundTypeKind.Function) ||
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
                    (this.ComparisonLoans[output].Reservation >= 0 ? !this.ValidCallReservation(this.ComparisonLoans[output].Reservation, output) : this.ComparisonLoans[output].Callable is not null ? !this.ValidCallableLoan(output) : this.ComparisonLoans[output].Mode == LoanRequirement.Uniq ? !this.ValidElementWriteLoan(output) :
                        this.ComparisonLoans[output].Projection >= 0 ? !this.ValidElementBorrowLoan(output) : this.ComparisonLoans[output].Parent != input))
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
            if (region.Seed >= 0 && region.Entry >= 0 && region.SeedCount == 0 && !this.CheckingSeedLoansMatch(region.Seed, region.Replay, region.Entry))
            {
                return false;
            }

            for (var i = 0; region.Entry >= 0 && i < region.SeedCount; i++)
            {
                var seed = this.CheckingSeeds[region.SeedStart + i];
                if (!this.CheckingSeedLoansMatch(seed.Operation, seed.Replay, region.Entry))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool CheckingSeedLoansMatch(int operation, int replay, int entry)
    {
        if (replay < -1 || replay >= this.CheckingReplays.Count)
        {
            return false;
        }

        // Replay includes guard cleanup. The original seed can still carry a
        // protection that was ended before this checking-only arrival.
        var end = replay < 0 ? operation : this.CheckingReplays[replay].End;
        return (uint)end < (uint)this.LoanStates.Count && (uint)entry < (uint)this.LoanInputs.Count &&
            this.LoanStates[end] == this.LoanInputs[entry];
    }

    private bool ValidCallableLoan(int index)
    {
        var loan = this.ComparisonLoans[index];
        return loan.Callable is { BoundValueCall: { } plan } && plan.ReceiverKind is SemanticsKind.Ref or SemanticsKind.Uniq &&
            loan.Mode == (plan.ReceiverKind == SemanticsKind.Uniq ? LoanRequirement.Uniq : LoanRequirement.Ref) &&
            (uint)loan.Place < (uint)this.Places.Count && (uint)loan.Read < (uint)this.Operations.Count && loan.Parent < index && loan.Depth > 0 &&
            loan.Call is null && loan.Guard == -1 && !loan.Access && loan.Projection == -1 &&
            this.Operations[loan.Read].Kind == OwnershipOperationKind.Read && this.Operations[loan.Read].Place == loan.Place &&
            ReferenceEquals(this.Operations[loan.Read].Source, plan.Receiver) && ReferenceEquals(this.Places[loan.Place].Type, this.Concrete(plan.ReceiverType)) &&
            (loan.Mode != LoanRequirement.Uniq || this.Places[loan.Place].Type.Semantics == SemanticsKind.Uniq || this.Places[loan.Place].Mutable) &&
            this.LoanInputs[loan.Read] == loan.Parent && this.LoanStates[loan.Read] == index &&
            (loan.Parent < 0 || this.ComparisonLoans[loan.Parent].Depth <= loan.Depth) &&
            (loan.Reservation >= 0 || loan.Mode != LoanRequirement.Uniq || !this.ElementWriteLoanConflicts(index));
    }

    private bool ValidElementWriteLoan(int id)
    {
        var loan = this.ComparisonLoans[id];
        if (loan.Call is { BoundCall: { } call } syntax && loan.Mode == LoanRequirement.Uniq &&
            call.Target.CompilerFunction is CompilerFunctionKind.Replace or CompilerFunctionKind.Exchange or CompilerFunctionKind.Swap)
        {
            if (loan.Access || loan.Projection != -1 || loan.Guard != -1 || loan.Depth <= 0 ||
                (uint)loan.Place >= (uint)this.Places.Count || (uint)loan.Read >= (uint)this.Operations.Count ||
                loan.Parent < -1 || loan.Parent >= id || this.LoanStates[loan.Read] != id || this.LoanInputs[loan.Read] != loan.Parent ||
                this.Operations[loan.Read] is not { Kind: OwnershipOperationKind.UpdateTarget, LoanMode: LoanRequirement.Uniq } operation ||
                operation.Place != loan.Place || !this.Places[loan.Place].Mutable ||
                (this.IsReachable(loan.Read) && (this.GetInputState(loan.Read, loan.Place) & PlaceState.MustInit) == 0))
            {
                return false;
            }

            for (var i = 0; i < syntax.ArgumentNodes.Count; i++)
            {
                var argument = call.ArgumentOperations[i];
                if (ReferenceEquals(syntax.ArgumentNodes[i], operation.Source) &&
                    (argument.ParameterIndex == 0 || call.Target.CompilerFunction == CompilerFunctionKind.Swap) &&
                    argument.ParameterType is { Semantics: SemanticsKind.Uniq, Components.Count: 1 } parameter &&
                    ReferenceEquals(parameter.Components[0], this.Places[loan.Place].Type))
                {
                    return loan.Reservation >= 0 || !this.ElementWriteLoanConflicts(id);
                }
            }

            return false;
        }

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

        return !this.ElementWriteLoanConflicts(id);
    }

    private bool ValidElementBorrowLoan(int id)
    {
        var loan = this.ComparisonLoans[id];
        if (loan.Mode != LoanRequirement.Ref || loan.Access || loan.Guard != -1 ||
            (uint)loan.Projection >= (uint)this.Projections.Count || (uint)loan.Read >= (uint)this.Operations.Count)
        {
            return false;
        }

        var plan = this.Projections[loan.Projection];
        if ((uint)plan.Loan >= (uint)id || (uint)plan.Root >= (uint)this.Places.Count)
        {
            return false;
        }

        var access = this.ComparisonLoans[plan.Loan];
        var operation = this.Operations[loan.Read];
        return plan.Borrow == loan.Read && plan.Borrow == plan.Operation + 1 && plan.Path == loan.Projection &&
            plan.Output == -1 && plan.Write == -1 && plan.Exclusive == -1 && plan.Update == -1 &&
            ElementAccess.SupportsBorrowRoot(this.Places[plan.Root]) && plan.Root == loan.Place &&
            operation.Kind == (loan.Call is null ? OwnershipOperationKind.Read : OwnershipOperationKind.Borrow) &&
            operation.Place == plan.Root && operation.Projection == loan.Projection &&
            operation.Acquisition == AcquisitionKind.None && operation.LoanMode == LoanRequirement.Ref &&
            (loan.Call is not null || (operation.Input == -1 && this.Values[loan.Read].Kind == OwnershipValueKind.None)) &&
            ReferenceEquals(operation.Source, this.Operations[plan.Operation].Source) && ReferenceEquals(operation.Source.BoundType, BoundType.String) &&
            access.Mode == LoanRequirement.Ref && access.Access && access.Place == loan.Place &&
            access.Parent == loan.Parent && access.Depth == loan.Depth && loan.Depth > 0 &&
            this.LoanInputs[loan.Read] == plan.Loan && this.LoanStates[loan.Read] == id;
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
            this.Values[loan.Read].Kind is OwnershipValueKind.Borrow or OwnershipValueKind.PatternProjection;
    }
}
