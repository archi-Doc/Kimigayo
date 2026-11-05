// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The intrinsic rows of FunctionCodegen (R5 plans C8a, C5c, C6a, C9b and C9c, package C 3.2; DECISIONS R5-M92, R5-M108, R5-M110, R5-M130, R5-M131): a call of
// an intrinsic by its row's writer (IntrinsicRows' writer column), over the closed IntrinsicWriter vocabulary: a routine row (Routine: a runtime routine
// through the one call writer, Invoke, with its slot-less values; Raw.release, $abort, tryAllocateBytes and transferBytes, whose routines the module
// defines on request (Requested; DECISIONS R5-M145), Raw.allocate with its element stride, a creation's factory with its control value and a clone's
// retain, FunctionCodegen.Objects, Text.writer's routine with its destination's writer kind and dispatch word,
// FunctionCodegen.Formatting), which a compiler function's Value call and adapter entry also call by its disposition (Routine by kind), a Fail's Abort reason,
// Raw.initialize's copy, Raw.slice's record, a staticEnsure's state test (FunctionCodegen.Statics), a runtime Type test
// (FunctionCodegen.Objects), an owning interpolation's buffer, segment steps and end (FunctionCodegen.Interpolation) and a verification's
// compared value, which the test runtime holds for its observation (Record; DECISIONS R5-M136). Each takes its operands at its
// declaration's slots (ArgumentPosition) and ends through Finish; a call whose row's Target column names an item carries the collector's target, read once by
// the cursor (I8) and given to the row's writer; a call of a row no MIR producer makes (Unproduced) is a broken contract at the call
// (DECISIONS R5-M146). In a caller-location body a location is C3b's row (Location).
internal sealed partial class FunctionCodegen
{
    private readonly Value[] slotless = new Value[2]; // A routine row's slot-less values (Routine): a formatting row's kind, Text.writer's two.

