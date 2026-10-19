// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// Destruction and static edges (K package 4.5 rows 7, 9 and 10, "DropGlue"; S2-S4 X3, X6, X23, X33 (a), X34; DECISIONS R5-M60) and the
// targets of the type-driven items.
internal sealed partial class InstanceCollector
{
    // K 4.5 rows 9 and 10: the DropGlue item of a Type an item's instance destroys, keyed by its erased Type, for a Type whose destruction
    // runs or releases something (MirTypes.MayNeedDrop); A2's verdicts do not narrow the checked set (codegen reads them). -1 for a Type
    // with nothing to destroy or a refused edge.
    private int Glue(int item, SourceRef subject, BoundType type) => this.In(item, subject, type) is { } closed && MirTypes.MayNeedDrop(closed, this.seen)
        ? this.AdmitType(item, subject, new(MonoKind.DropGlue, default), closed) : -1;

    // K 4.5 row 7: the StaticInit body a staticEnsure names (MirProgram.StaticInit, F10); none is a broken contract, since the builder
    // ensures only the statics whose initializers it builds. A static of a generic container's group, whose storage the MIR keys by its
    // symbol alone (SPEC 22.4.4), is the capability table's generic-static-key gap at the access (X6, X33 (a)).
    private int Static(MirProgram program, int item, MirBody body, SourceRef subject, in MirTerminator ensure)
    {
        var initializer = program.StaticInit(body, body.Operands[ensure.Start]);
        if (initializer is null || this.types.Instantiates(initializer.Key.Decl!))
        {
            this.Set.Report(initializer is null ? DiagnosticCode.InternalInvariant_Kd : Capability.Code(CapabilityGap.GenericStaticKey), subject);
            return -1;
        }

        return this.Admit(item, subject, initializer, null);
    }

    // K 4.3: a type-driven item's targets, from its representative Type, each at the default site: a ClosureErasure's body and its
    // environment's glue; an ItemAdapter's body; a shim's (InstanceCollector.Witnesses.cs); a DropGlue's direct parts in destruction order
    // (DropGlue.Parts): a destructor's Body under TyCtxt.Synthesized (R4-M6b), or each part's, element's or Sealed payload's glue.
    private void Expand(MirProgram program, int item)
    {
        var key = this.Set.Items[item].Key;
        var type = this.Set.Items[item].RepresentativeType!;
        var site = this.Set.Items[item].Site;
        if (key.Kind is MonoKind.Builtin or MonoKind.PropertyWitness)
        {
            this.Shim(program, item, site, key, type);
            return;
        }

        if (key.Kind != MonoKind.DropGlue)
        {
            this.Target(item, this.Callee(program, item, site, type));
            this.Target(item, key.Kind == MonoKind.ClosureErasure ? this.Glue(item, site, type) : -1);
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
    // at its request by the location rule.
    private bool ElementsLayOut(SourceRef site, BoundType type)
    {
        var parts = this.parts;
        var start = parts.Count;
        var laidOut = true;
        DropGlue.Parts(this.types, type, parts);
        for (var i = start; i < parts.Count; i++)
        {
            laidOut &= parts[i].Kind != DropPartKind.Element || this.LaysOut(site, parts[i].Type);
        }

        parts.RemoveRange(start, parts.Count - start);
        return laidOut;
    }

    // R4-M6b, SPEC 16.3.2: the Body of a struct's declared destructor in the struct's instance (TyCtxt.Synthesized), which destroys the
    // struct's Fields and base itself; a destructor without a body is one Binding rejected, so the edge derives from that error.
    private int Destructor(MirProgram program, int item, SourceRef site, BoundType type)
    {
        var destructor = AdtDef.DestructorSymbol(type)!;
        return program[new(destructor, MirBodyKind.Fn)] is { } body ? this.Admit(item, site, body, this.types.Synthesized(destructor, type, [])) : -1;
    }

    // A type-driven item's target at the default site, which runs whenever the item does.
    private void Target(int item, int target)
    {
        if (target >= 0)
        {
            this.Set.Target(item, new(target, default, Runtime: true));
        }
    }
}
