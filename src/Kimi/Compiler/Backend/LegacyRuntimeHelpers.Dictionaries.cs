// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1402 // The Dictionary helper records are this concern's request vocabulary.

using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal enum DictionaryHelperKind : byte
{
    CheckKey,
    Place,
    Drop,
}

/// <summary>The physical layout of a Dictionary entry: the key and value offsets and the entry stride.</summary>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal readonly record struct DictionaryEntryLayout(long KeyOffset, long ValueOffset, long Stride);

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

/// <summary>Physical Dictionary entry helper; source calls resolve to the ordinary instantiated ABI. Related indexes
/// <see cref="LegacyRuntimeHelpers.FunctionReferences"/>, or is -1.</summary>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal sealed record DictionaryHelper(
    DictionaryHelperKind Kind, FunctionAbi Abi, StoredValue Key, StoredValue Value, DictionaryEntryLayout Entry, int Related)
{
    /// <summary>Creates the helper of a kind for an entry, named and shaped by the kind's row.</summary>
    /// <param name="kind">The helper kind.</param>
    /// <param name="key">The key descriptor.</param>
    /// <param name="value">The value descriptor.</param>
    /// <param name="entry">The entry layout.</param>
    /// <param name="related">The index of the helper's Entries instance in the function references, or -1.</param>
    /// <param name="suffix">The generator's name suffix, which follows the kind's name prefix.</param>
    /// <returns>The helper.</returns>
    internal static DictionaryHelper Create(
        DictionaryHelperKind kind, StoredValue key, StoredValue value, DictionaryEntryLayout entry, int related, string suffix)
        => new(kind, LegacyRuntimeHelpers.DictionaryRow(kind).Shape(suffix, key, value), key, value, entry, related);
}

/// <summary>SPEC 4.3, 4.9.6, 22.2: the Dictionary runtime, the storage byte primitives and the per-entry Dictionary helpers over
/// the compiled DictionaryStorage sources (Windows x64 profile).</summary>
internal sealed partial class LegacyRuntimeHelpers
{
    // SPEC 22.2, 21.4.3: the nullable form of Alloc returns null wherever Alloc would Abort, and the byte copy moves no
    // responsibility; neither has a location to report.
    private const string StorageBytePrimitives = """
        define internal ptr @__kimi_try_allocate(i64 %bytes) #0 {
        entry:
          %invalid = icmp ugt i64 %bytes, 9223372036854775807
          br i1 %invalid, label %failed, label %heap
        heap:
          %process_heap = call ptr @GetProcessHeap()
          %no_heap = icmp eq ptr %process_heap, null
          br i1 %no_heap, label %failed, label %try
        try:
          %zero = icmp eq i64 %bytes, 0
          %actual = select i1 %zero, i64 1, i64 %bytes
          %memory = call ptr @HeapAlloc(ptr %process_heap, i32 0, i64 %actual)
          ret ptr %memory
        failed:
          ret ptr null
        }
        define internal void @__kimi_transfer_bytes(ptr %destination, ptr %source, i64 %size) #0 {
        entry:
          call void @llvm.memcpy.p0.p0.i64(ptr %destination, ptr %source, i64 %size, i1 false)
          ret void
        }

        """;

    // The duplicate-key callback {environment, table}; its environment is the address of the caller's location record.
    private const string DuplicateCallback = """
          %origin = alloca { ptr, i64 }, align 8
          store ptr %location, ptr %origin, align 8
          %length_ptr = getelementptr i8, ptr %origin, i64 8
          store i64 %location_length, ptr %length_ptr, align 8
          %environment = ptrtoint ptr %origin to i64
          %duplicate = alloca { i64, ptr }, align 8
          store i64 %environment, ptr %duplicate, align 8
          %duplicate_table = getelementptr i8, ptr %duplicate, i64 8
          store ptr @__kimi_dictionary_duplicate_table, ptr %duplicate_table, align 8

        """;

