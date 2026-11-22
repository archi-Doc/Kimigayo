// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's Loans of the view (A3 4's issued, E3, E13 (4)): one per non-raw Ref and Reborrowing PairAcquire past no shared borrow Deref, by
// location, each with its Place's steps, those up to its receiver an access is compared with (ReceiverSteps: before an Array element,
// the Array its index call borrows, SPEC 4.7.1) and whether it is a raw Place borrow's (Anchored: its Place passes a raw Deref, so its
// fresh anchor's, which no access reaches, R4-M156), in group order (MirBorrows.Groups); whether the view has an input of a Loan-stratum
// producer (D5, R4-M98): a Loan, a guard candidate, whose guard-local Origin a check follows without a Loan (E7), a Drop of a mutable
// static (R3) or a write rooted at a default template's PreparedInput (R26-access); for the effect rule (R4-M92) the view's effect roots,
// the mutable statics its Loans are on, each static's A4 Field number, whether the view drops a mutable static and, by block, the
// statics its Direct Loans borrow (MirBorrows.Passes' entries); whether the view has a raw Ref, whose address MirBorrows.Addresses
// forms; and by local, whether a direct Loan or address names it (a flow holder), whether it is an exposed holder (Exposed) and its
// stores, the one record of them (Refer), which gives its reference the Places it refers to (Referring: wherever it is read, Referred),
// the borrow it holds (MirBorrows.Lent's Lender) and the Place a Ref lent into it (LentInto).
internal sealed partial class MirBorrows
{
    private const byte NamedHolder = 1; // named: a direct Loan or address names the local, so it is a flow holder (MirBorrows.Holders).
    private const byte ExposedHolder = 2; // named: an exposed holder (Exposed).
    private const byte ExclusiveReference = 4; // named: a referring store gives the local an exclusive reference (Referred).
    private const byte OtherStore = 8; // named: a store into the local's whole Place is no referring one (Referring; MirBorrows.Lent's Lender).
    private const byte PartStore = 16; // named: a store into a part of the local, so its stores are not followed (Referring, Lender).

    private readonly List<int> effectRoots = []; // The root keys of the mutable statics the view's Loans are on, each once.
    private byte[] named = []; // By local: NamedHolder, ExposedHolder, ExclusiveReference, OtherStore and PartStore.
    private bool[] borrowed = []; // By root key (Key): whether a Loan's Place has that root.
    private int[] referFirst = []; // By local: its last whole store in referStores, -1 for none (Referred).
    private int[] referStores = []; // The whole stores, two entries each: the location, and its local's store before it.
    private int referCount; // The whole stores.
    private Loan[] loans = [];
    private MirStep[] loanSteps = []; // The Loans' Places, each a run.
    private int[] loanAt = []; // By location (Location): the Loan its Ref or Reborrowing PairAcquire issues, or -1.
    private int loanCount;
    private int loanStepCount;
    private bool producing; // Whether the view has an input of a Loan-stratum producer, which each producer's scan below sets (D5).
    private bool rawRefs; // Whether the view has a raw Ref, whose address MirBorrows.Addresses forms.
    private bool dropsStatic; // Whether the view has a Drop of a mutable static (R3).
    private int[] fields = []; // By static: A4's number of its Field (MirEffects.Field), -1 for none.

    // The Reborrow a statement's PairAcquire makes under the view (A3 E13 (4)): the borrow its Use's marker names (Uniq, or TwoPhaseUniq for
    // an invocation's input), or Raw where it Copies or the statement is none.
    private MirRefKind Reborrowing(in MirStatement statement)
        => statement is { Kind: MirStatementKind.Assign, Rvalue: { Kind: MirRvalueKind.Use, Op: not 0 } use } && cases.Reborrows(this.body.Operands[use.Start])
            ? (MirRefKind)use.Op : MirRefKind.Raw;

    // Whether a local is an exposed holder (PLAN G598; SPEC 5.2, 5.5): a non-shared direct Loan or address of the body names it, or an
    // Opaque site touched it, so a pointer the body formed may write its bytes at any later point, also where A5 reads no store (a
    // callee's scalar store, a pun); no other pointer reaches a local. A shared Loan gives no write authority (SPEC 5.2.1 condition 3).
    private bool Exposed(int local) => (this.named[local] & ExposedHolder) != 0;

