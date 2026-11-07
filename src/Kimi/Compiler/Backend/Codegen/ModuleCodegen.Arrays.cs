// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;

namespace Kimi.Compiler.Backend;

// The Array legacy rows of ModuleCodegen (R5 plan C8d, package C 3.4 and 3.6; S2-S4 X4, X39; DECISIONS R5-M121), until R8 writes them in
// Kimigayo. A LegacyRow item's routine (Row), which its calls, its function's Item's Value calls and its adapter's entry call (FunctionCodegen's
// Instance rows): its own helper, with one given number, for a fixed array's remainder (ArrayHelperRow.Result), the array's length. A helper
// is P1b's row text, the one copy (PD2; LegacyRuntimeHelpers.WriteArray), named after its item by its kind's prefix over, as its row's Result
// says, the remainder record's Fields in declaration order (storage, position, count, and OwnedRemainder's capacity) with Field 0's size.
// Each helper is made once per Write, its text then written to the helper buffer the frame writes before the definitions (PD8); Row requests
// the runtime its row declares (LegacyRow.Needs through Request, R5-M46: Needs.Arrays), for a helper and placeEntry's Place helper alike, whose
// routine is ModuleCodegen.Dictionaries' (DECISIONS R5-M124); a kept helper is reused while its descriptor matches, so a warm module
// allocates nothing. A record without its layout is a broken contract at the item's site (the collector laid them out).
internal sealed partial class ModuleCodegen
{
    private ArrayHelper?[] arrays = []; // By LegacyRow item, its helper, kept across Writes.
    private int[] arranged = []; // By item, the Write whose helper `arrays` holds (0: none).

    /// <summary>Gets the routine of a LegacyRow item (DECISIONS R5-M121, R5-M124), which its calls and its function's Item's adapter call: its
    /// Array helper and the number it is given, a fixed array's length; or placeEntry's Place helper (ModuleCodegen.Dictionaries), which is
    /// given none.</summary>
    /// <param name="item">The LegacyRow item.</param>
    /// <param name="given">The routine's given number.</param>
    /// <returns>The routine; null after a record at the item's site.</returns>
    internal FunctionAbi? Row(int item, out long given)
    {
        given = 0;
        this.Requested(item);
        var entry = this.set.Items[item];
        var collection = entry.RepresentativeType!;
        if (LegacyRuntimeHelpers.Row(entry.Key.Body.Def.Decl!.CompilerFunction) is not { InInstance: true } row)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, entry.Site); // A LegacyRow item's function runs in its instance (the collector's).
            return null;
        }

        this.Request(row.Needs);
        if (row.Writer == LegacyWriter.Place)
        {
            return this.Dictionary(item, DictionaryHelperKind.Place)?.Abi;
        }

        var kind = row.Helper!.Value;
        given = LegacyRuntimeHelpers.ArrayRow(kind).Result == ArrayHelperResult.FixedRemainder ? collection.Length : 0;
        return this.Helper(item, kind)?.Abi;
    }

    // The helper of a LegacyRow item, once per Write, its text written then: kept while its descriptor matches, else made and named after the
    // item (only then, so a warm module allocates nothing). Null after a record.
    private ArrayHelper? Helper(int item, ArrayHelperKind kind)
    {
        if (this.Stamped(ref this.arrays, ref this.arranged, item) is { } current)
        {
            return current;
        }

        if (this.ResultParts(item, kind) is not { } fields)
        {
            return null; // Recorded.
        }

        var helper = this.arrays[item] is { } kept && (kept.Kind, kept.Fields) == (kind, fields)
            ? kept : ArrayHelper.Create(kind, item.ToString(CultureInfo.InvariantCulture), string.Empty, fields);
        (this.arrays[item], this.arranged[item]) = (helper, this.writes);
        LegacyRuntimeHelpers.WriteArray(this.helperText, helper);
        return helper;
    }

    // The part of a helper's descriptor that its item's instance gives from its function's result (TyCtxt.Signature), as its kind's row
    // says (ArrayHelperRow.Result): a remainder's Fields in declaration order and its storage Field's size, an owned remainder's also its
    // capacity. Null after a record.
    private RemainderFields? ResultParts(int item, ArrayHelperKind kind)
    {
        var parts = LegacyRuntimeHelpers.ArrayRow(kind).Result;
        var entry = this.set.Items[item];
        var declared = TyCtxt.Signature(entry.Key.Body.Def.Decl!, entry.Representative, -1, out _);
        var result = declared is null ? null : this.collector.TypeOf(item, declared);
        if (result is null || this.Layout.Of(result).Value is not { } record || AdtDef.FieldType(result, 0) is not { } inner ||
            this.Layout.Of(inner).Value is not { } part)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, entry.Site); // The collector laid out the call's result (K 4.8).
            return null;
        }

        var offsets = record.Layout.FieldOffsets.Span;
        return new RemainderFields(offsets[0], offsets[1], offsets[2], parts == ArrayHelperResult.OwnedRemainder ? offsets[3] : 0, part.Layout.Size);
    }
}
