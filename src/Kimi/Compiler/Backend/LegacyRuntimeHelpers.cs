// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the legacy runtime helpers of the generated LLVM text until Kimigayo library code replaces them at R8 (plan PD2, P1a-P2b;
// DECISIONS R5-M16): the formatting runtime with its routines' ABIs and the wrappers over user formatting witnesses
// (LegacyRuntimeHelpers.Formatting.cs) and the string-comparison routines, spliced from their signatures (StringEqual, StringCompare,
// the ones every call reads). MIR code generation writes the helpers through this static component (promoted, never copied).
// Input: the formatting wrappers' ABIs and the compiled functions they call; a built-in formattable Type, whose
// formatting kind and bound it gives (FormatKind, FormatBound), and a Text.writer destination, whose writer kind it gives (WriterKind);
// an owning interpolation's literal bytes and values' bounds, summed by its estimate (FormatEstimate); the runtime templates
// (.ll.in); a writer, LLVM SSA names and sizes.
// Output: LLVM text written to the writer; per helper kind one row, readable
// by kind before any representation exists, which declares the kind's runtime needs for the instance collector (X4). Each formatting
// compiler function's row
// (FormattingRow; P1d) holds its routine's ABI, whose capacity Context names by its logical index the parameter receiving the fixed array
// it measures, and HelperNeeds (the formatting runtime) and declares no library callee, since the formatting runtime calls none; the
// formatting witness WriterWrite, TextToString and TextTryFormat run is the need their published library rows state (Formats, SPEC
// 4.9.6), which the collector reads there; the kind Context of a built-in formattable Type is the runtime's own numbering (FormatKind),
// and so is Text.writer's of its destination (WriterKind); a bounded Type's maximum is the runtime's bound at its kind (FormatBound),
// which an owning interpolation's estimate sums for its buffer, hints and stack placement (FormatEstimate, FormatStackLimit; R3-10).
// The one map from a compiler
// function of the Body disposition to how MIR code generation writes its call (Row, S2-S4 X39; DECISIONS R5-M116) names each kind's
// writer: writeLine's runtime routine and
// the Text, buffer, window and writer kinds and WriteLineUtf8 as their formatting routines (Formatting; DECISIONS R5-M128).
// Invariants: a new helper kind adds a row; a kind without a row fails, a Body-disposition compiler function's included; a helper's
// runtime needs come from its row; warm writes allocate nothing; the expanded runtime texts (Texts) are built only when a code
// generation writer first reads one, never on the check path (G350).
// Must not hold module state, take syntax or MIR types, or a BoundType but a built-in formattable Type or a writer's
// destination for its kind or bound (FormatKind, FormatBound, WriterKind), or use LINQ.

#pragma warning disable SA1402 // The helper records are this component's request vocabulary.

using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

/// <summary>The needs a legacy helper row declares (X4): runtime text its module must hold.</summary>
[Flags]
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal enum HelperNeeds : byte
{
    None = 0,

    /// <summary>The formatting runtime: the UTF-8 buffer, writer, formatting and floating-point routines and the memory
    /// declarations (SPEC 22.7, 22.9, 22.10).</summary>
    FormattingRuntime = 8,
}

/// <summary>How MIR code generation writes a call of a compiler function of the Body disposition (S2-S4 X39; DECISIONS R5-M116).</summary>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal enum LegacyWriter : byte
{
    /// <summary>The row's runtime routine through the one call writer.</summary>
    Routine,

    /// <summary>SPEC 22.7, 22.9, 22.10.1: the row's routine in the formatting runtime through the one call writer, its capacity Context the
    /// length of the fixed array its operand refers to and its kind Context its witness target's Self's (FunctionCodegen.Formatting;
    /// DECISIONS R5-M128).</summary>
    Formatting,
}

/// <summary>The legacy row of a compiler function of the Body disposition (X39): its writer and a routine row's runtime routine.</summary>
/// <param name="Writer">How MIR code generation writes a call.</param>
/// <param name="Routine">The row's runtime routine.</param>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal readonly record struct LegacyRow(LegacyWriter Writer, FunctionAbi? Routine = null)
{
    /// <summary>Gets the runtime needs a row declares (X4; DECISIONS R5-M46, R5-M128): the formatting runtime for a formatting routine, the
    /// need every formatting row declares (FormattingRow; DECISIONS R5-M30).</summary>
    internal HelperNeeds Needs => this.Writer == LegacyWriter.Formatting ? HelperNeeds.FormattingRuntime : HelperNeeds.None;
}