    // Whether a local's reference refers only to Places its stores name, and the last of those stores (the one before each in
    // referStores): every store of the body into a Place rooted at the local is a referring one, a non-raw Ref of a Place, a Reborrow
    // of another local's reference or a Move or Copy of another local's whole value (each referring to what that local refers to), into
    // its whole Place, and no pointer the body formed may write the local (Exposed). An argument's own value is its caller's, which
    // refers to no local of the body. MirBorrows.Holders' Written follows every one.
    private bool Referring(int local, out int store)
    {
        store = this.referFirst[local];
        return (this.named[local] & (ExposedHolder | OtherStore | PartStore)) == 0;
    }

    // The Place the last non-raw Ref into a local's whole Place lent into it, or the local itself: a Call's input temp's target and its
    // destroying reservations' Type read it flow-insensitively (G136, R5-M72: the builder writes such a temp once, MirValidator's Lent).
    // It reads the store record, as Lender does: Lender gives the one borrow of a local's one store, followed through Copies, for a
    // position; this the Place of the last Ref, whatever the other stores, for a temp written once.
    private MirPlace LentInto(int local)
    {
        for (var store = this.referFirst[local]; store >= 0; store = this.referStores[(2 * store) + 1])
        {
            var location = this.referStores[2 * store];
            if (location < this.body.Statements.Length && this.body.Statements[location].Rvalue is var assigned &&
                (assigned.Kind == MirRvalueKind.Adapt ? cases.Adapt(assigned, out _) : assigned) is { Kind: MirRvalueKind.Ref } rvalue &&
                (MirRefKind)rvalue.Op != MirRefKind.Raw)
            {
                return rvalue.Place;
            }
        }

        return new(local);
    }

    // Whether a local's reference refers, wherever the local is read, only to Places its stores name (Referring), and the last store:
    // every one shared, and the local no argument. A read through the local then reads one of those Places: a shared Loan lets nothing
    // write what it reaches while the reference is used (SPEC 5.2.1 condition 3). MirBorrows.Values' Sources follows every one.
    private bool Referred(int local, out int store)
        => this.Referring(local, out store) && store >= 0 && (this.named[local] & ExclusiveReference) == 0 &&
            this.body.Locals[local].Kind != MirLocalKind.Arg;

