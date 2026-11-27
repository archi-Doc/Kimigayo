// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's pun rule (PLAN G667b, G667c on G667a; SPEC 5.2 Type-punned access, 5.2.1 condition 1, 5.3, 5.5, 5.7 Calls, 15.6).
// Responsibility: a write may reach every Place its pointer's value carries a non-shared direct Loan or address of (an entry). Where it
// writes a punnable Type holding a Scalar over a Place that may hold a raw pointer at its own address, at a Type that Place does not
// designate, a later read of that pointer may be punned and reach only storage the body does not own (5.5): the holder gets {Φ_E, Φ_W},
// unless every raw layer there states an Origin other than `static` at the Place (5.2: such a punned pointer promises what a claim of
// its Origin promises). One fact per raw access and entry decides it: the Place the entry names, the pointer value's Position (on: an
// address where the Place's leading chain starts; off: Δ or Φ_N in the value; the value's, not an entry's: one entry off marks them
// all) and the Type written there. The judgement over those three is MirBorrows.PunTypes' (the chain, the one reading of a length, the
// designation judge, the step rule); a key's layer facts are MirBorrows.PunKeys'. This file holds the rule at its three sites and
// Position's dataflow.
// Inputs: A5's sets and entries (G667a), the stored Place's steps (MirSteps), Binding's Place facts through TyCtxt (PlaceKey, Select,
// Base, Leading, OwnLayers, SameStructure, NoStore; C-189, C-211) under the case view, and SPEC 5.2's punnable set (TyCtxt.Punnable).
// Outputs: the holder's markers. A pun: {Φ_E, Φ_W}. An undecided write (the layout, a length that is no constant, or a layer with no
// stated-Origin answer decides what it covers) may be a pun: {Φ_E, Φ_W, Φ_M}, and what rests on that Φ_E carries Φ_U: a value stored
// into E through a set carrying Φ_M or Φ_U, and what a read through one gives (Rested). A gather tags what it finds in a set carrying
// Φ_U apart (MirBorrows.Candidates), and what it reaches only through such a set, the holders it names and their stores
// (MirBorrows.CheckSets' read keys, PLAN G775), so a check only such finds meet is a Limit, the located Unsupported_Kd at its subject
// (R4-M153's path), and an undecided write no check rests on records nothing. Δ marks a pointer displaced by a stride that is no grid
// row, or an address formed past a raw Deref through a component step to no part TyCtxt.Leading places at its base's address (Step,
// the one step rule: a C-layout first Field; a nonnull Option's Some payload, its Downcast and Field one step, Selected).
// The sites: a seen store writes the stored Place, typed where a raw Place from its last Deref to it is designated; a call, through
// each input its contract may store through (TyCtxt.NoStore), every raw Place within the Types its input's layers lead to
// (TyCtxt.OwnLayers at the input's root key), then, past each holder its entries name, within those the parameter's next layers lead
// to (OwnLayers at each Past key; unknown past an abstract part): the parameter's Type expression gives the Types at every depth, A5's
// state the holders. A body's read (SPEC 5.2: only a raw Place may designate a punned Place, "a read of it, also through its
// borrow"; 5.5; 15.6 Holders, example punnedRef), at each Deref of the one step walk (MirBorrows.Values' Prefix) that reads a raw
// Place: the pointer's entries of any mode, a non-holder local's included, judged at the read Type as the call's first step judges
// them, by one Place-side answer (MirBorrows.PunTypes' NoProvenance), give the read's markers (PunRead): surely where every entry lacks
// provenance surely, {Φ_E} (5.5's pointer, D8); none where every entry keeps it; else may, {Φ_E, Φ_M} unless it carries Φ_E already;
// Φ_W only from an entry not shared (R4-M170). A Deref whose referent MirBorrows.Lent resolves exactly reads the Place's own Type and
// adds nothing. The class the value decides (a support reduction the owner approved by its rule, O-521 Q1 (a)): through a referent
// Lent does not resolve (a call's result, `p + n` and `p[n]`, a reference read from a part, an aggregate or a payload, an exposed or
// part-stored local, an argument, a Cast), a read whose Places hold raw pointer bytes and other bytes, in one holder or across the
// holders the value names where one keeps provenance, or whose structure or layout states no bytes, is may: a check only a store
// through it meets is the located Unsupported_Kd.
// Invariants: each (holder state, key) is scanned once per run; a warm run allocates nothing; Φ_U is a set's, not an element's: what
// a set carrying it holds otherwise is tagged with it, E's later content included; the own-step Warning and an activation's own scan
// do not read it.
// Must not: decide a pun by Type alone where the write's overlap with a raw pointer of the Place is not established (undecided, never
// a Language error); read a size, an offset or a stride value (Layout's); read a Type's structure for the chain's step (TyCtxt.Leading).
internal sealed partial class MirBorrows
{
    private readonly MirTable<(int Set, int View)> pastScans = new(); // By state and key: its scan's start in pastPool, per run.
    private readonly List<int> pastPool = []; // Each scan: write count, child count, the (slot, PunWrite) pairs, the child pairs.
    private readonly List<int> scanChildren = []; // Scan's scratch: its (holder, key) pairs.
    private readonly List<int> punQueue = []; // PunCall's walk: (holder, key index) pairs past the first step.
    private readonly HashSet<(int Holder, int Key)> punPairs = []; // The walk's queued pairs.
    private readonly List<PlaceKey> readRows = []; // PunRead's scratch: the read Type's raw pointer rows (RawPointers).

