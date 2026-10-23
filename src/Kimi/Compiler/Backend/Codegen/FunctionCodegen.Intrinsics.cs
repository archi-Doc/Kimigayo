// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The intrinsic rows of FunctionCodegen (R5 plans C8a and C5c, package C 3.2; DECISIONS R5-M92, R5-M108): a call of an intrinsic by its
// row's writer (IntrinsicRows' writer column), over the closed IntrinsicWriter vocabulary: a routine row (Routine: a runtime routine through
// the one call writer, Invoke, with its slot-less values; Raw.release, $abort, tryAllocateBytes, Raw.allocate with its element stride),
// which a compiler function's Value call and adapter entry also call by its disposition (Routine by kind), a Fail's
// Abort reason, Raw.initialize's copy, Raw.slice's record, transferBytes' memcpy and a staticEnsure's state test
// (FunctionCodegen.Statics). Each takes its operands at its declaration's slots (ArgumentPosition) and ends through Finish; a row not
// written yet is one located Unsupported_Kd at the call, its owning unit named in IntrinsicRows. In a caller-location body a location is
// C3b's row (Location).
internal sealed partial class FunctionCodegen
{
    private readonly Value[] slotless = new Value[1]; // A routine row's slot-less values (Routine).

    private void Intrinsic(in MirTerminator call, in IntrinsicRow row)
    {
        if (this.Routine(row, this.InstanceType(call.Place), out var given) is { } routine)
        {
            this.Invoke(call, routine, new(call.Callee.Symbol, MirBodyKind.Fn), given);
            return;
        }

        switch (row.Writer)
        {
            case IntrinsicWriter.Fail:
                this.Fail(call, row.Routine!);
                return;
            case IntrinsicWriter.Initialize:
                this.Initialize(call);
                return;
            case IntrinsicWriter.Slice:
                this.Slice(call);
                return;
            case IntrinsicWriter.Copy:
                this.CopyBytes(call);
                return;
            case IntrinsicWriter.Ensure:
                this.Ensure(call);
                return;
            case IntrinsicWriter.Unwritten:
                this.Report(DiagnosticCode.Unsupported_Kd, call.Source); // Its row's unit (IntrinsicRows).
                return;
            default:
                throw new InvalidOperationException($"The intrinsic writer {row.Writer} has no code generation row.");
        }
    }

    // SPEC 7.7.4 (DECISIONS R5-M108): a compiler function's routine row by its disposition, what its Value call and its adapter's entry call
    // (FunctionCodegen.Closures' Runs): an intrinsic's routine row (below), a Body disposition's legacy routine (LegacyRoutine); null for
    // any other row, which is not one routine call or not written yet (a Shim's, an instruction writer's, an unwritten row's).
    private FunctionAbi? Routine(CompilerFunctionKind kind, BoundType? result, out ReadOnlySpan<Value> given)
    {
        given = default;
        return IntrinsicDisposition.Of(kind) switch
        {
            { Kind: DispositionKind.Intrinsic, Id: var id } => this.Routine(IntrinsicRows.Of(id), result, out given),
            { Kind: DispositionKind.Body } => LegacyRoutine(kind),
            _ => null,
        };
    }

    // A routine row, a call that is one runtime routine: its routine, after its module request (TryAllocate's), and its slot-less values in
    // order into `given`, from the call's result Type in the item's instance. Raw.allocate<T>(count) (SPEC 5.6) is __kimi_raw_allocate, which
    // checks the count and the byte size and gives a nonnull aligned address for zero bytes, with T's stride; no Layout alignment exceeds the
    // heap's 16 (DECISIONS R5-M92), so no T is refused. Null for any other writer. A result without its element's layout is a broken
    // contract, recorded by Laid (its stride 0, in a module then not written).
    private FunctionAbi? Routine(IntrinsicRow row, BoundType? result, out ReadOnlySpan<Value> given)
    {
        given = default;
        switch (row.Writer)
        {
            case IntrinsicWriter.Routine:
                return row.Routine;
            case IntrinsicWriter.TryAllocate:
                this.module.Request(ModuleCodegen.Needs.TryAllocate);
                return row.Routine;
            case IntrinsicWriter.Allocate:
                this.slotless[0] = new('\0', this.Laid(result is { Components: [var element] } ? element : null)?.Layout.Stride ?? 0);
                given = this.slotless.AsSpan(0, 1);
                return row.Routine;
            default:
                return null;
        }
    }

