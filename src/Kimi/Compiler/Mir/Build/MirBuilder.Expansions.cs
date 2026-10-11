// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using System.Text;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Expansions (plan 5.3): the forms whose calls ExpansionPlan and CallPlan.Of name and whose other steps the intrinsic table serves: for
// loops, compound assignments, keyed and range selections, and the dynamic collection literals. Each evaluates an operand once; a later
// step reads the Place it was located at (Located) or the value it gave (a call's given argument).
internal sealed partial class MirBuilder
{
    private readonly List<(Koto Node, MirPlace Place)> located = []; // The Places the expansions being built located, innermost last.
    private readonly List<BoundType> formatTypes = []; // The values' Types of the owning interpolations being built, innermost last.

    // The literal bytes a write of an owning interpolation writes (utf8-formatting 5.1); -1 for a value, whose bound its instance decides.
    private static int Bytes(CallPlan write) => write.ArgumentOperations[1].Source is StringLiteralKoto text ? Encoding.UTF8.GetByteCount(text.Literal) : -1;

    // SPEC 14.6.2: the Subject acquired by the entry call into the iterator once, then each next() result decomposed as its plan's Some/None
    // match: Some binds the item for the body, None completes the loop with Unit. The body end and a continue clean the iteration up and call
    // next() again; an exit or a transfer out cleans it up, then the iterator and the Subject. SPEC 4.6.3.4: a ResolvedRange loop counts
    // its isize positions from start to end instead, checked, without an iterator value.
    private void For(MirPlace destination, ForKoto loop)
    {
        var (plan, entry, step) = (BoundMatch.Of(loop), CallPlan.Of(loop), CallPlan.Of(loop, 1));
        if (HirTables.Meaning(loop) is null || (plan is null) != (entry is null) || (plan is not null && step is null))
        {
            this.Opaque(loop, destination); // Binding rejected its Subject or its bindings.
            return;
        }

        if (plan is null || entry is null || step is null)
        {
            this.Count(destination, loop);
            return;
        }

        var iterator = this.Temp(entry.ReturnType, loop);
        this.Call(iterator, entry, loop);
        this.locals[HirTables.Symbol(step.Receiver!)!] = iterator.Root;
        var head = this.body.AddBlock();
        this.body.Terminate(this.current, MirTerminator.Goto(head, this.Info(loop)));
        var (after, start, none, tail) = (this.Open(loop, destination, head), this.selected.Count, this.body.AddBlock(), this.body.AddBlock());
        this.current = head;
        var iteration = this.OpenScope(false);
        var subject = this.Temp(step.ReturnType, loop, MirLocalRole.Subject);
        this.Call(subject, step, loop);
        CollectionsMarshal.SetCount(this.selected, start + plan.Positions.Count);
        this.Pattern(plan, plan.Arms[0].Pattern, subject, start, none);
        this.Iteration(loop, head, plan, start, default);
        this.current = none;
        this.Pattern(plan, plan.Arms[1].Pattern, subject, start, tail);
        this.Unit(destination, loop);
        this.body.Terminate(this.current, MirTerminator.Goto(this.Exit(this.targets[^1].Depth, after), this.Info(loop)));
        this.body.Terminate(tail, MirTerminator.Unreachable(this.Info(loop)));
        CollectionsMarshal.SetCount(this.selected, start);
        this.CloseScope(iteration, false);
        this.Close(after);
    }

