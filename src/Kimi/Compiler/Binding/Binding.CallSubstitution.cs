// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The substitution of a call (SPEC 15.6.4, 10.1 step 5, 15.3.1; PLAN G457, G707).
// - Responsibility: the one value a call's signature and its callee's clauses are read under (CallSubstitution: the callee, the owner of
//   its Type slots, the occurrence of the Type declaring it, its Type and length slots' bindings, the images of its declared Origins and
//   of its per-call atoms), its one builder for a callee (SubstitutionOf; a selected call's from its plan) and its one application
//   (Substitute, to a Type or an Origin; a clause's is SubstituteConstraint). The substitution core (Binding.TypeRelations: SubstitutePart,
//   SubstituteTypeCore, SubstituteSlotOrigin, AtomImage; SubstituteFamilyArgument, SubstituteLength) reads the value, never its columns
//   as separate parameters.
// - Inputs: a candidate's solution (SlotSolution.Substitution), a selected call's plan, a function reference's arguments, a witness
//   comparison's solved images; for a binder's own substitution (a container's clauses at an occurrence of its Type, SubstituteType's
//   slots), the binder's columns alone.
// - Outputs: the Type or clause in the caller's terms; null where a slot the Type names is unbound, except under an open substitution
//   (Open: PLAN G814), which keeps each unbound Type or length slot of the callee as itself, its own image (an `s/U` application whose
//   selector is unbound keeps itself, its parts substituted). Its one writer is
//   SlotSolution.OpenType, the open Type a candidate's argument is fitted at before selection (SPEC 10.8, 10.2.1).
// - Invariants: one application is one pass over both binders: an atom or slot takes its image under the callee, else under the
//   declaring occurrence, and no image is substituted again, so a caller's Type or Origin that names the callee's own or its declaring
//   Type's slots (a call in the callee's body, a call from another member of the same Type) is kept. Declaring is an occurrence of a
//   declaration or null (the value normalizes it). An image past the binder's slots is never read (SlotImage reads by the atom's slot),
//   so the columns are taken as given. Its interned form after selection is Subst (TyCtxt.Substitutions), column for column.
// - Prohibitions: no site assembles a call's substitution from parts (a second pass for the declaring Type, its own Origin columns); the
//   value holds no verdict.
// - Kept two-step reads (PLAN G457 (R1)), open there, no call's substitution and none of them a model for a new site:
//   - a pattern read before the callee's slots have images, the declaring occurrence's arguments first and the solver's images after:
//     the trial's CallMemberPattern (ArgumentKinds.Evidence, ArgumentKinds.Solution, CallSelection.Candidates, ShapeEvidence),
//     OriginInference's Project, PropertyCalls' Signature (an accessor's pattern, matched by MatchInputOrigins before its Origins are
//     solved) and FunctionReferences' InferReferenceSlots (a function reference's pattern); they join the one pass when the declaring
//     occurrence's arguments are bound slots of the solver's frame;
//   - a function item's signature, the declaring occurrence first (MemberType) and the item's own arguments after: FunctionItems'
//     SubstitutedItemType and FunctionReferences' Formed. Its clauses are read under the one value (ReferenceArgumentProof), so an item
//     formed inside its own Type's method on another occurrence still captures in its signature (an invalid call through it is
//     accepted: PLAN G457 (R1));
//   - a comparison of two signatures, where no call is made: FunctionTypes' Bound (ContractFits), CallableContracts' Corresponding and
//     Translate and ContractMatching's parameter read (a witness's signature in the requirement's terms), OriginInheritance's
//     BindContractType and BindContractOrigin (an inherited definition's contract);
//   - an associated Type's formation under its projection (AssociatedOrigins): the projection's substitution, not a call's.
public sealed partial class Binding
{
    // A callee's substitution, the one builder: its slots' owner is CallSlotOwner's and its declaring Type the occurrence it is found in.
    private static CallSubstitution SubstitutionOf(
        Koto binder,
        BoundType? declaring,
        ReadOnlySpan<BoundType?> types,
        ReadOnlySpan<BoundLength?> lengths,
        ReadOnlySpan<BoundOrigin> origins = default,
        ReadOnlySpan<BoundOrigin> inputs = default)
        => new(binder, types, lengths, origins) { Images = inputs, Declaring = declaring };

