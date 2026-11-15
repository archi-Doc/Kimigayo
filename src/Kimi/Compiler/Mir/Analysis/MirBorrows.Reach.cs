// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;

namespace Kimi.Compiler.Mir;

// A5's reach on a state (A3 5's content and refs, E16's W, DECISIONS R4-M73, R4-M116, R4-M147): the holders a set refers to (those its
// direct Loans and addresses name, and E where it carries Φ_E or no direct one), its content, the holders a call's store through it writes
// (W, whose first step alone is Referents), and the guard, MirTypes.HoldsLoans' MirWrites of the Type a holder's state is read at; its
// chain (SPEC 15.6.4's "what any chain of reads through it carries", a Chain row's measure), the least fixed point of a read from it,
// each holder read once per closure. W is one walk over state parts: each part of a reached holder's state is read once per walk at
// its reading, the guard of the holder's own root Type (Read; Through's reading covers Named's; MirBorrows.States' mark), each holder
// taking the Reached entry of the Type of the Place the reaching Loan names (DECISIONS R4-M154, Revision 8), except where the walk reads
// a holder at a reading key (Keyed, ReadAt, ReadPart; SPEC 15.6 Holders: every element with every layer of the key the holder was reached
// at): there each element is typed, punned or read nothing at each layer, and a holder it writes takes the record of that layer's
// storage and is read on at the key past it, at that Type's guard; a call input's key is the called function's parameter key (InputKey),
// so a layer its own Type parameters' bindings hide adds no reach; a holder that may afterwards hold the call's own pointer at such a
// layer reaches storage the body does not own, with the record of that layer's pointee (OwnPointer; SPEC 15.6.4's composition), and a
// key with a part that is no layer the walked Type expression writes keeps the declared step (PunKey.Declared; SPEC 5.7 Calls). At the
// first step the reading path's Type is the input's pointee or referent (SPEC 15.6 Holders, 5.7 Calls): a holder whose Place it does
// not designate (MirBorrows.PunTypes' Designates) is read punned (SPEC 5.2), its state read as well at the guard of that Type's raw pointer raw
// Places, each a layer read at its own key (Reading; never the whole Type's guard: a part that is no raw Place is not read), and
// storage the body does not own is reached, with those layers' record, where that guard steps over an `owner` Scalar's byte or a byte
// that a punned write may not change, a reference's or a handle's (Punned; MirBorrows.PunTypes' PlaceBytes).
// No element carries a Type.
// A read keeps Φ_W only from a holder reached through no shared Loan, and from E where the set read carries Φ_W (SPEC 15.6.4: what is
// reached through a shared layer is read-only; DECISIONS R4-M170), and W writes no holder that only a shared Loan names, at any step
// (SPEC 15.6.4 item 1, 5.2.1 condition 3; DECISIONS R4-M177).
// On the state a location's plan reads, a set's content and chain are each read once (E13 (5)), however many operands and retention rows
// read them; whether a Type may hold a Loan, and its MirWrites, are walked once per distinct case Type and program version (the holding
// memo, DECISIONS R4-M140), which answers a local's holder test and each reading of the walk. Must not form a value or a Loan
// (MirBorrows.Values).
internal sealed partial class MirBorrows
{
    // The holding memo's presized capacity: a compilation's distinct case Types of holder queries, the library's 339 and a program's own
    // (339 to 461, median 346, on the G258 corpus); a larger program grows it once.
    private const int HoldingCapacity = 384;

    // How a W walk reached a holder (writingVia, DECISIONS R4-M154): at its first step (ViaRoot) (the argument's direct Loans or addresses name it, or
    // E there), past another holder, or both; whether at the first step by an element the reading Type does not designate (ViaPun); and
    // whether a step of it at a reading key was left out, its guard Deep (ViaDeep; ReadAt).
    private const byte ViaRoot = 1;
    private const byte ViaPast = 2;
    private const byte ViaPun = 4;
    private const byte ViaDeep = 8;

    private readonly List<int> targets = []; // The holders a reference's value refers to (refs), or a call's store writes (W).
    private readonly List<int> targetRoots = []; // The targets' root locals, -1 for E: Refs', and W's (the Type a holder's state is read at).
    private readonly List<int> read = []; // The sets whose content or chain the location read, reset by Plan.
    private readonly List<int> chained = []; // A closure's holders to read, each queued once, again where first reached writable (Chain).
    private readonly List<int> parts = []; // The states a closure read, each through rule W's clause, which one n-ary union joins.
    private readonly Dictionary<MirHoldingKey, MirModeDepths> walked = new(); // HoldsLoans' scratch.
    private readonly List<RawMember> members = []; // HoldsLoans' scratch for a stored raw layer's members.
    private readonly List<Hold> holds = []; // HoldsLoans' scratch for the entries it reads.
    // Holding's memo: each case Type's answer for the program version holdingVersion names, by reference.
    private readonly Dictionary<BoundType, (bool Holds, MirWrites Writes)> holding = new(HoldingCapacity, ReferenceEqualityComparer.Instance);
    // W's reading nodes: a holder (with its root local) read on at a reading Type's key.
    private readonly List<(int Slot, int Root, BoundType Type, int Key)> nodes = [];
    private readonly HashSet<(int Slot, int Key)> nodeSeen = []; // The walk's nodes, each once.
    private readonly HashSet<(int Part, int Key)> nodeParts = []; // The walk's state parts read at a key, each once.
    // Reading's memo, by the reading key for the same program version: its raw pointer rows' guard, and the erased case Type whose
    // Reached entry storage the body does not own takes through them (null: Reached.All).
    private readonly Dictionary<PlaceKey, (MirWrites Guard, BoundType? Record)> readings = [];
    private readonly List<PlaceKey> readingRows = []; // Reading's scratch: a reading Type's raw pointer rows (RawPointers).
    private (int Content, int Chain)[] reads = []; // By set: its content and its chain at the location, each plus 1 (0: not read there).
    private int[] queuedBy = []; // By holder: the closure that queued it.
    private int[] partlyBy = []; // By holder: the closure that reached it through a part or E (rule W's read-through clause).
    private int[] writableBy = []; // By holder: the closure that reached it through no shared Loan (Φ_W's strip).
    private bool partsWritable; // Whether a part read through no shared Loan carries Φ_W (Parts), which the parts' union then keeps.
    private int closures; // The closures made (Chain).
    private int[] writing = []; // By slot: the W walk that reached it.
    private byte[] writingVia = []; // By slot: how that walk reached it (ViaRoot, ViaPast).
    private (BoundType? Type, bool Several)[] writingPlace = []; // By slot: the erased case Type of the storage by which that walk
                                                                 // reached it past a holder (a reading layer's, ReadPart; else the
                                                                 // Place the reaching Loan names, PlaceType), or several.
    private int[] keyedBy = []; // By slot: the W walk that reads it on at a reading key, not by the declared step.
    private int walks; // The W walks made.
    private bool walkResting; // Whether the W walk read a set that rests on an undecided write (Φ_M, Φ_U; MirBorrows.Puns).
    private ulong holdingVersion; // The program version whose case Types the holding memo answers (0: none).

