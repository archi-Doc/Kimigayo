// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the closed table of runtime abort reasons (SPEC 22.5.2; DECISIONS R5-M15): each reason's index, its template name and its
// message text, and their expansion into the runtime template (the reason table, the message constants, the reason count and the
// index at each `{{reason_<name>}}` call site).
// Input: the runtime template text. Output: the expanded text; the reason index generated code passes to the Abort routine.
// Invariants: indices are module-local ABI values, contiguous from 0 in table order; message texts are printable ASCII without
// `"` or `\`, checked at expansion, so they need no escaping.
// Must not take syntax, HIR, Binding internals or MIR types, or use LINQ.

using System.Globalization;
using System.Text;

namespace Kimi.Compiler.Backend;

/// <summary>The runtime abort reasons: their indices, names and messages.</summary>
internal static class AbortReasons
{
    internal const int Stdout = 0;
    internal const int AllocationSize = 1;
    internal const int ProcessHeap = 2;
    internal const int Allocation = 3;
    internal const int Free = 4;
    internal const int StringRelease = 5;
    internal const int IntegerOverflow = 6;
    internal const int IntegerDivisionZero = 7;
    internal const int IntegerShiftCount = 8;
    internal const int IntegerConversion = 9;
    internal const int FloatingConversion = 11;
    internal const int IndexBounds = 10;
    internal const int ArgumentRange = 12;
    internal const int Format = 13;
    internal const int MissingKey = 14;
    internal const int DuplicateKey = 15;
    internal const int ReferenceCount = 16;
    internal const int StaticCycle = 17;

    // Indices are module-local ABI values. The template's table, lengths and call sites
    // are expanded once from these records; the warm writer only copies the resulting text.
    internal static ReadOnlySpan<AbortReason> All => Reasons;

    private static readonly AbortReason[] Reasons =
    [
        new(Stdout, "stdout", "KIMI_E_STDOUT: Failed to write to stdout"),
        new(AllocationSize, "size", "KIMI_E_ALLOC_SIZE: Allocation size exceeds limit"),
        new(ProcessHeap, "heap", "KIMI_E_PROCESS_HEAP: Failed to obtain process heap"),
        new(Allocation, "alloc", "KIMI_E_ALLOC: Failed to allocate memory"),
        new(Free, "free", "KIMI_E_FREE: Failed to free memory"),
        new(StringRelease, "release", "KIMI_E_STRING_RELEASE: Invalid string release kind"),
        new(IntegerOverflow, "overflow", "KIMI_E_INT_OVERFLOW: Integer overflow"),
        new(IntegerDivisionZero, "division_zero", "KIMI_E_INT_DIV_ZERO: Integer division or remainder by zero"),
        new(IntegerShiftCount, "shift_count", "KIMI_E_INT_SHIFT_COUNT: Shift count out of range"),
        new(IntegerConversion, "conversion", "KIMI_E_INT_CONVERSION: Integer conversion out of range"),
        new(IndexBounds, "index_bounds", "KIMI_E_INDEX_BOUNDS: Index out of bounds"),
        new(FloatingConversion, "float_conversion", "KIMI_E_FLOAT_CONVERSION: Floating conversion out of range"),
        new(ArgumentRange, "argument_range", "KIMI_E_ARG_RANGE: Argument out of range"),
        new(Format, "format", "KIMI_E_FORMAT: Formatting failed"),
        new(MissingKey, "missing_key", "KIMI_E_MISSING_KEY: Dictionary key was not found"),
        new(DuplicateKey, "duplicate_key", "KIMI_E_DUPLICATE_KEY: Dictionary literal contains an equivalent key"),
        new(ReferenceCount, "ref_count", "KIMI_E_REF_COUNT: Reference count limit exceeded"),
        new(StaticCycle, "static_cycle", "KIMI_E_STATIC_CYCLE: Static initialization cycle"),
    ];

    // The runtime template with its reason table, message constants, reason count and call-site indices filled in.
    internal static string Expand(string runtime)
    {
        var table = new StringBuilder();
        var count = Reasons.Length.ToString(CultureInfo.InvariantCulture);
        table.Append("@__kimi_reasons = private constant [").Append(count).Append(" x { ptr, i64 }] [\n");
        for (var i = 0; i < Reasons.Length; i++)
        {
            var reason = Reasons[i];
            if (reason.Id != i || !IsPlain(reason.Text))
            {
                throw new InvalidDataException("Abort reasons require contiguous indices and unescaped ASCII text.");
            }

            table.Append("  { ptr, i64 } { ptr @__kimi_").Append(reason.Name).Append("_reason, i64 ")
                .Append(reason.Text.Length.ToString(CultureInfo.InvariantCulture)).Append(" }").Append(i + 1 == Reasons.Length ? "\n" : ",\n");
            runtime = runtime.Replace("{{reason_" + reason.Name + "}}", reason.Id.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        table.Append("]\n");
        foreach (var reason in Reasons)
        {
            table.Append("@__kimi_").Append(reason.Name).Append("_reason = private constant [")
                .Append(reason.Text.Length.ToString(CultureInfo.InvariantCulture)).Append(" x i8] c\"").Append(reason.Text).Append("\"\n");
        }

        return runtime.Replace("{{abort_reasons}}", table.ToString(), StringComparison.Ordinal)
            .Replace("{{abort_reason_count}}", count, StringComparison.Ordinal);
    }

    /// <summary>Gets the reason of a fixed SPEC 22.5.2 code, whose text begins with the code and a colon.</summary>
    /// <param name="code">The code, such as KIMI_E_INDEX_BOUNDS.</param>
    /// <returns>The reason, or -1 for a code without one.</returns>
    internal static int Of(string code)
    {
        foreach (var reason in Reasons)
        {
            if (reason.Text.Length > code.Length && reason.Text[code.Length] == ':' && reason.Text.StartsWith(code, StringComparison.Ordinal))
            {
                return reason.Id;
            }
        }

        return -1;
    }

    // Whether the text is printable ASCII without " or \, so an LLVM c"..." constant holds it unescaped.
    private static bool IsPlain(string text)
    {
        foreach (var c in text)
        {
            if (c < ' ' || c > '~' || c is '"' or '\\')
            {
                return false;
            }
        }

        return true;
    }

    internal readonly record struct AbortReason(int Id, string Name, string Text);
}
