// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;

namespace Kimi.Compiler.Backend;

// The Array legacy rows of ModuleCodegen (R5 plan C8d, package C 3.4 and 3.6; S2-S4 X4, X39; DECISIONS R5-M121), until R8 writes them in
// Kimigayo. A LegacyRow item's routine (Row), which its calls, its function's Item's Value calls and its adapter's entry call (FunctionCodegen's
// Instance rows): its own helper, or the capacity routine its row names, with one given number, its element's stride or, for a fixed array's
// ownStorage, the array's length. A helper is P1b's row text, the one copy both generators write (PD2; LegacyRuntimeHelpers.WriteArray), named
// after its item by its kind's prefix over its element descriptor in the item's instance (Helper): the element's Layout record, the name of
// the element glue its text destroys by (its item's target, so a string element dies by its glue) and, by its kind, the Option payload's
// offset (Pop: the Some Case's element at the Option record's Field 1) or the remainder record's Fields in declaration order (storage,
// position, count, and OwnedRemainder's capacity) with Field 0's size; an Array's glue calls its glue item's Drop helper (ArrayDrop). Each
// helper is made once per Write, its text then written to the helper buffer the frame writes before the definitions (PD8); Row requests the
// runtime its row declares (LegacyRow.Needs through Request, R5-M46: Needs.Arrays), for a helper, a capacity routine and placeEntry's Place helper
// alike, whose routine is ModuleCodegen.Dictionaries' (DECISIONS R5-M124); a kept helper is reused while its descriptor matches, so a warm
// module allocates nothing. An element or a record without its layout is a broken contract at the item's site (the collector laid them out).
internal sealed partial class ModuleCodegen
{
    private readonly StringWriter helperText = new(CultureInfo.InvariantCulture); // The helpers of the latest Write, before the definitions.
    private ArrayHelper?[] arrays = []; // By LegacyRow or Array DropGlue item, its helper, kept across Writes.
    private int[] arranged = []; // By item, the Write whose helper `arrays` holds (0: none).

    /// <summary>Gets the routine of a LegacyRow item (DECISIONS R5-M121, R5-M124), which its calls and its function's Item's adapter call: its
    /// Array helper, or the capacity routine its row names, and the number the routine is given, its element's stride or a fixed array's
    /// length; or placeEntry's Place helper (ModuleCodegen.Dictionaries), which is given none.</summary>
    /// <param name="item">The LegacyRow item.</param>
    /// <param name="given">The routine's given number.</param>
    /// <returns>The routine; null after a record at the item's site.</returns>
    internal FunctionAbi? Row(int item, out long given)
    {
        given = 0;
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

        if (this.Element(item, collection) is not { } element)
        {
            return null; // Recorded.
        }

        given = row.Helper == ArrayHelperKind.OwnFixedStorage ? collection.Length : element.Layout.Stride;
        return row.Helper is { } kind ? this.Helper(item, kind, element)?.Abi : row.Routine;
    }

    /// <summary>Gets the Drop helper an Array's glue calls with its own parameters (package C 3.6; DECISIONS R5-M121): P1b's Drop row over the
    /// glue item's element, destroyed by the glue item's target (its Element part's glue), named after the glue item.</summary>
    /// <param name="glue">The Array's DropGlue item.</param>
    /// <returns>The helper's signature; null after a record at the item's site.</returns>
    internal FunctionAbi? ArrayDrop(int glue)
        => this.Element(glue, this.set.Items[glue].RepresentativeType!) is { } element ? this.Helper(glue, ArrayHelperKind.Drop, element)?.Abi : null;

    // The part of a helper's result whose layout its text reads: Pop's Some Case payload, a remainder's storage Field.
    private static BoundType? PartOf(BoundType result, ArrayHelperKind kind)
        => kind == ArrayHelperKind.Pop ? AdtDef.CaseTypes(result)?[0] : AdtDef.FieldType(result, 0);

    // The helper of a LegacyRow or Array DropGlue item, once per Write, its text written then: kept while its descriptor matches, else made and
    // named after the item (only then, so a warm module allocates nothing). Null after a record.
    private ArrayHelper? Helper(int item, ArrayHelperKind kind, ValueLowering element)
    {
        if (this.Stamped(ref this.arrays, ref this.arranged, item) is { } current)
        {
            return current;
        }

        if (this.ResultParts(item, kind) is not { OptionPayload: var payload, Fields: var fields })
        {
            return null; // Recorded.
        }

        var targets = this.set.Targets(item);
        var stored = new StoredValue(element, targets.Length > 0 ? this.Glue(targets[0].Item).Name : null, Text: false);
        var helper = this.arrays[item] is { } kept && (kept.Kind, kept.Element, kept.OptionPayload, kept.Fields) == (kind, stored, payload, fields)
            ? kept : ArrayHelper.Create(kind, stored, item.ToString(CultureInfo.InvariantCulture), string.Empty, payload, fields);
        (this.arrays[item], this.arranged[item]) = (helper, this.writes);
        LegacyRuntimeHelpers.WriteArray(this.helperText, helper);
        return helper;
    }

    // The parts of a helper's descriptor that its item's instance gives from its function's result (TyCtxt.Signature): Pop's Option payload
    // offset, its Some Case's (the ordinal 0 P1b's text tags) element at the Option record's Field 1 plus the element's offset in the Case
    // payload; a storage helper's remainder Fields in declaration order and its storage Field's size, ownStorage's also OwnedRemainder's
    // capacity. Null after a record.
    private DescriptorParts? ResultParts(int item, ArrayHelperKind kind)
    {
        if (kind is not (ArrayHelperKind.Pop or ArrayHelperKind.BorrowStorage or ArrayHelperKind.OwnStorage or ArrayHelperKind.OwnFixedStorage))
        {
            return new DescriptorParts(null, default);
        }

        var entry = this.set.Items[item];
        var declared = TyCtxt.Signature(entry.Key.Body.Def.Decl!, entry.Representative, -1, out _);
        var result = declared is null ? null : this.collector.TypeOf(item, declared);
        if (result is null || this.Layout.Of(result).Value is not { } record || PartOf(result, kind) is not { } inner ||
            this.Layout.Of(inner).Value is not { } part)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, entry.Site); // The collector laid out the call's result (K 4.8).
            return null;
        }

        var offsets = record.Layout.FieldOffsets.Span;
        if (kind == ArrayHelperKind.Pop)
        {
            return new DescriptorParts(offsets[1] + part.Layout.FieldOffsets.Span[0], default);
        }

        return new DescriptorParts(
            null, new(offsets[0], offsets[1], offsets[2], kind == ArrayHelperKind.OwnStorage ? offsets[3] : 0, part.Layout.Size));
    }

    // The element a LegacyRow or Array DropGlue item's helper stores, its collection's first component (an Array's, a UniqSlice's or a fixed
    // array's element), by its Layout record; null, recorded at the item's site, without one (the collector laid it out, X34).
    private ValueLowering? Element(int item, BoundType collection)
    {
        if (collection.Components.Count > 0 && this.Layout.Of(collection.Components[0]).Value is { } element)
        {
            return element;
        }

        this.Report(DiagnosticCode.InternalInvariant_Kd, this.set.Items[item].Site);
        return null;
    }

    // The parts of a helper's descriptor its function's result gives (ResultParts), as ArrayHelper holds them: Pop's Option payload offset
    // and a storage helper's remainder Fields.
    private readonly record struct DescriptorParts(int? OptionPayload, RemainderFields Fields);
}