    // The literal duplicate-key adapter keeps the later key's location; the capacity algorithms are DictionaryStorage source.
    private static readonly string DuplicateAdapter = """
        @__kimi_dictionary_duplicate_table = private constant { ptr, ptr, ptr } { ptr @__kimi_dictionary_duplicate, ptr null, ptr null }, align 8
        define internal void @__kimi_dictionary_duplicate(i64 %environment, ptr %context) #0 {
        entry:
          %origin = inttoptr i64 %environment to ptr
          %location = load ptr, ptr %origin, align 8
          %length_ptr = getelementptr i8, ptr %origin, i64 8
          %location_length = load i64, ptr %length_ptr, align 8
          call void @__kimi_abort(i32 REASON_DUPLICATE, ptr %location, i64 %location_length, i64 -2)
          unreachable
        }

        """.Replace("REASON_DUPLICATE", Number(AbortReasons.DuplicateKey), StringComparison.Ordinal);

    // One row per kind, in DictionaryHelperKind order; a new kind adds a row. A request joins the row's needs to the module's:
    // CheckKey's duplicate callback reports through the Dictionary runtime's adapter.
    private static readonly DictionaryHelperRow[] DictionaryRows =
    [
        new(
            DictionaryHelperKind.CheckKey,
            static (suffix, key, _) => Located("__kimi_dictionary_checkkey" + suffix, [Argument(key, "key")]),
            static (output, helper, find, requireAbsent) => WriteKeyCheck(output, helper, find!, requireAbsent!),
            DictionaryCallee.FindEntry,
            DictionaryCallee.RequireAbsent,
            HelperNeeds.ArrayRuntime | HelperNeeds.DictionaryRuntime),
        new(
            DictionaryHelperKind.Place,
            static (suffix, key, value) => Located("__kimi_dictionary_place" + suffix, [Argument(key, "key"), Argument(value, "value")]),
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
            HelperNeeds.ArrayRuntime | HelperNeeds.ElementGlue),
    ];

    // The module's resolved DictionaryStorage functions, indexed by DictionaryCallee (KimiLibrary's view, F2c); None and the
    // generic ones stay null.
    private readonly FunctionAbi?[] dictionaryCallees = new FunctionAbi?[(int)DictionaryCallee.Reserve + 1];

    // The per-entry Dictionary helpers requested by lowered bodies, in first-request order.
    private readonly List<DictionaryHelper> dictionaryHelpers = new();

    internal IReadOnlyList<DictionaryHelper> DictionaryHelpers => this.dictionaryHelpers;

    /// <summary>Gets or sets a value indicating whether the module holds the Dictionary runtime: a CheckKey request needs it, and the
    /// old lowering requests it for a Dictionary's construction until the runtime declares its own needs (C8e).</summary>
    internal bool NeedsDictionaryRuntime { get; set; }

    /// <summary>Gets or sets a value indicating whether a lowered body uses the private storage byte primitives (SPEC 22.2).</summary>
    internal bool NeedsStorageBytes { get; set; }

    /// <summary>Gets the row of a Dictionary helper kind; a kind without a row is a broken contract.</summary>
    /// <param name="kind">The helper kind.</param>
    /// <returns>The kind's row.</returns>
    internal static DictionaryHelperRow DictionaryRow(DictionaryHelperKind kind)
        => (uint)kind < (uint)DictionaryRows.Length && DictionaryRows[(int)kind].Kind == kind
            ? DictionaryRows[(int)kind]
            : throw new InvalidOperationException("A Dictionary helper kind has no row.");

    /// <summary>Gets the module's ABI of a DictionaryStorage function, or null when it was not compiled.</summary>
    /// <param name="callee">The function.</param>
    /// <returns>Its compiled ABI.</returns>
    internal FunctionAbi? Callee(DictionaryCallee callee) => this.dictionaryCallees[(int)callee];

