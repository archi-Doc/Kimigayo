// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using System.Text;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Expansions (plan 5.3): the forms whose calls ExpansionPlan and CallPlan.Of name and whose other steps the intrinsic table serves: for
// loops, compound assignments and the Dictionary literal (keyed and range selections: MirBuilder.Selections.cs; an Array literal is an
// aggregate, MirBuilder.Values.cs). Each evaluates an operand once; a later step reads the Place it was located at (Located) or the value
// it gave (a call's given argument).
internal sealed partial class MirBuilder
{
    private readonly List<(Koto Node, MirPlace Place)> located = []; // The Places the expansions being built located, innermost last.

    // SPEC 14.6.2: the Subject acquired by the entry call into the iterator once, then each next() result decomposed as its plan's Some/None
    // match: Some binds the item for the body, None completes the loop with Unit. The body end and a continue clean the iteration up and call
    // next() again; an exit or a transfer out cleans it up, then the iterator and the Subject. SPEC 4.6.4: a ResolvedRange loop counts
    // its isize positions from start to end instead, checked, without an iterator value.
    private void For(MirPlace destination, ForKoto loop)
    {
        if (BoundMatch.Of(loop) is not { } plan)
        {
            this.Count(destination, loop);
            return;
        }

        var entry = CallPlan.Of(loop)!;
        var step = CallPlan.Of(loop, 1)!;
        var iterator = this.Hold(entry.ReturnType, loop);
        this.Call(iterator, entry, loop, first: null);
        this.locals[HirTables.Symbol(step.Receiver!)!] = this.Made(iterator).Root;
        var head = this.body.AddBlock();
        this.body.Terminate(this.current, MirTerminator.Goto(head, this.Info(loop)));
        var after = this.Open(loop, destination, head);
        var start = this.selected.Count;
        var none = this.body.AddBlock();
        var tail = this.body.AddBlock();
        this.current = head;
        var iteration = this.OpenScope(block: false);
        var subject = this.Hold(step.ReturnType, loop, MirLocalRole.Subject);
        this.Call(subject, step, loop, first: null);
        this.Made(subject);
        CollectionsMarshal.SetCount(this.selected, start + plan.Positions.Count);
        this.Pattern(plan, plan.Arms[0].Pattern, subject, start, none);
        this.Iteration(loop, head, plan, start, default);
        this.current = none;
        this.Pattern(plan, plan.Arms[1].Pattern, subject, start, tail);
        this.Unit(destination, loop);
        this.body.Terminate(this.current, MirTerminator.Goto(this.Exit(this.targets[^1].At.Depth, after), this.Info(loop)));
        this.body.Terminate(tail, MirTerminator.Unreachable(this.Info(loop)));
        CollectionsMarshal.SetCount(this.selected, start);
        this.CloseScope(iteration, clean: false);
        this.Close(after);
    }

    // SPEC 4.6.4: a ResolvedRange loop: the interval copied once, its position compared with its end before each iteration and advanced
    // after it (where a continue goes), checked.
    private void Count(MirPlace destination, ForKoto loop)
    {
        var type = TypeOf(loop.Iterable);
        var range = this.Hold(MirTypes.IsReference(type) ? type.Components[0] : type, loop.Iterable);
        this.Assign(range, loop.Iterable);
        this.Made(range);
        var position = this.Temp(BoundType.ISize, loop);
        var end = this.Temp(BoundType.ISize, loop);
        var test = this.Temp(BoundType.Boolean, loop);
        this.Use(position, MirOperand.Copy(this.Bound(range, "start")), loop);
        this.Use(end, MirOperand.Copy(this.Bound(range, "end")), loop);
        var head = this.body.AddBlock();
        var step = this.body.AddBlock();
        var done = this.body.AddBlock();
        this.body.Terminate(this.current, MirTerminator.Goto(head, this.Info(loop)));
        var after = this.Open(loop, destination, step);
        this.current = head;
        this.Compare(test, MirBinaryOp.Lt, MirOperand.Copy(position), MirOperand.Copy(end), done, loop);
        this.Iteration(loop, step, null, 0, position);
        this.current = step;
        var operands = this.body.AddOperand(MirOperand.Copy(position));
        this.body.AddOperand(MirOperand.Const(this.body.Const(new(MirConstKind.Integer, BoundType.ISize, 1))));
        this.Set(position, MirRvalue.Binary(MirBinaryOp.Add, operands, proven: false), loop);
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
        var bindings = this.OpenScope(block: true);
        if (plan is not null)
        {
            this.Bind(plan, plan.Arms[0].Pattern, start);
        }

        for (var i = 0; plan is null && i < loop.Bindings.Count; i++)
        {
            var name = loop.Bindings[i];
            var symbol = HirTables.Symbol(loop.Bindings[i])!;
            this.locals[symbol] = this.Live(
                this.body.AddLocal(
                    new(
                    BoundType.ISize,
                    MirLocalKind.Var,
                    this.Info(name, local: true),
                    Binding.Reassignable(symbol),
                    symbol.Name)),
                this.lexical);
            this.Use(new(this.locals[symbol]), MirOperand.Copy(position), name);
        }

        this.Iterate(loop.Body, next);
        this.CloseScope(bindings, clean: false);
        this.scope = outer;
    }

