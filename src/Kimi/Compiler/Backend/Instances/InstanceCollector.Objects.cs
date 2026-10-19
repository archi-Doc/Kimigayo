// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the targets of the object items of the checked set (R5 package K 4.3, F 4.2-4.3; SPEC 6.4, 13.6.2, 16.3.3; IMPL 21.1.3; S2-S4 X5;
// DECISIONS R5-M62, R5-M74): a created payload's object descriptor and the VTable of its struct, in an item's instance.
// Input: an ObjectDescriptor item, which a creation (MakeObj, MakeRc, MakeArc, or an Item of their functions) admitted with its payload's
// layout verdict (InstanceCollector.Edges.cs); VTableDef (slots and implementations), AdtDef.Base and TyCtxt.Applies on the representative.
// Output: a descriptor's targets: its payload's glue (the finalizer's destruction of the actual Type, SPEC 16.3.3) and, when the payload's
// struct has a slot, its VTable item, keyed by the erased struct; a VTable's targets in slot order: the body each slot runs,
// VTableDef.Implementations[slot] in the first layer on the struct's base chain that owns it (TyCtxt.Synthesized).
// Invariants: a slot whose original Applies refutes at the layer owning the original has no target, its override's body staying checked
// only (a closed root, or a generic body no instance applies; R4-M28); a slot naming a definition without a MIR body has none (an override
// Binding failed after slot selection, R4-M18; a struct whose slot selection failed has no table); an undecided Applies or a function off
// the representative's base chain is a broken contract, InternalInvariant_Kd at the VTable's site.
// Must not: give a type test or a clone a target (SPEC 13.6.4: a test acquires nothing; code generation numbers the Types of the generated
// descriptors' chains), a Virtual call or a virtual slot's Item (X5: the receiver's VTable holds the entries); decide applicability
// (TyCtxt.Applies) or prove on an erased value.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

internal sealed partial class InstanceCollector
{
    // The first layer on a Type's base chain whose struct owns a function (F 4.2's consumer rule), or null off the chain.
    private static BoundType? Owning(BoundType type, BindingSymbol function)
    {
        var owner = function.Scope.Owner.BoundSymbol;
        var layer = (BoundType?)type;
        while (layer is not null && !ReferenceEquals(layer.Symbol, owner))
        {
            layer = AdtDef.Base(layer);
        }

        return layer;
    }

    // K 4.3: an object item's targets at its default site: a descriptor's payload glue and VTable, a VTable's slot bodies.
    private void Object(MirProgram program, int item, SourceRef site, MonoKind kind, BoundType type)
    {
        if (kind == MonoKind.ObjectDescriptor)
        {
            var slots = type.Symbol is { } structure && VTableDef.Of(structure) is not null;
            this.Target(item, this.GlueOf(item, site, type));
            this.Target(item, slots ? this.AdmitType(item, site, new(MonoKind.VTable, default), type) : -1);
            return;
        }

        var table = VTableDef.Of(type.Symbol!)!;
        for (var slot = 0; slot < table.Originals.Length; slot++)
        {
            var (original, implementation) = (table.Originals[slot], table.Implementations[slot]);
            var (declaring, layer) = (Owning(type, original), Owning(type, implementation));
            var applies = declaring is null || layer is null ? null : this.types.Applies(original, declaring);
            if (applies is null)
            {
                this.Set.Report(DiagnosticCode.InternalInvariant_Kd, site);
            }
            else if (applies.Value)
            {
                this.Target(item, this.Implementation(program, item, site, implementation, this.types.Synthesized(implementation, layer!, [])));
            }
        }
    }
}