    /// <summary>Records the module's ABI of a DictionaryStorage function.</summary>
    /// <param name="callee">The function.</param>
    /// <param name="abi">Its compiled ABI, or null when it was not compiled as one function (absent, or generic over the entry
    /// Types).</param>
    internal void Resolve(DictionaryCallee callee, FunctionAbi? abi) => this.dictionaryCallees[(int)callee] = abi;

    /// <summary>Registers a Dictionary helper a lowered body uses: each helper is written once per module, in first-request order,
    /// and the runtimes its kind's row declares join the module's.</summary>
    /// <param name="helper">The requested helper.</param>
    internal void Request(DictionaryHelper helper)
    {
        this.JoinNeeds(DictionaryRow(helper.Kind).Needs);
        if (!this.dictionaryHelpers.Contains(helper))
        {
            this.dictionaryHelpers.Add(helper);
        }
    }

    /// <summary>Gets a value indicating whether a Dictionary helper's Related index or a callee its row declares is unresolved.</summary>
    /// <returns>True when the requests cannot be written.</returns>
    internal bool HasUnresolvedReference()
    {
        foreach (var helper in this.dictionaryHelpers)
        {
            var row = DictionaryRow(helper.Kind);
            if (helper.Related < -1 || helper.Related >= this.FunctionReferences.Count ||
                (helper.Related < 0 && row.CallsEntries(helper.Key.NeedsDestruction || helper.Value.NeedsDestruction)) ||
                (row.Storage != DictionaryCallee.None && this.Callee(row.Storage) is null))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Writes the storage byte primitives when a body uses them, the Dictionary runtime when the module needs it, then each
    /// requested helper.</summary>
    /// <param name="output">The module text.</param>
    internal void WriteDictionaries(TextWriter output)
    {
        if (this.NeedsStorageBytes)
        {
            output.Write(StorageBytePrimitives);
        }

        if (this.NeedsDictionaryRuntime)
        {
            output.Write(DuplicateAdapter);
            this.WriteDictionaryInit(output);
        }

        foreach (var helper in this.dictionaryHelpers)
        {
            var row = DictionaryRow(helper.Kind);
            output.Write(helper.Abi.GetDefinition(exported: false));
            output.Write("entry:\n");
            row.Write(output, helper, helper.Related < 0 ? null : this.FunctionReferences[helper.Related], this.Callee(row.Storage));
            output.Write("}\n\n");
        }
    }

    // SPEC 4.3: a literal key is looked up through findEntry, and requireAbsent Aborts at the literal's location when it is present.
    private static void WriteKeyCheck(TextWriter output, DictionaryHelper helper, FunctionAbi find, FunctionAbi requireAbsent)
    {
        var scalar = helper.Key.IsScalar;
        if (scalar)
        {
            output.Write("  %key_slot = alloca ");
            output.Write(helper.Key.Value.Layout.StorageType);
            output.Write(", align ");
            WriteNumber(output, helper.Key.Value.Layout.Alignment);
            output.Write('\n');
            WriteStoredArgument(output, helper.Key, "%key", "%key_slot", "%key_input");
        }

        output.Write("  %link = call i64 @");
        output.Write(find.Name);
        output.Write("(ptr %handle, ptr ");
        output.Write(scalar ? "%key_slot" : "%key");
        output.Write(")\n");
        output.Write(DuplicateCallback);
        output.Write("  call void @");
        output.Write(requireAbsent.Name);
        output.Write("(i64 %link, ptr %duplicate)\n  ret void\n");
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

    // The empty-Dictionary initialization bridge; the ordered-link algorithms are compiled from DictionaryStorage.kimi, and the
    // functions initialize calls compile with it.
    private void WriteDictionaryInit(TextWriter output)
    {
        if (this.Callee(DictionaryCallee.Initialize) is not { } initialize)
        {
            throw new InvalidOperationException("Dictionary storage sources were not compiled.");
        }

        output.Write(RuntimeAbi.DictionaryInit.GetDefinition(exported: false));
        output.Write("entry:\n  call void @");
        output.Write(initialize.Name);
        output.Write("(ptr %handle)\n  ret void\n}\n\n");
    }
}
