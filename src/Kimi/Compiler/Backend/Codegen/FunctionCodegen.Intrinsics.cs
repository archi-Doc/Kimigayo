// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The intrinsic rows of FunctionCodegen (R5 plan C8a, package C 3.2; DECISIONS R5-M92): a call of an intrinsic by its row's writer
// (IntrinsicRows' writer column), one switch over the closed IntrinsicWriter vocabulary: a runtime routine through the one call writer
// (Invoke; Raw.release, $abort, tryAllocateBytes), Raw.allocate's routine with its element stride, a Fail's Abort reason, Raw.initialize's
// copy, Raw.slice's record and transferBytes' memcpy. Each takes its operands at its declaration's slots (ArgumentPosition) and ends
// through Finish; a row not written yet is one located Unsupported_Kd at the call, its owning unit named in IntrinsicRows. In a
// caller-location body a location is C3b's row (Location).
internal sealed partial class FunctionCodegen
{
    private void Intrinsic(in MirTerminator call)
    {
        var row = IntrinsicRows.Of((IntrinsicId)call.Callee.Arg);
        switch (row.Writer)
        {
            case IntrinsicWriter.Routine:
                this.Invoke(call, row.Routine!, new(call.Callee.Symbol, MirBodyKind.Fn));
                return;
            case IntrinsicWriter.TryAllocate:
                this.module.Request(ModuleCodegen.Needs.TryAllocate);
                this.Invoke(call, row.Routine!, new(call.Callee.Symbol, MirBodyKind.Fn));
                return;
            case IntrinsicWriter.Allocate:
                this.Allocate(call, row.Routine!);
                return;
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
            case IntrinsicWriter.Unwritten:
                this.Report(DiagnosticCode.Unsupported_Kd, call.Source); // Its row's unit (IntrinsicRows).
                return;
            default:
                throw new InvalidOperationException($"The intrinsic writer {row.Writer} has no code generation row.");
        }
    }

    // SPEC 5.6: Raw.allocate<T>(count) through its routine (__kimi_raw_allocate), which checks the count and the byte size and gives a
    // nonnull aligned address for zero bytes: the count, T's stride in the item's instance, the call's location. No Layout alignment
    // exceeds the heap's 16 (DECISIONS R5-M92), so no T is refused.
    private void Allocate(in MirTerminator call, FunctionAbi routine)
    {
        var parameters = routine.Parameters;
        this.arguments = this.arguments.Length < parameters.Length ? new Value[parameters.Length] : this.arguments;
        if (this.InstanceType(call.Place) is not { Components: [var element] } || this.Laid(element) is not { } stored)
        {
            return; // A broken contract, recorded.
        }

        if (!this.Argument(call.Start + ArgumentPosition(new(call.Callee.Symbol, MirBodyKind.Fn), 0), parameters[0], out this.arguments[0]))
        {
            this.Report(DiagnosticCode.Unsupported_Kd, call.Source);
            return;
        }

        var location = this.Location(call.Source);
        if (location < 0)
        {
            return; // Recorded.
        }

        this.arguments[1] = new('\0', stored.Layout.Stride);
        this.arguments[2] = new('@', location);
        this.arguments[3] = new('\0', this.module.Constants[location].ByteLength);
        this.Finish(call, this.WriteCall(routine, this.arguments.AsSpan(0, parameters.Length)));
    }

    // SPEC 17.4, 22.5.2: a Fail Aborts with its fixed code's reason (AbortReasons.Of; its one operand, the code, names a reason or the
    // MIR is broken) at the call's location, with no OS error (RuntimeAbi.NoOsError); it has no successor.
    private void Fail(in MirTerminator call, FunctionAbi routine)
    {
        var code = this.body.Operands[call.Start];
        if (code.Kind != MirOperandKind.Const || this.body.Consts[code.Arg].Value is not string text || AbortReasons.Of(text) is not (>= 0 and var reason))
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, call.Source);
            return;
        }

        var location = this.Location(call.Source);
        if (location < 0)
        {
            return; // Recorded.
        }

        this.arguments = this.arguments.Length < routine.Parameters.Length ? new Value[routine.Parameters.Length] : this.arguments;
        this.arguments[0] = new('\0', reason);
        this.arguments[1] = new('@', location);
        this.arguments[2] = new('\0', this.module.Constants[location].ByteLength);
        this.arguments[3] = new('\0', RuntimeAbi.NoOsError);
        this.Finish(call, this.WriteCall(routine, this.arguments.AsSpan(0, routine.Parameters.Length)));
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
