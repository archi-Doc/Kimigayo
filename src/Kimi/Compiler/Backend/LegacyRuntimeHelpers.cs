// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the legacy runtime helpers of the generated LLVM text until Kimigayo library code replaces them at R8 (plan PD2, P1a-P2b;
// DECISIONS R5-M16): the formatting runtime with its routines' ABIs (LegacyRuntimeHelpers.Formatting.cs). MIR code generation writes the
// helpers through this static component (promoted, never copied).
// Input: a Text.writer destination, whose writer kind it gives (WriterKind); an owning interpolation's literal bytes and values'
// bounds, summed by its estimate (FormatEstimate); the runtime templates (.ll.in); a writer, LLVM SSA names and sizes.
// Output: LLVM text written to the writer; per helper kind one row, readable by kind before any representation exists (X4); the
// formatting runtime's routine ABIs, whose callers request its text (HelperNeeds.FormattingRuntime), and which call no library
// function; the kind Context of a string literal's write is the runtime's own number (StringFormatKind),
// and so is Text.writer's of its destination (WriterKind); a bounded Type's maximum is FormattingTypes.Bound's,
// which an owning interpolation's estimate sums for its buffer and hints (FormatEstimate; R3-10).
// The one map from a compiler
// function of the Body disposition to the runtime routine MIR code generation calls for it (Row, S2-S4 X39; DECISIONS R5-M116):
// writeLine's, the one such kind left (the formatting kinds are bodies, R8-W1a to W1c).
// Invariants: a new helper kind adds a row; a kind without a row fails, a Body-disposition compiler function's included; the
// formatting runtime's text is written once when a caller requests it; warm writes allocate nothing; the expanded runtime texts
// (Texts) are built only when a code generation writer first reads one, never on the check path (G350).
// Must not hold module state, take syntax or MIR types, or a BoundType but a writer's destination for its kind (WriterKind),
// or use LINQ.

#pragma warning disable SA1402 // The helper records are this component's request vocabulary.

namespace Kimi.Compiler.Backend;

/// <summary>The legacy runtime text a module must hold, which its callers request (X4).</summary>
[Flags]
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal enum HelperNeeds : byte
{
    None = 0,

    /// <summary>The formatting runtime: the UTF-8 buffer, writer, formatting and floating-point routines and the memory
    /// declarations (SPEC 22.7, 22.9, 22.10).</summary>
    FormattingRuntime = 8,
}

/// <summary>The legacy row of a compiler function of the Body disposition (X39): the runtime routine MIR code generation calls for it
/// through the one call writer.</summary>
/// <param name="Routine">The row's runtime routine.</param>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal readonly record struct LegacyRow(FunctionAbi Routine);

/// <summary>The legacy runtime helpers' rows and writers, and the routines its helpers share.</summary>
[Transitional("R8", Reason = Removal)]
internal static partial class LegacyRuntimeHelpers
{
    internal const string Removal = "R8 replaces the legacy runtime helpers with Kimigayo library code.";

    /// <summary>Gets the legacy row of a compiler function of the Body disposition (S2-S4 X39; DECISIONS R5-M116), the one map from such a
    /// kind to the runtime routine MIR code generation calls for it, and the one test of whether a function is one: every site asks
    /// it, a Call, a Value call, the copy slots and the routine rows.</summary>
    /// <param name="kind">The compiler function.</param>
    /// <returns>Its row; null for None and for a kind of another disposition.</returns>
    internal static LegacyRow? Row(CompilerFunctionKind kind) => kind switch
    {
        CompilerFunctionKind.None => null,
        CompilerFunctionKind.WriteLine => new(RuntimeAbi.WriteLine), // SPEC 22.1-22.5.5: it borrows its string.
        _ when IntrinsicDisposition.Of(kind).Kind == DispositionKind.Body =>
            throw new InvalidOperationException($"The compiler function {kind} has no legacy row."), // A fault: each has a row.
        _ => null,
    };
}
