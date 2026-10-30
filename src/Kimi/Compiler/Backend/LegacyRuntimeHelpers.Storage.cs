// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1402 // The storage kernel records are this concern's request vocabulary.

using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

/// <summary>An inline storage-boundary kernel (SPEC 22.2): the text a Body-disposition storage operation writes in its caller's
/// body.</summary>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal enum StorageKernelKind : byte
{
    /// <summary>StorageDictionaryLayout: the (raw Dictionary, isize) Tuple {handle address, entry stride}, at offsets 0 and 8.</summary>
    DictionaryLayout,

    /// <summary>StorageKeyAt and StorageValueAt: the address of a key or value inside a validated live slot.</summary>
    EntryAddress,

    /// <summary>StorageBorrowFixedShared and StorageBorrowFixedExclusive: the remainder {storage, position, count} = {first
    /// element, 0, length} of the borrowed fixed array.</summary>
    BorrowFixed,

    /// <summary>StorageBorrowDictionary and StorageBorrowDictionaryExclusive: the remainder {storage, stride, link, count} =
    /// {slot buffer, entry stride, first link, live count} of the borrowed handle.</summary>
    BorrowDictionary,

    /// <summary>StorageOwnDictionary: the owning remainder {storage, stride, link, tail, count}, which also keeps the last
    /// link of the transferred handle.</summary>
    OwnDictionary,
}

/// <summary>The input of an inline storage kernel: the spelling of the address it reads; the spelling of its result, the record
/// it writes or, for EntryAddress, the address it defines; its number, the entry stride, the array length or the entry offset;
/// its result record's Field offsets in the order its kind lists them (none for DictionaryLayout and EntryAddress); and the
/// number naming its temporaries.</summary>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal readonly ref struct StorageKernelInput(ReadOnlySpan<char> source, ReadOnlySpan<char> result, long number, ReadOnlySpan<int> fields, int id)
{
    internal ReadOnlySpan<char> Source { get; } = source;

    internal ReadOnlySpan<char> Result { get; } = result;

    internal long Number { get; } = number;

    internal ReadOnlySpan<int> Fields { get; } = fields;

    internal int Id { get; } = id;
}

/// <summary>Writes the text of an inline storage kernel.</summary>
/// <param name="output">The function text.</param>
/// <param name="input">The kernel's input.</param>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal delegate void StorageKernelWriter(TextWriter output, in StorageKernelInput input);

/// <summary>The row of one inline storage kernel kind, readable by kind before any representation exists (X4): its writer and
/// its declared needs, which are none, since its text is loads, stores and address arithmetic in the caller's body; no storage
/// kernel row declares a library callee.</summary>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal sealed record StorageKernelRow(StorageKernelKind Kind, StorageKernelWriter Write, HelperNeeds Needs);

/// <summary>SPEC 22.2: the storage-boundary kernels written inline in their caller's body, the kernels of the Raw.slice and
/// capacity intrinsics (SPEC 5.6, 4.9.5), the private storage byte primitives' request, and the addressing of a library record's Field
/// that they share with the old writer (Windows x64 profile).</summary>
internal sealed partial class LegacyRuntimeHelpers
{
    // One row per kind, in StorageKernelKind order; a new kind adds a row.
    private static readonly StorageKernelRow[] StorageKernelRows =
    [
        new(StorageKernelKind.DictionaryLayout, WriteDictionaryLayout, HelperNeeds.None),
        new(
            StorageKernelKind.EntryAddress,
            static (TextWriter output, in StorageKernelInput input) => WriteEntryAddress(output, input.Result, input.Source, input.Number),
            HelperNeeds.None),
        new(StorageKernelKind.BorrowFixed, WriteFixedBorrow, HelperNeeds.None),
        new(StorageKernelKind.BorrowDictionary, WriteDictionaryBorrow, HelperNeeds.None),
        new(StorageKernelKind.OwnDictionary, WriteDictionaryOwn, HelperNeeds.None),
    ];

    /// <summary>Gets or sets a value indicating whether a lowered body uses the private storage byte primitives (SPEC 22.2).</summary>
    internal bool NeedsStorageBytes { get; set; }

    /// <summary>Gets the row of an inline storage kernel kind; a kind without a row is a broken contract.</summary>
    /// <param name="kind">The kernel kind.</param>
    /// <returns>The kind's row.</returns>
    internal static StorageKernelRow StorageRow(StorageKernelKind kind)
        => (uint)kind < (uint)StorageKernelRows.Length && StorageKernelRows[(int)kind].Kind == kind
            ? StorageKernelRows[(int)kind]
            : throw new InvalidOperationException("A storage kernel kind has no row.");

