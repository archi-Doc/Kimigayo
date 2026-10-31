// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Built-in selections (SPEC 4.6, 4.7): an element of a fixed array, Array, Slice or UniqSlice at a literal or runtime position, or a range
// of them as a Slice; the receiver is located once, before the index (SPEC 4.7.4), and each position is the isize the selection reads,
// which only the selection's one bounds check rejects (SPEC 4.6.3).
internal sealed partial class MirBuilder
{
    // SPEC 15.1.3: a fixed-array element at a literal position in range (its PlaceShape): at the position the index's own plan row keeps
    // (Folded); the position evaluates nothing.
    private MirPlace ConstElement(IndexKoto index, bool input)
        => this.Selected(this.Place(index.Left, input), MirProjectionKind.ConstIndex, (int)HirTables.PlanOf(index).Folded!.Value, index);

    // SPEC 4.7, 4.7.1: a built-in element at a runtime position: an integer, at a written ^x the length minus x, or at another key's
    // resolution; SPEC 5.3: a raw pointer's p[n] is *(p + n). SPEC 4.7.4: the receiver is located once, before the index: a handle the
    // selection reads by value is secured there where the index builds code (Handle), and storage is lent there by a guard, a shared
    // borrow ended once the index is evaluated, before the element's own access is formed. The guard is the receiver's check at the
    // selection (DECISIONS R4-M56), and the position local is located there, so A1 reports an access through the Index under the same
    // subject: one problem unless the receiver changed in between.
    private MirPlace Element(IndexKoto index, bool input)
    {
        var sequence = this.Place(index.Left, input);
        sequence = Builds(index.Right) ? this.Handle(sequence, index.Left) : sequence;
        var guard = MirTypes.SelectsByValue(this.body.TypeOf(sequence)) ? (MirPlace?)null : this.Borrow(SemanticsKind.Ref, sequence, index).Place;
        var key = ExpansionPlan.Of(index)?.First;
        MirPlace position;
        if (key is not null || HirTables.Op(index.Right) == HirOp.Range)
        {
            position = key is null ? this.Boundary(index.Right, this.Length(sequence, index), index) : this.Resolve(key, index, sequence);
        }
        else if (this.body.TypeOf(sequence) is { Semantics: SemanticsKind.Raw } pointer)
        {
            var count = this.Operand(index.Right, expected: null);
            var offset = this.Temp(pointer, index);
            var start = this.body.AddOperand(MirOperand.Copy(sequence));
            this.body.AddOperand(count);
            this.Set(offset, MirRvalue.Binary(MirBinaryOp.Add, start, proven: true), index);
            return this.body.Project(offset, MirProjectionKind.Deref, 0);
        }
        else
        {
            position = this.Position(this.Operand(index.Right, expected: null), index.Right, index);
        }

        if (guard is { } lent)
        {
            this.body.Push(this.current, MirStatement.Of(MirStatementKind.EndBorrow, lent, this.Info(index)));
        }

        return this.Selected(sequence, MirProjectionKind.Index, position.Root, index);
    }

    // SPEC 4.7.4, 12.2: a selection's receiver whose value it reads, a Slice handle or a raw pointer, secured where it is located (before an
    // index or a boundary that builds code), so that the selection reads the value it located; another receiver is its storage.
    private MirPlace Handle(MirPlace sequence, Koto node) => MirTypes.SelectsByValue(this.body.TypeOf(sequence))
        ? this.Secure(MirOperand.Copy(sequence), node).Place : sequence;

    // SPEC 4.6.1, 4.7.1, 22.5.2 (DECISIONS R5-M84, R5-M154): a position as the isize a selection reads, its value compared mathematically:
    // the value where isize holds it, else -1 (Unresolved), which the selection's one bounds check rejects at the selection; it has no check
    // of its own. A Type whose range lies within isize's (Within: i8 to i64 and u8 to u32 on a 64-bit target) converts proven; another is
    // first compared with isize's extremes in its own Type, the minimum only where it is signed. A Type parameter's range is its instance's:
    // it is compared with 0 in its own Type, a negative value being -1, which every selection rejects as it rejects any negative position
    // (SPEC 4.6.3), and then, by a Proven Cast to u128, which holds every nonnegative value of every PrimitiveInteger Type, with isize's
    // maximum there. The local is located at its selection, whose subject A1 reports an access through its Index under (DECISIONS R4-M56).
    private MirPlace Position(in MirOperand position, Koto node, Koto selection)
    {
        var type = this.TypeOf(position) ?? BoundType.ISize;
        var local = this.Temp(BoundType.ISize, selection);
        if (ReferenceEquals(type, BoundType.ISize) || ReferenceEquals(type, BoundType.Never))
        {
            this.Use(local, position, node);
            return local;
        }

        var outside = this.Within(type, BoundType.ISize) ? -1 : this.body.AddBlock();
        if (outside >= 0)
        {
            var value = position.Kind == MirOperandKind.Move ? MirOperand.Copy(position.Place) : position;
            var test = this.Temp(BoundType.Boolean, node);
            var extreme = Int128.One << (compilation.PointerWidth - 1);
            if (this.Width(type, out var signed) == 0)
            {
                this.Compare(test, MirBinaryOp.Le, MirOperand.Const(this.body.Const(new(MirConstKind.Integer, type))), value, outside, node);
                (type, signed) = (BoundType.Primitives["u128"], false);
                var wide = this.Temp(type, node);
                this.Set(wide, MirRvalue.Cast(MirCastKind.Integer, this.body.AddOperand(value), type) with { Proven = true }, node);
                value = MirOperand.Copy(wide);
            }

            if (signed)
            {
                this.Compare(test, MirBinaryOp.Le, MirOperand.Const(this.body.Const(new(MirConstKind.Integer, type, -extreme))), value, outside, node);
            }

            this.Compare(test, MirBinaryOp.Le, value, MirOperand.Const(this.body.Const(new(MirConstKind.Integer, type, extreme - 1))), outside, node);
        }

        this.Set(local, MirRvalue.Cast(MirCastKind.Integer, this.body.AddOperand(position), BoundType.ISize) with { Proven = true }, node);
        if (outside >= 0)
        {
            this.Unresolved(local, outside, node);
        }

        return local;
    }

