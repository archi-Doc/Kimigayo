// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Backend;

// Destruction and static edges (K package 4.5 rows 7, 9 and 10, "DropGlue"; S2-S4 X3, X6, X23, X33 (a), X34; DECISIONS R5-M60, R5-M116,
// R8-M5), the glue of what a call destroys in place, of what a Drop's group destroys and a DropGlue item's targets.
internal sealed partial class InstanceCollector
{
    // SPEC 16.3.2, 8.15.3, 14.10.3 (PLAN G641; DECISIONS R4-M6a): a Drop's targets at its site (the template's: its location and runtime
    // bit, which every target of the site carries): the target of each part its group's first row destroys, in its walk's order
    // (DropGlue.Destroyed, the walk code generation writes the Drop by, FunctionCodegen.Drops; Destroys), on the runtime paths or the
    // assumed paths of a Drop only the checks reach, so an Elide row, which destroys nothing, requests none; a Drop without a group (one only
    // the checks reach whose Place has no Move Path, or of a body whose verdicts are inexact, MirDropTable.Incomplete) destroys its Type
    // whole (8.15.3: reachable or not).
    private void DropTargets(MirProgram program, int item, SourceRef subject, MirBody body, in (MirLocation At, bool Runtime) site, BoundType closed)
    {
        var drops = program.Drops;
        var group = drops.Incomplete(body.Ordinal) is null ? drops.Group(body.Ordinal, site.At.Block, this.Set.Items[item].View) : -1;
        if (group < 0)
        {
            if (this.GlueOf(item, subject, closed) is >= 0 and var glue)
            {
                this.Set.Target(item, new(glue, site.At, site.Runtime));
            }

            return;
        }

        var start = this.steps.Count;
        DropGlue.Destroyed(drops, body, group, closed, this.walk, this.steps);
        this.Destroys(program, item, subject, site, start);
    }

    // The targets of a walk's steps from a start (DropGlue.Destroyed), at a site with its runtime bit, the steps then removed: per Whole or
    // Run step its part's (PartTarget).
    private void Destroys(MirProgram program, int item, SourceRef subject, in (MirLocation At, bool Runtime) site, int start)
    {
        var steps = this.steps;
        for (var i = start; i < steps.Count; i++)
        {
            if (steps[i].Kind is MirDropStepKind.Whole or MirDropStepKind.Run && this.PartTarget(program, item, subject, steps[i].Part) is >= 0 and var target)
            {
                this.Set.Target(item, new(target, site.At, site.Runtime));
            }
        }

        steps.RemoveRange(start, steps.Count - start);
    }

    // K 4.5 rows 9 and 10: the DropGlue item of a Type an item's instance destroys (GlueOf). -1 for a Type TyCtxt cannot make there.
    private int Glue(int item, SourceRef subject, BoundType type) => this.In(item, subject, type) is { } closed ? this.GlueOf(item, subject, closed) : -1;

    // The DropGlue item of a closed Type, keyed by what its function reads (GlueKey), for a Type whose destruction runs or releases something
    // (MirTypes.MayNeedDrop), a call's in-place destruction's too (SPEC 15.7.1, 16.3.2; DECISIONS R5-M116: replace's published row). -1 for
    // a Type with nothing to destroy or a refused edge.
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

    // K 4.5 "DropGlue": a DropGlue's targets, at its default site, its level (DropGlue.Level; Destroys): its
    // direct parts in destruction order, a destructor's Body under TyCtxt.Synthesized (R4-M6b), or each part's, element's or Sealed
    // payload's glue; a collection's, its one destruction body (Collection), whose body reaches the elements' glue by its own edges.
    private void DestructionTargets(MirProgram program, int item, SourceRef site, BoundType type)
    {
        if (DropGlue.Row(type) == DropGlueRow.Collection)
        {
            this.Target(item, this.Collection(program, item, site, type));
            return;
        }

        var start = this.steps.Count;
        DropGlue.Level(this.types, type, this.parts, this.walk, this.steps);
        this.Destroys(program, item, site, (default(MirLocation), true), start); // At the default site, run whenever the item is (Target).
    }

    // The target of a part a destruction destroys: a destructor's Body (Destructor), else the part's glue (Glue); -1 for none.
    private int PartTarget(MirProgram program, int item, SourceRef site, in DropPart part)
        => part.Kind == DropPartKind.Destructor ? this.Destructor(program, item, site, part.Type) : this.Glue(item, site, part.Type);

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

    // SPEC 4.9.7, 16.3.2 (DECISIONS R8-M5, R8-K1c): a collection glue's one target, the library body its destruction runs, called with the
    // glue's own parameters: an Array's, destroyArray over its element; a Dictionary's, destroyDictionary over its entry Types when an entry
    // may need a drop (the rule the collector targets parts by), else releaseDictionary, which visits no entry, so a cleanup-free
    // Dictionary's destruction is its release alone.
    private int Collection(MirProgram program, int item, SourceRef site, BoundType collection)
    {
        var components = collection.Components;
        if (collection.Kind == BoundTypeKind.Array)
        {
            var array = this.types.Declaration(KimiDeclarationId.StorageDestroyArray)!;
            return this.Declared(program, item, site, array, this.types.Synthesized(array, null, [components[0]]));
        }

        if (!MirTypes.MayNeedDrop(components[0], this.seen) && !MirTypes.MayNeedDrop(components[1], this.seen))
        {
            return this.Declared(program, item, site, this.types.Declaration(KimiDeclarationId.StorageReleaseDictionary)!, null);
        }

        var dictionary = this.types.Declaration(KimiDeclarationId.StorageDestroyDictionary)!;
        return this.Declared(program, item, site, dictionary, this.types.Synthesized(dictionary, null, [components[0], components[1]]));
    }

    // R4-M6b, SPEC 16.3.2: the Body of a struct's declared destructor in the struct's instance (TyCtxt.Synthesized), which destroys the
    // struct's Fields and base itself; a destructor without a body is one Binding rejected, so the edge derives from that error.
    private int Destructor(MirProgram program, int item, SourceRef site, BoundType type)
    {
        var destructor = AdtDef.DestructorSymbol(type)!;
        return program[new(destructor, MirBodyKind.Fn)] is { } body ? this.Admit(item, site, body, this.types.Synthesized(destructor, type, [])) : -1;
    }
}
