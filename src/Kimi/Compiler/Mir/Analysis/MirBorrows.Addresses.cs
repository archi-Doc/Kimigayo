// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's raw addresses of the view (SPEC 15.6 Addresses, 5.2.1, 5.7; DECISIONS R4-M116, design B′ §4.1–§4.3 on B §5.1–§5.2): entries of the
// Loan table with mode Raw after the Loans, which no Loan rule meets: one per Place `P` of a `P@raw` that passes no dereference or
// owning handle's payload (SPEC 5.2.1 condition 1: `P@raw` reaches the storage of `P` alone), and one own-step entry per owning Place (an
// owning handle's payload past which `@raw` forms a pointer, or a value whose `during self` layer a Place uses), direct (naming its root)
// when the owning Place passes no borrow or raw Deref, else carried for the own-step Warning only. Each entry keeps its Place and the
// Place's steps, as a Loan does, and names its root's holder as a direct Loan does, so the readers of direct Loans read it (a part's
// address is no whole-value reference, and a call's write past a holder takes the Reached entry of the Place's Type), and it is never
// a conflict candidate (MirBorrows.Candidates). A raw Ref issues its entry (loanAt); `ownAt` gives a layer Place's own-step entry.
// A `during self` layer is decided from the MIR Place path, by the stored step Binding publishes (TyCtxt.StoredStep: Own), never by an
// Origin. A Place a location uses that is a layer or a Tuple or fixed array enclosing one inline (SPEC 5.7: read element by element)
// has one own-step row (ownAt): through no borrow or raw Deref by its own steps (R-A; Z5-R2); past one, after the walk, by its exact
// lent position (MirBorrows.Lent: the entry of the body Place its owner step applies to, LentPlace) where it has one, else by its own
// steps (Place, R-C; Z5-R1; G1276), the row a read, Copy or Move carries with its parents whole (MirBorrows.Values' Layered). A non-raw
// Borrow of a layer's Place or of a Tuple or fixed-array Place enclosing one inline issues its ordinary Loan and no mark: it sets the
// view's `tabling` bit, the base's mark condition (Owns). PQ-10, narrowed
// (registered for X-2, PD28): a read whose row is Unplaced in a view with `tabling` set is a located Unsupported_Kd at the read, making
// the view a producer. An own-step entry makes its root borrowed, so the root's accesses are listed for its departures
// (MirBorrows.OwnSteps), and the view a producer where an access may depart it (OwnSteps' MayDepart, D5, R4-M141); it exposes its
// owner's storage in the root (OwnerExposed), a plain address the whole root (ExposedHolder). The pre-pass runs once per view, after the
// Loans and before the holders. It walks the view's uses only where Loans saw a raw Ref or a projection row may be or enclose a layer (Layering's
// Type tests: each Type once per pre-pass); elsewhere no use forms an address, a row or a mark (G246). Its Type walks visit a Type, or a
// (Type, written Type) pair, once per walk (DECISIONS R4-M157), and each visit is a step of Work.
// Must not issue a Loan, carry a value (MirBorrows.Values) or record a problem (MirBorrows.Locations records PQ-10's).
internal sealed partial class MirBorrows
{
    private const int Unplaced = -2; // ownAt: a Place past a borrow or raw Deref that may be a layer, whose owner no step run names (R-C).

    private readonly MirTable<(int Root, int Key)> addressOf = new(); // By Place: plain address (Key ~projection), own-step entry (projection).
    private readonly MirTable<(int Root, int Layer)> ownAt = new(); // By a Place a location uses: its owner's own-step entry, -1 for none, Unplaced.
    private readonly HashSet<(BoundType Type, BoundType? Written)> visited = new(); // A Type walk's visited parts, cleared per walk.
    private readonly List<(int Root, int Projection, int Location, int Position)> placing = []; // ownAt's rows past a borrow or raw Deref, decided by Place.
    private int[] positions = []; // An owner's inline positions, from the part on.
    private bool tabling; // R-B: whether a non-raw borrow of the view lends a layer's Place or a Tuple or fixed-array Place enclosing one (PQ-10's borrow mark on the base).
    private int addressCount;
    private int ownCount; // The Places with an own-step row.

