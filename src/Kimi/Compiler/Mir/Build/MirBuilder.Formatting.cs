// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using System.Text;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Formatting's ordered writes, adapter lifetime and hints (SPEC 22.10, 22.10.5), and a direct line's region (SPEC 22.7, rule L).
internal sealed partial class MirBuilder
{
    private readonly List<BoundType> formatTypes = []; // The values' Types of the owning interpolations being built, innermost last.

    // The literal bytes of a write position (SPEC 22.10.5): its text's, the string constant the one literal rule makes (Literal), where it
    // writes text (Binding.WritesText: a literal segment, or an embedded string literal); -1 at a value's, whose bound its instance decides.
    private int Bytes(CallPlan write)
    {
        if (!Binding.WritesText(write))
        {
            return -1;
        }

        var text = this.Literal(write.ArgumentOperations[1].Source!); // Made before the constants are read.
        return Encoding.UTF8.GetByteCount((string)this.body.Consts[text.Arg].Value!);
    }

    // SPEC 22.10.3, 22.10.4: the writes of a formatting plan in order (CallPlan.Of), each through an exclusive borrow of its adapter, a failed
    // one leaving them: a $tryWrite's adapter is its writer operand, acquired once and Reborrowed, its status tested before a first segment
    // that is an embedded expression, and a failure goes to its result, the writer's status; a writer of Type Never is the Never value, which
    // the status and each write take as it is (Acquire), so they and the embedded expressions are built in their order where only the checks
    // continue (SPEC 14.10.3; DECISIONS R4-M62). An owning interpolation's is the internal adapter (its second call, Adapt) over an exclusive
    // borrow of the private buffer (its first call, Heap), its writes Writes' positions, and its end Finish: the adapter's finish, then the
    // moved buffer's string.
    private void Format(MirPlace destination, Koto node)
    {
        var mark = this.located.Count;
        var start = this.formatTypes.Count;
        var done = -1;
        if (HirTables.PlanOf(node).Formatting!.Acquisition is null)
        {
            var bytes = this.Values(node);
            var buffer = this.Hold(CallPlan.Of(node)!.ReturnType, node);
            this.Heap(node, buffer);
            this.Made(buffer);
            var writer = this.Hold(CallPlan.Of(node, 1)!.ReturnType, node);
            this.Adapt(node, buffer, writer);
            this.Made(writer);
            this.Writes(node, writer, bytes, start, ref done);
            this.Finish(destination, writer, buffer, this.Done(ref done), node);
            this.Fail(done, node);
        }
        else
        {
            var storage = CallPlan.Of(node, 0)!.ArgumentOperations[0].Source!;
            var operand = ((InvocationKoto)((MacroKoto)node).Operand).ArgumentNodes[0];
            var input = CallPlan.Of(HirTables.PlanOf(node).Formatting!.Acquisition!)!.ArgumentOperations[0]; // The writer's, a Reborrow (BU-27).
            var secured = this.Secure(this.Operand(operand, expected: input.AdaptedType ?? input.ParameterType), operand).Place;
            this.located.Add((storage, secured));
            if (!Binding.WritesText(CallPlan.Of(node, 0)!))
            {
                // SPEC 22.10.4: an embedded expression is evaluated only after a test that the adapter is healthy, the previous write's Ok
                // result or, before the first segment, the adapter's status (the plan's last call); a text's write returns the recorded
                // failure itself (SPEC 22.10.1).
                this.Tested(CallPlan.Of(node, HirTables.PlanOf(node).Formatting!.Writes.Count)!, this.Done(ref done), node, storage: null, default);
            }

            var last = 0;
            for (; CallPlan.Of(node, last + 1) is not null; last++)
            {
                this.Tested(CallPlan.Of(node, last)!, this.Done(ref done), node, storage: null, default); // Its writes; the last call, its status, left.
            }

            this.body.Terminate(this.current, MirTerminator.Goto(this.Done(ref done), this.Info(node)));
            this.current = done;
            this.Call(destination, CallPlan.Of(node, last)!, node, first: null);
        }

        CollectionsMarshal.SetCount(this.located, mark);
        CollectionsMarshal.SetCount(this.formatTypes, start);
    }

