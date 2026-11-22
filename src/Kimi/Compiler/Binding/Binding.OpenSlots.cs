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
//   the walk's identity uses read; a reference-equal pair is discharged before the arm. Evidence still binds its own candidate's slots,
//   and the expected-result match binds a nested candidate's unbound slot to the part of an open Type it meets, an atom included
//   (D-1097's fills; the first binding at Infer's slot arm); Infer's head holds an atom against any other pattern part.
// - Invariants: nothing binds an atom, and no in-flight owner's frame is written (InferCtxt's one check at SetType and SetLength); a frame
//   holds an atom only while an owner is in flight (the same check), so atoms end at selection; the HIR publisher lists a node Type that
//   holds one as Broken (InternalInvariant_Kd). A Constraint whose operand holds an atom is not judged (the prover's intake guard), and a
//   slot whose binding holds one is unsolved for its candidate's Constraints (UnsolvedSlots).
// - Prohibitions: an atom is never a Parameter, has no Origin and no components, and is never substituted; no reader decides by an
//   ambient mode; no second store of atom bindings beside the owners' frames.
public sealed partial class Binding
{
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
