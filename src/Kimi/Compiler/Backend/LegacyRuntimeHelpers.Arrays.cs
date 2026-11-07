// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1402 // The Array helper records are this concern's request vocabulary.

using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal enum ArrayHelperKind : byte
{
    // SPEC 22.2: the storage boundary over an Array handle and its remainder records.
    BorrowStorage,
    OwnStorage,

    // PLAN G33: a consumed fixed array moves into the inline storage of its owning remainder.
    OwnFixedStorage,
}

/// <summary>What an Array helper's descriptor reads of its function's result, which its item's instance gives (ModuleCodegen.ResultParts;
/// DECISIONS R5-M121): nothing; or a remainder record's Fields and its storage Field's size (SPEC 22.2), an owned remainder's with its
/// capacity, a fixed array's, whose routine is given the array's length.</summary>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal enum ArrayHelperResult : byte
{
    None,
    Remainder,
    OwnedRemainder,
    FixedRemainder,
}

/// <summary>The layout offsets of a storage remainder record's Fields {storage, position, count, capacity}, addressed by name,
/// and the size of its storage Field (SPEC 22.2).</summary>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal readonly record struct RemainderFields(int Storage, int Position, int Count, int Capacity, int StorageSize);

/// <summary>The row of one Array helper kind, readable by kind before any representation exists (X4): the prefix of its helpers'
/// names, its ABI shape over a helper's name and remainder Fields, its body's prologue and writer, its declared needs and what its
/// descriptor reads of its function's result. No Array helper calls a library function (F2c), so no row
/// declares a library callee.</summary>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal sealed record ArrayHelperRow(
    ArrayHelperKind Kind,
    string Prefix,
    Func<string, RemainderFields, FunctionAbi> Shape,
    string Prologue,
    Action<TextWriter, ArrayHelper> Write,
    HelperNeeds Needs,
    ArrayHelperResult Result);

/// <summary>A generated Array helper for one instance: its ABI and its remainder record's Fields.</summary>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal sealed record ArrayHelper(ArrayHelperKind Kind, FunctionAbi Abi, RemainderFields Fields)
{
    /// <summary>Creates the helper of a kind, named by the kind's row prefix and shaped by its row.</summary>
    /// <param name="kind">The helper kind.</param>
    /// <param name="elementName">The generator's name of the instance, which follows the prefix.</param>
    /// <param name="recordsName">The generator's name of the helper's result and remainder records, or empty.</param>
    /// <param name="fields">The remainder record's Fields.</param>
    /// <returns>The helper.</returns>
    internal static ArrayHelper Create(ArrayHelperKind kind, string elementName, string recordsName, RemainderFields fields)
    {
        var row = LegacyRuntimeHelpers.ArrayRow(kind);
        var abi = row.Shape(string.Concat(row.Prefix, elementName, recordsName), fields);
        return new(kind, abi, fields);
    }
}

/// <summary>SPEC 4.9.5, 4.9.8, 22.2: the literal's Array growth routine and the per-element Array helpers over the {buffer, length,
/// capacity} handle (Windows x64 profile).</summary>
internal static partial class LegacyRuntimeHelpers
{
    // SPEC 4.9.5, 4.9.8: the capacity routine, written only for modules that use Arrays so string-only modules keep no memcpy; its one
    // caller is the literal (FunctionCodegen.ArrayBuffer). A runtime template (LlvmModuleText.ExpandRuntime): the routine's definition at
    // its marker line, its abort reasons and the zero-sized buffer address at theirs.
    internal const string ArrayRuntimeTemplate = """
        {{__kimi_array_grow}}
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
          store ptr inttoptr (i64 {{zero_sized_buffer}} to ptr), ptr %handle, align 8
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
          call void @__kimi_abort(i32 {{reason_size}}, ptr %location, i64 %location_length, i64 {{no_os_error}})
          unreachable
        done:
          ret void
        }

        """;

    /// <summary>The routines whose definitions ArrayRuntimeTemplate splices, each at its one marker line.</summary>
    internal static readonly FunctionAbi[] ArrayRuntimeDefinitions = [RuntimeAbi.ArrayGrow];

    private const string ArrayHelperPrologue = """
        entry:
          %length_ptr = getelementptr i8, ptr %handle, i64 8
          %length = load i64, ptr %length_ptr, align 8

        """;

    // The parameters take the internal convention's kinds and logical indices (IMPL 21.3.1): the handle a Value at slot 0, a value in memory
    // an OwnedSlot, a stride or count given; MIR code generation's one call writer places a call's operands by them (R5 plan C8d), and a
    // definition prints only their types and names.
    private static readonly AbiParameter Handle = new("ptr", "handle", AbiParameterKind.Value, 0);

    private static readonly AbiParameter Result = new("ptr", "result", AbiParameterKind.ResultSlot);

    // One row per kind, in ArrayHelperKind order (SPEC 4.9.3, 4.9.7, 22.2); a new kind adds a row. A row declares the Array
    // runtime only when its text copies elements: the storage boundary only loads and stores the handle's words.
    private static readonly ArrayHelperRow[] ArrayRows =
    [
        new(
            ArrayHelperKind.BorrowStorage,
            "__kimi_array_borrow_",
            static (name, _) => Slotted(name, [Handle, Result]),
            ArrayHelperPrologue,
            WriteArrayBorrow,
            HelperNeeds.None,
            ArrayHelperResult.Remainder),
        new(
            ArrayHelperKind.OwnStorage,
            "__kimi_array_own_",
            static (name, _) => Slotted(name, [new("ptr", "value", AbiParameterKind.OwnedSlot, 0), Result]),
            "entry:\n",
            WriteArrayOwn,
            HelperNeeds.None,
            ArrayHelperResult.OwnedRemainder),
        new(
            ArrayHelperKind.OwnFixedStorage,
            "__kimi_fixed_own_",
            static (name, fields) => OwnFixedShape(name, fields),
            "entry:\n",
            WriteFixedOwn,
            HelperNeeds.ArrayRuntime,
            ArrayHelperResult.FixedRemainder),
    ];

    /// <summary>Gets the row of an Array helper kind; a kind without a row is a broken contract.</summary>
    /// <param name="kind">The helper kind.</param>
    /// <returns>The kind's row.</returns>
    internal static ArrayHelperRow ArrayRow(ArrayHelperKind kind)
        => (uint)kind < (uint)ArrayRows.Length && ArrayRows[(int)kind].Kind == kind
            ? ArrayRows[(int)kind]
            : throw new InvalidOperationException("An Array helper kind has no row.");

    /// <summary>Writes the Array runtime, the capacity routine (SPEC 4.9.5, 4.9.8), which a module holds once when a helper or call of it
    /// declares it.</summary>
    /// <param name="output">The module text.</param>
    internal static void WriteArrayRuntime(TextWriter output) => output.Write(Texts.ArrayRuntime);

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

    // The consumed fixed array moves in by address unless its storage has no bytes (SPEC 22.2).
    private static FunctionAbi OwnFixedShape(string name, RemainderFields fields) => fields.StorageSize == 0
        ? Slotted(name, [new("i64", "count"), Result])
        : Slotted(name, [new("ptr", "value", AbiParameterKind.OwnedSlot, 0), new("i64", "count"), Result]);

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

    private static partial class Texts
    {
        // The Array runtime (ArrayRuntimeTemplate), expanded once.
        internal static readonly string ArrayRuntime = ExpandRuntime(ArrayRuntimeTemplate, ArrayRuntimeDefinitions);
    }
}