    // SPEC 22.7, 22.10.3, 22.10.5 and rule L (R8-W2 design §4): Console.writeLine of a direct interpolation (ExpansionPlan.Direct), its writes
    // built once over one erased writer, whose destination the interpolation-bound term L (TyCtxt.InterpolationBound) of the literal bytes
    // and the values' Types decides: a zero-filled region of exactly L bytes and its FixedBuffer where L > 0, else the owning buffer. A
    // constant term builds its one form. A term an instance decides makes both buffers on every path, the FixedBuffer over the region
    // (Text.fixed<0>'s, of no bytes, at an owning instance) and the owning buffer (Heap, no allocation), then branches on L's value (its
    // Length constant, SPEC 4.4.1), each arm making only the writer (Binding's plan writer Type) over its buffer,
    // and branches again after the last write: the fixed arm's writer is dead there, so its Loan on the buffer ends before text() views the
    // bytes for writeLine(Utf8Slice) (a literal is deferred only before a value, so none is pending), and the owning arm's end (Finish)
    // moves the owning buffer, its string to writeLine(ref/string). text()'s Err, which no run reaches (every byte is a literal or a
    // built-in formatter's committed encoding, so text() validates nothing), goes to the failed writes' KIMI_E_FORMAT block. Each arm's
    // temporaries end in it; the buffers and the writer live across both branches, so each end reads a buffer initialized on every path
    // (DECISIONS R8-M24 as amended at R8-W2 c3b), and the owning buffer's drop on the fixed path, a flagged one, releases nothing.
    private void Line(MirPlace destination, CallPlan call, Koto node, Koto text)
    {
        var mark = this.located.Count;
        var start = this.formatTypes.Count;
        var done = -1;
        var bytes = this.Values(text);
        var length = compilation.Types.InterpolationBound(compilation.Types.Tuple(CollectionsMarshal.AsSpan(this.formatTypes)[start..]), bytes);
        var fixedArm = !length.IsConstant || length.Value > 0;
        var owningArm = !length.IsConstant || length.Value == 0;
        var region = fixedArm ? this.Region(length, text) : default;
        var fixedBuffer = fixedArm ? this.Hold(this.FixedCall(length).Result, text) : default;
        var buffer = owningArm ? this.Hold(CallPlan.Of(text)!.ReturnType, text) : default;
        var writer = this.Hold(CallPlan.Of(text, 1)!.ReturnType, text);
        var test = fixedArm && owningArm ? this.Positive(length, text) : default(MirPlace?);
        if (fixedArm)
        {
            this.Fixed(region, fixedBuffer, length, text);
            this.Made(fixedBuffer);
        }

        if (owningArm)
        {
            this.Heap(text, buffer);
            this.Made(buffer);
        }

        var (owning, join) = this.Fork(test, text);
        if (fixedArm)
        {
            this.Erase(fixedBuffer, writer, text);
        }

        this.Join(owning, join, text);
        if (owningArm)
        {
            var scope = this.OpenScope(block: false);
            this.Adapt(text, buffer, writer);
            this.CloseScope(scope);
        }

        this.Join(-1, join, text);
        this.Made(writer);

        this.Writes(text, writer, bytes, start, ref done);
        (owning, var after) = this.Fork(test, text);
        if (fixedArm)
        {
            this.View(destination, node, fixedBuffer, this.Done(ref done));
        }

        this.Join(owning, after, text);
        if (owningArm)
        {
            var scope = this.OpenScope(block: false);
            var line = this.Temp(BoundType.String, text);
            this.Finish(line, writer, buffer, this.Done(ref done), text);
            this.Call(destination, call, node, first: null, text, this.Borrow(SemanticsKind.Ref, line, text));
            this.CloseScope(scope);
        }

        this.Join(-1, after, text);
        this.Fail(done, text); // A tested write's failure, or text()'s.
        CollectionsMarshal.SetCount(this.located, mark);
        CollectionsMarshal.SetCount(this.formatTypes, start);
    }

    // A direct line's region of the term's bytes (Line), zero-filled.
    private MirPlace Region(BoundLength length, Koto node)
    {
        var u8 = BoundType.Primitives["u8"];
        var type = compilation.Types.Intern(
            BoundTypeKind.FixedArray,
            null,
            SemanticsKind.Owner,
            [u8],
            length.IsConstant ? length.Value : 0,
            lengthExpression: length.IsConstant ? null : length);
        var region = this.Temp(type, node);
        var fill = this.body.AddOperand(MirOperand.Const(this.body.Const(new(MirConstKind.Integer, u8))));
        this.Set(region, MirRvalue.Aggregate(MirAggregateKind.Fill, fill, 1, type), node);
        return region;
    }

