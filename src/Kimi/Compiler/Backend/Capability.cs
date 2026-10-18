// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the capability table (IMPL 21.3.3, SPEC 23.1.1; R5 package K 4.7; DECISIONS R5-M40): what the backend does not generate on
// windows-x64, the capability check of COMPILER_ARCHITECTURE's closed vocabularies, which the instance collector reads (and A3b from K9a).
// Rows: the instance gaps of storage nesting past Binding's bound (PLAN G128, R5 PD30), whose substitution TyCtxt.Apply cannot make, and of
// a nested function called without its enclosing instance (until PLAN G149), and of a static of a generic container's group (K7). OCC-X's
// refusal (K6) and the operation rows (K9a) join them.
// Output: a gap's record, Unsupported_Kd at the subject the collector's location rule gives, once per subject.
// Must not: be restated in a Supports* gate or another table, or decide acceptance.

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

    // A static of a generic container's group, whose storage the MIR keys by its symbol alone, so no key holds the container's arguments
    // (S2-S4 X6; the owner's choice X33 (a): generic storage keys belong to P42; DECISIONS R5-M60).
    GenericStaticKey,
}

/// <summary>The capability table of the Backend.</summary>
internal static class Capability
{
    /// <summary>Gets the record a gap makes (SPEC 23.1.1).</summary>
    /// <param name="gap">The gap.</param>
    /// <returns>Its code.</returns>
    internal static DiagnosticCode Code(CapabilityGap gap) => gap switch
    {
        CapabilityGap.StorageNesting or CapabilityGap.EnclosingInstance or CapabilityGap.GenericStaticKey => DiagnosticCode.Unsupported_Kd,
        _ => throw new InvalidOperationException($"The capability gap {gap} has no row."),
    };
}
