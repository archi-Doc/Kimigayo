// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1402 // The Dictionary helper records are this concern's request vocabulary.

using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal enum DictionaryHelperKind : byte
{
    Place,
    Drop,
}

/// <summary>The physical layout of a Dictionary entry: the key and value offsets and the entry stride.</summary>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal readonly record struct DictionaryEntryLayout(long KeyOffset, long ValueOffset, long Stride)
{
    /// <summary>Gets the layout of an entry (IMPL 21.1.3, implementation-defined), the one rule code generation reads (PD2): its two links
    /// first, then the key at the first offset from 16 aligned for it, the value after the key aligned for it, and the stride the value's
    /// end aligned for the entry, whose alignment is at least the links' 8.</summary>
    /// <param name="key">The key's layout.</param>
    /// <param name="value">The value's layout.</param>
    /// <returns>The entry layout.</returns>
    internal static DictionaryEntryLayout Of(TypeLayout key, TypeLayout value)
    {
        var alignment = Math.Max(8, Math.Max(key.Alignment, value.Alignment));
        var keyOffset = Align(16, key.Alignment);
        var valueOffset = Align(keyOffset + key.Size, value.Alignment);
        return new(keyOffset, valueOffset, Align(valueOffset + value.Size, alignment));
    }

    private static long Align(long size, int alignment) => (size + alignment - 1) & -(long)alignment;
}

/// <summary>The row of one Dictionary helper kind, readable by kind before any representation exists (X4): its ABI shape over the
/// generator's name suffix (after the kind's name prefix) and the key and value descriptors, its body's writer over the helper and the ABIs of its Entries and
/// Storage callees, the generic DictionaryStorage function its helpers call over the entry Types (Entries), the one the module
/// resolves (Storage), and its declared needs.</summary>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal sealed record DictionaryHelperRow(
    DictionaryHelperKind Kind,
    Func<string, StoredValue, StoredValue, FunctionAbi> Shape,
    Action<TextWriter, DictionaryHelper, FunctionAbi?, FunctionAbi?> Write,
    DictionaryCallee Entries,
    DictionaryCallee Storage,
    HelperNeeds Needs)
{
    /// <summary>Gets a value indicating whether a helper of the kind calls its Entries function: always, or under
    /// <see cref="HelperNeeds.ElementGlue"/> only when an entry needs destruction.</summary>
    /// <param name="destroysEntries">Whether the key or the value needs destruction.</param>
    /// <returns>True when the helper calls the instance of its Entries function.</returns>
    internal bool CallsEntries(bool destroysEntries)
        => this.Entries != DictionaryCallee.None && (destroysEntries || (this.Needs & HelperNeeds.ElementGlue) == 0);
}

/// <summary>Physical Dictionary entry helper; source calls resolve to the ordinary instantiated ABI.</summary>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal sealed record DictionaryHelper(
    DictionaryHelperKind Kind, FunctionAbi Abi, StoredValue Key, StoredValue Value, DictionaryEntryLayout Entry)
{
    /// <summary>Creates the helper of a kind for an entry, named and shaped by the kind's row.</summary>
    /// <param name="kind">The helper kind.</param>
    /// <param name="key">The key descriptor.</param>
    /// <param name="value">The value descriptor.</param>
    /// <param name="entry">The entry layout.</param>
    /// <param name="suffix">The generator's name suffix, which follows the kind's name prefix.</param>
    /// <returns>The helper.</returns>
    internal static DictionaryHelper Create(
        DictionaryHelperKind kind, StoredValue key, StoredValue value, DictionaryEntryLayout entry, string suffix)
        => new(kind, LegacyRuntimeHelpers.DictionaryRow(kind).Shape(suffix, key, value), key, value, entry);
}

/// <summary>SPEC 4.3, 4.9.6: the Dictionary runtime and the per-entry Dictionary helpers over the compiled DictionaryStorage sources
/// (Windows x64 profile); the storage byte primitives they write first are the storage kernels' (LegacyRuntimeHelpers.Storage.cs).</summary>
internal static partial class LegacyRuntimeHelpers
{
    /// <summary>The DictionaryStorage function a Dictionary's construction from none runs (SPEC 4.3, 4.9.5), the callee the Dictionary
    /// runtime declares (X4; DECISIONS R5-M27, R5-M124): the instance collector targets its body for a Dictionary from none,
    /// which MIR code generation calls.</summary>
    internal const DictionaryCallee Construction = DictionaryCallee.Initialize;

