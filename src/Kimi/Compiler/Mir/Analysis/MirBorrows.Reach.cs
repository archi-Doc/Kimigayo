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
// so a layer its own Type parameters' bindings hide adds no reach. At the first step the
// reading path's Type is the input's pointee or referent (SPEC 15.6 Holders, 5.7 Calls): a holder whose Place it does not designate
// (MirBorrows.PunTypes' Designates) is read punned (SPEC 5.2), its state read at that Type's guard as well, and storage the body
// does not own is reached where that guard steps over an `owner` Scalar's byte (Punned). No element carries a Type.
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
    // E there), past another holder, or both; and whether at the first step by an element the reading Type does not designate (ViaPun).
    private const byte ViaRoot = 1;
    private const byte ViaPast = 2;
    private const byte ViaPun = 4;

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
    private readonly List<(int Slot, BoundType Type, int Key)> nodes = []; // W's reading nodes: a holder read on at a reading Type's key.
    private readonly HashSet<(int Slot, int Key)> nodeSeen = []; // The walk's nodes, each once.
    private readonly HashSet<(int Part, int Key)> nodeParts = []; // The walk's state parts read at a key, each once.
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

    // Whether a local's case Type may hold a Loan (Holds): the holder test (MirBorrows.Holders).
    private bool Holding(int local) => this.Holds(cases.Type(this.body.Locals[local].Type), out _);

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
        if (this.holdingVersion != this.program.Version)
        {
            this.holdingVersion = this.program.Version;
            this.holding.Clear();
        }

        ref var known = ref CollectionsMarshal.GetValueRefOrAddDefault(this.holding, type, out var exists);
        if (!exists)
        {
            known.Holds = MirTypes.HoldsLoans(type, types, this.walked, this.members, this.holds, out known.Writes);
        }

        writes = known.Writes;
        return known.Holds;
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
    // is a node read once per walk at that reading (Read); a holder the first step reads punned is read at the reading Type's guard too
    // (Punned), whose Limit and Deep count as its own guard's. The two facts that leave the store undecided, which the call's location
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

        var punned = this.Punned(set, input, referent);
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
                limited |= this.ReadAt(state, this.nodes[n]) == MirWrites.Limit;
            }
        }

        var deep = false;
        for (var i = 0; deeps && i < this.targets.Count && !deep; i++)
        {
            var held = state[this.targets[i]];
            deep = (this.Guard(this.targetRoots[i]) == MirWrites.Deep || this.PunGuard(this.targets[i], punned) == MirWrites.Deep) &&
                (this.Changes(Placed(held)) || this.Changes(Shared(held)));
        }

        return (limited, deep);
    }

    // W's first step read at the reading path's Type (SPEC 15.6 Holders: a holder is read at the Type of the path that reads it; 5.7 Calls,
    // 15.6.4: the input's pointee or referent): each Place an entry of the argument names (PunEntry) that the Type does not designate at
    // the value's Position (Designates, OffGrid) is read punned (SPEC 5.2) where the Type has a raw pointer raw Place within (RawPointer;
    // through a borrow layer only a punnable referent puns) and the Place is no safe reference (the owner's O-223 (B')): over a raw
    // pointer's byte (Pointered) the punned pointer has the provenance written there, so the holder's state is read at the reading Type's
    // guard beside its own (Writes: dev's declared reading stays); where that guard steps over an `owner` Scalar's byte (Scalared), the
    // pointer reaches storage the body does not own (SPEC 15.6 Holders, 5.5): E, past the holder. The guard, where a holder is punned.
    private MirWrites Punned(int set, BoundType input, BoundType? referent)
    {
        var raw = input is { Kind: BoundTypeKind.Semantics, Semantics: Parsing.SemanticsKind.Raw };
        var borrow = input is { Kind: BoundTypeKind.Semantics, Semantics: Parsing.SemanticsKind.Ref or Parsing.SemanticsKind.Uniq };
        var reading = referent is null || !(raw || borrow) ? null : cases.Type(referent);
        if (reading is null || (borrow && !types.Punnable(reading)) || !this.RawPointer(reading))
        {
            return MirWrites.None;
        }

        var (on, guard, punned) = (!this.OffGrid(set), this.Guard(reading), false);
        this.Work += this.sets.Count(set);
        foreach (var element in this.sets.Elements(set))
        {
            if (this.PunEntry(element) is var entry and >= 0 && cases.TypeOf(this.loans[entry].Place) is var place &&
                place is not { Kind: BoundTypeKind.Semantics, Semantics: Parsing.SemanticsKind.Ref or Parsing.SemanticsKind.Uniq } &&
                !this.Designates(place, reading, on))
            {
                if (this.Pointered(place) != Extent.None)
                {
                    punned = true;
                    this.writingVia[this.loans[entry].Named] |= ViaPun;
                }

                if (guard is MirWrites.Through or MirWrites.Named && this.Scalared(place))
                {
                    this.Written(Escaped, -1, ViaPast, null);
                }
            }
        }

        return punned ? guard : MirWrites.None;
    }

    // The guard a holder's state is read at by the first step's punned reading: the reading Type's where the walk reached it so (ViaPun).
    private MirWrites PunGuard(int slot, MirWrites punned) => (this.writingVia[slot] & ViaPun) != 0 ? punned : MirWrites.None;

    // The reading key past the first step (SPEC 15.6 Holders, 5.7 Calls, 15.6.4): a holder the argument's entry names at a Place the input's
    // pointee or referent designates (Designates) is read on at that Type's key, the one key past the input's own layer at the input's
    // key (InputKey: a parameter key marks what the callee's Type parameters hide), never by the declared step (Writes). An input whose
    // key has no single layer, or no known layers, keeps the declared step.
    private void Keyed(int set, BoundType input, BoundType? referent, in MirTerminator call, int position)
    {
        if (referent is null || input is not { Kind: BoundTypeKind.Semantics })
        {
            return;
        }

        var facts = this.punKeys[this.Key(this.InputKey(call, position, input))];
        if (facts.FirstUnknown || facts.Unsupported || facts.PastCount != 1)
        {
            return;
        }

        var (key, reading, on) = (this.Key(this.punPastKeys[facts.PastStart]), cases.Type(referent), !this.OffGrid(set));
        var raw = this.punPastRaw[facts.PastStart];
        foreach (var element in this.sets.Elements(set))
        {
            if (this.PunEntry(element) is var entry and >= 0 && cases.TypeOf(this.loans[entry].Place) is var place &&
                (this.Designates(place, reading, on) || (raw && this.RawPointer(reading) && this.Pointered(place) != Extent.None)))
            {
                this.Node(this.loans[entry].Named, reading, key);
            }
        }
    }

    // A holder read on at a reading key, once per walk; its read-on is the key's (ReadAt), not the declared step's.
    private void Node(int slot, BoundType type, int key)
    {
        if (this.nodeSeen.Add((slot, key)))
        {
            this.nodes.Add((slot, type, key));
            this.keyedBy[slot] = this.walks;
        }
    }

    // A holder's state read on at its reading Type's key (SPEC 15.6 Holders: every element with every layer of the key the holder was
    // reached at), at that Type's guard (MirTypes.HoldsLoans; U3b's reading of a punned holder): at each raw layer and exclusive borrow
    // layer the key's Type holds at its own address (the key table, TyCtxt.OwnLayers; a hidden layer has none), each part of the state
    // once per (part, layer key). A key whose layers are unknown (an abstract part) or unsupported reads the holder by the declared
    // step at that guard. The guard, whose Limit counts as the walk's (Writes); a Deep one leaves the step out, as the declared walk's
    // (R4-M153: the targets' Changes decides whether that is undecided).
    private MirWrites ReadAt(ulong[] state, (int Slot, BoundType Type, int Key) node)
    {
        var guard = this.Guard(node.Type);
        var facts = this.punKeys[node.Key];
        if (facts.FirstUnknown || facts.Unsupported)
        {
            this.ReadHolder(state[node.Slot], guard);
            return guard;
        }

        if (guard is not (MirWrites.Through or MirWrites.Named))
        {
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
            var key = this.Key(this.punPastKeys[facts.PastStart + j]);
            this.ReadPart(Placed(state[node.Slot]), guard, type, raw, key);
            this.ReadPart(Shared(state[node.Slot]), guard, type, raw, key);
        }

        return guard;
    }

    // One state part read at one layer of a reading key, the layer's pointee or referent `type` and the key past it (SPEC 15.6 Holders, 5.2,
    // 5.7's table, 15.6.4): E where the part carries Φ_E (Φ_W past a Named guard); each holder a non-shared direct Loan or address names,
    // typed where `type` designates its Place (Designates) and punned where the layer is raw, `type` has a raw pointer raw Place within
    // (RawPointer) and the Place may hold a raw pointer's byte (Pointered), or a borrow layer's referent is punnable: written with the record
    // of `type`'s storage (Written's place: the reading layer's, in place of R4-M154 Revision 8's Place Type of the reaching Loan; for a
    // borrow of a Place's own Type the two agree), read on at the key past the layer, and E reached where that storage may hold the call's
    // own pointer (OwnPointer); at no other layer (none: the layer reads nothing there). Past a holder an element's Position is not known
    // (R4-M198's Position is the argument value's; U5's per-element markers), so every row of its Place's leading chain may be designated.
    private void ReadPart(int part, MirWrites guard, BoundType type, bool raw, int key)
    {
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
                if (this.Designates(place, type, true) ||
                    (raw ? this.RawPointer(type) && this.Pointered(place) != Extent.None : types.Punnable(type)))
                {
                    this.Written(loan.Named, GuardRoot(loan), ViaPast, types.Erase(type));
                    this.Node(loan.Named, type, key);
                    if (!named && this.OwnPointer(key))
                    {
                        this.Written(Escaped, -1, ViaPast, null);
                    }
                }
            }
        }
    }

    // Whether storage the call writes, read at `key`, holds a raw layer without an Origin past which a value that may carry a Loan is
    // stored (SPEC 15.6.4's composition, example twoLayers: the call may write its own pointer at that layer, which may address storage the
    // body does not own, and store through it at the next one): rule 4, past a holder.
    private bool OwnPointer(int key)
    {
        var facts = this.punKeys[key];
        for (var j = 0; facts.RawInline && !facts.Stated && j < facts.FirstCount; j++)
        {
            if (this.punPastRaw[facts.PastStart + j] && this.Holds(this.punViewTypes[facts.FirstStart + j], out _))
            {
                return true;
            }
        }

        return false;
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
    // Place's Type; E, which takes Reached.All by either way, only where W does not reach it. A shared Loan names no holder the step
    // writes (R4-M177).
    private bool Changes(int set)
    {
        this.Work += this.sets.Count(set);
        foreach (var element in this.sets.Elements(set))
        {
            if (element == Escaped ? this.writing[Escaped] != this.walks : element >= this.loanBase &&
                this.loans[element - this.loanBase] is { Direct: true, Named: >= 0, Mode: not MirRefKind.Shared } loan && this.Changes(loan))
            {
                return true;
            }
        }

        return false;
    }

    // Changes' answer for the holder one direct Loan names.
    private bool Changes(in Loan loan)
        => this.writing[loan.Named] != this.walks || (loan.Named != Escaped && ((this.writingVia[loan.Named] & ViaPast) == 0 ||
            (this.writingPlace[loan.Named] is (var known, false) && !ReferenceEquals(known, this.PlaceType(loan)))));

    // A step of W from a set (Writes), at its first step (ViaRoot) or past a holder (ViaPast) by the holder's guard (`past`, Through at the
    // first step): the holder each direct Loan not shared or address names (R4-M177), and E where it carries Φ_E, past Named Φ_W. A value
    // that may refer to storage the body does not own carries Φ_E and Φ_W where it is formed (an untied result or store, a raw pointer
    // formed untracked, an integer's pointer, a parameter and E's own content at entry), so a set naming nothing writes nothing.
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
    // the erased case Type of the Place the Loan names, `place`, the storage the call writes there; DECISIONS R4-M154, Revision 8).
    private void Written(int slot, int root, byte via, BoundType? place)
    {
        if (this.writing[slot] != this.walks)
        {
            (this.writing[slot], this.writingVia[slot], this.writingPlace[slot]) = (this.walks, 0, default);
            this.targets.Add(slot);
            this.targetRoots.Add(root);
        }

        this.writingVia[slot] |= via;
        if (place is not null && this.writingPlace[slot] is var (known, several) && !several && !ReferenceEquals(known, place))
        {
            this.writingPlace[slot] = (known ?? place, known is not null);
        }
    }

    // A direct Loan's Place's case Type under the view, Origin-erased (TyCtxt.Erase): the storage a call's write past a holder reaches
    // through it, whose Reached entry the holder receives (MirBorrows.Calls).
    private BoundType PlaceType(in Loan loan) => types.Erase(cases.TypeOf(loan.Place));
}