    // What a write does at a Place.
    private enum PunWrite : byte
    {
        Typed, // The Place designates the written Type, or its pun keeps every stated Origin: no pun.
        Pun, // A pun over a raw pointer the Place may hold.
        Undecided, // A write the layout places, or a layer with no stated-Origin answer: a pun or none (Φ_M).
    }

    // A write's verdict at a Place of these facts, `write` what the Place's Type makes of it (SPEC 5.2, 5.7): none where every raw layer
    // there states an Origin other than `static` (Own, Shared or Exclusive: a pun or none, the pointer promises that Origin's storage);
    // undecided where one has no answer (Unsupported); else the Type's. A write the call may make at any raw layer it reaches (OwnWrite,
    // UnknownWrite) takes this verdict at partly stated layers too: a pointer it leaves at one stating no Origin reaches storage the body
    // does not own, and every value read from any part of the holder carries it (SPEC 15.6 Holders; O-230 Q1 (a)).
    private static PunWrite PunAt(PunWrite write, in PunKey facts)
    {
        if (write == PunWrite.Typed || facts.Stated)
        {
            return PunWrite.Typed;
        }

        return facts.Unsupported ? PunWrite.Undecided : write;
    }

    // A placed write's verdict (StoreWrite, CallWrite: the designation judge places the written Type at the pointer's Position, SPEC 5.2,
    // IMPL 21.1): PunAt's, but undecided where the pun lands among partly stated layers (PunKey.PartlyStated). The layout decides whether it
    // covers a stated layer, whose pointer promises its Origin's storage, or another one (R4-M198's undecided class: the located
    // Unsupported_Kd where a check rests on it, never a Language record).
    private static PunWrite PlacedAt(PunWrite write, in PunKey facts)
        => write == PunWrite.Pun && facts.PartlyStated ? PunWrite.Undecided : PunAt(write, facts);

    // The worse of two writes at one Place: a pun, else an undecided write.
    private static PunWrite Worse(PunWrite a, PunWrite b)
    {
        if (a == PunWrite.Pun || b == PunWrite.Pun)
        {
            return PunWrite.Pun;
        }

        return a == PunWrite.Typed ? b : a;
    }

    // The pun rule at a store to `place` through the pointer set `pointer`, what its last Deref, step `last` of the Place's `count` steps
    // in the walk buffer, goes through (MirBorrows.Reach's WriteTargets). A written Place takes rule W (MirCases.KnownTypeOf, as Store's
    // receiving part): one of no Type, a Field step whose slot failed (PLAN G1184), has no bytes to pun.
    private void Punning(int pointer, MirPlace place, int count, int last)
    {
        if (cases.KnownTypeOf(place) is not { } written || this.Scalar(written) == Extent.None)
        {
            return;
        }

        var on = !this.OffGrid(pointer);
        this.Work += this.sets.Count(pointer);
        foreach (var (element, _) in this.sets.Elements(pointer))
        {
            if (this.PunEntry(element) is >= 0 and var entry)
            {
                this.Pun(this.loans[entry].Named, this.StoreWrite(entry, place, last, count, on));
            }
        }
    }