    // One row per kind, in DictionaryHelperKind order; a new kind adds a row. Place copies its key and value through the memory
    // declarations of the Array runtime, and Drop releases through the main runtime.
    private static readonly DictionaryHelperRow[] DictionaryRows =
    [
        new(
            DictionaryHelperKind.Place,
            static (suffix, key, value) => Located("__kimi_dictionary_place" + suffix, [Argument(key, "key", 1), Argument(value, "value", 2)]),
            static (output, helper, _, append) => WriteEntryPlacement(output, helper, append!),
            DictionaryCallee.None,
            DictionaryCallee.Append,
            HelperNeeds.ArrayRuntime),
        new(
            DictionaryHelperKind.Drop,
            static (suffix, _, _) => Located("__kimi_dictionary_drop" + suffix, []),
            static (output, _, clear, _) => WriteDictionaryDrop(output, clear),
            DictionaryCallee.ClearEntries,
            DictionaryCallee.None,
            HelperNeeds.ElementGlue),
    ];

    /// <summary>Gets the row of a Dictionary helper kind; a kind without a row is a broken contract.</summary>
    /// <param name="kind">The helper kind.</param>
    /// <returns>The kind's row.</returns>
    internal static DictionaryHelperRow DictionaryRow(DictionaryHelperKind kind)
        => (uint)kind < (uint)DictionaryRows.Length && DictionaryRows[(int)kind].Kind == kind
            ? DictionaryRows[(int)kind]
            : throw new InvalidOperationException("A Dictionary helper kind has no row.");

    /// <summary>Writes one Dictionary helper's definition: its ABI and its kind row's writer over the ABIs of the row's Entries instance and
    /// Storage callee (PD2).</summary>
    /// <param name="output">The module text.</param>
    /// <param name="helper">The helper.</param>
    /// <param name="entries">The ABI of the helper's Entries instance, or null when it calls none.</param>
    /// <param name="storage">The ABI of its row's Storage callee, or null when its row declares none.</param>
    internal static void WriteDictionary(TextWriter output, DictionaryHelper helper, FunctionAbi? entries, FunctionAbi? storage)
    {
        output.Write(helper.Abi.GetDefinition(exported: false));
        output.Write("entry:\n");
        DictionaryRow(helper.Kind).Write(output, helper, entries, storage);
        output.Write("}\n\n");
    }

    // SPEC 4.9.6: typed placement only; the ordinary source append body owns growth, ordering and free-slot reuse, and reports
    // its failures at the forwarded caller location.
    private static void WriteEntryPlacement(TextWriter output, DictionaryHelper helper, FunctionAbi append)
    {
        output.Write("  %new_slot = call ptr @");
        output.Write(append.Name);
        output.Write("(ptr %handle, i64 ");
        WriteNumber(output, helper.Entry.Stride);
        output.Write(", ptr %location, i64 %location_length)\n");
        WriteEntryAddress(output, "%new_key", "%new_slot", helper.Entry.KeyOffset);
        WriteEntryAddress(output, "%new_value", "%new_slot", helper.Entry.ValueOffset);
        WriteStoredArgument(output, helper.Key, "%key", "%new_key", "%key_inserted");
        WriteStoredArgument(output, helper.Value, "%value", "%new_value", "%value_inserted");
        output.Write("  ret void\n");
    }

    // SPEC 4.9.7: clearEntries destroys the live entries when they need destruction; the buffer is then released.
    private static void WriteDictionaryDrop(TextWriter output, FunctionAbi? clear)
    {
        if (clear is not null)
        {
            output.Write("  call void @");
            output.Write(clear.Name);
            output.Write("(ptr %handle, ptr %location, i64 %location_length)\n");
        }

        output.Write("  call void @__kimi_array_free(ptr %handle, ptr %location, i64 %location_length)\n  ret void\n");
    }
}