    // Whether a direct Loan's Place is its holder's whole value, through which rule W's read-through clause reads a Copy (Parts).
    private static bool Whole(in Loan loan) => loan.Count == 0;

    // The root local of the holder a direct Loan names, whose Type guards it (Guard) and whose exposure a store reads (MirBorrows.Flow);
    // none (-1) for E: a static's direct Loan names E and keeps the static's key, past the locals, as its Root (MirBorrows.Holders).
    // The one statement of it: Refs, FirstStep and Write record a target's root through it, so E takes no local's guard on any route.
    private static int GuardRoot(in Loan loan) => loan.Named == Escaped ? -1 : loan.Root;

    // content(S) (A3 5): the states of the holders its direct Loans name, and E's where it carries Φ_E or no direct Loan, each read through
    // rule W's clause (Parts), a shared Loan's holder and E for a set without Φ_W read-only, each distinct Shared set once, joined by one
    // n-ary union (PartsUnion); memoized at the location.
    private int Content(ulong[] state, int set)
    {
        if (set < this.reads.Length && this.reads[set].Content != 0)
        {
            return this.reads[set].Content - 1;
        }

        var (direct, escaped, writable) = (false, false, false);
        this.Work += this.sets.Count(set);
        this.partsWritable = false;
        this.parts.Clear();
        this.Step();
        foreach (var element in this.sets.Elements(set))
        {
            escaped |= element == Escaped;
            writable |= element == Writable;
            if (element >= this.loanBase && this.loans[element - this.loanBase] is { Direct: true } loan)
            {
                direct = true;
                if (loan.Named >= 0)
                {
                    this.Parts(state[loan.Named], Whole(loan), loan.Mode != MirRefKind.Shared);
                }
            }
        }

        if (escaped || !direct)
        {
            this.Parts(state[Escaped], whole: false, writable);
        }

        var content = this.Rested(this.PartsUnion(), set);
        this.Memo(set).Content = content + 1;
        return content;
    }

    // chain(S) (SPEC 15.6.4; DECISIONS BU-40, R4-M138): the least fixed point of a read from S, the states of the holders content(S) reads,
    // then of each holder a state read names by a direct Loan or address, and E's where that state carries Φ_E; each holder is queued once
    // per closure, so a pointer cycle ends, again where a reach of it is the first through no shared Loan (a Shared set read once per
    // closure at each of the two levels, the writable one covering the other), and read through rule W's clause as a whole value only where
    // every reach of it was one, read-only where none was through no shared Loan (Parts); the states are joined by one n-ary union
    // (PartsUnion); memoized at the location.
    private int Chain(ulong[] state, int set)
    {
        if (set < this.reads.Length && this.reads[set].Chain != 0)
        {
            return this.reads[set].Chain - 1;
        }

        MirDataflow.Grow(ref this.queuedBy, this.holderCount);
        MirDataflow.Grow(ref this.partlyBy, this.holderCount);
        MirDataflow.Grow(ref this.writableBy, this.holderCount);
        this.closures++;
        this.chained.Clear();
        if (!this.Queue(set, writable: true))
        {
            this.Queued(Escaped, whole: false, writable: false); // content's rule: E where S carries no direct Loan (Queue read its Φ_W).
        }

        this.Step();
        for (var i = 0; i < this.chained.Count; i++)
        {
            var holder = this.chained[i];
            var writable = this.writableBy[holder] == this.closures;
            this.Queue(Placed(state[holder]), writable);
            if (this.Steps(Shared(state[holder]), writable ? 1 : 0))
            {
                this.Queue(Shared(state[holder]), writable);
            }
        }

        this.partsWritable = false;
        this.parts.Clear();
        this.Step();
        foreach (var holder in this.chained)
        {
            this.Parts(state[holder], whole: this.partlyBy[holder] != this.closures, this.writableBy[holder] == this.closures);
        }

        var chain = this.Rested(this.PartsUnion(), set);
        this.Memo(set).Chain = chain + 1;
        return chain;
    }

