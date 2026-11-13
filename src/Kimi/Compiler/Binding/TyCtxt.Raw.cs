// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// The raw layers an instance stores, read after Binding over Raw Origins (Binding.RawOrigins.Steps.cs, .Keepers.cs) and Binding's instance
// storage (Binding.Storage.cs; SPEC 5.7, 5.2.1 condition 3; DECISIONS BU-24, BU-39, BU-41, BU-46; R4's constraint 2): the authority such a
// layer's Origin can keep (StoredStep, which A5's addresses read), the borrows it belongs to (RawMembers, A5's holding walk), the parts a
// written input holds at its own address (OwnLayers) and the borrows each stated layer there belongs to among a call's inputs
// (LayerMembers), which A5's Cover reads, a Place's key (PlaceKey) one step along its path (Select, Base) or to the part at its own
// address (Leading; C-189, C-211), which A5's pun rule reads, whether a declaration states a
// layer without an Origin (RawStated, Binding's), the stored parts through which a walk reaches the instance storing it
// (StoredParts, retention's), the Type a declaration writes at a stored part's inline positions (WrittenAt, DECISIONS BU-58, A5's
// addresses), whether a Type states Unstated (StatesUnstated), which only a kept part does (MirValidator's Local row), and SPEC 5.2's
// punnable set (Punnable, its one owner), which A5's pun rule reads.
internal sealed partial class TyCtxt
{
    private readonly Dictionary<BoundType, bool> punnable = new(); // Punnable's answer per composite Type, memoized like the interning.
    private readonly List<Hold> rawForm = []; // Decided's Holdings scratch, a stack.

    // SPEC 5.7 Calls, Fits (DECISIONS BU-46): whether a declaration states a raw layer without an Origin: true exactly for a layer a Field,
    // Case payload or base clause writes without `during`, which an instance's kept parts (StoredParts) state as Unstated at any depth
    // below them, through every nested instance, Type argument, pair slot's argument, element and `during self` pointee they reach; false
    // for a layer the walk's own Type writes (a parameter's or local's Type expression, a Type argument or pair argument it supplies) and
    // for one stating an Origin. A read of the layer Type; total; allocates nothing.
    internal static bool RawStated(BoundType layer) => layer.RawOrigin?.Kind == OriginKind.Unstated;

    // SPEC 5.2, 4.7.1, 15.1.5 (PLAN G672, G806): whether a part of a raw Place of this Type is itself a raw Place (RawPlaces' rows
    // exist), so that a Take moves it without state tracking: an `owner` struct's stored Field or base, a Tuple's element, a fixed
    // array's element at any position, from-end included. An enum's Case payload is none (SPEC 5.2 lists Fields and elements: a payload is
    // reached by matching the enum, which reads it), nor is a Closure's environment, an Array's, view's or Dictionary's element (the
    // referent of `index`/`indexUniq`) or a reference's or handle's part (a referent). The one statement of the rule: Binding's IsRawPlace
    // walk (ElementAccess), MIR's move paths (MirMoveData.Resolve) per step and RawPlaces' rows. A read of the Type; total; allocates nothing.
    internal static bool RawPart(BoundType? container) => container is { Semantics: Parsing.SemanticsKind.Owner } &&
        (container.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray || AdtDef.IsStruct(container));

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

    // SPEC 5.2 Placement, 5.6 (C-189): whether the operation contract stores no value through its parameter slot `input`, numbered as
    // Signature numbers it (the catalog's NoStoreRow: `slice` its storage, `initialize` its value, `release` its storage, `transferBytes`
    // its source); false for every other callee and slot: a function with a body, a foreign import, a missing row. Static; allocates nothing.
    internal static bool NoStore(BindingSymbol? callee, int input) => KimiLibraryCatalog.NoStore(callee, input);

