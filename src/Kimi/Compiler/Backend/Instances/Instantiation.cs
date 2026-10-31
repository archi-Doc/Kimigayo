// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1402 // The instance key and the rows of the checked set belong together.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

/// <summary>The key of a body instance (R5 PD5; K package KD1): its MIR definition and its Origin-erased substitution, null exactly when
/// the definition does not instantiate (<see cref="TyCtxt.Instantiates"/>), so that a root and an edge to it agree by construction; the
/// erasure is <see cref="TyCtxt.Erase(Subst)"/>.</summary>
/// <param name="Def">The definition.</param>
/// <param name="Erased">The erased substitution, or null for a closed definition.</param>
internal readonly record struct Instantiation(MirDefKey Def, Subst? Erased);

/// <summary>The kind of a mono item (K package 4.3).</summary>
internal enum MonoKind : byte
{
    Body, // A body instance.
    ClosureErasure, // A Closure Type's value of a common Function Type (SPEC 7.7.4): its erased Closure Type and erased Function Type.
    ItemAdapter, // A function item Type's value of a common Function Type (SPEC 7.7.4): its erased Item Type and erased Function Type.
    DropGlue, // The destruction of a value of a Type (SPEC 16.3): its erased Type.
    Builtin, // A compiler function a requirement runs on a Self (SPEC 8.11.3): the function (its kind its CompilerFunction), the erased Self;
    // a comparison's also its ComparisonMode, its node's erased Type at the template and the requesting instance when that has a parameter.
    PropertyWitness, // A standard property witness (SPEC 11.9.2, S2-S4 X14): its Field, the erased Self and the PropertyWitnessKind.
    ObjectDescriptor, // The runtime descriptor of an object payload (IMPL 21.1.3, SPEC 13.6): its erased payload Type.
    VTable, // The virtual table of a struct with a slot (SPEC 6.4, F package 4.2): its erased struct Type.

    // A compiler function of the Body disposition whose legacy row writes its call in its instance (K package 4.3, 4.5 row 2; DECISIONS
    // R5-M121): its function and erased substitution, represented by the substitution and the Type below its parameter 0.
    [Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
    LegacyRow,
}

/// <summary>The key of a mono item; the checked set holds one item per key.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="Body">A body item's instance; a comparison Builtin item's requesting instance (its erased substitution, no definition)
/// when its Type has a parameter (DECISIONS R5-M166).</param>
/// <param name="Type">A type-driven item's erased Type (<see cref="TyCtxt.Erase(BoundType)"/>): a comparison Builtin item's is its node's,
/// at the template when it has a parameter (DECISIONS R5-M166).</param>
/// <param name="Symbol">A Builtin's compiler function or a property witness's Field.</param>
/// <param name="Variant">A property witness's operation; a comparison Builtin item's <see cref="ComparisonMode"/>.</param>
/// <param name="Function">A ClosureErasure's or ItemAdapter's erased target Function Type, the one whose erased calls reach its entry
/// (SPEC 10.7: a Never-result source fits Function Types of other results; DECISIONS R5-M108).</param>
internal readonly record struct MonoKey(MonoKind Kind, Instantiation Body, BoundType? Type, BindingSymbol? Symbol, byte Variant, BoundType? Function);

/// <summary>A mono item of the checked set (K package 4.3, KD3): its key; a body item's representative, the first unerased closed
/// substitution that admitted it, on which its proofs and Types are computed (null for a closed item, whose Types are its own), the MIR
/// body it replays and the one view of it the representative selects; a type-driven item's representative Type, the first unerased Type
/// that admitted it; the item that first admitted it (-1 for a root), its depth on that chain and the located site of that admission; and
/// its targets, a range of the checked set's.</summary>
internal record struct MonoItem
{
    /// <summary>Gets the key.</summary>
    internal MonoKey Key { get; init; }

    /// <summary>Gets the representative substitution, or null for a closed item.</summary>
    internal Subst? Representative { get; init; }

    /// <summary>Gets a type-driven item's representative Type.</summary>
    internal BoundType? RepresentativeType { get; init; }

    /// <summary>Gets the ordinal of a body item's MIR body, -1 for a type-driven item.</summary>
    internal int BodyOrdinal { get; init; }

    /// <summary>Gets the view of the body the representative selects (MirCases).</summary>
    internal byte View { get; init; }

    /// <summary>Gets the item that first admitted it, or -1 for a root.</summary>
    internal int Parent { get; init; }

    /// <summary>Gets its place in its identity's growth on the admission chain (DECISIONS R5-M170).</summary>
    internal Growth Growth { get; init; }

    /// <summary>Gets the root-module site of its first admission (a root's body).</summary>
    internal SourceRef Site { get; init; }

    /// <summary>Gets or sets the first of its targets.</summary>
    internal int FirstTarget { get; set; }

    /// <summary>Gets or sets the number of its targets.</summary>
    internal int TargetCount { get; set; }
}

/// <summary>A mono item's place in the growth of its identity (DECISIONS R5-M170): its generation, the number of times the height of its
/// identity's keys grew on its admission chain, counting from 1 (0 for an item without an identity); the greatest height of those keys
/// (its peak); and the nearest item with an identity above it on the chain (-1 for none), through which a search reads only identity
/// items. Generation and peak saturate.</summary>
/// <param name="Generation">The generation.</param>
/// <param name="Peak">The peak height.</param>
/// <param name="Above">The nearest identity item above it, or -1.</param>
internal readonly record struct Growth(byte Generation, byte Peak, int Above);

/// <summary>An edge of the checked set: the item a site of an item's body names, the site, and whether a runtime path from the body's
/// entry reaches it (false: only the checks reach it, so the item is checked, never generated through it); a type-driven item's targets
/// are at the default site and run whenever it does, a VTable's in slot order.</summary>
/// <param name="Item">The target item.</param>
/// <param name="At">The site.</param>
/// <param name="Runtime">Whether a runtime path reaches the site.</param>
internal readonly record struct MonoTarget(int Item, MirLocation At, bool Runtime);
