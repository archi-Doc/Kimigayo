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
}

/// <summary>The input of an inline storage kernel: the spelling of the address it reads; the spelling of its result, the record
/// it writes; its number, the entry stride; and the number naming its temporaries.</summary>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal readonly ref struct StorageKernelInput(ReadOnlySpan<char> source, ReadOnlySpan<char> result, long number, int id)
{
    internal ReadOnlySpan<char> Source { get; } = source;

    internal ReadOnlySpan<char> Result { get; } = result;

    internal long Number { get; } = number;

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

/// <summary>SPEC 22.2: the storage-boundary kernels written inline in their caller's body, the kernel of the capacity intrinsic
/// (SPEC 4.9.5), and the addressing of a library record's Field (Windows x64 profile).</summary>
internal static partial class LegacyRuntimeHelpers
{
    // One row per kind, in StorageKernelKind order; a new kind adds a row.
    private static readonly StorageKernelRow[] StorageKernelRows =
    [
        new(StorageKernelKind.DictionaryLayout, WriteDictionaryLayout, HelperNeeds.None),
    ];

    /// <summary>Gets the row of an inline storage kernel kind; a kind without a row is a broken contract.</summary>
    /// <param name="kind">The kernel kind.</param>
    /// <returns>The kind's row.</returns>
    internal static StorageKernelRow StorageRow(StorageKernelKind kind)
        => (uint)kind < (uint)StorageKernelRows.Length && StorageKernelRows[(int)kind].Kind == kind
            ? StorageKernelRows[(int)kind]
            : throw new InvalidOperationException("A storage kernel kind has no row.");

    // SPEC 22.2: the layout projection's Tuple {handle address, entry stride}, its elements at offsets 0 and 8.
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

    // SPEC 4.9.6, 22.2: the address of a key or value at its offset inside an entry slot, for placeEntry's helper (unreachable since R8-K1b-2; R8-K1b-3).
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
}
