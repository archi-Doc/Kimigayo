// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Backend;

// Destruction and static edges (K package 4.5 rows 7, 9 and 10, "DropGlue"; S2-S4 X3, X4, X6, X23, X33 (a), X34; DECISIONS R5-M60, R5-M116,
// R5-M121, R5-M124), the glue of what a call destroys in place and a DropGlue item's targets.
internal sealed partial class InstanceCollector
{
    // SPEC 8.15.3, 14.10.3, 16.2.1 (DECISIONS R4-M6a): whether a Drop of a body destroys nothing in a view: A2 judged its group Elide (on
    // the runtime paths, or on the assumed paths of a Drop only the checks reach), so its glue is no instance the body requires; a Drop
    // only the checks reach that may destroy something has no group and keeps its glue (8.15.3: reachable or not), and so does every Drop
    // of a body whose verdicts are inexact (MirDropTable.Incomplete).
    private static bool Elided(MirProgram program, MirBody body, int block, byte view)
        => program.Drops.Incomplete(body.Ordinal) is null && program.Drops.Elides(body.Ordinal, block, view);

    // K 4.5 rows 9 and 10: the DropGlue item of a Type an item's instance destroys (GlueOf). -1 for a Type TyCtxt cannot make there.
    private int Glue(int item, SourceRef subject, BoundType type) => this.In(item, subject, type) is { } closed ? this.GlueOf(item, subject, closed) : -1;

    // The DropGlue item of a closed Type, keyed by what its function reads (GlueKey), for a Type whose destruction runs or releases something
    // (MirTypes.MayNeedDrop), a call's in-place destruction's too (SPEC 15.7.1, 16.3.2; DECISIONS R5-M116: replace's published row); a Drop
    // A2 elides requests none (Elided). -1 for a Type with nothing to destroy or a refused edge.
    private int GlueOf(int item, SourceRef subject, BoundType closed)
        => MirTypes.MayNeedDrop(closed, this.seen) ? this.AdmitType(item, subject, this.GlueKey(closed), closed, null) : -1;

    // DECISIONS R5-M60 (amended by U11 and G372): the key of a glue item, what its function reads: its erased Type (AdmitType), except for a
    // Function value, whose glue (FunctionCodegen.Erasures' DestroyErased) reads only the one layout row of every Function Type, keyed by
    // the Function Type () -> (), one glue in all; and for an owning handle whose release destroys no payload glue (FunctionCodegen.Objects'
    // Release then reads its counting alone): a Sealed payload (DropGlue.Parts gives it) that needs no drop, or a payload the descriptor's
    // finalizer destroys (none given, Variant 1), keyed by the handle over Unit, so each such release has one glue per counting. A Sealed
    // payload that may need a drop keeps its Type's. Each handle Type is classified once per run (glueKeys): MayNeedDrop walks the
    // payload's destruction to its bound.
    private MonoKey GlueKey(BoundType closed)
    {
        MonoKey key = new(MonoKind.DropGlue, default, null, null, 0, null);
        var row = DropGlue.Row(closed);
        if (row == DropGlueRow.Function)
        {
            return key with { Type = this.types.Intern(BoundTypeKind.Function, null, SemanticsKind.Owner, [BoundType.Unit, BoundType.Unit]) };
        }

        if (row != DropGlueRow.Handle)
        {
            return key;
        }

        if (this.glueKeys.TryGetValue(closed, out var known))
        {
            return known;
        }

        var (parts, start) = (this.parts, this.parts.Count);
        DropGlue.Parts(this.types, closed, parts);
        var payload = parts.Count > start ? parts[start].Type : null;
        parts.RemoveRange(start, parts.Count - start);
        key = payload is not null && MirTypes.MayNeedDrop(payload, this.seen) ? key
            : key with { Type = this.types.Rebuild(closed, [BoundType.Unit], null, []), Variant = payload is null ? (byte)1 : (byte)0 };
        this.glueKeys.Add(closed, key);
        return key;
    }

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
    // Body under TyCtxt.Synthesized (R4-M6b), or each part's, element's or Sealed payload's glue; an Array's, the destroyArray instance over
    // its element, whose body reaches the element's glue by its own edge; a Dictionary's, what its legacy Drop row declares (Entries).
    private void DestructionTargets(MirProgram program, int item, SourceRef site, BoundType type)
    {
        if (DropGlue.Row(type) == DropGlueRow.Array)
        {
            var destruction = this.types.Declaration(KimiDeclarationId.StorageDestroyArray)!;
            this.Target(item, this.Declared(program, item, site, destruction, this.types.Synthesized(destruction, null, [type.Components[0]])));
            return;
        }

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

        var entries = this.types.DictionaryStorage(row.Entries);
        return this.Declared(program, item, site, entries, this.types.Synthesized(entries, null, [key, value]));
    }

    // R4-M6b, SPEC 16.3.2: the Body of a struct's declared destructor in the struct's instance (TyCtxt.Synthesized), which destroys the
    // struct's Fields and base itself; a destructor without a body is one Binding rejected, so the edge derives from that error.
    private int Destructor(MirProgram program, int item, SourceRef site, BoundType type)
    {
        var destructor = AdtDef.DestructorSymbol(type)!;
        return program[new(destructor, MirBodyKind.Fn)] is { } body ? this.Admit(item, site, body, this.types.Synthesized(destructor, type, [])) : -1;
    }
}