    // SPEC 4.6.1, 4.6.3: a position that does not resolve, where its tests leave for outside, is -1, which the selection's one bounds check
    // rejects; building goes on where it joins the resolved one.
    private void Unresolved(MirPlace position, int outside, Koto node)
    {
        var join = this.body.AddBlock();
        this.body.Terminate(this.current, MirTerminator.Goto(join, this.Info(node)));
        this.current = outside;
        this.Use(position, MirOperand.Const(this.body.Const(new(MirConstKind.Integer, BoundType.ISize, -1))), node);
        this.body.Terminate(outside, MirTerminator.Goto(join, this.Info(node)));
        this.current = join;
    }

    // SPEC 4.6.3, 4.8.1: a built-in range selection: the receiver located once and its range resolved against its length, a range written
    // directly at each boundary (omitted: 0 or the length; ^x: the length minus x; a closed end q: q + 1), else the ResolvedRange its key
    // is or its key's resolution (First) gives; checked once (0 <= start <= end <= length, else KIMI_E_INDEX_BOUNDS), then the Slice RawSlice
    // serves (R3 decision 5) of the elements from the address of element start, any address for none (SPEC 5.6), backed by what the
    // Slice retains (DECISIONS R4-M40): a Slice receiver's handle, its value and no handle variable, read where it is located (Handle), or
    // a shared borrow of an array's whole Place made where it is located, before the boundaries, which is the receiver's check (R4-M56).
    private void Slice(MirPlace destination, IndexKoto index)
    {
        var range = HirTables.Op(index.Right) == HirOp.Range ? index.Right as RangeKoto : null;
        var sequence = this.Place(index.Left);
        var later = range is null ? Builds(index.Right) : (range.Start is { } from && Builds(from)) || (range.End is { } to && Builds(to));
        sequence = later ? this.Handle(sequence, index.Left) : sequence;
        var backing = MirTypes.SelectsByValue(this.body.TypeOf(sequence)) ? MirOperand.Copy(sequence) : this.Borrow(SemanticsKind.Ref, sequence, index);
        var length = this.Length(sequence, index);
        var zero = MirOperand.Const(this.body.Const(new(MirConstKind.Integer, BoundType.ISize)));
        MirPlace start, last;
        if (range is not null)
        {
            start = range.Start is null ? this.Position(zero, index, index) : this.Boundary(range.Start, length, index);
            last = range.End is null ? length : this.Boundary(range.End, length, index);
        }
        else
        {
            var resolved = this.Follow(ExpansionPlan.Of(index)?.First is { } key ? this.Resolve(key, index, sequence) : this.Place(index.Right), 16);
            (start, last) = (this.Position(MirOperand.Copy(this.Bound(resolved, "start")), index, index),
                this.Position(MirOperand.Copy(this.Bound(resolved, "end")), index, index));
        }

        var test = this.Temp(BoundType.Boolean, index);
        var count = this.Temp(BoundType.ISize, index);
        var fail = this.body.AddBlock();
        var closed = range?.IsInclusive == true;
        this.Compare(test, MirBinaryOp.Le, zero, MirOperand.Copy(start), fail, index);
        this.Compare(test, MirBinaryOp.Le, MirOperand.Copy(start), MirOperand.Copy(last), fail, index);
        this.Compare(test, closed ? MirBinaryOp.Lt : MirBinaryOp.Le, MirOperand.Copy(last), MirOperand.Copy(length), fail, index);
        this.Fail(fail, "KIMI_E_INDEX_BOUNDS", index);
        var operands = this.body.AddOperand(MirOperand.Copy(last));
        this.body.AddOperand(MirOperand.Copy(start));
        this.Set(count, MirRvalue.Binary(MirBinaryOp.Sub, operands, proven: true), index);
        if (closed)
        {
            operands = this.body.AddOperand(MirOperand.Copy(count));
            this.body.AddOperand(MirOperand.Const(this.body.Const(new(MirConstKind.Integer, BoundType.ISize, 1))));
            this.Set(count, MirRvalue.Binary(MirBinaryOp.Add, operands, proven: true), index);
        }

        var element = TypeOf(index).Components[0];
        var pointer = this.Temp(compilation.Types.Pointer(element), index);
        var some = this.body.AddBlock();
        var empty = this.body.AddBlock();
        var join = this.body.AddBlock();
        this.body.Terminate(this.current, MirTerminator.SwitchInt(MirOperand.Copy(count), this.body.AddCase(0, empty), 1, some, this.Info(index)));
        this.body.Push(
            some,
            MirStatement.Assign(
            pointer,
            this.Ref(
            this.body.Project(sequence, MirProjectionKind.Index, start.Root),
            this.body.TypeOf(pointer)!,
            input: false),
            this.Info(index)));
        this.body.Push(
            empty,
            MirStatement.Assign(
            pointer,
            MirRvalue.Use(this.body.AddOperand(MirOperand.Const(this.body.Const(new(MirConstKind.Integer, this.body.TypeOf(pointer)!))))),
            this.Info(index)));
        this.body.Terminate(some, MirTerminator.Goto(join, this.Info(index)));
        this.body.Terminate(empty, MirTerminator.Goto(join, this.Info(index)));
        var at = this.arguments.Count;
        this.current = join;
        this.arguments.Add(MirOperand.Move(pointer));
        this.arguments.Add(MirOperand.Copy(count));
        this.arguments.Add(backing);
        this.Serve(destination, CompilerFunctionKind.RawSlice, null, [element], at, index);
    }