    // A selected call's substitution, from its plan's columns.
    private static CallSubstitution SubstitutionOf(FunctionKoto function, CallPlan call)
        => SubstitutionOf(function, call.DeclaringType, call.TypeArguments, call.LengthArguments, call.Origins, call.InputOrigins);

    // One outermost application is one query of the substitution table, the value fixed, so a part shared within the Type is substituted
    // once (DECISIONS BU-19).
    private BoundType? Substitute(BoundType type, in CallSubstitution substitution)
    {
        var result = this.SubstitutePart(type, substitution, this.substitutedParts.Begin(this.storageVersion));
        return OverHeight(result) ? this.RefuseHeight() : result; // IMPL A.4: no Type higher than the limit is formed.
    }

    // An Origin of the callee's declared relations (SPEC 15.6.4 step 3) under the substitution, in the same one pass.
    private BoundOrigin Substitute(BoundOrigin origin, in CallSubstitution substitution)
        => this.SubstituteSlotOrigin(origin, substitution);

    private readonly ref struct CallSubstitution
    {
        private readonly BoundType? declaring;

        // A binder's own substitution: no Type declares it and no call gives per-call images.
        internal CallSubstitution(Koto binder, ReadOnlySpan<BoundType?> types, ReadOnlySpan<BoundLength?> lengths = default, ReadOnlySpan<BoundOrigin> origins = default)
        {
            this.Binder = binder;
            this.SlotOwner = CallSlotOwner(binder);
            this.Types = types;
            this.Lengths = lengths;
            this.Origins = origins;
        }

        // The binder of the Origins and the per-call atoms: the callee, or an accessor's binder.
        internal Koto Binder { get; }

        // The owner of the Type and length slots: CallSlotOwner's, or the binder a slots-only substitution names (SubstituteType).
        internal Koto SlotOwner { get; init; }

        internal ReadOnlySpan<BoundType?> Types { get; init; }

        internal ReadOnlySpan<BoundLength?> Lengths { get; }

        internal ReadOnlySpan<BoundOrigin> Origins { get; }

        // The images of the binder's per-call atoms: its inputs and, at a value call of a Closure, its reserved atoms.
        internal PerCallImages Images { get; init; }

        // SPEC 10.8, 10.2.1 (PLAN G814): an unbound Type or length slot of the callee keeps itself as its image instead of failing the
        // substitution; set only by SlotSolution.OpenType.
        internal bool Open { get; init; }

        // The occurrence of the Type declaring the callee, the second binder; null for a binder's own substitution and for a Type that
        // is no occurrence of a declaration (no Symbol), which has no slots.
        internal BoundType? Declaring
        {
            get => this.declaring;
            init => this.declaring = value?.Symbol is null ? null : value;
        }

        // Whether the slot owner's column binds the slot.
        internal bool OwnsSlot(int slot) => (uint)slot < (uint)this.Types.Length;

        // The slot a Type's symbol names and the column binding it: the slot owner's, else the declaring occurrence's (the one pass over
        // both binders). A constructor's slots are its structure's: a column without them (a plan keeps the callee's own) leaves them to
        // the declaring occurrence. -1 for no symbol and for a symbol neither binder owns.
        internal int Slot(BindingSymbol? parameter, out ReadOnlySpan<BoundType?> column)
        {
            column = this.Types;
            var slot = parameter is null ? -1 : ContainerSlot(this.SlotOwner, parameter);
            if (parameter is null || this.OwnsSlot(slot) || this.declaring is null || ContainerSlot(this.declaring.Symbol!.Declaration, parameter) is not (>= 0 and var outer))
            {
                return slot;
            }

            column = (BoundType[])this.declaring.Components;
            return outer;
        }
    }
}
