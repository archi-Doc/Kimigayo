// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// LibraryLayer (PLAN G949; DECISIONS BU-220), owned by KimiLibrary: the one home of what the library leaves outside its SyntaxTable.
// - Responsibility: holding the base stores, each a store of its own: ImplTable's base (Impls), the library Types' identities and paths
//   (S1-b1); the library pass's part of the published per-pass lists (Part: nodes, issues, obligations, imports, the effect
//   obligations' sources and the counts; S1-r1a), which no pass writes until the library pass (PLAN G949 S1-r1b).
// - Inputs: the library's registrations, routed here by ImplTable's key rule (a library Type symbol's module, SPEC 8.6.1); the library
//   pass's writes to the part Binding's pass driver hands it.
// - Outputs: Impls, which only the compilation's ImplTable reads and composes (its one composition point); Part, which only Binding's
//   pass driver hands to a pass and only Binding's composed views read.
// - Invariants: never a region of an overlay store and never copied into one; until the library pass exists (PLAN G949 S1-r1b) its
//   stores fill and reset per pass as the overlay's do; written only by the library's registrations and the library pass; Frozen
//   from S1-1.
// - Prohibitions: no field holds a Compilation, Binding, TyCtxt, DiagnosticOwner or diagnostic collection (C-376 condition 3); no
//   reader composes it with an overlay outside the overlay's own accessor.
internal sealed class LibraryLayer
{
    /// <summary>Gets ImplTable's base store: the library Types' conformance identities and paths.</summary>
    internal ImplTable Impls { get; } = new();

    /// <summary>Gets the library pass's part of the published per-pass lists (Binding's pass driver).</summary>
    internal Binding.PassPart Part { get; } = new(0);
}
