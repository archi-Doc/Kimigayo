// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private int CreateSlice(IndexKoto source)
    {
        var depth = this.comparisonDepth++;
        var receiver = this.SequenceReceiver(source.Left, out var projection);
        var range = (RangeKoto)source.Right;
        var start = range.Start is { } begin ? this.Value(this.Expression(begin)) : -1;
        var end = range.End is { } finish ? this.Value(this.Expression(finish)) : -1;
        var result = receiver < 0 || (range.Start is not null && start < 0) || (range.End is not null && end < 0)
            ? -1 : this.SequenceValue(source, source.BoundType!, SequenceOperation.Slice, receiver, projection, start, end);
        this.EndComparisonLoans(depth, source);
        this.comparisonDepth = depth;
        return result;
    }

    private int ReadSlice(IndexKoto source, AcquisitionKind? acquisition)
    {
        var sequence = ReferenceTypes.IsDynamicArray(source.Left.BoundType) ? source.Left.BoundType!.Components[0] : source.Left.BoundType;
        if (ReferenceTypes.IsStruct(source.BoundType) && source.BoundType!.Semantics == SemanticsKind.Ref &&
            sequence?.Kind is BoundTypeKind.Array or BoundTypeKind.Slice && ReferenceEquals(source.BoundType.Components[0], sequence.Components[0]))
        {
            if (acquisition == AcquisitionKind.Move)
            {
                this.Unsupported(source);
                return -1;
            }

            return this.BorrowStruct(source, source.BoundType);
        }

        var depth = this.comparisonDepth++;
        var dynamicArray = source.Left.BoundType?.Kind == BoundTypeKind.Array;
        // Snapshot a Copy Slice/reference, but keep an owned Array in its Place. Protect
        // the handle throughout index evaluation so it cannot be resized underneath the read.
        var keepPlace = dynamicArray || (ReferenceTypes.IsArray(source.Left.BoundType) && source.Left.BoundType!.Semantics == SemanticsKind.Uniq);
        var receiver = this.Expression(source.Left, keepPlace ? PlaceUseKind.Read : PlaceUseKind.Consume);
        if (dynamicArray && receiver >= 0)
        {
            this.Emit(OwnershipOperationKind.LocateReceiver, source.Left, receiver);
            this.BeginSharedLoan(receiver, access: true);
        }

        var index = this.Value(this.Expression(source.Right));
        if (receiver < 0 || index < 0)
        {
            this.EndComparisonLoans(depth, source);
            this.comparisonDepth = depth;
            return -1;
        }

        if (acquisition == AcquisitionKind.Move || (!ScalarTypes.Supports(source.BoundType) &&
            !(source.BoundType?.Kind == BoundTypeKind.Parameter && ReferenceTypes.IsArray(source.Left.BoundType) &&
            this.compilation.Binding.ProveCopy(source.BoundType, source) == ConstraintProof.Proven)))
        {
            this.Unsupported(source);
            this.EndComparisonLoans(depth, source);
            this.comparisonDepth = depth;
            return -1;
        }

        var result = this.SequenceValue(source, source.BoundType!, SequenceOperation.Read, receiver, index: index);
        this.EndComparisonLoans(depth, source);
        this.comparisonDepth = depth;
        return result;
    }

    private int SequenceValue(Koto source, BoundType type, SequenceOperation kind, int receiver, int projection = -1, int index = -1, int end = -1, int element = -1)
    {
        var result = this.Place(source, type, OwnershipPlaceKind.Temporary, true);
        var op = this.Emit(OwnershipOperationKind.Produce, source, result);
        this.SetValue(op, OwnershipValueKind.Sequence, ReferenceTypes.IsArray(this.body.Places[receiver].Type) || ReferenceTypes.IsDynamicArray(this.body.Places[receiver].Type) ? [this.Value(receiver)] : [], constant: this.body.Sequences.Count);
        this.body.Sequences.Add(new(op, kind, receiver, projection, index, end, element));
        return this.RegisterTemporary(result);
    }

    private int SequenceReceiver(Koto source, out int projection)
    {
        projection = -1;
        var receiver = KotoHelper.UnwrapParentheses(source);
        if (receiver is BinaryKoto element && ElementAccess.IsSyntax(element) && source.BoundType?.Kind == BoundTypeKind.FixedArray)
        {
            projection = this.LocateElement(element);
            return projection < 0 ? -1 : this.body.Projections[projection].Root;
        }

        if (source.BoundType?.Kind == BoundTypeKind.Array)
        {
            // SPEC 4.6.1: an owned Array shares access for the operation and is never consumed by it.
            var root = this.Expression(source, PlaceUseKind.Read);
            if (root >= 0)
            {
                this.Emit(OwnershipOperationKind.LocateReceiver, receiver, root);
                this.BeginSharedLoan(root, access: true);
            }

            return root;
        }

        if (source.BoundType?.Kind == BoundTypeKind.FixedArray)
        {
            var root = receiver is IdentifierNameKoto ? this.Local(receiver) : this.Expression(source);
            if (root >= 0)
            {
                this.Emit(OwnershipOperationKind.LocateReceiver, receiver, root);
                this.BeginSharedLoan(root, access: true);
            }

            return root;
        }

        return this.Expression(source, ReferenceTypes.IsArray(source.BoundType) || ReferenceTypes.IsDynamicArray(source.BoundType) ? PlaceUseKind.Read : PlaceUseKind.Consume);
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
        var shared = source.SharedIterable;
        var array = shared is null && source.Iterable.BoundType?.Kind == BoundTypeKind.FixedArray;
        var dynamicArray = shared is null && source.Iterable.BoundType?.Kind == BoundTypeKind.Array;
        var slice = shared is not null || source.Iterable.BoundType?.Kind == BoundTypeKind.Slice;
        var element = slice ? source.Bindings[0].BoundType! : array || dynamicArray ? source.Iterable.BoundType!.Components[0] : BoundType.ISize;
        if (dynamicArray && (source.IsTupleBinding || !this.SupportsType(element) || this.compilation.Binding.ProveOwned(element, source) != ConstraintProof.Proven))
        {
            this.Unsupported(source);
            return;
        }

        if (array && (!this.SupportsType(element) || this.compilation.Binding.ProveCopy(element, source) != ConstraintProof.Proven))
        {
            this.Unsupported(source);
            return;
        }

        int iterable;
        if (shared is not null)
        {
            // SPEC 14.6.2: a bare array Place is borrowed for the loop as its implicit whole-range Slice.
            var depth = this.comparisonDepth++;
            var receiver = this.SequenceReceiver(source.Iterable, out var projection);
            iterable = receiver < 0 ? -1 : this.SequenceValue(source, shared, SequenceOperation.Slice, receiver, projection);
            this.EndComparisonLoans(depth, source);
            this.comparisonDepth = depth;
        }
        else
        {
            iterable = this.Expression(source.Iterable);
        }

        if (iterable < 0)
        {
            return;
        }

        var localMark = this.locals.Count;
        if (dynamicArray)
        {
            this.SequenceValue(source, BoundType.Unit, SequenceOperation.ArrayIterator, iterable);
        }

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
        // Fixed-array elements are Copy; the owning Array iterator instead transfers
        // one element before the body and keeps responsibility only for its remaining tail.
        for (var slot = 0; slot < source.Bindings.Count; slot++)
        {
            var name = source.Bindings[slot];
            var slotType = name.BoundType!;
            var binding = this.LocalPlace(name.BoundSymbol, name, slotType, false);
            this.locals.Add(new(binding, name, this.registrationSequence++));
            this.Emit(OwnershipOperationKind.Declare, name, binding);
            int item;
            if (dynamicArray)
            {
                item = this.SequenceValue(source, slotType, SequenceOperation.ArrayMoveRead, iterable, index: this.Value(current));
            }
            else if (array)
            {
                item = this.SequenceValue(source, slotType, SequenceOperation.ArrayRead, iterable, index: this.Value(current), element: source.IsTupleBinding ? slot : -1);
            }
            else if (slice)
            {
                item = this.SequenceValue(source, slotType, SequenceOperation.Borrow, iterable, index: this.Value(current));
            }
            else
            {
                item = this.Place(name, BoundType.ISize, OwnershipPlaceKind.Temporary, true);
                var itemOp = this.Emit(OwnershipOperationKind.Produce, name, item);
                this.SetValue(itemOp, OwnershipValueKind.Alias, [this.Value(current)]);
                this.RegisterTemporary(item);
            }

            this.Emit(OwnershipOperationKind.Write, name, binding, item);
        }

        // The last advance reaches end (at most maximum isize), never end + 1.
        var one = this.SequenceConstant(source, BoundType.ISize, 1);
        var next = this.ComputeUpdate(source, BoundType.ISize, this.Value(current), this.Value(one), KotoKind.Plus);
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
