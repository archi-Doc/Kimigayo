// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    private static void WriteDictionaryHelpers(EmissionModule module, TextWriter output)
    {
        foreach (var helper in module.DictionaryHelpers)
        {
            output.Write(helper.Abi.GetDefinition(false));
            output.Write("entry:\n");
            switch (helper.Kind)
            {
                case DictionaryHelperKind.CheckKey:
                    WriteDictionaryKeyCheck(output, helper, module.SourceCalls[helper.Related!], module.DictionaryRequireAbsent!);
                    break;
                case DictionaryHelperKind.Place:
                    WriteDictionaryInsertion(output, helper, module.DictionaryAppend ?? throw new InvalidOperationException("Dictionary append source was not compiled."));
                    output.Write("  ret void\n");
                    break;
                case DictionaryHelperKind.Drop:
                    if (helper.Related is { } clear)
                    {
                        output.Write("  call void @");
                        output.Write(module.SourceCalls[clear].Name);
                        output.Write("(ptr %handle, ptr %location, i64 %location_length)\n");
                    }

                    output.Write("  call void @__kimi_array_free(ptr %handle, ptr %location, i64 %location_length)\n  ret void\n");
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

    private static void WriteDictionaryKeyStorage(TextWriter output, DictionaryHelper helper)
    {
        output.Write("  %key_slot = alloca ");
        output.Write(helper.Key.Layout.StorageType);
        output.Write(", align ");
        WriteNumber(output, helper.Key.Layout.Alignment);
        output.Write('\n');
        WriteStoredArgument(output, helper.Key, true, "%key", "%key_slot", "%key_input");
    }

    private static void WriteDictionaryKeyCheck(TextWriter output, DictionaryHelper helper, FunctionAbi find, FunctionAbi requireAbsent)
    {
        var scalar = helper.KeyLayout is null && !helper.KeyIsString && helper.Key.Layout.Size != 0;
        if (scalar)
        {
            WriteDictionaryKeyStorage(output, helper);
        }

        output.Write("  %link = call i64 @");
        output.Write(find.Name);
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
