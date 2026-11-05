// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the dispatch shape of a struct family (SPEC 6.4.1-6.4.3; DECISIONS R5-M62; R5 package F 4.2): for each struct that has or
// inherits a virtual slot, its slots in order and the function each runs.
// Input: Binding's slot selection at the end of PrepareVirtualOverrides (each override's original) and the struct's merged members in
// logical declaration order (SPEC 6.2.1), a conditional-conformance Block's members in its place.
// Output: Of(struct), the table or null; Originals[slot]; Implementations[slot], the struct's own override, else its base's entry, else
// the original; BaseCount; Slot(original), the original's index in its owner's own range, the same in every table of the family;
// SlotOf(function), the slot an original or an override runs in its owner's table, the same in every table of the family.
// Invariants: a struct's table starts with its base's table; own slots follow member order; a table exists only for a struct whose
// declaration, base table, virtuals and overrides resolved through slot selection, none failed or a dependent of a failed part (R4-M18;
// a failure later in the pass keeps the table and leaves Binding's result incomplete); refilled per pass into the same arrays.
// Must not: sort by path or span, or decide applicability (TyCtxt.Applies), ABI, receiver adaptation or a CallPlan.
// Consumer rule (K8, C6c): slot i of a closed T runs Implementations[i] in the first layer on T's AdtDef.Base chain that declares it, under
// TyCtxt.Synthesized(implementation, layer, []) (a virtual function has no own generic parameters); where TyCtxt.Applies of the slot's
// original at the layer that declares the original is false, the entry is null and the body is checked only (R4-M28); a call that
// dispatches loads SlotOf of the function it runs (a Virtual callee names the original, X5; a virtual witness, SPEC 6.4.6).

namespace Kimi.Compiler;

/// <summary>The dispatch shape of one struct, kept on its <see cref="AdtDef"/>: its slots, its base's first, and the function each runs.
/// Binding refills it once per pass after slot selection.</summary>
internal sealed class VTableDef
{
    /// <summary>Gets the original virtual declaration of each slot.</summary>
    internal BindingSymbol[] Originals { get; private set; } = [];

    /// <summary>Gets the function each slot runs on this struct.</summary>
    internal BindingSymbol[] Implementations { get; private set; } = [];

    /// <summary>Gets the number of slots the base's table holds; the struct's own slots follow.</summary>
    internal int BaseCount { get; private set; }

    /// <summary>Gets or sets the storage version of the pass that filled the table.</summary>
    internal ulong Version { get; set; }

    /// <summary>Gets or sets a value indicating whether the struct resolved in that pass; an unresolved struct has no table.</summary>
    internal bool Resolved { get; set; }

    // The table of a struct with a slot.
    internal static VTableDef? Of(BindingSymbol structure) => structure.Adt?.VTable is { Resolved: true, Originals.Length: > 0 } table ? table : null;

    // The slot of an original, or -1 when its owner has no table.
    internal static int Slot(BindingSymbol original)
    {
        var table = Of(original.Scope.Owner.BoundSymbol!);
        for (var i = table?.BaseCount ?? 0; table is not null && i < table.Originals.Length; i++)
        {
            if (ReferenceEquals(table.Originals[i], original))
            {
                return i;
            }
        }

        return -1;
    }

    // The slot a function runs in its owner's table, an original's own or an override's (one function per slot), or -1 when it runs none.
    internal static int SlotOf(BindingSymbol function)
        => function.Scope.Owner.BoundSymbol is { } owner && Of(owner) is { } table ? Array.IndexOf(table.Implementations, function) : -1;

    // Starts a pass's table with the base's slots and room for `own` more, reusing the arrays while the count is unchanged.
    internal void Refill(VTableDef? inherited, int own)
    {
        this.BaseCount = inherited?.Originals.Length ?? 0;
        if (this.Originals.Length != this.BaseCount + own)
        {
            this.Originals = new BindingSymbol[this.BaseCount + own];
            this.Implementations = new BindingSymbol[this.BaseCount + own];
        }

        inherited?.Originals.CopyTo(this.Originals, 0);
        inherited?.Implementations.CopyTo(this.Implementations, 0);
    }
}