    // Whether a Tuple or fixed array holds a raw pointer inline, at any of its positions: each Type once per pre-pass (Layering), whose
    // visited Types all hold none, since the pre-pass stops at the first that does.
    private bool Inline(BoundType type)
    {
        for (var i = 0; i < type.Components.Count && type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray; i++)
        {
            this.Work++;
            var component = type.Components[i];
            if (component.IsPointer || (this.visited.Add((component, null)) && this.Inline(component)))
            {
                return true;
            }
        }

        return false;
    }

    // The addresses of the view (B §5.2): from every Place each location uses, after the Loans and before the holders, so that a holder an
    // address names is a flow holder.
    private void Addresses()
    {
        var locations = this.body.Statements.Length + this.body.Blocks.Length;
        (this.addressCount, this.ownCount, this.ownEntries) = (0, 0, 0);
        this.addressOf.Clear();
        this.ownAt.Clear();
        this.placing.Clear();
        this.tabling = false;
        this.Work += this.body.Projections.Length;
        if (!this.rawRefs && !this.Layering())
        {
            return; // No Place the walk would find: no raw Ref, and no Place that may be or enclose a layer.
        }

        var visitor = new Addressing(this);
        for (var block = 0; block < this.body.Blocks.Length; block++)
        {
            for (var i = 0; i <= this.body.Blocks[block].Count; i++)
            {
                visitor.At(this.Location(block, i));
                MirUses.ForEach(cases, new(block, i), ref visitor);
            }
        }

        this.Place();
        this.producing = this.producing || (this.ownEntries > 0 && this.MayDepart()); // An entry an access may depart, the Warning's (D5).
        this.Work += locations;
    }

    // Whether a Place of the view may be a `during self` layer or enclose one inline (Layer's and Encloses' Type tests, on the projection
    // rows, each a Place's last step): a row whose Type is a raw pointer, or under the view a Tuple or fixed array holding one inline.
    private bool Layering()
    {
        this.visited.Clear();
        foreach (var row in this.body.Projections)
        {
            if (row.Type is { } type && (type is { IsPointer: true } || this.Inline(cases.Type(type))))
            {
                return true;
            }
        }

        return false;
    }

    // A Place a location uses: a raw Ref's address; the own-step row of each prefix; R-B's tabling for a non-raw Borrow of a layer's or
    // enclosing Place.
    private void Use(int location, MirPlace place, MirAccess access, MirRefKind kind)
    {
        var borrows = (access & MirAccess.Borrows) != 0;
        if (borrows && kind == MirRefKind.Raw)
        {
            this.loanAt[location] = this.Formation(place, location);
        }

        for (var id = place.Projection; id > 0; id = this.body.Projections[id].Prefix)
        {
            this.Work++;
            this.Layer(place with { Projection = id }, location);
        }

        this.tabling |= borrows && kind != MirRefKind.Raw && this.Owns(place, cases.KnownTypeOf(place)); // R-B: the base's mark condition, and no mark.
    }

