// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the Backend's row of each intrinsic (R5 package K 4.5, C 3.2; S2-S4 X15, X27; DECISIONS R5-M35), the one table the instance
// collector and code generation read by IntrinsicId. Column Target: the item a call of the intrinsic names as its collector target, the
// whitelist of target-bearing intrinsic sites (X15); every other intrinsic carries none. Column LaysOutType: the call's Type argument is a
// by-value use whose layout the collector checks (K 4.8; DECISIONS R5-M44). The first code generation unit that writes an intrinsic call (C3a,
// C8a; DECISIONS R5-M65) adds the writer column.
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

/// <summary>A row of the intrinsic table.</summary>
/// <param name="Id">The intrinsic, the row's index plus 1.</param>
/// <param name="Target">The item a call names as its collector target.</param>
/// <param name="LaysOutType">Whether the call's Type argument is a by-value use (K package 4.8).</param>
internal readonly record struct IntrinsicRow(IntrinsicId Id, IntrinsicTarget Target, bool LaysOutType = false);

/// <summary>The intrinsic table of the Backend: one row per <see cref="IntrinsicId"/>.</summary>
internal static class IntrinsicRows
{
    // One row per IntrinsicId but None, in the enum's order (Complete).
    private static readonly IntrinsicRow[] Rows = Complete(
    [
        new(IntrinsicId.RawAllocate, IntrinsicTarget.None, LaysOutType: true),
        new(IntrinsicId.RawRelease, IntrinsicTarget.None),
        new(IntrinsicId.RawInitialize, IntrinsicTarget.None),
        new(IntrinsicId.RawSlice, IntrinsicTarget.None),
        new(IntrinsicId.TryAllocateBytes, IntrinsicTarget.None),
        new(IntrinsicId.MoveBytes, IntrinsicTarget.None),
        new(IntrinsicId.Stride, IntrinsicTarget.None, LaysOutType: true),
        new(IntrinsicId.DestroyValues, IntrinsicTarget.Glue, LaysOutType: true),
        new(IntrinsicId.ReinterpretLoan, IntrinsicTarget.None),
        new(IntrinsicId.Abort, IntrinsicTarget.None),
        new(IntrinsicId.Fail, IntrinsicTarget.None),
        new(IntrinsicId.WriteStdout, IntrinsicTarget.None),
        new(IntrinsicId.FormatFloat, IntrinsicTarget.None),
        new(IntrinsicId.ErasedWriter, IntrinsicTarget.Witness),
        new(IntrinsicId.ErasedReserve, IntrinsicTarget.None),
        new(IntrinsicId.FormatInteger128, IntrinsicTarget.None),
        new(IntrinsicId.MakeObj, IntrinsicTarget.Descriptor),
        new(IntrinsicId.MakeRc, IntrinsicTarget.Descriptor),
        new(IntrinsicId.MakeArc, IntrinsicTarget.Descriptor),
        new(IntrinsicId.Clone, IntrinsicTarget.None), // Payload-independent (IMPL 21.1.3).
        new(IntrinsicId.TypeTest, IntrinsicTarget.None), // Acquires nothing (SPEC 13.6.4); codegen numbers its target (R5-M74).
        new(IntrinsicId.TestObserve, IntrinsicTarget.None),
        new(IntrinsicId.TestMessage, IntrinsicTarget.None),
        new(IntrinsicId.TestRequireAbort, IntrinsicTarget.None),
        new(IntrinsicId.TestRecordValue, IntrinsicTarget.None),
        new(IntrinsicId.TestPhaseEnter, IntrinsicTarget.None),
        new(IntrinsicId.TestPhaseLeave, IntrinsicTarget.None),
        new(IntrinsicId.TestTempDirectory, IntrinsicTarget.None),
        new(IntrinsicId.StaticEnsure, IntrinsicTarget.StaticInit),
        new(IntrinsicId.Capacity, IntrinsicTarget.None),
        new(IntrinsicId.FormatBuffer, IntrinsicTarget.None),
        new(IntrinsicId.FormatHint, IntrinsicTarget.None),
        new(IntrinsicId.FormatFinish, IntrinsicTarget.None),
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
