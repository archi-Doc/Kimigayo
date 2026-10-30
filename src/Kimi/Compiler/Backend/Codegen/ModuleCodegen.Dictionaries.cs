// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;

namespace Kimi.Compiler.Backend;

// The Dictionary legacy rows of ModuleCodegen (R5 plan C8e, package C 3.4 and 3.6; S2-S4 X4, X39; DECISIONS R5-M124), until R8 writes them in
// Kimigayo: placeEntry's LegacyRow item's routine (Row's Place writer), which its calls, its function's Item's Value calls and its adapter's
// entry call, and a Dictionary glue item's Drop helper (DictionaryDrop), which the glue calls with its own parameters. Each is P1c's row
// text, the one copy both generators write (PD2; LegacyRuntimeHelpers.WriteDictionary), named after its item by its kind's prefix
// (__kimi_dictionary_place_<item>, __kimi_dictionary_drop_<item>), over its Dictionary's entry: the key's and the value's Layout records
// and their entry layout (DictionaryEntryLayout.Of); the callee its row declares is its item's target, by its signature: Place's Storage
// callee, the append body its LegacyRow item targets; Drop's Entries callee, the clearEntries instance its glue targets when an entry may
// need a drop (DictionaryHelperRow.CallsEntries), else none. Each helper is made once per Write, its text then written to the helper buffer
// the frame writes before the definitions (PD8); a kept helper is reused while its descriptor matches, so a warm module allocates nothing.
// A Dictionary without its entry Types' layouts is a broken contract at the item's site (the collector laid them out, X34).
internal sealed partial class ModuleCodegen
{
    private DictionaryHelper?[] dictionaries = []; // By placeEntry LegacyRow or Dictionary DropGlue item, its helper, kept across Writes.
    private int[] placed = []; // By item, the Write whose helper `dictionaries` holds (0: none).

    /// <summary>Gets the Drop helper a Dictionary's glue calls with its own parameters (package C 3.6; DECISIONS R5-M124): P1c's Drop row over
    /// the glue item's entry, which calls the glue item's target, the clearEntries instance, when it has one, named after the glue item.</summary>
    /// <param name="glue">The Dictionary's DropGlue item.</param>
    /// <returns>The helper's signature; null after a record at the item's site.</returns>
    internal FunctionAbi? DictionaryDrop(int glue) => this.Dictionary(glue, DictionaryHelperKind.Drop)?.Abi;

    // The helper of a placeEntry LegacyRow item or of a Dictionary DropGlue item, once per Write, its text written then: kept while its
    // descriptor matches, else made and named after the item (only then, so a warm module allocates nothing). Null after a record.
    private DictionaryHelper? Dictionary(int item, DictionaryHelperKind kind)
    {
        if (this.Stamped(ref this.dictionaries, ref this.placed, item) is { } current)
        {
            return current;
        }

        var entry = this.set.Items[item];
        var targets = this.set.Targets(item);
        if (entry.RepresentativeType!.Components is not [var keyType, var valueType] || this.Layout.Of(keyType).Value is not { } key ||
            this.Layout.Of(valueType).Value is not { } value || !this.Declares(targets))
        {
            // The collector laid out the entry Types (X34) and targets the one callee the row declares, a body, or none (X4).
            this.Report(DiagnosticCode.InternalInvariant_Kd, entry.Site);
            return null;
        }

        var callee = targets.Length > 0 ? this.Signature(targets[0].Item) : null;
        if (targets.Length > 0 && callee is null)
        {
            return null; // Recorded.
        }

        var stored = new StoredValue(key, null, Text: false);
        var storedValue = new StoredValue(value, null, Text: false);
        var helper = this.dictionaries[item] is { } kept && (kept.Kind, kept.Key, kept.Value) == (kind, stored, storedValue) ? kept
            : DictionaryHelper.Create(
                kind, stored, storedValue, DictionaryEntryLayout.Of(key.Layout, value.Layout), -1, "_" + item.ToString(CultureInfo.InvariantCulture));
        (this.dictionaries[item], this.placed[item]) = (helper, this.writes);
        var row = LegacyRuntimeHelpers.DictionaryRow(kind);
        var entries = row.Entries == DictionaryCallee.None ? null : callee;
        LegacyRuntimeHelpers.WriteDictionary(this.helperText, helper, entries, row.Storage == DictionaryCallee.None ? null : callee);
        return helper;
    }

    // Whether a Dictionary row item's targets are what its row declares (X4): its one callee's body, or none.
    private bool Declares(ReadOnlySpan<MonoTarget> targets)
        => targets.Length == 0 || (targets.Length == 1 && this.set.Items[targets[0].Item].Key.Kind == MonoKind.Body);
}