    // The pun rule at a call through one input at `position`, of case Type `input` (MirCases.TypeOf; every input the contract may store
    // through, the callee operand included, whatever its retention rows: a scalar store keeps no Loan): its first-step entries against the
    // write Types of its root key, then, past them, each holder an entry names with the parameter's Past keys of that key: the holder's
    // state's entries against each Past key's write Types (unknown where an abstract part is there), each (holder, key) once per walk.
    // Nothing for a value with no entry.
    private void PunCall(ulong[] state, in MirTerminator call, int position, BoundType input, int value)
    {
        if (!this.AnyEntry(value) || this.NoStore(call, position))
        {
            return;
        }

        var output = this.OutputAt(position);
        var index = this.Key(this.InputKey(call, position, input), this.Fold(output)); // The fold Keep's walk of the output reads.
        if (output is null)
        {
            this.CarriedInput(state, value, index); // Route 1 where the plan has no output (MirBorrows.Reach).
        }

        if (!this.Below(index))
        {
            return;
        }

        this.PunRun();
        this.punPairs.Clear();
        this.punQueue.Clear();
        this.Children(this.Scan(value, index));
        for (var i = 0; i < this.punQueue.Count; i += 2)
        {
            var held = state[this.punQueue[i]];
            this.Children(this.Scan(Placed(held), this.punQueue[i + 1]));
            this.Children(this.Scan(Shared(held), this.punQueue[i + 1]));
        }
    }

    // The pun rule at a body's read through the walk buffer's Deref step `i` from `root` (MirBorrows.Values' Prefix), `pointer` the value
    // it goes through and `read` its layer: a raw Deref reads at its pointee; a borrow Deref at its referent where punnable (through a
    // borrow layer only a punnable referent puns, as MirBorrows.Reach's Punned reads a borrow input). Where that adds a marker, a Deref
    // whose referent MirBorrows.Lent resolves exactly (LentExactly: a Place the body forms by Type-keeping steps alone) reads it at its
    // own Type (SPEC 5.2: a typed Place), so nothing new; an Index step (`p[n]` reads at `p + n`) and every other referent keep it.
    private int PunRead(int root, int i, int pointer, int read)
    {
        var prefix = root < 0 ? null : cases.TypeOf(new MirPlace(root, this.walk[i].Prefix));
        var judged = this.walk[i].Class == MirStepClass.Raw || prefix?.SafeReferent is not { } referent
            ? prefix is { IsPointer: true, Components: [var to] } ? this.PunRead(cases.Type(to), pointer, read) : read
            : types.Punnable(cases.Type(referent)) ? this.PunRead(cases.Type(referent), pointer, read) : read;
        return judged == read || this.walk[i].Kind != MirProjectionKind.Deref || !this.LentExactly(new MirPlace(root, this.walk[i].Prefix))
            ? judged : read;
    }

    // The read site's judgement at the read Type `type` (SPEC 5.2, 5.5, 15.6 Holders): where it has a raw pointer raw Place
    // (RawPointers), each Place a direct entry of the pointer names, of any mode and a non-holder local's included (ReadEntry), answers
    // None where the Type designates it at the value's Position, else NoProvenance. The step answers surely where every entry lacks
    // provenance surely: the read also carries {Φ_E}, the pointer 5.5 restores from an integer (D8); None where none lacks it; else may,
    // {Φ_E, Φ_M}, the write site's undecided answer (the value cannot tell a typed read from a punned one: a check only such finds meet
    // is the located Unsupported_Kd, the class above), unless the read carries Φ_E already; Φ_W only from an entry not shared (R4-M170).
    private int PunRead(BoundType type, int pointer, int read)
    {
        var (on, open, positive, kept, writable) = (!this.OffGrid(pointer), false, false, false, false);
        this.Work += this.sets.Count(pointer);
        foreach (var (element, _) in this.sets.Elements(pointer))
        {
            if (this.ReadEntry(element) is >= 0 and var entry && cases.TypeOf(this.loans[entry].Place) is var place)
            {
                var lacks = this.Designates(place, type, on) ? Extent.None : this.NoProvenance(place);
                (open, positive, kept) = (open || lacks == Extent.Open, positive || lacks == Extent.Positive, kept || lacks == Extent.None);
                writable |= lacks != Extent.None && this.loans[entry].Mode != MirRefKind.Shared;
            }
        }

        var answer = positive && !open && !kept ? Extent.Positive : open || positive ? Extent.Open : Extent.None;
        if (answer == Extent.None || (answer == Extent.Open && this.sets.Contains(read, Escaped)))
        {
            return read;
        }

        this.readRows.Clear();
        this.RawPointers(PlaceKey.Root(type), this.readRows);
        if (this.readRows.Count == 0)
        {
            return read;
        }

        var add = answer == Extent.Positive ? this.escapedSet : this.maybeSet;
        return this.sets.Union(read, writable ? add : this.sets.Remove(add, this.writableSet));
    }