    // SPEC 5.2 Type-punned access (PLAN G562-a; the set's one owner): whether a Type is punnable: the `owner` Scalars of 3.1 (ScalarTypes'
    // one definition), raw pointer Types, and Tuples and fixed arrays of punnable Types, an `owner` layer as its component. An abstract
    // part is a binding's (SPEC 8.15.1: every binding, a Scalar of any size among them; PLAN G792), so it is punnable. Any other Type is
    // not. Readers: A5's pun rule (MirBorrows.PunTypes, whose own fact is how much of a Scalar a punnable Type holds). A function of the
    // interned Type, each composite one decided once (`punnable`, kept like the interning: a Type's answer never changes); a walk that
    // ends with the Type (no bound of its own); total; allocates nothing for a Type already decided.
    internal bool Punnable(BoundType type)
    {
        if (ScalarTypes.Supports(type) || type.IsAbstract || type is { Kind: BoundTypeKind.Semantics, Semantics: Parsing.SemanticsKind.Raw })
        {
            return true;
        }

        var parts = type switch
        {
            { Kind: BoundTypeKind.Tuple } => type.Components.Count,
            { Kind: BoundTypeKind.FixedArray } or { Kind: BoundTypeKind.Semantics, Semantics: Parsing.SemanticsKind.Owner, Components.Count: > 0 } => 1,
            _ => -1,
        };

        if (parts < 0)
        {
            return false;
        }

        if (!this.punnable.TryGetValue(type, out var answer))
        {
            answer = true;

            // By index: a foreach over the interface would allocate an enumerator per call.
            for (var i = 0; answer && i < parts; i++)
            {
                answer = this.Punnable(type.Components[i]);
            }

            this.punnable[type] = answer;
        }

        return answer;
    }

    // SPEC 5.2 (PLAN G806, the owner's O-220 (a)): the raw Places directly within a raw Place of key `at`, appended to `into`; the raw
    // Place itself is its reader's (the root it holds), and a reader takes "transitively" by reading each row's key again. Rows exactly
    // where RawPart holds: an `owner` struct's stored Fields, in MIR's numbering, after its base's rows (an inherited Field is a Field of
    // the struct; the base subobject is none, SPEC 6.2), a Tuple's elements, a fixed array's element (one row for every index: Element,
    // a grid of the element's stride; whether it has one is the reader's length reading, A5's Sized). Each row's key is Select's
    // (Leading's for the element), so a part's written Type and holder stay. Lead: at the raw Place's own address where IMPL 21.1 fixes
    // it there (Leading's one step: a `#Layout("C")` struct's first Field; an element row, whose element 0 is there); every other row is
    // off: IMPL 21.1.2 leaves a Kimigayo-layout offset open and a C-layout later Field's needs sizes (Layout's), so a verdict resting on
    // where it lies is the reader's rested located Unsupported_Kd. The answer is one of three classes (O-228 (a), O-231 (a)), read from
    // Holdings' one form table at the key (Decided): Listed, the rows (RawPart's forms); Empty, no row: an enum (a Case payload is reached by
    // matching the enum, which reads it), a Closure (its environment) and every form whose Holdings rows hold nothing inline (a
    // referent, a collection's elements, Type arguments, an erased environment, a Scalar); Undecided, so that no reader counts too few:
    // an Abstract row (SPEC 8.15.1, 4.4.2: any admitted binding's raw Places, as Punnable's abstract row states it), an Unsupported row,
    // an unbound part, an Inline row of any other form (an owner layer, a pair layer's owner case: its target's, not listed here; none
    // reaches it on dev) and Binding's Limit. Rows are published for RawPart's forms whatever the answer (as Holdings publishes its
    // entries at a Limit): at Undecided they are the parts Binding's storage states, an unbound part giving none. At Undecided the
    // reader keeps its own reading (O-231 (a): the pun rule its written parts; A5 its abstract rule or its located Unsupported_Kd),
    // never "empty". Total, a function of the key; allocates nothing warm.
    // Must not: list an enum's Case payload or a Closure's environment (Holdings' by-value reading, the holder and written side, keeps
    // them); read a size, an offset or a length; decide a pun, typed or none (the reader's).
    internal RawPlacesAnswer RawPlaces(PlaceKey at, List<RawPlace> into) => this.RawPlaces(at, on: true, into);

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

