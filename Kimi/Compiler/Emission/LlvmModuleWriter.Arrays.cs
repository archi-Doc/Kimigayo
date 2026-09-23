// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

/// <summary>SPEC 4.7.2, 4.7.6: per-element Array helpers over the {buffer, length, capacity} handle (Windows x64 profile).</summary>
internal static partial class LlvmModuleWriter
{
    private const string ArrayHelperPrologue = """
        entry:
          %length_ptr = getelementptr i8, ptr %handle, i64 8
          %length = load i64, ptr %length_ptr, align 8

        """;

    private static void WriteArrayHelpers(EmissionModule module, TextWriter output)
    {
        foreach (var helper in module.ArrayHelpers)
        {
            output.Write(helper.Abi.GetDefinition(false));
            output.Write(ArrayHelperPrologue);
            switch (helper.Kind)
            {
                case ArrayHelperKind.Append:
                    WriteArrayAppend(output, helper);
                    break;
                case ArrayHelperKind.Insert:
                    WriteArrayInsert(output, helper);
                    break;
                case ArrayHelperKind.Pop:
                    WriteArrayPop(output, helper);
                    break;
                case ArrayHelperKind.Remove:
                    WriteArrayRemove(output, helper);
                    break;
                default:
                    WriteArrayClear(output, helper, helper.Kind == ArrayHelperKind.Drop);
                    break;
            }

            output.Write("}\n");
        }
    }

    private static long Stride(ArrayHelper helper) => helper.Element.Layout.Stride;

    // Grows to hold one more element and places it at buffer + length * stride (SPEC 4.7.2, 4.7.7).
    private static void WriteArrayAppend(TextWriter output, ArrayHelper helper)
    {
        output.Write("  %needed = add i64 %length, 1\n  call void @__kimi_array_grow(ptr %handle, i64 ");
        WriteNumber(output, Stride(helper));
        output.Write(", i64 %needed, ptr %location, i64 %location_length)\n  %buffer = load ptr, ptr %handle, align 8\n  %offset = mul i64 %length, ");
        WriteNumber(output, Stride(helper));
        output.Write("\n  %slot = getelementptr i8, ptr %buffer, i64 %offset\n");
        WriteArrayElementStore(output, helper);
        output.Write("  store i64 %needed, ptr %length_ptr, align 8\n  ret void\n");
    }

    // Requires 0 <= index <= length, then shifts the tail up by one element before placing the value.
    private static void WriteArrayInsert(TextWriter output, ArrayHelper helper)
    {
        output.Write("  %negative = icmp slt i64 %index, 0\n  %beyond = icmp sgt i64 %index, %length\n  %invalid = or i1 %negative, %beyond\n  br i1 %invalid, label %bounds_failure, label %grow\ngrow:\n  %needed = add i64 %length, 1\n  call void @__kimi_array_grow(ptr %handle, i64 ");
        WriteNumber(output, Stride(helper));
        output.Write(", i64 %needed, ptr %location, i64 %location_length)\n  %buffer = load ptr, ptr %handle, align 8\n  %offset = mul i64 %index, ");
        WriteNumber(output, Stride(helper));
        output.Write("\n  %slot = getelementptr i8, ptr %buffer, i64 %offset\n  %next = getelementptr i8, ptr %slot, i64 ");
        WriteNumber(output, Stride(helper));
        output.Write("\n  %tail_count = sub i64 %length, %index\n  %tail_bytes = mul i64 %tail_count, ");
        WriteNumber(output, Stride(helper));
        output.Write("\n  call void @llvm.memmove.p0.p0.i64(ptr %next, ptr %slot, i64 %tail_bytes, i1 false)\n");
        WriteArrayElementStore(output, helper);
        output.Write("  store i64 %needed, ptr %length_ptr, align 8\n  ret void\n");
        WriteArrayBoundsFailure(output);
    }

    // Moves the last element into the Option payload with the Some tag, or writes the None tag (SPEC 4.7.2).
    private static void WriteArrayPop(TextWriter output, ArrayHelper helper)
    {
        var option = helper.Option ?? throw new InvalidOperationException("Array pop needs its Option layout.");
        output.Write("  %empty = icmp eq i64 %length, 0\n  br i1 %empty, label %none, label %some\nsome:\n  %last = sub i64 %length, 1\n  %buffer = load ptr, ptr %handle, align 8\n  %offset = mul i64 %last, ");
        WriteNumber(output, Stride(helper));
        output.Write("\n  %slot = getelementptr i8, ptr %buffer, i64 %offset\n  %payload = getelementptr i8, ptr %result, i64 ");
        WriteNumber(output, option.PayloadOffset);
        output.Write("\n  call void @llvm.memcpy.p0.p0.i64(ptr %payload, ptr %slot, i64 ");
        WriteNumber(output, Stride(helper));
        output.Write(", i1 false)\n  store i32 0, ptr %result, align 4\n  store i64 %last, ptr %length_ptr, align 8\n  ret void\nnone:\n  store i32 1, ptr %result, align 4\n  ret void\n");
    }