    // SPEC 4.6.3.4: a ResolvedRange loop: the interval copied once, its position compared with its end before each iteration and advanced
    // after it (where a continue goes), checked.
    private void Count(MirPlace destination, ForKoto loop)
    {
        var type = TypeOf(loop.Iterable);
        var range = this.Temp(MirTypes.IsReference(type) ? type.Components[0] : type, loop.Iterable);
        this.Assign(range, loop.Iterable);
        var (position, end, test) = (this.Temp(BoundType.ISize, loop), this.Temp(BoundType.ISize, loop), this.Temp(BoundType.Boolean, loop));
        this.Use(position, MirOperand.Copy(this.Bound(range, "start")), loop);
        this.Use(end, MirOperand.Copy(this.Bound(range, "end")), loop);
        var (head, step, done) = (this.body.AddBlock(), this.body.AddBlock(), this.body.AddBlock());
        this.body.Terminate(this.current, MirTerminator.Goto(head, this.Info(loop)));
        var after = this.Open(loop, destination, step);
        this.current = head;
        this.Compare(test, MirBinaryOp.Lt, MirOperand.Copy(position), MirOperand.Copy(end), done, loop);
        this.Iteration(loop, step, null, 0, position);
        this.current = step;
        var operands = this.body.AddOperand(MirOperand.Copy(position));
        this.body.AddOperand(MirOperand.Const(this.body.Const(new(MirConstKind.Integer, BoundType.ISize, 1))));
        this.Set(position, MirRvalue.Binary(MirBinaryOp.Add, operands, false), loop);
        this.body.Terminate(step, MirTerminator.Goto(head, this.Info(loop)));
        this.current = done;
        this.Unit(destination, loop);
        this.body.Terminate(done, MirTerminator.Goto(after, this.Info(loop)));
        this.Close(after);
    }

    // SPEC 14.6.1: one iteration, its bindings in a scope of their own (the item's by the plan, or the counted position), then the body,
    // whose end goes to the next iteration through the iteration's cleanup.
    private void Iteration(ForKoto loop, int next, BoundMatch? plan, int start, MirPlace position)
    {
        var outer = this.scope;
        this.scope = (ushort)this.body.AddScope(outer, MirScopeKind.Lexical, SourceRef.Of(loop));
        var bindings = this.OpenScope(true);
        if (plan is not null)
        {
            this.Bind(plan, plan.Arms[0].Pattern, start, loop);
        }

        for (var i = 0; plan is null && i < loop.Bindings.Count; i++)
        {
            var (name, symbol) = (loop.Bindings[i], HirTables.Symbol(loop.Bindings[i])!);
            this.locals[symbol] = this.Live(this.body.AddLocal(new(BoundType.ISize, MirLocalKind.Var, this.Info(name, true), loop.IsMutableSlot(i), symbol.Name)), this.lexical);
            this.Use(new(this.locals[symbol]), MirOperand.Copy(position), name);
        }

        this.Iterate(loop.Body, next);
        this.CloseScope(bindings, false);
        this.scope = outer;
    }

