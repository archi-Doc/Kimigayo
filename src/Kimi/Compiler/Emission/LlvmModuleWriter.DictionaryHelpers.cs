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
            else if (helper.Kind == DictionaryHelperKind.Clear && DictionaryNeedsDestruction(helper))
            {
                WriteDictionaryDestructionAdapter(output, helper);
            }

            output.Write(helper.Abi.GetDefinition(false));
            output.Write("entry:\n");
            switch (helper.Kind)
            {
                case DictionaryHelperKind.CheckKey:
                    WriteDictionaryKeyCheck(output, helper, module.DictionaryRequireAbsent!);
                    break;
                case DictionaryHelperKind.Place:
                    WriteDictionaryInsertion(output, helper);
                    output.Write("  ret void\n");
                    break;
                case DictionaryHelperKind.Find:
                    WriteDictionaryFind(output, helper, module.DictionaryFind!);
                    break;
                case DictionaryHelperKind.Clear:
                    WriteDictionaryClear(output, helper, module.DictionaryClear!, module.DictionaryClearLinks!);
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
        WriteDictionaryCallbackTable(output, helper, "equals");
        output.Write("define internal i1 @");
        output.Write(helper.Abi.Name);
        output.Write("_equals(i64 %environment, ptr %stored_key, ptr %key, ptr %context) #0 {\nentry:\n");
        output.Write("  %equal = call i1 @");
        output.Write(helper.Related!.Name);
        output.Write("(ptr %stored_key, ptr %key)\n  ret i1 %equal\n}\n\n");
    }

    private static void WriteDictionaryCallbackTable(TextWriter output, DictionaryHelper helper, string entry)
    {
        output.Write('@');
        output.Write(helper.Abi.Name);
        output.Write("_table = private constant { ptr, ptr, ptr } { ptr @");
        output.Write(helper.Abi.Name);
        output.Write('_');
        output.Write(entry);
        output.Write(", ptr null, ptr null }, align 8\n");
    }

    private static void WriteDictionaryCallbackHandle(TextWriter output, string table, string handle, string environment)
    {
        output.Write("  ");
        output.Write(handle);
        output.Write(" = alloca { i64, ptr }, align 8\n  store i64 ");
        output.Write(environment);
        output.Write(", ptr ");
        output.Write(handle);
        output.Write(", align 8\n  ");
        output.Write(handle);
        output.Write("_table = getelementptr i8, ptr ");
        output.Write(handle);
        output.Write(", i64 8\n  store ptr @");
        output.Write(table);
        output.Write("_table, ptr ");
        output.Write(handle);
        output.Write("_table, align 8\n");
    }

    private static void WriteDictionaryFind(TextWriter output, DictionaryHelper helper, FunctionAbi find)
    {
        WriteDictionaryCallbackHandle(output, helper.Abi.Name, "%callback", "0");
        output.Write("  %link = call i64 @");
        output.Write(find.Name);
        output.Write("(ptr %handle, i64 ");
        WriteNumber(output, helper.Stride);
        output.Write(", i64 ");
        WriteNumber(output, helper.KeyOffset);
        output.Write(", ptr %key, ptr %callback)\n  ret i64 %link\n");
    }

    private static bool DictionaryNeedsDestruction(DictionaryHelper helper)
        => helper.KeyIsString || helper.KeyLayout?.NeedsDestruction == true || helper.ValueIsString || helper.ValueLayout?.NeedsDestruction == true;

    private static void WriteDictionaryDestructionAdapter(TextWriter output, DictionaryHelper helper)
    {
        WriteDictionaryCallbackTable(output, helper, "destroy");
        output.Write("define internal void @");
        output.Write(helper.Abi.Name);
        output.Write("_destroy(i64 %environment, ptr %stored_key, ptr %stored_value, ptr %context) #0 {\nentry:\n  %origin = inttoptr i64 %environment to ptr\n  %location = load ptr, ptr %origin, align 8\n");
        DictionaryAddress(output, "%length_ptr", "%origin", 8);
        output.Write("  %location_length = load i64, ptr %length_ptr, align 8\n");
        WriteStoredDestruction(output, helper.ValueLayout, helper.ValueIsString, "%stored_value");
        WriteStoredDestruction(output, helper.KeyLayout, helper.KeyIsString, "%stored_key");
        output.Write("  ret void\n}\n\n");
    }

    private static void WriteDictionaryClear(TextWriter output, DictionaryHelper helper, FunctionAbi clear, FunctionAbi clearLinks)
    {
        if (DictionaryNeedsDestruction(helper))
        {
            WriteDictionaryOrigin(output);
            WriteDictionaryCallbackHandle(output, helper.Abi.Name, "%callback", "%environment");
            output.Write("  call void @");
            output.Write(clear.Name);
            output.Write("(ptr %handle, i64 ");
            WriteNumber(output, helper.Stride);
            output.Write(", i64 ");
            WriteNumber(output, helper.KeyOffset);
            output.Write(", i64 ");
            WriteNumber(output, helper.ValueOffset);
            output.Write(", ptr %callback)\n  ret void\n");
            return;
        }

        output.Write("  call void @");
        output.Write(clearLinks.Name);
        output.Write("(ptr %handle)\n  ret void\n");
    }

    private static void WriteDictionarySearchOperation(TextWriter output, DictionaryHelper helper)
    {
        var scalarKey = helper.KeyLayout is null && !helper.KeyIsString && helper.Key.Layout.Size != 0;
        var scalarValue = helper.ValueLayout is null && !helper.ValueIsString && helper.Value.Layout.Size != 0;
        if (scalarKey)
        {
            WriteDictionaryKeyStorage(output, helper);
        }

        output.Write("  %link = call i64 @");
        output.Write(helper.Related!.Name);
        output.Write("(ptr %handle, ptr ");
        output.Write(scalarKey ? "%key_slot" : "%key");
        output.Write(")\n  %missing = icmp eq i64 %link, 0\n  br i1 %missing, label %absent, label %found\nfound:\n  %buffer = load ptr, ptr %handle, align 8\n");
        WriteDictionarySlot(output, helper);
        var result = helper.Result!;
        if (helper.Kind == DictionaryHelperKind.TryInsert)
        {
            var pair = result.Cases![1].Children[0]!;
            DictionaryAddress(output, "%result_key", "%result", result.PayloadOffset + pair.Offset(0));
            DictionaryAddress(output, "%result_value", "%result", result.PayloadOffset + pair.Offset(1));
            WriteStoredArgument(output, helper.Key, scalarKey, "%key", "%result_key", "%key_rejected");
            WriteStoredArgument(output, helper.Value, scalarValue, "%value", "%result_value", "%value_rejected");
            output.Write("  store i32 1, ptr %result, align 4\n");
        }
        else
        {
            DictionaryAddress(output, "%result_value", "%result", result.PayloadOffset);
            WriteStoredCopy(output, "%result_value", "%stored_value", helper.Value.Layout.Size);
            WriteStoredArgument(output, helper.Value, scalarValue, "%value", "%stored_value", "%value_replaced");
            WriteStoredDestruction(output, helper.KeyLayout, helper.KeyIsString, "%key");

            output.Write("  store i32 0, ptr %result, align 4\n");
        }

        output.Write("  ret void\nabsent:\n");
        WriteDictionaryInsertion(output, helper);

        output.Write(helper.Kind == DictionaryHelperKind.TryInsert ? "  store i32 0, ptr %result, align 4\n" : "  store i32 1, ptr %result, align 4\n");
        output.Write("  ret void\n");
    }

    private static void WriteDictionaryKeyStorage(TextWriter output, DictionaryHelper helper)
    {
        output.Write("  %key_slot = alloca ");
        output.Write(helper.Key.Layout.StorageType);
        output.Write(", align ");
        WriteNumber(output, helper.Key.Layout.Alignment);
        output.Write('\n');
        WriteStoredArgument(output, helper.Key, true, "%key", "%key_slot", "%key_input");
    }

    private static void WriteDictionaryKeyCheck(TextWriter output, DictionaryHelper helper, FunctionAbi requireAbsent)
    {
        var scalar = helper.KeyLayout is null && !helper.KeyIsString && helper.Key.Layout.Size != 0;
        if (scalar)
        {
            WriteDictionaryKeyStorage(output, helper);
        }

        output.Write("  %link = call i64 @");
        output.Write(helper.Related!.Name);
        output.Write("(ptr %handle, ptr ");
        output.Write(scalar ? "%key_slot" : "%key");
        output.Write(")\n");
        WriteDictionaryOrigin(output);
        WriteDictionaryCallbackHandle(output, "__kimi_dictionary_duplicate", "%duplicate", "%environment");
        output.Write("  call void @");
        output.Write(requireAbsent.Name);
        output.Write("(i64 %link, ptr %duplicate)\n  ret void\n");
    }

    private static void WriteDictionaryInsertion(TextWriter output, DictionaryHelper helper)
    {
        // Typed placement only; the ordinary source appendSlot body owns ordering and free-slot reuse.
        output.Write("  %new_slot = call ptr @__kimi_dictionary_append_slot(ptr %handle, i64 ");
        WriteNumber(output, helper.Stride);
        output.Write(", ptr %location, i64 %location_length)\n");
        DictionaryAddress(output, "%new_key", "%new_slot", helper.KeyOffset);
        DictionaryAddress(output, "%new_value", "%new_slot", helper.ValueOffset);
        WriteStoredArgument(output, helper.Key, helper.KeyLayout is null && !helper.KeyIsString && helper.Key.Layout.Size != 0, "%key", "%new_key", "%key_inserted");
        WriteStoredArgument(output, helper.Value, helper.ValueLayout is null && !helper.ValueIsString && helper.Value.Layout.Size != 0, "%value", "%new_value", "%value_inserted");
    }
}
