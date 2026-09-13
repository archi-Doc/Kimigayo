// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text;

namespace Kimi.Compiler;

internal static partial class WindowsLowering
{
    internal const int StdoutReason = 0;
    internal const int AllocationSizeReason = 1;
    internal const int ProcessHeapReason = 2;
    internal const int AllocationReason = 3;
    internal const int FreeReason = 4;
    internal const int StringReleaseReason = 5;
    internal const int IntegerOverflowReason = 6;
    internal const int IntegerDivisionZeroReason = 7;

    // Indices are stable internal ABI values. The template's table, lengths and call sites
    // are expanded once from these records; the warm writer only copies the resulting text.
    internal static ReadOnlySpan<AbortReason> AbortReasons => Reasons;

    private static readonly AbortReason[] Reasons =
    [
        new(StdoutReason, "stdout", "KIMI_E_STDOUT: Failed to write to stdout"),
        new(AllocationSizeReason, "size", "KIMI_E_ALLOC_SIZE: Allocation size exceeds limit"),
        new(ProcessHeapReason, "heap", "KIMI_E_PROCESS_HEAP: Failed to obtain process heap"),
        new(AllocationReason, "alloc", "KIMI_E_ALLOC: Failed to allocate memory"),
        new(FreeReason, "free", "KIMI_E_FREE: Failed to free memory"),
        new(StringReleaseReason, "release", "KIMI_E_STRING_RELEASE: Invalid string release kind"),
        new(IntegerOverflowReason, "overflow", "KIMI_E_INT_OVERFLOW: Integer overflow"),
        new(IntegerDivisionZeroReason, "division_zero", "KIMI_E_INT_DIV_ZERO: Integer division or remainder by zero"),
    ];

    internal static string ExpandAbortReasons(string runtime)
    {
        var table = new StringBuilder();
        var count = Reasons.Length.ToString(CultureInfo.InvariantCulture);
        table.Append("@__kimi_reasons = private constant [").Append(count).Append(" x { ptr, i64 }] [\n");
        for (var i = 0; i < Reasons.Length; i++)
        {
            var reason = Reasons[i];
            if (reason.Id != i || reason.Text.Any(c => c < ' ' || c > '~' || c is '"' or '\\'))
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

    internal readonly record struct AbortReason(int Id, string Name, string Text);
}