    // SPEC 13.7.2, 11.2: the right operand secured, then the target located once, its old value read, the result computed and written back
    // once. Reading and writing are selected independently: a Property's custom getter (First) and setter (Second, taking the result as its
    // Evaluated argument) are called on its receiver, located once, and a standard accessor reads or writes its storage, as a Place target is
    // read and written in place. The old value is read by a built-in operation, or by a user arithmetic call (CallPlan.Of) with the secured
    // operand, whose result replaces it. A compound assignment gives Unit; an increment or decrement its old value (postfix) or its result
    // (prefix), SPEC 13.2, the Property not read again.
    private void Update(MirPlace destination, Koto node)
    {
        var (target, right) = node is BinaryKoto update ? (update.Left, update.Right) : (((UnaryKoto)node).Operand, null);
        var (plan, arithmetic, mark) = (ExpansionPlan.Of(node)!.Value, CallPlan.Of(node), this.located.Count);
        if (plan.Second is not null && plan.Evaluated < 0)
        {
            this.Unsupported(node, destination); // A setter without the argument the update computes.
            return;
        }

        var value = right is null ? MirOperand.Const(this.body.Const(new(MirConstKind.Integer, TypeOf(target), 1))) : this.Secure(this.Operand(right), right);
        var accessor = plan.First ?? plan.Second; // A Property's accessors take its receiver, located once; a static Property's have none.
        var (storage, receiver) = (accessor is null ? this.Place(target) : default, accessor is null || HirTables.ResOf(target) == HirRes.Static ? null : (target as MemberAccessKoto)?.Left);
        if (receiver is not null)
        {
            this.located.Add((receiver, this.Place(receiver)));
        }

        if (accessor is not null && (plan.First is null || plan.Second is null))
        {
            storage = this.Storage(HirTables.Symbol(target)!, receiver is null ? null : this.located[mark].Place, target);
        }

        var read = plan.First is null ? storage : this.Temp(plan.First.ReturnType, target);
        if (plan.First is { } getter)
        {
            // A receiver both accessors take keeps the setter's acquisition as its Adjust; the getter takes it as its own operation does
            // (shared, SPEC 11.2): in place by that adaptation, or by its bare acquisition (a stored reference Copied).
            var (given, operand) = (plan.Second is null ? null : receiver, default(MirOperand));
            foreach (ref readonly var argument in getter.ArgumentOperations)
            {
                if (given is not null && ReferenceEquals(argument.Source, given) && HirAdjust.Of(argument) is var adaptation)
                {
                    operand = this.Acquire(new(adaptation == HirAdaptation.None ? HirAcquire.ByCase : HirAcquire.None, adaptation), this.located[mark].Place, argument.AdaptedType ?? argument.ParameterType, true, given);
                }
            }

            this.Call(read, getter, target, null, given, operand);
        }

        var postfix = node.Akind is KotoKind.PostfixIncrement or KotoKind.PostfixDecrement;
        if (postfix)
        {
            this.Use(destination, MirOperand.Copy(read), node);
        }

        var result = plan.Second is null && arithmetic is null ? storage : this.Temp(arithmetic?.ReturnType ?? this.body.TypeOf(read)!, node);
        if (arithmetic is not null)
        {
            this.located.Add((target, read));
            this.Call(result, arithmetic, node, null, right, value);
        }
        else
        {
            var kind = right is null ? (node.Akind is KotoKind.PrefixPlusPlus or KotoKind.PostfixIncrement ? KotoKind.Plus : KotoKind.Minus) : KotoHelper.CompoundOperation(node.Akind);
            var (op, operands) = (BinaryOp(kind)!.Value, this.body.AddOperand(MirOperand.Copy(read)));
            this.body.AddOperand(value);
            this.Set(result, MirRvalue.Binary(op, operands, this.Proven(op, this.body.TypeOf(read)!, value)), node);
        }

        if (right is null && !postfix)
        {
            this.Use(destination, MirOperand.Copy(result), node);
        }

        if (plan.Second is { } setter)
        {
            this.Call(right is null ? this.Temp(BoundType.Unit, node) : destination, setter, node, null, setter.ArgumentOperations[plan.Evaluated].Source, MirOperand.Move(result));
        }
        else if (arithmetic is not null)
        {
            this.Overwrite(storage, MirOperand.Move(result), node);
        }

        if (right is not null && plan.Second is null)
        {
            this.Unit(destination, node);
        }

        CollectionsMarshal.SetCount(this.located, mark);
    }