    // Queues for Chain the holders a set names, those its direct Loans and addresses name and E where it carries Φ_E, each reached through
    // no shared Loan where the set was (`writable`) and its Loan is not shared, E where the set carries Φ_W; whether it has a direct one.
    private bool Queue(int set, bool writable)
    {
        var direct = false;
        this.Work += this.sets.Count(set);
        foreach (var element in this.sets.Elements(set))
        {
            if (element is Escaped or Writable)
            {
                this.Queued(Escaped, whole: false, writable && element == Writable);
            }
            else if (element >= this.loanBase && this.loans[element - this.loanBase] is { Direct: true } loan)
            {
                direct = true;
                if (loan.Named >= 0)
                {
                    this.Queued(loan.Named, Whole(loan), writable && loan.Mode != MirRefKind.Shared);
                }
            }
        }

        return direct;
    }

    // A holder Chain reads, once per closure and again where a reach of it is the first through no shared Loan, and whether a reach of it
    // was through a part or through E, and one through no shared Loan (may-facts, joined).
    private void Queued(int holder, bool whole, bool writable)
    {
        if (this.queuedBy[holder] != this.closures || (writable && this.writableBy[holder] != this.closures))
        {
            this.queuedBy[holder] = this.closures;
            this.chained.Add(holder);
        }

        if (writable)
        {
            this.writableBy[holder] = this.closures;
        }

        if (!whole)
        {
            this.partlyBy[holder] = this.closures;
        }
    }

    // A set's memo at the location, listed for Plan's reset when first touched.
    private ref (int Content, int Chain) Memo(int set)
    {
        MirDataflow.Grow(ref this.reads, set + 1);
        if (this.reads[set] == default)
        {
            this.read.Add(set);
        }

        return ref this.reads[set];
    }

    // Rule W's read-through clause (SPEC 15.6.5): a read through a reference to a holder's whole value is a Copy of its state, through one
    // to a part of it, or through E, not (Indirect, MirBorrows.Causes); into the parts an n-ary union joins: its Placed set, and its
    // Shared set where the walk (Step) has not read it at that level (Indirect distributes over the parts' union); and whether a read of a
    // holder reached through no shared Loan (`writable`) carries Φ_W.
    private void Parts(ulong held, bool whole, bool writable)
    {
        this.parts.Add(whole ? Placed(held) : this.Indirect(Placed(held)));
        if (this.Steps(Shared(held), whole ? 0 : 1))
        {
            this.parts.Add(whole ? Shared(held) : this.Indirect(Shared(held)));
        }

        this.partsWritable |= writable && (this.sets.Contains(Placed(held), Writable) || this.sets.Contains(Shared(held), Writable));
    }

    // The parts' n-ary union, without Φ_W where no part read through no shared Loan carries it (SPEC 15.6.4, R4-M170).
    private int PartsUnion()
    {
        var union = this.sets.Union(CollectionsMarshal.AsSpan(this.parts));
        return this.partsWritable || !this.sets.Contains(union, Writable) ? union : this.sets.Remove(union, this.writableSet);
    }

    // refs(S) (A3 5) into the targets: the holders its direct Loans name, and E where it carries Φ_E or no direct Loan; with each target's
    // local in targetRoots (-1 for E), whose exposure a store through S reads (MirBorrows.Flow).
    private void Refs(int set)
    {
        var (direct, escaped) = (false, false);
        this.targets.Clear();
        this.targetRoots.Clear();
        this.Work += this.sets.Count(set);
        foreach (var element in this.sets.Elements(set))
        {
            escaped |= element == Escaped;
            if (element >= this.loanBase && this.loans[element - this.loanBase] is { Direct: true } loan)
            {
                direct = true;
                if (loan.Named >= 0)
                {
                    this.targets.Add(loan.Named);
                    this.targetRoots.Add(GuardRoot(loan));
                }
            }
        }

        if (escaped || !direct)
        {
            this.targets.Add(Escaped);
            this.targetRoots.Add(-1);
        }
    }

    // The guard of a holder's own root Type, the Type the walk reads its state at (Writes, Read): what a call that writes its storage
    // may write past it (Holds); none for E and a static, which a call writes as storage the body does not own.
    private MirWrites Guard(int root) => root < 0 ? MirWrites.None : this.Guard(cases.Type(this.body.Locals[root].Type));

    // The guard of the Type a holder's state is read at, its root Type or the first step's reading Type (Punned): what a call that
    // writes through a value of it may write past it (Holds).
    private MirWrites Guard(BoundType type)
    {
        _ = this.Holds(type, out var writes);
        return writes;
    }

    // Whether a case Type may hold a Loan, with what a call may write past a value of it (MirTypes.HoldsLoans, one walk per distinct case
    // Type and program version), memoized: the answer is a function of the interned Type and of what Binding publishes it holds
    // (TyCtxt.Holdings), which a version fixes, so the memo is cleared when the program's version changes (DECISIONS R4-M140). The
    // holder test and the walk's guard ask it of a local's Type, the storing rule's landing fact of a Place's (MirBorrows.Storing).
    private bool Holds(BoundType type, out MirWrites writes)
    {
        this.HoldingVersion();
        ref var known = ref CollectionsMarshal.GetValueRefOrAddDefault(this.holding, type, out var exists);
        if (!exists)
        {
            known.Holds = MirTypes.HoldsLoans(type, types, this.walked, this.members, this.holds, out known.Writes);
        }

        writes = known.Writes;
        return known.Holds;
    }

    // Clears the Type memos (Holds, Reading) at a new program version, whose case Types and holdings they answer (DECISIONS R4-M140).
    private void HoldingVersion()
    {
        if (this.holdingVersion != this.program.Version)
        {
            this.holdingVersion = this.program.Version;
            this.holding.Clear();
            this.readings.Clear();
        }
    }