    // Whether a direct line's region has bytes (Line): the term's value (a Length constant) > 0, the test an instance decides.
    private MirPlace Positive(BoundLength length, Koto node)
    {
        var size = this.Temp(BoundType.ISize, node);
        this.Set(size, MirRvalue.Use(this.body.AddOperand(this.LengthOperand(length))), node);
        var operands = this.body.AddOperand(MirOperand.Copy(size));
        this.body.AddOperand(MirOperand.Const(this.body.Const(new(MirConstKind.Integer, BoundType.ISize))));
        var test = this.Temp(BoundType.Boolean, node);
        this.Set(test, MirRvalue.Binary(MirBinaryOp.Gt, operands, proven: true), node);
        return test;
    }

    // A direct line's FixedBuffer (Line): Text.fixed over an exclusive borrow of the region, of the term's length, its temporaries ending
    // here.
    private void Fixed(MirPlace region, MirPlace fixedBuffer, BoundLength length, Koto node)
    {
        var scope = this.OpenScope(block: false);
        var (make, subst, _) = this.FixedCall(length);
        var at = this.arguments.Count;
        this.arguments.Add(this.Borrow(SemanticsKind.Uniq, region, node));
        this.Invoke(fixedBuffer, new(MirCalleeKind.Fn, make, subst), at, node);
        this.CloseScope(scope);
    }

    // A direct line's fixed arm (Line): the writer over an exclusive borrow of the FixedBuffer, its temporaries ending in the arm.
    private void Erase(MirPlace fixedBuffer, MirPlace writer, Koto node)
    {
        var scope = this.OpenScope(block: false);
        var at = this.arguments.Count;
        this.arguments.Add(this.Borrow(SemanticsKind.Uniq, fixedBuffer, node));
        var make = compilation.Library.GetSymbol(KimiDeclarationId.TextWriter)!; // A body since R8-W2 b2-1; its FixedBuffer specialization runs.
        this.Invoke(writer, new(MirCalleeKind.Fn, make, compilation.Types.Synthesized(make, null, [this.body.TypeOf(fixedBuffer)], [])), at, node);
        this.CloseScope(scope);
    }

    // A direct line's Text.fixed of the term's length (Line), whose result Type is its FixedBuffer's (interned, so each read is the same).
    private (BindingSymbol Make, Subst Subst, BoundType Result) FixedCall(BoundLength length)
    {
        var make = compilation.Library.GetSymbol(KimiDeclarationId.TextFixed)!;
        var subst = compilation.Types.Synthesized(make, null, [null], [length]);
        return (make, subst, TyCtxt.Signature(make, subst, -1, out _)!);
    }

    // A direct line's fixed end (Line), after the writer's last use: text() over a shared borrow of the FixedBuffer, its Ok view to
    // writeLine(Utf8Slice) at the call, its Err to the failed writes' block, its temporaries ending in the arm.
    private void View(MirPlace destination, Koto node, MirPlace fixedBuffer, int done)
    {
        var scope = this.OpenScope(block: false);
        var view = compilation.Library.GetSymbol(KimiDeclarationId.FixedBufferText)!;
        var subst = compilation.Types.Synthesized(view, this.body.TypeOf(fixedBuffer), [], []);
        var result = this.Temp(TyCtxt.Signature(view, subst, -1, out _)!, node);
        var at = this.arguments.Count;
        this.arguments.Add(this.Borrow(SemanticsKind.Ref, fixedBuffer, node));
        this.Invoke(result, new(MirCalleeKind.Fn, view, subst), at, node);
        var discriminant = this.Temp(BoundType.ISize, node);
        this.Set(discriminant, MirRvalue.Discriminant(result), node);
        this.Pass(MirOperand.Copy(discriminant), 0, done, node);
        at = this.arguments.Count;
        this.arguments.Add(MirOperand.Copy(this.body.Project(this.body.Project(result, MirProjectionKind.Downcast, 0), MirProjectionKind.Field, 0)));
        var line = compilation.Library.GetSymbol(KimiDeclarationId.WriteLineUtf8)!; // A body since R8-W1c-3.
        this.Invoke(destination, new(MirCalleeKind.Fn, line, compilation.Types.Synthesized(line, null, [], [])), at, node);
        this.CloseScope(scope);
    }

    // A direct line's branch on its test (Line): the fixed arm goes on here, the owning arm's block and the join made; none without a test.
    private (int Owning, int Join) Fork(MirPlace? test, Koto node)
    {
        if (test is not { } tested)
        {
            return (-1, -1);
        }

        var owning = this.body.AddBlock();
        var join = this.body.AddBlock();
        var arm = this.body.AddBlock();
        this.body.Terminate(this.current, MirTerminator.SwitchInt(MirOperand.Copy(tested), this.body.AddCase(0, owning), 1, arm, this.Info(node)));
        this.current = arm;
        return (owning, join);
    }

