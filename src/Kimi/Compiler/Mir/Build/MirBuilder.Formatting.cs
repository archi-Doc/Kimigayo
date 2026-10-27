// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using System.Text;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Formatting's ordered writes, adapter lifetime and capacity estimates (SPEC 22.10, 22.10.5).
internal sealed partial class MirBuilder
{
    private readonly List<BoundType> formatTypes = []; // The values' Types of the owning interpolations being built, innermost last.

    // The literal bytes of a write position (SPEC 22.10.5): its text's, the string constant Literal makes (Text), where it writes text
    // (Binding.WritesText); -1 at a value's, whose bound its instance decides.
    private int Bytes(CallPlan write, in MirOperand text)
        => Binding.WritesText(write) ? Encoding.UTF8.GetByteCount((string)this.body.Consts[text.Arg].Value!) : -1;

    // The literal text of a write position of an owning interpolation (SPEC 3.1, 22.10.3), its FormatSegment's operand: where it writes text
    // (a literal segment, or an embedded string literal), that literal's constant through the one literal rule (Literal); the empty text at
    // a value's position, whose write follows.
    private MirOperand Text(CallPlan write) => Binding.WritesText(write) ? this.Literal(write.ArgumentOperations[1].Source!)
        : MirOperand.Const(this.body.Const(new(MirConstKind.String, BoundType.String, Value: string.Empty)));