    // SPEC 17.4, 22.5.2: a Fail Aborts through the runtime Abort with its fixed code's reason (AbortReasons.Of; its one operand, the code,
    // names a reason or the MIR is broken) and no OS error (RuntimeAbi.NoOsError), the routine's slot-less parameters, at the call's
    // location; it has no successor.
    private void Fail(in MirTerminator call, FunctionAbi routine)
    {
        var code = this.body.Operands[call.Start];
        if (code.Kind != MirOperandKind.Const || this.body.Consts[code.Arg].Value is not string text || AbortReasons.Of(text) is not (>= 0 and var reason))
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, call.Source);
            return;
        }

        this.Invoke(call, routine, new(call.Callee.Symbol, MirBodyKind.Fn), [new('\0', reason), new('\0', RuntimeAbi.NoOsError)]);
    }

    // SPEC 5.6: Raw.initialize(storage, value) moves the value into the storage, which holds none, so nothing is destroyed: the value put
    // at the storage's address in its Type's representation (R5-M87's copy form); nothing for a zero-sized one.
    private void Initialize(in MirTerminator call)
    {
        var key = new MirDefKey(call.Callee.Symbol, MirBodyKind.Fn);
        var storage = this.body.Operands[call.Start + ArgumentPosition(key, 0)];
        var operand = this.body.Operands[call.Start + ArgumentPosition(key, 1)];
        if (this.LoweringOf(operand) is not { } lowering)
        {
            return; // A broken contract, recorded.
        }

        if (lowering.Layout.Size != 0)
        {
            if (!this.Operand(storage, out var address) || !this.Operand(operand, out var value))
            {
                this.Report(DiagnosticCode.Unsupported_Kd, call.Source);
                return;
            }

            var type = operand.Kind == MirOperandKind.Const
                ? this.collector.TypeOf(this.item, this.body.Consts[operand.Arg].Type)
                : this.InstanceType(operand.Place);
            this.Put(address, type!, lowering, value);
        }

        this.Finish(call, null);
    }

    // SPEC 5.6: Raw.slice(storage, length) forms the Slice record {buffer, length} in the destination, at its Fields 0 and 1 (Layout's
    // offsets, as Length reads Field 1), reading nothing; the backing, Unit, has no value (DECISIONS R4-M40).
    private void Slice(in MirTerminator call)
    {
        var key = new MirDefKey(call.Callee.Symbol, MirBodyKind.Fn);
        var storage = this.body.Operands[call.Start + ArgumentPosition(key, 0)];
        var length = this.body.Operands[call.Start + ArgumentPosition(key, 1)];
        if (!this.Address(call.Place, out var record) || !this.Operand(storage, out var buffer) || !this.Operand(length, out var count))
        {
            this.Report(DiagnosticCode.Unsupported_Kd, call.Source);
            return;
        }

        if (this.LoweringOf(call.Place) is not { } slice || this.LoweringOf(storage) is not { } pointer || this.LoweringOf(length) is not { } index)
        {
            return; // A broken contract, recorded.
        }

        var offsets = slice.Layout.FieldOffsets.Span;
        this.StoreAt(this.Offset(record, offsets[0]), pointer.Layout, buffer);
        this.StoreAt(this.Offset(record, offsets[1]), index.Layout, count);
        this.Finish(call, null);
    }

    // SPEC 22.2: transferBytes(destination, source, count) copies bytes whose ranges do not overlap (its contract), so llvm.memcpy
    // (DECISIONS R5-M18); it reports nothing.
    private void CopyBytes(in MirTerminator call)
    {
        var key = new MirDefKey(call.Callee.Symbol, MirBodyKind.Fn);
        if (!this.Operand(this.body.Operands[call.Start + ArgumentPosition(key, 0)], out var destination) ||
            !this.Operand(this.body.Operands[call.Start + ArgumentPosition(key, 1)], out var source) ||
            !this.Operand(this.body.Operands[call.Start + ArgumentPosition(key, 2)], out var count))
        {
            this.Report(DiagnosticCode.Unsupported_Kd, call.Source);
            return;
        }

        this.module.Request(ModuleCodegen.Needs.Memory);
        var output = this.output;
        output.Write("  call void @llvm.memcpy.p0.p0.i64(ptr ");
        this.WriteValue(destination);
        output.Write(", ptr ");
        this.WriteValue(source);
        output.Write(", i64 ");
        this.WriteValue(count);
        output.Write(", i1 false)\n");
        this.Finish(call, null);
    }
}
