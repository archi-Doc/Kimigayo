// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the legacy runtime helpers of code generation until Kimigayo library code replaces them at R8 (plan PD2, P1a-P2b; DECISIONS
// R5-M16): the legacy row map and Text.writer's destination kind (LegacyRuntimeHelpers.Formatting.cs); it writes no runtime text since
// R8-W2 c4. MIR code generation reads them through this static component (promoted, never copied).
// Input: a compiler function of the Body disposition; a Text.writer destination, whose writer kind it gives (WriterKind).
// Output: the one map from a compiler function of the Body disposition to the runtime routine MIR code generation calls for it (Row,
// S2-S4 X39; DECISIONS R5-M116): writeLine's, the one such kind left (the formatting kinds are bodies, R8-W1a to W1c; every other
// compiler function's declaration carries `#Intrinsic`, KimiLibraryShapeTest's row L2, and CompilerFunctionKind gains no member, RQ9);
// the kind Context of Text.writer's destination, the number Utf8Writer.reserve's kind switch reads (WriterKind).
// Invariants: a new helper kind adds a row; reads allocate nothing.
// Must not hold module state, take syntax or MIR types, or a BoundType but a writer's destination for its kind (WriterKind),
// or use LINQ.

#pragma warning disable SA1402 // The legacy row is this component's vocabulary.

namespace Kimi.Compiler.Backend;

/// <summary>The legacy row of a compiler function of the Body disposition (X39): the runtime routine MIR code generation calls for it
/// through the one call writer.</summary>
/// <param name="Routine">The row's runtime routine.</param>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal readonly record struct LegacyRow(FunctionAbi Routine);

/// <summary>The legacy runtime helpers: the legacy row map and Text.writer's destination kind.</summary>
[Transitional("R8", Reason = Removal)]
internal static partial class LegacyRuntimeHelpers
{
    internal const string Removal = "R8 replaces the legacy runtime helpers with Kimigayo library code.";

    /// <summary>Gets the legacy row of a compiler function of the Body disposition (S2-S4 X39; DECISIONS R5-M116), the one map from such a
    /// kind to the runtime routine MIR code generation calls for it, and the one test of whether a function is one: every site asks
    /// it, a Call, a Value call, the copy slots and the routine rows.</summary>
    /// <param name="kind">The compiler function.</param>
    /// <returns>Its row; null for None and for a kind of another disposition (an intrinsic's or a shim's).</returns>
    internal static LegacyRow? Row(CompilerFunctionKind kind)
        => kind == CompilerFunctionKind.WriteLine ? new(RuntimeAbi.WriteLine) : null; // SPEC 22.1-22.5.5: it borrows its string.
}
