// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

// A5's raw addresses of the view (SPEC 15.6 Addresses, 5.2.1, 5.7; DECISIONS R4-M116, design B′ §4.1–§4.3 on B §5.1–§5.2): entries of the
// Loan table with mode Raw after the Loans, which no Loan rule meets: one per root a `P@raw` addresses through no dereference or owning
// handle's payload, and one own-step entry per owning Place (an owning handle's payload past which `@raw` forms a pointer, or a value
// whose `during self` layer a Place uses), direct (naming its root) when the owning Place passes no borrow or raw Deref, else carried
// for the own-step Warning only. Each names its root's holder as a direct Loan does, so the readers of direct Loans read it, and it is
// never a conflict candidate (MirBorrows.Candidates). A raw Ref issues its entry (loanAt); `ownAt` gives a layer Place's own-step entry.
// A `during self` layer is decided from the MIR Place path, by the stored step Binding publishes (TyCtxt.RawStep: Own), never by an
// Origin. The untracked rule (PQ-2): a raw value formed with a direct address or exclusive Loan naming a local that is no holder also
// carries Φ_E. A non-raw Borrow of a layer's Place or of a Tuple or fixed-array Place enclosing one inline, and a Copy or Move of such an
// enclosing Place, mark its location for a located Unsupported_Kd and make the view a producer (PQ-10; registered for X-2, PD28; removal
// stage: the Loan records the layer's own-step entry and positions). An own-step entry makes its root borrowed, so the root's accesses are
// listed for its departures (MirBorrows.OwnSteps), and the view a producer where an access may depart it (OwnSteps' MayDepart, D5,
// R4-M141). The pre-pass runs once per view, after the Loans and before the holders. It walks the view's uses only where Loans saw a raw
// Ref or a projection row may be or enclose a layer (Layer's and Encloses' Type tests: each Type once per pre-pass); elsewhere no use
// forms an address or a mark (G246). Its Type walks visit a Type, or a (Type, written Type) pair, once per walk (DECISIONS R4-M157), and
// each visit is a step of Work.
// Must not issue a Loan, carry a value (MirBorrows.Values) or record a problem (MirBorrows.Locations records PQ-10's).
internal sealed partial class MirBorrows
{
    private readonly MirTable<(int Root, int Owner)> addressOf = new(); // A root's plain address (Owner -1) and its own-step entries.
    private readonly MirTable<(int Root, int Layer)> ownAt = new(); // By a raw Place: its layer's own-step entry, -1 for no layer.
    private readonly HashSet<(BoundType Type, BoundType? Written)> visited = new(); // A Type walk's visited parts, cleared per walk.
    private int[] positions = []; // An owner's inline positions, from the part on.
    private bool[] layerBorrowed = []; // By location: PQ-10's located Unsupported_Kd.
    private int addressCount;
    private int ownCount; // The layer Places of the view.

    // Whether a Tuple or fixed array holds a raw pointer inline, at any of its positions: each Type once per pre-pass (Layering), whose
    // visited Types all hold none, since the pre-pass stops at the first that does.
    private bool Inline(BoundType type)
    {
        for (var i = 0; i < type.Components.Count && type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray; i++)
        {
            this.Work++;
            var component = type.Components[i];
            if (component.Semantics == SemanticsKind.Raw || (this.visited.Add((component, null)) && this.Inline(component)))
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
        MirDataflow.Grow(ref this.layerBorrowed, locations);
        Array.Clear(this.layerBorrowed, 0, locations);
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
            if (row.Type is { } type && (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw } || this.Inline(cases.Type(type))))
            {
                return true;
            }
        }

