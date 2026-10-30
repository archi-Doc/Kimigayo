// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// A5's views of the raw layers an instance stores (SPEC 5.7, 5.2.1 condition 3; DECISIONS BU-24, BU-39, BU-41, BU-46; R4's constraint
// 2): the authority such a layer's Origin can keep (RawStep, which A5 and MirTypes.Holding read), the borrows it belongs to (RawMembers)
// and whether a declaration states it without an Origin (RawStated), with the stored parts through which a walk reaches the instance
// storing it (StoredParts), for G148 (1)'s per-part guard (R4-M76), read after Binding over Raw Origins (Binding.RawOrigins.Steps.cs,
// .Keepers.cs) and Binding's instance storage (Binding.Storage.cs); the Type a declaration writes at a stored part's inline positions
// (WrittenAt, DECISIONS BU-58), for A5's walks over them; what a `during self` layer admits past its pointee (SelfAdmitted,
// Binding's one reading forwarded, which MirTypes.Holding reads); and whether a Type states Unstated (StatesUnstated), which only a kept
// part does (MirValidator's Local row).
internal sealed partial class TyCtxt
{
    // SPEC 5.7 Calls, Fits (DECISIONS BU-46): whether a declaration states a raw layer without an Origin: true exactly for a layer a Field,
    // Case payload or base clause writes without `during`, which an instance's kept parts (StoredParts) state as Unstated at any depth
    // below them, through every nested instance, Type argument, pair slot's argument, element and `during self` pointee they reach; false
    // for a layer the walk's own Type writes (a parameter's or local's Type expression, a Type argument or pair argument it supplies) and
    // for one stating an Origin. A read of the layer Type; total; allocates nothing.
    internal static bool RawStated(BoundType layer) => layer.RawOrigin?.Kind == OriginKind.Unstated;

    // DECISIONS BU-46, BU-58: whether a raw layer of a Type states Unstated at any depth, which a Type that is no kept part never does
    // (MirValidator's Local row: a MIR local's Type states none). Only a part carrying a raw Origin is walked, each distinct one once per
    // call (`seen`, the caller's scratch, cleared first: the answer is the part's alone), by index; allocates nothing when warm.
    internal static bool StatesUnstated(BoundType type, HashSet<BoundType> seen)
    {
        if (!type.CarriesRawOrigin)
        {
            return false;
        }

        seen.Clear();
        return Unstated(type, seen);

        static bool Unstated(BoundType type, HashSet<BoundType> seen)
        {
            if (RawStated(type))
            {
                return true;
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                var part = type.Components[i];
                if (part.CarriesRawOrigin && seen.Add(part) && Unstated(part, seen))
                {
                    return true;
                }
            }

            return false;
        }
    }

    // DECISIONS BU-58: the Type the declaration of an instance's part writes at inline positions (as for RawStep), Binding's one reading
    // (Binding.WrittenAt), while it is a Tuple, a fixed array or a raw layer (at a raw layer, exactly the layer RawStep's cap reads); null
    // at or past any other Type, where no declaration writes the part, and at an enum's []. Monotone from a part's first positions (a
    // struct's [], a Case's [i]). The facts at and below a stored inline node are a function of its kept Type, this and the instance;
    // where this is null, every raw layer below steps StepOf of its kept layer (Unstated, Static or Exclusive, never Shared or Own) and
    // RawMembers appends nothing. Reads the declaration only, O(|positions|), total; allocates nothing.
    internal static BoundType? WrittenAt(BoundType instance, int part, ReadOnlySpan<int> positions) => Binding.WrittenAt(instance, part, positions, out _);

    // SPEC 5.7 Calls (the owner's Q2 (a)): what a `during self` layer of a stored part admits past it besides its pointee, Binding's one
    // reading (Binding.SelfAdmitted): the holding instance's Type arguments by their actual Types, or the write fact where it has an Origin
    // argument (true).
    internal static bool SelfAdmitted(BoundType holder, out IReadOnlyList<BoundType> arguments) => Binding.SelfAdmitted(holder, out arguments);