    /// <summary>Writes SPEC 5.6's <c>Raw.slice</c>: the Slice record {buffer, length} stored into its result slot; nothing is read
    /// or checked.</summary>
    /// <param name="output">The function text.</param>
    /// <param name="buffer">The spelling of the raw pointer.</param>
    /// <param name="count">The spelling of the element count.</param>
    /// <param name="record">The spelling of the result slot's address.</param>
    /// <param name="id">The number naming the kernel's temporaries.</param>
    internal static void WriteRawSlice(TextWriter output, ReadOnlySpan<char> buffer, ReadOnlySpan<char> count, ReadOnlySpan<char> record, int id)
    {
        output.Write("  store ptr ");
        output.Write(buffer);
        output.Write(", ptr ");
        output.Write(record);
        output.Write(", align 8\n  %rslen");
        WriteNumber(output, id);
        output.Write(" = getelementptr i8, ptr ");
        output.Write(record);
        output.Write(", i64 8\n  store i64 ");
        output.Write(count);
        output.Write(", ptr %rslen");
        WriteNumber(output, id);
        output.Write(", align 8\n");
    }

    /// <summary>Writes SPEC 4.9.5's <c>capacity</c>: an Array or Dictionary handle stores its capacity at offset 16, an Array's
    /// after its buffer and length.</summary>
    /// <param name="output">The function text.</param>
    /// <param name="handle">The spelling of the handle's address.</param>
    /// <param name="result">The name of the loaded capacity.</param>
    /// <param name="id">The number naming the kernel's temporaries.</param>
    internal static void WriteCapacity(TextWriter output, ReadOnlySpan<char> handle, ReadOnlySpan<char> result, int id)
    {
        output.Write("  %seqcapptr");
        WriteNumber(output, id);
        output.Write(" = getelementptr i8, ptr ");
        output.Write(handle);
        output.Write(", i64 16\n  ");
        output.Write(result);
        output.Write(" = load i64, ptr %seqcapptr");
        WriteNumber(output, id);
        output.Write(", align 8\n");
    }

    /// <summary>Defines the address of a library record's Field at its named layout offset when it needs one: offset 0 is the
    /// record's own address, and a nonzero offset is reached through <c>{name}{id}_{offset}</c>, written here before its use.</summary>
    /// <param name="output">The function text.</param>
    /// <param name="record">The spelling of the record's address.</param>
    /// <param name="name">The prefix of the Field address's name.</param>
    /// <param name="id">The number naming the writer's temporaries.</param>
    /// <param name="offset">The Field's offset in the record.</param>
    internal static void DefineField(TextWriter output, ReadOnlySpan<char> record, string name, int id, long offset)
    {
        if (offset != 0)
        {
            output.Write("  ");
            WriteField(output, record, name, id, offset);
            output.Write(" = getelementptr i8, ptr ");
            output.Write(record);
            output.Write(", i64 ");
            WriteNumber(output, offset);
            output.Write('\n');
        }
    }

    /// <summary>Writes the address of a library record's Field that <see cref="DefineField"/> defined.</summary>
    /// <param name="output">The function text.</param>
    /// <param name="record">The spelling of the record's address.</param>
    /// <param name="name">The prefix of the Field address's name.</param>
    /// <param name="id">The number naming the writer's temporaries.</param>
    /// <param name="offset">The Field's offset in the record.</param>
    internal static void WriteField(TextWriter output, ReadOnlySpan<char> record, string name, int id, long offset)
    {
        if (offset == 0)
        {
            output.Write(record);
            return;
        }

        output.Write(name);
        WriteNumber(output, id);
        output.Write('_');
        WriteNumber(output, offset);
    }

    // SPEC 22.2: the layout projection's Tuple {handle address, entry stride}, whose elements the old lowering found at offsets 0
    // and 8.
    private static void WriteDictionaryLayout(TextWriter output, in StorageKernelInput input)
    {
        output.Write("  store ptr ");
        output.Write(input.Source);
        output.Write(", ptr ");
        output.Write(input.Result);
        output.Write(", align 8\n  %dlayout");
        WriteNumber(output, input.Id);
        output.Write("_1 = getelementptr i8, ptr ");
        output.Write(input.Result);
        output.Write(", i64 8\n  store i64 ");
        WriteNumber(output, input.Number);
        output.Write(", ptr %dlayout");
        WriteNumber(output, input.Id);
        output.Write("_1, align 8\n");
    }

    // SPEC 4.9.6, 22.2: the address of a key or value at its offset inside an entry slot.
    private static void WriteEntryAddress(TextWriter output, ReadOnlySpan<char> result, ReadOnlySpan<char> slot, long offset)
    {
        output.Write("  ");
        output.Write(result);
        output.Write(" = getelementptr i8, ptr ");
        output.Write(slot);
        output.Write(", i64 ");
        WriteNumber(output, offset);
        output.Write('\n');
    }

