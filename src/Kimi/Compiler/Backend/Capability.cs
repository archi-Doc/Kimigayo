// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the capability table (IMPL 21.3.3, SPEC 23.1.1; R5 package K 4.7; DECISIONS R5-M40, R5-M53): what the backend does not generate on
// windows-x64, the capability check of COMPILER_ARCHITECTURE's closed vocabularies, which the instance collector reads, at the definition
// stage (A3b) and per instance.
// Rows: the instance gaps of storage nesting past Binding's bound (PLAN G128, R5 PD30), whose substitution TyCtxt.Apply cannot make, and of
// a nested function called without its enclosing instance (until PLAN G149), and of a requirement call whose inherited witness cannot
// preserve its receiver (OCC-X, DECISIONS R5-M67); the declaration row of a static whose storage P42 has not brought, a generic
// container's included (DECISIONS R5-M82, superseding R5-M60's access row); the operation rows of IMPL
// 21.3.3's 128-bit subset, division and remainder of i128 and u128 (a compound form builds as the same operation) and numeric conversions
// between them and f32 or f64 (a direct literal conversion is a constant, Binding having fitted the literal, so it is no Cast).
// Input: for an operation, its MIR rvalue and its first operand's closed Type. Output: a gap's record, Unsupported_Kd at the subject the
// collector's location rule gives (a declaration row's: the declaration), once per subject.
// Must not: be restated in a Supports* gate or another table, or decide acceptance.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

/// <summary>A row of the capability table: a form or instance the backend does not generate (IMPL 21.3.3).</summary>
internal enum CapabilityGap : byte
{
    StorageNesting, // An instance whose stored Types nest past Binding's bound, so TyCtxt.Apply refuses it (PLAN G128).

    // A function nested in a body (a block of a generic function, a function nested in one, a method or a specialization of a generic
    // container) called by a substitution that does not carry the enclosing instance, so no key holds the enclosing arguments (SPEC
    // 8.13.3; DECISIONS R5-M40, R5-M43).
    [Transitional("R5", Reason = "PLAN G149 represents the enclosing-instance chain in a call's substitution.")]
    EnclosingInstance,

    // A requirement call whose witness is inherited and takes a receiver, which lends only its base prefix and so preserves no exclusive
    // or owning requirement receiver (OCC-X, SPEC 12.7.1; Binding's refused witness, DECISIONS R5-M22, R5-M67).
    ExclusiveInheritedWitness,

    // A static whose storage P42 has not brought (SPEC 22.4, 22.4.4; STATUS "Static members"): one a root module declares that can be set,
    // whose Type is not a scalar, or in a generic container's group, whose storage the MIR keys by its symbol alone (X33 (a)), used or not,
    // since code generation reaches only what the output requires (DECISIONS R5-M77, R5-M82).
    StaticStorage,

    WideDivision, // Division or remainder of i128 or u128 (IMPL 21.3.3).
    WideFloatConversion, // A numeric conversion between i128 or u128 and f32 or f64 (IMPL 21.3.3).
}

/// <summary>The capability table of the Backend.</summary>
internal static class Capability
{
    /// <summary>Gets the record a gap makes (SPEC 23.1.1).</summary>
    /// <param name="gap">The gap.</param>
    /// <returns>Its code.</returns>
    internal static DiagnosticCode Code(CapabilityGap gap) => gap switch
    {
        CapabilityGap.StorageNesting => DiagnosticCode.Unsupported_Kd,
        CapabilityGap.EnclosingInstance => DiagnosticCode.Unsupported_Kd,
        CapabilityGap.ExclusiveInheritedWitness => DiagnosticCode.Unsupported_Kd,
        CapabilityGap.StaticStorage => DiagnosticCode.Unsupported_Kd,
        CapabilityGap.WideDivision => DiagnosticCode.Unsupported_Kd,
        CapabilityGap.WideFloatConversion => DiagnosticCode.Unsupported_Kd,
        _ => throw new InvalidOperationException($"The capability gap {gap} has no row."),
    };

    /// <summary>Gets the row of an operation the backend does not generate (IMPL 21.3.3), or none.</summary>
    /// <param name="rvalue">The operation: a Cast's Type is its target.</param>
    /// <param name="operand">The closed Type of its first operand.</param>
    /// <returns>The row, or null.</returns>
    internal static CapabilityGap? Of(in MirRvalue rvalue, BoundType operand) => rvalue.Kind switch
    {
        MirRvalueKind.BinaryOp when (MirBinaryOp)rvalue.Op is MirBinaryOp.Div or MirBinaryOp.Rem && ScalarTypes.Width(operand) == 128 =>
            CapabilityGap.WideDivision,
        MirRvalueKind.Cast when (MirCastKind)rvalue.Op == MirCastKind.Numeric && IsWideFloat(operand, rvalue.Type!) => CapabilityGap.WideFloatConversion,
        _ => null,
    };

    // Whether a numeric conversion is between a 128-bit integer and a floating-point Type.
    private static bool IsWideFloat(BoundType source, BoundType target)
        => (ScalarTypes.Width(source) == 128 && target.IsFloatingPoint) || (source.IsFloatingPoint && ScalarTypes.Width(target) == 128);
}
