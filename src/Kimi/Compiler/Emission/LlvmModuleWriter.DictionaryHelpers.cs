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
                    WriteDictionaryInsertion(output, helper, module.DictionaryAppend ?? throw new InvalidOperationException("Dictionary append source was not compiled."));
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
                    throw new InvalidOperationException("Unknown Dictionary helper kind.");
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

    private static void WriteDictionaryInsertion(TextWriter output, DictionaryHelper helper, FunctionAbi append)
    {
        // Typed placement only; the ordinary source append body owns growth, ordering and free-slot reuse, and reports its
        // failures at the forwarded caller location.
        output.Write("  %new_slot = call ptr @");
        output.Write(append.Name);
        output.Write("(ptr %handle, i64 ");
        WriteNumber(output, helper.Stride);
        output.Write(", ptr %location, i64 %location_length)\n");
        DictionaryAddress(output, "%new_key", "%new_slot", helper.KeyOffset);
        DictionaryAddress(output, "%new_value", "%new_slot", helper.ValueOffset);
        WriteStoredArgument(output, helper.Key, helper.KeyLayout is null && !helper.KeyIsString && helper.Key.Layout.Size != 0, "%key", "%new_key", "%key_inserted");
        WriteStoredArgument(output, helper.Value, helper.ValueLayout is null && !helper.ValueIsString && helper.Value.Layout.Size != 0, "%value", "%new_value", "%value_inserted");
    }
}
