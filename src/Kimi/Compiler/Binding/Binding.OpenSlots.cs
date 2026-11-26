// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Open expected Types (PLAN G924 AF-3b v3-A; the owner's O-499 (a); K's D-1097 and D-1117 readings; ArgFlow rulings 148, 150, 151).
// SPEC 10.5.1 (a checked part is never evidence for an outer slot, also while candidates are compared), 10.8 Unbound slots (an unbound
// slot is no mismatch, and a parameter Type containing it is identical to no other Type), 10.2.1 (the selected candidate's checks that
// needed the slot use the Type bound there).
// - Responsibility: the expected Type at an open position, a candidate's parameter pattern formed as at a closed position with each own
//   slot the candidate leaves unbound after steps 3 and 4 of SPEC 10.1 passed as its open atom (OpenType); the atoms' one rule.
// - Inputs: a candidate's solution (its frame, binder, scope, Self, Origins) and the pattern of one of its positions; the level of the
//   owner, the next in-flight owner's (InferCtxt's owners: an owner is pushed while a checked part is queried at its open Type).
// - Outputs: an interned Type that may hold atoms (BoundTypeKind.OpenSlot, interned per slot symbol and owner level by TyCtxt.OpenSlot;
//   LengthOperation.OpenSlot by TyCtxt.OpenSlotLength), summarized by BoundType.ContainsOpenSlot; null only where the substitution forms
//   no Type (the candidate's Unformed or Pending outcome, never the raw pattern).
// - The rule (stated once; ArgFlow ruling 148, K's D-1117): an atom names a slot that no evidence binds. While candidates are compared it
//   holds against any part and binds nothing, in every fit and every agreement it meets; two parts meeting one atom, or a part holding it,
//   are the selected candidate's checks, made when each waiting part takes its own Type. In SPEC 10.4 step 2 a Type holding an atom is
//   identical only to itself: the relation walk's atom arm holds and publishes that it met an atom (RelationWalk.MetOpenSlot), which
//   the walk's identity uses read (step 2; Infer's evidence agreement, where a part held against an atom waits and records no binding);
//   every other use of the walk is a fit or an agreement, where the atom holds; a reference-equal pair is discharged before the arm.
//   Evidence still binds its own candidate's slots, and the expected-result match binds a nested candidate's unbound slot to the part of
//   an open Type it meets, an atom included (D-1097's fills; the first binding at Infer's slot arm); Infer's head holds an atom against
//   any other pattern part. The fills of one slot agree in the structure they share, fill first (K's D-1132, ArgFlow ruling 154): an atom
//   adds none, two distinct atoms at one place agree in none and fail the match, and later evidence never replaces a fill (FillOpenSlots,
//   read at the match's agreement only). A projection over an atom that the premises do not reduce, one formed with no closed form,
//   is as open as its subject (SPEC 10.8: a projection is never inverted), so every reader of the rule reads it as an atom (IsOpenAtom;
//   PLAN G814, o5); a projection that a premise or a binding fixes is formed closed and is fitted.
// - The place rule (SPEC 10.5.1, 10.8; design v3-A 11.8.3 and its Rev, ArgFlow rulings 159 and 160): a checked part that is not a
//   nested call (a literal form, a Dictionary literal one while its position forms its Type, a join, a leading-dot Case, an awaited
//   anonymous function or function reference) whose place is a bare atom holds there and binds nothing; it is completed after
//   selection (PlacedAtOpenSlot, read at FitArgumentPart's entry and by FitsPosition's hold, PLAN G814 R). Its
//   place is the owner temporary's Type for an owner value by its form, else the position's Type; an atom has no borrow value's outer
//   Semantics for the place only (HasBorrowSemantics answers no, so the place at `ref/T°` is `T°`), the after-selection check reading
//   the bound Type. A nested call, a construction included, is checked at the position's Type itself, a bare atom included, by its
//   own resolution (FitChecked), whose non-null result is judged by the closed fit with the walk's atom arm. No owner is carried into a
//   query: SPEC 10.5.1 judges a checked part by the part and its expected Type alone, once per distinct expected Type, so a candidate's
//   own pattern (10.2.2 item 1) decides only how evidence binds within its own trial.
// - Invariants: nothing binds an atom, and no in-flight owner's frame is written (InferCtxt's one check at SetType and SetLength); a frame
//   holds an atom only while an owner is in flight (the same check), so atoms end at selection; the HIR publisher lists a node Type that
//   holds one as Broken (InternalInvariant_Kd). The prover's two intakes judge nothing that holds an atom, a Constraint's operand
//   (JudgeConstraintAtom) or a length obligation (ProveLength, SPEC 4.4.2: checked at a concrete call): each is judged at the completed
//   Type after selection; a slot whose binding holds one is unsolved for its candidate's Constraints (UnsolvedSlots).
// - Prohibitions: an atom is never a Parameter, has no Origin and no components, and is never substituted; no reader decides by an
//   ambient mode; no second store of atom bindings beside the owners' frames.
public sealed partial class Binding
{
    // The atoms' one predicate (the contract above): an atom, or a projection whose subject holds one.
    private static bool IsOpenAtom(BoundType type)
        => type.Kind == BoundTypeKind.OpenSlot || type is { Kind: BoundTypeKind.AssociatedProjection, Components: [{ ContainsOpenSlot: true }, ..] };