        return false;
    }

    // A Place a location uses: a raw Ref's address; the layer of each prefix; PQ-10's non-raw Borrow of a layer, and a non-raw Borrow or a
    // read of an enclosing Place.
    private void Use(int location, MirPlace place, MirAccess access, MirRefKind kind, bool read)
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

        borrows &= kind != MirRefKind.Raw;
        this.layerBorrowed[location] |= (borrows && place.Projection > 0 && this.Layer(place, location) >= 0) || ((borrows || read) && this.Encloses(place));
        this.producing |= this.layerBorrowed[location]; // A located Unsupported_Kd is the replay's to record (D5).
    }

    // `P@raw`'s entry (B §5.2 item 1): through no borrow or raw Deref the root's plain address, or past an owning handle's payload its
    // owning Place's own-step entry; past one with an owning handle's payload after the last, that owner's entry past a dereference; else
    // none, the value being the borrow's parents.
    private int Formation(MirPlace place, int location)
    {
        var (count, deref, own) = (this.placeSteps.Walk(this.body, place, reborrow: false, ref this.walk), -1, -1);
        for (var i = 0; i < count; i++)
        {
            deref = this.walk[i].Class is MirStepClass.Shared or MirStepClass.Uniq or MirStepClass.Raw ? i : deref;
            own = this.walk[i].Class == MirStepClass.Own ? i : own;
        }

        if (own > deref)
        {
            return this.OwnStep(place with { Projection = this.walk[own].Prefix }, location);
        }

        return deref < 0 ? this.Address(place.Root, -1, direct: true, location, []) : -1;
    }

    // The own-step entry of a raw Place that is a `during self` layer (B §5.2 item 2), decided once per view; -1 for another Place.
    private int Layer(MirPlace layer, int location)
    {
        if (this.body.TypeOf(layer) is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw })
        {
            return -1;
        }

        if (this.ownAt.TryGetValue((layer.Root, layer.Projection), out var entry))
        {
            return entry;
        }

        entry = this.Owner(layer, out var part, out var length) is var owner and >= 0 &&
            types.RawStep(cases.Type(this.body.TypeOf(layer with { Projection = owner })!), part, this.positions.AsSpan(0, length)) == RawStep.Own
            ? this.OwnStep(layer with { Projection = owner }, location) : -1;
        this.ownCount += entry >= 0 ? 1 : 0;
        this.ownAt.Add((layer.Root, layer.Projection), entry);
        return entry;
    }

    // A layer Place's own-step entry once the pre-pass made the table (-1: none).
    private int OwnStepAt(int root, int projection) => this.ownCount > 0 && this.ownAt.TryGetValue((root, projection), out var entry) ? entry : -1;

    // The owning Place's own-step entry, made at its first formation: direct where the owning Place passes no borrow or raw Deref.
    private int OwnStep(MirPlace owner, int location)
    {
        if (this.addressOf.TryGetValue((owner.Root, owner.Projection), out var entry))
        {
            return entry;
        }

        var count = this.placeSteps.Walk(this.body, owner, reborrow: false, ref this.walk);
        var direct = true;
        for (var i = 0; i < count; i++)
        {
            direct &= this.walk[i].Class is not (MirStepClass.Shared or MirStepClass.Uniq or MirStepClass.Raw);
        }

        return this.Address(owner.Root, owner.Projection, direct, location, this.walk.AsSpan(0, count));
    }

    // An address entry: a root's plain address (owner -1) or an owning Place's own-step entry with its steps, at its first formation.
    private int Address(int root, int owner, bool direct, int location, ReadOnlySpan<MirStep> steps)
    {
        if (this.addressOf.TryGetValue((root, owner), out var entry))
        {
            return entry;
        }

        entry = this.loanCount + this.addressCount++;
        MirDataflow.Grow(ref this.loans, entry + 1);
        MirDataflow.Grow(ref this.loanSteps, this.loanStepCount + steps.Length);
        steps.CopyTo(this.loanSteps.AsSpan(this.loanStepCount));
        this.loans[entry] = new()
        {
            Root = this.Key(root),
            Place = new(root, owner >= 0 ? owner : 0),
            Group = -1,
            Start = this.loanStepCount,
            Count = steps.Length,
            Mode = MirRefKind.Raw,
            Holder = -1,
            Named = direct ? root : -1,
            Direct = direct,
            OwnStep = owner >= 0,
            Location = location,
        };
        this.loanStepCount += steps.Length;
        if (direct && root >= 0)
        {
            this.named[root] = true;
        }

        if (owner >= 0)
        {
            this.borrowed[this.Key(root)] = true; // Its root's accesses are listed for its departures (MirBorrows.OwnSteps).
            this.ownEntries++;
        }

        this.addressOf.Add((root, owner), entry);
        return entry;
    }

    // The owning Place of a Place inside a stored part (B §5.2 item 2): back over Tuple elements and fixed-array elements (the inline
    // positions) to a struct's Field (the part) or an enum Case payload's component under a Downcast (the part the Case, the first
    // position the component); -1 for any other path. The positions are left in the scratch, from the part on.
    private int Owner(MirPlace place, out int part, out int length)
    {
        (part, length) = (-1, 0);
        var projections = this.body.Projections;
        for (var id = place.Projection; id > 0; id = projections[id].Prefix)
        {
            var (row, prefix) = (projections[id], projections[id].Prefix);
            MirDataflow.Grow(ref this.positions, length + 1);
            if (row.Kind == MirProjectionKind.Field && prefix > 0 && projections[prefix].Kind == MirProjectionKind.Downcast)
            {
                (this.positions[length++], part) = (row.Arg, projections[prefix].Arg);
                this.positions.AsSpan(0, length).Reverse();
                return projections[prefix].Prefix;
            }

            var holding = cases.Type(this.body.TypeOf(place with { Projection = prefix })!);
            if (row.Kind == MirProjectionKind.Field && AdtDef.IsStruct(holding))
            {
                part = row.Arg;
                this.positions.AsSpan(0, length).Reverse();
                return prefix;
            }

            if (!(row.Kind == MirProjectionKind.Field && holding.Kind == BoundTypeKind.Tuple) &&
                !(row.Kind is MirProjectionKind.ConstIndex or MirProjectionKind.Index && holding.Kind == BoundTypeKind.FixedArray))
            {
                return -1;
            }

            this.positions[length++] = row.Kind == MirProjectionKind.Field ? row.Arg : 0;
        }

        return -1;
    }

    // Whether a Tuple or fixed-array Place inside a stored part encloses a `during self` layer inline (PQ-10, S4).
    private bool Encloses(MirPlace place)
    {
        var type = cases.Type(this.body.TypeOf(place)!);
        this.visited.Clear();
        return type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray && this.Owner(place, out var part, out var length) is var owner and >= 0 &&
            this.Encloses(cases.Type(this.body.TypeOf(place with { Projection = owner })!), part, length, type);
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
            if (component.Semantics == SemanticsKind.Raw
                ? types.RawStep(instance, part, this.positions.AsSpan(0, length + 1)) == RawStep.Own
                : this.Encloses(instance, part, length + 1, component))
            {
                return true;
            }
        }

        return false;
    }

    // The untracked rule (B′ §4.2, PQ-2): whether a set has a direct element naming a local that is no holder and granting a write there
    // (an address or an exclusive Loan; a pointer formed through a shared borrow writes nothing, SPEC 5.2.1 condition 3), through which a
    // raw pointer may place a Loan-carrying value where A5 tracks none.
    private bool Untracked(int set)
    {
        this.Work += this.sets.Count(set);
        foreach (var element in this.sets.Elements(set))
        {
            if (element >= this.loanBase && this.loans[element - this.loanBase] is { Direct: true, Named: < 0, Mode: not MirRefKind.Shared } entry &&
                entry.Root < this.body.Locals.Length)
            {
                return true;
            }
        }

        return false;
    }

    // A raw value formed (a raw Ref, a layer's read, a call result that may hold a raw pointer): Φ_E with it where it is untracked.
    private int Formed(int value) => this.Untracked(value) ? this.sets.Union(value, this.escapedSet) : value;

    // The addresses' uses of one location: its Places with their accesses, an operand's Place read by a Copy, Move or PairAcquire.
    private struct Addressing(MirBorrows owner) : IMirUses
    {
        private int location;
        private MirOperand operand;
        private bool inOperand; // Whether the Places reported are the current operand's (its index locals precede its Place).

        public void Operand(in MirOperand operand) => (this.operand, this.inOperand) = (operand, operand.Kind != MirOperandKind.Const);

        public readonly void Effect(MirEffect effects, int site)
        {
        }

        public void Place(MirPlace place, MirAccess access, MirRefKind kind)
        {
            var read = this.inOperand && this.operand.Place == place;
            this.inOperand &= !read;
            owner.Use(this.location, place, access, kind, read);
        }

        internal void At(int location) => (this.location, this.inOperand) = (location, false);
    }
}
