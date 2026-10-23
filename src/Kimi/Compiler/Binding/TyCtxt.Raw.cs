// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// A5's views of raw layers (SPEC 5.7, 5.2.1 condition 3; DECISIONS BU-24; R4's constraint 2): the authority a raw layer's Origin can keep
// (RawStep) and the borrows it belongs to (RawMembers), read after Binding over Raw Origins (Binding.RawOrigins.Steps.cs, .Keepers.cs).
internal sealed partial class TyCtxt
{
    // SPEC 5.7, 5.2.1 condition 3 (design-final §7.3, §13 A5; DECISIONS BU-24; R4's constraint 2): the authority a raw layer's Origin can
    // keep, an over-approximation A5 reads and no diagnostic does, for a layer no declaration states (MIR's pointer temporaries state
    // none, a typed path or a local's Type its Origin): by the Origin's kind, never Shared (Binding.StepOf). Total over published Types; A5
    // reads no Origin kind itself. A declared layer, an instance's stored one or a signature's, is read by the overloads below.
    internal RawStep RawStep(BoundType raw) => Binding.StepOf(raw.RawOrigin);

    // SPEC 5.2.1 condition 3 (design-final §15, R4's conditions; the owner's 「Loan 要求で上限 (推奨)」): the step of a raw layer an instance
    // stores, the one reader of one: a struct's Field `part`, or an enum's Case `part` whose first position selects the payload's component,
    // then the inline positions (a Tuple's component, a fixed array's element). Shared when the declaration states the layer with a slot
    // whose Loan requirement is `ref`, else the kind of its Origin under the instance's substitution, `self` the holding value's own step;
    // A5 never reads an instance's FieldType or CaseTypes for a raw Origin (they state none) nor substitutes one itself. Cost: one
    // substitution of the declared part per call, interned, allocating nothing when warm. Total over what Binding accepts: an Unbound slot
    // argument gives Unstated, an erased instance answers with its erased arguments, and a layer a Type argument supplies (the declaration
    // states a Type parameter or pair layer at the path or above it) has the kind of the Origin it states, never the cap; a path that ends
    // at no raw layer is a broken contract.
    internal RawStep RawStep(BoundType instance, int part, ReadOnlySpan<int> positions) => binding.StoredStep(instance, part, positions);

    // SPEC 5.2.1 condition 3 (the owner's 「シグネチャ版も入れる (推奨)」): the step of a raw layer a function's or accessor's signature
    // states at `input` (Retention.Result for its result) and the component `positions` below it: Shared when its Origin belongs to some
    // borrow among the signature's inputs and every such borrow keeps only shared Loans, else its kind; a Function Type's layer is not a
    // function's signature and reads RawStep(raw). It reads the declared signature, retention a call's instantiated inputs, where a
    // substitution binding a shared and an exclusive statement to one Origin turns Shared into Exclusive, the conservative side (an open
    // difference, DECISIONS BU-24). Cost: one walk of the declared input Types, no allocation when warm.
    internal RawStep RawStep(BindingSymbol function, int input, ReadOnlySpan<int> positions) => binding.SignatureLayerStep(function, input, positions);

    // SPEC 5.7 Origins, Calls (G160-4's (3a); DECISIONS BU-24): the borrows `origin` belongs to within `scope`, appended to `members`:
    // for a signature's layer, its Origin with the call's instantiated input Types (Signature); for an instance's stored layer, use the
    // overload below. Each member is the scope Type it is found at, the referent its borrow admits (a slot keeper's under the Type binding
    // the slot) and whether it may keep an exclusive Loan; a call may store through the layer what each referent admits. Total: false when
    // the members are incomplete (a keeper cycle that changes its Type arguments, or a keeper Binding's storage instantiation does not
    // complete), which A5 takes as it takes CallRetention.Limit. One walk of the scope's Types and of each (slot, owner Type) the bound
    // slots' keepers reach, allocating nothing beyond `members` when warm.
    internal bool RawMembers(BoundOrigin origin, ReadOnlySpan<BoundType> scope, List<RawMember> members) => binding.RawMembers(origin, scope, members);

    // RawMembers of the raw layer an instance stores (positions as for RawStep): its Origin under the instance's substitution, within the
    // instance, so its members are the keepers of the slots the instance binds it to; `self`, `static` and an unstated layer have none.
    // A layer a Type argument supplies whose Origin steps Exclusive is incomplete (false): its borrows are the scope's that wrote the Type.
    internal bool RawMembers(BoundType instance, int part, ReadOnlySpan<int> positions, List<RawMember> members)
        => binding.StoredMembers(instance, part, positions, members);
}
