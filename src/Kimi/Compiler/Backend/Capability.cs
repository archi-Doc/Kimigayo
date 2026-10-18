// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the capability table (IMPL 21.3.3, SPEC 23.1.1; R5 package K 4.7; DECISIONS R5-M40): what the backend does not generate on
// windows-x64, the capability check of COMPILER_ARCHITECTURE's closed vocabularies, which the instance collector reads (and A3b from K9a).
// Rows: the instance gap of storage nesting past Binding's bound (PLAN G128, R5 PD30), whose substitution TyCtxt.Apply cannot make. OCC-X's
// refusal (K6), the generic static storage key (K7) and the operation rows (K9a) join it.
// Output: a gap's record, Unsupported_Kd at the subject the collector's location rule gives, once per subject.
// Must not: be restated in a Supports* gate or another table, or decide acceptance.

namespace Kimi.Compiler.Backend;

/// <summary>A row of the capability table: a form or instance the backend does not generate (IMPL 21.3.3).</summary>
internal enum CapabilityGap : byte
{
    StorageNesting, // An instance whose stored Types nest past Binding's bound, so TyCtxt.Apply refuses it (PLAN G128).
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
        _ => throw new InvalidOperationException($"The capability gap {gap} has no row."),
    };
}
