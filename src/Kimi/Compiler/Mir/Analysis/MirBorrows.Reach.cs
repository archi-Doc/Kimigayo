// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;

namespace Kimi.Compiler.Mir;

// A5's reach on a state (A3 5's content and refs, E16's W, DECISIONS R4-M73, R4-M116, R4-M147): the holders a set refers to (those its
// direct Loans and addresses name, and E where it carries Φ_E or no direct one), its content, the holders a call's store through it writes
// (W, whose first step alone is Referents), and the guard's per-local MirWrites (MirTypes.HoldsLoans); its chain (SPEC 15.6.4's "what any
// chain of reads through it carries", a Chain row's measure), the least fixed point of a read from it, each holder read once per closure.
// A read keeps Φ_W only from a holder reached through no shared Loan, and from E where the set read carries Φ_W (SPEC 15.6.4: what is
// reached through a shared layer is read-only; DECISIONS R4-M170), and W writes no holder that only a shared Loan names, at any step
// (SPEC 15.6.4 item 1, 5.2.1 condition 3; DECISIONS R4-M177).
// On the state a location's plan reads, a set's content and chain are each read once (E13 (5)), however many operands and retention rows
// read them; whether a local is a holder, and its MirWrites, are walked once per distinct case Type and program version (the holding memo,
// DECISIONS R4-M140). Must not form a value or a Loan (MirBorrows.Values).
internal sealed partial class MirBorrows
{
    // The holding memo's presized capacity: a compilation's distinct case Types of holder queries, the library's 339 and a program's own
    // (339 to 461, median 346, on the G258 corpus); a larger program grows it once.
    private const int HoldingCapacity = 384;

    // How a W walk reached a holder (writingVia, DECISIONS R4-M154): at its first step (ViaRoot) (the argument's direct Loans or addresses name it, or
    // E there), past another holder on a declared route (ViaPast), past one on a converted route (ViaView), and whether on a declared route
    // at all (ViaDeclared: the callee views its storage at its declared Type, so the guard and the Place's Type decide).
    private const byte ViaRoot = 1;
    private const byte ViaPast = 2;
    private const byte ViaView = 4;
    private const byte ViaDeclared = 8;

    // writingView's values besides a key list's head: reached on no converted route; on one whose view no key gives (an abstract part, an
    // own-step entry); on one with no view key yet.
    private const int NoView = -1;
    private const int UnknownView = -2;
    private const int EmptyView = -3;

    private readonly List<(int Key, BoundType? Type, int Next)> viewNodes = []; // The walk's view key lists (pun keys, MirBorrows.PunKeys).
    private readonly List<int> walkQueue = []; // W's queue: each target once, again where its route facts grow.
    private readonly List<int> targets = []; // The holders a reference's value refers to (refs), or a call's store writes (W).
    private readonly List<int> targetRoots = []; // Refs' targets' locals, -1 for E.
    private readonly List<int> read = []; // The sets whose content or chain the location read, reset by Plan.
    private readonly List<int> chained = []; // A closure's holders to read, each queued once, again where first reached writable (Chain).
    private readonly List<int> parts = []; // The states a closure read, each through rule W's clause, which one n-ary union joins.
    private readonly Dictionary<MirHoldingKey, MirModeDepths> walked = new(); // HoldsLoans' scratch.
    private readonly List<RawMember> members = []; // HoldsLoans' scratch for a stored raw layer's members.
    private readonly List<Hold> holds = []; // HoldsLoans' scratch for the entries it reads.
    // Holding's memo: each case Type's answer for the program version holdingVersion names, by reference.
    private readonly Dictionary<BoundType, (bool Holds, MirWrites Writes)> holding = new(HoldingCapacity, ReferenceEqualityComparer.Instance);
    private (int Content, int Chain)[] reads = []; // By set: its content and its chain at the location, each plus 1 (0: not read there).
    private int[] queuedBy = []; // By holder: the closure that queued it.
    private int[] partlyBy = []; // By holder: the closure that reached it through a part or E (rule W's read-through clause).
    private int[] writableBy = []; // By holder: the closure that reached it through no shared Loan (Φ_W's strip).
    private bool partsWritable; // Whether a part read through no shared Loan carries Φ_W (Parts), which the parts' union then keeps.
    private int closures; // The closures made (Chain).
    private MirWrites[] writable = []; // By local: what a call may write past it (MirTypes.HoldsLoans).
    private int[] writing = []; // By slot: the W walk that reached it.
    private MirWrites[] writingKind = []; // By slot: its local's MirWrites, at that walk.
    private byte[] writingVia = []; // By slot: how that walk reached it (ViaRoot, ViaPast).
    private (BoundType? Type, bool Several)[] writingPlace = []; // By slot: the erased case Type of the Places named by the Loans by
                                                                 // which that walk reached it past a holder (PlaceType), or several.
    private int[] writingView = []; // By slot: on a converted route, the head of its view keys in viewNodes, UnknownView, EmptyView, or NoView.
    private int[] writingScalar = []; // By slot: the W walk that reached it on a converted route through a Place holding an `owner` Scalar.
    private int walks; // The W walks made.
    private bool walkResting; // Whether the W walk read a set that rests on an undecided write (Φ_M, Φ_U; MirBorrows.Puns).
    private ulong holdingVersion; // The program version whose case Types the holding memo answers (0: none).

