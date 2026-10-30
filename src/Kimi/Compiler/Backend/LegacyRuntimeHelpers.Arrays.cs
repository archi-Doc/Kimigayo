// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1402 // The Array helper records are this concern's request vocabulary.

using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal enum ArrayHelperKind : byte
{
    Append,
    Insert,
    Pop,
    Remove,
    Place,
    Clear,
    Drop,
    Swap,

    // SPEC 22.2: the storage boundary over an Array handle and its remainder records.
    BorrowStorage,
    OwnStorage,

    // PLAN G33: a consumed fixed array moves into the inline storage of its owning remainder.
    OwnFixedStorage,
}

/// <summary>The layout offsets of a storage remainder record's Fields {storage, position, count, capacity}, addressed by name,
/// and the size of its storage Field (SPEC 22.2).</summary>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal readonly record struct RemainderFields(int Storage, int Position, int Count, int Capacity, int StorageSize);

/// <summary>The row of one Array helper kind, readable by kind before any representation exists (X4): the prefix of its helpers'
/// names, its ABI shape over a helper's name, element descriptor and remainder Fields, its body's prologue and writer, and its
/// declared needs. No Array helper calls a library function (F2c), so no row declares a library callee.</summary>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal sealed record ArrayHelperRow(
    ArrayHelperKind Kind,
    string Prefix,
    Func<string, StoredValue, RemainderFields, FunctionAbi> Shape,
    string Prologue,
    Action<TextWriter, ArrayHelper> Write,
    HelperNeeds Needs);

/// <summary>A generated Array helper for one element: its ABI, element descriptor, for pop the Option payload offset (the tag is
/// at offset 0), and for the storage boundary its remainder record's Fields.</summary>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal sealed record ArrayHelper(ArrayHelperKind Kind, FunctionAbi Abi, StoredValue Element, int? OptionPayload = null, RemainderFields Fields = default)
{
    /// <summary>Creates the helper of a kind for an element, named by the kind's row prefix and shaped by its row.</summary>
    /// <param name="kind">The helper kind.</param>
    /// <param name="element">The element descriptor.</param>
    /// <param name="elementName">The generator's name of the element, which follows the prefix.</param>
    /// <param name="recordsName">The generator's name of the helper's result and remainder records, or empty.</param>
    /// <param name="optionPayload">For pop, the Option payload offset.</param>
    /// <param name="fields">For the storage boundary, the remainder record's Fields.</param>
    /// <returns>The helper.</returns>
    internal static ArrayHelper Create(
        ArrayHelperKind kind, StoredValue element, string elementName, string recordsName, int? optionPayload, RemainderFields fields)
    {
        var row = LegacyRuntimeHelpers.ArrayRow(kind);
        var abi = row.Shape(string.Concat(row.Prefix, elementName, recordsName), element, fields);
        return new(kind, abi, element, optionPayload, fields);
    }
}

