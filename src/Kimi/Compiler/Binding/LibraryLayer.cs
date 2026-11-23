// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// LibraryLayer (PLAN G949; DECISIONS BU-220), owned by KimiLibrary: the one home of what the library leaves outside its SyntaxTable.
// - Responsibility: holding the base stores, each a store of its own: ImplTable's base (Impls), the library Types' identities and paths
//   (S1-b1); the library pass's part of the per-pass collections (Part: the published lists, nodes, issues, obligations, imports, the
//   effect obligations' sources and the counts, S1-r1a; the local-region graph, PLAN G949's localRegions batch; the keepers, the
//   Contract shapes, the effect bounds, the import declarations, the prune steps' sets, the closure index and the aliases, S1-r1b),
//   which only the library pass writes (PLAN G949 S1-r1b); the interners' frozen levels (Types, Substs, Intersections, Lengths,
//   Constraints; PLAN G949 Stage 2 U4); a refused library pass's refusal (Refusal, PLAN G949 Stage 2 U2a).
// - Inputs: the library's registrations, routed here by ImplTable's key rule (a library Type symbol's module, SPEC 8.6.1); the library
//   pass's writes to the part Binding's pass driver hands it.
// - Outputs: Impls, which only the compilation's ImplTable reads and composes (its one composition point); Part, which only Binding's
//   pass driver hands to a pass and only Binding's composed views and its keyed collections' one find (PassPart.Find) read; Refusal:
//   the refused library pass's Binding-side records as located facts, which Core publishes, and the first record the freeze counted,
//   where Core publishes the refusal's own record (the syntax list stays its own records' owner, the module's DiagnosticOwner's: form (R));
//   each interner level, which only its interner's find reads (PassPart.Levels) and only its insert writes, until the seal.
// - Invariants: never a region of an overlay store and never copied into one; its stores are filled once per process and target, in
//   the freeze compilation's library pass, and S1-1's freeze stamps them Frozen (Binding.FreezeLibrary); written only by the library's
//   registrations and that pass; read by every compilation of the target (PLAN G949 Stage 2 U3). The library's publication version is
//   Binding.Frozen once frozen (HirTables.LibraryVersion), the same in every compilation, and the library HIR is identical in each.
//   A refused layer's part is reset and nothing in it is Frozen (Binding's FreezeLibrary refuses); a refused layer is never bound again
//   (the driver reads its refusal first), so no compilation interns into its levels or reads them; besides its refusal, which owns its
//   Binding-side records and refers to the syntax list it does not own, it keeps only its interner levels' objects (immutable). An
//   interner level is filled until the library's table freezes (the seal, KimiLibrary.Frozen) and only read after it; no content of a
//   level is in the compilation's level, so no frozen object holds a compilation-level one.
// - Prohibitions: no field holds a Compilation, Binding, TyCtxt, DiagnosticOwner or diagnostic collection (C-376 condition 3); no
//   reader composes it with an overlay outside the overlay's own accessor.
internal sealed class LibraryLayer
{
    /// <summary>Gets ImplTable's base store: the library Types' conformance identities and paths.</summary>
    internal ImplTable Impls { get; } = new();

    /// <summary>Gets the library pass's part of the per-pass collections, the published lists and the local-region graph (Binding's pass
    /// driver).</summary>
    internal Binding.PassPart Part { get; } = new();

    /// <summary>Gets or sets L (PLAN G949 S1-1, d2b-d 1.4): the last Type id the library pass gave, so every Type the library made has an
    /// id at most this; Binding's freeze writes it.</summary>
    internal int TypeCount { get; set; }

    // The interners' frozen levels (PLAN G949 Stage 2 U4; design §4): TyCtxt's Types and substitutions, Binding's Origin intersections,
    // lengths and constraints, each keyed as its interner keys it. Each interner's one find reads its level here first, then the
    // compilation's own; an insert goes here until the library's table freezes (the seal, Binding.FreezeLibrary) and to the compilation's
    // level after it, so equal content is one object in both levels and no frozen object holds a compilation-level one.
    internal Dictionary<int, List<BoundType>> Types { get; } = new();

    internal Dictionary<int, List<Subst>> Substs { get; } = new();

    internal Dictionary<int, List<BoundOrigin>> Intersections { get; } = new();

    internal Dictionary<(LengthOperation Operation, long Value, BindingSymbol? Parameter, BoundLength? Left, BoundLength? Right, BoundType? Values, BindingSymbol? OpenSlot), BoundLength> Lengths { get; } = new();

    internal Dictionary<ConstraintKey, BoundConstraint> Constraints { get; } = new();

    /// <summary>Gets or sets the refusal of the latest library pass (PLAN G949 Stage 2 U2a; design 5.3): its Binding-side records, kept
    /// while its tables stood, and whether the library module's syntax list held a record; null for a library its freeze did not refuse.
    /// Binding's freeze is its one writer.</summary>
    internal Refused? Refusal { get; set; }

    /// <summary>A refused library pass's records (PLAN G949 Stage 2 U2a, C2): the Binding-side ones as located facts, which the reading
    /// compilation publishes as InternalInvariant_Kd at their library locations, and the first record the freeze counted (the library
    /// module's syntax list's first, else the first Binding-side one), where the refusal's own InternalInvariant_Kd is published (B's
    /// ruling 40 (i)), so a refused library always publishes an Error; syntax records stay that list's (form (R)).</summary>
    /// <param name="Records">The Binding-side records.</param>
    /// <param name="First">The first counted record's location.</param>
    internal sealed record Refused(Kimi.Diagnostics.LocatedRecord[] Records, Kimi.Diagnostics.LocatedRecord First);
}
