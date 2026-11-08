// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1402 // The Dictionary helper records are this concern's request vocabulary.

namespace Kimi.Compiler.Backend;

[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal enum DictionaryHelperKind : byte
{
    Drop,
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
internal sealed record DictionaryHelper(DictionaryHelperKind Kind, FunctionAbi Abi, StoredValue Key, StoredValue Value)
{
    /// <summary>Creates the helper of a kind for an entry, named and shaped by the kind's row.</summary>
    /// <param name="kind">The helper kind.</param>
    /// <param name="key">The key descriptor.</param>
    /// <param name="value">The value descriptor.</param>
    /// <param name="suffix">The generator's name suffix, which follows the kind's name prefix.</param>
    /// <returns>The helper.</returns>
    internal static DictionaryHelper Create(DictionaryHelperKind kind, StoredValue key, StoredValue value, string suffix)
        => new(kind, LegacyRuntimeHelpers.DictionaryRow(kind).Shape(suffix, key, value), key, value);
}

/// <summary>SPEC 4.3, 4.9.7: the Dictionary runtime and the per-entry Dictionary helpers over the compiled DictionaryStorage sources
/// (Windows x64 profile).</summary>
internal static partial class LegacyRuntimeHelpers
{
    /// <summary>The DictionaryStorage function a Dictionary's construction from none runs (SPEC 4.3, 4.9.5), the callee the Dictionary
    /// runtime declares (X4; DECISIONS R5-M27, R5-M124): the instance collector targets its body for a Dictionary from none,
    /// which MIR code generation calls.</summary>
    internal const DictionaryCallee Construction = DictionaryCallee.Initialize;

    // One row per kind, in DictionaryHelperKind order; a new kind adds a row. Drop releases through the main runtime.
    // The handle a Value at slot 0 (IMPL 21.3.1), as the internal convention's kinds and logical indices place it.
    private static readonly AbiParameter Handle = new("ptr", "handle", AbiParameterKind.Value, 0);

    private static readonly DictionaryHelperRow[] DictionaryRows =
    [
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

    // A signature without a result: the handle, the leading parameters and the caller's location (SPEC 22.5.1).
    private static FunctionAbi Located(string name, ReadOnlySpan<AbiParameter> leading)
        => new(name, RuntimeAbi.Unit.ComputationType, [Handle, .. leading, RuntimeAbi.Location, RuntimeAbi.LocationLength]);
}