    // W's first step (A3 E14) into the targets, each holder once, at a new W walk: the holders S's direct Loans not shared and addresses
    // name, and E where S carries Φ_E or no direct Loan at all, a shared one counting (SPEC 15.6.4 item 1: never a holder that only a
    // shared Loan names; DECISIONS R4-M177). A referent store's whole reach (MirBorrows.Calls). Through a stated layer with covering inputs
    // (PLAN G338), also each one's first step. A set with no direct Loan reaches E, the empty set included: only Keep, at a covered store,
    // reads the empty set as null (PLAN G598); elsewhere it stays dev's E, the owner's exception O-60 (DECISIONS R4-M183).
    private void Referents(int set)
    {
        this.BeginWalk();
        this.FirstStep(set);
        this.CoveredSteps();
    }

    // Begins a W walk.
    private void BeginWalk()
    {
        MirDataflow.Grow(ref this.writing, this.holderCount);
        MirDataflow.Grow(ref this.writingVia, this.holderCount);
        MirDataflow.Grow(ref this.writingPlace, this.holderCount);
        MirDataflow.Grow(ref this.keyedBy, this.holderCount);
        this.walks++;
        this.walkResting = false;
        this.targets.Clear();
        this.targetRoots.Clear();
        this.nodes.Clear();
        this.nodeSeen.Clear();
        this.nodeParts.Clear();
    }

    // Each covering input's first step (SPEC 5.2.1, 5.7's table: what a stated layer addresses is what its Origin covers; PLAN G338).
    private void CoveredSteps()
    {
        foreach (var input in this.covering)
        {
            this.FirstStep(this.argValues[input]);
        }
    }

    // W's first step from one set (Referents).
    private void FirstStep(int set)
    {
        var (direct, escaped) = (false, false);
        this.Work += this.sets.Count(set);
        foreach (var element in this.sets.Elements(set))
        {
            escaped |= element == Escaped;
            this.walkResting |= element is Maybe or Undecided;
            if (element >= this.loanBase && this.loans[element - this.loanBase] is { Direct: true } loan)
            {
                direct = true;
                if (loan is { Named: >= 0, Mode: not MirRefKind.Shared })
                {
                    this.Written(loan.Named, GuardRoot(loan), ViaRoot, null);
                }
            }
        }

        if (escaped || !direct)
        {
            this.Written(Escaped, -1, ViaRoot, null);
        }
    }

    // W(S) (A3 E16, DECISIONS R4-M73, R4-M116, R4-M147, R4-M170, R4-M177) into the targets, the holders a call's store through S writes,
    // never one that only a shared Loan names (SPEC 15.6.4 item 1): past its first step, which Referents put there first, from each
    // holder h ≠ E of W whose root Type passes the guard (MirWrites.Through), the holder each direct Loan not shared or address of σ(h)
    // names, and E where σ(h) carries Φ_E; from one whose guard is Named (an abstract part, SPEC 15.6.4's provenance list), those holders
    // and E where σ(h) carries Φ_W (the named step). One walk: each holder it reaches is asked its guard once, and each part of its state
    // is a node read once per walk at that reading (Read); a holder the first step reads punned is read at its rows' guard too
    // (Punned, Reading), whose Limit and Deep count as its own guard's. The two facts that leave the store undecided, which the call's location
    // records (MirBorrows.Calls, .Locations): whether W reached a holder the guard leaves undecided (MirWrites.Limit, R4-M147), and
    // whether, at W's fixed point, the step from a holder whose guard is Deep would change what W writes (Changes, R4-M153; that step
    // is left out, and a Deep holder whose step changes nothing decides nothing). Every store path (a Call's rows, its untied stores
    // among them; A5-F7's capture stores, unit 2c; A5-F6's desc(c) stores, 2f) takes W here, except a 4.6 to 4.9 operation's referent
    // store, which takes W's first step alone, its row's destruction composed at the call (Referents; MirBorrows.Destructions;
    // DECISIONS R4-M150).
    private (bool Limit, bool Deep) Writes(ulong[] state, int set, BoundType input, BoundType? referent, in MirTerminator call, int position)
    {
        this.Write(set, ViaRoot, MirWrites.Through); // The first step again, by the walk (Referents put its holders in the targets).
        foreach (var covered in this.covering)
        {
            this.Write(this.argValues[covered], ViaRoot, MirWrites.Through);
        }

        var punned = this.Punned(set, input, referent, call, position);
        this.Keyed(set, input, referent, call, position);
        var (limited, deeps) = (false, false);
        this.Step(); // Written is idempotent, so a state part read again at its reading writes nothing more.
        for (int i = 0, n = 0; i < this.targets.Count; i++)
        {
            var (guard, pun) = (this.Guard(this.targetRoots[i]), this.PunGuard(this.targets[i], punned));
            var held = state[this.targets[i]];
            limited |= guard == MirWrites.Limit || pun == MirWrites.Limit;
            deeps |= guard == MirWrites.Deep || pun == MirWrites.Deep;
            if (this.keyedBy[this.targets[i]] != this.walks)
            {
                this.ReadHolder(held, guard);
            }

            this.ReadHolder(held, pun);
            for (; n < this.nodes.Count; n++)
            {
                var read = this.ReadAt(state, this.nodes[n]);
                limited |= read == MirWrites.Limit;
                deeps |= read == MirWrites.Deep;
            }
        }

        var deep = false;
        for (var i = 0; deeps && i < this.targets.Count && !deep; i++)
        {
            var held = state[this.targets[i]];
            deep = this.LeftOut(i, punned) && (this.Changes(Placed(held)) || this.Changes(Shared(held)));
        }

        return (limited, deep);
    }