    // R-C (SPEC 5.7 Reading a Field through a dereference, 15.6 Addresses, 15.6 Holders): after the walk, where Exposed is final
    // (MirBorrows.Lent's precondition), each own-step row past a borrow or raw Deref is decided by one Owner walk over one step run: the
    // Place's exact lent position where it has one, else its own steps. Over a position, an owner step gives the own-step entry of the
    // body Place it applies to (MirBorrows.Lent's LentPlace, G1276): direct where LentPlace names it from the position's root (the Place
    // the steps before it name, where the body has its row), else past the Deref of the walk that gave the step (a lender's own Place, or
    // the Place's own: the base's row); none, -1. Over the own steps (no exact position), an owner past the last Deref gives its entry
    // past one (the base's row); none, Unplaced. So no row is less than the base's: Unplaced only where no position exists and the own
    // steps name no owner (G1301's classes). A read carries its row's entry with its parents whole (Values' Layered); an Unplaced one
    // carries its layer and is the location's limit where tabling is set, which makes the view a producer (PQ-10 narrowed: the base
    // marked a borrow of the view, so no program the base accepts is put at the limit). Every position is made before any entry, and no
    // own-step entry, here or in the walk, refuses a lender: it exposes its owner's storage only (OwnerExposed), and no position is
    // followed through an owner (Lender refuses a struct's and a Case's stores).
    private void Place()
    {
        for (var i = 0; i < this.placing.Count; i++)
        {
            var (root, projection, location, _) = this.placing[i];
            this.placing[i] = (root, projection, location, this.Lent(new MirPlace(root, projection)));
        }

        var unplaced = 0;
        foreach (var (root, projection, location, position) in this.placing)
        {
            var place = new MirPlace(root, projection);
            var type = cases.TypeOf(place);
            var entry = Unplaced;
            if (position >= 0)
            {
                var steps = this.LentRun(position, out var key);
                var at = this.Owned(this.RootOf(key), steps, type);
                entry = at < 0 ? -1 : this.OwnStep(this.LentPlace(position, at), location);
            }
            else if (this.Owned(place, type) is var at and >= 0)
            {
                entry = this.OwnStep(place with { Projection = this.walk[at].Prefix }, location);
            }

            this.ownCount -= entry == -1 ? 1 : 0;
            unplaced += entry == Unplaced ? 1 : 0;
            this.ownAt.Entry((root, projection), out _) = entry;
        }

        this.producing |= this.tabling && unplaced > 0; // N8: an Unplaced read's limit is the replay's to record (D5).
    }

    // A root key's root (Key's inverse).
    private int RootOf(int key) => key < this.body.Locals.Length ? key : ~(key - this.body.Locals.Length);

    // Whether a Place's own steps name an owner whose part at them is a layer stepping Own or encloses one (PQ-10's mark condition on the
    // base: R-B's tabling).
    private bool Owns(MirPlace place, BoundType? type)
        => type is { } known && (known.IsPointer || known.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray) &&
            this.Owned(place, known) >= 0;

    // The owner step of a Place's own steps (Owned over them, left in the walk buffer); -1 for none.
    private int Owned(MirPlace place, BoundType type)
    {
        var count = this.placeSteps.Walk(place, reborrow: false, ref this.walk);
        return this.Owned(place.Root, this.walk.AsSpan(0, count), type);
    }

    // The owner step of a step run from a root (Owner) where the owner's part at the run's positions is, for a raw pointer, a layer stepping
    // Own (TyCtxt.StoredStep), else a Tuple or fixed array enclosing one inline (Encloses); -1 for none.
    private int Owned(int root, ReadOnlySpan<MirStep> steps, BoundType type)
    {
        var at = this.Owner(root, steps, out var part, out var length);
        if (at < 0 || cases.KnownTypeOf(new MirPlace(root, steps[at].Prefix)) is not { } instance)
        {
            return -1;
        }

        this.visited.Clear();
        return (type.IsPointer ? types.StoredStep(instance, part, this.positions.AsSpan(0, length)) == HoldStep.Own : this.Encloses(instance, part, length, type)) ? at : -1;
    }

    // `P@raw`'s entry (B §5.2 item 1): through no borrow or raw Deref the plain address of `P`, or past an owning handle's payload its
    // owning Place's own-step entry; past one with an owning handle's payload after the last, that owner's entry past a dereference; else
    // none, the value being the borrow's parents.
    private int Formation(MirPlace place, int location)
    {
        var (count, own) = (this.AddressWalk(place, location), -1);
        for (var i = 0; i < count; i++)
        {
            own = this.walk[i].Class == MirStepClass.Own ? i : own;
        }

        var deref = this.LastDeref(count);

        if (own > deref)
        {
            return this.OwnStep(place with { Projection = this.walk[own].Prefix }, location);
        }

        return deref < 0 ? this.Address(place, ~place.Projection, location) : -1;
    }