    // A selection's position (SPEC 4.6.1, 4.7.1): an integer as the isize it reads (Position); a written ^x, which resolves only for
    // 0 <= x <= L, the length minus x where 0 <= x, a literal in range proving it, so the subtraction cannot overflow, else -1 (Unresolved);
    // an offset past the length gives a negative position. Neither fails here: the selection's one bounds check rejects it, after every
    // boundary is evaluated (SPEC 4.7.4), at the selection (SPEC 22.5.1). The position is a local located at its selection.
    private MirPlace Boundary(Koto node, MirPlace length, Koto selection)
    {
        if (node is not FromEndIndexKoto { Operand: var offset } || HirTables.Op(node) != HirOp.Range)
        {
            return this.Position(this.Operand(node, expected: null), node, selection);
        }

        var operand = this.Operand(offset, expected: null);
        var x = this.Position(operand, offset, offset);
        var position = this.Temp(BoundType.ISize, selection);
        var outside = -1;
        if (operand.Kind != MirOperandKind.Const || this.body.Consts[operand.Arg].Bits < 0 || this.body.Consts[operand.Arg].Bits > int.MaxValue)
        {
            var zero = MirOperand.Const(this.body.Const(new(MirConstKind.Integer, BoundType.ISize)));
            outside = this.body.AddBlock();
            this.Compare(this.Temp(BoundType.Boolean, node), MirBinaryOp.Le, zero, MirOperand.Copy(x), outside, node);
        }

        var operands = this.body.AddOperand(MirOperand.Copy(length));
        this.body.AddOperand(MirOperand.Copy(x));
        this.Set(position, MirRvalue.Binary(MirBinaryOp.Sub, operands, proven: true), node);
        if (outside >= 0)
        {
            this.Unresolved(position, outside, node);
        }

        return position;
    }

    // SPEC 4.6.3: a key resolved against the length of the selection's receiver by its resolution call (First), whose Evaluated argument is
    // that length: the receiver is not evaluated again.
    private MirPlace Resolve(CallPlan key, IndexKoto index, MirPlace sequence)
    {
        var length = this.Length(sequence, index);
        var result = this.Hold(key.ReturnType, index);
        this.Call(result, key, index, null, key.ArgumentOperations[ExpansionPlan.Of(index)!.Value.Evaluated].Source, MirOperand.Copy(length));
        return this.Made(result);
    }

    // A located sequence's length, a part of its selection made before its boundaries or key.
    private MirPlace Length(MirPlace sequence, Koto node)
    {
        var length = this.Temp(BoundType.ISize, node);
        this.Set(length, MirRvalue.Len(sequence), node);
        return length;
    }

    // A boundary Field of a ResolvedRange.
    private MirPlace Bound(MirPlace range, string name) => this.body.Project(range, MirProjectionKind.Field, AdtDef.IndexOf(this.body.TypeOf(range)!, name));
}