/// <summary>SPEC 4.9.3, 4.9.5, 4.9.7, 4.9.8, 22.2: the Array capacity routines and the per-element Array helpers over the
/// {buffer, length, capacity} handle (Windows x64 profile).</summary>
internal static partial class LegacyRuntimeHelpers
{
    private const string ArrayGrowBody = """
        entry:
          %capacity_ptr = getelementptr i8, ptr %handle, i64 16
          %capacity = load i64, ptr %capacity_ptr, align 8
          %enough = icmp sge i64 %capacity, %minimum
          br i1 %enough, label %done, label %grow
        grow:
          %zero_sized = icmp eq i64 %stride, 0
          br i1 %zero_sized, label %unbounded, label %sized
        unbounded:
          ; SPEC 4.5, 4.9.5: zero-sized elements need no bytes, so every count fits without allocation. The buffer is
          ; a fixed nonnull substitute, aligned for any element, that __kimi_free never releases.
          store ptr inttoptr (i64 ZERO_SIZED_BUFFER to ptr), ptr %handle, align 8
          store i64 9223372036854775807, ptr %capacity_ptr, align 8
          br label %done
        sized:
          ; SPEC 4.9.5: optional growth rounding cannot reject a representable minimum.
          %maximum = udiv i64 9223372036854775807, %stride
          %unrepresentable = icmp ugt i64 %minimum, %maximum
          br i1 %unrepresentable, label %size_failure, label %prepare
        prepare:
          ; SPEC 4.9.8: amortized doubling, clamped to the maximum representable capacity.
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
          ; SPEC 4.9.5: a nonnegative additional count; the checked total is the minimum capacity.
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
          ; SPEC 4.9.5: a failed candidate allocation leaves the handle unchanged.
          %length_ptr = getelementptr i8, ptr %handle, i64 8
          %length = load i64, ptr %length_ptr, align 8
          %capacity_ptr = getelementptr i8, ptr %handle, i64 16
          %capacity = load i64, ptr %capacity_ptr, align 8
          %fits = icmp sle i64 %capacity, %length
          br i1 %fits, label %done, label %shrink
        shrink:
          %buffer = load ptr, ptr %handle, align 8
          %empty = icmp eq i64 %length, 0
          br i1 %empty, label %release, label %sized
        sized:
          ; Zero-sized elements keep the substitute buffer; only the recorded capacity shrinks.
          %zero_sized = icmp eq i64 %stride, 0
          br i1 %zero_sized, label %fit, label %heap
        fit:
          store i64 %length, ptr %capacity_ptr, align 8
          br label %done
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

    private const string ArrayHelperPrologue = """
        entry:
          %length_ptr = getelementptr i8, ptr %handle, i64 8
          %length = load i64, ptr %length_ptr, align 8

        """;

    // Swap checks both indices before it touches a slot (SPEC 4.9.3).
    private const string SwapIndexChecks = """
          %first_negative = icmp slt i64 %first, 0
          %first_beyond = icmp sge i64 %first, %length
          %second_negative = icmp slt i64 %second, 0
          %second_beyond = icmp sge i64 %second, %length
          %first_invalid = or i1 %first_negative, %first_beyond
          %second_invalid = or i1 %second_negative, %second_beyond
          %invalid = or i1 %first_invalid, %second_invalid