    // W's first step read at the reading path's Type (SPEC 15.6 Holders: a holder is read at the Type of the path that reads it; 5.7 Calls,
    // 15.6.4: the input's pointee or referent): each Place an entry of the argument names (PunEntry) that the Type does not designate at
    // the value's Position (Designates, OffGrid) is read punned (SPEC 5.2) through the Type's raw pointer raw Places (Reading; through a
    // borrow layer only a punnable referent puns): over a raw pointer's byte (PlaceBytes: a safe reference and a handle hold none, the
    // owner's O-223 (B')) the punned pointer has the provenance written there, so the holder's state is read at those layers' guard beside
    // its own (Writes: dev's declared reading stays); where that guard steps over an `owner` Scalar's byte or a byte that a punned
    // write may not change, the pointer reaches storage the body does not own (Unowned): E, past the holder, with those layers'
    // record. The rows are read at the key past the input's own layer at the input's key (InputKey, as Keyed reads on), where it has
    // one; an input whose own layer is hidden in the called function's Type parameter's binding adds no reach of its own (SPEC 15.6.4),
    // and one whose key has no single layer (a shared borrow, a key at Binding's Limit) is read at its referent's root key. The guard,
    // where a holder is punned.
    private MirWrites Punned(int set, BoundType input, BoundType? referent, in MirTerminator call, int position)
    {
        var raw = input is { Kind: BoundTypeKind.Semantics, Semantics: Parsing.SemanticsKind.Raw };
        var borrow = input is { Kind: BoundTypeKind.Semantics, Semantics: Parsing.SemanticsKind.Ref or Parsing.SemanticsKind.Uniq };
        var reading = referent is null || !(raw || borrow) ? null : cases.Type(referent);
        if (reading is null)
        {
            return MirWrites.None;
        }

        var facts = this.punKeys[this.Key(this.InputKey(call, position, input))];
        var rows = facts.Hidden || (borrow && !types.Punnable(reading)) ? default
            : this.Reading(facts.PastCount == 1 ? this.punPastKeys[facts.PastStart] : PlaceKey.Root(reading));
        if (rows.Guard == MirWrites.None)
        {
            return MirWrites.None;
        }

        var (on, guard, punned) = (!this.OffGrid(set), rows.Guard, false);
        this.Work += this.sets.Count(set);
        foreach (var element in this.sets.Elements(set))
        {
            if (this.PunEntry(element) is var entry and >= 0 && cases.TypeOf(this.loans[entry].Place) is var place &&
                !this.Designates(place, reading, on))
            {
                if (this.PlaceBytes(place).Pointer != Extent.None)
                {
                    punned = true;
                    this.writingVia[this.loans[entry].Named] |= ViaPun;
                }

                this.Unowned(rows, place);
            }
        }

        return punned ? guard : MirWrites.None;
    }

    // Storage the body does not own under a punned reading (SPEC 15.6 Holders, example punnedRef; 5.5; 15.6.4's composition): a raw
    // pointer read at the reading's rows (Reading) over a Place of Type `place` reaches it, with the rows' record, where a Loan may
    // be stored through a row (their guard steps) and a byte of the Place may be an `owner` Scalar's (Scalared: no raw pointer need
    // have written it) or one that a punned write may not change (PlaceBytes: a reference's, a handle's; none can have), whatever
    // else the holder carries; at the first step (Punned) and past a holder (ReadPart) alike.
    private void Unowned((MirWrites Guard, BoundType? Record) rows, BoundType place)
    {
        if (rows.Guard is MirWrites.Through or MirWrites.Named && (this.PlaceBytes(place).Fixed != Extent.None || this.Scalared(place)))
        {
            this.Written(Escaped, -1, ViaPast, rows.Record);
        }
    }

    // A punned reading at a Type (SPEC 5.2, 15.6 Holders; the owner's O-223 (B')): a read through a raw layer of that pointee takes a
    // pointer only at a raw pointer raw Place of the Type (RawPointers: no other part of it is read), and each such row is a layer of its
    // own, so the guard is the rows', each read at its own key (MirTypes.WritesPast; SPEC 5.7 Calls: a layer the walked Type expression
    // writes bounds the call's store through it to what its pointee admits, a layer a declaration states does not, a layer stating an
    // Origin admits what its borrows do), None where the Type has no row or the call stores no Loan through any. The record storage the
    // body does not own takes through the rows is the Reached entry of their pointee, where each row through which a Loan may be stored
    // is one the walked Type expression writes (HoldContent.Its at HoldStep.Address) and they have one pointee Type; else Reached.All
    // (null: a declaration's own layer, DECISIONS' A5 value redesign entry; a layer stating an Origin, as the declared walk's). Read at
    // the reading key `at`, the key past the reading layer (a call input's: InputKey's, so a row in a binding of the called function's
    // own Type parameter is hidden: it adds no reach of its own, SPEC 15.6.4, and is no row). Memoized per key for the program version.
    private (MirWrites Guard, BoundType? Record) Reading(PlaceKey at)
    {
        this.HoldingVersion();
        if (this.readings.TryGetValue(at, out var known))
        {
            return known;
        }

        this.readingRows.Clear();
        this.RawPointers(at, this.readingRows);
        var shown = 0;
        for (var i = 0; i < this.readingRows.Count; i++)
        {
            if (!this.punKeys[this.Key(this.readingRows[i])].Hidden)
            {
                this.readingRows[shown++] = this.readingRows[i];
            }
        }

        this.readingRows.RemoveRange(shown, this.readingRows.Count - shown);
        var guard = MirTypes.WritesPast(CollectionsMarshal.AsSpan(this.readingRows), types, this.walked, this.members, this.holds);
        var (record, all) = (default(BoundType), false);
        foreach (var row in this.readingRows)
        {
            var first = this.holds.Count;
            _ = types.Holdings(row.Type, row.Written, row.Holder, this.holds);
            var pointee = this.holds.Count == first + 1 && this.holds[first] is { Step: HoldStep.Address, Holds: HoldContent.Its, Part: { } part }
                ? cases.Type(part) : null;
            this.holds.RemoveRange(first, this.holds.Count - first);
            if (pointee is null || this.Holds(pointee, out _))
            {
                var entry = pointee is null ? null : types.Erase(pointee);
                all |= entry is null || (record is not null && !ReferenceEquals(record, entry));
                record = entry;
            }
        }

        return this.readings[at] = (guard, all ? null : record);
    }