    // Requires 0 <= index < length, moves the element out and shifts the tail down (SPEC 4.7.2).
    private static void WriteArrayRemove(TextWriter output, ArrayHelper helper)
    {
        var scalar = helper.ElementLayout is null && !helper.ElementIsString;
        output.Write("  %negative = icmp slt i64 %index, 0\n  %beyond = icmp sge i64 %index, %length\n  %invalid = or i1 %negative, %beyond\n  br i1 %invalid, label %bounds_failure, label %remove\nremove:\n  %buffer = load ptr, ptr %handle, align 8\n  %offset = mul i64 %index, ");
        WriteNumber(output, Stride(helper));
        output.Write("\n  %slot = getelementptr i8, ptr %buffer, i64 %offset\n");
        if (scalar)
        {
            var storage = helper.Element.Layout.StorageType;
            var narrowed = storage != helper.Element.ComputationType;
            output.Write(narrowed ? "  %loaded = load " : "  %value = load ");
            output.Write(storage);
            output.Write(", ptr %slot, align ");
            WriteNumber(output, helper.Element.Layout.Alignment);
            output.Write('\n');
            if (narrowed)
            {
                output.Write("  %value = trunc ");
                output.Write(storage);
                output.Write(" %loaded to ");
                output.Write(helper.Element.ComputationType);
                output.Write('\n');
            }
        }
        else
        {
            output.Write("  call void @llvm.memcpy.p0.p0.i64(ptr %result, ptr %slot, i64 ");
            WriteNumber(output, Stride(helper));
            output.Write(", i1 false)\n");
        }

        output.Write("  %next = getelementptr i8, ptr %slot, i64 ");
        WriteNumber(output, Stride(helper));
        output.Write("\n  %last = sub i64 %length, 1\n  %tail_count = sub i64 %last, %index\n  %tail_bytes = mul i64 %tail_count, ");
        WriteNumber(output, Stride(helper));
        output.Write("\n  call void @llvm.memmove.p0.p0.i64(ptr %slot, ptr %next, i64 %tail_bytes, i1 false)\n  store i64 %last, ptr %length_ptr, align 8\n");
        output.Write(scalar ? "  ret " + helper.Element.ComputationType + " %value\n" : "  ret void\n");
        WriteArrayBoundsFailure(output);
    }

    // Destroys the elements in reverse index order and keeps the capacity; Drop then releases the buffer (SPEC 4.7.6).
    private static void WriteArrayClear(TextWriter output, ArrayHelper helper, bool release)
    {
        if (helper.ElementIsString || helper.ElementLayout?.NeedsDestruction == true)
        {
            output.Write("  %buffer = load ptr, ptr %handle, align 8\n  br label %test\ntest:\n  %remaining = phi i64 [ %length, %entry ], [ %index, %body ]\n  %done = icmp eq i64 %remaining, 0\n  br i1 %done, label %end, label %body\nbody:\n  %index = sub i64 %remaining, 1\n  %offset = mul i64 %index, ");
            WriteNumber(output, Stride(helper));
            output.Write("\n  %element = getelementptr i8, ptr %buffer, i64 %offset\n");
            if (helper.ElementIsString)
            {
                output.Write("  call void @__kimi_destroy_string(ptr %element, ptr %location, i64 %location_length)\n");
            }
            else
            {
                Name(output, "  call void @__kimi_drop_aggregate", helper.ElementLayout!.Id);
                output.Write("(ptr %element, ptr %location, i64 %location_length)\n");
            }

            output.Write("  br label %test\nend:\n");
        }

        output.Write("  store i64 0, ptr %length_ptr, align 8\n");
        if (release)
        {
            output.Write("  call void @__kimi_array_free(ptr %handle, ptr %location, i64 %location_length)\n");
        }

        output.Write("  ret void\n");
    }

    private static void WriteArrayElementStore(TextWriter output, ArrayHelper helper)
    {
        if (helper.ElementLayout is null && !helper.ElementIsString)
        {
            // Scalars keep their storage representation in the buffer (a bool is an i8 byte, SPEC 21.1.4).
            var storage = helper.Element.Layout.StorageType;
            var widened = storage != helper.Element.ComputationType;
            if (widened)
            {
                output.Write("  %stored = zext ");
                output.Write(helper.Element.ComputationType);
                output.Write(" %value to ");
                output.Write(storage);
                output.Write('\n');
            }

            output.Write("  store ");
            output.Write(storage);
            output.Write(widened ? " %stored, ptr %slot, align " : " %value, ptr %slot, align ");
            WriteNumber(output, helper.Element.Layout.Alignment);
            output.Write('\n');
        }
        else
        {
            output.Write("  call void @llvm.memcpy.p0.p0.i64(ptr %slot, ptr %value, i64 ");
            WriteNumber(output, Stride(helper));
            output.Write(", i1 false)\n");
        }
    }

    private static void WriteArrayBoundsFailure(TextWriter output)
    {
        output.Write("bounds_failure:\n  call void @__kimi_abort(i32 ");
        WriteNumber(output, WindowsLowering.IndexBoundsReason);
        output.Write(", ptr %location, i64 %location_length, i64 -2)\n  unreachable\n");
    }
}