    // The Loans of the view (A3 4's issued, E3): a non-raw Ref of a Place that passes no shared borrow Deref and a PairAcquire the view makes a
    // Reborrow, of its Place's referent in its marker's mode (A3 E13 (4)), by location; each named by its whole Place, then ordered by group.
    private void Loans()
    {
        var (locals, locations) = (this.body.Locals.Length, this.body.Statements.Length + this.body.Blocks.Length);
        var keys = locals + this.body.Statics.Length;
        MirDataflow.Grow(ref this.loanAt, locations);
        MirDataflow.Grow(ref this.borrowed, keys);
        MirDataflow.Grow(ref this.named, locals);
        MirDataflow.Grow(ref this.referFirst, locals);
        Array.Fill(this.loanAt, -1, 0, locations);
        Array.Clear(this.borrowed, 0, keys);
        Array.Clear(this.named, 0, locals);
        Array.Fill(this.referFirst, -1, 0, locals);
        this.Unlend();
        foreach (var use in this.body.OpaqueUses)
        {
            if (use.Root >= 0)
            {
                this.named[use.Root] |= ExposedHolder; // The replaced form may have written it (R4-M70).
            }
        }

        MirDataflow.Grow(ref this.fields, this.body.Statics.Length);
        MirDataflow.Grow(ref this.passBit, this.body.Statics.Length);
        MirDataflow.Grow(ref this.passStarts, this.body.Blocks.Length + 1);
        for (var i = 0; i < this.body.Statics.Length; i++)
        {
            (this.fields[i], this.passBit[i]) = (effects.Field(this.body.Statics[i].Symbol), -1);
        }

        this.producing = false;
        this.effectRoots.Clear();
        for (var i = 0; i < locals; i++)
        {
            this.producing |= this.MarkerOf(i) == Marker.Guard;
        }

        (this.loanCount, this.loanStepCount, this.rawRefs, this.referCount, this.dropsStatic) = (0, 0, false, 0, false);
        (this.passCount, this.passBitCount) = (0, 0);
        for (var block = 0; block < this.body.Blocks.Length; block++)
        {
            ref readonly var data = ref this.body.Blocks[block];
            this.passStarts[block] = this.passBitCount;
            for (var i = 0; i < data.Count; i++)
            {
                ref readonly var statement = ref this.body.Statements[data.Start + i];
                if (statement.Kind != MirStatementKind.Assign)
                {
                    continue;
                }

                this.producing |= MirSteps.Prepared(this.body, statement.Place.Root);
                var rvalue = statement.Rvalue.Kind == MirRvalueKind.Adapt ? cases.Adapt(statement.Rvalue, out _) : statement.Rvalue;
                this.rawRefs |= rvalue is { Kind: MirRvalueKind.Ref or MirRvalueKind.Storage, Op: (byte)MirRefKind.Raw };
                byte store; // What the store gives its local (Refer): a referring one's exclusive bit, or OtherStore.
                if (rvalue.Kind == MirRvalueKind.Ref && (MirRefKind)rvalue.Op != MirRefKind.Raw)
                {
                    this.loanAt[data.Start + i] = this.Issue(rvalue.Place, reborrow: false, (MirRefKind)rvalue.Op, statement.Place.Root, data.Start + i);
                    store = (MirRefKind)rvalue.Op != MirRefKind.Shared ? ExclusiveReference : (byte)0;
                }
                else if (this.Reborrowing(statement) is var kind and not MirRefKind.Raw)
                {
                    var place = this.body.Operands[rvalue.Start].Place;
                    this.loanAt[data.Start + i] = this.Issue(place, reborrow: true, kind, statement.Place.Root, data.Start + i);
                    store = place is { Root: >= 0, Projection: 0 } ? ExclusiveReference : OtherStore;
                }
                else
                {
                    store = rvalue.Kind == MirRvalueKind.Use &&
                        this.body.Operands[rvalue.Start] is { Kind: MirOperandKind.Copy or MirOperandKind.Move, Place: { Root: >= 0, Projection: 0 } } ? (byte)0 : OtherStore;
                }

                this.Refer(statement.Place, data.Start + i, store);
            }

            var terminator = data.Terminator;
            if (terminator.Kind == MirTerminatorKind.Call)
            {
                this.Refer(terminator.Place, this.Location(block, data.Count), OtherStore);
            }

            this.dropsStatic |= this.DroppedStatic(terminator) >= 0;
            this.producing |= terminator.Kind == MirTerminatorKind.Call && MirSteps.Prepared(this.body, terminator.Place.Root);
        }

        this.passStarts[this.body.Blocks.Length] = this.passBitCount;
        this.producing |= this.loanCount > 0 || this.dropsStatic;

        this.Order(moves.Paths.Length + this.body.Statics.Length);
        this.Renumber(this.loanAt.AsSpan(0, locations));
        this.Work += locations;
    }

    // One more store into a Place rooted at a local, by its location (Referring, MirBorrows.Lent's Lender, LentInto): into the local's whole
    // Place, listed with what it gives the local (ExclusiveReference for a referring one that gives an exclusive reference, OtherStore for no
    // referring one); into a part, PartStore, after which the local's stores are not followed; a static has none to count.
    private void Refer(MirPlace place, int location, byte store)
    {
        if (place.Root < 0)
        {
            return;
        }

        if (place.Projection != 0)
        {
            this.named[place.Root] |= PartStore;
        }
        else
        {
            this.referCount = Linked(ref this.referStores, this.referFirst, (place.Root, location), this.referCount);
            this.named[place.Root] |= store;
        }
    }