    // The guard a holder's state is read at by the first step's punned reading: its rows' (Reading) where the walk reached it so (ViaPun).
    private MirWrites PunGuard(int slot, MirWrites punned) => (this.writingVia[slot] & ViaPun) != 0 ? punned : MirWrites.None;

    // Whether the walk left out a step of a target, its guard Deep (R4-M153): the guard of its own root Type, of the first step's punned
    // reading, or of a reading key it was read on at (ViaDeep).
    private bool LeftOut(int target, MirWrites punned)
        => this.Guard(this.targetRoots[target]) == MirWrites.Deep || this.PunGuard(this.targets[target], punned) == MirWrites.Deep ||
            (this.writingVia[this.targets[target]] & ViaDeep) != 0;

    // The reading key past the first step (SPEC 15.6 Holders, 5.7 Calls, 15.6.4): a holder the argument's entry names at a Place the input's
    // pointee or referent designates (Designates) is read on at that Type's key, the one key past the input's own layer at the input's
    // key (InputKey: a parameter key marks what the callee's Type parameters hide), never by the declared step (Writes); one it reads
    // punned through a raw layer (PunRows), at each raw pointer row's own key (PunNodes). An input whose key has no single layer, or
    // keeps the declared step (PunKey.Declared: a layer stating an Origin is Cover's), keeps it.
    private void Keyed(int set, BoundType input, BoundType? referent, in MirTerminator call, int position)
    {
        if (referent is null || input is not { Kind: BoundTypeKind.Semantics })
        {
            return;
        }

        var facts = this.punKeys[this.Key(this.InputKey(call, position, input))];
        if (facts.Declared || facts.PastCount != 1)
        {
            return;
        }

        var (past, raw) = (this.punPastKeys[facts.PastStart], this.punPastRaw[facts.PastStart]);
        var (key, reading, on) = (this.Key(past), cases.Type(referent), !this.OffGrid(set));
        foreach (var element in this.sets.Elements(set))
        {
            if (this.PunEntry(element) is not (var entry and >= 0))
            {
                continue;
            }

            var place = cases.TypeOf(this.loans[entry].Place);
            if (this.Designates(place, reading, on))
            {
                this.Node(this.loans[entry].Named, GuardRoot(this.loans[entry]), reading, key);
            }
            else if (raw && this.PunRows(place, past))
            {
                this.PunNodes(this.loans[entry].Named, GuardRoot(this.loans[entry]));
            }
        }
    }

    // Whether a read through a layer whose pointee or referent, at key `at`, does not designate a Place of Type `place` is a punned
    // read that may take a raw pointer from the Place's bytes (SPEC 5.2, 15.6 Holders; U3b's arm, Punned): the pointee has a raw pointer
    // raw Place within (PunTypes.RawPointers: its rows, left in readingRows for PunNodes) and the Place may hold a raw pointer's byte
    // (PlaceBytes: a safe reference and a handle hold none; the owner's O-223 (B')).
    private bool PunRows(BoundType place, PlaceKey at)
    {
        this.readingRows.Clear();
        if (this.PlaceBytes(place).Pointer != Extent.None)
        {
            this.RawPointers(at, this.readingRows);
        }

        return this.readingRows.Count > 0;
    }

    // A punned holder's read-on (PunRows' rows): at each raw pointer row's own key, the row a layer of its own (SPEC 5.2, 15.6 Holders;
    // the owner's O-223 (B')), never at the whole pointee's key, whose other parts (an Option's payload, a Function value, a borrow)
    // are no raw Place a punned read takes a pointer from.
    private void PunNodes(int slot, int root)
    {
        foreach (var row in this.readingRows)
        {
            this.Node(slot, root, cases.Type(row.Type!), this.Key(row));
        }
    }

    // A holder read on at a reading key, once per walk; its read-on is the key's (ReadAt), not the declared step's, unless the key keeps
    // the declared step (PunKey.Declared).
    private void Node(int slot, int root, BoundType type, int key)
    {
        if (this.nodeSeen.Add((slot, key)))
        {
            this.nodes.Add((slot, root, type, key));
            this.keyedBy[slot] = this.punKeys[key].Declared ? this.keyedBy[slot] : this.walks;
        }
    }

