// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    private static void WriteDictionaryHelpers(EmissionModule module, TextWriter output)
    {
        foreach (var helper in module.DictionaryHelpers)
        {
            if (helper.Kind == DictionaryHelperKind.Find)
            {
                WriteDictionaryEqualityAdapter(output, helper);
            }

            output.Write(helper.Abi.GetDefinition(false));
            output.Write("entry:\n");
            switch (helper.Kind)
            {
                case DictionaryHelperKind.Find:
                    WriteDictionaryFind(output, helper, module.DictionaryFind!);
                    break;
                case DictionaryHelperKind.Clear:
                    WriteDictionaryClear(output, helper, module.DictionaryClearLinks!);
                    break;
                case DictionaryHelperKind.Drop:
                    output.Write("  call void @");
                    output.Write(helper.Related!.Name);
                    output.Write("(ptr %handle, ptr %location, i64 %location_length)\n  call void @__kimi_array_free(ptr %handle, ptr %location, i64 %location_length)\n  ret void\n");
                    break;
                default:
                    WriteDictionarySearchOperation(output, helper);
                    break;
            }

            output.Write("}\n\n");
        }
    }

    private static void DictionaryAddress(TextWriter output, string name, string source, long offset)
    {
        output.Write("  ");
        output.Write(name);
        output.Write(" = getelementptr i8, ptr ");
        output.Write(source);
        output.Write(", i64 ");
        WriteNumber(output, offset);
        output.Write('\n');
    }

    private static void WriteDictionarySlot(TextWriter output, DictionaryHelper helper)
    {
        output.Write("  %index = sub i64 %link, 1\n  %offset = mul i64 %index, ");
        WriteNumber(output, helper.Stride);
        output.Write("\n  %slot = getelementptr i8, ptr %buffer, i64 %offset\n");
        DictionaryAddress(output, "%stored_key", "%slot", helper.KeyOffset);
        DictionaryAddress(output, "%stored_value", "%slot", helper.ValueOffset);
    }

    private static void WriteDictionaryEqualityAdapter(TextWriter output, DictionaryHelper helper)
    {
        // Adapt the verified equality witness to the ordinary Function Type ABI.
        // The handle has an empty environment and no drop action or heap allocation.
        output.Write('@');
        output.Write(helper.Abi.Name);
        output.Write("_table = private constant { ptr, ptr, ptr } { ptr @");
        output.Write(helper.Abi.Name);
        output.Write("_equals, ptr null, ptr null }, align 8\ndefine internal i1 @");
        output.Write(helper.Abi.Name);
        output.Write("_equals(i64 %environment, ptr %stored_key, ptr %key, ptr %context) #0 {\nentry:\n");
        output.Write("  %equal = call i1 @");
        output.Write(helper.Related!.Name);
        output.Write("(ptr %stored_key, ptr %key)\n  ret i1 %equal\n}\n\n");
    }

    private static void WriteDictionaryFind(TextWriter output, DictionaryHelper helper, FunctionAbi find)
    {
        output.Write("  %callback = alloca { i64, ptr }, align 8\n  store i64 0, ptr %callback, align 8\n  %table = getelementptr i8, ptr %callback, i64 8\n  store ptr @");
        output.Write(helper.Abi.Name);
        output.Write("_table, ptr %table, align 8\n  %link = call i64 @");
        output.Write(find.Name);
        output.Write("(ptr %handle, i64 ");
        WriteNumber(output, helper.Stride);
        output.Write(", i64 ");
        WriteNumber(output, helper.KeyOffset);
        output.Write(", ptr %key, ptr %callback)\n  ret i64 %link\n");
    }

    private static void WriteDictionaryClear(TextWriter output, DictionaryHelper helper, FunctionAbi clearLinks)
    {
        var destroy = helper.KeyIsString || helper.KeyLayout?.NeedsDestruction == true || helper.ValueIsString || helper.ValueLayout?.NeedsDestruction == true;
        if (destroy)
        {
            output.Write("  %buffer = load ptr, ptr %handle, align 8\n");
            DictionaryAddress(output, "%tail_ptr", "%handle", 40);
            output.Write("  %tail = load i64, ptr %tail_ptr, align 8\n  br label %test\ntest:\n  %link = phi i64 [ %tail, %entry ], [ %previous, %destroy ]\n  %empty = icmp eq i64 %link, 0\n  br i1 %empty, label %end, label %destroy\ndestroy:\n");
            WriteDictionarySlot(output, helper);
            output.Write("  %previous = load i64, ptr %slot, align 8\n");
            WriteStoredDestruction(output, helper.ValueLayout, helper.ValueIsString, "%stored_value");
            WriteStoredDestruction(output, helper.KeyLayout, helper.KeyIsString, "%stored_key");
            output.Write("  br label %test\nend:\n");
        }

        output.Write("  call void @");
        output.Write(clearLinks.Name);
        output.Write("(ptr %handle)\n  ret void\n");
    }

    private static void WriteDictionarySearchOperation(TextWriter output, DictionaryHelper helper)
    {
        var insertion = helper.Kind is DictionaryHelperKind.TryInsert or DictionaryHelperKind.InsertOrReplace;
        var scalarKey = helper.KeyLayout is null && !helper.KeyIsString && helper.Key.Layout.Size != 0;
        var scalarValue = helper.ValueLayout is null && !helper.ValueIsString && helper.Value.Layout.Size != 0;
        if (insertion && scalarKey)
        {
            output.Write("  %key_slot = alloca ");
            output.Write(helper.Key.Layout.StorageType);
            output.Write(", align ");
            WriteNumber(output, helper.Key.Layout.Alignment);
            output.Write('\n');
            WriteStoredArgument(output, helper.Key, true, "%key", "%key_slot", "%key_input");
        }

        output.Write("  %link = call i64 @");
        output.Write(helper.Related!.Name);
        output.Write("(ptr %handle, ptr ");
        output.Write(insertion && scalarKey ? "%key_slot" : "%key");
        output.Write(")\n  %missing = icmp eq i64 %link, 0\n  br i1 %missing, label %absent, label %found\nfound:\n  %buffer = load ptr, ptr %handle, align 8\n");
        WriteDictionarySlot(output, helper);
        var result = helper.Result!;
        if (helper.Kind is DictionaryHelperKind.TryInsert or DictionaryHelperKind.Remove)
        {
            var active = helper.Kind == DictionaryHelperKind.TryInsert ? 1 : 0;
            var pair = result.Cases![active].Children[0]!;
            DictionaryAddress(output, "%result_key", "%result", result.PayloadOffset + pair.Offset(0));
            DictionaryAddress(output, "%result_value", "%result", result.PayloadOffset + pair.Offset(1));
            if (insertion)
            {
                WriteStoredArgument(output, helper.Key, scalarKey, "%key", "%result_key", "%key_rejected");
                WriteStoredArgument(output, helper.Value, scalarValue, "%value", "%result_value", "%value_rejected");
            }
            else
            {
                WriteStoredCopy(output, "%result_key", "%stored_key", helper.Key.Layout.Size);
                WriteStoredCopy(output, "%result_value", "%stored_value", helper.Value.Layout.Size);
                output.Write("  call void @__kimi_dictionary_unlink(ptr %handle, i64 ");
                WriteNumber(output, helper.Stride);
                output.Write(", i64 %link)\n");
            }

            output.Write("  store i32 ");
            WriteNumber(output, active);
            output.Write(", ptr %result, align 4\n");
        }
        else
        {
            DictionaryAddress(output, "%result_value", "%result", result.PayloadOffset);
            if (helper.Kind == DictionaryHelperKind.TryGet)
            {
                output.Write("  store ptr %stored_value, ptr %result_value, align 8\n");
            }
            else
            {
                WriteStoredCopy(output, "%result_value", "%stored_value", helper.Value.Layout.Size);
                WriteStoredArgument(output, helper.Value, scalarValue, "%value", "%stored_value", "%value_replaced");
                WriteStoredDestruction(output, helper.KeyLayout, helper.KeyIsString, "%key");
            }

            output.Write("  store i32 0, ptr %result, align 4\n");
        }

        output.Write("  ret void\nabsent:\n");
        if (insertion)
        {
            output.Write("  %new_slot = call ptr @__kimi_dictionary_append_slot(ptr %handle, i64 ");
            WriteNumber(output, helper.Stride);
            output.Write(", ptr %location, i64 %location_length)\n");
            DictionaryAddress(output, "%new_key", "%new_slot", helper.KeyOffset);
            DictionaryAddress(output, "%new_value", "%new_slot", helper.ValueOffset);
            WriteStoredArgument(output, helper.Key, scalarKey, "%key", "%new_key", "%key_inserted");
            WriteStoredArgument(output, helper.Value, scalarValue, "%value", "%new_value", "%value_inserted");
        }

        output.Write(helper.Kind == DictionaryHelperKind.TryInsert ? "  store i32 0, ptr %result, align 4\n" : "  store i32 1, ptr %result, align 4\n");
        output.Write("  ret void\n");
    }
}
