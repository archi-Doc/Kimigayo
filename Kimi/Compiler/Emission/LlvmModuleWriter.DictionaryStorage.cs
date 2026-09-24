// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    // Links are slot index + 1; zero means absent. Reusing a free slot and maintaining
    // insertion order both take O(1), independently of the equality search.
    private static readonly string DictionaryStorageRuntime = """
        define internal ptr @__kimi_dictionary_append_slot(ptr %handle, i64 %stride, ptr %location, i64 %location_length) #0 {
        entry:
          %length_ptr = getelementptr i8, ptr %handle, i64 24
          %length = load i64, ptr %length_ptr, align 8
          %pair = call { i64, i1 } @llvm.sadd.with.overflow.i64(i64 %length, i64 1)
          %needed = extractvalue { i64, i1 } %pair, 0
          %overflow = extractvalue { i64, i1 } %pair, 1
          br i1 %overflow, label %failure, label %grow
        failure:
          call void @__kimi_abort(i32 REASON_OVERFLOW, ptr %location, i64 %location_length, i64 -2)
          unreachable
        grow:
          call void @__kimi_array_grow(ptr %handle, i64 %stride, i64 %needed, ptr %location, i64 %location_length)
          %buffer = load ptr, ptr %handle, align 8
          %free_ptr = getelementptr i8, ptr %handle, i64 48
          %free = load i64, ptr %free_ptr, align 8
          %unused = icmp eq i64 %free, 0
          br i1 %unused, label %extend, label %reuse
        extend:
          %used_ptr = getelementptr i8, ptr %handle, i64 8
          %used = load i64, ptr %used_ptr, align 8
          %advanced = add i64 %used, 1
          store i64 %advanced, ptr %used_ptr, align 8
          br label %place
        reuse:
          %free_index = sub i64 %free, 1
          %free_offset = mul i64 %free_index, %stride
          %free_slot = getelementptr i8, ptr %buffer, i64 %free_offset
          %next_free_ptr = getelementptr i8, ptr %free_slot, i64 8
          %next_free = load i64, ptr %next_free_ptr, align 8
          store i64 %next_free, ptr %free_ptr, align 8
          br label %place
        place:
          %index = phi i64 [ %used, %extend ], [ %free_index, %reuse ]
          %link = add i64 %index, 1
          %offset = mul i64 %index, %stride
          %slot = getelementptr i8, ptr %buffer, i64 %offset
          %next_ptr = getelementptr i8, ptr %slot, i64 8
          %tail_ptr = getelementptr i8, ptr %handle, i64 40
          %tail = load i64, ptr %tail_ptr, align 8
          store i64 %tail, ptr %slot, align 8
          store i64 0, ptr %next_ptr, align 8
          %empty = icmp eq i64 %tail, 0
          br i1 %empty, label %first, label %last
        first:
          %head_ptr = getelementptr i8, ptr %handle, i64 32
          store i64 %link, ptr %head_ptr, align 8
          br label %finish
        last:
          %tail_index = sub i64 %tail, 1
          %tail_offset = mul i64 %tail_index, %stride
          %tail_slot = getelementptr i8, ptr %buffer, i64 %tail_offset
          %tail_next = getelementptr i8, ptr %tail_slot, i64 8
          store i64 %link, ptr %tail_next, align 8
          br label %finish
        finish:
          store i64 %link, ptr %tail_ptr, align 8
          store i64 %needed, ptr %length_ptr, align 8
          ret ptr %slot
        }

        """.Replace("REASON_OVERFLOW", Reason(WindowsLowering.IntegerOverflowReason), StringComparison.Ordinal);

    private static void WriteDictionaryUnlink(EmissionModule module, TextWriter output)
    {
        var implementation = module.DictionaryUnlink ?? throw new InvalidOperationException("Dictionary unlink source was not compiled.");
        output.Write("define internal void @__kimi_dictionary_unlink(ptr %handle, i64 %stride, i64 %link) #0 {\nentry:\n  call void @");
        output.Write(implementation.Name);
        output.Write("(ptr %handle, i64 %stride, i64 %link)\n  ret void\n}\n\n");
    }
}