    // A holder's state read on at its reading Type's key (SPEC 15.6 Holders: every element with every layer of the key the holder was
    // reached at), at that Type's guard (MirTypes.HoldsLoans; U3b's reading of a punned holder): at each raw layer and exclusive borrow
    // layer the key's Type holds at its own address (the key table, TyCtxt.OwnLayers; a hidden layer has none), each part of the state
    // once per (part, layer key); and E where the holder may afterwards hold the call's own pointer at a raw layer (OwnPointer: the read-on
    // through that layer, with its pointee's record; SPEC 15.6.4's composition). A key with a part that is no layer the walked Type
    // expression writes (a declaration's own layer, a layer stating an Origin, an own step, an abstract or unknown part: PunKey.Declared;
    // SPEC 5.7 Calls) reads the holder by the declared step at its own root Type's guard, as the walk's targets do. The guard, whose Limit
    // counts as the walk's (Writes); where it, or the holder's own root Type's, is Deep the step is left out, as the declared walk's
    // (R4-M153: the targets' Changes decides whether that is undecided, ViaDeep).
    private MirWrites ReadAt(ulong[] state, (int Slot, int Root, BoundType Type, int Key) node)
    {
        var facts = this.punKeys[node.Key];
        if (facts.Declared)
        {
            this.ReadHolder(state[node.Slot], this.Guard(node.Root)); // Its Limit and Deep are the target's (Writes).
            return MirWrites.None;
        }

        var guard = this.Guard(node.Root) == MirWrites.Deep ? MirWrites.Deep : this.Guard(node.Type);
        if (guard is not (MirWrites.Through or MirWrites.Named))
        {
            this.writingVia[node.Slot] |= guard == MirWrites.Deep ? ViaDeep : (byte)0;
            return guard;
        }

        // A hidden part adds no reach of its own, but the abstract value reaches what its holder's Loans and addresses reach (SPEC
        // 15.6.4: the named step, R4-M147's).
        if (facts.Hidden)
        {
            this.ReadHolder(state[node.Slot], MirWrites.Named);
        }

        for (var j = 0; j < facts.FirstCount; j++)
        {
            var (type, raw) = (this.punViewTypes[facts.FirstStart + j], this.punPastRaw[facts.PastStart + j]);
            var past = this.punPastKeys[facts.PastStart + j];
            this.ReadPart(Placed(state[node.Slot]), guard, type, raw, past);
            this.ReadPart(Shared(state[node.Slot]), guard, type, raw, past);
        }

        if (guard == MirWrites.Through)
        {
            this.OwnPointer(node.Key);
        }

        return guard;
    }

    // One state part read at one layer of a reading key, the layer's pointee or referent `type` and the key past it (SPEC 15.6 Holders, 5.2,
    // 5.7's table, 15.6.4): E where the part carries Φ_E (Φ_W past a Named guard); each holder a non-shared direct Loan or address names,
    // typed where `type` designates its Place (Designates) and punned where it may take a raw pointer from the Place's bytes (PunRows),
    // through a borrow layer only where the referent is punnable: written with the record of `type`'s storage (Written's place: the
    // reading layer's, in place of R4-M154 Revision 8's Place Type of the reaching Loan; for a borrow of a Place's own Type the two
    // agree) and read on at the key past the layer (Node, ReadAt), a punned one at its raw pointer rows' keys (PunNodes), and storage
    // the body does not own where the punned read is over a byte no raw pointer need or can have written (Unowned); at no other
    // layer (none: the layer reads nothing there). Past a holder
    // an element's Position is not known (R4-M198's Position is the argument value's; U5's per-element markers), so every row of its
    // Place's leading chain may be designated.
    private void ReadPart(int part, MirWrites guard, BoundType type, bool raw, PlaceKey past)
    {
        var key = part == 0 ? -1 : this.Key(past);
        if (part == 0 || !this.nodeParts.Add((part, key)))
        {
            return;
        }

        var named = guard == MirWrites.Named;
        this.Work += this.sets.Count(part);
        foreach (var element in this.sets.Elements(part))
        {
            this.walkResting |= element is Maybe or Undecided;
            if (element == (named ? Writable : Escaped))
            {
                this.Written(Escaped, -1, ViaPast, null);
            }
            else if (element >= this.loanBase && this.loans[element - this.loanBase] is { Direct: true, Named: >= 0, Mode: not MirRefKind.Shared } loan)
            {
                if (loan.Named == Escaped)
                {
                    this.Written(Escaped, -1, ViaPast, null);
                    continue;
                }

                var place = cases.TypeOf(loan.Place);
                if (this.Designates(place, type, true))
                {
                    this.Written(loan.Named, GuardRoot(loan), ViaPast, types.Erase(type));
                    this.Node(loan.Named, GuardRoot(loan), type, key);
                }
                else if (raw || types.Punnable(type))
                {
                    if (this.PunRows(place, past))
                    {
                        this.Written(loan.Named, GuardRoot(loan), ViaPast, types.Erase(type));
                        this.PunNodes(loan.Named, GuardRoot(loan));
                    }

                    this.Unowned(this.Reading(past), place);
                }
            }
        }
    }

    // Storage the body does not own that the call's own pointer reaches (SPEC 15.6.4's composition, example twoLayers; 5.7 Calls): where
    // storage the call writes, read at `key`, holds a raw layer without an Origin past which a value that may carry a Loan is stored, the
    // call may write its own pointer at that layer, also as a Tuple element, which may address that storage, and store through it at the
    // next one; at the first step and past a holder alike (ReadAt). What it stores there takes the layer's record and bound (the A5 value
    // redesign's rule 5, its `Its` row): the Reached entry of the layer's pointee (Written's place, Received), the pointee judged at its
    // own key, where the parts a called function's own Type parameter's binding holds add no reach (PunKey.WhollyHidden; SPEC 15.6.4:
    // storage of an abstract Type holds an argument's own value only where the premises make it one; DECISIONS BU-202; the owner's O-353
    // (a)).
    private void OwnPointer(int key)
    {
        var facts = this.punKeys[key];
        for (var j = 0; facts.RawInline && !facts.Stated && j < facts.FirstCount; j++)
        {
            var pointee = this.punViewTypes[facts.FirstStart + j];
            if (this.punPastRaw[facts.PastStart + j] && this.Holds(pointee, out _) &&
                !this.punKeys[this.Key(this.punPastKeys[facts.PastStart + j])].WhollyHidden)
            {
                this.Written(Escaped, -1, ViaPast, types.Erase(pointee));
            }
        }
    }

