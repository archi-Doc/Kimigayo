// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

/// <summary>SPEC 4.7.2, 4.7.6: per-element Array helpers over the {buffer, length, capacity} handle (Windows x64 profile).</summary>
internal static partial class LlvmModuleWriter
{
    private const string ArrayGrowBody = """
        entry:
          %capacity_ptr = getelementptr i8, ptr %handle, i64 16
          %capacity = load i64, ptr %capacity_ptr, align 8
          %enough = icmp sge i64 %capacity, %minimum
          br i1 %enough, label %done, label %grow
        grow:
          ; SPEC 4.7.4: optional growth rounding cannot reject a representable minimum.
          %maximum = udiv i64 9223372036854775807, %stride
          %unrepresentable = icmp ugt i64 %minimum, %maximum
          br i1 %unrepresentable, label %size_failure, label %prepare
        prepare:
          ; SPEC 4.7.7: amortized doubling, clamped to the maximum representable capacity.
          %doubled = shl i64 %capacity, 1
          %wrapped = icmp slt i64 %doubled, %capacity
          %safe_doubled = select i1 %wrapped, i64 9223372036854775807, i64 %doubled
          %small = icmp slt i64 %safe_doubled, 4
          %base = select i1 %small, i64 4, i64 %safe_doubled
          %below = icmp slt i64 %base, %minimum
          %requested = select i1 %below, i64 %minimum, i64 %base
          %overshoot = icmp ugt i64 %requested, %maximum
          %target = select i1 %overshoot, i64 %maximum, i64 %requested
          %bytes = mul i64 %target, %stride
          br label %allocate
        allocate:
          %memory = call ptr @__kimi_alloc(i64 %bytes, ptr %location, i64 %location_length)
          %buffer = load ptr, ptr %handle, align 8
          %length_ptr = getelementptr i8, ptr %handle, i64 8
          %length = load i64, ptr %length_ptr, align 8
          %empty = icmp eq i64 %length, 0
          br i1 %empty, label %replace, label %copy
        copy:
          %used = mul i64 %length, %stride
          call void @llvm.memcpy.p0.p0.i64(ptr %memory, ptr %buffer, i64 %used, i1 false)
          br label %replace
        replace:
          call void @__kimi_free(ptr %buffer, ptr %location, i64 %location_length)
          store ptr %memory, ptr %handle, align 8
          store i64 %target, ptr %capacity_ptr, align 8
          br label %done
        size_failure:
          call void @__kimi_abort(i32 REASON_SIZE, ptr %location, i64 %location_length, i64 -2)
          unreachable
        done:
          ret void
        }

        """;

    private const string ArrayReserveBody = """
        entry:
          ; SPEC 4.7.4: a nonnegative additional count; the checked total is the minimum capacity.
          %negative = icmp slt i64 %additional, 0
          br i1 %negative, label %argument_failure, label %total
        total:
          %length_ptr = getelementptr i8, ptr %handle, i64 8
          %length = load i64, ptr %length_ptr, align 8
          %pair = call { i64, i1 } @llvm.sadd.with.overflow.i64(i64 %length, i64 %additional)
          %minimum = extractvalue { i64, i1 } %pair, 0
          %overflow = extractvalue { i64, i1 } %pair, 1
          br i1 %overflow, label %overflow_failure, label %grow
        grow:
          call void @__kimi_array_grow(ptr %handle, i64 %stride, i64 %minimum, ptr %location, i64 %location_length)
          ret void
        argument_failure:
          call void @__kimi_abort(i32 REASON_ARGUMENT, ptr %location, i64 %location_length, i64 -2)
          unreachable
        overflow_failure:
          call void @__kimi_abort(i32 REASON_OVERFLOW, ptr %location, i64 %location_length, i64 -2)
          unreachable
        }

        """;

    private const string ArrayShrinkBody = """
        entry:
          ; SPEC 4.7.4: a failed candidate allocation leaves the handle unchanged.
          %length_ptr = getelementptr i8, ptr %handle, i64 8
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
          br i1 %failed, label %done, label %move
        move:
          call void @llvm.memcpy.p0.p0.i64(ptr %memory, ptr %buffer, i64 %bytes, i1 false)
          call void @__kimi_free(ptr %buffer, ptr %location, i64 %location_length)
          store ptr %memory, ptr %handle, align 8
          store i64 %length, ptr %capacity_ptr, align 8
          br label %done
        release:
          call void @__kimi_free(ptr %buffer, ptr %location, i64 %location_length)
          store ptr null, ptr %handle, align 8
          store i64 0, ptr %capacity_ptr, align 8
          br label %done
        done:
          ret void
        }

        """;