    // SPEC 4.6.4, 4.6.5: a built-in range selection: the receiver located once and its range resolved against its length, a range written
    // directly at each boundary (omitted: 0 or the length; ^x: the length minus x; a closed end q: q + 1), else the ResolvedRange its key
    // is or its key's resolution (First) gives; checked once (0 <= start <= end <= length, else KIMI_E_INDEX_BOUNDS), then the Slice its row
    // serves (BuiltinForm.Slice: RawSlice) of the elements from the address of element start, any address for none (SPEC 5.6).
    private void Slice(MirPlace destination, IndexKoto index)
    {
        var (sequence, range) = (this.Place(index.Left), HirTables.Op(index.Right) == HirOp.Range ? index.Right as RangeKoto : null);
        var length = this.Length(sequence, index);
        var zero = MirOperand.Const(this.body.Const(new(MirConstKind.Integer, BoundType.ISize)));
        MirPlace start, last;
        if (range is not null)
        {
            start = range.Start is null ? this.Position(zero, index) : this.Boundary(range.Start, length);
            last = range.End is null ? length : this.Boundary(range.End, length);
        }
        else
        {
            var resolved = this.Follow(ExpansionPlan.Of(index)?.First is { } key ? this.Resolve(key, index, sequence) : this.Place(index.Right), 16);
            (start, last) = (this.Position(MirOperand.Copy(this.Bound(resolved, "start")), index), this.Position(MirOperand.Copy(this.Bound(resolved, "end")), index));
        }

        if (!Completes(index))
        {
            return;
        }

        var (test, count, fail, closed) = (this.Temp(BoundType.Boolean, index), this.Temp(BoundType.ISize, index), this.body.AddBlock(), range?.IsInclusive == true);
        this.Compare(test, MirBinaryOp.Le, zero, MirOperand.Copy(start), fail, index);
        this.Compare(test, MirBinaryOp.Le, MirOperand.Copy(start), MirOperand.Copy(last), fail, index);
        this.Compare(test, closed ? MirBinaryOp.Lt : MirBinaryOp.Le, MirOperand.Copy(last), MirOperand.Copy(length), fail, index);
        this.Fail(fail, "KIMI_E_INDEX_BOUNDS", index);
        var operands = this.body.AddOperand(MirOperand.Copy(last));
        this.body.AddOperand(MirOperand.Copy(start));
        this.Set(count, MirRvalue.Binary(MirBinaryOp.Sub, operands, true), index);
        if (closed)
        {
            operands = this.body.AddOperand(MirOperand.Copy(count));
            this.body.AddOperand(MirOperand.Const(this.body.Const(new(MirConstKind.Integer, BoundType.ISize, 1))));
            this.Set(count, MirRvalue.Binary(MirBinaryOp.Add, operands, true), index);
        }

        var element = TypeOf(index).Components[0];
        var pointer = this.Temp(compilation.Types.Pointer(element), index);
        var (some, empty, join) = (this.body.AddBlock(), this.body.AddBlock(), this.body.AddBlock());
        this.body.Terminate(this.current, MirTerminator.SwitchInt(MirOperand.Copy(count), this.body.AddCase(0, empty), 1, some, this.Info(index)));
        this.body.Push(some, MirStatement.Assign(pointer, this.Ref(this.body.Project(sequence, MirProjectionKind.Index, start.Root), this.body.TypeOf(pointer)!, false), this.Info(index)));
        this.body.Push(empty, MirStatement.Assign(pointer, MirRvalue.Use(this.body.AddOperand(MirOperand.Const(this.body.Const(new(MirConstKind.Integer, this.body.TypeOf(pointer)!))))), this.Info(index)));
        this.body.Terminate(some, MirTerminator.Goto(join, this.Info(index)));
        this.body.Terminate(empty, MirTerminator.Goto(join, this.Info(index)));
        var at = this.arguments.Count;
        this.current = join;
        this.arguments.Add(MirOperand.Move(pointer));
        this.arguments.Add(MirOperand.Copy(count));
        this.Serve(destination, CompilerFunctionKind.RawSlice, null, [element], at, index);
    }

    // A selection's position (SPEC 4.6.2, 4.6.9): an integer as the isize it reads; a written ^x, which resolves only for 0 <= x <= L, the
    // length minus x. An offset below 0 fails as an out-of-bounds position (KIMI_E_INDEX_BOUNDS, SPEC 22.5.4), a literal one in range proving
    // it, so the subtraction cannot overflow; an offset past the length gives a negative position, which the selection's bounds check rejects.
    private MirPlace Boundary(Koto node, MirPlace length)
    {
        if (node is not FromEndIndexKoto { Operand: var offset } || HirTables.Op(node) != HirOp.Range)
        {
            return this.Position(this.Operand(node), node);
        }

        var operand = this.Operand(offset);
        var (x, zero) = (this.Position(operand, offset), MirOperand.Const(this.body.Const(new(MirConstKind.Integer, BoundType.ISize))));
        if (Completes(node) && (operand.Kind != MirOperandKind.Const || this.body.Consts[operand.Arg].Bits < 0 || this.body.Consts[operand.Arg].Bits > int.MaxValue))
        {
            var (test, fail) = (this.Temp(BoundType.Boolean, node), this.body.AddBlock());
            this.Compare(test, MirBinaryOp.Le, zero, MirOperand.Copy(x), fail, node);
            this.Fail(fail, "KIMI_E_INDEX_BOUNDS", node);
        }

        var (position, operands) = (this.Temp(BoundType.ISize, node), this.body.AddOperand(MirOperand.Copy(length)));
        this.body.AddOperand(MirOperand.Copy(x));
        this.Set(position, MirRvalue.Binary(MirBinaryOp.Sub, operands, true), node);
        return position;
    }