    // The place rule (the contract above): whether a checked part that is not a nested call, one that awaits its call or a literal form,
    // has a bare atom as its place: the owner temporary's Type for a literal, an aggregate or a join (Materialization), a leading-dot
    // Case's expected Type (ExpectedTypeOf, as its enum binder reads it), else the position's Type. A Dictionary literal is a literal form
    // while its position forms its Type (IsAggregatePart: one holding a failed or waiting part; PLAN G814-5), as a Tuple or array literal
    // is; one typed whole (BU-43) is a typed part, evidence.
    private bool PlacedAtOpenSlot(Koto node, PartKind kind, BoundType type, BindingScope scope)
        => kind != PartKind.Call && (kind is PartKind.Literal or PartKind.Aggregate || this.IsAggregatePart(node) || this.AwaitsCall(node)) &&
            IsOpenAtom(this.Materialization(node, type, scope) ?? (kind == PartKind.Case ? this.ExpectedTypeOf(type) : type));

    // SPEC 10.8 Solving (the contract above; FillOpenOrigins is its Origin precedent): `type` with each atom replaced by the part `other`
    // has at the same place, unless that part is another atom (two distinct atoms agree in no structure), and a fixed array's length atom
    // by `other`'s length (SPEC 4.4.2: a normalized length). Unequal shapes are left to the caller's comparison.
    private BoundType FillOpenSlots(BoundType type, BoundType other)
    {
        if (ReferenceEquals(type, other) || !type.ContainsOpenSlot)
        {
            return type;
        }

        if (IsOpenAtom(type))
        {
            return IsOpenAtom(other) ? type : other;
        }

        if (type.Kind != other.Kind || !ReferenceEquals(type.Symbol, other.Symbol) || type.Semantics != other.Semantics ||
            type.Components.Count != other.Components.Count)
        {
            return type;
        }

        var components = this.RentTypes(type.Components.Count);
        try
        {
            var changed = false;
            for (var i = 0; i < type.Components.Count; i++)
            {
                components[i] = this.FillOpenSlots(type.Components[i], other.Components[i]);
                changed |= !ReferenceEquals(components[i], type.Components[i]);
            }

            var filled = changed ? this.types.Rebuild(type, components.AsSpan(0, type.Components.Count), type.Origin, (BoundOrigin[])type.OriginArguments) : type;
            return filled.Kind == BoundTypeKind.FixedArray && filled.LengthExpression is { ContainsOpenSlot: true } && other.LengthExpression is not { ContainsOpenSlot: true }
                ? this.FixedArrayType(filled.Components[0], other.LengthExpression ?? this.InternLength(LengthOperation.Constant, other.Length)) : filled;
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
        }
    }

    private ref partial struct SlotSolution
    {
        // The pattern's open Type at the next owner level: Type's formation with each own slot this solution leaves unbound passed as its
        // atom, lengths alike; bound slots, selectors and pair layers substituted as at a closed position.
        internal readonly BoundType? OpenType(BoundType pattern)
        {
            var binding = this.Binding;
            var level = binding.infer.OwnerCount;
            var own = CallOwnSlots(this.Function);
            var types = binding.typeScratch.Rent(this.Types.Length);
            var lengths = binding.lengthScratch.Rent(this.Lengths.Length);
            try
            {
                this.Types.CopyTo(types);
                this.Lengths.CopyTo(lengths);
                for (var g = 0; g < own.Count; g++)
                {
                    if (own[g] is LengthParameterKoto)
                    {
                        lengths[g] ??= binding.types.OpenSlotLength(own[g].BoundSymbol!, level);
                    }
                    else
                    {
                        types[g] ??= binding.types.OpenSlot(own[g].BoundSymbol!, level);
                    }
                }

                return binding.CallType(
                    pattern,
                    this.Binder,
                    types.AsSpan(0, this.Types.Length),
                    this.Scope,
                    this.Self,
                    this.Origins,
                    this.Inputs,
                    this.DeclaringType,
                    lengths.AsSpan(0, this.Lengths.Length));
            }
            finally
            {
                binding.lengthScratch.Return(lengths, clearArray: true);
                binding.typeScratch.Return(types, clearArray: true);
            }
        }
    }
}
