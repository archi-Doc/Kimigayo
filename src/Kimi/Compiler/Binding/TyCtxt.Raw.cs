// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// The raw layers an instance stores, read after Binding over Raw Origins (Binding.RawOrigins.Steps.cs, .Keepers.cs) and Binding's instance
// storage (Binding.Storage.cs; SPEC 5.7, 5.2.1 condition 3; DECISIONS BU-24, BU-39, BU-41, BU-46; R4's constraint 2): the authority such a
// layer's Origin can keep (StoredStep, which A5's addresses read), the borrows it belongs to (RawMembers, A5's holding walk), the borrows
// a raw parameter's own stated layer belongs to among a call's inputs (LayerMembers, A5's Cover), whether a declaration states a
// layer without an Origin (RawStated, Binding's), the stored parts through which a walk reaches the instance storing it
// (StoredParts, retention's), the Type a declaration writes at a stored part's inline positions (WrittenAt, DECISIONS BU-58, A5's
// addresses), and whether a Type states Unstated (StatesUnstated), which only a kept part does (MirValidator's Local row).
internal sealed partial class TyCtxt
{
    // SPEC 5.7 Calls, Fits (DECISIONS BU-46): whether a declaration states a raw layer without an Origin: true exactly for a layer a Field,
    // Case payload or base clause writes without `during`, which an instance's kept parts (StoredParts) state as Unstated at any depth
    // below them, through every nested instance, Type argument, pair slot's argument, element and `during self` pointee they reach; false
    // for a layer the walk's own Type writes (a parameter's or local's Type expression, a Type argument or pair argument it supplies) and
    // for one stating an Origin. A read of the layer Type; total; allocates nothing.
    internal static bool RawStated(BoundType layer) => layer.RawOrigin?.Kind == OriginKind.Unstated;

    // SPEC 5.2, 4.7.1, 15.1.5 (PLAN G672): whether a part of a raw Place of this Type is itself a raw Place, so that a Take moves it
    // without state tracking: the storage of an `owner` struct (a stored Field, a base), Tuple (an element) or fixed array (an element at
    // any position, from-end included) holds the part itself. So does an `owner` enum's: a Case payload's components are parts of the
    // value's Place (SPEC 3.4), which no Take observes, since no source form takes a payload Place in place (a Subject is borrowed,
    // SPEC 15.1.6; `@move` takes the whole value). An Array's, view's or Dictionary's element is the referent of `index`/`indexUniq`,
    // and a reference's or handle's part a referent: never raw Places. The one statement of the rule: Binding's IsRawPlace walk
    // (ElementAccess) and MIR's move paths (MirMoveData.Resolve) read it per step. A read of the Type; total; allocates nothing.
    internal static bool RawPart(BoundType? container) => container is { Semantics: Parsing.SemanticsKind.Owner } &&
        (container.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray || AdtDef.IsStruct(container) || AdtDef.IsEnum(container));

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

    // DECISIONS BU-58: the Type the declaration of an instance's part writes at inline positions (as for StoredStep), Binding's one reading
    // (Binding.WrittenAt), while it is a Tuple, a fixed array or a raw layer (at a raw layer, exactly the layer StoredStep's cap reads); null
    // at or past any other Type, where no declaration writes the part, and at an enum's []. Monotone from a part's first positions (a
    // struct's [], a Case's [i]). The facts at and below a stored inline node are a function of its kept Type, this and the instance;
    // where this is null, every raw layer below steps StepOf of its kept layer (Unstated, Static or Exclusive, never Shared or Own) and
    // RawMembers appends nothing. Reads the declaration only, O(|positions|), total; allocates nothing.
    internal static BoundType? WrittenAt(BoundType instance, int part, ReadOnlySpan<int> positions) => Binding.WrittenAt(instance, part, positions, out _);

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
    internal HoldStep StoredStep(BoundType instance, int part, ReadOnlySpan<int> positions) => binding.StoredStep(instance, part, positions);

    // SPEC 5.7 Origins, Calls (G160-4's (3a); DECISIONS BU-24): the borrows the Origin of a raw layer a holder stores belongs to, by the key
    // of the Holdings read that met it (the layer, the Type its declaration writes there, the holding instance), appended to `members`: its
    // Origin under the instance's substitution, within the instance, so its members
    // are the keepers of the slots the instance binds it to; `self`, `static` and an unstated layer have none. Each member is the scope
    // Type it is found at, the referent its borrow admits (a slot keeper's under the Type binding the slot) and whether it may keep an
    // exclusive Loan. Total: false when the members are incomplete (a keeper cycle that changes its Type arguments, or a keeper Binding's
    // storage instantiation does not complete), and for a layer a Type slot's argument supplies whose Origin steps Exclusive: its borrows
    // are the scope's that wrote the Type (a pair slot's states none). One walk of each (slot, owner Type) the bound slots' keepers reach.
    // The key is the caller's: a `layer` that is no raw layer answers true with no members, where StoredStep's path query throws; Holdings
    // publishes a Members entry only at a raw layer, so its reader cannot pass one.
    internal bool RawMembers(BoundType layer, BoundType? written, BoundType holder, List<RawMember> members)
        => binding.StoredMembers(layer, written, holder, members);

    // SPEC 5.2.1, 5.7's table (PLAN G338): the borrows a raw parameter's own stated layer belongs to among a call's instantiated inputs,
    // Binding's one reading, appended to `members`, each member's Input the slot it is found at; none for `self`, `static` or an unstated
    // layer. False as RawMembers is.
    internal bool LayerMembers(BoundType layer, ReadOnlySpan<BoundType> inputs, List<RawMember> members) => binding.LayerMembers(layer, inputs, members);

    // SPEC 5.7, 5.2.1 condition 3 (DECISIONS BU-24, BU-41, BU-46): a struct's or enum's stored parts under its substitution with
    // the raw Origins it gives, `self` as Holder and Unstated on each raw layer its declaration writes without `during` (RawStated), by the
    // `part` StoredStep takes (a struct's Fields, an enum's Case payload Tuples), its base in `parent`; empty for any other
    // Type. AdtDef's FieldType, CaseTypes and Base are these WithoutRawOrigins, null exactly where they are. A walk reaches through them
    // the instance that stores a raw layer as the holding value's substitution makes it, and reads that layer only through StoredStep and
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