    private void Intrinsic(in MirTerminator call, in IntrinsicRow row)
    {
        var target = row.Target == IntrinsicTarget.None ? -1 : this.Target(new(this.at, this.body.Blocks[this.at].Count));
        if (this.Routine(row, this.InstanceType(call.Place), call.Callee.Subst, target, out var given) is { } routine)
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
            case IntrinsicWriter.Ensure:
                this.Ensure(call, target);
                return;
            case IntrinsicWriter.Test:
                this.Test(call);
                return;
            case IntrinsicWriter.Capacity:
                this.Capacity(call);
                return;
            case IntrinsicWriter.Buffer:
                this.FormatBuffer(call, row);
                return;
            case IntrinsicWriter.Segment:
                this.FormatSegment(call, row);
                return;
            case IntrinsicWriter.Finish:
                this.FormatFinish(call, row);
                return;
            case IntrinsicWriter.Record:
                this.Record(call, row.Routine!);
                return;
            case IntrinsicWriter.Create or IntrinsicWriter.Clone or IntrinsicWriter.Writer:
                return; // A broken contract, recorded by Routine.
            case IntrinsicWriter.Unproduced:
                this.Report(DiagnosticCode.InternalInvariant_Kd, call.Source); // No MIR producer makes its call (IntrinsicRows).
                return;
            default:
                throw new InvalidOperationException($"The intrinsic writer {row.Writer} has no code generation row.");
        }
    }

    // SPEC 7.7.4 (DECISIONS R5-M108, R5-M116): a compiler function's routine row by its disposition, what its Item's adapter entry calls
    // (FunctionCodegen.Closures' Runs) with the target it carries, and its Item's Value call: an intrinsic's routine row (below) with the
    // Item's Type argument (its own substitution's, TyCtxt.SubstOfItem), a Body disposition's legacy routine (LegacyRuntimeHelpers.Row); null
    // for any other row, which is not one routine call (a Shim's, an instruction writer's, an Unproduced row's), and after a record at the
    // source.
    private FunctionAbi? Routine(BoundType item, BoundType? result, int target, SourceInfo source, out ReadOnlySpan<Value> given)
    {
        given = default;
        var kind = item.Symbol?.CompilerFunction ?? CompilerFunctionKind.None;
        return IntrinsicDisposition.Of(kind) switch
        {
            { Kind: DispositionKind.Intrinsic, Id: var id } =>
                this.Routine(IntrinsicRows.Of(id), result, this.typeContext.SubstOfItem(item), target, out given),
            _ => LegacyRuntimeHelpers.Row(kind) is { } row ? this.Routine(row, target, source, out given) : null,
        };
    }

    // A legacy row's routine (DECISIONS R5-M116, R5-M121, R5-M124, R5-M128), the one path its direct call, its Item's Value call and its
    // adapter's entry take, its slot-less values into `given`: a Routine row's (writeLine); a Formatting row's, given its witness target's kind
    // (FunctionCodegen.Formatting); a row's in its function's instance (LegacyRow.InInstance), its call site's LegacyRow item's
    // (ModuleCodegen.Row: the item's Array helper or its row's capacity routine, given the element's stride or a fixed array's length, or
    // placeEntry's Place helper, which takes no given value); null for any other row, and after a record at the source (such a row's call
    // targets its item, K 4.5 row 2).
    private FunctionAbi? Routine(LegacyRow row, int target, SourceInfo source, out ReadOnlySpan<Value> given)
    {
        given = default;
        if (!row.InInstance)
        {
            return row.Writer switch
            {
                LegacyWriter.Routine => row.Routine,
                LegacyWriter.Formatting => this.Formatting(row, target, source, out given),
                _ => null,
            };
        }

        if (target < 0 || this.collector.Set.Items[target].Key.Kind != MonoKind.LegacyRow)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, source); // The collector targets an Instance row's call at its LegacyRow item.
            return null;
        }

        var routine = this.module.Row(target, out var number);
        this.slotless[0] = new('\0', number);
        given = this.slotless.AsSpan(0, 1);
        return routine;
    }

    // A routine row, a call that is one runtime routine: its routine, after its module request (Requested's), and its slot-less values in
    // order into `given`, from the call's result Type, its Type argument in the item's instance where the row reads one (TypeArgument, of
    // the call's substitution or an Item's own) and the target it carries (-1: none). Raw.allocate<T>(count) (SPEC 5.6) is
    // __kimi_raw_allocate, which checks the count and the byte size and gives a nonnull aligned address for zero bytes, with T's stride; no
    // Layout alignment exceeds the heap's 16 (DECISIONS R5-M92), so no T is refused. A creation is its target descriptor's factory and a
    // clone its handle's retain (FunctionCodegen.Objects). Null for any other writer, and after a broken contract's record. A T without its
    // layout (the row's TypeArguments need, Needed) is a broken contract, recorded by Laid (its stride 0, in a module then not written).
    private FunctionAbi? Routine(IntrinsicRow row, BoundType? result, Subst? subst, int target, out ReadOnlySpan<Value> given)
    {
        given = default;
        var argument = this.TypeArgument(row, subst);
        switch (row.Writer)
        {
            case IntrinsicWriter.Routine:
                return row.Routine;
            case IntrinsicWriter.Requested:
                this.module.Request(ModuleCodegen.Needs.StorageBytes | ModuleCodegen.Needs.Memory);
                return row.Routine;
            case IntrinsicWriter.Allocate:
                this.slotless[0] = new('\0', this.Needed(subst!, 0, out _)?.Layout.Stride ?? 0); // Its row's TypeArguments need.
                given = this.slotless.AsSpan(0, 1);
                return row.Routine;
            case IntrinsicWriter.Create:
                return this.Create(result, target, out given);
            case IntrinsicWriter.Clone:
                return this.Retain(result);
            case IntrinsicWriter.Writer:
                return this.Writer(row, argument, target, out given);
            default:
                return null;
        }
    }

    // K package 4.5 rows 6-9 (DECISIONS R5-M74, R5-M136): an intrinsic's Type argument in the item's instance, the one operand form its rows
    // read, as the collector reads it for the row's target (InstanceCollector.Intrinsic): a call's substitution's or an Item's own
    // (SubstOfItem), read only where the row reads one (IntrinsicRow.ReadsTypeArgument), whose function has it; null for another row.
    private BoundType? TypeArgument(in IntrinsicRow row, Subst? subst)
        => row.ReadsTypeArgument && subst!.Types[0] is { } first ? this.collector.TypeOf(this.item, first) : null;

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

    // SPEC 17.6.3 (PD22; DECISIONS R5-M136): a verification's compared Scalar, which the builder records before its comparison (F8b), is held
    // by the test runtime until its observation numbers the failure: its side (operand 0), its Type's frame kind (TestRuntime.ValueKind) and
    // width (1 for bool, else its size in bits) and its bits as i128, a float's reinterpreted as an integer, then zero-extended. The value is
    // a Scalar: MirValidator's testRecordValue row admits no other known Type (the builder never records the Never value it admits).
    private void Record(in MirTerminator call, FunctionAbi routine)
    {
        var key = new MirDefKey(call.Callee.Symbol, MirBodyKind.Fn);
        var operand = this.body.Operands[call.Start + ArgumentPosition(key, 1)];
        if (this.LoweringOf(operand) is not { } lowering)
        {
            return; // A broken contract, recorded (Laid).
        }

        var type = this.InstanceType(operand)!;
        if (!this.Operand(operand, out var value))
        {
            return; // A broken contract, recorded.
        }

        var bits = this.Narrow(lowering, value);
        var width = lowering.ComputationType == "i1" ? 1 : 8 * lowering.Layout.Size;
        var integer = lowering.ComputationType;
        if (type.IsFloatingPoint)
        {
            integer = width == 32 ? "i32" : "i64";
            bits = this.Convert("bitcast ", lowering.ComputationType, bits, integer);
        }

        bits = this.Convert("zext ", integer, bits, "i128");
        this.Invoke(call, routine, key, [new('\0', Testing.TestRuntime.ValueKind(type)), new('\0', width), bits]);
    }

    // SPEC 5.6: Raw.initialize(storage, value) moves the value into the storage, which holds none, so nothing is destroyed: the value put
    // at the storage's address in its Type's representation (R5-M87's copy form; a string constant written whole, DECISIONS R5-M118);
    // nothing for a zero-sized one.
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
            if (!this.Operand(storage, out var address) || !this.Put(address, this.InstanceType(operand)!, lowering, operand))
            {
                return; // A broken contract, recorded.
            }
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
            return; // A broken contract, recorded.
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
}
