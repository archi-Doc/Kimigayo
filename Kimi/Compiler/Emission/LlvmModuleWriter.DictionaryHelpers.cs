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
                case DictionaryHelperKind.Find:
                    WriteDictionaryFind(output, helper);
                    break;
                case DictionaryHelperKind.Clear:
                    WriteDictionaryClear(output, helper);
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

    private static void DictionaryCopy(TextWriter output, string destination, string source, long size)
    {
        if (size == 0)
        {
            return;
        }

        output.Write("  call void @llvm.memcpy.p0.p0.i64(ptr ");
        output.Write(destination);
        output.Write(", ptr ");
        output.Write(source);
        output.Write(", i64 ");
        WriteNumber(output, size);
        output.Write(", i1 false)\n");
    }

    private static void DictionaryStore(TextWriter output, ValueLowering value, bool scalar, string source, string destination, string widened)
    {
        if (!scalar)
        {
            DictionaryCopy(output, destination, source, value.Layout.Size);
            return;
        }

        if (value.ComputationType != value.Layout.StorageType)
        {
            output.Write("  ");
            output.Write(widened);
            output.Write(" = zext ");
            output.Write(value.ComputationType);
            output.Write(' ');
            output.Write(source);
            output.Write(" to ");
            output.Write(value.Layout.StorageType);
            output.Write('\n');
            source = widened;
        }

        output.Write("  store ");
        output.Write(value.Layout.StorageType);
        output.Write(' ');
        output.Write(source);
        output.Write(", ptr ");
        output.Write(destination);
        output.Write(", align ");
        WriteNumber(output, value.Layout.Alignment);
        output.Write('\n');
    }

    private static void DictionaryDestroy(TextWriter output, AggregateLayout? layout, bool text, string address)
    {
        if (!text && layout?.NeedsDestruction != true)
        {
            return;
        }

        if (text)
        {
            output.Write("  call void @__kimi_destroy_string");
        }
        else
        {
            Name(output, "  call void @__kimi_drop_aggregate", layout!.Id);
        }

        output.Write("(ptr ");
        output.Write(address);
        output.Write(", ptr %location, i64 %location_length)\n");
    }

    private static void WriteDictionarySlot(TextWriter output, DictionaryHelper helper)
    {
        output.Write("  %index = sub i64 %link, 1\n  %offset = mul i64 %index, ");
        WriteNumber(output, helper.Stride);
        output.Write("\n  %slot = getelementptr i8, ptr %buffer, i64 %offset\n");
        DictionaryAddress(output, "%stored_key", "%slot", helper.KeyOffset);
        DictionaryAddress(output, "%stored_value", "%slot", helper.ValueOffset);
    }

    private static void WriteDictionaryFind(TextWriter output, DictionaryHelper helper)
    {
        output.Write("  %buffer = load ptr, ptr %handle, align 8\n");
        DictionaryAddress(output, "%head_ptr", "%handle", 32);
        output.Write("  %head = load i64, ptr %head_ptr, align 8\n  br label %test\ntest:\n  %link = phi i64 [ %head, %entry ], [ %next, %advance ]\n  %empty = icmp eq i64 %link, 0\n  br i1 %empty, label %absent, label %compare\ncompare:\n");
        WriteDictionarySlot(output, helper);
        output.Write("  %equal = call i1 @");
        output.Write(helper.Related!.Name);
        output.Write("(ptr %stored_key, ptr %key)\n  br i1 %equal, label %found, label %advance\nadvance:\n");
        DictionaryAddress(output, "%next_ptr", "%slot", 8);
        output.Write("  %next = load i64, ptr %next_ptr, align 8\n  br label %test\nfound:\n  ret i64 %link\nabsent:\n  ret i64 0\n");
    }

    private static void WriteDictionaryClear(TextWriter output, DictionaryHelper helper)
    {
        var destroy = helper.KeyIsString || helper.KeyLayout?.NeedsDestruction == true || helper.ValueIsString || helper.ValueLayout?.NeedsDestruction == true;
        if (destroy)
        {
            output.Write("  %buffer = load ptr, ptr %handle, align 8\n");
            DictionaryAddress(output, "%tail_ptr", "%handle", 40);
            output.Write("  %tail = load i64, ptr %tail_ptr, align 8\n  br label %test\ntest:\n  %link = phi i64 [ %tail, %entry ], [ %previous, %destroy ]\n  %empty = icmp eq i64 %link, 0\n  br i1 %empty, label %end, label %destroy\ndestroy:\n");
            WriteDictionarySlot(output, helper);
            output.Write("  %previous = load i64, ptr %slot, align 8\n");
            DictionaryDestroy(output, helper.ValueLayout, helper.ValueIsString, "%stored_value");
            DictionaryDestroy(output, helper.KeyLayout, helper.KeyIsString, "%stored_key");
            output.Write("  br label %test\nend:\n");
        }

        // Clear retains the allocation and makes all slots available without a scan.
        DictionaryAddress(output, "%used_ptr", "%handle", 8);
        DictionaryAddress(output, "%length_ptr", "%handle", 24);
        DictionaryAddress(output, "%head_reset", "%handle", 32);
        DictionaryAddress(output, "%tail_reset", "%handle", 40);
        DictionaryAddress(output, "%free_reset", "%handle", 48);
        output.Write("  store i64 0, ptr %used_ptr, align 8\n  store i64 0, ptr %length_ptr, align 8\n  store i64 0, ptr %head_reset, align 8\n  store i64 0, ptr %tail_reset, align 8\n  store i64 0, ptr %free_reset, align 8\n  ret void\n");
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
            DictionaryStore(output, helper.Key, true, "%key", "%key_slot", "%key_input");
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
                DictionaryStore(output, helper.Key, scalarKey, "%key", "%result_key", "%key_rejected");
                DictionaryStore(output, helper.Value, scalarValue, "%value", "%result_value", "%value_rejected");
            }
            else
            {
                DictionaryCopy(output, "%result_key", "%stored_key", helper.Key.Layout.Size);
                DictionaryCopy(output, "%result_value", "%stored_value", helper.Value.Layout.Size);
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
                DictionaryCopy(output, "%result_value", "%stored_value", helper.Value.Layout.Size);
                DictionaryStore(output, helper.Value, scalarValue, "%value", "%stored_value", "%value_replaced");
                DictionaryDestroy(output, helper.KeyLayout, helper.KeyIsString, "%key");
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
            DictionaryStore(output, helper.Key, scalarKey, "%key", "%new_key", "%key_inserted");
            DictionaryStore(output, helper.Value, scalarValue, "%value", "%new_value", "%value_inserted");
        }

        output.Write(helper.Kind == DictionaryHelperKind.TryInsert ? "  store i32 0, ptr %result, align 4\n" : "  store i32 1, ptr %result, align 4\n");
        output.Write("  ret void\n");
    }
}
