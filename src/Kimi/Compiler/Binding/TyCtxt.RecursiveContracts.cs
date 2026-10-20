// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the recursive-base fact of effect summaries (SPEC 8.3.3, 15.6.4; DECISIONS R4-M87): whether a projection has a base that a
// definition's premises prove to conform to a recursive Contract, by which A4 widens a parameter-dependent effect (MirEffects.AddAtom).
// Input: a definition (null: a module's top-level statements) and an interned Type, after Bind.
// Output: RecursiveBase. The pass's recursive Contract declarations behind it are found on the first query that reaches a projection and
// kept for the pass (stamped with Binding's pass version), so they are immutable after Bind; nothing runs inside Bind.
// Invariants: a Contract C is recursive exactly when the premise supply of `T is C` (SPEC 8.3.2: AddContractPremises, C's Self as T),
// expanded through each supplied conformance of T or of a projection of T, supplies an available conformance of a projection of T to C's
// declaration with any Type arguments or to a refinement of it; a declaration is expanded at most once along a chain of bases, so the
// search is finite. A base conforms when the proof rule (ProveConstraint) proves it under the definition's effect premises
// (Binding.EffectPremises), the premise scope of Copy and Sealed.
// Must not: list Constraint kinds or read clause syntax, prove anything outside the proof rule and the premise supply, or write state other
// than its memo and its scratch scope.

namespace Kimi.Compiler;

internal sealed partial class TyCtxt
{
    // SPEC 15.6.4: whether one of a projection's bases (its base, that base's base, and so on) is proven under a definition's effect premises
    // to conform to a recursive Contract (SPEC 8.3.3); false for a Type that is not a projection.
    internal bool RecursiveBase(BindingSymbol? definition, BoundType type) => binding.RecursiveBase(definition, type);
}