    // SPEC 4.6.4: a key resolved against the length of the selection's receiver by its resolution call (First), whose Evaluated argument is
    // that length: the receiver is not evaluated again.
    private MirPlace Resolve(CallPlan key, IndexKoto index, MirPlace sequence)
    {
        var (length, result, evaluated) = (this.Length(sequence, index), this.Temp(key.ReturnType, index), ExpansionPlan.Of(index)!.Value.Evaluated);
        this.Call(result, key, index, null, evaluated < 0 ? null : key.ArgumentOperations[evaluated].Source, MirOperand.Copy(length));
        return result;
    }

    private MirPlace Length(MirPlace sequence, Koto node)
    {
        var length = this.Temp(BoundType.ISize, node);
        this.Set(length, MirRvalue.Len(sequence), node);
        return length;
    }

    // A boundary Field of a ResolvedRange.
    private MirPlace Bound(MirPlace range, string name) => this.body.Project(range, MirProjectionKind.Field, AdtDef.IndexOf(this.body.TypeOf(range)!, name));

    // A check's comparison into a bool: building goes on where it holds, else at the failure.
    private void Compare(MirPlace test, MirBinaryOp op, in MirOperand left, in MirOperand right, int fail, Koto node)
    {
        var operands = this.body.AddOperand(left);
        this.body.AddOperand(right);
        this.Set(test, MirRvalue.Binary(op, operands, true), node);
        this.Pass(MirOperand.Copy(test), 1, fail, node);
    }