    // A raw address's step walk at a location: its Place's steps, and where the location's rvalue is a Storage, the element step that
    // `&raw P[i]` takes after them (MirSteps.Element), so that a Storage is exactly a raw Ref of an element of its Place (O-92 (a)).
    private int AddressWalk(MirPlace place, int location)
    {
        var count = this.placeSteps.Walk(place, reborrow: false, ref this.walk);
        return location < this.body.Statements.Length && this.body.Statements[location] is { Kind: MirStatementKind.Assign, Rvalue.Kind: MirRvalueKind.Storage }
            ? this.placeSteps.Element(place, count, ref this.walk) : count;
    }

    // A Place's own-step row (B §5.2 item 2; R-A, R-C), one per Place a location uses, decided once per view: the own-step entry of the
    // owner whose part the Place is a `during self` layer of (a raw pointer) or encloses one inline (a Tuple or fixed array); -1 for none.
    // Through no borrow or raw Deref a Place's own steps are its exact position, decided here; past one the row is listed (Unplaced until
    // then) and decided after the walk (Place).
    private int Layer(MirPlace layer, int location)
    {
        if (cases.KnownTypeOf(layer) is not { } type || !(type.IsPointer || type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray))
        {
            return -1;
        }

        if (this.ownAt.TryGetValue((layer.Root, layer.Projection), out var entry))
        {
            return entry;
        }

        var count = this.placeSteps.Walk(layer, reborrow: false, ref this.walk);
        entry = this.LastDeref(count) >= 0 ? Unplaced : this.Owned(layer.Root, this.walk.AsSpan(0, count), type) is var at and >= 0
            ? this.OwnStep(layer with { Projection = this.walk[at].Prefix }, location) : -1;
        if (entry == Unplaced)
        {
            this.placing.Add((layer.Root, layer.Projection, location, -1));
        }

        this.ownCount += entry != -1 ? 1 : 0;
        this.ownAt.Add((layer.Root, layer.Projection), entry);
        return entry;
    }

    // A Place's own-step row once the pre-pass made the table (-1: none).
    private int OwnStepAt(int root, int projection) => this.ownCount > 0 && this.ownAt.TryGetValue((root, projection), out var entry) ? entry : -1;

    // The owning Place's own-step entry, made at its first formation: direct where the owning Place passes no borrow or raw Deref.
    private int OwnStep(MirPlace owner, int location) => this.Address(owner, owner.Projection, location);

    // A Place's address entry at its first formation, keyed by the Place (key ~projection: the plain address of the Place itself, which
    // passes no dereference; its projection: the Place as an owning Place's own-step entry), with the Place's steps, as a Loan names its
    // Place; direct where the steps pass no borrow or raw Deref.
    private int Address(MirPlace place, int key, int location)
    {
        if (this.addressOf.TryGetValue((place.Root, key), out var entry))
        {
            return entry;
        }

        var count = this.placeSteps.Walk(place, reborrow: false, ref this.walk);
        var direct = this.LastDeref(count) < 0;
        var address = new Loan
        {
            Root = this.Key(place.Root),
            Place = place,
            Group = -1,
            Mode = MirRefKind.Raw,
            Holder = -1,
            Named = direct ? place.Root : -1,
            Direct = direct,
            ReceiverSteps = count,
            OwnStep = key >= 0,
            Location = location,
        };
        entry = this.Record(this.loanCount + this.addressCount++, address, count);
        if (direct && place.Root >= 0)
        {
            this.named[place.Root] |= (byte)(NamedHolder | (key >= 0 ? OwnerExposed : ExposedHolder)); // What the entry exposes (Exposed).
        }

        if (key >= 0)
        {
            this.borrowed[this.Key(place.Root)] = true; // Its root's accesses are listed for its departures (MirBorrows.OwnSteps).
            this.ownEntries++;
        }

        this.addressOf.Add((place.Root, key), entry);
        return entry;
    }

