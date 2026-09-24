// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    // Allocation is the platform boundary. Compaction itself is ordinary Kimigayo.
    private const string DictionaryShrinkAllocation = """
        entry:
          %length_ptr = getelementptr i8, ptr %handle, i64 24
          %length = load i64, ptr %length_ptr, align 8
          %capacity_ptr = getelementptr i8, ptr %handle, i64 16
          %capacity = load i64, ptr %capacity_ptr, align 8
          %fits = icmp sle i64 %capacity, %length
          br i1 %fits, label %done, label %shrink
        shrink:
          %buffer = load ptr, ptr %handle, align 8
          %empty = icmp eq i64 %length, 0
          br i1 %empty, label %release, label %heap
        heap:
          %process_heap = call ptr @GetProcessHeap()
          %no_heap = icmp eq ptr %process_heap, null
          br i1 %no_heap, label %done, label %try
        try:
          %bytes = mul i64 %length, %stride
          %memory = call ptr @HeapAlloc(ptr %process_heap, i32 0, i64 %bytes)
          %failed = icmp eq ptr %memory, null
          br i1 %failed, label %done, label %prepare
        prepare:
        """;

    private const string DictionaryShrinkRelease = """
        release:
          call void @__kimi_free(ptr %buffer, ptr %location, i64 %location_length)
          call void @__kimi_dictionary_init(ptr %handle, ptr %location, i64 %location_length)
          br label %done
        done:
          ret void
        }

        """;

    // The first three words are the common growable buffer prefix. Dictionary's
    // fourth word is its live count; subsequent words hold insertion and free links.
    private static readonly string DictionaryRuntime = WindowsLowering.DictionaryReserve.GetDefinition(false) + ArrayReserveBody
            .Replace("ptr %handle, i64 8", "ptr %handle, i64 24", StringComparison.Ordinal)
            .Replace("REASON_ARGUMENT", Reason(WindowsLowering.ArgumentReason), StringComparison.Ordinal)
            .Replace("REASON_OVERFLOW", Reason(WindowsLowering.IntegerOverflowReason), StringComparison.Ordinal);

    private static void WriteDictionaryShrink(EmissionModule module, TextWriter output)
    {
        output.Write("@__kimi_dictionary_transfer_table = private constant { ptr, ptr, ptr } { ptr @__kimi_dictionary_transfer, ptr null, ptr null }, align 8\ndefine internal void @__kimi_dictionary_transfer(i64 %environment, ptr %destination, ptr %source, i64 %size, ptr %context) #0 {\nentry:\n  call void @llvm.memcpy.p0.p0.i64(ptr %destination, ptr %source, i64 %size, i1 false)\n  ret void\n}\n\n");
        output.Write(WindowsLowering.DictionaryShrink.GetDefinition(false));
        output.Write(DictionaryShrinkAllocation);
        output.Write("\n  %callback = alloca { i64, ptr }, align 8\n  store i64 0, ptr %callback, align 8\n  %table = getelementptr i8, ptr %callback, i64 8\n  store ptr @__kimi_dictionary_transfer_table, ptr %table, align 8\n  %old = call ptr @");
        output.Write(module.DictionaryCompact!.Name);
        output.Write("(ptr %handle, ptr %memory, i64 %stride, ptr %callback)\n  call void @__kimi_free(ptr %old, ptr %location, i64 %location_length)\n  br label %done\n");
        output.Write(DictionaryShrinkRelease);
    }
}
