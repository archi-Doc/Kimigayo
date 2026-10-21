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
}

/// <summary>A row of the intrinsic table.</summary>
/// <param name="Id">The intrinsic, the row's index plus 1.</param>
/// <param name="Target">The item a call names as its collector target.</param>
/// <param name="LaysOutType">Whether the call's Type argument is a by-value use (K package 4.8).</param>
/// <param name="Writer">How code generation writes a call.</param>
/// <param name="Routine">The runtime routine the writer calls, if any.</param>
internal readonly record struct IntrinsicRow(
    IntrinsicId Id, IntrinsicTarget Target, bool LaysOutType = false, IntrinsicWriter Writer = IntrinsicWriter.Unwritten, FunctionAbi? Routine = null);

/// <summary>The intrinsic table of the Backend: one row per <see cref="IntrinsicId"/>.</summary>
internal static class IntrinsicRows
{
    // One row per IntrinsicId but None, in the enum's order (Complete).
    private static readonly IntrinsicRow[] Rows = Complete(
    [
        new(IntrinsicId.RawAllocate, IntrinsicTarget.None, LaysOutType: true, IntrinsicWriter.Allocate, RuntimeAbi.RawAllocate),
        new(IntrinsicId.RawRelease, IntrinsicTarget.None, Writer: IntrinsicWriter.Routine, Routine: RuntimeAbi.StorageRelease),
        new(IntrinsicId.RawInitialize, IntrinsicTarget.None, Writer: IntrinsicWriter.Initialize),
        new(IntrinsicId.RawSlice, IntrinsicTarget.None, Writer: IntrinsicWriter.Slice),
        new(IntrinsicId.TryAllocateBytes, IntrinsicTarget.None, Writer: IntrinsicWriter.TryAllocate, Routine: RuntimeAbi.TryAllocateBytes),
        new(IntrinsicId.MoveBytes, IntrinsicTarget.None, Writer: IntrinsicWriter.Copy),
        new(IntrinsicId.Stride, IntrinsicTarget.None, LaysOutType: true), // C8a's once a compiler function serves it (none does yet).
        new(IntrinsicId.DestroyValues, IntrinsicTarget.Glue, LaysOutType: true), // C4a.
        new(IntrinsicId.ReinterpretLoan, IntrinsicTarget.None), // C8a's once a compiler function serves it (none does yet).
        new(IntrinsicId.Abort, IntrinsicTarget.None, Writer: IntrinsicWriter.Routine, Routine: RuntimeAbi.AbortMessage),
        new(IntrinsicId.Fail, IntrinsicTarget.None, Writer: IntrinsicWriter.Fail, Routine: RuntimeAbi.Abort),
        new(IntrinsicId.WriteStdout, IntrinsicTarget.None), // C9a.
        new(IntrinsicId.FormatFloat, IntrinsicTarget.None), // C9b.
        new(IntrinsicId.ErasedWriter, IntrinsicTarget.Witness), // C9b.
        new(IntrinsicId.ErasedReserve, IntrinsicTarget.None), // C9b.
        new(IntrinsicId.FormatInteger128, IntrinsicTarget.None), // C9b.
        new(IntrinsicId.MakeObj, IntrinsicTarget.Descriptor), // C6a.
        new(IntrinsicId.MakeRc, IntrinsicTarget.Descriptor), // C6a.
        new(IntrinsicId.MakeArc, IntrinsicTarget.Descriptor), // C6a.
        new(IntrinsicId.Clone, IntrinsicTarget.None), // Payload-independent (IMPL 21.1.3); C6a.
        new(IntrinsicId.TypeTest, IntrinsicTarget.None), // Acquires nothing (SPEC 13.6.4); C6a numbers its target (R5-M74).
        new(IntrinsicId.TestObserve, IntrinsicTarget.None), // C10a.
        new(IntrinsicId.TestMessage, IntrinsicTarget.None), // C10a.
        new(IntrinsicId.TestRequireAbort, IntrinsicTarget.None), // C10a.
        new(IntrinsicId.TestRecordValue, IntrinsicTarget.None), // C10a.
        new(IntrinsicId.TestPhaseEnter, IntrinsicTarget.None), // C10b.
        new(IntrinsicId.TestPhaseLeave, IntrinsicTarget.None), // C10b.
        new(IntrinsicId.TestTempDirectory, IntrinsicTarget.None), // C10a.
        new(IntrinsicId.StaticEnsure, IntrinsicTarget.StaticInit), // C7.
        new(IntrinsicId.Capacity, IntrinsicTarget.None), // C8d.
        new(IntrinsicId.FormatBuffer, IntrinsicTarget.None), // C9c.
        new(IntrinsicId.FormatHint, IntrinsicTarget.None), // C9c.
        new(IntrinsicId.FormatFinish, IntrinsicTarget.None), // C9c.
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