    // The owner step of a step run from a root (B §5.2 item 2; one walk for a Place's own steps and an exact position's steps): back over
    // Tuple elements and fixed-array elements (the inline positions) to a struct's Field (the part) or an enum Case payload's component
    // under a Downcast (the part the Case, the first position the component); its index (the step whose Prefix is the owner Place: the
    // Field's, or the Downcast's), -1 for any other path. The positions are left in the scratch, from the part on.
    private int Owner(int root, ReadOnlySpan<MirStep> steps, out int part, out int length)
    {
        (part, length) = (-1, 0);
        for (var i = steps.Length - 1; i >= 0; i--)
        {
            var step = steps[i];
            MirDataflow.Grow(ref this.positions, length + 1);
            if (step.Kind == MirProjectionKind.Field && i > 0 && steps[i - 1].Kind == MirProjectionKind.Downcast)
            {
                (this.positions[length++], part) = (step.Arg, steps[i - 1].Arg);
                this.positions.AsSpan(0, length).Reverse();
                return i - 1;
            }

            if (cases.KnownTypeOf(new MirPlace(root, step.Prefix)) is not { } holding)
            {
                return -1;
            }

            if (step.Kind == MirProjectionKind.Field && AdtDef.IsStruct(holding))
            {
                part = step.Arg;
                this.positions.AsSpan(0, length).Reverse();
                return i;
            }

            if (!(step.Kind == MirProjectionKind.Field && holding.Kind == BoundTypeKind.Tuple) &&
                !(step.Kind is MirProjectionKind.ConstIndex or MirProjectionKind.Index && holding.Kind == BoundTypeKind.FixedArray))
            {
                return -1;
            }

            this.positions[length++] = step.Kind == MirProjectionKind.Field ? step.Arg : 0;
        }

        return -1;
    }

    // Whether an inline Type at the scratch's positions encloses a layer the instance's part stores stepping Own: never where the
    // declaration writes nothing there (TyCtxt.WrittenAt null; DECISIONS BU-58, a layer steps Own only where written), else each (Type,
    // written Type) once per enclosing use, what the step reads beside the instance and part the use fixes (DECISIONS R4-M157).
    private bool Encloses(BoundType instance, int part, int length, BoundType type)
    {
        if (TyCtxt.WrittenAt(instance, part, this.positions.AsSpan(0, length)) is not { } written || !this.visited.Add((type, written)))
        {
            return false;
        }

        for (var i = 0; i < type.Components.Count && type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray; i++)
        {
            this.Work++;
            MirDataflow.Grow(ref this.positions, length + 1);
            this.positions[length] = i;
            var component = type.Components[i];
            if (component.IsPointer
                ? types.StoredStep(instance, part, this.positions.AsSpan(0, length + 1)) == HoldStep.Own
                : this.Encloses(instance, part, length + 1, component))
            {
                return true;
            }
        }

        return false;
    }

    // The addresses' uses of one location: its Places with their accesses, an operand's Place read by a Copy, Move or PairAcquire, an
    // Opaque site's undecided use at its root (MirDescriptors.Judged, DECISIONS Z17).
    private struct Addressing(MirBorrows owner) : IMirUses
    {
        private int location;

        public readonly void Operand(in MirOperand operand)
        {
        }

        public readonly void Effect(MirEffect effects, int site)
        {
        }

        public readonly void Place(MirPlace place, MirAccess access, MirRefKind kind) => owner.Use(this.location, MirDescriptors.Judged(place, access), access, kind);

        internal void At(int location) => this.location = location;
    }
}