    private static string Reason(int reason) => reason.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private const string ArrayHelperPrologue = """
        entry:
          %length_ptr = getelementptr i8, ptr %handle, i64 8
          %length = load i64, ptr %length_ptr, align 8

        """;

    // SPEC 4.7.4, 4.7.7: the capacity routines, emitted only for modules that use Arrays so string-only modules keep no memcpy.
    private static readonly string ArrayRuntime =
        WindowsLowering.ArrayGrow.GetDefinition(false) + ArrayGrowBody.Replace("REASON_SIZE", Reason(WindowsLowering.AllocationSizeReason), StringComparison.Ordinal) +
        WindowsLowering.ArrayReserve.GetDefinition(false) + ArrayReserveBody.Replace("REASON_ARGUMENT", Reason(WindowsLowering.ArgumentReason), StringComparison.Ordinal).Replace("REASON_OVERFLOW", Reason(WindowsLowering.IntegerOverflowReason), StringComparison.Ordinal) +
        WindowsLowering.ArrayShrink.GetDefinition(false) + ArrayShrinkBody;

    private static void WriteArrayHelpers(EmissionModule module, TextWriter output)
    {
        foreach (var helper in module.ArrayHelpers)
        {
            output.Write(helper.Abi.GetDefinition(false));
            output.Write(ArrayHelperPrologue);
            if (helper.Kind is ArrayHelperKind.InsertIndex or ArrayHelperKind.RemoveIndex)
            {
                output.Write("  %index_offset = load i64, ptr %index_value, align 8\n  %direction_ptr = getelementptr i8, ptr %index_value, i64 8\n  %direction = load i8, ptr %direction_ptr, align 1\n  %from_end = icmp ne i8 %direction, 0\n  br i1 %from_end, label %resolve_end, label %resolve_start\nresolve_end:\n  %backward = sub i64 %length, %index_offset\n  br label %resolved\nresolve_start:\n  br label %resolved\nresolved:\n  %index = phi i64 [ %backward, %resolve_end ], [ %index_offset, %resolve_start ]\n");
            }

            switch (helper.Kind)
            {
                case ArrayHelperKind.Append:
                    WriteArrayAppend(output, helper);
                    break;
                case ArrayHelperKind.Insert:
                case ArrayHelperKind.InsertIndex:
                    WriteArrayInsert(output, helper);
                    break;
                case ArrayHelperKind.Pop:
                    WriteArrayPop(output, helper);
                    break;
                case ArrayHelperKind.Remove:
                case ArrayHelperKind.RemoveIndex:
                    WriteArrayRemove(output, helper);
                    break;
                case ArrayHelperKind.Take:
                    WriteArrayRemove(output, helper, iterator: true);
                    break;
                case ArrayHelperKind.Place:
                    WriteArrayPlace(output, helper);
                    break;
                default:
                    WriteArrayClear(output, helper, helper.Kind is ArrayHelperKind.Drop or ArrayHelperKind.IteratorDrop);
                    break;
            }

            output.Write("}\n");
        }
    }

    private static long Stride(ArrayHelper helper) => helper.Element.Layout.Stride;

