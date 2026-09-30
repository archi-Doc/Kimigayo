// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    // These adapters expose allocation/release/byte transfer to ordinary source.
    private static readonly string DictionaryMemoryAdapters = """
        @__kimi_dictionary_allocate_table = private constant { ptr, ptr, ptr } { ptr @__kimi_dictionary_allocate, ptr null, ptr null }, align 8
        @__kimi_dictionary_release_table = private constant { ptr, ptr, ptr } { ptr @__kimi_dictionary_release, ptr null, ptr null }, align 8
        @__kimi_dictionary_transfer_table = private constant { ptr, ptr, ptr } { ptr @__kimi_dictionary_transfer, ptr null, ptr null }, align 8
        @__kimi_dictionary_duplicate_table = private constant { ptr, ptr, ptr } { ptr @__kimi_dictionary_duplicate, ptr null, ptr null }, align 8
        @__kimi_dictionary_allocate_checked_table = private constant { ptr, ptr, ptr } { ptr @__kimi_dictionary_allocate_checked, ptr null, ptr null }, align 8
        @__kimi_dictionary_capacity_failure_table = private constant { ptr, ptr, ptr } { ptr @__kimi_dictionary_capacity_failure, ptr null, ptr null }, align 8
        define internal ptr @__kimi_dictionary_allocate_checked(i64 %environment, i64 %bytes, ptr %context) #0 {
        entry:
          %origin = inttoptr i64 %environment to ptr
          %location = load ptr, ptr %origin, align 8
          %length_ptr = getelementptr i8, ptr %origin, i64 8
          %location_length = load i64, ptr %length_ptr, align 8
          %memory = call ptr @__kimi_alloc(i64 %bytes, ptr %location, i64 %location_length)
          ret ptr %memory
        }
        define internal void @__kimi_dictionary_capacity_failure(i64 %environment, i32 %failure, ptr %context) #0 {
        entry:
          ; Translate the private CapacityFailure ABI codes, not the growth algorithm.
          %argument = icmp eq i32 %failure, 0
          %overflow = icmp eq i32 %failure, 1
          %range_reason = select i1 %overflow, i32 REASON_OVERFLOW, i32 REASON_SIZE
          %reason = select i1 %argument, i32 REASON_ARGUMENT, i32 %range_reason
          %origin = inttoptr i64 %environment to ptr
          %location = load ptr, ptr %origin, align 8
          %length_ptr = getelementptr i8, ptr %origin, i64 8
          %location_length = load i64, ptr %length_ptr, align 8
          call void @__kimi_abort(i32 %reason, ptr %location, i64 %location_length, i64 -2)
          unreachable
        }
        define internal void @__kimi_dictionary_duplicate(i64 %environment, ptr %context) #0 {
        entry:
          %origin = inttoptr i64 %environment to ptr
          %location = load ptr, ptr %origin, align 8
          %length_ptr = getelementptr i8, ptr %origin, i64 8
          %location_length = load i64, ptr %length_ptr, align 8
          call void @__kimi_abort(i32 REASON_DUPLICATE, ptr %location, i64 %location_length, i64 -2)
          unreachable
        }
        define internal ptr @__kimi_dictionary_allocate(i64 %environment, i64 %bytes, ptr %context) #0 {
        entry:
          %process_heap = call ptr @GetProcessHeap()
          %no_heap = icmp eq ptr %process_heap, null
          br i1 %no_heap, label %failed, label %try
        try:
          %memory = call ptr @HeapAlloc(ptr %process_heap, i32 0, i64 %bytes)
          ret ptr %memory
        failed:
          ret ptr null
        }
        define internal void @__kimi_dictionary_release(i64 %environment, ptr %buffer, ptr %context) #0 {
        entry:
          %origin = inttoptr i64 %environment to ptr
          %location = load ptr, ptr %origin, align 8
          %length_ptr = getelementptr i8, ptr %origin, i64 8
          %location_length = load i64, ptr %length_ptr, align 8
          call void @__kimi_free(ptr %buffer, ptr %location, i64 %location_length)
          ret void
        }
        define internal void @__kimi_dictionary_transfer(i64 %environment, ptr %destination, ptr %source, i64 %size, ptr %context) #0 {
        entry:
          call void @llvm.memcpy.p0.p0.i64(ptr %destination, ptr %source, i64 %size, i1 false)
          ret void
        }

        """.Replace("REASON_DUPLICATE", Reason(WindowsLowering.DuplicateKeyReason), StringComparison.Ordinal)
            .Replace("REASON_ARGUMENT", Reason(WindowsLowering.ArgumentReason), StringComparison.Ordinal)
            .Replace("REASON_OVERFLOW", Reason(WindowsLowering.IntegerOverflowReason), StringComparison.Ordinal)
            .Replace("REASON_SIZE", Reason(WindowsLowering.AllocationSizeReason), StringComparison.Ordinal);

    private static void WriteDictionaryCapacity(EmissionModule module, TextWriter output, bool append)
    {
        output.Write(append ? "define internal ptr @__kimi_dictionary_append_slot(ptr %handle, i64 %stride, ptr %location, i64 %location_length) #0 {\n" : WindowsLowering.DictionaryReserve.GetDefinition(false));
        output.Write("entry:\n");
        WriteDictionaryOrigin(output);
        WriteDictionaryCallbackHandle(output, "__kimi_dictionary_allocate_checked", "%allocate", "%environment");
        WriteDictionaryCallbackHandle(output, "__kimi_dictionary_release", "%release", "%environment");
        WriteDictionaryCallbackHandle(output, "__kimi_dictionary_transfer", "%transfer", "0");
        WriteDictionaryCallbackHandle(output, "__kimi_dictionary_capacity_failure", "%failure", "%environment");
        output.Write(append ? "  %result = call ptr @" : "  %result = call i64 @");
        output.Write((append ? module.DictionaryAppend : module.DictionaryReserveStorage)!.Name);
        output.Write("(ptr %handle, i64 %stride");
        if (!append)
        {
            output.Write(", i64 %additional");
        }

        output.Write(", i64 9223372036854775807, ptr %allocate, ptr %release, ptr %transfer, ptr %failure)\n");
        output.Write(append ? "  ret ptr %result\n}\n\n" : "  ret void\n}\n\n");
    }

    private static void WriteDictionaryShrink(EmissionModule module, TextWriter output)
    {
        output.Write(DictionaryMemoryAdapters);
        output.Write(WindowsLowering.DictionaryShrink.GetDefinition(false));
        output.Write("entry:\n");
        WriteDictionaryOrigin(output);
        WriteDictionaryCallbackHandle(output, "__kimi_dictionary_allocate", "%allocate", "0");
        WriteDictionaryCallbackHandle(output, "__kimi_dictionary_release", "%release", "%environment");
        WriteDictionaryCallbackHandle(output, "__kimi_dictionary_transfer", "%transfer", "0");
        output.Write("  call void @");
        output.Write(module.DictionaryShrink!.Name);
        output.Write("(ptr %handle, i64 %stride, ptr %allocate, ptr %release, ptr %transfer)\n  ret void\n}\n\n");
    }

    private static void WriteDictionaryOrigin(TextWriter output)
        => output.Write("  %origin = alloca { ptr, i64 }, align 8\n  store ptr %location, ptr %origin, align 8\n  %length_ptr = getelementptr i8, ptr %origin, i64 8\n  store i64 %location_length, ptr %length_ptr, align 8\n  %environment = ptrtoint ptr %origin to i64\n");
}
