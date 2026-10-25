// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// A5's views of the raw layers an instance stores (SPEC 5.7, 5.2.1 condition 3; DECISIONS BU-24, BU-39; R4's constraint 2): the
// authority such a layer's Origin can keep (RawStep, which A5 and MirTypes.Holding read) and the borrows it belongs to (RawMembers, for
// G148 (1)'s per-part guard, R4-M76), read after Binding over Raw Origins (Binding.RawOrigins.Steps.cs, .Keepers.cs).
internal sealed partial class TyCtxt
{
    // SPEC 5.2.1 condition 3 (design-final §15, R4's conditions; the owner's 「Loan 要求で上限 (推奨)」): the step of a raw layer an instance
    // stores, the one reader of one: a struct's Field `part`, or an enum's Case `part` whose first position selects the payload's component,
    // then the inline positions (a Tuple's component, a fixed array's element). Shared when the declaration states the layer with a slot
    // whose Loan requirement is `ref`, else the kind of its Origin under the instance's substitution, `self` the holding value's own step;
    // A5 never reads an instance's FieldType or CaseTypes for a raw Origin (they state none) nor substitutes one itself. Cost: one
    // substitution of the declared part per call, interned, allocating nothing when warm. Total over what Binding accepts: an Unbound slot
    // argument gives Unstated, an erased instance answers with its erased arguments, and a layer a Type argument supplies (the declaration
    // states a Type parameter or pair layer at the path or above it) has the kind of the Origin it states (a pair slot's argument states
    // none, PairBinding, so it reads Unstated), never the cap, and one below the holding value, reached through instance shapes that state
    // no raw Origin, reads Unstated (DECISIONS BU-24); a path that ends at no raw layer is a broken contract.
    internal RawStep RawStep(BoundType instance, int part, ReadOnlySpan<int> positions) => binding.StoredStep(instance, part, positions);

    // SPEC 5.7 Origins, Calls (G160-4's (3a); DECISIONS BU-24): the borrows the Origin of the raw layer an instance stores (positions as
    // for RawStep) belongs to, appended to `members`: its Origin under the instance's substitution, within the instance, so its members
    // are the keepers of the slots the instance binds it to; `self`, `static` and an unstated layer have none. Each member is the scope
    // Type it is found at, the referent its borrow admits (a slot keeper's under the Type binding the slot) and whether it may keep an
    // exclusive Loan. Total: false when the members are incomplete (a keeper cycle that changes its Type arguments, or a keeper Binding's
    // storage instantiation does not complete), and for a layer a Type slot's argument supplies whose Origin steps Exclusive: its borrows
    // are the scope's that wrote the Type (a pair slot's states none). One substitution of the stored layer and one walk of each (slot,
    // owner Type) the bound slots' keepers reach.
    internal bool RawMembers(BoundType instance, int part, ReadOnlySpan<int> positions, List<RawMember> members)
        => binding.StoredMembers(instance, part, positions, members);
}