    // SPEC 22.2: borrowStorage over a fixed array writes the contiguous remainder {storage, position, count} as the borrowed
    // array's first element address, 0 and its length.
    private static void WriteFixedBorrow(TextWriter output, in StorageKernelInput input)
    {
        var id = input.Id;
        DefineField(output, input.Result, "%ffield", id, input.Fields[0]);
        output.Write("  store ptr ");
        output.Write(input.Source);
        output.Write(", ptr ");
        WriteField(output, input.Result, "%ffield", id, input.Fields[0]);
        output.Write(", align 8\n  %fpos");
        WriteNumber(output, id);
        output.Write(" = getelementptr i8, ptr ");
        output.Write(input.Result);
        output.Write(", i64 ");
        WriteNumber(output, input.Fields[1]);
        output.Write("\n  store i64 0, ptr %fpos");
        WriteNumber(output, id);
        output.Write(", align 8\n  %fcount");
        WriteNumber(output, id);
        output.Write(" = getelementptr i8, ptr ");
        output.Write(input.Result);
        output.Write(", i64 ");
        WriteNumber(output, input.Fields[2]);
        output.Write("\n  store i64 ");
        WriteNumber(output, input.Number);
        output.Write(", ptr %fcount");
        WriteNumber(output, id);
        output.Write(", align 8\n");
    }

    // SPEC 22.2: borrowStorage over a Dictionary copies the handle's slot buffer, first link and live count into the remainder
    // {storage, stride, link, count} with the concrete entry stride.
    private static void WriteDictionaryBorrow(TextWriter output, in StorageKernelInput input)
    {
        WriteDictionaryRemainder(output, input);
        WriteDictionaryField(output, input, input.Fields[3], "i64 %dlen", input.Id);
    }

    // SPEC 22.2: ownStorage transfers the handle into the owning remainder {storage, stride, link, tail, count}, which also keeps
    // the last link.
    private static void WriteDictionaryOwn(TextWriter output, in StorageKernelInput input)
    {
        var id = input.Id;
        WriteDictionaryRemainder(output, input);
        output.Write("  %dtailp");
        WriteNumber(output, id);
        output.Write(" = getelementptr i8, ptr ");
        output.Write(input.Source);
        output.Write(", i64 40\n  %dtail");
        WriteNumber(output, id);
        output.Write(" = load i64, ptr %dtailp");
        WriteNumber(output, id);
        output.Write(", align 8\n");
        WriteDictionaryField(output, input, input.Fields[3], "i64 %dtail", id);
        WriteDictionaryField(output, input, input.Fields[4], "i64 %dlen", id);
    }

    // Loads the handle's slot buffer, live count (offset 24) and first link (offset 32), then stores the remainder's storage,
    // stride and link Fields.
    private static void WriteDictionaryRemainder(TextWriter output, in StorageKernelInput input)
    {
        var id = input.Id;
        output.Write("  %dbuf");
        WriteNumber(output, id);
        output.Write(" = load ptr, ptr ");
        output.Write(input.Source);
        output.Write(", align 8\n  %dlenp");
        WriteNumber(output, id);
        output.Write(" = getelementptr i8, ptr ");
        output.Write(input.Source);
        output.Write(", i64 24\n  %dlen");
        WriteNumber(output, id);
        output.Write(" = load i64, ptr %dlenp");
        WriteNumber(output, id);
        output.Write(", align 8\n  %dheadp");
        WriteNumber(output, id);
        output.Write(" = getelementptr i8, ptr ");
        output.Write(input.Source);
        output.Write(", i64 32\n  %dhead");
        WriteNumber(output, id);
        output.Write(" = load i64, ptr %dheadp");
        WriteNumber(output, id);
        output.Write(", align 8\n");
        WriteDictionaryField(output, input, input.Fields[0], "ptr %dbuf", id);
        WriteDictionaryField(output, input, input.Fields[1], "i64 ", input.Number);
        WriteDictionaryField(output, input, input.Fields[2], "i64 %dhead", id);
    }

    // Stores `prefix` and `number` into the remainder Field at `offset`.
    private static void WriteDictionaryField(TextWriter output, in StorageKernelInput input, int offset, string prefix, long number)
    {
        DefineField(output, input.Result, "%dfield", input.Id, offset);
        output.Write("  store ");
        output.Write(prefix);
        WriteNumber(output, number);
        output.Write(", ptr ");
        WriteField(output, input.Result, "%dfield", input.Id, offset);
        output.Write(", align 8\n");
    }

    // The storage byte primitives, written before the Dictionary runtime when a body uses them: the runtime's nullable Alloc, then its
    // byte copy (LlvmModuleText.TryAllocate, LlvmModuleText.TransferBytes).
    private void WriteStorageBytes(TextWriter output)
    {
        if (this.NeedsStorageBytes)
        {
            output.Write(TryAllocate);
            output.Write(TransferBytes);
        }
    }
}
