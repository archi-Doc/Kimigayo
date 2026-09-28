// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    // ABI bridges only. Ordered-link algorithms are compiled from DictionaryStorage.kimi.
    // Capacity work retains the original operation's diagnostic context.
    private static readonly string DictionaryAppendCapacity = """
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
        """.Replace("REASON_OVERFLOW", Reason(WindowsLowering.IntegerOverflowReason), StringComparison.Ordinal);

    private static void WriteDictionaryStorage(EmissionModule module, TextWriter output)
    {
        if (module.DictionaryUnlink is not { } unlink || module.DictionaryAppendSlot is not { } append ||
            module.DictionaryInitialize is not { } initialize || module.DictionaryClearLinks is null)
        {
            throw new InvalidOperationException("Dictionary storage sources were not compiled.");
        }

        output.Write(DictionaryAppendCapacity);
        output.Write("\n  %slot = call ptr @");
        output.Write(append.Name);
        output.Write("(ptr %handle, i64 %stride, i64 %needed)\n  ret ptr %slot\n}\n\n");
        output.Write("define internal void @__kimi_dictionary_unlink(ptr %handle, i64 %stride, i64 %link) #0 {\nentry:\n  call void @");
        output.Write(unlink.Name);
        output.Write("(ptr %handle, i64 %stride, i64 %link)\n  ret void\n}\n\n");
        output.Write(WindowsLowering.DictionaryInit.GetDefinition(false));
        output.Write("entry:\n  call void @");
        output.Write(initialize.Name);
        output.Write("(ptr %handle)\n  ret void\n}\n\n");
    }
}