    // A direct line's arm ending at its join (Fork), building going on at next: the owning arm's block, or the join (-1); none without one.
    private void Join(int next, int join, Koto node)
    {
        if (join >= 0)
        {
            this.body.Terminate(this.current, MirTerminator.Goto(join, this.Info(node)));
            this.current = next >= 0 ? next : join;
        }
    }

    // An owning interpolation's literal bytes, its values' Types added to formatTypes (SPEC 22.10.5).
    private long Values(Koto node)
    {
        var bytes = 0L;
        for (var i = 2; CallPlan.Of(node, i) is { } write; i++)
        {
            if (this.Bytes(write) is var count and >= 0)
            {
                bytes += count;
            }
            else
            {
                this.formatTypes.Add(write.TypeArguments[0]!);
            }
        }

        return bytes;
    }

    // An owning interpolation's private buffer (its first call, Text.heap) of capacity 0, the constant at the plan's Capacity argument: it
    // allocates nothing, and the adapter's first reservation grows it once by its bytes, a deferred literal and its hint (SPEC 22.10.5).
    private void Heap(Koto node, MirPlace buffer)
    {
        var heap = CallPlan.Of(node)!;
        var empty = MirOperand.Const(this.body.Const(new(MirConstKind.Integer, BoundType.ISize)));
        this.Call(buffer, heap, node, null, heap.ArgumentOperations[0].Source, empty);
    }

    // An owning interpolation's internal adapter (its second call) into the writer over an exclusive borrow of its buffer.
    private void Adapt(Koto node, MirPlace buffer, MirPlace writer)
    {
        var adapter = CallPlan.Of(node, 1)!;
        this.Call(writer, adapter, node, null, adapter.ArgumentOperations[0].Source, this.Borrow(SemanticsKind.Uniq, buffer, node));
    }

    // An owning interpolation's write positions (SPEC 22.10.3), each after the adapter's hint (SPEC 22.10.5; R3-10): Utf8Writer.hint's
    // internal set, the Field's store (by name, AdtDef.IndexOf) of the term of the later literal bytes and values' Types
    // (TyCtxt.InterpolationHint) in its one form (LengthOperand: an Integer where it folds, else a Length its instance decides); then the
    // position's write, a failure going to done: a value's is its plan's write; a literal segment's is no write (SPEC 22.10.3 passes only
    // embedded expressions to write; R5-M133) but its text through the library (Text), and so is an embedded string literal's, as if
    // written (a constant has no effect, and string's built-in formatting writes its bytes with the same failure).
    private void Writes(Koto node, MirPlace writer, long bytes, int start, ref int done)
    {
        var storage = CallPlan.Of(node, 2)!.ArgumentOperations[0].Source!;
        var hint = this.body.Project(writer, MirProjectionKind.Field, AdtDef.IndexOf(this.body.TypeOf(writer)!, "hint"));
        var (later, values) = (bytes, start);
        for (var k = 2; CallPlan.Of(node, k) is { } write; k++)
        {
            var count = this.Bytes(write);
            (later, values) = count < 0 ? (later, values + 1) : (later - count, values);
            var term = compilation.Types.InterpolationHint(compilation.Types.Tuple(CollectionsMarshal.AsSpan(this.formatTypes)[values..]), later);
            this.Use(hint, this.LengthOperand(term), node);
            if (count < 0)
            {
                this.Tested(write, this.Done(ref done), node, storage, writer);
            }
            else
            {
                this.Text(write, writer, values < this.formatTypes.Count, this.Done(ref done), node);
            }
        }
    }