    // utf8-formatting 5.2, 5.3: the writes of a formatting plan in order (CallPlan.Of), each through an exclusive borrow of its adapter, a failed
    // one leaving them: a $tryWrite's adapter is its writer operand, acquired once and Reborrowed, and a failure goes to its result, the writer's
    // status. An owning interpolation's is the internal adapter (its second call) over an exclusive borrow of the private buffer FormatCapacity
    // gives of the capacity its instance estimates from the literal bytes and the values' Types (5.1), on the stack where it is passed directly to
    // writeLine and its instance bounds it within the limit (6.1); each write follows FormatHint of the later literal bytes and values' Types, a
    // failure Aborts with KIMI_E_FORMAT, and FormatFinish ends the adapter and takes the buffer's bytes as the string, which leaves the buffer
    // empty for its cleanup. The three intrinsics are temporary (owner, 2026-10-11) until R8 writes the internal adapter in Kimigayo.
    private void Format(MirPlace destination, Koto node)
    {
        if (HirTables.Meaning(node) is null)
        {
            this.Opaque(node, destination); // Binding rejected a write.
            return;
        }

        var (mark, start, done, first) = (this.located.Count, this.formatTypes.Count, this.body.AddBlock(), node is InterpolatedStringKoto ? 2 : 0);
        var (storage, bytes, writer, unit) = (CallPlan.Of(node, first)!.ArgumentOperations[0].Source!, 0L, default(MirPlace), default(MirPlace));
        if (first == 0)
        {
            var operand = ((InvocationKoto)((MacroKoto)node).Operand).ArgumentNodes[0];
            this.located.Add((storage, this.Secure(this.Operand(operand), operand).Place));
            if (!Completes(operand))
            {
                this.body.Terminate(this.current, MirTerminator.Goto(done, this.Info(node))); // No writer is acquired (SPEC 15.6.7).
                this.current = done;
                CollectionsMarshal.SetCount(this.located, mark);
                return;
            }
        }
        else
        {
            for (var i = first; CallPlan.Of(node, i) is { } write; i++)
            {
                if (Bytes(write) is var count and >= 0)
                {
                    bytes += count;
                }
                else
                {
                    this.formatTypes.Add(write.TypeArguments[0]!);
                }
            }

            var (buffer, adapter) = (this.Temp(CallPlan.Of(node)!.ReturnType, node), CallPlan.Of(node, 1)!);
            this.Estimate(buffer, IntrinsicId.FormatCapacity, bytes, start, this.arguments.Count, node);
            (writer, unit) = (this.Temp(adapter.ReturnType, node), this.Temp(BoundType.Unit, node));
            this.Call(writer, adapter, node, null, adapter.ArgumentOperations[0].Source, this.Borrow(SemanticsKind.Uniq, buffer, node));
        }

        var (later, values, k) = (bytes, start, first);
        for (; CallPlan.Of(node, k) is { } write && (first > 0 || CallPlan.Of(node, k + 1) is not null); k++)
        {
            if (first > 0)
            {
                var (count, at) = (Bytes(write), this.arguments.Count);
                (later, values) = count < 0 ? (later, values + 1) : (later - count, values);
                this.arguments.Add(this.Borrow(SemanticsKind.Uniq, writer, node));
                this.Estimate(unit, IntrinsicId.FormatHint, later, values, at, node);
            }

            var (result, discriminant) = (this.Temp(write.ReturnType, node), this.Temp(BoundType.ISize, node));
            this.Call(result, write, node, null, first > 0 ? storage : null, first > 0 ? this.Borrow(SemanticsKind.Uniq, writer, node) : default);
            if (Completes(write.ArgumentOperations[1].Source!))
            {
                this.body.Push(this.current, MirStatement.Assign(discriminant, MirRvalue.Discriminant(result), this.Info(node)));
                this.Pass(MirOperand.Copy(discriminant), 0, done, node);
            }
        }

        if (first == 0)
        {
            this.body.Terminate(this.current, MirTerminator.Goto(done, this.Info(node)));
            this.current = done;
            this.Call(destination, CallPlan.Of(node, k)!, node);
        }
        else
        {
            this.Fail(done, "KIMI_E_FORMAT", node);
            if (Completes(node))
            {
                this.arguments.Add(MirOperand.Move(writer));
                this.Estimate(destination, IntrinsicId.FormatFinish, bytes, start, this.arguments.Count - 1, node);
            }
        }

        CollectionsMarshal.SetCount(this.located, mark);
        CollectionsMarshal.SetCount(this.formatTypes, start);
    }

    // An owning interpolation's intrinsic on the operands pushed since at, then its estimate (utf8-formatting 5.1, 6.1): the literal bytes and a
    // token of the Tuple of the values' Types from a position on, whose bounds its instance sums, and for the buffer and the end whether it is
    // passed directly to writeLine.
    private void Estimate(MirPlace destination, IntrinsicId id, long bytes, int from, int at, Koto node)
    {
        this.arguments.Add(MirOperand.Const(this.body.Const(new(MirConstKind.Integer, BoundType.ISize, bytes))));
        this.arguments.Add(MirOperand.Const(this.body.Const(new(MirConstKind.TypeToken, compilation.Types.Tuple(CollectionsMarshal.AsSpan(this.formatTypes)[from..])))));
        if (id != IntrinsicId.FormatHint)
        {
            this.arguments.Add(MirOperand.Const(this.body.Const(new(MirConstKind.Bool, BoundType.Boolean, ExpansionPlan.Of(node)!.Value.Direct ? 1 : 0))));
        }

        this.Invoke(destination, MirCallee.Intrinsic(id), at, node);
    }

