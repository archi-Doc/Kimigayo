// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The substitution of a call (SPEC 15.6.4, 10.1 step 5, 15.3.1; PLAN G457, G707).
// - Responsibility: the one value a call's signature and its callee's clauses are read under (CallSubstitution: the callee, the occurrence
//   of the Type declaring it, its Type and length slots' bindings, the images of its declared Origins and of its input Origins), its one
//   builder for a callee (SubstitutionOf) and its one application to a Type (Substitute; a clause's is SubstituteConstraint).
// - Inputs: a candidate's solution (SlotSolution.Substitution), a selected call's plan columns, a function reference's arguments, a
//   witness comparison's solved images; for a container's own clauses at an occurrence of its Type, the occurrence's arguments alone.
// - Outputs: the Type or clause in the caller's terms; null where a slot the Type names is unbound.
// - Invariants: one application is one pass over both binders: an atom or slot takes its image under the callee, else under the
//   declaring occurrence, and no image is substituted again, so a caller's Type or Origin that names the callee's own or its declaring
//   Type's slots (a call in the callee's body, a call from another member of the same Type) is kept. Its interned form after selection is
//   Subst (TyCtxt.Substitutions), column for column.
// - Prohibitions: no site assembles a call's substitution from parts (a second pass for the declaring Type, its own Origin columns); the
//   value holds no verdict.
// - Kept two-step reads (PLAN G457 (R1)), no call's substitution and none of them a model for a new site:
//   - a pattern read before the callee's slots have images, the declaring occurrence's arguments first and the solver's images after:
//     the trial's CallMemberPattern (ArgumentKinds.Evidence, ArgumentKinds.Solution, CallSelection.Candidates, ShapeEvidence),
//     OriginInference's Project and PropertyCalls' Signature (an accessor's pattern, matched by MatchInputOrigins before its Origins are
//     solved); they join the one pass when the declaring occurrence's arguments are bound slots of the solver's frame;
//   - a comparison of two signatures, where no call is made: FunctionTypes' Bound (ContractFits), CallableContracts' Corresponding and
//     Translate and ContractMatching's parameter read (a witness's signature in the requirement's terms), OriginInheritance's
//     BindContractType and BindContractOrigin (an inherited definition's contract);
//   - an associated Type's formation under its projection (AssociatedOrigins): the projection's substitution, not a call's.
public sealed partial class Binding
{
    // A callee's substitution: Origins and Inputs are clamped to the callee's counts; a declaring Type that is no occurrence of a
    // declaration (no Symbol) has no slots.
    private static CallSubstitution SubstitutionOf(
        Koto binder,
        BoundType? declaring,
        ReadOnlySpan<BoundType?> types,
        ReadOnlySpan<BoundLength?> lengths = default,
        ReadOnlySpan<BoundOrigin> origins = default,
        ReadOnlySpan<BoundOrigin> inputs = default)
        => new(
            binder,
            types,
            lengths,
            origins[..Math.Min(origins.Length, CallOriginCount(binder))],
            inputs[..Math.Min(inputs.Length, InputOriginCount(binder))],
            declaring?.Symbol is null ? null : declaring);

    private BoundType? Substitute(BoundType type, in CallSubstitution substitution)
        => this.SubstituteType(
            type,
            CallSlotOwner(substitution.Binder),
            substitution.Types,
            substitution.Lengths,
            substitution.Origins,
            substitution.Inputs,
            substitution.Binder,
            substitution.Declaring);

    // An Origin of the callee's declared relations (SPEC 15.6.4 step 3) under the substitution, in the same one pass.
    private BoundOrigin Substitute(BoundOrigin origin, in CallSubstitution substitution)
        => this.SubstituteSlotOrigin(
            origin,
            CallSlotOwner(substitution.Binder),
            substitution.Types,
            substitution.Binder,
            substitution.Origins,
            substitution.Inputs,
            substitution.Declaring);

    private readonly ref struct CallSubstitution
    {
        internal CallSubstitution(
            Koto binder,
            ReadOnlySpan<BoundType?> types,
            ReadOnlySpan<BoundLength?> lengths = default,
            ReadOnlySpan<BoundOrigin> origins = default,
            ReadOnlySpan<BoundOrigin> inputs = default,
            BoundType? declaring = null)
        {
            this.Binder = binder;
            this.Types = types;
            this.Lengths = lengths;
            this.Origins = origins;
            this.Inputs = inputs;
            this.Declaring = declaring;
        }

        // The callee, the binder of the Origins and inputs; its Type slots' owner is CallSlotOwner's.
        internal Koto Binder { get; }

        internal ReadOnlySpan<BoundType?> Types { get; }

        internal ReadOnlySpan<BoundLength?> Lengths { get; }

        internal ReadOnlySpan<BoundOrigin> Origins { get; }

        internal ReadOnlySpan<BoundOrigin> Inputs { get; }

        // The occurrence of the Type declaring the callee, the second binder; null for a container's own clauses.
        internal BoundType? Declaring { get; }
    }
}
