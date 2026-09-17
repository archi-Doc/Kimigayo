// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private int CreateSlice(IndexKoto source)
    {
        var depth = this.comparisonDepth++;
        var receiver = this.SequenceReceiver(source.Left, out var projection);
        var result = receiver < 0 ? -1 : this.SequenceValue(source, source.BoundType!, SequenceOperation.Slice, receiver, projection);
        this.EndComparisonLoans(depth, source);
        this.comparisonDepth = depth;
        return result;
    }

    private int ReadSlice(IndexKoto source)
    {
        // Snapshot the Copy handle before evaluating the index; its Origin remains
        // live until the indexed read, even if the original handle is reassigned.
        var receiver = this.Expression(source.Left);
        var index = this.Value(this.Expression(source.Right));
        if (receiver < 0 || index < 0)
        {
            return -1;
        }

        if (!ScalarTypes.Supports(source.BoundType))
        {
            this.Unsupported(source);
            return -1;
        }

        return this.SequenceValue(source, source.BoundType!, SequenceOperation.Read, receiver, index: index);
    }

    private int SequenceValue(Koto source, BoundType type, SequenceOperation kind, int receiver, int projection = -1, int index = -1)
    {
        var result = this.Place(source, type, OwnershipPlaceKind.Temporary, true);
        var op = this.Emit(OwnershipOperationKind.Produce, source, result);
        this.SetValue(op, OwnershipValueKind.Sequence, [], constant: this.body.Sequences.Count);
        this.body.Sequences.Add(new(op, kind, receiver, projection, index));
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

        return this.Expression(source);
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
        var name = source.Bindings[0];
        var binding = this.LocalPlace(name.BoundSymbol, name, BoundType.ISize, false);
        this.locals.Add(new(binding, name, this.registrationSequence++));
        this.Emit(OwnershipOperationKind.Declare, name, binding);
        // Acquire the current value before advancing the hidden iterator. The last
        // increment reaches end (at most maximum isize), never end + 1.
        var item = this.Place(name, BoundType.ISize, OwnershipPlaceKind.Temporary, true);
        var itemOp = this.Emit(OwnershipOperationKind.Produce, name, item);
        this.SetValue(itemOp, OwnershipValueKind.Alias, [this.Value(current)]);
        this.RegisterTemporary(item);
        this.Emit(OwnershipOperationKind.Write, name, binding, item);
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