    // A Loan of a Place issued into a local (-1: none) at a location, or -1 past a shared borrow Deref (E3); compared on its steps up to
    // its receiver, before its first ArrayElement step, else on all of them (Loan.ReceiverSteps).
    private int Issue(MirPlace place, bool reborrow, MirRefKind mode, int into, int location)
    {
        var count = this.placeSteps.Walk(place, reborrow, ref this.walk);
        var (direct, anchored, receiver) = (true, false, count);
        for (var i = 0; i < count; i++)
        {
            if (this.walk[i].Class == MirStepClass.Shared)
            {
                return -1;
            }

            direct &= this.walk[i].Class is not (MirStepClass.Uniq or MirStepClass.Raw);
            anchored |= this.walk[i].Class == MirStepClass.Raw;
            receiver = this.walk[i].Class == MirStepClass.ArrayElement ? Math.Min(receiver, i) : receiver;
        }

        var (key, root) = (this.Key(place.Root), direct ? place.Root : -1);
        if (place.IsStatic && !this.borrowed[key] && this.body.Statics[~place.Root].Mutable)
        {
            this.effectRoots.Add(key);
        }

        this.borrowed[key] = true;
        if (place.IsStatic && direct)
        {
            this.PassBit(~place.Root); // A Borrow of the static's storage, a part of it or the payload it owns (MirBorrows.Passes).
        }

        if (root >= 0)
        {
            this.named[root] |= (byte)(mode == MirRefKind.Shared ? NamedHolder : NamedHolder | ExposedHolder);
        }

        var loan = new Loan
        {
            Root = key,
            Place = place,
            Group = place.IsStatic ? moves.Paths.Length + ~place.Root : moves.Find(place, out _),
            Mode = mode,
            Holder = into,
            Named = root,
            Direct = direct,
            Anchored = anchored,
            ReceiverSteps = receiver,
            Location = location,
        };
        return this.Record(this.loanCount++, loan, count);
    }

    // Records a Loan, or an address entry (MirBorrows.Addresses), at its index, with the step walk's first `count` steps as its Place's.
    private int Record(int index, in Loan loan, int count)
    {
        MirDataflow.Grow(ref this.loans, index + 1);
        MirDataflow.Grow(ref this.loanSteps, this.loanStepCount + count);
        this.walk.AsSpan(0, count).CopyTo(this.loanSteps.AsSpan(this.loanStepCount));
        this.loans[index] = loan with { Start = this.loanStepCount, Count = count };
        this.loanStepCount += count;
        return index;
    }

    /// <summary>A Loan (A3 3).</summary>
    private struct Loan
    {
        /// <summary>Gets or sets its Place's root key.</summary>
        internal int Root { get; set; }

        /// <summary>Gets or sets its Place, whose case Type names the storage it lends (a call's Reached entry, MirBorrows.Calls).</summary>
        internal MirPlace Place { get; set; }

        /// <summary>Gets or sets its Place's group: the Move Path of its longest inline prefix (MirMoveData), or for a static's Place the
        /// number of paths and the static's index.</summary>
        internal int Group { get; set; }

        /// <summary>Gets or sets the first of its Place's steps.</summary>
        internal int Start { get; set; }

        /// <summary>Gets or sets the number of its Place's steps.</summary>
        internal int Count { get; set; }

        /// <summary>Gets or sets the number of its Place's steps up to its receiver, which an access is compared with (the conflicts
        /// relation, MirBorrows.Checks' Hits and its trie form Through): those before its first ArrayElement step, the Array whose
        /// element's index call borrows it whole, so the Loan is met as that call's receiver Loan (SPEC 4.7.1, 15.6.3), else Count, also
        /// an address entry's.</summary>
        internal int ReceiverSteps { get; set; }

        /// <summary>Gets or sets its mode.</summary>
        internal MirRefKind Mode { get; set; }

        /// <summary>Gets or sets the slot it was issued into, a two-phase Loan's reservation holder (-1: none).</summary>
        internal int Holder { get; set; }

        /// <summary>Gets or sets the slot a direct Loan names, E's for a static's (G136 (i), R4-M92), -1 for none.</summary>
        internal int Named { get; set; }

        /// <summary>Gets or sets a value indicating whether it is direct: its Place passes no borrow or raw Deref.</summary>
        internal bool Direct { get; set; }

        /// <summary>Gets or sets a value indicating whether it is a raw Place borrow's: its Place passes a raw Deref, so it is its fresh
        /// anchor's, which no access reaches (SPEC 5.2.2, 15.6.2; DECISIONS R4-M156).</summary>
        internal bool Anchored { get; set; }

        /// <summary>Gets or sets a value indicating whether it is a destroying reservation: its call destroys its target in place.</summary>
        internal bool Destroying { get; set; }

        /// <summary>Gets or sets a value indicating whether it is an address's own-step entry (MirBorrows.Addresses): its steps are its
        /// owning Place's.</summary>
        internal bool OwnStep { get; set; }

        /// <summary>Gets or sets its issuing statement's or terminator's location (Location's index), whose subject is its anchor and whose
        /// scope its issue's (D14).</summary>
        internal int Location { get; set; }
    }
}