    // SPEC 5.2.1, 5.7's table (PLAN G338, G583): the borrows a stated layer a written input holds at its own address belongs to (a raw
    // parameter's own layer, or an element's, a Field's or a payload's that OwnLayers lists) among a call's instantiated inputs, Binding's
    // one reading, the RawMembers query the storage walk makes at that layer, appended to `members`, each member's Input the slot it is
    // found at; none for `self`, `static` or an unstated layer. False as RawMembers is.
    internal bool LayerMembers(BoundType layer, ReadOnlySpan<BoundType> inputs, List<RawMember> members) => binding.LayerMembers(layer, inputs, members);

    // SPEC 5.2.1 Retained dependencies, 5.7 Calls, Reading and Writing a Field, 15.6.4 (PLAN G583, G598, C-189): the parts the value at a
    // Place's key holds at its own address, Binding's one reading (Binding.OwnLayers), so that the storage walk's output fold and A5's
    // covering and pun rule enumerate the same raw layers by one criterion. Input: a PlaceKey: a call input's PlaceKey.Root(input) (a
    // contract slot's declared Type under a call's substitution, no Written, no Holder, no dereference), or a Place's key, Select's fold of
    // its path from its root. At its own address means the storage walk's by-value relation (Binding.ByValue) applied recursively at any
    // holder: Tuple and fixed-array elements, a struct's Fields and base, an enum's Case payloads (a Case key: its payload's components with
    // the Case's written Types), a Closure's environment bindings, a pair layer's owner case, an own step's payload or elements (`obj/T`,
    // Array, Dictionary, `string`) and a `Loan<T>`'s argument; any other Type argument adds nothing. Each part is read once per call through
    // Holdings as the contract's body sees it (a Closure part by ClosureEnvironment, DECISIONS R4-M148). Output, appended to `into` in walk
    // order: (layer, step, Past) for each raw layer met there, not gone past, its step that at the Place: DeclaredStep under its holding
    // instance, except that a `during self` layer, where the key's path dereferences, has the step of the Origin reading it gives (SPEC 5.7
    // Writing a Field), and Unsupported through a U2 path; (null, step, Past) for each other part that reaches storage (a borrow or pair
    // case, an rc or arc view, an abstract, erased, unsupported or unbound part); nothing for a part with no entry. Past is the key past
    // the entry, with the path's last dereferenced layer past it (Binding.PastThrough). Unsupported is never a stated Origin: a reader takes
    // it as no answer (A5: a located Unsupported_Kd or Φ kept, never a dropped Φ). The step is not the walked signature's cap (RawStepAt),
    // which a covering needs not. A `during self` layer reached through a dereference has StepOf's cap-free kind (DECISIONS BU-24): past a
    // `ref` it is Exclusive, an over-approximation of write authority that only adds writes (conservative); a borrow entry keeps Holdings'
    // step (review N12). Named limit (review N13): a path rooted at a constructor's or `drop`'s receiver starts as PlaceKey.Root (no
    // dereference), where PathOrigin gives the receiver's Origin, so past a `self` layer of the receiver the answer is Address, never a
    // dropped Φ; where a valid program reaches it A5 gives a located Unsupported_Kd. Removal: a ReceiverRoot(type, receiver) key, the
    // Origin PathOrigin's Receiver step gives, when a reader keys such a Place. The key holds Types only (PlaceKey.Through, a layer Type as
    // every MIR Type is), so MIR reads no Origin from it but through the readers the raw Origin role ratchet covers (review N14). False at
    // Binding's Limit. Total over what Binding accepts, a function of the key, allocating nothing warm. Must not decide a covering or a pun
    // (A5's), read a call's arguments or solved Origins, or test a Type's Kind or Semantics to restrict a form.
    internal bool OwnLayers(PlaceKey at, List<OwnLayer> into) => binding.OwnLayers(at, into);

