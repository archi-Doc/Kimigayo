// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the Backend's row of each intrinsic (R5 package K 4.5, C 3.2; S2-S4 X15, X27; DECISIONS R5-M35), the one table the instance
// collector and code generation read by IntrinsicId. Column Target: the item a call of the intrinsic names as its collector target, the
// whitelist of target-bearing intrinsic sites (X15); every other intrinsic carries none. Column LaysOutType: the call's Type argument is a
// by-value use whose layout the collector checks (K 4.8; DECISIONS R5-M44). Columns Writer and Routine: how code generation writes a call
// (C8a; DECISIONS R5-M65, R5-M92), the runtime routine a routine writer calls; a row not written yet names its owning unit.
// Input: an IntrinsicId other than None. Output: its row.
// Invariants: one row per IntrinsicId, in its order, checked once; an id without a row, None included, is a broken MIR contract.
// Must not: resolve a target's item (the collector's), or restate the descriptor table's columns (MirDescriptors).

namespace Kimi.Compiler.Backend;

/// <summary>The item a call of an intrinsic names as its collector target (S2-S4 X15; K package 4.5 rows 6-9).</summary>
internal enum IntrinsicTarget : byte
{
    None,
    Witness, // The body of the user's writer witness (ErasedWriter, K6b).
    StaticInit, // The initializer body of the static (StaticEnsure, K7).
    Descriptor, // The object descriptor of the created payload (MakeObj, MakeRc, MakeArc; K8, DECISIONS R5-M74).
    Glue, // The drop glue of the element (DestroyValues, K7).
}

/// <summary>How code generation writes a call of an intrinsic (package C 3.2's writer column; DECISIONS R5-M92).</summary>
internal enum IntrinsicWriter : byte
{
    Unwritten, // A later unit's row (its comment names it): one located Unsupported_Kd at the call.
    Routine, // The row's runtime routine through the one call writer: the call's operands at its parameters, then the call's location.
    TryAllocate, // Routine's, the routine defined in the module on request (LlvmModuleText.TryAllocate).
    Allocate, // Raw.allocate: the count, the element's stride and the location to the row's routine.
    Fail, // A fixed SPEC 22.5.2 code's Abort reason and the location to the row's routine (the runtime Abort).
    Initialize, // Raw.initialize: the value put at the storage.
    Slice, // Raw.slice: the Slice record of the storage and the length.
    Copy, // transferBytes: llvm.memcpy of the byte count.
    Ensure, // staticEnsure: its slot's state tested inline, the slot's ensure function off the Initialized path (FunctionCodegen.Statics).
    Create, // makeObj, makeRc, makeArc: Routine's, the factory of the call's target descriptor with its control value (FunctionCodegen.Objects).
    Clone, // clone: Routine's, the runtime's retain of the result handle's counting (FunctionCodegen.Objects).
    Test, // A runtime Type test: its target's number in the module through the runtime's test, or false (FunctionCodegen.Objects).
    Capacity, // capacity: the legacy handle's capacity word through P2b's kernel (FunctionCodegen.Arrays; DECISIONS R5-M121).
    Writer, // Text.writer: P1d's TextWriter routine, given its destination's writer kind and its reserve witness (FunctionCodegen.Formatting).
    Buffer, // FormatBuffer: an owning interpolation's private buffer, on its frame's region or the heap by its estimate (FunctionCodegen.Interpolation).
    Hint, // FormatHint: the routine given the hint its estimate of the later writes gives (FunctionCodegen.Interpolation).
    Finish, // FormatFinish: the routine, or its stack form, over the adapter's buffer (FunctionCodegen.Interpolation).
}

/// <summary>A row of the intrinsic table.</summary>
/// <param name="Id">The intrinsic, the row's index plus 1.</param>
/// <param name="Target">The item a call names as its collector target.</param>
/// <param name="LaysOutType">Whether the call's Type argument is a by-value use (K package 4.8).</param>
/// <param name="Writer">How code generation writes a call.</param>
/// <param name="Routine">The runtime routine the writer calls, if any.</param>
internal readonly record struct IntrinsicRow(IntrinsicId Id, IntrinsicTarget Target, bool LaysOutType, IntrinsicWriter Writer, FunctionAbi? Routine);

