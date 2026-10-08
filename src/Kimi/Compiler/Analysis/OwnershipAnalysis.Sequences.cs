// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    // The evaluated operand of a directly applied range boundary: the boundary itself, or the operand of its `^`.
    private static Koto? PositionOperand(Koto? boundary) => boundary is FromEndIndexKoto fromEnd ? fromEnd.Operand : boundary;

    private int CreateSlice(IndexKoto source)
    {
        var depth = this.comparisonDepth++;
        var receiver = this.SequenceReceiver(source.Left, out var projection);
        int result;
        if (ElementAccess.IsResolvedSlice(source))
        {
            // SPEC 4.6.4: a ResolvedRange key, written or resolved from a range key of other boundaries, is one value.
            var key = receiver < 0 ? -1 : this.Value(this.SelectionKey(source, receiver, projection));
            result = receiver < 0 || key < 0 ? -1 : this.SequenceValue(source, source.BoundType!, SequenceOperation.Slice, receiver, projection, key);
        }
        else
        {
            // SPEC 4.6.4: both written boundaries, or the operands of their `^`, are evaluated before either is converted
            // to an isize position; the slice operation resolves from-end boundaries and checks the interval once.
            var range = (RangeKoto)source.Right;
            var begin = PositionOperand(range.Start);
            var finish = PositionOperand(range.End);
            var startPlace = begin is null ? -1 : this.Expression(begin);
            var endPlace = finish is null ? -1 : this.Expression(finish);
            var start = begin is null ? -1 : this.Value(this.PositionPlace(begin, startPlace));
            var end = finish is null ? -1 : this.Value(this.PositionPlace(finish, endPlace));
            result = receiver < 0 || (range.Start is not null && start < 0) || (range.End is not null && end < 0)
                ? -1 : this.SequenceValue(source, source.BoundType!, SequenceOperation.Slice, receiver, projection, start, end);
        }

        this.EndComparisonLoans(depth, source);
        this.comparisonDepth = depth;
        return result;
    }

    private int ReadSlice(IndexKoto source, AcquisitionKind? acquisition)
    {
        if (ReferenceTypes.IsDictionary(source.Left.BoundType))
        {
            if (acquisition == AcquisitionKind.Move || this.Resolve(source.BoundType, this.Active) is not { } stored || !this.SupportsCopySnapshot(stored, source))
            {
                if (acquisition is null && this.compilation.Binding.ProveCopy(source.BoundType!, source) != ConstraintProof.Proven)
                {
                    this.body.ReportIssue(new(source, OwnershipFailure.TransferRequired));
                }
                else
                {
                    this.Unsupported(source);
                }

                return -1;
            }

            // Lookup produces the same borrowed Place used by explicit @ref; a bare read snapshots its Copy referent.
            var reference = this.BorrowStruct(source, this.compilation.Binding.SharedReference(source.BoundType!, source.Left.BoundType!.Origin));
            return reference < 0 ? -1 : this.LoadPointer(source, this.Value(reference));
        }

        var depth = this.comparisonDepth++;
        var dynamicArray = source.Left.BoundType?.Kind == BoundTypeKind.Array;
        // Snapshot a Copy Slice/reference, but keep an owned Array in its Place. Protect
        // the handle throughout index evaluation so it cannot be resized underneath the read.
        var keepPlace = dynamicArray || ((ReferenceTypes.IsArray(source.Left.BoundType) || ReferenceTypes.IsDictionary(source.Left.BoundType)) && source.Left.BoundType!.Semantics == SemanticsKind.Uniq);
        var receiver = this.compilation.Binding.TryGetAdaptation(source.Left, out var acquisitionView) && acquisitionView.Kind == ExpectedAdaptationKind.SharedBorrow
            ? this.Receiver(source.Left) : this.Expression(source.Left, keepPlace ? PlaceUseKind.Read : PlaceUseKind.Consume);
        if (dynamicArray && receiver >= 0)
        {
            this.Emit(OwnershipOperationKind.LocateReceiver, source.Left, receiver);
            this.BeginSharedLoan(receiver, access: true);
        }

        var index = this.Value(this.SelectionKey(source, receiver));
        if (receiver < 0 || index < 0)
        {
            this.EndComparisonLoans(depth, source);
            this.comparisonDepth = depth;
            return -1;
        }

        int result;
        if (acquisition == AcquisitionKind.Move || this.Resolve(source.BoundType, this.Active) is not { } resultType ||
            !this.SupportsCopySnapshot(resultType, source))
        {
            if (acquisition is null && source.BoundType is { } element && this.compilation.Binding.ProveCopy(element, source) != ConstraintProof.Proven)
            {
                // Like a bare local, the rejected acquisition still yields its typed element: dropping it would invent an
                // uninitialized use where the value arrives, such as a returned result.
                this.body.ReportIssue(new(source, OwnershipFailure.TransferRequired));
                result = this.Temporary(source);
            }
            else
            {
                this.Unsupported(source);
                result = -1;
            }
        }
        else
        {
            result = this.SequenceValue(source, source.BoundType!, SequenceOperation.Read, receiver, index: index);
        }

        this.EndComparisonLoans(depth, source);
        this.comparisonDepth = depth;
        return result;
    }

    // SPEC 4.6.2, 4.6.9: an integer position of another Type is converted to isize where the plan places it. A value that
    // isize cannot hold becomes -1, which the one bounds check of its use rejects; the universal verification of a generic
    // integer has no value to convert.
    private int PositionPlace(Koto source, int place)
    {
        if (place < 0 || this.Value(place) < 0 || ReferenceEquals(this.body.Places[place].Type, BoundType.ISize) || this.body.Places[place].Type is not { IsInteger: true })
        {
            return place;
        }

        var converted = this.Place(source, BoundType.ISize, OwnershipPlaceKind.Temporary, false);
        this.Emit(OwnershipOperationKind.Produce, source, converted);
        this.RegisterTemporary(converted);
        this.SetValue(this.Value(converted), OwnershipValueKind.Convert, [this.Value(place)], constant: OwnershipValue.PositionConversion);
        return converted;
    }

    private int SequenceValue(Koto source, BoundType type, SequenceOperation kind, int receiver, int projection = -1, int index = -1, int end = -1, int address = -1)
    {
        var result = this.Place(source, type, OwnershipPlaceKind.Temporary, true);
        var op = this.Emit(OwnershipOperationKind.Produce, source, result);
        this.SetValue(op, OwnershipValueKind.Sequence, ReferenceTypes.IsArray(this.body.Places[receiver].Type) || ReferenceTypes.IsDynamicArray(this.body.Places[receiver].Type) || ReferenceTypes.IsDictionary(this.body.Places[receiver].Type) || FormattingTypes.IsSliceBorrow(this.body.Places[receiver].Type) || ReferenceTypes.IsSlice(this.body.Places[receiver].Type) ? [this.Value(receiver)] : [], constant: this.body.Sequences.Count);
        this.body.Sequences.Add(new(op, kind, receiver, projection, index, end, address));
        return this.RegisterTemporary(result);
    }

    private int SequenceReceiver(Koto source, out int projection, BoundOrigin? elementOrigin = null)
    {
        projection = -1;
        var receiver = KotoHelper.UnwrapParentheses(source);
        if (receiver is EvaluatedKoto evaluated)
        {
            return this.EvaluatedOperand(evaluated, out projection); // The selection already located its receiver.
        }

        if (this.compilation.Binding.ImplicitPairAdmitted(receiver) != SemanticsMask.None)
        {
            if (this.ThroughPairLayers(receiver, SemanticsKind.Ref) is var through && through != -2)
            {
                return through;
            }

            // SPEC 13.5.5.1: a pair receiver in a ref or uniq instance is read as the reference it holds; the universal
            // verification and an owner instance share the Place itself for the operation.
            var pair = this.Expression(source, PlaceUseKind.Read);
            if (pair >= 0 && !ReferenceTypes.IsBorrow(this.body.Places[pair].Type))
            {
                this.Emit(OwnershipOperationKind.LocateReceiver, receiver, pair);
                this.BeginSharedLoan(pair, access: true);
            }

            return pair;
        }

        if (receiver is ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PairFollow } followed && this.FollowsReference(followed) &&
            source.BoundType?.Kind is BoundTypeKind.Array or BoundTypeKind.Dictionary)
        {
            // SPEC 13.5.5, 14.6.2: a followed reference to a collection is used through that reference, like the bare reference.
            return this.Expression(KotoHelper.UnwrapParentheses(followed.Left), PlaceUseKind.Read);
        }

        if (receiver is IndexKoto && this.compilation.Binding.TryGetAdaptation(receiver, out var view) && view.Kind == ExpectedAdaptationKind.SharedBorrow)
        {
            // SPEC 4.6.6: a view of a fixed-array element reached through a Slice or a borrow selects through the borrowed element.
            return this.BorrowStruct(receiver, view.Type);
        }

        if (receiver is BinaryKoto element && ElementAccess.IsSyntax(element) && source.BoundType?.Kind == BoundTypeKind.FixedArray)
        {
            projection = this.LocateElement(element);
            return projection < 0 ? -1 : this.body.Projections[projection].Root;
        }

        if (source.BoundType?.Kind is BoundTypeKind.Array or BoundTypeKind.Dictionary && receiver is BinaryKoto stored &&
            (ElementAccess.IsSyntax(stored) || (stored is MemberAccessKoto borrowedField && ElementAccess.BorrowedPathRoot(borrowedField) is not null)) &&
            !Binding.IsGetterResult(stored) && !this.SpecialField(stored))
        {
            // SPEC 4.6.1: collection fields and elements are inspected through their shared Place, without attempting to
            // Copy or Move the Non-Copy handle. An element borrowed through it keeps the Origin of that borrow.
            return this.BorrowStruct(stored, this.compilation.Binding.SharedReference(source.BoundType, elementOrigin));
        }

        if (source.BoundType?.Kind is BoundTypeKind.Array or BoundTypeKind.Dictionary)
        {
            // SPEC 4.6.1: an owned Array shares access for the operation and is never consumed by it. An owner pair layer
            // followed explicitly locates the Place it selects (SPEC 13.5.5.1).
            var root = this.Expression(source, PlaceUseKind.Read);
            if (root >= 0)
            {
                this.Emit(OwnershipOperationKind.LocateReceiver, this.SelectedPlace(receiver), root);
                this.BeginSharedLoan(root, access: true);
            }

            return root;
        }

        if (source.BoundType?.Kind == BoundTypeKind.FixedArray)
        {
            var root = receiver is IdentifierNameKoto ? this.Local(receiver) : this.Expression(source);
            if (root >= 0)
            {
                this.Emit(OwnershipOperationKind.LocateReceiver, this.SelectedPlace(receiver), root);
                this.BeginSharedLoan(root, access: true);
            }

            return root;
        }

        return this.Expression(source, ReferenceTypes.IsArray(source.BoundType) || ReferenceTypes.IsDynamicArray(source.BoundType) || ReferenceTypes.IsDictionary(source.BoundType) || FormattingTypes.IsSliceBorrow(source.BoundType) || ReferenceTypes.IsSlice(source.BoundType) ? PlaceUseKind.Read : PlaceUseKind.Consume);
    }

    private int SequenceMember(MemberAccessKoto source)
    {
        var depth = this.comparisonDepth++;
        var receiver = this.SequenceReceiver(source.Left, out var projection);
        var kind = ((IdentifierNameKoto)source.Right).IdentifierName switch
        {
            "indices" => SequenceOperation.Indices,
            "start" => SequenceOperation.Start,
            "end" => SequenceOperation.End,
            "isEmpty" => SequenceOperation.IsEmpty,
            "capacity" => SequenceOperation.Capacity,
            _ => SequenceOperation.Length,
        };
        var result = receiver < 0 ? -1 : this.SequenceValue(source, source.BoundType!, kind, receiver, projection);
        this.EndComparisonLoans(depth, source);
        this.comparisonDepth = depth;
        return result;
    }

    private int SequenceConstant(Koto source, BoundType type, long value)
    {
        var result = this.Place(source, type, OwnershipPlaceKind.Temporary, true);
        var op = this.Emit(OwnershipOperationKind.Produce, source, result);
        this.SetValue(op, OwnershipValueKind.Constant, [], constant: value);
        return this.RegisterTemporary(result);
    }

    private void Iterate(ForKoto source)
    {
        if (source.Iteration is { Decomposition.IsCurrent: true } protocol)
        {
            this.IterateUser(source, protocol);
            return;
        }

        // SPEC 4.6.3.4: a ResolvedRange loop enumerates its isize positions directly; every other Subject iterates through
        // its entry conformance. A borrowed interval is read as its Copy value; Binding recorded that read.
        var iterableType = this.compilation.Binding.TryGetAdaptation(source.Iterable, out var read) && read.Kind == ExpectedAdaptationKind.ReferentRead
            ? read.Type : source.Iterable.BoundType;
        if (!ReferenceTypes.IsResolvedRange(iterableType))
        {
            this.Unsupported(source);
            return;
        }

        var iterable = this.Expression(source.Iterable);
        if (iterable < 0)
        {
            return;
        }

        var localMark = this.locals.Count;

        var cursor = this.Place(source, BoundType.ISize, OwnershipPlaceKind.Local, true);
        this.locals.Add(new(cursor, source, this.registrationSequence++));
        this.Emit(OwnershipOperationKind.Declare, source, cursor);
        var first = this.SequenceValue(source, BoundType.ISize, SequenceOperation.Start, iterable);
        var end = this.SequenceValue(source, BoundType.ISize, SequenceOperation.End, iterable);
        this.Emit(OwnershipOperationKind.Write, source, cursor, first);
        var head = this.Emit(OwnershipOperationKind.Branch, source);
        var exit = this.New(OwnershipOperationKind.Branch, source);
        var current = this.Use(source, cursor, PlaceUseKind.Read);
        var testValue = this.ComputeUpdate(source, BoundType.Boolean, this.Value(current), this.Value(end), KotoKind.LessThan);
        var test = this.Emit(OwnershipOperationKind.Branch, source);
        this.SetValue(test, OwnershipValueKind.Alias, [this.Value(testValue)]);
        var enter = this.New(OwnershipOperationKind.Branch, source.Body);
        this.Connect(test, enter, OwnershipEdgeKind.True);
        this.Connect(test, exit, OwnershipEdgeKind.False);
        this.current = enter;
        var bindingMark = this.locals.Count;
        this.loops.Add(new(source, head, exit, bindingMark, this.temporaries.Count, Comparisons: this.comparisonDepth));
        // Each slot has the ordinary iteration lifetime, including unnamed slots.
        for (var slot = 0; slot < source.Bindings.Count; slot++)
        {
            var name = source.Bindings[slot];
            var binding = this.LocalPlace(name.BoundSymbol, name, name.BoundType!, source.IsMutableSlot(slot));
            this.locals.Add(new(binding, name, this.registrationSequence++));
            this.Emit(OwnershipOperationKind.Declare, name, binding);
            var item = this.Place(name, BoundType.ISize, OwnershipPlaceKind.Temporary, true);
            var itemOp = this.Emit(OwnershipOperationKind.Produce, name, item);
            this.SetValue(itemOp, OwnershipValueKind.Alias, [this.Value(current)]);
            this.RegisterTemporary(item);
            this.Emit(OwnershipOperationKind.Write, name, binding, item);
        }

        // The last advance reaches end (at most maximum isize), never end + 1.
        var next = this.ComputeUpdate(source, BoundType.ISize, this.Value(current), this.Value(this.SequenceConstant(source, BoundType.ISize, 1)), KotoKind.Plus);

        this.Emit(OwnershipOperationKind.Write, source, cursor, next);
        var seeds = this.terminalSeeds.Count;
        this.Block(source.Body, out var continuation);
        this.RecordTerminalSeed(source.Body, continuation);
        this.FilterTerminalSeeds(source, seeds);
        this.Cleanup(this.temporaries.Count, bindingMark, source, CleanupReason.ScopeExit);
        this.locals.RemoveRange(bindingMark, this.locals.Count - bindingMark);
        this.Connect(this.current, head, OwnershipEdgeKind.Back);
        this.loops.RemoveAt(this.loops.Count - 1);
        this.current = exit;
        this.Cleanup(this.temporaries.Count, localMark, source, CleanupReason.ScopeExit);
        this.locals.RemoveRange(localMark, this.locals.Count - localMark);
    }
}