/// <summary>The legacy runtime helpers' rows and writers, and the routines its helpers share.</summary>
[Transitional("R8", Reason = Removal)]
internal static partial class LegacyRuntimeHelpers
{
    internal const string Removal = "R8 replaces the legacy runtime helpers with Kimigayo library code.";

    /// <summary>SPEC 13.4: the string equality routine, 0 exactly when the two byte sequences are equal: the one signature its definition
    /// (<see cref="StringComparisons"/>) and every call read.</summary>
    internal static readonly FunctionAbi StringEqual =
        new("__kimi_string_equal", "i32", [new("ptr", "a"), new("i64", "an"), new("ptr", "b"), new("i64", "bn")]);

    /// <summary>SPEC 13.4: the string ordering routine, negative, 0 or positive as the first byte sequence orders before, equal to or after
    /// the second, lexicographically by UTF-8 byte.</summary>
    internal static readonly FunctionAbi StringCompare = new("__kimi_string_compare", "i32", StringEqual.Parameters);

    /// <summary>Gets the string-comparison routines' text, each definition spliced from its signature (LlvmModuleText.ExpandRuntime), which
    /// code generation writes once per module that compares strings.</summary>
    internal static string StringComparisons => Texts.StringComparisons;

    /// <summary>Gets the legacy row of a compiler function of the Body disposition (S2-S4 X39; DECISIONS R5-M116), the one map from such a
    /// kind to how MIR code generation writes its call, by the row's writer, and the one test of whether a function is one: every site asks
    /// it, a Call, a Value call, the copy slots and the routine rows.</summary>
    /// <param name="kind">The compiler function.</param>
    /// <returns>Its row; null for None and for a kind of another disposition.</returns>
    internal static LegacyRow? Row(CompilerFunctionKind kind) => kind switch
    {
        CompilerFunctionKind.None => null,
        CompilerFunctionKind.WriteLine => new(LegacyWriter.Routine, RuntimeAbi.WriteLine), // SPEC 22.1-22.5.5: it borrows its string.

        // SPEC 22.7, 22.9, 22.10.1 (DECISIONS R5-M128): the Text, buffer, window and writer kinds and WriteLineUtf8, their formatting routines
        // (P1d's rows, one text); a Body-disposition kind without a row is a fault.
        _ when IntrinsicDisposition.Of(kind).Kind == DispositionKind.Body =>
            new(LegacyWriter.Formatting, FormattingRow(kind)?.Abi ?? throw new InvalidOperationException($"The compiler function {kind} has no legacy row.")),
        _ => null,
    };

    // Code generation's expanded runtime texts (G350), one part beside each text's rows: the CLR initializes this class when a writer
    // (WriteFormattingRuntime, StringComparisons) first reads one for a module, never on the check path, whose
    // collector reads the rows and ABIs (Row, IntrinsicRows) but no text; no other member reads them.
    private static partial class Texts
    {
        internal static readonly string StringComparisons = ExpandRuntime(
            """
            declare i32 @memcmp(ptr, ptr, i64)
            {{__kimi_string_equal}}
            entry:
              %sameLength = icmp eq i64 %an, %bn
              br i1 %sameLength, label %same, label %different
            same:
              %empty = icmp eq i64 %an, 0
              br i1 %empty, label %equal, label %bytes
            bytes:
              %comparison = call i32 @memcmp(ptr %a, ptr %b, i64 %an)
              ret i32 %comparison
            equal:
              ret i32 0
            different:
              ret i32 1
            }
            {{__kimi_string_compare}}
            entry:
              %shorter = icmp ult i64 %an, %bn
              %count = select i1 %shorter, i64 %an, i64 %bn
              %empty = icmp eq i64 %count, 0
              br i1 %empty, label %lengths, label %bytes
            bytes:
              %comparison = call i32 @memcmp(ptr %a, ptr %b, i64 %count)
              %equal = icmp eq i32 %comparison, 0
              br i1 %equal, label %lengths, label %different
            different:
              ret i32 %comparison
            lengths:
              %sameLength = icmp eq i64 %an, %bn
              %direction = select i1 %shorter, i32 -1, i32 1
              %result = select i1 %sameLength, i32 0, i32 %direction
              ret i32 %result
            }

            """,
            [StringEqual, StringCompare]);
    }
}