/// <summary>The intrinsic table of the Backend: one row per <see cref="IntrinsicId"/>.</summary>
internal static class IntrinsicRows
{
    // One row per IntrinsicId but None, in the enum's order (Complete).
    private static readonly IntrinsicRow[] Rows = Complete(
    [
        new(IntrinsicId.RawAllocate, IntrinsicTarget.None, LaysOutType: true, IntrinsicWriter.Allocate, RuntimeAbi.RawAllocate),
        new(IntrinsicId.RawRelease, IntrinsicTarget.None, LaysOutType: false, IntrinsicWriter.Routine, RuntimeAbi.StorageRelease),
        new(IntrinsicId.RawInitialize, IntrinsicTarget.None, LaysOutType: false, IntrinsicWriter.Initialize, null),
        new(IntrinsicId.RawSlice, IntrinsicTarget.None, LaysOutType: false, IntrinsicWriter.Slice, null),
        new(IntrinsicId.TryAllocateBytes, IntrinsicTarget.None, LaysOutType: false, IntrinsicWriter.TryAllocate, RuntimeAbi.TryAllocateBytes),
        new(IntrinsicId.MoveBytes, IntrinsicTarget.None, LaysOutType: false, IntrinsicWriter.Copy, null),
        new(IntrinsicId.Stride, IntrinsicTarget.None, LaysOutType: true, IntrinsicWriter.Unwritten, null), // No MIR producer yet (an open PLAN row).
        new(IntrinsicId.DestroyValues, IntrinsicTarget.Glue, LaysOutType: true, IntrinsicWriter.Unwritten, null), // No MIR producer yet (PLAN G198).
        new(IntrinsicId.ReinterpretLoan, IntrinsicTarget.None, LaysOutType: false, IntrinsicWriter.Unwritten, null), // No MIR producer yet (an open PLAN row).
        new(IntrinsicId.Abort, IntrinsicTarget.None, LaysOutType: false, IntrinsicWriter.Routine, RuntimeAbi.AbortMessage),
        new(IntrinsicId.Fail, IntrinsicTarget.None, LaysOutType: false, IntrinsicWriter.Fail, RuntimeAbi.Abort),
        new(IntrinsicId.WriteStdout, IntrinsicTarget.None, LaysOutType: false, IntrinsicWriter.Unwritten, null), // No MIR producer (R8, PD21).
        new(IntrinsicId.FormatFloat, IntrinsicTarget.None, LaysOutType: false, IntrinsicWriter.Unwritten, null), // No MIR producer (R8, PD21).
        new(
            IntrinsicId.ErasedWriter,
            IntrinsicTarget.Witness,
            LaysOutType: false,
            IntrinsicWriter.Writer,
            LegacyRuntimeHelpers.FormattingRow(CompilerFunctionKind.TextWriter)!.Abi),
        new(IntrinsicId.ErasedReserve, IntrinsicTarget.None, LaysOutType: false, IntrinsicWriter.Unwritten, null), // No MIR producer (R8, PD21).
        new(IntrinsicId.FormatInteger128, IntrinsicTarget.None, LaysOutType: false, IntrinsicWriter.Unwritten, null), // No MIR producer (R8, PD21).
        new(IntrinsicId.MakeObj, IntrinsicTarget.Descriptor, LaysOutType: false, IntrinsicWriter.Create, null),
        new(IntrinsicId.MakeRc, IntrinsicTarget.Descriptor, LaysOutType: false, IntrinsicWriter.Create, null),
        new(IntrinsicId.MakeArc, IntrinsicTarget.Descriptor, LaysOutType: false, IntrinsicWriter.Create, null),
        new(IntrinsicId.Clone, IntrinsicTarget.None, LaysOutType: false, IntrinsicWriter.Clone, null), // Payload-independent (IMPL 21.1.3).
        // TypeTest acquires nothing (SPEC 13.6.4); code generation numbers its target (R5-M74, R5-M110).
        new(IntrinsicId.TypeTest, IntrinsicTarget.None, LaysOutType: false, IntrinsicWriter.Test, null),
        new(IntrinsicId.TestObserve, IntrinsicTarget.None, LaysOutType: false, IntrinsicWriter.Unwritten, null), // C10a.
        new(IntrinsicId.TestMessage, IntrinsicTarget.None, LaysOutType: false, IntrinsicWriter.Unwritten, null), // C10a.
        new(IntrinsicId.TestRequireAbort, IntrinsicTarget.None, LaysOutType: false, IntrinsicWriter.Unwritten, null), // C10a.
        new(IntrinsicId.TestRecordValue, IntrinsicTarget.None, LaysOutType: false, IntrinsicWriter.Unwritten, null), // C10a.
        new(IntrinsicId.TestPhaseEnter, IntrinsicTarget.None, LaysOutType: false, IntrinsicWriter.Unwritten, null), // C10b.
        new(IntrinsicId.TestPhaseLeave, IntrinsicTarget.None, LaysOutType: false, IntrinsicWriter.Unwritten, null), // C10b.
        new(IntrinsicId.TestTempDirectory, IntrinsicTarget.None, LaysOutType: false, IntrinsicWriter.Unwritten, null), // C10a.
        new(IntrinsicId.StaticEnsure, IntrinsicTarget.StaticInit, LaysOutType: false, IntrinsicWriter.Ensure, null),
        new(IntrinsicId.Capacity, IntrinsicTarget.None, LaysOutType: false, IntrinsicWriter.Capacity, null),
        new(
            IntrinsicId.FormatBuffer,
            IntrinsicTarget.None,
            LaysOutType: false,
            IntrinsicWriter.Buffer,
            LegacyRuntimeHelpers.FormattingRow(CompilerFunctionKind.TextHeap)!.Abi),
        new(IntrinsicId.FormatHint, IntrinsicTarget.None, LaysOutType: false, IntrinsicWriter.Hint, LegacyRuntimeHelpers.FormatHint),
        new(IntrinsicId.FormatFinish, IntrinsicTarget.None, LaysOutType: false, IntrinsicWriter.Finish, LegacyRuntimeHelpers.FormatFinish),
    ]);

    /// <summary>Gets the row of an intrinsic.</summary>
    /// <param name="id">The intrinsic, not None.</param>
    /// <returns>The row.</returns>
    internal static IntrinsicRow Of(IntrinsicId id)
        => (uint)((int)id - 1) < (uint)Rows.Length ? Rows[(int)id - 1] : throw new InvalidOperationException($"The intrinsic {id} has no Backend row.");

    // The table, its rows in IntrinsicId order from the first member after None; a row missing or out of order is a broken contract.
    private static IntrinsicRow[] Complete(IntrinsicRow[] rows)
    {
        var complete = rows.Length == Enum.GetValues<IntrinsicId>().Length - 1;
        for (var i = 0; complete && i < rows.Length; i++)
        {
            complete = rows[i].Id == (IntrinsicId)(i + 1);
        }

        return complete ? rows : throw new InvalidOperationException("The intrinsic table needs one row per IntrinsicId, in its order.");
    }
}