        """;

    // SPEC 4.9.5, 4.9.8: the capacity routines, written only for modules that use Arrays so string-only modules keep no memcpy.
    private static readonly string ArrayRuntime =
        RuntimeAbi.ArrayGrow.GetDefinition(exported: false) +
        ArrayGrowBody.Replace("REASON_SIZE", Number(AbortReasons.AllocationSize), StringComparison.Ordinal)
            .Replace("ZERO_SIZED_BUFFER", Number(RuntimeAbi.ZeroSizedBuffer), StringComparison.Ordinal) +
        RuntimeAbi.ArrayReserve.GetDefinition(exported: false) +
        ArrayReserveBody.Replace("REASON_ARGUMENT", Number(AbortReasons.ArgumentRange), StringComparison.Ordinal)
            .Replace("REASON_OVERFLOW", Number(AbortReasons.IntegerOverflow), StringComparison.Ordinal) +
        RuntimeAbi.ArrayShrink.GetDefinition(exported: false) + ArrayShrinkBody;

    // The parameters take the internal convention's kinds and logical indices (IMPL 21.3.1): the handle a Value at slot 0, an index, a second
    // input and an element at their slots, a value in memory an OwnedSlot, a stride or count given; MIR code generation's one call writer
    // places a call's operands by them (R5 plan C8d), and a definition prints only their types and names.
    private static readonly AbiParameter Handle = new("ptr", "handle", AbiParameterKind.Value, 0);

    private static readonly AbiParameter Index = new("i64", "index", AbiParameterKind.Value, 1);

    private static readonly AbiParameter Result = new("ptr", "result", AbiParameterKind.ResultSlot);

    // One row per kind, in ArrayHelperKind order (SPEC 4.9.3, 4.9.7, 22.2); a new kind adds a row. A row declares the Array
    // runtime only when its text grows the buffer or copies or moves elements: Clear and Drop destroy elements and release
    // through the main runtime, and the storage boundary only loads and stores the handle's words.
    private static readonly ArrayHelperRow[] ArrayRows =
    [
        new(
            ArrayHelperKind.Append,
            "__kimi_array_append_",
            static (name, element, _) => AppendShape(name, element),
            ArrayHelperPrologue,
            WriteArrayAppend,
            HelperNeeds.ArrayRuntime),
        new(
            ArrayHelperKind.Insert,
            "__kimi_array_insert_",
            static (name, element, _) => InsertShape(name, element),
            ArrayHelperPrologue,
            WriteArrayInsert,
            HelperNeeds.ArrayRuntime),
        new(
            ArrayHelperKind.Pop,
            "__kimi_array_pop_",
            static (name, _, _) => Slotted(name, [Handle, Result]),
            ArrayHelperPrologue,
            WriteArrayPop,
            HelperNeeds.ArrayRuntime),
        new(
            ArrayHelperKind.Remove,
            "__kimi_array_remove_",
            static (name, element, _) => RemoveShape(name, element),
            ArrayHelperPrologue,
            WriteArrayRemove,
            HelperNeeds.ArrayRuntime),
        new(
            ArrayHelperKind.Place,
            "__kimi_array_place_",
            static (name, element, _) => PlaceShape(name, element),
            ArrayHelperPrologue,
            WriteArrayPlace,
            HelperNeeds.ArrayRuntime),
        new(
            ArrayHelperKind.Clear,
            "__kimi_array_clear_",
            static (name, _, _) => Located(name, []),
            ArrayHelperPrologue,
            WriteArrayClear,
            HelperNeeds.ElementGlue),
        new(
            ArrayHelperKind.Drop,
            "__kimi_array_drop_",
            static (name, _, _) => Located(name, []),
            ArrayHelperPrologue,
            WriteArrayClear,
            HelperNeeds.ElementGlue),
        new(
            ArrayHelperKind.Swap,
            "__kimi_array_swap_",
            static (name, _, _) => Located(name, [new("i64", "first", AbiParameterKind.Value, 1), new("i64", "second", AbiParameterKind.Value, 2)]),
            ArrayHelperPrologue,
            WriteArraySwap,
            HelperNeeds.ArrayRuntime),
        new(
            ArrayHelperKind.BorrowStorage,
            "__kimi_array_borrow_",
            static (name, _, _) => Slotted(name, [Handle, Result]),
            ArrayHelperPrologue,
            WriteArrayBorrow,
            HelperNeeds.None),
        new(
            ArrayHelperKind.OwnStorage,
            "__kimi_array_own_",
            static (name, _, _) => Slotted(name, [new("ptr", "value", AbiParameterKind.OwnedSlot, 0), Result]),
            "entry:\n",
            WriteArrayOwn,
            HelperNeeds.None),
        new(
            ArrayHelperKind.OwnFixedStorage,
            "__kimi_fixed_own_",
            static (name, _, fields) => OwnFixedShape(name, fields),
            "entry:\n",
            WriteFixedOwn,
            HelperNeeds.ArrayRuntime),
    ];

    /// <summary>Gets the row of an Array helper kind; a kind without a row is a broken contract.</summary>
    /// <param name="kind">The helper kind.</param>
    /// <returns>The kind's row.</returns>
    internal static ArrayHelperRow ArrayRow(ArrayHelperKind kind)
        => (uint)kind < (uint)ArrayRows.Length && ArrayRows[(int)kind].Kind == kind
            ? ArrayRows[(int)kind]
            : throw new InvalidOperationException("An Array helper kind has no row.");

    /// <summary>Writes the Array runtime, the capacity routines (SPEC 4.9.5, 4.9.8), which a module holds once when a helper or call of it
    /// declares it.</summary>
    /// <param name="output">The module text.</param>
    internal static void WriteArrayRuntime(TextWriter output) => output.Write(ArrayRuntime);

    /// <summary>Writes one Array helper's definition: its ABI, its kind row's prologue and writer (PD2).</summary>
    /// <param name="output">The module text.</param>
    /// <param name="helper">The helper.</param>
    internal static void WriteArray(TextWriter output, ArrayHelper helper)
    {
        var row = ArrayRow(helper.Kind);
        output.Write(helper.Abi.GetDefinition(exported: false));
        output.Write(row.Prologue);
        row.Write(output, helper);
        output.Write("}\n");
    }

    // A signature without a result: the handle, the leading parameters and the caller's location (SPEC 22.5.1).
    private static FunctionAbi Located(string name, ReadOnlySpan<AbiParameter> leading)
        => new(name, RuntimeAbi.Unit.ComputationType, [Handle, .. leading, RuntimeAbi.Location, RuntimeAbi.LocationLength]);

    // A signature that writes the caller's result slot.
    private static FunctionAbi Slotted(string name, AbiParameter[] parameters)
        => new(name, RuntimeAbi.Unit.ComputationType, parameters) { ResultSlot = true };

    // A stored-value argument of a helper that moves it in at its call's slot: a scalar by value, another value by address (SPEC 21.3.1).
    private static AbiParameter Argument(in StoredValue value, string name, int slot) => value.IsScalar
        ? new(value.Value.ComputationType, name, AbiParameterKind.Value, slot)
        : new("ptr", name, AbiParameterKind.OwnedSlot, slot);

    // A zero-sized element has no slot, so its helpers take no value, source or result pointer (SPEC 4.5).
    private static FunctionAbi AppendShape(string name, StoredValue element)
        => element.IsZeroSized ? Located(name, []) : Located(name, [Argument(element, "value", 1)]);

    private static FunctionAbi InsertShape(string name, StoredValue element)
        => element.IsZeroSized ? Located(name, [Index]) : Located(name, [Index, Argument(element, "value", 2)]);

    private static FunctionAbi PlaceShape(string name, StoredValue element)
        => element.IsZeroSized ? Located(name, []) : Located(name, [new("ptr", "source")]);

    // A removed scalar is returned; another element moves into the caller's result slot.
    private static FunctionAbi RemoveShape(string name, StoredValue element)
    {
        if (element.IsScalar)
        {
            return new(name, element.Value.ComputationType, [Handle, Index, RuntimeAbi.Location, RuntimeAbi.LocationLength]);
        }

        return element.IsZeroSized
            ? Located(name, [Index])
            : Slotted(name, [Handle, Index, Result, RuntimeAbi.Location, RuntimeAbi.LocationLength]);
    }

    // The consumed fixed array moves in by address unless its storage has no bytes (SPEC 22.2).
    private static FunctionAbi OwnFixedShape(string name, RemainderFields fields) => fields.StorageSize == 0
        ? Slotted(name, [new("i64", "count"), Result])
        : Slotted(name, [new("ptr", "value", AbiParameterKind.OwnedSlot, 0), new("i64", "count"), Result]);

    private static long Stride(ArrayHelper helper) => helper.Element.Value.Layout.Stride;

    // Grows to hold one more element and places it at buffer + length * stride (SPEC 4.9.3, 4.9.8).
    private static void WriteArrayAppend(TextWriter output, ArrayHelper helper)
    {
        WriteArrayIncrease(output);
        output.Write("  call void @__kimi_array_grow(ptr %handle, i64 ");
        WriteNumber(output, Stride(helper));
        output.Write(", i64 %needed, ptr %location, i64 %location_length)\n  %buffer = load ptr, ptr %handle, align 8\n  %offset = mul i64 %length, ");
        WriteNumber(output, Stride(helper));
        output.Write("\n  %slot = getelementptr i8, ptr %buffer, i64 %offset\n");
        WriteStoredArgument(output, helper.Element, "%value", "%slot", "%stored");
        output.Write("  store i64 %needed, ptr %length_ptr, align 8\n  ret void\n");
    }

    // Requires 0 <= index <= length, then shifts the tail up by one element before placing the value.
    private static void WriteArrayInsert(TextWriter output, ArrayHelper helper)
    {
        output.Write("""
              %negative = icmp slt i64 %index, 0
              %beyond = icmp sgt i64 %index, %length
              %invalid = or i1 %negative, %beyond
              br i1 %invalid, label %bounds_failure, label %increase
            increase:

            """);
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
        WriteStoredArgument(output, helper.Element, "%value", "%slot", "%stored");
        output.Write("  store i64 %needed, ptr %length_ptr, align 8\n  ret void\n");
        WriteArrayBoundsFailure(output);
    }

    // Moves the last element into the Option payload with the Some tag, or writes the None tag (SPEC 4.9.3).
    private static void WriteArrayPop(TextWriter output, ArrayHelper helper)
    {
        var payload = helper.OptionPayload ?? throw new InvalidOperationException("Array pop needs its Option payload offset.");
        output.Write("""
              %empty = icmp eq i64 %length, 0
              br i1 %empty, label %none, label %some
            some:
              %last = sub i64 %length, 1
              %buffer = load ptr, ptr %handle, align 8

            """);
        output.Write("  %offset = mul i64 %last, ");
        WriteNumber(output, Stride(helper));
        output.Write("\n  %slot = getelementptr i8, ptr %buffer, i64 %offset\n  %payload = getelementptr i8, ptr %result, i64 ");
        WriteNumber(output, payload);
        output.Write("\n  call void @llvm.memcpy.p0.p0.i64(ptr %payload, ptr %slot, i64 ");
        WriteNumber(output, Stride(helper));
        output.Write("""
            , i1 false)
              store i32 0, ptr %result, align 4
              store i64 %last, ptr %length_ptr, align 8
              ret void
            none:
              store i32 1, ptr %result, align 4
              ret void

            """);
    }

    // Requires 0 <= index < length, moves the element out and shifts the tail down (SPEC 4.9.3).
    private static void WriteArrayRemove(TextWriter output, ArrayHelper helper)
    {
        var scalar = helper.Element.IsScalar;
        output.Write("""
              %negative = icmp slt i64 %index, 0
              %beyond = icmp sge i64 %index, %length
              %invalid = or i1 %negative, %beyond
              br i1 %invalid, label %bounds_failure, label %remove
            remove:

            """);
        output.Write("  %buffer = load ptr, ptr %handle, align 8\n  %offset = mul i64 %index, ");
        WriteNumber(output, Stride(helper));
        output.Write("\n  %slot = getelementptr i8, ptr %buffer, i64 %offset\n");
        if (scalar)
        {
            WriteArrayElementLoad(output, helper.Element.Value);
        }
        else
        {
            WriteStoredCopy(output, "%result", "%slot", Stride(helper));
        }

        output.Write("  %next = getelementptr i8, ptr %slot, i64 ");
        WriteNumber(output, Stride(helper));
        output.Write("\n  %last = sub i64 %length, 1\n  %tail_count = sub i64 %last, %index\n  %tail_bytes = mul i64 %tail_count, ");
        WriteNumber(output, Stride(helper));
        output.Write("\n  call void @llvm.memmove.p0.p0.i64(ptr %slot, ptr %next, i64 %tail_bytes, i1 false)\n  store i64 %last, ptr %length_ptr, align 8\n");
        output.Write("  ret ");
        output.Write(scalar ? helper.Element.Value.ComputationType : "void");
        output.Write(scalar ? " %value\n" : "\n");
        WriteArrayBoundsFailure(output);
    }

    // Loads a removed scalar from its slot into %value, narrowing a wider storage form (SPEC 21.3.1).
    private static void WriteArrayElementLoad(TextWriter output, ValueLowering element)
    {
        var storage = element.Layout.StorageType;
        var narrowed = storage != element.ComputationType;
        output.Write(narrowed ? "  %loaded = load " : "  %value = load ");
        output.Write(storage);
        output.Write(", ptr %slot, align ");
        WriteNumber(output, element.Layout.Alignment);
        output.Write('\n');
        if (narrowed)
        {
            output.Write("  %value = trunc ");
            output.Write(storage);
            output.Write(" %loaded to ");
            output.Write(element.ComputationType);
            output.Write('\n');
        }
    }

    // Requires both indices in [0, length) and exchanges the two element slots through a stack buffer (SPEC 4.9.3).
    private static void WriteArraySwap(TextWriter output, ArrayHelper helper)
    {
        var stride = Stride(helper);
        if (stride == 0)
        {
            // Zero-sized elements have no bytes to exchange; the indices are still checked.
            output.Write(SwapIndexChecks);
            output.Write("  br i1 %invalid, label %bounds_failure, label %done\ndone:\n  ret void\n");
            WriteArrayBoundsFailure(output);
            return;
        }

        output.Write("  %scratch = alloca i8, i64 ");
        WriteNumber(output, stride);
        output.Write(", align 16\n");
        output.Write(SwapIndexChecks);
        output.Write("""
              br i1 %invalid, label %bounds_failure, label %check
            check:
              %same = icmp eq i64 %first, %second
              br i1 %same, label %done, label %exchange
            exchange:
              %buffer = load ptr, ptr %handle, align 8

            """);
        output.Write("  %first_offset = mul i64 %first, ");
        WriteNumber(output, stride);
        output.Write("\n  %first_slot = getelementptr i8, ptr %buffer, i64 %first_offset\n  %second_offset = mul i64 %second, ");
        WriteNumber(output, stride);
        output.Write("\n  %second_slot = getelementptr i8, ptr %buffer, i64 %second_offset\n");
        WriteStoredCopy(output, "%scratch", "%first_slot", stride);
        WriteStoredCopy(output, "%first_slot", "%second_slot", stride);
        WriteStoredCopy(output, "%second_slot", "%scratch", stride);
        output.Write("  br label %done\ndone:\n  ret void\n");
        WriteArrayBoundsFailure(output);
    }

    // SPEC 22.2: the remainder records the buffer and the whole length as its untaken range {storage, position, count}.
    private static void WriteArrayBorrow(TextWriter output, ArrayHelper helper)
    {
        output.Write("  %buffer = load ptr, ptr %handle, align 8\n");
        WriteRemainderField(output, "storage", helper.Fields.Storage, "ptr %buffer");
        WriteRemainderField(output, "position", helper.Fields.Position, "i64 0");
        WriteRemainderField(output, "count", helper.Fields.Count, "i64 %length");
        output.Write("  ret void\n");
    }

    // SPEC 22.2: ownStorage transfers the handle {buffer, length, capacity} into the owning remainder
    // {storage, position 0, count length, capacity}; the consumed Array slot is not read again.
    private static void WriteArrayOwn(TextWriter output, ArrayHelper helper)
    {
        output.Write("""
              %buffer = load ptr, ptr %value, align 8
              %length_ptr = getelementptr i8, ptr %value, i64 8
              %length = load i64, ptr %length_ptr, align 8
              %capacity_ptr = getelementptr i8, ptr %value, i64 16
              %capacity = load i64, ptr %capacity_ptr, align 8

            """);
        if (helper.Fields.Storage == 0)
        {
            output.Write("  store ptr %buffer, ptr %result, align 8\n");
        }
        else
        {
            WriteRemainderField(output, "storage", helper.Fields.Storage, "ptr %buffer");
        }

        WriteRemainderField(output, "position", helper.Fields.Position, "i64 0");
        WriteRemainderField(output, "count", helper.Fields.Count, "i64 %length");
        WriteRemainderField(output, "capacity", helper.Fields.Capacity, "i64 %capacity");
        output.Write("  ret void\n");
    }

    // Moves the consumed fixed array into the remainder's inline storage and opens its whole range (SPEC 22.2).
    private static void WriteFixedOwn(TextWriter output, ArrayHelper helper)
    {
        if (helper.Fields.StorageSize != 0)
        {
            output.Write("  %storage_slot = getelementptr i8, ptr %result, i64 ");
            WriteNumber(output, helper.Fields.Storage);
            output.Write('\n');
            WriteStoredCopy(output, "%storage_slot", "%value", helper.Fields.StorageSize);
        }

        WriteRemainderField(output, "position", helper.Fields.Position, "i64 0");
        WriteRemainderField(output, "count", helper.Fields.Count, "i64 %count");
        output.Write("  ret void\n");
    }

    // Stores `value` into the remainder Field `name` of the helper's result record at its named layout offset.
    private static void WriteRemainderField(TextWriter output, string name, int offset, string value)
    {
        output.Write("  %");
        output.Write(name);
        output.Write("_slot = getelementptr i8, ptr %result, i64 ");
        WriteNumber(output, offset);
        output.Write("\n  store ");
        output.Write(value);
        output.Write(", ptr %");
        output.Write(name);
        output.Write("_slot, align 8\n");
    }

    // Moves an acquired payload slot into the next element of a literal whose capacity was reserved (SPEC 4.3).
    private static void WriteArrayPlace(TextWriter output, ArrayHelper helper)
    {
        if (!helper.Element.IsZeroSized)
        {
            output.Write("  %buffer = load ptr, ptr %handle, align 8\n  %offset = mul i64 %length, ");
            WriteNumber(output, Stride(helper));
            output.Write("\n  %slot = getelementptr i8, ptr %buffer, i64 %offset\n");
            WriteStoredCopy(output, "%slot", "%source", Stride(helper));
        }

        output.Write("  %next = add i64 %length, 1\n  store i64 %next, ptr %length_ptr, align 8\n  ret void\n");
    }

    // Destroys the elements in reverse index order and keeps the capacity; Drop then releases the buffer (SPEC 4.9.7).
    private static void WriteArrayClear(TextWriter output, ArrayHelper helper)
    {
        if (helper.Element.NeedsDestruction)
        {
            output.Write("""
                  %buffer = load ptr, ptr %handle, align 8
                  br label %test
                test:
                  %remaining = phi i64 [ %length, %entry ], [ %index, %body ]
                  %done = icmp eq i64 %remaining, 0
                  br i1 %done, label %end, label %body
                body:
                  %index = sub i64 %remaining, 1

                """);
            output.Write("  %offset = mul i64 %index, ");
            WriteNumber(output, Stride(helper));
            output.Write("\n  %element = getelementptr i8, ptr %buffer, i64 %offset\n");
            WriteStoredDestruction(output, helper.Element, "%element");
            output.Write("  br label %test\nend:\n");
        }

        output.Write("  store i64 0, ptr %length_ptr, align 8\n");
        if (helper.Kind == ArrayHelperKind.Drop)
        {
            output.Write("  call void @__kimi_array_free(ptr %handle, ptr %location, i64 %location_length)\n");
        }

        output.Write("  ret void\n");
    }

    // The checked count of an append or insert; a full Array's count overflow Aborts before any growth (SPEC 4.9.3).
    private static void WriteArrayIncrease(TextWriter output)
    {
        output.Write("""
              %count_pair = call { i64, i1 } @llvm.sadd.with.overflow.i64(i64 %length, i64 1)
              %needed = extractvalue { i64, i1 } %count_pair, 0
              %count_overflow = extractvalue { i64, i1 } %count_pair, 1
              br i1 %count_overflow, label %count_failure, label %grow
            count_failure:

            """);
        WriteArrayAbort(output, AbortReasons.IntegerOverflow);
        output.Write("grow:\n");
    }

    private static void WriteArrayBoundsFailure(TextWriter output)
    {
        output.Write("bounds_failure:\n");
        WriteArrayAbort(output, AbortReasons.IndexBounds);
    }

    // A helper's precondition failure Aborts at its caller's location with no operating-system error (SPEC 22.5.2).
    private static void WriteArrayAbort(TextWriter output, int reason)
    {
        output.Write("  call void @__kimi_abort(i32 ");
        WriteNumber(output, reason);
        output.Write(", ptr %location, i64 %location_length, i64 -2)\n  unreachable\n");
    }
}
