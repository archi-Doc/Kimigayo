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

/// <summary>A mono item of the checked set: its key, the ordinal of the MIR body it replays, and its targets, a range of the checked set's.
/// </summary>
/// <param name="Key">The key.</param>
/// <param name="BodyOrdinal">The ordinal of a body item's MIR body.</param>
/// <param name="FirstTarget">The first of its targets.</param>
/// <param name="TargetCount">The number of its targets.</param>
internal record struct MonoItem(MonoKey Key, int BodyOrdinal, int FirstTarget, int TargetCount);

/// <summary>An edge of the checked set: the item a site of an item's body names, the site, and whether a runtime path from the body's
/// entry reaches it (false: only the checks reach it, so the item is checked, never generated through it).</summary>
/// <param name="Item">The target item.</param>
/// <param name="At">The site.</param>
/// <param name="Runtime">Whether a runtime path reaches the site.</param>
internal readonly record struct MonoTarget(int Item, MirLocation At, bool Runtime);
