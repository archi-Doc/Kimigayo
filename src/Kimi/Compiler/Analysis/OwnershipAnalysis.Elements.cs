// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    // SPEC 13.7: secure the RHS, select one exclusive element address, then destroy/store through the same replacement
    // used by followed references and Place calls. Fixed and dynamic arrays share the element borrow and replacement.
    private int WriteBorrowedArrayElement(Koto source, IndexKoto target)
    {
        var operation = source.Akind == KotoKind.Equals ? KotoKind.Equals : ElementAccess.UpdateOperator(source.Akind);
        var type = this.Concrete(target.BoundType);
        if (type is null || (operation != KotoKind.Equals && !this.SupportsUpdate(target, target.BoundType, operation)))
        {
            this.Unsupported(source);
            return -1;
        }

        var right = source is BinaryKoto binary ? this.Expression(binary.Right) : -1;
        if (source is BinaryKoto && right < 0)
        {
            return -1;
        }

        var address = this.BorrowStruct(target, this.compilation.Binding.Reference(SemanticsKind.Uniq, target.BoundType!, ElementAccess.AccessType(target.Left, true)!.Origin));
        var pointer = this.Value(address);
        if (pointer < 0)
        {
            return -1;
        }

        var value = right;
        var previous = -1;
        if (operation != KotoKind.Equals)
        {
            var loaded = this.Place(target, type, OwnershipPlaceKind.Temporary, true, AcquisitionKind.Copy);
            this.Emit(OwnershipOperationKind.Produce, target, loaded);
            this.SetValue(this.Value(loaded), OwnershipValueKind.PointerLoad, [pointer]);
            this.RegisterTemporary(loaded);
            previous = this.Value(loaded);
            var operand = source is BinaryKoto ? this.Value(right) : this.IncrementOne(source);
            value = operand >= 0 && this.flow!.Nodes[source].CanCompleteNormally
                ? this.ComputeUpdate(source, type, previous, operand, operation) : -1;
        }

        if (value < 0)
        {
            return -1;
        }

        this.StorePointer(target, pointer, value);
        return operation == KotoKind.Equals ? this.Temporary(source) : this.UpdateResult(source, previous, value);
    }

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
        if (!this.SupportsUpdate(target, type, operation) || ElementAccess.WritableRoot(target) is null)
        {
            this.Unsupported(source);
            return -1;
        }

        // SPEC 13.7.2: the RHS is secured before the element is located and read. A transfer in the RHS
        // leaves a checking continuation; the target is still checked there.
        var right = source is BinaryKoto binary ? this.Value(this.Expression(binary.Right)) : 0;
        if (right < 0)
        {
            return -1;
        }

        var depth = this.comparisonDepth++;
        var projection = this.LocateElement(target);
        if (projection >= 0)
        {
            this.BeginElementWrite(projection);
        }

        var previous = this.Value(this.AcquireElement(target, projection));
        if (source is not BinaryKoto)
        {
            right = previous >= 0 ? this.IncrementOne(source) : -1;
        }

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
        if (this.body.ElementWriteLoanConflicts(loan, reservations: false))
        {
            this.body.ReportIssue(new(this.body.Operations[plan.Operation].Source, OwnershipFailure.ComparisonLoanConflict));
        }
        else if (this.body.ElementWriteLoanConflicts(loan))
        {
            // Only call reservations enclose the write; the completed plan decides whether it reports it against one.
            this.body.HoldReservedElementWrite(projection, new(this.body.Operations[plan.Operation].Source, OwnershipFailure.ComparisonLoanConflict));
        }
    }

    private int ElementValue(BinaryKoto source, PlaceUseKind use, AcquisitionKind? acquisition = null)
    {
        var depth = this.comparisonDepth++;
        var projection = this.LocateElement(source);
        var result = this.AcquireElement(source, projection, use == PlaceUseKind.Consume, acquisition);
        this.EndComparisonLoans(depth, source);
        this.comparisonDepth = depth;
        return result;
    }

    private int AcquireElement(BinaryKoto source, int projection, bool allowMove = false, AcquisitionKind? acquisition = null)
    {
        var result = -1;
        if (projection >= 0 && this.flow!.Nodes[source].CanCompleteNormally)
        {
            result = this.Temporary(source, projection: projection);
            if (allowMove && acquisition is null && this.body.Places[result].Acquisition != AcquisitionKind.Copy)
            {
                // SPEC 3.5: a bare element never Moves; write values[i]@move or pair.0@move. A bare element storing an exclusive
                // reference, the exclusive case of a pair element included, would Reborrow (SPEC 15.1.5), which element paths do not
                // support yet (STATUS), as the concrete element does not.
                if (this.body.Places[result].Type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq, Components.Count: 1 })
                {
                    this.Unsupported(source);
                }
                else
                {
                    this.body.ReportIssue(new(source, OwnershipFailure.TransferRequired));
                }
            }

            if (allowMove && acquisition == AcquisitionKind.Move && this.body.Places[result].Acquisition is AcquisitionKind.Move or AcquisitionKind.CopyOrMove &&
                (this.body.Projections[projection].Path != projection || !ElementAccess.SupportsMoveRoot(this.body.Places[this.body.Projections[projection].Root])))
            {
                // SPEC 15.1.3: an explicit Move of an element without a static Move Path is rejected. The element is then not
                // taken, so later uses of the array are explained by this failure rather than by a Move.
                this.body.ReportIssue(new(source, OwnershipFailure.StaticMovePathRequired));
                var produce = this.body.Operations.Count - 1;
                this.body.OperationStorage[produce] = this.body.OperationStorage[produce] with { Acquisition = AcquisitionKind.None };
            }
            else if (this.body.Places[result].Acquisition != AcquisitionKind.Copy &&
                (!allowMove || this.body.Places[result].Acquisition is not (AcquisitionKind.Move or AcquisitionKind.CopyOrMove) || this.body.Projections[projection].Path != projection ||
                    !ElementAccess.SupportsMoveRoot(this.body.Places[this.body.Projections[projection].Root])))
            {
                // Inspection and shared arguments must borrow the element Place;
                // they may not silently Move it into a temporary to obtain a borrow.
                this.Unsupported(source);
            }

            var output = this.Value(result);
            for (var ancestor = projection; ancestor >= 0; ancestor = this.body.Projections[ancestor].Parent)
            {
                if (this.body.Places[result].Acquisition is AcquisitionKind.Move or AcquisitionKind.CopyOrMove &&
                    this.body.Operations[this.body.Projections[ancestor].Operation].Source is BinaryKoto path &&
                    path.Left.BoundType is { } owner && StructStorage.HasDestructorOnPath(owner, path.BoundSymbol))
                {
                    this.Unsupported(source); // No partial Move through any drop-bearing ancestor.
                }
            }

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
        if (receiver is BinaryKoto nested && !Binding.IsGetterResult(nested) && ElementAccess.IsSyntax(nested) && !ElementAccess.ReachesThroughBorrow(nested))
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
            // SPEC 4.6.6, 4.6.9: a Place reached through a Slice or a borrow is not part of an owned root; a Copy one is read as the root.
            root = receiver is IdentifierNameKoto && receiver.BoundSymbol?.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Capture
                ? this.Local(receiver) : this.Expression(receiver, PlaceUseKind.Read);
            if (root >= 0)
            {
                this.Emit(OwnershipOperationKind.LocateReceiver, receiver, root);
                loan = this.BeginSharedLoan(root, access: true);
            }
        }

        var index = source is IndexKoto keyed ? this.Value(this.SelectionKey(keyed, root, parent, PlaceUseKind.Read)) : -1;
        if (this.defaultFunction is not null && source is IndexKoto keyedRead &&
            this.body.Operations[^1] is { Kind: OwnershipOperationKind.Read } read && ReferenceEquals(read.Source, ElementAccess.ValueSource(ElementAccess.KeySyntax(keyedRead), this.body, this.body.Operations.Count - 1)))
        {
            // Prepared scalar reads retain their acquired value for subsequent uses.
            // The index plan also needs this declaration-side read's source identity.
            index = this.body.Operations.Count - 1;
        }

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