    // SPEC 13.7.2, 11.3: the right operand secured, then the target located once, its old value read, the result computed and written back
    // once. Reading and writing are selected independently: a Property's custom getter (First) and setter (Second, taking the result as its
    // Evaluated argument) are called on its receiver, located once, and a standard accessor reads or writes its storage, as a Place target is
    // read and written in place. The old value is read by a built-in operation, or by a user arithmetic call (CallPlan.Of) with the secured
    // operand, whose result replaces it. A destination written in place is reserved once located and read and written through that
    // reservation, which the write activates (writeback protection). A compound assignment gives Unit; an increment or decrement its old
    // value (postfix) or its result (prefix), SPEC 13.2, the Property not read again.
    private void Update(MirPlace destination, Koto node)
    {
        var (target, right) = node is BinaryKoto update ? (update.Left, update.Right) : (((UnaryKoto)node).Operand, null);
        var plan = ExpansionPlan.Of(node)!.Value;
        var arithmetic = CallPlan.Of(node);
        var mark = this.located.Count;
        var value = right is null ? default : this.Secure(this.Operand(right, expected: null), right);
        // A Property's accessors take its receiver, located once; a static Property's have none. Binding designates the Property through
        // the target's parentheses (a parenthesized Place is its operand's, SPEC 3.4).
        var accessor = plan.First ?? plan.Second;
        var property = KotoHelper.UnwrapParentheses(target);
        var storage = accessor is null ? this.Place(target) : default;
        var receiver = accessor is null || HirTables.ResOf(property) == HirRes.Static ? null : (property as MemberAccessKoto)?.Left;
        if (receiver is not null)
        {
            this.located.Add((receiver, this.Place(receiver)));
        }

        if (accessor is not null && (plan.First is null || plan.Second is null))
        {
            storage = this.Storage(HirTables.Symbol(property)!, receiver is null ? null : this.located[mark].Place, property);
        }

        if (plan.Second is null)
        {
            storage = this.Writeback(storage, node);
        }

        var read = plan.First is null ? storage : this.Hold(plan.First.ReturnType, target);
        if (right is null)
        {
            value = MirOperand.Const(this.body.Const(new(MirConstKind.Integer, this.body.TypeOf(read)!, 1))); // One of the old value's Type.
        }

        if (plan.First is { } getter)
        {
            // A receiver both accessors take is taken by each as the HIR publishes: the setter by the column's Adjust, the getter by the
            // receiver's plan-row Adjust where the two differ (SPEC 11.3: shared), else by the column's (DECISIONS R4-M109).
            var given = plan.Second is null ? null : receiver;
            var operand = default(MirOperand);
            foreach (ref readonly var argument in getter.ArgumentOperations)
            {
                if (given is not null && ReferenceEquals(argument.Source, given))
                {
                    var adjust = HirTables.PlanOf(given).Adjust;
                    operand = this.Operand(
                        given, adjust == default ? HirTables.AdjustOf(given) : adjust, argument.AdaptedType ?? argument.ParameterType, input: true);
                }
            }

            this.Call(read, getter, target, null, given, operand);
            this.Made(read);
        }

        var postfix = node.Akind is KotoKind.PostfixIncrement or KotoKind.PostfixDecrement;
        if (postfix)
        {
            this.Use(destination, MirOperand.Copy(read), node);
        }

        var result = plan.Second is null && arithmetic is null ? storage : this.Hold(arithmetic?.ReturnType ?? this.body.TypeOf(read)!, node);
        if (arithmetic is not null)
        {
            this.located.Add((target, read));
            this.Call(result, arithmetic, node, null, right, value);
        }
        else
        {
            KotoKind kind;
            if (right is null)
            {
                kind = node.Akind is KotoKind.PrefixPlusPlus or KotoKind.PostfixIncrement ? KotoKind.Plus : KotoKind.Minus;
            }
            else
            {
                kind = KotoHelper.CompoundOperation(node.Akind);
            }

            var op = BinaryOp(kind)!.Value;
            var operands = this.body.AddOperand(MirOperand.Copy(read));
            this.body.AddOperand(value);
            this.Set(result, MirRvalue.Binary(op, operands, this.Proven(op, this.body.TypeOf(read)!, value)), node);
        }

        if (result != storage)
        {
            this.Made(result);
        }

        if (right is null && !postfix)
        {
            this.Use(destination, MirOperand.Copy(result), node);
        }

        if (plan.Second is { } setter)
        {
            var unit = right is null ? this.Hold(BoundType.Unit, node) : destination;
            this.Call(unit, setter, node, null, setter.ArgumentOperations[plan.Evaluated].Source, MirOperand.Move(result));
            if (unit != destination)
            {
                this.Made(unit);
            }
        }
        else if (arithmetic is not null)
        {
            this.Overwrite(storage, result, node);
        }

        if (right is not null && plan.Second is null)
        {
            this.Unit(destination, node);
        }

        CollectionsMarshal.SetCount(this.located, mark);
    }

