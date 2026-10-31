// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// Destruction and static edges (K package 4.5 rows 7, 9 and 10, "DropGlue"; S2-S4 X3, X4, X6, X23, X33 (a), X34; DECISIONS R5-M60, R5-M116,
// R5-M121, R5-M124), the glue of what a call destroys in place, a DropGlue item's targets and a LegacyRow item's.
internal sealed partial class InstanceCollector
{
    // K 4.5 rows 9 and 10: the DropGlue item of a Type an item's instance destroys (GlueOf). -1 for a Type TyCtxt cannot make there.
    private int Glue(int item, SourceRef subject, BoundType type) => this.In(item, subject, type) is { } closed ? this.GlueOf(item, subject, closed) : -1;

    // The DropGlue item of a closed Type, keyed by its erased Type, for a Type whose destruction runs or releases something
    // (MirTypes.MayNeedDrop), a call's in-place destruction's too (SPEC 15.7.1, 16.3.2; DECISIONS R5-M116: replace's published row); A2's
    // verdicts do not narrow the checked set (codegen reads them). -1 for a Type with nothing to destroy or a refused edge.
    private int GlueOf(int item, SourceRef subject, BoundType closed)
        => MirTypes.MayNeedDrop(closed, this.seen) ? this.AdmitType(item, subject, new(MonoKind.DropGlue, default, null, null, 0, null), closed, null) : -1;

    // K 4.5 row 7: the StaticInit body a staticEnsure names (MirProgram.StaticInit, F10), which the validator checks the program has (G191).
    // A static of a generic container's group, whose storage the MIR keys by its symbol alone (SPEC 22.4.4), has no instance to admit: its
    // declaration is the static-storage gap once (DECISIONS R5-M82, which supersedes R5-M60's record at each access; the bundled library
    // declares none, as its rooted check shows).
    private int Static(MirProgram program, int item, MirBody body, SourceRef subject, in MirTerminator ensure)
    {
        var initializer = program.StaticInit(body, body.Operands[ensure.Start])!;
        return this.types.Instantiates(initializer.Key.Decl!) ? -1 : this.Admit(item, subject, initializer, null);
    }

    // K 4.5 "DropGlue": a DropGlue's targets, at its default site, its direct parts in destruction order (DropGlue.Parts): a destructor's
    // Body under TyCtxt.Synthesized (R4-M6b), or each part's, element's or Sealed payload's glue; a Dictionary's, what its legacy Drop row
    // declares (Entries).
    private void DestructionTargets(MirProgram program, int item, SourceRef site, BoundType type)
    {
        if (DropGlue.Row(type) == DropGlueRow.Dictionary)
        {
            this.Target(item, this.Entries(program, item, site, type));
            return;
        }

        var parts = this.parts;
        var start = parts.Count;
        DropGlue.Parts(this.types, type, parts);
        for (var i = start; i < parts.Count; i++)
        {
            var part = parts[i];
            this.Target(item, part.Kind == DropPartKind.Destructor ? this.Destructor(program, item, site, part.Type) : this.Glue(item, site, part.Type));
        }

        parts.RemoveRange(start, parts.Count - start);
    }

    // X4 (DECISIONS R5-M121, R5-M124): a LegacyRow item's targets, at its default site, the callee and needs its row declares: the body of
    // its library callee (placeEntry's append, Declared; a Storage callee is not generic, R5-M27); for ElementGlue (clear), the glue its
    // helper destroys each element by, of each Element part of its representative Type (DropGlue.Parts) that may need a drop.
    private void RowTargets(MirProgram program, int item, SourceRef site, BoundType collection)
    {
        var row = LegacyRuntimeHelpers.Row(this.Set.Items[item].Key.Body.Def.Decl!.CompilerFunction)!.Value;
        if (row.Callee != DictionaryCallee.None)
        {
            this.Target(item, this.Declared(program, item, site, row.Callee, null));
        }

        if ((row.Needs & HelperNeeds.ElementGlue) == 0)
        {
            return;
        }

        var parts = this.parts;
        var start = parts.Count;
        DropGlue.Parts(this.types, collection, parts);
        for (var i = start; i < parts.Count; i++)
        {
            if (parts[i].Kind == DropPartKind.Element)
            {
                this.Target(item, this.Glue(item, site, parts[i].Type));
            }
        }

        parts.RemoveRange(start, parts.Count - start);
    }

    // K 4.8, X34: whether a DropGlue's collection element Types are laid out: a collection's own layout is its handle words, and its glue
    // lays out its elements even when they need no drop (as the old CollectionDrop does), so their verdicts join the glue item's, reported
    // at its request (the requesting item's use) by the location rule.
    private bool ElementsLayOut(int item, SourceRef site, BoundType type)
    {
        var parts = this.parts;
        var start = parts.Count;
        var laidOut = true;
        DropGlue.Parts(this.types, type, parts);
        for (var i = start; i < parts.Count; i++)
        {
            laidOut &= parts[i].Kind != DropPartKind.Element || this.LaysOut(item, site, parts[i].Type);
        }

        parts.RemoveRange(start, parts.Count - start);
        return laidOut;
    }

    // SPEC 4.9.7, 16.3.2, X4 (DECISIONS R5-M27, R5-M124; the owner's R5-M125): a Dictionary glue's target, what its legacy Drop row declares:
    // the instance of its Entries callee, clearEntries over the entry Types (TyCtxt.Synthesized), when an entry may need a drop (CallsEntries),
    // whose body destroys the entries in reverse insertion order, reaching their glue by its own edges; else none.
    private int Entries(MirProgram program, int item, SourceRef site, BoundType dictionary)
    {
        var row = LegacyRuntimeHelpers.DictionaryRow(DictionaryHelperKind.Drop);
        var key = dictionary.Components[0];
        var value = dictionary.Components[1];
        if (!row.CallsEntries(MirTypes.MayNeedDrop(key, this.seen) || MirTypes.MayNeedDrop(value, this.seen)))
        {
            return -1;
        }

        return this.Declared(program, item, site, row.Entries, this.types.Synthesized(this.types.DictionaryStorage(row.Entries), null, [key, value]));
    }

    // R4-M6b, SPEC 16.3.2: the Body of a struct's declared destructor in the struct's instance (TyCtxt.Synthesized), which destroys the
    // struct's Fields and base itself; a destructor without a body is one Binding rejected, so the edge derives from that error.
    private int Destructor(MirProgram program, int item, SourceRef site, BoundType type)
    {
        var destructor = AdtDef.DestructorSymbol(type)!;
        return program[new(destructor, MirBodyKind.Fn)] is { } body ? this.Admit(item, site, body, this.types.Synthesized(destructor, type, [])) : -1;
    }
}
