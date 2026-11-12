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
// read them; in a W run the memo is one slot per set that also keeps the access Type it was read for (Whole reads it), so a read of the
// set under another access Type at the location recomputes and replaces it: each read costs at most one computation, as with no memo.
// Whether a local is a holder, and its MirWrites, are walked once per distinct case Type and program version (the holding memo,
// DECISIONS R4-M140). Must not form a value or a Loan (MirBorrows.Values).
internal sealed partial class MirBorrows
{
    // The holding memo's presized capacity: a compilation's distinct case Types of holder queries, the library's 339 and a program's own
    // (339 to 461, median 346, on the G258 corpus); a larger program grows it once.
    private const int HoldingCapacity = 384;

    // How a W walk reached a holder (writingVia, DECISIONS R4-M154): at its first step (ViaRoot) (the argument's direct Loans or addresses name it, or
    // E there), past another holder, or both.
    private const byte ViaRoot = 1;
    private const byte ViaPast = 2;

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
    private (BoundType? Content, BoundType? Chain)[] accessed = []; // By set, written in a W run only: the access Types reads answers.
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
    private int walks; // The W walks made.
    private bool walkResting; // Whether the W walk read a set that rests on an undecided write (Φ_M, Φ_U; MirBorrows.Puns).
    private ulong holdingVersion; // The program version whose case Types the holding memo answers (0: none).

    // A Semantics Type's value or referent Type, its first component: through a borrow or raw pointer the Type an access designates (SPEC
    // 5.2, 15.6), the receiving part of a store through it (MirBorrows.Causes' Fixed). Of an owning or object handle (`owner/T` in a pair
    // case, `obj/T`, `rc/T`, `arc/T`) it is the value's or payload's Type, one layer inside what the handle's entries name: Whole then
    // answers "not whole" or what the Place alone answers, never a whole value the Place rule would not. Null for any other Type (a view
    // handle, an aggregate): no access Type is established.
    private static BoundType? Referent(BoundType pointer) => pointer is { Kind: BoundTypeKind.Semantics, Components: [var to, ..] } ? to : null;

    // Whether an access of Type `access` through a direct Loan or address designates its holder's whole value: rule W's read-through
    // clause reads a Copy through it (Parts), a write through it is a Copy into the holder (WholeTarget; SPEC 15.6.5). In a W run its
    // Place is the holder's root and the access's Type is that Place's (SPEC 5.2: an access designates the Place of its Type at its
    // address, Origins treated as equal): a pointer Cast or a displacement keeps the entry and may move the access onto a part (4.1, 5.3),
    // and a reborrow or an address formed through a Deref carries the entry as a parent (15.6) and designates the Place its path names, a
    // part where the path steps past the Deref. Type identity decides a parent only while that path stays in the holder's storage: past
    // an own step a Place of the holder's own Type may lie (a recursive Type), read whole here (PLAN G783's open row). Where no access
    // Type is established (null: an aggregate's or a view handle's input, a Chain read past E or a holder that is no pointer), not whole.
    // Outside a W run the Type is not read (MirBorrows.Causes: no query).
    private bool Whole(in Loan loan, BoundType? access)
        => loan.Count == 0 && (this.wCount == 0 || (access is not null && types.SameStructure(cases.TypeOf(loan.Place), access)));

    // The access Type of a Chain read past a holder (Whole): a pointer stored in a holder whose local is one borrow or raw pointer is of
    // the local's Type; E and any other holder hold pointers of Types A5 does not track (null). Read in a W run only.
    private BoundType? PastAccess(int holder)
        => this.wCount > 0 && this.holderLocal[holder] is >= 0 and var local ? Referent(cases.TypeOf(new MirPlace(local))) : null;