    // SPEC 5.7 Origins, Reading a Field, Writing a Field (C-189): the key of the Place `part` selects from `at`'s Place, one step of a
    // Place's path from its root (PlaceKey.Root), `part` numbered as MIR numbers it: a struct's Field, an enum's Case (then that payload's
    // component), a Tuple's or Closure's component; unread for a Type with one Place at or past its own address (a fixed array's element,
    // a raw pointer's pointee, a borrow's or view's referent, an owning handle's payload, a collection's element, a pair layer's target).
    // A slot is replaced by the holding value's binding (the kept part, StoredParts). The path's dereference past a value is PathOrigin's
    // one rule (Binding.Dereferenced over Reached), a `during self` layer first read as ReadHolder reads it. A struct's base is selected
    // by Base. Total: an unbound part gives Type null. A function of (at, part); allocates nothing warm. Must not decide a pun or
    // covering (A5's).
    internal PlaceKey Select(PlaceKey at, int part) => binding.Select(at, part);

    // SPEC 5.7 (C-189): the key of a struct's base Place (MIR's Base, once per depth): its kept base (StoredParts' parent), inline, the
    // path's dereference kept; Type null for any other Type.
    internal PlaceKey Base(PlaceKey at)
    {
        BoundType? parent = null;
        if (at.Type is { } type && AdtDef.IsStruct(type))
        {
            _ = this.StoredParts(type, out parent);
        }

        return at with { Type = parent, Written = null, Holder = null, Case = -1 };
    }

    // SPEC 4.1, 6.7.1, IMPL 21.1.2, 21.1.3 (C-211, PLAN G667c, G808): the key of the part the specification places at a Place's own
    // address, the one step of a Place's leading chain: a fixed array's element, whatever the array's length (element i is at offset
    // i * stride; `element` true), a `#Layout("C")` struct's first Field in written order (offset 0; `element` false), or a nonnull
    // Option's Some payload (NonnullSome: the payload is the Option's whole word; `element` false), each as Select steps it (the payload:
    // the Some Case, then its component 0, MIR's Downcast and Field), the path's dereference kept. The first written Field is MIR's Field
    // 0: a C-layout struct has no base and one storage fragment (SPEC 6.7.1), and AdtDef keeps stored Fields in declaration order. Type
    // null for every other Type (a Kimigayo-layout struct, a Tuple, any other enum or a Case key, a Closure: IMPL 21.1.2-21.1.3 promise no
    // offset there), for an unbound key and for a first Field Binding did not bind. `element` true always comes with a part (a fixed
    // array's Type states its element at any length), so a reader takes the part of an element step without a null test; a null Type has
    // `element` false. Reads no length: whether an array has an element is its reader's (A5's Sized). The answer's Type is a function of
    // the key's Type, so a reader steps a Type that has no path (a written or stride Type, a chain row) as PlaceKey.Root of it. Total, a
    // function of the key; allocates nothing warm. Must not decide a pun, a covering or a designation (A5's).
    internal PlaceKey Leading(PlaceKey at, out bool element)
    {
        element = at is { Case: < 0, Type.Kind: BoundTypeKind.FixedArray };
        if (element || (at is { Case: < 0, Type: { } type } && AdtDef.IsStruct(type) && type.Symbol!.Adt is { IsCLayout: true }))
        {
            return binding.Select(at, 0);
        }

        return at.Case < 0 && this.NonnullSome(at.Type) is var some and >= 0
            ? binding.Select(binding.Select(at, some), 0) : at with { Type = null, Written = null, Holder = null, Case = -1 };
    }

