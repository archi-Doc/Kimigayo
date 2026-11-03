// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the capability table (IMPL 21.3.3, SPEC 23.1.1; R5 package K 4.7; DECISIONS R5-M40, R5-M53, R5-M146): what the backend does not
// generate on windows-x64, IMPL 21.3.3's operation rows and the decided D2 rows of the R5 plan's PD27 list, each with its removal condition;
// the capability check of COMPILER_ARCHITECTURE's closed vocabularies, which the instance collector reads, at the definition stage (A3b) and
// per instance.
// Rows: the instance gaps of storage nesting past Binding's bound (PLAN G128, R5 PD30), whose substitution TyCtxt.Apply cannot make, and of
// a nested function called without its enclosing instance (until PLAN G149) or a Closure no instance closed (a held instance with an Origin
// position until PLAN G300's per-use opening, a generic container's group static until P42; DECISIONS R5-M157, BU-60), and of a
// requirement call whose inherited witness cannot preserve its receiver (OCC-X, DECISIONS R5-M67), and of a requirement call or Item whose
// witness has no MIR body (DECISIONS R5-M146);
// the declaration row of a static whose storage P42 has not brought, a generic
// container's included (DECISIONS R5-M82, superseding R5-M60's access row); the operation rows of IMPL
// 21.3.3's 128-bit subset, division and remainder of i128 and u128 (a compound form builds as the same operation) and numeric conversions
// between them and f32 or f64 (a direct literal conversion is a constant, Binding having fitted the literal, so it is no Cast).
// Input: for an operation, its MIR rvalue, or its kind, operator and Cast target (an arithmetic shim's, BuiltinArithmetic), and its first
// operand's closed Type. Output: a gap's record, Unsupported_Kd at the subject the collector's location rule gives (a declaration row's:
// the declaration), once per subject.
// Must not: be restated in a Supports* gate or another table, or decide acceptance.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

/// <summary>A row of the capability table: a form or instance the backend does not generate (IMPL 21.3.3).</summary>
internal enum CapabilityGap : byte
{
    // An instance whose stored Types nest past Binding's bound, so TyCtxt.Apply refuses it (PLAN G128).
    [Transitional("R7", Reason = "PLAN G128's storage bound gives way to a substitution TyCtxt.Apply makes (R5 PD30; the owner's PD27 list, R5-M146).")]
    StorageNesting,

    // A function nested in a body (a block of a generic function, a function nested in one, a method or a specialization of a generic
    // container) called by a substitution that does not carry the enclosing instance, so no key holds the enclosing arguments (SPEC
    // 8.13.3; DECISIONS R5-M40, R5-M43), a function declared in a Field initializer or a getter included; and a Closure that no instance
    // closed, so its body is requested in its own identity (TyCtxt.SubstOfClosure; DECISIONS R5-M157, BU-60): one in a Field or static
    // initializer whose held instance has an Origin position (TyCtxt.ClosureInstance), until PLAN G300's per-use opening, and one in a
    // generic container's group static, whose group has no instance (R5-M82's static-storage row, X33 (a)), until P42 keys that storage.
    [Transitional("R7", Reason = "G149: a call's enclosing-instance chain (PD27, R5-M146); G300: Origin-carrying owners opened per use; P42: group statics.")]
    EnclosingInstance,

    // A requirement call whose witness is inherited and takes a receiver, which lends only its base prefix and so preserves no exclusive
    // or owning requirement receiver (OCC-X, SPEC 12.7.1; Binding's refused witness, DECISIONS R5-M22, R5-M67).
    ExclusiveInheritedWitness,

    // A requirement call or requirement Item whose witness is a function without a MIR body (SPEC 8.6.4, 7.7.4; DECISIONS R5-M146,
    // R5-M148): FixedBuffer's or HeapBuffer's reserve through BufferWriter (SPEC 22.9.4) until R8 gives them Kimigayo bodies, or a
    // #LibraryImport function (SPEC 22.6) until R8 too, when the collector publishes the import as the call's target; a witness runs as a
    // body item or a shim item.
    [Transitional("R8", Reason = "R8 gives the buffers' reserve Kimigayo bodies and publishes an import as its call's target (R5-M146, R5-M148).")]
    BodylessWitness,

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
        CapabilityGap.BodylessWitness => DiagnosticCode.Unsupported_Kd,
        CapabilityGap.StaticStorage => DiagnosticCode.Unsupported_Kd,
        CapabilityGap.WideDivision => DiagnosticCode.Unsupported_Kd,
        CapabilityGap.WideFloatConversion => DiagnosticCode.Unsupported_Kd,
        _ => throw new InvalidOperationException($"The capability gap {gap} has no row."),
    };

    /// <summary>Gets the row of an operation the backend does not generate (IMPL 21.3.3), or none.</summary>
    /// <param name="rvalue">The operation: a Cast's Type is its target.</param>
    /// <param name="operand">The closed Type of its first operand.</param>
    /// <returns>The row, or null.</returns>
    internal static CapabilityGap? Of(in MirRvalue rvalue, BoundType operand) => Of(rvalue.Kind, rvalue.Op, operand, rvalue.Type);

    /// <summary>Gets the row of an operation the backend does not generate (IMPL 21.3.3), or none, by its kind and operator: a statement's
    /// or an arithmetic shim's (BuiltinArithmetic).</summary>
    /// <param name="kind">The operation's rvalue kind.</param>
    /// <param name="op">Its operator or cast kind.</param>
    /// <param name="operand">The closed Type of its first operand.</param>
    /// <param name="target">A Cast's target Type.</param>
    /// <returns>The row, or null.</returns>
    internal static CapabilityGap? Of(MirRvalueKind kind, byte op, BoundType operand, BoundType? target) => kind switch
    {
        MirRvalueKind.BinaryOp when (MirBinaryOp)op is MirBinaryOp.Div or MirBinaryOp.Rem && ScalarTypes.Width(operand) == 128 =>
            CapabilityGap.WideDivision,
        MirRvalueKind.Cast when (MirCastKind)op == MirCastKind.Numeric && IsWideFloat(operand, target!) => CapabilityGap.WideFloatConversion,
        _ => null,
    };

    // Whether a numeric conversion is between a 128-bit integer and a floating-point Type.
    private static bool IsWideFloat(BoundType source, BoundType target)
        => (ScalarTypes.Width(source) == 128 && target.IsFloatingPoint) || (source.IsFloatingPoint && ScalarTypes.Width(target) == 128);
}