    // Grows to hold one more element and places it at buffer + length * stride (SPEC 4.7.2, 4.7.7).
    private static void WriteArrayAppend(TextWriter output, ArrayHelper helper)
    {
        WriteArrayIncrease(output);
        output.Write("  call void @__kimi_array_grow(ptr %handle, i64 ");
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
        output.Write("  %negative = icmp slt i64 %index, 0\n  %beyond = icmp sgt i64 %index, %length\n  %invalid = or i1 %negative, %beyond\n  br i1 %invalid, label %bounds_failure, label %increase\nincrease:\n");
        WriteArrayIncrease(output);
        output.Write("  call void @__kimi_array_grow(ptr %handle, i64 ");
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
    private static void WriteArrayRemove(TextWriter output, ArrayHelper helper, bool iterator = false)
    {
        var scalar = helper.ElementLayout is null && !helper.ElementIsString;
        if (iterator)
        {
            output.Write("  %cursor_ptr = getelementptr i8, ptr %handle, i64 16\n  %index = load i64, ptr %cursor_ptr, align 8\n");
        }

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

        if (iterator)
        {
            // SPEC 14.6.2: transfer one element without shifting the remaining buffer or allocating storage.
            output.Write("  %advanced = add i64 %index, 1\n  store i64 %advanced, ptr %cursor_ptr, align 8\n");
            output.Write(scalar ? "  ret " + helper.Element.ComputationType + " %value\n" : "  ret void\n");
            WriteArrayBoundsFailure(output);
            return;
        }

        output.Write("  %next = getelementptr i8, ptr %slot, i64 ");
        WriteNumber(output, Stride(helper));
        output.Write("\n  %last = sub i64 %length, 1\n  %tail_count = sub i64 %last, %index\n  %tail_bytes = mul i64 %tail_count, ");
        WriteNumber(output, Stride(helper));
        output.Write("\n  call void @llvm.memmove.p0.p0.i64(ptr %slot, ptr %next, i64 %tail_bytes, i1 false)\n  store i64 %last, ptr %length_ptr, align 8\n");
        output.Write(scalar ? "  ret " + helper.Element.ComputationType + " %value\n" : "  ret void\n");
        WriteArrayBoundsFailure(output);
    }

    // Moves an acquired payload slot into the next element of a literal whose capacity was reserved (SPEC 4.3).
    private static void WriteArrayPlace(TextWriter output, ArrayHelper helper)
    {
        output.Write("  %buffer = load ptr, ptr %handle, align 8\n  %offset = mul i64 %length, ");
        WriteNumber(output, Stride(helper));
        output.Write("\n  %slot = getelementptr i8, ptr %buffer, i64 %offset\n  call void @llvm.memcpy.p0.p0.i64(ptr %slot, ptr %source, i64 ");
        WriteNumber(output, Stride(helper));
        output.Write(", i1 false)\n  %next = add i64 %length, 1\n  store i64 %next, ptr %length_ptr, align 8\n  ret void\n");
    }

    // Destroys the elements in reverse index order and keeps the capacity; Drop then releases the buffer (SPEC 4.7.6).
    private static void WriteArrayClear(TextWriter output, ArrayHelper helper, bool release)
    {
        if (helper.ElementIsString || helper.ElementLayout?.NeedsDestruction == true)
        {
            var iterator = helper.Kind == ArrayHelperKind.IteratorDrop;
            if (iterator)
            {
                output.Write("  %cursor_ptr = getelementptr i8, ptr %handle, i64 16\n  %cursor = load i64, ptr %cursor_ptr, align 8\n");
            }

            output.Write("  %buffer = load ptr, ptr %handle, align 8\n  br label %test\ntest:\n  %remaining = phi i64 [ %length, %entry ], [ %index, %body ]\n  %done = icmp eq i64 %remaining, ");
            output.Write(iterator ? "%cursor" : "0");
            output.Write("\n  br i1 %done, label %end, label %body\nbody:\n  %index = sub i64 %remaining, 1\n  %offset = mul i64 %index, ");
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

    private static void WriteArrayIncrease(TextWriter output)
    {
        output.Write("  %count_pair = call { i64, i1 } @llvm.sadd.with.overflow.i64(i64 %length, i64 1)\n  %needed = extractvalue { i64, i1 } %count_pair, 0\n  %count_overflow = extractvalue { i64, i1 } %count_pair, 1\n  br i1 %count_overflow, label %count_failure, label %grow\ncount_failure:\n  call void @__kimi_abort(i32 ");
        WriteNumber(output, WindowsLowering.IntegerOverflowReason);
        output.Write(", ptr %location, i64 %location_length, i64 -2)\n  unreachable\ngrow:\n");
    }

    private static void WriteArrayBoundsFailure(TextWriter output)
    {
        output.Write("bounds_failure:\n  call void @__kimi_abort(i32 ");
        WriteNumber(output, WindowsLowering.IndexBoundsReason);
        output.Write(", ptr %location, i64 %location_length, i64 -2)\n  unreachable\n");
    }
}