    // SPEC 4.7.4, 12.3.2: a dynamic Array literal: its elements evaluated in order, then the empty Array, an aggregate of no operand,
    // reserved for their count and each appended (BuiltinForm.ArrayLiteral, R3 decision 5).
    private void Collection(MirPlace destination, ArrayLiteralKoto array, BoundType type)
    {
        var start = this.arguments.Count;
        foreach (var element in array.Elements)
        {
            this.arguments.Add(this.Operand(element, type.Components[0]));
        }

        var count = this.arguments.Count - start;
        this.Set(destination, MirRvalue.Aggregate(MirAggregateKind.FixedArray, this.body.Operands.Length, 0, type), array);
        if (count > 0 && Completes(array))
        {
            var unit = this.Temp(BoundType.Unit, array);
            for (var i = -1; i < count; i++)
            {
                var at = this.arguments.Count;
                this.arguments.Add(this.Borrow(SemanticsKind.Uniq, destination, array));
                this.arguments.Add(i < 0 ? MirOperand.Const(this.body.Const(new(MirConstKind.Integer, BoundType.ISize, count))) : this.arguments[start + i]);
                this.Serve(unit, i < 0 ? CompilerFunctionKind.ArrayReserve : CompilerFunctionKind.ArrayAppend, type, [], at, array);
            }
        }

        this.arguments.RemoveRange(start, count);
    }

    // SPEC 12.3.4: a dictionary literal: the empty Dictionary, then each entry in source order: its key evaluated once and retained, looked
    // up among the entries inserted (its plan's findEntry call), a duplicate failing with KIMI_E_DUPLICATE_KEY before the value is evaluated,
    // then the value evaluated and the pair placed (the placeEntry body, over the handle dictionaryStorage gives).
    private void Dictionary(MirPlace destination, DictionaryLiteralKoto dictionary)
    {
        if (CallPlan.Of(dictionary) is not { } find)
        {
            this.Opaque(dictionary, destination); // Binding rejected it.
            return;
        }

        var type = TypeOf(dictionary);
        var layout = compilation.Library.Function(CompilerFunctionKind.StorageDictionaryLayout)!.Type!;
        ReadOnlySpan<BoundType?> slots = [type.Components[0], type.Components[1]];
        this.Set(destination, MirRvalue.Aggregate(MirAggregateKind.FixedArray, this.body.Operands.Length, 0, type), dictionary);
        foreach (var entry in dictionary.Entries)
        {
            var (key, value, link, test, fail, start) = (this.Temp(type.Components[0], entry.Key), this.Temp(type.Components[1], entry.Value), this.Temp(BoundType.ISize, entry.Key), this.Temp(BoundType.Boolean, entry.Key), this.body.AddBlock(), this.arguments.Count);
            this.Assign(key, entry.Key);
            this.arguments.Add(this.Borrow(SemanticsKind.Ref, destination, entry.Key));
            this.arguments.Add(this.Borrow(SemanticsKind.Ref, key, entry.Key));
            this.Invoke(link, new(MirCalleeKind.Fn, find.Target, compilation.Types.SubstOf(find)), start, entry.Key);
            this.Compare(test, MirBinaryOp.Eq, MirOperand.Copy(link), MirOperand.Const(this.body.Const(new(MirConstKind.Integer, BoundType.ISize))), fail, entry.Key);
            this.Fail(fail, "KIMI_E_DUPLICATE_KEY", entry.Key);
            this.Assign(value, entry.Value);
            var handle = this.Temp(layout, entry.Value);
            this.arguments.Add(this.Borrow(SemanticsKind.Uniq, destination, entry.Value));
            this.Serve(handle, CompilerFunctionKind.StorageDictionaryLayout, null, slots, start, entry.Value);
            this.arguments.Add(MirOperand.Copy(this.body.Project(handle, MirProjectionKind.Field, 0)));
            this.arguments.Add(MirOperand.Move(key));
            this.arguments.Add(MirOperand.Move(value));
            this.Serve(this.Temp(BoundType.Unit, entry.Value), CompilerFunctionKind.StoragePlaceDictionaryEntry, null, slots, start, entry.Value);
        }
    }
}
