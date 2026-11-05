// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the recursive-base fact of effect summaries (SPEC 8.3.3, 15.6.4; DECISIONS R4-M87): whether a projection has a base that a
// definition's premises prove to conform to a recursive Contract, by which A4 widens a parameter-dependent effect (MirEffects.AddAtom).
// Input: a definition (null: a module's top-level statements) and an interned Type, after Bind.
// Output: RecursiveBase. The pass's recursive Contract declarations behind it are found on the first query that reaches a projection and
// kept for the pass (stamped with Binding's pass version), so they are immutable after Bind; nothing runs inside Bind but the inline-layout
// summaries' own judgment (Binding.RecursiveInStorage, DECISIONS BU-65), which runs Binding.IsRecursive uncached and never fills this memo.
// Invariants: a Contract C is recursive when the premise supply of `T is C` (SPEC 8.3.2: AddContractPremises, C's Self as T), expanded
// through each supplied conformance of T or of a projection of T, supplies an available conformance of a projection of T to C's
// declaration with any Type arguments or to a refinement of it. Each declaration is expanded once at T and once at a projection of T, so
// the search is finite (at most twice the declarations) and exact for what a Contract supplies through Self; an argument-dependent
// clause (a Constraint on a Contract's own Type parameter or a projection of it), which can supply something new only at a later
// expansion, is not followed (an under-approximation: such a base keeps its atom). A base conforms when the proof rule (ProveConstraint) proves it under the definition's effect premises
// (Binding.EffectPremises), the premise scope of Copy and Sealed.
// Must not: list the Constraint forms that make a Contract recursive or read clause syntax, or prove anything outside the proof rule and
// the premise supply. Besides its memo and its scratch scope it writes only what any proof after Bind writes (interned constraints and
// Types, capability results, bound Contract references).

namespace Kimi.Compiler;

internal sealed partial class TyCtxt
{
    // SPEC 15.6.4: whether one of a projection's bases (its base, that base's base, and so on) is proven under a definition's effect premises
    // to conform to a recursive Contract (SPEC 8.3.3); false for a Type that is not a projection.
    internal bool RecursiveBase(BindingSymbol? definition, BoundType type) => binding.RecursiveBase(definition, type);
}
