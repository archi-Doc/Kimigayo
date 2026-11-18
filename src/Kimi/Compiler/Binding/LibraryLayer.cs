// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// LibraryLayer (PLAN G949; DECISIONS BU-220), owned by KimiLibrary: the one home of what the library leaves outside its SyntaxTable.
// - Responsibility: holding the base stores, each a store of its own: ImplTable's base (Impls), the library Types' identities and paths
//   (S1-b1); the library pass's part of the per-pass collections (Part: the published lists, nodes, issues, obligations, imports, the
//   effect obligations' sources and the counts, S1-r1a; the local-region graph, PLAN G949's localRegions batch; the keepers, the
//   Contract shapes, the effect bounds, the import declarations, the prune steps' sets, the closure index and the aliases, S1-r1b),
//   which only the library pass writes (PLAN G949 S1-r1b); the library's size (Size: the library pass's table counts at its end,
//   KimiLibrarySize's measure, O8 (a)).
// - Inputs: the library's registrations, routed here by ImplTable's key rule (a library Type symbol's module, SPEC 8.6.1); the library
//   pass's writes to the part Binding's pass driver hands it.
// - Outputs: Impls, which only the compilation's ImplTable reads and composes (its one composition point); Part, which only Binding's
//   pass driver hands to a pass and only Binding's composed views and its keyed collections' one find (PassPart.Find) read.
// - Invariants: never a region of an overlay store and never copied into one; its stores fill and reset with each library pass, one
//   per run, until S1-1 freezes them; written only by the library's registrations and the library pass; Frozen from S1-1.
// - Prohibitions: no field holds a Compilation, Binding, TyCtxt, DiagnosticOwner or diagnostic collection (C-376 condition 3); no
//   reader composes it with an overlay outside the overlay's own accessor.
internal sealed class LibraryLayer
{
    /// <summary>Gets ImplTable's base store: the library Types' conformance identities and paths.</summary>
    internal ImplTable Impls { get; } = new();

    /// <summary>Gets the library pass's part of the per-pass collections, the published lists and the local-region graph (Binding's pass
    /// driver). Its node list starts at the library's stated count, a library-only table (KimiLibrarySize).</summary>
    internal Binding.PassPart Part { get; } = new(KimiLibrarySize.Stated.Nodes);

    /// <summary>Gets or sets the library's size: the library pass's table counts at its end (KimiLibrarySize's measure; Binding's pass
    /// driver writes it).</summary>
    internal KimiLibrarySize.Tables Size { get; set; }
}