    // SPEC 22.10.3, 22.10.4: the writes of a formatting plan in order (CallPlan.Of), each through an exclusive borrow of its adapter, a failed
    // one leaving them: a $tryWrite's adapter is its writer operand, acquired once and Reborrowed, its status tested before a first segment
    // that is an embedded expression, and a failure goes to its result, the writer's status. An owning interpolation's is the internal adapter
    // (its second call) over an exclusive borrow of the private buffer FormatBuffer
    // gives of the capacity its instance estimates from the literal bytes and the values' Types (5.1), on the stack where it is passed directly to
    // writeLine and its instance bounds it within the limit (6.1); each write position is a FormatSegment step, the hint of the later literal
    // bytes and values' Types, then the position's literal text: a literal segment is its step's text and no write (SPEC 22.10.3 passes only
    // embedded expressions to write; R5-M133), and so is an embedded string literal, as if written (a constant has no effect, and string's
    // built-in formatting writes its bytes with the same failure); a value's position has the empty text, whose write follows, a failure
    // Aborting with KIMI_E_FORMAT; FormatFinish ends the adapter and takes the buffer's bytes as the string, which leaves the buffer empty for
    // its cleanup. The three intrinsics are temporary (owner, 2026-10-11) until R8 writes the internal adapter in Kimigayo. The block a
    // failed write (and a $tryWrite's end) goes to is made by the first branch to it, so an interpolation of text alone has none.
    private void Format(MirPlace destination, Koto node)
    {
        var mark = this.located.Count;
        var start = this.formatTypes.Count;
        var done = -1;
        var first = HirTables.PlanOf(node).Formatting!.Acquisition is null ? 2 : 0; // An interpolation's heap and adapter come first.
        var storage = CallPlan.Of(node, first)!.ArgumentOperations[0].Source!;
        var bytes = 0L;
        var writer = default(MirPlace);
        var unit = default(MirPlace);
        if (first == 0)
        {
            var operand = ((InvocationKoto)((MacroKoto)node).Operand).ArgumentNodes[0];
            var input = HirTables.PlanOf(node).Formatting!.Acquisition!.BoundCall!.ArgumentOperations[0]; // The writer's, a Reborrow (BU-27).
            var secured = this.Secure(this.Operand(operand, expected: input.AdaptedType ?? input.ParameterType), operand).Place;
            this.located.Add((storage, secured));
            if (this.IsNever(secured))
            {
                this.body.Terminate(this.current, MirTerminator.Goto(Done(), this.Info(node))); // The Never value has no writer to acquire.
                this.current = done;
                CollectionsMarshal.SetCount(this.located, mark);
                return;
            }

            if (!Binding.WritesText(CallPlan.Of(node, 0)!))
            {
                // SPEC 22.10.4: an embedded expression is evaluated only after a test that the adapter is healthy, the previous write's Ok
                // result or, before the first segment, the adapter's status (the plan's last call); a text's write returns the recorded
                // failure itself (SPEC 22.10.1).
                this.Tested(CallPlan.Of(node, HirTables.PlanOf(node).Formatting!.Writes.Count)!, Done(), node, storage: null, default);
            }
        }
        else
        {
            for (var i = first; CallPlan.Of(node, i) is { } write; i++)
            {
                if (this.Bytes(write, this.Text(write)) is var count and >= 0)
                {
                    bytes += count;
                }
                else
                {
                    this.formatTypes.Add(write.TypeArguments[0]!);
                }
            }

            var buffer = this.Temp(CallPlan.Of(node)!.ReturnType, node);
            var adapter = CallPlan.Of(node, 1)!;
            this.Estimate(buffer, IntrinsicId.FormatBuffer, bytes, start, this.arguments.Count, node);
            (writer, unit) = (this.Hold(adapter.ReturnType, node), this.Hold(BoundType.Unit, node));
            this.Call(writer, adapter, node, null, adapter.ArgumentOperations[0].Source, this.Borrow(SemanticsKind.Uniq, buffer, node));
            this.Made(writer);
        }

        var (later, values, k) = (bytes, start, first);
        for (; CallPlan.Of(node, k) is { } write && (first > 0 || CallPlan.Of(node, k + 1) is not null); k++)
        {
            if (first > 0)
            {
                var text = this.Text(write);
                var count = this.Bytes(write, text);
                var at = this.arguments.Count;
                (later, values) = count < 0 ? (later, values + 1) : (later - count, values);
                this.arguments.Add(this.Borrow(SemanticsKind.Uniq, writer, node));
                this.arguments.Add(text);
                this.Estimate(unit, IntrinsicId.FormatSegment, later, values, at, node);
                if (count >= 0)
                {
                    continue; // Text is its step's, no write.
                }
            }

            this.Tested(write, Done(), node, first > 0 ? storage : null, writer);
        }

        if (first == 0)
        {
            this.body.Terminate(this.current, MirTerminator.Goto(Done(), this.Info(node)));
            this.current = done;
            this.Call(destination, CallPlan.Of(node, k)!, node, first: null);
        }
        else
        {
            this.Made(unit); // The steps' Unit, written by each.
            if (done >= 0)
            {
                this.Fail(done, "KIMI_E_FORMAT", node); // A tested write's failure.
            }

            this.arguments.Add(MirOperand.Move(writer));
            this.Estimate(destination, IntrinsicId.FormatFinish, bytes, start, this.arguments.Count - 1, node);
        }

        CollectionsMarshal.SetCount(this.located, mark);
        CollectionsMarshal.SetCount(this.formatTypes, start);

        // The block a branch leaves to, made by the first branch.
        int Done() => done >= 0 ? done : done = this.body.AddBlock();
    }

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

    // An owning interpolation's intrinsic on the operands pushed since at, then its estimate (SPEC 22.10.5, 22.7): the literal bytes and a
    // token of the Tuple of the values' Types from a position on, whose bounds its instance sums, and for the buffer and the end whether it is
    // passed directly to writeLine.
    private void Estimate(MirPlace destination, IntrinsicId id, long bytes, int from, int at, Koto node)
    {
        this.arguments.Add(MirOperand.Const(this.body.Const(new(MirConstKind.Integer, BoundType.ISize, bytes))));
        this.arguments.Add(
            MirOperand.Const(
            this.body.Const(
            new(
            MirConstKind.TypeToken,
            compilation.Types.Tuple(CollectionsMarshal.AsSpan(this.formatTypes)[from..])))));
        if (id != IntrinsicId.FormatSegment)
        {
            this.arguments.Add(MirOperand.Const(this.body.Const(new(MirConstKind.Bool, BoundType.Boolean, ExpansionPlan.Of(node)!.Value.Direct ? 1 : 0))));
        }

        this.Invoke(destination, MirCallee.Intrinsic(id), at, node);
    }
}