    // SPEC 5.2.1 condition 3 (design-final §15, R4's conditions; the owner's 「Loan 要求で上限 (推奨)」): the step of a raw layer an instance
    // stores, the one reader of one: a struct's Field `part`, or an enum's Case `part` whose first position selects the payload's component,
    // then the inline positions (a Tuple's component, a fixed array's element). Shared when the declaration states the layer with a slot
    // whose Loan requirement is `ref`, else the kind of its Origin under the instance's substitution, `self` the holding value's own step;
    // A5 never reads an instance's FieldType or CaseTypes for a raw Origin (they state none) nor substitutes one itself. Cost: one read of
    // the instance's kept part (StoredParts), allocating nothing when warm. Total over what Binding accepts: an Unbound slot argument gives
    // Unstated, an erased instance answers with its erased arguments, and a layer a Type argument supplies has the kind of the Origin it
    // states on the instance it is read on (a pair slot's argument states none, PairBinding, so it reads Unstated; Unstated when a
    // declaration above states it, RawStated), never the cap: through AdtDef's parts, which state no raw Origin, Unstated; through
    // StoredParts, the holder's (DECISIONS BU-41). A path that ends at no raw layer is a broken contract.
    internal RawStep RawStep(BoundType instance, int part, ReadOnlySpan<int> positions) => binding.StoredStep(instance, part, positions);

    // SPEC 5.7 Origins, Calls (G160-4's (3a); DECISIONS BU-24): the borrows the Origin of the raw layer an instance stores (positions as
    // for RawStep) belongs to, appended to `members`: its Origin under the instance's substitution, within the instance, so its members
    // are the keepers of the slots the instance binds it to; `self`, `static` and an unstated layer have none. Each member is the scope
    // Type it is found at, the referent its borrow admits (a slot keeper's under the Type binding the slot) and whether it may keep an
    // exclusive Loan. Total: false when the members are incomplete (a keeper cycle that changes its Type arguments, or a keeper Binding's
    // storage instantiation does not complete), and for a layer a Type slot's argument supplies whose Origin steps Exclusive: its borrows
    // are the scope's that wrote the Type (a pair slot's states none). One read of the kept part and one walk of each (slot, owner Type)
    // the bound slots' keepers reach.
    internal bool RawMembers(BoundType instance, int part, ReadOnlySpan<int> positions, List<RawMember> members)
        => binding.StoredMembers(instance, part, positions, members);

    // SPEC 5.7, 5.2.1 condition 3 (DECISIONS BU-24, BU-41, BU-46; G148 (1)): a struct's or enum's stored parts under its substitution with
    // the raw Origins it gives, `self` as Holder and Unstated on each raw layer its declaration writes without `during` (RawStated), by the
    // `part` RawStep and RawMembers take (a struct's Fields, an enum's Case payload Tuples), its base in `parent`; empty for any other
    // Type. AdtDef's FieldType, CaseTypes and Base are these WithoutRawOrigins, null exactly where they are. A walk reaches through them
    // the instance that stores a raw layer as the holding value's substitution makes it, and reads that layer only through RawStep and
    // RawMembers on that instance and RawStated on the layer, never its Origin otherwise, so no premise, obligation or row names Holder or
    // Unstated (design-final §13 A2). Binding's one substitution per instance and pass, an instance first reached after Binding
    // substituted on that first read; a warm read allocates nothing.
    internal ReadOnlySpan<BoundType?> StoredParts(BoundType instance, out BoundType? parent)
    {
        if (!AdtDef.IsStruct(instance) && !AdtDef.IsEnum(instance))
        {
            parent = null;
            return [];
        }

        var storage = binding.Instance(instance);
        parent = storage.PartsBase;
        return storage.Parts;
    }
}