    // A literal position's text through the library (SPEC 22.10.3, 22.10.5; DECISIONS R8-M7): before a later value of the template (the
    // plan's fact: a value Type left in formatTypes), Utf8Writer.deferLiteral, which keeps the static bytes for the adapter's next reservation
    // (one allocation for a first unbounded value); else string's built-in format (StringWitness.format), which writes them. The text is
    // the literal's constant record (MirConst.IsRecord), which no Place holds, at a ref/string parameter that keeps nothing of it
    // (MirValidator.ConstantRecords): no temporary, Loan or destruction. The call's Result is tested as a write's.
    private void Text(CallPlan write, MirPlace writer, bool deferred, int done, Koto node)
    {
        var target = compilation.Library.GetSymbol(deferred ? KimiDeclarationId.WriterDeferLiteral : KimiDeclarationId.StringWitnessFormat)!;
        var subst = compilation.Types.Synthesized(target, deferred ? this.body.TypeOf(writer) : null, [], []);
        var result = this.Hold(TyCtxt.Signature(target, subst, -1, out _)!, node);
        var discriminant = this.Hold(BoundType.ISize, node);
        var record = this.Viewed(this.Literal(write.ArgumentOperations[1].Source!), BoundType.String, node);
        var adapter = this.Borrow(SemanticsKind.Uniq, writer, node);
        var at = this.arguments.Count;
        this.arguments.Add(deferred ? adapter : record); // deferLiteral(self, text); format(self: ref/string, writer).
        this.arguments.Add(deferred ? record : adapter);
        this.Invoke(result, new(MirCalleeKind.Fn, target, subst), at, node);
        this.Set(discriminant, MirRvalue.Discriminant(this.Made(result)), node);
        this.Pass(MirOperand.Copy(this.Made(discriminant)), 0, done, node);
    }

    // An owning interpolation's failed writes' block, made by the first branch to it, failing with KIMI_E_FORMAT.
    private void Fail(int done, Koto node)
    {
        if (done >= 0)
        {
            this.Fail(done, "KIMI_E_FORMAT", node);
        }
    }

    // An owning interpolation's end (SPEC 22.10.3, 22.9.3): Utf8Writer.finish over an exclusive borrow of the writer, tested as a write (the
    // recorded failure, else a deferred literal that no reservation wrote, written and committed), is the writer's last use, so its Loan
    // on the buffer ends there; then HeapBuffer.intoString of the moved buffer, its Ok string moved into the destination, its Err, which no
    // run reaches (every byte is a literal or a built-in formatter's committed encoding, so it validates nothing), to done. Its
    // temporaries end here.
    private void Finish(MirPlace destination, MirPlace writer, MirPlace buffer, int done, Koto node)
    {
        var scope = this.OpenScope(block: false);
        var finish = compilation.Library.GetSymbol(KimiDeclarationId.WriterFinish)!;
        var subst = compilation.Types.Synthesized(finish, this.body.TypeOf(writer), [], []);
        var ended = this.Hold(TyCtxt.Signature(finish, subst, -1, out _)!, node);
        var at = this.arguments.Count;
        this.arguments.Add(this.Borrow(SemanticsKind.Uniq, writer, node));
        this.Invoke(ended, new(MirCalleeKind.Fn, finish, subst), at, node);
        var discriminant = this.Temp(BoundType.ISize, node);
        this.Set(discriminant, MirRvalue.Discriminant(this.Made(ended)), node);
        this.Pass(MirOperand.Copy(discriminant), 0, done, node);
        var take = compilation.Library.GetSymbol(KimiDeclarationId.HeapBufferIntoString)!;
        subst = compilation.Types.Synthesized(take, this.body.TypeOf(buffer), [], []);
        var result = this.Hold(TyCtxt.Signature(take, subst, -1, out _)!, node);
        at = this.arguments.Count;
        this.arguments.Add(MirOperand.Move(buffer));
        this.Invoke(result, new(MirCalleeKind.Fn, take, subst), at, node);
        this.Set(discriminant, MirRvalue.Discriminant(this.Made(result)), node);
        this.Pass(MirOperand.Copy(discriminant), 0, done, node);
        this.Use(destination, MirOperand.Move(this.body.Project(this.body.Project(result, MirProjectionKind.Downcast, 0), MirProjectionKind.Field, 0)), node);
        this.CloseScope(scope);
    }

    // The block a branch leaves to, made by the first branch.
    private int Done(ref int done) => done >= 0 ? done : done = this.body.AddBlock();

    // A write's or a $tryWrite's status call (SPEC 22.10.1, 22.10.4), its Result tested: Ok continues, a failure goes to done. An owning
    // interpolation's write takes its adapter, an exclusive borrow of the internal adapter at its storage's argument.
    private void Tested(CallPlan call, int done, Koto node, Koto? storage, MirPlace adapter)
    {
        var result = this.Hold(call.ReturnType, node);
        var discriminant = this.Hold(BoundType.ISize, node);
        this.Call(result, call, node, null, storage, storage is null ? default : this.Borrow(SemanticsKind.Uniq, adapter, node));
        this.Set(discriminant, MirRvalue.Discriminant(this.Made(result)), node);
        this.Pass(MirOperand.Copy(this.Made(discriminant)), 0, done, node);
    }
}
