// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the Backend's row of each intrinsic (R5 package K 4.5, C 3.2; S2-S4 X15, X27; DECISIONS R5-M35), the one table the instance
// collector and code generation read by IntrinsicId. Column Target: the item a call of the intrinsic names as its collector target, the
// whitelist of target-bearing intrinsic sites (X15); every other intrinsic carries none. Column LaysOutType: the call's Type argument is a
// by-value use whose layout the collector checks (K 4.8; DECISIONS R5-M44). C0b1 adds the writer column.
// Input: an IntrinsicId other than None. Output: its row.
// Invariants: one row per IntrinsicId; an id without a row, None included, is a broken MIR contract.
// Must not: resolve a target's item (the collector's), or restate the descriptor table's columns (MirDescriptors).

namespace Kimi.Compiler.Backend;

/// <summary>The item a call of an intrinsic names as its collector target (S2-S4 X15; K package 4.5 rows 6-9).</summary>
internal enum IntrinsicTarget : byte
{
    None,
    Witness, // The body of the user's writer witness (ErasedWriter, K6b).
    StaticInit, // The initializer body of the static (StaticEnsure, K7).
    Descriptor, // The object descriptor of the payload (K8).
    Glue, // The drop glue of the element (DestroyValues, K7).
}

/// <summary>A row of the intrinsic table.</summary>
/// <param name="Target">The item a call names as its collector target.</param>
/// <param name="LaysOutType">Whether the call's Type argument is a by-value use (K package 4.8).</param>
internal readonly record struct IntrinsicRow(IntrinsicTarget Target, bool LaysOutType = false);

/// <summary>The intrinsic table of the Backend: one row per <see cref="IntrinsicId"/>.</summary>
internal static class IntrinsicRows
{
    /// <summary>Gets the row of an intrinsic.</summary>
    /// <param name="id">The intrinsic, not None.</param>
    /// <returns>The row.</returns>
    internal static IntrinsicRow Of(IntrinsicId id) => id switch
    {
        IntrinsicId.RawAllocate => new(IntrinsicTarget.None, LaysOutType: true),
        IntrinsicId.RawRelease => new(IntrinsicTarget.None),
        IntrinsicId.RawInitialize => new(IntrinsicTarget.None),
        IntrinsicId.RawSlice => new(IntrinsicTarget.None),
        IntrinsicId.TryAllocateBytes => new(IntrinsicTarget.None),
        IntrinsicId.MoveBytes => new(IntrinsicTarget.None),
        IntrinsicId.Stride => new(IntrinsicTarget.None, LaysOutType: true),
        IntrinsicId.DestroyValues => new(IntrinsicTarget.Glue, LaysOutType: true),
        IntrinsicId.ReinterpretLoan => new(IntrinsicTarget.None),
        IntrinsicId.Abort => new(IntrinsicTarget.None),
        IntrinsicId.Fail => new(IntrinsicTarget.None),
        IntrinsicId.WriteStdout => new(IntrinsicTarget.None),
        IntrinsicId.FormatFloat => new(IntrinsicTarget.None),
        IntrinsicId.ErasedWriter => new(IntrinsicTarget.Witness),
        IntrinsicId.ErasedReserve => new(IntrinsicTarget.None),
        IntrinsicId.FormatInteger128 => new(IntrinsicTarget.None),
        IntrinsicId.MakeObj => new(IntrinsicTarget.Descriptor),
        IntrinsicId.MakeRc => new(IntrinsicTarget.Descriptor),
        IntrinsicId.MakeArc => new(IntrinsicTarget.Descriptor),
        IntrinsicId.Clone => new(IntrinsicTarget.Descriptor),
        IntrinsicId.TypeTest => new(IntrinsicTarget.Descriptor),
        IntrinsicId.TestObserve => new(IntrinsicTarget.None),
        IntrinsicId.TestMessage => new(IntrinsicTarget.None),
        IntrinsicId.TestRequireAbort => new(IntrinsicTarget.None),
        IntrinsicId.TestRecordValue => new(IntrinsicTarget.None),
        IntrinsicId.TestPhaseEnter => new(IntrinsicTarget.None),
        IntrinsicId.TestPhaseLeave => new(IntrinsicTarget.None),
        IntrinsicId.TestTempDirectory => new(IntrinsicTarget.None),
        IntrinsicId.StaticEnsure => new(IntrinsicTarget.StaticInit),
        IntrinsicId.Capacity => new(IntrinsicTarget.None),
        IntrinsicId.FormatBuffer => new(IntrinsicTarget.None),
        IntrinsicId.FormatHint => new(IntrinsicTarget.None),
        IntrinsicId.FormatFinish => new(IntrinsicTarget.None),
        _ => throw new InvalidOperationException($"The intrinsic {id} has no Backend row."),
    };
}
