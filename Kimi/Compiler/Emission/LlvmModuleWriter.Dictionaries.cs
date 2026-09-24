// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    // The first three words are the common growable buffer prefix. Dictionary's
    // fourth word is its live count; subsequent words hold insertion and free links.
    private static readonly string DictionaryRuntime = WindowsLowering.DictionaryInit.GetDefinition(false) + """
        entry:
          store ptr null, ptr %handle, align 8
          %used = getelementptr i8, ptr %handle, i64 8
          store i64 0, ptr %used, align 8
          %capacity = getelementptr i8, ptr %handle, i64 16
          store i64 0, ptr %capacity, align 8
          %length = getelementptr i8, ptr %handle, i64 24
          store i64 0, ptr %length, align 8
          %head = getelementptr i8, ptr %handle, i64 32
          store i64 0, ptr %head, align 8
          %tail = getelementptr i8, ptr %handle, i64 40
          store i64 0, ptr %tail, align 8
          %free = getelementptr i8, ptr %handle, i64 48
          store i64 0, ptr %free, align 8
          ret void
        }

        """ + WindowsLowering.DictionaryReserve.GetDefinition(false) + ArrayReserveBody
            .Replace("ptr %handle, i64 8", "ptr %handle, i64 24", StringComparison.Ordinal)
            .Replace("REASON_ARGUMENT", Reason(WindowsLowering.ArgumentReason), StringComparison.Ordinal)
            .Replace("REASON_OVERFLOW", Reason(WindowsLowering.IntegerOverflowReason), StringComparison.Ordinal);
}