    // content(S) (A3 5) for an access of Type `access` through S: the states of the holders its direct Loans name, and E's where it carries
    // Φ_E or no direct Loan, each read through rule W's clause (Parts), a shared Loan's holder and E for a set without Φ_W read-only, each
    // distinct Shared set once, joined by one n-ary union (PartsUnion); memoized at the location (in a W run, for its access Type).
    private int Content(ulong[] state, int set, BoundType? access)
    {
        if (set < this.reads.Length && this.reads[set].Content != 0 && (this.wCount == 0 || ReferenceEquals(this.accessed[set].Content, access)))
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
                    this.Parts(state[loan.Named], this.Whole(loan, access), loan.Mode != MirRefKind.Shared);
                }
            }
        }

        if (escaped || !direct)
        {
            this.Parts(state[Escaped], whole: false, writable);
        }

        var content = this.Rested(this.PartsUnion(), set);
        this.Memo(set).Content = content + 1;
        if (this.wCount > 0)
        {
            this.accessed[set].Content = access;
        }

        return content;
    }

    // chain(S) (SPEC 15.6.4; DECISIONS BU-40, R4-M138): the least fixed point of a read from S, the states of the holders content(S) reads,
    // then of each holder a state read names by a direct Loan or address, and E's where that state carries Φ_E; each holder is queued once
    // per closure, so a pointer cycle ends, again where a reach of it is the first through no shared Loan (a Shared set read once per
    // closure at each of the two levels, the writable one covering the other), and read through rule W's clause as a whole value only where
    // every reach of it was one, read-only where none was through no shared Loan (Parts); the states are joined by one n-ary union
    // (PartsUnion); memoized at the location (in a W run, for the access Type of its first read, `access`; a read past a holder reads
    // through the pointers stored in it, PastAccess).
    private int Chain(ulong[] state, int set, BoundType? access)
    {
        if (set < this.reads.Length && this.reads[set].Chain != 0 && (this.wCount == 0 || ReferenceEquals(this.accessed[set].Chain, access)))
        {
            return this.reads[set].Chain - 1;
        }

        MirDataflow.Grow(ref this.queuedBy, this.holderCount);
        MirDataflow.Grow(ref this.partlyBy, this.holderCount);
        MirDataflow.Grow(ref this.writableBy, this.holderCount);
        this.closures++;
        this.chained.Clear();
        if (!this.Queue(set, writable: true, access))
        {
            this.Queued(Escaped, whole: false, writable: false); // content's rule: E where S carries no direct Loan (Queue read its Φ_W).
        }

        this.Step();
        for (var i = 0; i < this.chained.Count; i++)
        {
            var holder = this.chained[i];
            var (writable, past) = (this.writableBy[holder] == this.closures, this.PastAccess(holder));
            this.Queue(Placed(state[holder]), writable, past);
            if (this.Steps(Shared(state[holder]), writable ? 1 : 0))
            {
                this.Queue(Shared(state[holder]), writable, past);
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
        if (this.wCount > 0)
        {
            this.accessed[set].Chain = access;
        }

        return chain;
    }

    // Queues for Chain the holders a set names, those its direct Loans and addresses name and E where it carries Φ_E, each reached through
    // no shared Loan where the set was (`writable`) and its Loan is not shared, E where the set carries Φ_W, as a whole value where an
    // access of Type `access` through it designates one (Whole); whether it has a direct one.
    private bool Queue(int set, bool writable, BoundType? access)
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
                    this.Queued(loan.Named, this.Whole(loan, access), writable && loan.Mode != MirRefKind.Shared);
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
        if (this.wCount > 0)
        {
            MirDataflow.Grow(ref this.accessed, set + 1); // The access Types it answers (Content, Chain).
        }

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
        MirDataflow.Grow(ref this.writingKind, this.holderCount);
        MirDataflow.Grow(ref this.writingVia, this.holderCount);
        MirDataflow.Grow(ref this.writingPlace, this.holderCount);
        this.walks++;
        this.walkResting = false;
        this.targets.Clear();
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
                    this.Written(loan.Named, MirWrites.None, ViaRoot, null);
                }
            }
        }

        if (escaped || !direct)
        {
            this.Written(Escaped, MirWrites.None, ViaRoot, null);
        }
    }

    // W(S) (A3 E16, DECISIONS R4-M73, R4-M116, R4-M147, R4-M170, R4-M177) into the targets, the holders a call's store through S writes,
    // never one that only a shared Loan names (SPEC 15.6.4 item 1): past its first step, which Referents put there first, from each
    // holder h ≠ E of W whose Type passes the guard (MirWrites.Through), the holder each direct Loan not shared or address of σ(h) names,
    // and E where σ(h) carries Φ_E; from one whose guard is Named (an abstract part, SPEC 15.6.4's provenance list), those holders and E
    // where σ(h) carries Φ_W (the named step, its Shared set read at the lower level, which Through's covers). The two facts that leave
    // the store undecided, which the call's location records (MirBorrows.Calls, .Locations): whether W reached a holder the guard leaves
    // undecided (MirWrites.Limit, R4-M147), and whether, at W's fixed point, the step from a holder whose guard is Deep would change what
    // W writes (Changes, R4-M153; that step is left out, and a Deep holder whose step changes nothing decides nothing). Every store path (a
    // Call's rows, its untied stores among them; A5-F7's capture stores, unit 2c; A5-F6's desc(c) stores, 2f) takes W here, except a 4.6
    // to 4.9 operation's referent store, which takes W's first step alone, its row's destruction composed at the call (Referents;
    // MirBorrows.Destructions; DECISIONS R4-M150).
    private (bool Limit, bool Deep) Writes(ulong[] state, int set)
    {
        this.Write(set, ViaRoot, MirWrites.Through); // The kinds of the first step's holders, which Referents put in the targets.
        foreach (var input in this.covering)
        {
            this.Write(this.argValues[input], ViaRoot, MirWrites.Through);
        }

        var limited = false;
        this.Step(); // Written is idempotent, so a Shared id stepped from again writes nothing more.
        for (var i = 0; i < this.targets.Count; i++)
        {
            var kind = this.writingKind[this.targets[i]];
            var held = state[this.targets[i]];
            limited |= kind == MirWrites.Limit;
            if (kind is MirWrites.Through or MirWrites.Named)
            {
                this.Write(Placed(held), ViaPast, kind);
                if (this.Steps(Shared(held), kind == MirWrites.Through ? 1 : 0))
                {
                    this.Write(Shared(held), ViaPast, kind);
                }
            }
        }

        var deep = false;
        for (var i = 0; i < this.targets.Count && !deep; i++)
        {
            var held = state[this.targets[i]];
            deep = this.writingKind[this.targets[i]] == MirWrites.Deep && (this.Changes(Placed(held)) || this.Changes(Shared(held)));
        }

        return (limited, deep);
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
                this.Written(Escaped, MirWrites.None, via, null);
            }
            else if (element >= this.loanBase &&
                this.loans[element - this.loanBase] is { Direct: true, Named: >= 0, Mode: not MirRefKind.Shared } loan)
            {
                var place = via == ViaPast && loan.Named != Escaped ? this.PlaceType(loan) : null;
                this.Written(loan.Named, loan.Named == Escaped ? MirWrites.None : this.writable[loan.Root], via, place); // A static's names E.
            }
        }
    }

    // A holder W reaches, with its local's MirWrites (E: none) and how it was reached (past a holder, with the erased case Type of the Place
    // the Loan names, `place`, the storage the call writes there; DECISIONS R4-M154, Revision 8).
    private void Written(int slot, MirWrites kind, byte via, BoundType? place)
    {
        if (this.writing[slot] != this.walks)
        {
            (this.writing[slot], this.writingVia[slot], this.writingPlace[slot]) = (this.walks, 0, default);
            this.targets.Add(slot);
        }

        (this.writingKind[slot], this.writingVia[slot]) = (kind, (byte)(this.writingVia[slot] | via));
        if (place is not null && this.writingPlace[slot] is var (known, several) && !several && !ReferenceEquals(known, place))
        {
            this.writingPlace[slot] = (known ?? place, known is not null);
        }
    }

    // A direct Loan's Place's case Type under the view, Origin-erased (TyCtxt.Erase): the storage a call's write past a holder reaches
    // through it, whose Reached entry the holder receives (MirBorrows.Calls).
    private BoundType PlaceType(in Loan loan) => types.Erase(cases.TypeOf(loan.Place));
}