    // The call's output through the input at `position` (the plan's Writes, as Keep reads them), null where none.
    private OutputRetention? OutputAt(int position)
    {
        foreach (var written in this.planned is null ? default : this.planned.Writes)
        {
            if (this.slotPositions[written.Output] == position)
            {
                return written;
            }
        }

        return null;
    }

    // SPEC 5.2 Placement, 5.6: whether the call's contract stores no value through the input at `position` (TyCtxt.NoStore by slot; a
    // contract numbers at most the call's operands and its callee operand).
    private bool NoStore(in MirTerminator call, int position)
    {
        var receiver = -2;
        for (var slot = 0; slot <= call.Count; slot++)
        {
            if (TyCtxt.NoStore(call.Callee.Symbol, slot))
            {
                receiver = receiver == -2 ? this.Receiver(call) : receiver;
                if (Position(call.Callee, receiver, slot) == position)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // A write's verdict at a Place of the holder `slot`: a pun gives the holder {Φ_E, Φ_W} (SPEC 5.5); an undecided write, which may be
    // one, Φ_M besides.
    private void Pun(int slot, PunWrite write)
    {
        if (write != PunWrite.Typed)
        {
            this.AddSlot(slot, write == PunWrite.Pun ? this.escapedSet : this.maybeSet, 0, JoinedThrough, issue: false);
        }
    }

    // Whether a set's Φ_E, or its content, rests on an undecided write (Φ_M, Φ_U).
    private bool Resting(int set) => this.sets.Contains(set, Maybe) || this.sets.Contains(set, Undecided);

    // A value stored into E, or read, through the set `through`: with Φ_U where that set rests on an undecided write.
    private int Rested(int value, int through) => this.Resting(through) ? this.sets.Union(value, this.undecidedSet) : value;

    // A scan's effect on the walk: its written slots get their writes' sets (Pun), its (holder, key) pairs not yet queued are queued.
    private void Children(int scan)
    {
        var pairs = scan + 2 + (2 * this.pastPool[scan]);
        for (var i = scan + 2; i < pairs; i += 2)
        {
            this.Pun(this.pastPool[i], (PunWrite)this.pastPool[i + 1]);
        }

        for (var i = pairs; i < pairs + (2 * this.pastPool[scan + 1]); i += 2)
        {
            if (this.punPairs.Add((this.pastPool[i], this.pastPool[i + 1])))
            {
                this.punQueue.Add(this.pastPool[i]);
                this.punQueue.Add(this.pastPool[i + 1]);
            }
        }
    }

    // A set's scan (memoized per run): the set is a value the parameter's key `view` (an index) types: its entries' holders to pun
    // against that key's write Types; and the (holder, key) pairs they lead to: each Past key of `view` with a writable layer or an
    // abstract part (the callee may read a pointer there), and `view` itself where its chain is unknown past an abstract part.
    private int Scan(int set, int view)
    {
        if (this.pastScans.TryGetValue((set, view), out var start))
        {
            return start;
        }

        start = this.pastPool.Count;
        this.pastPool.Add(0);
        this.pastPool.Add(0);
        this.Work += this.sets.Count(set);
        this.scanChildren.Clear();
        var on = !this.OffGrid(set);
        var facts = this.punKeys[view];
        foreach (var (element, _) in this.sets.Elements(set))
        {
            var entry = this.PunEntry(element);
            if (entry < 0)
            {
                continue;
            }

            var write = Worse(this.CallWrite(entry, this.Writes(view), on), facts.FirstUnknown ? this.UnknownWrite(entry) : PunWrite.Typed);
            if (write != PunWrite.Typed)
            {
                this.pastPool.Add(this.loans[entry].Named);
                this.pastPool.Add((int)write);
                this.pastPool[start]++;
            }

            // Past the entry's Place: the keys the parameter's layers lead to (SPEC 5.7 Calls), unknown past an abstract part.
            if (facts.FirstUnknown)
            {
                this.scanChildren.Add(this.loans[entry].Named);
                this.scanChildren.Add(view);
            }

            for (var p = facts.PastStart; p < facts.PastStart + facts.PastCount; p++)
            {
                var past = this.Key(this.punPastKeys[p], facts.Fold);
                if (this.punKeys[past] is { FirstCount: > 0 } or { FirstUnknown: true })
                {
                    this.scanChildren.Add(this.loans[entry].Named);
                    this.scanChildren.Add(past);
                }
            }
        }

        this.pastPool.AddRange(this.scanChildren);
        this.pastPool[start + 1] = this.scanChildren.Count / 2;
        this.pastScans.Add((set, view), start);
        return start;
    }

    // The index of the direct Loan or address an element names a Place by, of any mode (not an own-step entry, never E), else -1; a
    // local of a Type that holds no Loan has no holder slot (Named -1, MirBorrows.Holders), but a read judges its Place's Type.
    private int ReadEntry(int element)
    {
        var index = element - this.loanBase;
        return index >= 0 && index < this.loanCount + this.addressCount &&
            this.loans[index] is { Direct: true, Named: not Escaped, OwnStep: false } ? index : -1;
    }

    // That entry where it names a holder and is not shared (a write's: SPEC 15.6 a write may reach every Place its pointer's value
    // carries a non-shared direct Loan or address of; 15.6.4 item 1), else -1.
    private int PunEntry(int element) =>
        this.ReadEntry(element) is >= 0 and var index && this.loans[index] is { Named: >= 0, Mode: not MirRefKind.Shared } ? index : -1;

    private bool AnyEntry(int set)
    {
        foreach (var (element, _) in this.sets.Elements(set))
        {
            if (this.PunEntry(element) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    // Whether a pointer's value may be off an element grid: Δ, or bytes A5 did not see formed (Φ_N).
    private bool OffGrid(int set) => this.sets.Contains(set, Offgrid) || this.sets.Contains(set, Unformed);

    // What a store to `place` does at the Place P entry `entry` names (SPEC 5.2), the walk buffer holding the stored Place's steps and
    // `on` the pointer's Position: the raw Places from its last Deref to the stored Place, each at its Position; typed where one of them
    // is designated (its Fields and elements are then P's own typed parts); else the stored Place's own Type at its Position.
    private PunWrite StoreWrite(int entry, MirPlace place, int last, int count, bool on)
    {
        if (this.Holder(entry, out var facts) is not { } held)
        {
            return PunWrite.Typed;
        }

        for (var i = last; ; i++)
        {
            var at = cases.TypeOf(place with { Projection = i + 1 < count ? this.walk[i + 1].Prefix : place.Projection });
            if (on && this.Leads(held, at, grid: false))
            {
                return PunWrite.Typed;
            }

            if (i == count - 1)
            {
                return PlacedAt(this.Designated(held, at, on), facts);
            }

            on = on && (this.Selected(i + 1, count) is not { } field || this.Step(held, at, field));
        }
    }

    // What a call writing at the Types `writes` does at the Place entry `entry` names (SPEC 5.7 Calls, 5.2), `on` the pointer's Position:
    // every raw Place within each written Type (Within), as the Place's layers keep a pun.
    private PunWrite CallWrite(int entry, ReadOnlySpan<BoundType> writes, bool on)
    {
        if (this.Holder(entry, out var facts) is not { } place)
        {
            return PunWrite.Typed;
        }

        var result = PunWrite.Typed;
        foreach (var write in writes)
        {
            result = Worse(result, this.Within(place, write, on));
        }

        return PlacedAt(result, facts);
    }

    // What a call writing at Types an abstract part leaves unknown does at the Place entry `entry` names: a pun through a raw address
    // where the Place surely holds a raw pointer's byte (PlaceBytes: Pointer Positive), undecided where it only may (Open: under a length that is
    // no constant, or in a part the Type's structure does not state), as the Place's layers keep it; none through a safe borrow, which
    // designates its Place at its own Type.
    private PunWrite UnknownWrite(int entry)
    {
        if (this.loans[entry].Mode != MirRefKind.Raw || this.Holder(entry, out var facts) is not { } place)
        {
            return PunWrite.Typed;
        }

        return PunAt(this.PlaceBytes(place).Pointer == Extent.Positive ? PunWrite.Pun : PunWrite.Undecided, facts);
    }

    // What a call that may leave its own pointer in the Place entry `entry` names does there (SPEC 15.6.4: a pointer the call may write at
    // a raw layer without an Origin, its own included, may address storage the body does not own; 5.7 Calls: one at a layer stating an
    // Origin is judged by that Origin, R4-M183): a pun where the Place may hold a raw pointer, at any raw layer it reaches, as the Place's
    // layers keep it (PunAt).
    private PunWrite OwnWrite(int entry) => this.Holder(entry, out var facts) is null ? PunWrite.Typed : PunAt(PunWrite.Pun, facts);

    // The Type of the Place entry `entry` names where a write there may pun a raw pointer, else null: a raw layer at the Place's own
    // address (the key's facts: Binding's layers there) in a Place that holds a raw pointer's byte (PlaceBytes; SPEC 4.1, 5.2: an array
    // of no element holds none; a write at its address lies outside it, undefined when it runs and no compile-time error, SPEC 1.3).
    private BoundType? Holder(int entry, out PunKey facts)
    {
        facts = this.punKeys[this.EntryKey(entry)];
        return facts.RawInline && cases.TypeOf(this.loans[entry].Place) is var place && this.PlaceBytes(place).Pointer != Extent.None ? place : null;
    }

    // A displacement of a raw pointer (SPEC 5.3) by its pointee's stride.
    private int Displaced(int value, BoundType? pointer)
        => pointer is { IsPointer: true } ? this.Stepped(value, pointer, -1) : value;

    // A pointer's value past a step (Step) of Type `of`: with Δ where a Place an entry names does not keep its Position. The Position is
    // the value's: one entry's Place off marks every entry.
    private int Stepped(int value, BoundType of, int field)
    {
        if (value == 0 || this.sets.Contains(value, Offgrid))
        {
            return value;
        }

        this.Work += this.sets.Count(value);
        foreach (var (element, _) in this.sets.Elements(value))
        {
            if (this.PunEntry(element) is >= 0 and var entry && !this.Step(cases.TypeOf(this.loans[entry].Place), of, field))
            {
                return this.sets.Union(value, this.offgridSet);
            }
        }

        return value;
    }

    // An address or borrow formed past a raw Deref (SPEC 5.2, 5.3, IMPL 21.1), its steps in the walk buffer: each component step past
    // the Deref (an element: Index or ConstIndex; a Field, Base or Downcast) moves its pointer's value (Stepped), a Case payload's
    // component with its Downcast (Selected). The five kinds are named: a new projection kind is no component step until it is listed
    // here.
    private int Interior(int value, MirPlace place, int count)
    {
        var deref = -1;
        for (var i = 0; i < count; i++)
        {
            deref = this.walk[i].Class == MirStepClass.Raw ? i : deref;
        }

        for (var i = deref + 1; deref >= 0 && i < count; i++)
        {
            var kind = this.walk[i].Kind;
            var element = kind is MirProjectionKind.Index or MirProjectionKind.ConstIndex;
            if ((element || kind is MirProjectionKind.Field or MirProjectionKind.Base or MirProjectionKind.Downcast) &&
                this.Selected(i, count) is { } field)
            {
                value = this.Stepped(value, cases.TypeOf(place with { Projection = this.walk[i].Prefix }), field);
            }
        }

        return value;
    }

    // The one reading of the walk buffer's step `i` (of `count`) for the step rule (Step), the Field it selects from the Type it applies
    // to, else -1: an enum Case payload's component is one step with its Downcast, judged at the Downcast from the enum (TyCtxt.Leading's
    // nonnull Option row: the Some payload, MIR's Downcast and Field), so that Field is no step of its own (null), and a Downcast with no
    // Field after it selects none.
    private int? Selected(int i, int count) => this.walk[i].Kind switch
    {
        MirProjectionKind.Downcast => i + 1 < count && this.walk[i + 1].Kind == MirProjectionKind.Field ? this.walk[i + 1].Arg : -1,
        MirProjectionKind.Field when i > 0 && this.walk[i - 1].Kind == MirProjectionKind.Downcast => null,
        MirProjectionKind.Field => this.walk[i].Arg,
        _ => -1,
    };
}
