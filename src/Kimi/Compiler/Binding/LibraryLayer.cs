// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// LibraryLayer (PLAN G949; DECISIONS BU-220), owned by KimiLibrary: the one home of what the library leaves outside its SyntaxTable.
// - Responsibility: holding the base stores, each a store of its own: ImplTable's base (Impls), the library Types' identities and paths
//   (S1-b1); the library pass's part of the per-pass collections (Part: the published lists, nodes, issues, obligations, imports, the
//   effect obligations' sources and the counts, S1-r1a; the local-region graph, PLAN G949's localRegions batch; the keepers, the
//   Contract shapes, the effect bounds, the import declarations, the prune steps' sets, the closure index and the aliases, S1-r1b),
//   which only the library pass writes (PLAN G949 S1-r1b); the library's size (Size: the library pass's table counts at its end,
//   KimiLibrarySize's measure, O8 (a)); a refused library pass's refusal (Refusal, PLAN G949 Stage 2 U2a).
// - Inputs: the library's registrations, routed here by ImplTable's key rule (a library Type symbol's module, SPEC 8.6.1); the library
//   pass's writes to the part Binding's pass driver hands it.
// - Outputs: Impls, which only the compilation's ImplTable reads and composes (its one composition point); Part, which only Binding's
//   pass driver hands to a pass and only Binding's composed views and its keyed collections' one find (PassPart.Find) read; Refusal:
//   the refused library pass's Binding-side records as located facts, which Core publishes, and whether the library module's syntax
//   list held a record (that list stays its own records' owner, the module's DiagnosticOwner's: form (R)).
// - Invariants: never a region of an overlay store and never copied into one; its stores fill and reset with each library pass, one
//   per compilation: the library pass fills them once and S1-1's freeze stamps them Frozen (Binding.FreezeLibrary); written only by
//   the library's registrations and the library pass. The library version is per compilation (HirTables.LibraryVersion, the run of
//   the compilation's own Binding that froze the library), and the library HIR is identical in every Bind of a compilation after the
//   freeze (G949 ruling 174). A refused layer holds only its refusal: its part is reset and nothing in it is Frozen (Binding's
//   FreezeLibrary refuses; the next Bind runs the library pass again); the refusal owns its Binding-side records and refers to the syntax
//   list it does not own.
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

    /// <summary>Gets or sets L (PLAN G949 S1-1, d2b-d 1.4): the last Type id the library pass gave, so every Type the library made has an
    /// id at most this; Binding's freeze writes it.</summary>
    internal int TypeCount { get; set; }

    /// <summary>Gets or sets the refusal of the latest library pass (PLAN G949 Stage 2 U2a; design 5.3): its Binding-side records, kept
    /// while its tables stood, and whether the library module's syntax list held a record; null for a library its freeze did not refuse.
    /// Binding's freeze is its one writer.</summary>
    internal Refused? Refusal { get; set; }

    /// <summary>A refused library pass's records (PLAN G949 Stage 2 U2a, C2): the Binding-side ones as located facts, which the reading
    /// compilation publishes as InternalInvariant_Kd at their library locations, and whether the clause counted the library module's
    /// syntax list, whose records stay that list's (form (R)).</summary>
    /// <param name="Records">The Binding-side records.</param>
    /// <param name="Syntax">Whether the library module's syntax list held a record.</param>
    internal sealed record Refused(Kimi.Diagnostics.LocatedRecord[] Records, bool Syntax);
}
