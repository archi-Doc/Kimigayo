// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    // The literal duplicate-key adapter keeps the later key's location; the capacity algorithms are DictionaryStorage source.
    private static readonly string DictionaryDuplicateAdapter = """
        @__kimi_dictionary_duplicate_table = private constant { ptr, ptr, ptr } { ptr @__kimi_dictionary_duplicate, ptr null, ptr null }, align 8
        define internal void @__kimi_dictionary_duplicate(i64 %environment, ptr %context) #0 {
        entry:
          %origin = inttoptr i64 %environment to ptr
          %location = load ptr, ptr %origin, align 8
          %length_ptr = getelementptr i8, ptr %origin, i64 8
          %location_length = load i64, ptr %length_ptr, align 8
          call void @__kimi_abort(i32 REASON_DUPLICATE, ptr %location, i64 %location_length, i64 -2)
          unreachable
        }

        """.Replace("REASON_DUPLICATE", Reason(WindowsLowering.DuplicateKeyReason), StringComparison.Ordinal);

    // SPEC 22.1.2.5, 22.5.2: the nullable form of Alloc returns null wherever Alloc would Abort, and the byte copy moves no
    // responsibility; neither has a location to report.
    private static readonly string StorageBytePrimitives = """
        define internal ptr @__kimi_try_allocate(i64 %bytes) #0 {
        entry:
          %invalid = icmp ugt i64 %bytes, 9223372036854775807
          br i1 %invalid, label %failed, label %heap
        heap:
          %process_heap = call ptr @GetProcessHeap()
          %no_heap = icmp eq ptr %process_heap, null
          br i1 %no_heap, label %failed, label %try
        try:
          %zero = icmp eq i64 %bytes, 0
          %actual = select i1 %zero, i64 1, i64 %bytes
          %memory = call ptr @HeapAlloc(ptr %process_heap, i32 0, i64 %actual)
          ret ptr %memory
        failed:
          ret ptr null
        }
        define internal void @__kimi_transfer_bytes(ptr %destination, ptr %source, i64 %size) #0 {
        entry:
          call void @llvm.memcpy.p0.p0.i64(ptr %destination, ptr %source, i64 %size, i1 false)
          ret void
        }

        """;

    private static void WriteDictionaryOrigin(TextWriter output)
        => output.Write("  %origin = alloca { ptr, i64 }, align 8\n  store ptr %location, ptr %origin, align 8\n  %length_ptr = getelementptr i8, ptr %origin, i64 8\n  store i64 %location_length, ptr %length_ptr, align 8\n  %environment = ptrtoint ptr %origin to i64\n");
}
