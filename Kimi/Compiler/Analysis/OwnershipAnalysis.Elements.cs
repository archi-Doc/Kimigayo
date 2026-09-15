// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private int BorrowStringElement(BinaryKoto source, InvocationKoto? call, BoundType? type, out int loan)
    {
        loan = -1;
        var projection = this.LocateElement(source);
        if (projection < 0)
        {
            return -1;
        }

        var plan = this.body.Projections[projection];
        if (!ReferenceEquals(source.BoundType, BoundType.String) || plan.Path != projection ||
            !ElementAccess.SupportsBorrowRoot(this.body.Places[plan.Root]))
        {
            this.Unsupported(source);
            return -1;
        }

        var result = call is null ? -1 : this.Place(source, type, OwnershipPlaceKind.Temporary, false, AcquisitionKind.Copy);
        var operation = this.Emit(call is null ? OwnershipOperationKind.Read : OwnershipOperationKind.Borrow, source, plan.Root, result, loanMode: LoanRequirement.Ref, projection: projection);
        var access = this.body.ComparisonLoans[plan.Loan];
        // Replace only this access's root protection after bounds resolution. The
        // resulting element Loan retains the comparison/call's enclosing depth.
        loan = this.body.ComparisonLoans.Count;
        this.body.ComparisonLoans.Add(new(operation, plan.Root, access.Parent, access.Depth, Call: call, Projection: projection));
        this.body.Projections[projection] = plan with { Borrow = operation };
        this.body.LoanStates[operation] = loan;
        return call is null ? plan.Root : this.RegisterTemporary(result);
    }

    private int UpdateElement(Koto source, BinaryKoto target)
    {
        var operation = ElementAccess.UpdateOperator(source.Akind);
        var type = ElementAccess.DestinationType(target, target.BoundType);
        if (operation == KotoKind.Invalid || type?.IsNumeric != true || ElementAccess.WritableRoot(target) is null)
        {
            this.Unsupported(source);
            return -1;
        }

        var depth = this.comparisonDepth++;
        var projection = this.LocateElement(target);
        if (projection >= 0)
        {
            this.BeginElementWrite(projection);
        }

        var previous = this.Value(this.AcquireElement(target, projection));
        // A transfer can leave a checking continuation; still check the written RHS there.
        var right = source is BinaryKoto binary ? this.Value(this.Expression(binary.Right))
            : previous >= 0 ? this.IncrementOne(source) : -1;
        var updated = previous >= 0 && right >= 0 && this.flow!.Nodes[source].CanCompleteNormally
            ? this.ComputeUpdate(source, type, previous, right, operation) : -1;
        if (updated >= 0)
        {
            this.StoreElement(source, projection, updated);
        }

        this.EndComparisonLoans(depth, source);
        this.comparisonDepth = depth;
        if (updated < 0)
        {
            return -1;
        }

        var result = this.UpdateResult(source, previous, updated);
        var index = this.body.ElementUpdates.Count;
        this.body.ElementUpdates.Add(new(projection, right, this.Value(updated), this.Value(result)));
        this.body.Projections[projection] = this.body.Projections[projection] with { Update = index };
        return result;
    }

    private int AssignElement(BinaryKoto assignment, BinaryKoto target)
    {
        if (assignment.Akind != KotoKind.Equals || ElementAccess.WritableRoot(target) is null)
        {
            this.Unsupported(assignment);
            return -1;
        }

        var input = this.Expression(assignment.Right);
        if (input < 0)
        {
            return -1;
        }

        var depth = this.comparisonDepth++;
        var projection = this.LocateElement(target);
        var complete = projection >= 0 && this.flow!.Nodes[assignment].CanCompleteNormally;
        if (complete)
        {
            this.BeginElementWrite(projection);
            this.StoreElement(assignment, projection, input);
        }

        this.EndComparisonLoans(depth, assignment);
        this.comparisonDepth = depth;
        if (!complete)
        {
            return -1;
        }

        return this.Temporary(assignment);
    }

    private void StoreElement(Koto source, int projection, int input)
    {
        var plan = this.body.Projections[projection];
        // Register the only authorized write before conflict checking the emitted operation.
        this.body.Projections[projection] = plan with { Write = this.body.Operations.Count };
        var write = this.Emit(OwnershipOperationKind.WriteElement, source, plan.Root, input, projection: projection);
        if (ScalarResult(this.body.Places[input].Type))
        {
            this.SetValue(write, OwnershipValueKind.Alias, [this.Value(input)]);
        }
    }

    private void BeginElementWrite(int projection)
    {
        var plan = this.body.Projections[projection];
        var access = this.body.ComparisonLoans[plan.Loan];
        // Bounds resolution finishes at ProjectElement. Replace only this access's
        // shared protection; all enclosing Loans retain their independent lifetimes.
        var loan = this.body.ComparisonLoans.Count;
        this.body.ComparisonLoans.Add(new(plan.Operation, plan.Root, access.Parent, access.Depth, LoanRequirement.Uniq, Access: true, Projection: projection));
        this.body.Projections[projection] = plan with { Exclusive = loan };
        this.body.LoanStates[plan.Operation] = loan;
        if (this.body.ElementWriteLoanConflicts(loan))
        {
            this.body.ReportIssue(new(this.body.Operations[plan.Operation].Source, OwnershipFailure.ComparisonLoanConflict));
        }
    }

    private int ElementValue(BinaryKoto source, PlaceUseKind use)
    {
        var depth = this.comparisonDepth++;
        var projection = this.LocateElement(source);
        var result = this.AcquireElement(source, projection, use == PlaceUseKind.Consume);
        this.EndComparisonLoans(depth, source);
        this.comparisonDepth = depth;
        return result;
    }

    private int AcquireElement(BinaryKoto source, int projection, bool allowMove = false)
    {
        var result = -1;
        if (projection >= 0 && this.flow!.Nodes[source].CanCompleteNormally)
        {
            result = this.Temporary(source, projection: projection);
            if (this.body.Places[result].Acquisition != AcquisitionKind.Copy &&
                (!allowMove || this.body.Places[result].Acquisition != AcquisitionKind.Move || this.body.Projections[projection].Path != projection ||
                    !ElementAccess.SupportsMoveRoot(this.body.Places[this.body.Projections[projection].Root])))
            {
                // Inspection and shared arguments must borrow the element Place;
                // they may not silently Move it into a temporary to obtain a borrow.
                this.Unsupported(source);
            }

            var output = this.Value(result);
            this.SetValue(output, OwnershipValueKind.Element, []);
            this.body.Projections[projection] = this.body.Projections[projection] with { Output = output };
        }

        return result;
    }

    private int LocateElement(BinaryKoto source)
    {
        var parent = -1;
        var root = -1;
        var loan = -1;
        var receiver = KotoHelper.UnwrapParentheses(source.Left);
        if (receiver is BinaryKoto nested && ElementAccess.IsSyntax(nested))
        {
            parent = this.LocateElement(nested);
            if (parent >= 0)
            {
                root = this.body.Projections[parent].Root;
                loan = this.body.Projections[parent].Loan;
            }
        }
        else
        {
            root = receiver is IdentifierNameKoto && receiver.BoundSymbol?.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter
                ? this.Local(receiver) : this.Expression(receiver, PlaceUseKind.Read);
            if (root >= 0)
            {
                this.Emit(OwnershipOperationKind.LocateReceiver, receiver, root);
                loan = this.BeginSharedLoan(root, access: true);
            }
        }

        var index = source is IndexKoto ? this.Value(this.Expression(source.Right, PlaceUseKind.Read)) : -1;
        if (root < 0 || (source is IndexKoto && index < 0) || !this.flow!.Nodes[source].CanCompleteNormally)
        {
            return -1;
        }

        if (!ElementAccess.TryType(source, out _, out var element))
        {
            this.Unsupported(source);
            return -1;
        }

        var id = this.body.Projections.Count;
        var selector = ElementAccess.StaticSelector(source);
        var path = parent < 0 ? -1 : this.body.Projections[parent].Path;
        var depth = parent < 0 ? 0 : this.body.Projections[parent].PathDepth;
        if (selector >= 0 && path == parent)
        {
            path = id;
            depth++;
        }

        this.body.Projections.Add(new(this.body.Operations.Count, root, parent, index, element, loan, Path: path, PathDepth: depth, Selector: selector));
        this.Emit(OwnershipOperationKind.ProjectElement, source, root, projection: id);
        return id;
    }
}
