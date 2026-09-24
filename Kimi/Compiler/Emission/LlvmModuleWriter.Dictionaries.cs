// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    // Allocate a candidate before touching the old buffer. Linear compaction preserves
    // insertion order and transfers bytes without equality or destruction callbacks.
    private const string DictionaryShrinkBody = """
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
          %head_ptr = getelementptr i8, ptr %handle, i64 32
          %head = load i64, ptr %head_ptr, align 8
          br label %copy
        copy:
          %link = phi i64 [ %head, %prepare ], [ %next, %copy ]
          %position = phi i64 [ 0, %prepare ], [ %advanced, %copy ]
          %index = sub i64 %link, 1
          %offset = mul i64 %index, %stride
          %slot = getelementptr i8, ptr %buffer, i64 %offset
          %next_ptr = getelementptr i8, ptr %slot, i64 8
          %next = load i64, ptr %next_ptr, align 8
          %destination_offset = mul i64 %position, %stride
          %destination = getelementptr i8, ptr %memory, i64 %destination_offset
          call void @llvm.memcpy.p0.p0.i64(ptr %destination, ptr %slot, i64 %stride, i1 false)
          %advanced = add i64 %position, 1
          %last = icmp eq i64 %advanced, %length
          %following = add i64 %advanced, 1
          %next_link = select i1 %last, i64 0, i64 %following
          %destination_next = getelementptr i8, ptr %destination, i64 8
          store i64 %position, ptr %destination, align 8
          store i64 %next_link, ptr %destination_next, align 8
          br i1 %last, label %replace, label %copy
        replace:
          call void @__kimi_free(ptr %buffer, ptr %location, i64 %location_length)
          store ptr %memory, ptr %handle, align 8
          store i64 %length, ptr %capacity_ptr, align 8
          %used_ptr = getelementptr i8, ptr %handle, i64 8
          store i64 %length, ptr %used_ptr, align 8
          store i64 1, ptr %head_ptr, align 8
          %tail_ptr = getelementptr i8, ptr %handle, i64 40
          store i64 %length, ptr %tail_ptr, align 8
          %free_ptr = getelementptr i8, ptr %handle, i64 48
          store i64 0, ptr %free_ptr, align 8
          br label %done
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
            .Replace("REASON_OVERFLOW", Reason(WindowsLowering.IntegerOverflowReason), StringComparison.Ordinal) +
        WindowsLowering.DictionaryShrink.GetDefinition(false) + DictionaryShrinkBody;
}