    // A reached holder's state read at one guard: its two parts, each a node of the walk, where the guard steps (Through, Named).
    private void ReadHolder(ulong held, MirWrites guard)
    {
        if (guard is MirWrites.Through or MirWrites.Named)
        {
            this.Read(Placed(held), guard);
            this.Read(Shared(held), guard);
        }
    }

    // A node of the walk: a part of a reached holder's state, read at its reading, the holder's guard, once per walk (MirBorrows.States'
    // mark by set and level): what a Named read writes, a Through read of the same part writes too (Φ_W is always beside Φ_E), so a part
    // read at Through is not read again at Named, whichever holders' part it is.
    private void Read(int part, MirWrites guard)
    {
        if (this.Steps(part, guard == MirWrites.Through ? 1 : 0))
        {
            this.Write(part, ViaPast, guard);
        }
    }

    // Whether W's step from a set past a holder whose guard is Deep, which W leaves out (Writes), would change what the store writes, by
    // Written's ways (R4-M153, R4-M154): reach a holder W does not, one W reaches only at its first step, or one past a holder by another
    // record than the Type of the Place the step's Loan names (what the declared step, or a reading layer of the Place's own Type,
    // records; W's record is the reading layer's where it read the holder at a key, ReadPart); E, which takes Reached.All by either way,
    // only where W does not reach it. A shared Loan names no holder the step writes (R4-M177).
    private bool Changes(int set)
    {
        this.Work += this.sets.Count(set);
        foreach (var element in this.sets.Elements(set))
        {
            if (element == Escaped ? this.ChangesUnowned() : element >= this.loanBase &&
                this.loans[element - this.loanBase] is { Direct: true, Named: >= 0, Mode: not MirRefKind.Shared } loan && this.Changes(loan))
            {
                return true;
            }
        }

        return false;
    }

    // Changes' answer for the holder one direct Loan names.
    private bool Changes(in Loan loan)
        => loan.Named == Escaped ? this.ChangesUnowned() : this.writing[loan.Named] != this.walks ||
            (this.writingVia[loan.Named] & ViaPast) == 0 ||
            (this.writingPlace[loan.Named] is (var known, false) && !ReferenceEquals(known, this.PlaceType(loan)));

    // Changes' answer for E, which the step reaches with Reached.All: where W does not reach it, or reaches it only with a layer's record
    // (a punned reading's layers, Punned and Unowned; the call's own pointer's layer, OwnPointer; Written).
    private bool ChangesUnowned() => this.writing[Escaped] != this.walks || !this.writingPlace[Escaped].Several;

    // A step of W from a set (Writes), at its first step (ViaRoot) or past a holder (ViaPast) by the holder's guard (`past`, Through at the
    // first step): the holder each direct Loan not shared or address names (R4-M177), and E where it carries Φ_E, past Named Φ_W. A value
    // that may refer to storage the body does not own carries Φ_E and Φ_W where it is formed (an untied result or store, an integer's
    // pointer, a parameter and E's own content at entry), so a set naming nothing writes nothing.
    private void Write(int set, byte via, MirWrites past)
    {
        var named = past == MirWrites.Named;
        this.Work += this.sets.Count(set);
        foreach (var element in this.sets.Elements(set))
        {
            this.walkResting |= element is Maybe or Undecided;
            if (element == (named ? Writable : Escaped))
            {
                this.Written(Escaped, -1, via, null);
            }
            else if (element >= this.loanBase &&
                this.loans[element - this.loanBase] is { Direct: true, Named: >= 0, Mode: not MirRefKind.Shared } loan)
            {
                var place = via == ViaPast && loan.Named != Escaped ? this.PlaceType(loan) : null;
                this.Written(loan.Named, GuardRoot(loan), via, place);
            }
        }
    }

    // A holder W reaches, with its root local (E: -1), whose Type the walk reads its state at, and how it was reached (past a holder, with
    // the erased case Type of the Place the Loan names, `place`, the storage the call writes there; DECISIONS R4-M154, Revision 8). E
    // takes Reached.All by every way but a layer's own, whose record `place` names (a punned reading's layers, Punned and Unowned; the
    // call's own pointer's layer, OwnPointer): a reach of E with no `place` counts as several, so All stands beside any record.
    private void Written(int slot, int root, byte via, BoundType? place)
    {
        if (this.writing[slot] != this.walks)
        {
            (this.writing[slot], this.writingVia[slot], this.writingPlace[slot]) = (this.walks, 0, default);
            this.targets.Add(slot);
            this.targetRoots.Add(root);
        }

        this.writingVia[slot] |= via;
        if (this.writingPlace[slot] is var (known, several) && !several && (place is null ? slot == Escaped : !ReferenceEquals(known, place)))
        {
            this.writingPlace[slot] = (known ?? place, known is not null || place is null);
        }
    }

    // A direct Loan's Place's case Type under the view, Origin-erased (TyCtxt.Erase): the storage a call's write past a holder reaches
    // through it, whose Reached entry the holder receives (MirBorrows.Calls).
    private BoundType PlaceType(in Loan loan) => types.Erase(cases.TypeOf(loan.Place));
}