    // Whether a direct Loan's Place is its holder's whole value, through which rule W's read-through clause reads a Copy (Parts).
    private static bool Whole(in Loan loan) => loan.Count == 0;

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
                    this.targetRoots.Add(loan.Named == Escaped ? -1 : loan.Root);
                }
            }
        }

        if (escaped || !direct)
        {
            this.targets.Add(Escaped);
            this.targetRoots.Add(-1);
        }
    }

    // Whether a local's case Type may hold a Loan, with what a call may write past it (MirTypes.HoldsLoans, one walk per distinct case Type
    // and program version): the answer is a function of the interned Type and of what Binding publishes it holds (TyCtxt.Holdings), which a
    // version fixes, so the memo is cleared when the program's version changes (DECISIONS R4-M140).
    private bool Holding(int local)
    {
        MirDataflow.Grow(ref this.writable, this.body.Locals.Length);
        return this.Holds(cases.Type(this.body.Locals[local].Type), out this.writable[local]);
    }

    // Holding's answer for a case Type, memoized: the storing rule's landing fact asks it of a Place's Type (MirBorrows.Storing).
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
    // reads the empty set as null (PLAN G598); elsewhere it stays dev's E, the owner's exception O-60 (DECISIONS R4-M183). A set carrying
    // Κ reaches its holders on a converted route, each viewed at the keys the input's Type expression leads it to (ViewSteps).
    private void Referents(int set, BoundType? input)
    {
        this.BeginWalk();
        this.FirstStep(set, this.sets.Contains(set, Converted) ? (input is null ? UnknownView : this.Key(PlaceKey.Root(input))) : NoView);
        this.CoveredSteps();
    }

    // Begins a W walk.
    private void BeginWalk()
    {
        MirDataflow.Grow(ref this.writing, this.holderCount);
        MirDataflow.Grow(ref this.writingKind, this.holderCount);
        MirDataflow.Grow(ref this.writingVia, this.holderCount);
        MirDataflow.Grow(ref this.writingPlace, this.holderCount);
        MirDataflow.Grow(ref this.writingView, this.holderCount);
        MirDataflow.Grow(ref this.writingScalar, this.holderCount);
        this.walks++;
        this.walkResting = false;
        this.targets.Clear();
        this.walkQueue.Clear();
        this.viewNodes.Clear();
    }

    // Each covering input's first step (SPEC 5.2.1, 5.7's table: what a stated layer addresses is what its Origin covers; PLAN G338).
    private void CoveredSteps()
    {
        foreach (var input in this.covering)
        {
            var value = this.argValues[input];
            this.FirstStep(value, this.sets.Contains(value, Converted) ? UnknownView : NoView);
        }
    }

    // W's first step from one set (Referents), each holder with its local's MirWrites: on the declared route where `key` is NoView, else
    // on a converted route viewed past the value's key `key` (ViewSteps; UnknownView where none is known).
    private void FirstStep(int set, int key)
    {
        var (direct, escaped) = (false, false);
        const byte via = ViaRoot | ViaDeclared; // The declared route always; a converted one besides where `key` gives a view.
        key = key >= 0 && this.punKeys[key].FirstUnknown ? UnknownView : key;
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
                    this.Written(loan.Named, via, null, key == NoView ? NoView : key == UnknownView || loan.OwnStep ? UnknownView : EmptyView, null);
                    this.Scalars(loan, key != NoView);
                }
            }
        }

        if (escaped || !direct)
        {
            this.Written(Escaped, ViaRoot | ViaDeclared, null, NoView, null);
        }

        if (key >= 0)
        {
            this.ViewSteps(set, key, ViaRoot); // Each holder's view keys.
        }
    }

    // W(S) (A3 E16, DECISIONS R4-M73, R4-M116, R4-M147, R4-M170, R4-M177) into the targets, the holders a call's store through S writes,
    // never one that only a shared Loan names (SPEC 15.6.4 item 1): past its first step, which Referents put there first, from each
    // holder h ≠ E of W reached on the declared route whose Type passes the guard (MirWrites.Through), the holder each direct Loan not
    // shared or address of σ(h) names, and E where σ(h) carries Φ_E; from one whose guard is Named (an abstract part, SPEC 15.6.4's
    // provenance list), those holders and E where σ(h) carries Φ_W (the named step, its Shared set read at the lower level, which Through's
    // covers); from each reached on a converted route, by the callee's view (Viewed). The two facts that leave the store undecided, which
    // the call's location records (MirBorrows.Calls, .Locations), each read on the route that decides it: whether W reached on the declared
    // route a holder the guard leaves undecided (MirWrites.Limit, R4-M147), and whether, at W's fixed point, the declared step from such a
    // holder whose guard is Deep would change what W writes (Changes, R4-M153; that step is left out, and a Deep holder whose step changes
    // nothing decides nothing); a converted route steps by the view, which no guard bounds. Every store path (a Call's rows, its untied
    // stores among them; A5-F7's capture stores, unit 2c; A5-F6's desc(c) stores, 2f) takes W here, except a 4.6 to 4.9 operation's
    // referent store, which takes W's first step alone, its row's destruction composed at the call (Referents; MirBorrows.Destructions;
    // DECISIONS R4-M150).
    private (bool Limit, bool Deep) Writes(ulong[] state)
    {
        var limited = false;
        this.Step(); // Written is idempotent, so a Shared id stepped from again on the declared route writes nothing more.
        for (var i = 0; i < this.walkQueue.Count; i++)
        {
            var slot = this.walkQueue[i];
            var (kind, held, declared) = (this.writingKind[slot], state[slot], (this.writingVia[slot] & ViaDeclared) != 0);
            limited |= declared && kind == MirWrites.Limit;
            if (declared && kind is MirWrites.Through or MirWrites.Named)
            {
                this.Write(Placed(held), kind, this.holderLocals[slot]);
                if (this.Steps(Shared(held), kind == MirWrites.Through ? 1 : 0))
                {
                    this.Write(Shared(held), kind, this.holderLocals[slot]);
                }
            }

            if (slot != Escaped && this.writingView[slot] != NoView)
            {
                this.Viewed(held, slot);
            }
        }

        var deep = false;
        for (var i = 0; i < this.targets.Count && !deep; i++)
        {
            var (slot, held) = (this.targets[i], state[this.targets[i]]);
            deep = this.writingKind[slot] == MirWrites.Deep && (this.writingVia[slot] & ViaDeclared) != 0 &&
                (this.Changes(Placed(held), slot) || this.Changes(Shared(held), slot));
        }

        return (limited, deep);
    }

    // Whether W's declared step from a set past a holder `slot` whose guard is Deep, which W leaves out (Writes), would change what the
    // store writes, by Written's ways (R4-M153, R4-M154): reach a holder W does not, one W reaches only at its first step or on a converted
    // route, or one past a holder by another Place's Type; E, which takes Reached.All by either way, only where W does not reach it. A shared
    // Loan names no holder the step writes (R4-M177). A set carrying Κ makes it a converted step (Write): it changes W where it would reach a
    // holder at a view key W's reach of it lacks, or E W does not reach.
    private bool Changes(int set, int slot)
    {
        this.Work += this.sets.Count(set);
        if (this.sets.Contains(set, Converted))
        {
            var key = this.OwnKey(this.holderLocals[slot]);
            if (key < 0 || this.punKeys[key].FirstUnknown || this.writing[Escaped] != this.walks)
            {
                return true; // An unknown view, or E, which a converted step past a holder may reach (Viewed).
            }

            var scan = this.Scan(set, key);
            for (var i = this.ScanPairs(scan); i < this.ScanPairs(scan) + (2 * this.pastPool[scan + 1]); i += 2)
            {
                if (!this.HasView(this.pastPool[i], this.pastPool[i + 1]))
                {
                    return true;
                }
            }

            return false;
        }

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

    // Whether this walk reached a slot on a converted route at a view key (or at an unknown view, which covers every key).
    private bool HasView(int slot, int key)
    {
        if (this.writing[slot] != this.walks || this.writingView[slot] is NoView or EmptyView)
        {
            return false;
        }

        for (var node = this.writingView[slot]; node >= 0; node = this.viewNodes[node].Next)
        {
            if (this.viewNodes[node].Key == key)
            {
                return true;
            }
        }

        return this.writingView[slot] == UnknownView;
    }

    // A declared step of W from a set past the holder of `local` (Writes) by its guard (`past`): the holder each direct Loan not shared or
    // address names (R4-M177), and E where it carries Φ_E, past Named Φ_W. A value that may refer to storage the body does not own carries
    // Φ_E and Φ_W where it is formed (an untied result or store, a raw pointer formed untracked, an integer's pointer, a parameter and E's
    // own content at entry), so a set naming nothing writes nothing. Past an abstract part (Named) or through an own-step entry the Place's
    // Type is not the storage's, so the holder takes All (several Places). A set carrying Κ holds pointers converted where they were formed:
    // the step is a converted one besides, viewed past the holder's own Type (its local's key: no conversion lies before it; ViewSteps).
    private void Write(int set, MirWrites past, int local)
    {
        var named = past == MirWrites.Named;
        if (this.sets.Contains(set, Converted))
        {
            this.StepViewed(set, this.OwnKey(local), scalar: false); // The converted route besides; the holder at its own Type.
        }

        this.Work += this.sets.Count(set);
        foreach (var element in this.sets.Elements(set))
        {
            this.walkResting |= element is Maybe or Undecided;
            if (element == (named ? Writable : Escaped))
            {
                this.Written(Escaped, ViaPast | ViaDeclared, null, NoView, null);
            }
            else if (element >= this.loanBase &&
                this.loans[element - this.loanBase] is { Direct: true, Named: >= 0, Mode: not MirRefKind.Shared } loan)
            {
                var place = loan.Named != Escaped && !named && !loan.OwnStep ? this.PlaceType(loan) : null;
                this.Written(loan.Named, ViaPast | ViaDeclared, place, NoView, null);
            }
        }
    }

    // A converted step of W past the holder in `slot` whose state is `held`, at each of its view keys (StepViewed).
    private void Viewed(ulong held, int slot)
    {
        var scalar = this.writingScalar[slot] == this.walks;
        for (var node = this.writingView[slot]; node >= 0 || node == UnknownView; node = node >= 0 ? this.viewNodes[node].Next : EmptyView)
        {
            var key = node == UnknownView ? UnknownView : this.viewNodes[node].Key;
            this.StepViewed(Placed(held), key, scalar);
            this.StepViewed(Shared(held), key, scalar);
        }
    }

    // A converted step from a set, a state's part the callee views at the key `key` (UnknownView where none is known): the holders its
    // direct Loans not shared and addresses name, each at the key its Place's layers lead to (ViewSteps); and E, at the keys past `key`,
    // where the holder was reached through a Place holding `owner` Scalar bytes (`scalar`; SPEC 15.6 Holders: a raw pointer read from a
    // punned Place, alone or as an element of a punned Tuple or fixed array, reaches storage the body does not own when any byte it is
    // read from lies in a part of an `owner` Scalar Type, whatever else the holder carries; 15.6.4 item 4; 5.2, 5.5) or where the set
    // carries Φ_E, unless the untracked rule (MirBorrows.Addresses) may have given it: that Φ_E stands for a local that is no holder,
    // which only unit B's holder rule lets a converted route reach (PLAN F2; the declared step keeps dev's E for it). A holder an
    // own-step entry names, or any at an unknown view, takes UnknownView.
    private void StepViewed(int set, int key, bool scalar)
    {
        var unknown = key < 0 || this.punKeys[key].FirstUnknown;
        var escaped = scalar || (this.sets.Contains(set, Escaped) && !this.Untracked(set));
        this.Work += this.sets.Count(set);
        foreach (var element in this.sets.Elements(set))
        {
            this.walkResting |= element is Maybe or Undecided;
            if (element >= this.loanBase && this.loans[element - this.loanBase] is { Direct: true, Named: >= 0, Mode: not MirRefKind.Shared } loan)
            {
                this.Scalars(loan, true);
                if (unknown || loan.OwnStep || loan.Named == Escaped)
                {
                    this.Written(loan.Named, ViaView, null, UnknownView, null); // ViewSteps names neither (MirBorrows.Puns' PunEntry).
                }
            }
        }

        if (unknown)
        {
            if (escaped)
            {
                this.Written(Escaped, ViaView, null, UnknownView, null);
            }

            return;
        }

        var facts = this.punKeys[key];
        for (var j = 0; escaped && j < facts.FirstCount; j++)
        {
            var (past, type) = (this.Key(this.punPastKeys[facts.PastStart + j]), this.punViewTypes[facts.FirstStart + j]);
            if (this.IsViewStep(past, type))
            {
                this.Written(Escaped, ViaView, null, past, type); // E at each key past `key`.
            }
        }

        this.ViewSteps(set, key, ViaView);
    }

    // The callee's view of what a set's entries name, the set viewed at the key `key` (SPEC 5.7 Calls): the pun rule's walk step
    // (MirBorrows.Puns' Scan, memoized per state and key for the run), whose pairs give each holder an entry names with the Past key of `key`
    // its storage is viewed at; the one walk of that relation, which the pun rule reads too. Each holder takes its key by `via`.
    private void ViewSteps(int set, int key, byte via)
    {
        var scan = this.Scan(set, key);
        for (var i = this.ScanPairs(scan); i < this.ScanPairs(scan) + (2 * this.pastPool[scan + 1]); i += 2)
        {
            var (slot, past) = (this.pastPool[i], this.pastPool[i + 1]);
            this.Written(slot, via, null, past, this.ViewType(key, past));
        }
    }

    // Marks the holder a direct Loan or address names where it is reached on a converted route through a Place whose Type holds an `owner`
    // Scalar (MirBorrows.PunTypes' Scalared): a pointer the callee reads there may be read from such bytes (SPEC 15.6 Holders).
    private void Scalars(in Loan loan, bool converted)
    {
        if (converted && loan.Named != Escaped && this.writingScalar[loan.Named] != this.walks && this.Scalared(cases.TypeOf(loan.Place)))
        {
            this.writingScalar[loan.Named] = this.walks;
        }
    }

    // A scan's first pair in pastPool (MirBorrows.Puns' Scan layout: two counts, the write pairs, the walk pairs).
    private int ScanPairs(int scan) => scan + 2 + (2 * this.pastPool[scan]);

    // The pun key of a local's own Type at its root (the view of a holder reached on the declared route), -1 for none.
    private int OwnKey(int local) => local < 0 ? -1 : this.Key(PlaceKey.Root(cases.Type(this.body.Locals[local].Type)));

    // The Type a key's storage is viewed at, from the key that leads to it (its Past keys align with its write Types), else null.
    private BoundType? ViewType(int from, int key)
    {
        var facts = this.punKeys[from];
        for (var j = 0; j < facts.FirstCount; j++)
        {
            if (this.Key(this.punPastKeys[facts.PastStart + j]) == key)
            {
                return this.punViewTypes[facts.FirstStart + j];
            }
        }

        return null;
    }

    // A holder W reaches, with its local's MirWrites (E: none) and how it was reached (past a holder on the declared route, with the erased
    // case Type of the Place the Loan names, `place`, the storage the call writes there, none for several; DECISIONS R4-M154, Revision 8;
    // on a converted route, the view key `view` with the Type it views the storage at, `type`: a pun key, UnknownView, EmptyView (none
    // yet), or NoView (no converted route)); queued for its step where first reached or where its route facts grow.
    private void Written(int slot, byte via, BoundType? place, int view, BoundType? type)
    {
        var local = this.holderLocals[slot];
        var kind = local < 0 ? MirWrites.None : this.writable[local];
        var (oldVia, grew) = (this.writingVia[slot], false);
        if (this.writing[slot] != this.walks)
        {
            (this.writing[slot], this.writingVia[slot], this.writingPlace[slot], this.writingView[slot]) = (this.walks, 0, default, NoView);
            (oldVia, grew) = (0, true);
            this.targets.Add(slot);
        }

        (this.writingKind[slot], this.writingVia[slot]) = (kind, (byte)(this.writingVia[slot] | via));
        if ((via & ViaPast) != 0 && this.writingPlace[slot] is var (known, several) && !several && (place is null || !ReferenceEquals(known, place)))
        {
            this.writingPlace[slot] = place is null ? (null, true) : (known ?? place, known is not null);
        }

        grew |= view != NoView && this.Views(slot, view, type);
        if (grew || ((oldVia ^ this.writingVia[slot]) & ViaDeclared) != 0)
        {
            this.walkQueue.Add(slot);
        }
    }

    // Joins a view key and its Type (none where no key leads to it: an unknown view) into a slot's view keys; whether they grew. A slot's keys are
    // pun keys of the walk's input Type and of the holders' own Types (OwnKey) and the Past keys they lead to, each once: the list is bounded
    // by those Types' keys, which PunKeys memoizes per program version, so no cap.
    private bool Views(int slot, int key, BoundType? type)
    {
        var head = this.writingView[slot];
        if (head == UnknownView || key == EmptyView)
        {
            if (head == NoView)
            {
                this.writingView[slot] = EmptyView;
                return true;
            }

            return false;
        }

        if (key == UnknownView)
        {
            this.writingView[slot] = UnknownView;
            return true;
        }

        for (var node = head; node >= 0; node = this.viewNodes[node].Next)
        {
            if (this.viewNodes[node].Key == key)
            {
                return false;
            }
        }

        this.viewNodes.Add((key, type, head >= 0 ? head : -1));
        this.writingView[slot] = this.viewNodes.Count - 1;
        return true;
    }

    // Whether a Past key is a step of the callee's view: its layers lead on, or its Type holds a Loan, which a store there may keep.
    private bool IsViewStep(int key, BoundType type) => this.punKeys[key] is { FirstCount: > 0 } or { FirstUnknown: true } || this.Holds(types.Erase(type), out _);

    // A direct Loan's Place's case Type under the view, Origin-erased (TyCtxt.Erase): the storage a call's write past a holder reaches
    // through it, whose Reached entry the holder receives (MirBorrows.Calls).
    private BoundType PlaceType(in Loan loan) => types.Erase(cases.TypeOf(loan.Place));
}