    // IMPL 21.1.3 (G808): the nonnull Option: `Option<R>`, R a `ref`, `uniq`, `obj`, `rc`, `arc`, `objref` or `objuniq` layer, is one
    // pointer-sized word, null None and every valid R Some. The Some Case's ordinal (KimiLibrary.OptionSome) for such a Type; -1 for every
    // other (`Option<raw/T>`, `Option<Weak<S>>`, `Option<Option<R>>`, an Option of a Tuple, a Scalar, an abstract or a pair part, any
    // other enum). The one statement of IMPL's R list: Leading reads it, and the Backend's Layout reads it with PLAN U8. Total; allocates
    // nothing.
    internal int NonnullSome(BoundType? type)
        => type is { Semantics: Parsing.SemanticsKind.Owner, Components: [{ Kind: BoundTypeKind.Semantics } payload] } &&
            ReferenceEquals(type.Symbol, library.Option) && payload.Semantics is Parsing.SemanticsKind.Ref or Parsing.SemanticsKind.Uniq or
            Parsing.SemanticsKind.Obj or Parsing.SemanticsKind.Rc or Parsing.SemanticsKind.Arc or Parsing.SemanticsKind.ObjRef or
            Parsing.SemanticsKind.ObjUniq ? library.OptionSome : -1;

    // SPEC 5.2, 3.8, 15.6.1 (C-189): normalized Type identity with Origins treated as equal, raw Origins included: the structural part of a
    // fit (Binding's relation walk, every Origin binding equal, no Never root rule), symmetric; reads no premise. Total, O(size of the
    // smaller Type), allocates nothing. Must not stand for a fit (FitsStructuralPartAt keeps Never's root rule).
    internal bool SameStructure(BoundType a, BoundType b) => binding.SameStructure(a, b);

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

    // RawPlaces' whitelist, read from Holdings' one form table (SPEC 3.3.4; Binding.Holdings, its rows at the key; O-228 (a)): a form is
    // decided where every row is one of: a layer's or a collection's referent, a Type argument, an erased environment (none of them
    // inline), no row at all (a Scalar, a Range, a Function Item), or an Inline part of a form whose inline parts SPEC 5.2 classifies:
    // RawPart's (Fields, base, elements: the rows), an enum's Case payloads or a Closure's environment (no raw Place). Every other row
    // leaves it unknown: Abstract (SPEC 8.15.1: a binding's), Unsupported (a form with no row), a part Binding did not bind, an Inline
    // part of any other form (an owner layer's or a pair layer's owner case: its target at its own address, not listed here), a Limit.
    private bool Decided(PlaceKey at, BoundType type)
    {
        var inline = RawPart(type) || AdtDef.IsEnum(type) || type.Kind == BoundTypeKind.Closure;
        var start = this.rawForm.Count;
        var decided = binding.Holdings(type, at.Written, at.Holder, this.rawForm);
        for (var i = start; decided && i < this.rawForm.Count; i++)
        {
            decided = this.rawForm[i] switch
            {
                { Step: HoldStep.Abstract or HoldStep.Unsupported } => false,
                { Step: HoldStep.Inline } part => inline && part.Part is not null,
                _ => true,
            };
        }

        this.rawForm.RemoveRange(start, this.rawForm.Count - start);
        return decided;
    }

    // RawPlaces' rows and answer; `on`: the raw Place is at the address its reader starts from (false for a base's rows).
    private RawPlacesAnswer RawPlaces(PlaceKey at, bool on, List<RawPlace> into)
    {
        if (at.Type is not { } type)
        {
            return RawPlacesAnswer.Undecided;
        }

        var decided = this.Decided(at, type);
        if (at.Case >= 0 || !RawPart(type))
        {
            return decided ? RawPlacesAnswer.Empty : RawPlacesAnswer.Undecided;
        }

        var answer = decided ? RawPlacesAnswer.Listed : RawPlacesAnswer.Undecided;
        var lead = this.Leading(at, out var element);
        if (element)
        {
            into.Add(new(lead, on, Element: true));
            return answer;
        }

        // A base is a struct, listed unless its own storage is undecided; no base gives none.
        if (AdtDef.IsStruct(type) && this.Base(at) is { Type: not null } parent && this.RawPlaces(parent, on: false, into) != RawPlacesAnswer.Listed)
        {
            answer = RawPlacesAnswer.Undecided;
        }

        var count = AdtDef.IsStruct(type) ? AdtDef.Count(type) : type.Components.Count;
        for (var i = 0; i < count; i++)
        {
            into.Add(new(binding.Select(at, i), on && i == 0 && lead.Type is not null, Element: false));
        }

        return answer;
    }
}