    // SPEC 13.7.2: an update's located destination reserved by a two-phase exclusive borrow, made where it is located, and the Place
    // through it, which the update reads and writes; the write activates it.
    private MirPlace Writeback(MirPlace destination, Koto node)
    {
        var reference = compilation.Types.Reference(SemanticsKind.Uniq, this.body.TypeOf(destination) ?? BoundType.Unit);
        var reservation = this.Temp(reference, node);
        this.Set(reservation, this.Ref(destination, reference, input: true), node);
        return this.body.Project(reservation, MirProjectionKind.Deref, 0);
    }

    // A check's comparison into a bool: building goes on where it holds, else at the failure.
    private void Compare(MirPlace test, MirBinaryOp op, in MirOperand left, in MirOperand right, int fail, Koto node)
    {
        var operands = this.body.AddOperand(left);
        this.body.AddOperand(right);
        this.Set(test, MirRvalue.Binary(op, operands, proven: true), node);
        this.Pass(MirOperand.Copy(test), 1, fail, node);
    }

    // SPEC 12.4.3: a dictionary literal: the empty Dictionary, then each entry in source order: its key evaluated once and retained, looked
    // up among the entries inserted (its plan's findEntry call), a duplicate failing with KIMI_E_DUPLICATE_KEY before the value is evaluated,
    // then the value evaluated and the pair placed (the placeEntry body, over the handle dictionaryStorage gives), every step of the entry's
    // insertion located at its key, where a failure inside it reports (SPEC 22.5.1; DECISIONS R5-M144). The empty Dictionary is a
    // part made before the entries, in a temp of its own that an interrupted entry's exit destroys, and moved to the destination after the
    // last entry, where the literal's value is created (SPEC 3.6, 16.2.1; DECISIONS R4-M129).
    private void Dictionary(MirPlace destination, DictionaryLiteralKoto dictionary)
    {
        var find = CallPlan.Of(dictionary)!;
        var type = TypeOf(dictionary);
        var layout = compilation.Types.Tuple([compilation.Types.Pointer(type), BoundType.ISize]);
        ReadOnlySpan<BoundType?> slots = [type.Components[0], type.Components[1]];
        var built = this.Temp(type, dictionary);
        this.Set(built, MirRvalue.Aggregate(MirAggregateKind.FixedArray, this.body.Operands.Length, 0, type), dictionary);
        foreach (var entry in dictionary.Entries)
        {
            var key = this.Hold(type.Components[0], entry.Key);
            var value = this.Hold(type.Components[1], entry.Value);
            var link = this.Hold(BoundType.ISize, entry.Key);
            var test = this.Hold(BoundType.Boolean, entry.Key);
            var fail = this.body.AddBlock();
            var start = this.arguments.Count;
            this.Assign(key, entry.Key);
            this.Made(key);
            this.arguments.Add(this.Borrow(SemanticsKind.Ref, built, entry.Key));
            this.arguments.Add(this.Borrow(SemanticsKind.Ref, key, entry.Key));
            this.Invoke(link, new(MirCalleeKind.Fn, find.Target, compilation.Types.SubstOf(find)), start, entry.Key);
            this.Made(link);
            this.Compare(
                test,
                MirBinaryOp.Eq,
                MirOperand.Copy(link),
                MirOperand.Const(this.body.Const(new(MirConstKind.Integer, BoundType.ISize))),
                fail,
                entry.Key);
            this.Made(test);
            this.Fail(fail, "KIMI_E_DUPLICATE_KEY", entry.Key);
            this.Assign(value, entry.Value);
            this.Made(value);
            var handle = this.Hold(layout, entry.Key);
            this.arguments.Add(this.Borrow(SemanticsKind.Uniq, built, entry.Key));
            this.Serve(handle, CompilerFunctionKind.StorageDictionaryLayout, null, slots, start, entry.Key);
            this.Made(handle);
            this.arguments.Add(MirOperand.Copy(this.body.Project(handle, MirProjectionKind.Field, 0)));
            this.arguments.Add(MirOperand.Move(key));
            this.arguments.Add(MirOperand.Move(value));
            this.Serve(this.Temp(BoundType.Unit, entry.Key), CompilerFunctionKind.StoragePlaceDictionaryEntry, null, slots, start, entry.Key);
        }

        this.Use(destination, MirOperand.Move(built), dictionary);
    }
}
