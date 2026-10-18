// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1402 // The instance key and the rows of the checked set belong together.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

/// <summary>The key of a body instance (R5 PD5; K package KD1): its MIR definition and its Origin-erased substitution, null exactly when
/// the definition does not instantiate (<see cref="TyCtxt.Instantiates"/>), so that a root and an edge to it agree by construction. K3
/// brings the erasure; until then only closed definitions are keyed.</summary>
/// <param name="Def">The definition.</param>
/// <param name="Erased">The erased substitution, or null for a closed definition.</param>
internal readonly record struct Instantiation(MirDefKey Def, Subst? Erased);

/// <summary>The kind of a mono item (K package 4.3); the type-driven items and shims come with their units (K5-K8).</summary>
internal enum MonoKind : byte
{
    Body, // A body instance.
}

/// <summary>The key of a mono item; the checked set holds one item per key.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="Body">A body item's instance.</param>
internal readonly record struct MonoKey(MonoKind Kind, Instantiation Body);

/// <summary>A mono item of the checked set (K package 4.3, KD3): its key; its representative, the first unerased closed substitution that
/// admitted it, on which its proofs and Types are computed (null for a closed item, whose Types are its own); the MIR body it replays and
/// the one view of it the representative selects; the item that first admitted it (-1 for a root), its depth on that chain and the
/// located site of that admission; and its targets, a range of the checked set's.</summary>
internal record struct MonoItem
{
    /// <summary>Gets the key.</summary>
    internal MonoKey Key { get; init; }

    /// <summary>Gets the representative substitution, or null for a closed item.</summary>
    internal Subst? Representative { get; init; }

    /// <summary>Gets the ordinal of a body item's MIR body.</summary>
    internal int BodyOrdinal { get; init; }

    /// <summary>Gets the view of the body the representative selects (MirCases).</summary>
    internal byte View { get; init; }

    /// <summary>Gets the item that first admitted it, or -1 for a root.</summary>
    internal int Parent { get; init; }

    /// <summary>Gets its depth on the admission chain, 0 for a root.</summary>
    internal ushort Depth { get; init; }

    /// <summary>Gets the root-module site of its first admission (a root's body).</summary>
    internal SourceRef Site { get; init; }

    /// <summary>Gets or sets the first of its targets.</summary>
    internal int FirstTarget { get; set; }

    /// <summary>Gets or sets the number of its targets.</summary>
    internal int TargetCount { get; set; }
}

/// <summary>An edge of the checked set: the item a site of an item's body names, the site, and whether a runtime path from the body's
/// entry reaches it (false: only the checks reach it, so the item is checked, never generated through it).</summary>
/// <param name="Item">The target item.</param>
/// <param name="At">The site.</param>
/// <param name="Runtime">Whether a runtime path reaches the site.</param>
internal readonly record struct MonoTarget(int Item, MirLocation At, bool Runtime);
