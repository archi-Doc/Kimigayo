// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the targets of the object items of the checked set (R5 package K 4.3, F 4.2-4.3; SPEC 6.4, 13.6.2, 16.3.3; IMPL 21.1.3; S2-S4 X5;
// DECISIONS R5-M62, R5-M74, R5-M77): a created payload's object descriptor and the VTable of its struct, in an item's instance, and by the
// same slot rule whether a Library generates a closed root.
// Input: an ObjectDescriptor item, which a creation (MakeObj, MakeRc, MakeArc, or an Item of their functions) admitted with its payload's
// layout verdict (InstanceCollector.Edges.cs); VTableDef (slots and implementations), AdtDef.Base and TyCtxt.Applies on the representative
// (a root's: its receiver's Self).
// Output: a descriptor's targets: its payload's glue (the finalizer's destruction of the actual Type, SPEC 16.3.3) and, when the payload's
// struct has a slot, its VTable item, keyed by the erased struct; a VTable's targets in slot order: the body each slot runs,
// VTableDef.Implementations[slot] in the first layer on the struct's base chain that owns it (TyCtxt.Synthesized); Generates(root).
// Invariants: a slot whose original Applies refutes at the layer owning the original has no target, its override's body staying checked
// only (a closed root, which a Library does not generate either (Generates, K11), or a generic body no instance applies; R4-M28); a slot
// naming a definition without a MIR body has none (an override Binding failed after slot selection, R4-M18; a struct whose slot selection
// failed has no table); an undecided Applies or a function off the representative's base chain is a broken contract, InternalInvariant_Kd
// at the VTable's site.
// Must not: give a type test or a clone a target (SPEC 13.6.4: a test acquires nothing; code generation numbers the Types of the generated
// descriptors' chains), a Virtual call or a virtual slot's Item (X5: the receiver's VTable holds the entries); decide applicability
// (TyCtxt.Applies) or prove on an erased value.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

internal sealed partial class InstanceCollector
{
    /// <summary>Gets whether a Library generates a root (K package 4.4, 4.10; R4-M28): every root but the body of a closed virtual or
    /// override whose slot <see cref="TyCtxt.Applies"/> refutes on its struct, which is checked only.</summary>
    /// <param name="root">The root.</param>
    /// <returns>Whether it is generated, or null where its slot's applicability is undecided, a broken contract.</returns>
    internal bool? Generates(int root)
    {
        var function = this.Set.Items[root].Key.Body.Def.Decl;
        var table = function?.Scope.Owner.BoundSymbol is { } owner ? VTableDef.Of(owner) : null;
        var slot = table is null ? -1 : Array.IndexOf(table.Implementations, function);
        if (slot < 0)
        {
            return true;
        }

        _ = TyCtxt.Layer(TyCtxt.Signature(function!, null, function!.ReceiverIndex, out _), out var self, out _);
        return self is null ? null : this.Applies(self, table!, slot, out _);
    }

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
            this.Target(item, slots ? this.AdmitType(item, site, new(MonoKind.VTable, default, null, null, 0, null), type) : -1);
            return;
        }

        var table = VTableDef.Of(type.Symbol!)!;
        for (var slot = 0; slot < table.Originals.Length; slot++)
        {
            var applies = this.Applies(type, table, slot, out var layer);
            if (applies is null)
            {
                this.Set.Report(DiagnosticCode.InternalInvariant_Kd, site);
            }
            else if (applies.Value)
            {
                var implementation = table.Implementations[slot];
                this.Target(item, this.Implementation(program, item, site, implementation, this.types.Synthesized(implementation, layer!, [])));
            }
        }
    }

    // F 4.2's consumer rule (R4-M28): whether a slot exists on a closed Type, TyCtxt.Applies of its original at the Type's layer owning the
    // original, with the layer owning its implementation; null where either is off the Type's base chain or Applies is undecided.
    private bool? Applies(BoundType type, VTableDef table, int slot, out BoundType? layer)
    {
        var original = table.Originals[slot];
        var declaring = Owning(type, original);
        layer = Owning(type, table.Implementations[slot]);
        return declaring is null || layer is null ? null : this.types.Applies(original, declaring);
    }
}
