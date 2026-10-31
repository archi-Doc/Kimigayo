// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;

namespace Kimi.Compiler.Mir;

// A5's reach on a state (A3 5's content and refs, E16's W, DECISIONS R4-M73, R4-M116, R4-M147): the holders a set refers to (those its
// direct Loans and addresses name, and E where it carries Φ_E or no direct one), its content, the holders a call's store through it writes
// (W, whose first step alone is Referents), and the guard's per-local MirWrites (MirTypes.HoldsLoans); its chain (SPEC 15.6.4's "what any
// chain of reads through it carries", a Chain row's measure), the least fixed point of a read from it, each holder read once per closure.
// On the state a location's plan reads, a set's content and chain are each read once (E13 (5)), however many operands and retention rows
// read them; whether a local is a holder, and its MirWrites, are walked once per distinct case Type and program version (the holding memo,
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
    private readonly List<int> read = []; // The sets whose content or chain the location read, reset by Plan.
    private readonly List<int> chained = []; // A closure's holders to read, each queued once (Chain).
    private readonly List<int> parts = []; // The states a closure read, each through rule W's clause, which one n-ary union joins.
    private readonly Dictionary<MirHoldingKey, MirModeDepths> walked = new(); // HoldsLoans' scratch.
    private readonly List<RawMember> members = []; // HoldsLoans' scratch for a stored raw layer's members.
    // Holding's memo: each case Type's answer for the program version holdingVersion names, by reference.
    private readonly Dictionary<BoundType, (bool Holds, MirWrites Writes)> holding = new(HoldingCapacity, ReferenceEqualityComparer.Instance);
    private (int Content, int Chain)[] reads = []; // By set: its content and its chain at the location, each plus 1 (0: not read there).
    private int[] queuedBy = []; // By holder: the closure that queued it.
    private int[] partlyBy = []; // By holder: the closure that reached it through a part or E (rule W's read-through clause).
    private int closures; // The closures made (Chain).
    private MirWrites[] writable = []; // By local: what a call may write past it (MirTypes.HoldsLoans).
    private int[] writing = []; // By slot: the W walk that reached it.
    private MirWrites[] writingKind = []; // By slot: its local's MirWrites, at that walk.
    private byte[] writingVia = []; // By slot: how that walk reached it (ViaRoot, ViaPast).
    private (BoundType? Type, bool Several)[] writingPlace = []; // By slot: the erased case Type of the Places named by the Loans by
                                                                 // which that walk reached it past a holder (PlaceType), or several.
    private int walks; // The W walks made.
    private ulong holdingVersion; // The program version whose case Types the holding memo answers (0: none).

    // Whether a direct Loan's Place is its holder's whole value, through which rule W's read-through clause reads a Copy (ReadThrough).
    private static bool Whole(in Loan loan) => loan.Count == 0;

    // content(S) (A3 5): the states of the holders its direct Loans name, and E's where it carries Φ_E or no direct Loan, each read through
    // rule W's clause (ReadThrough); memoized at the location.
    private int Content(ulong[] state, int set)
    {
        if (set < this.reads.Length && this.reads[set].Content != 0)
        {
            return this.reads[set].Content - 1;
        }

        var (content, direct, escaped) = (0, false, false);
        this.Work += this.sets.Count(set);
        foreach (var element in this.sets.Elements(set))
        {
            escaped |= element == Escaped;
            if (element >= this.loanBase && this.loans[element - this.loanBase] is { Direct: true } loan)
            {
                direct = true;
                content = loan.Named >= 0 ? this.sets.Union(content, this.ReadThrough(state, loan.Named, Whole(loan))) : content;
            }
        }

        content = escaped || !direct ? this.sets.Union(content, this.ReadThrough(state, Escaped, whole: false)) : content;
        this.Memo(set).Content = content + 1;
        return content;
    }

    // chain(S) (SPEC 15.6.4; DECISIONS BU-40, R4-M138): the least fixed point of a read from S, the states of the holders content(S) reads,
    // then of each holder a state read names by a direct Loan or address, and E's where that state carries Φ_E; each holder is queued once
    // per closure, so a pointer cycle ends, and read through rule W's clause as a whole value only where every reach of it was one
    // (ReadThrough); the states are joined by one n-ary union (MirLoanSets.Union); memoized at the location.
    private int Chain(ulong[] state, int set)
    {
        if (set < this.reads.Length && this.reads[set].Chain != 0)
        {
            return this.reads[set].Chain - 1;
        }

        this.Closing();
        if (!this.Queue(set))
        {
            this.Queued(Escaped, whole: false); // content's rule: E where S carries no direct Loan.
        }

        var chain = this.Closure(state);
        this.Memo(set).Chain = chain + 1;
        return chain;
    }

    // A new closure, before its seeds are queued (Chain's set seed, or a destroyed Place's holder).
    private void Closing()
    {
        MirDataflow.Grow(ref this.queuedBy, this.holderCount);
        MirDataflow.Grow(ref this.partlyBy, this.holderCount);
        this.closures++;
        this.chained.Clear();
    }

    // The closure's reach from its queued seeds: each holder a state read names is queued once, then every queued holder read through
    // rule W's clause, joined by one n-ary union.
    private int Closure(ulong[] state)
    {
        for (var i = 0; i < this.chained.Count; i++)
        {
            this.Queue((int)state[this.chained[i]]);
        }

        this.parts.Clear();
        foreach (var holder in this.chained)
        {
            this.parts.Add(this.ReadThrough(state, holder, whole: this.partlyBy[holder] != this.closures));
        }

        return this.sets.Union(CollectionsMarshal.AsSpan(this.parts));
    }

    // Queues for Chain the holders a set names, those its direct Loans and addresses name and E where it carries Φ_E; whether it has a
    // direct one.
    private bool Queue(int set)
    {
        var direct = false;
        this.Work += this.sets.Count(set);
        foreach (var element in this.sets.Elements(set))
        {
            if (element == Escaped)
            {
                this.Queued(Escaped, whole: false);
            }
            else if (element >= this.loanBase && this.loans[element - this.loanBase] is { Direct: true } loan)
            {
                direct = true;
                if (loan.Named >= 0)
                {
                    this.Queued(loan.Named, Whole(loan));
                }
            }
        }

        return direct;
    }

    // A holder Chain reads, once per closure, and whether a reach of it was through a part or through E (a may-fact, joined).
    private void Queued(int holder, bool whole)
    {
        if (this.queuedBy[holder] != this.closures)
        {
            this.queuedBy[holder] = this.closures;
            this.chained.Add(holder);
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
    // to a part of it, or through E, not (Indirect, MirBorrows.Causes).
    private int ReadThrough(ulong[] state, int holder, bool whole) => whole ? (int)state[holder] : this.Indirect((int)state[holder]);

    // refs(S) (A3 5) into the targets: the holders its direct Loans name, and E where it carries Φ_E or no direct Loan.
    private void Refs(int set)
    {
        var (direct, escaped) = (false, false);
        this.targets.Clear();
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
                }
            }
        }

        if (escaped || !direct)
        {
            this.targets.Add(Escaped);
        }
    }

    // Whether a local's case Type may hold a Loan, with what a call may write past it (MirTypes.HoldsLoans, one walk per distinct case Type
    // and program version): the answer is a function of the interned Type and of Binding's stored raw steps (TyCtxt.RawStep), which a
    // version fixes, so the memo is cleared when the program's version changes (DECISIONS R4-M140).
    private bool Holding(int local)
    {
        MirDataflow.Grow(ref this.writable, this.body.Locals.Length);
        if (this.holdingVersion != this.program.Version)
        {
            this.holdingVersion = this.program.Version;
            this.holding.Clear();
        }

        var type = cases.Type(this.body.Locals[local].Type);
        ref var known = ref CollectionsMarshal.GetValueRefOrAddDefault(this.holding, type, out var exists);
        if (!exists)
        {
            known.Holds = MirTypes.HoldsLoans(type, types, this.walked, this.members, out known.Writes);
        }

        this.writable[local] = known.Writes;
        return known.Holds;
    }

    // W's first step (A3 E14): refs(S) into the targets, each holder once, at a new W walk; their count. A referent store's whole reach
    // (MirBorrows.Calls).
    private int Referents(int set)
    {
        this.Refs(set);
        return this.Walking();
    }

    // A new W walk from the holders in the targets (W's seeds: refs(S), or a destroyed Place's holder), each once; their count.
    private int Walking()
    {
        MirDataflow.Grow(ref this.writing, this.holderCount);
        MirDataflow.Grow(ref this.writingKind, this.holderCount);
        MirDataflow.Grow(ref this.writingVia, this.holderCount);
        MirDataflow.Grow(ref this.writingPlace, this.holderCount);
        this.walks++;
        var count = 0;
        for (var i = 0; i < this.targets.Count; i++)
        {
            if (this.targets[i] is var target && this.writing[target] != this.walks)
            {
                (this.writing[target], this.writingKind[target], this.writingVia[target]) = (this.walks, MirWrites.None, ViaRoot);
                this.writingPlace[target] = default;
                this.targets[count++] = target;
            }
        }

        this.targets.RemoveRange(count, this.targets.Count - count);
        return count;
    }

    // W(S) (A3 E16, DECISIONS R4-M73, R4-M116, R4-M147) into the targets, the holders a call's store through S writes: past refs(S), which
    // Referents put there first, from each holder h ≠ E of W whose Type passes the guard (MirWrites.Through), the holder each direct Loan or
    // address of σ(h) names, in any mode, and E where σ(h) carries Φ_E. The two facts that leave the store undecided, which the call's
    // location records (MirBorrows.Calls, .Locations): whether W reached a holder the guard leaves undecided (MirWrites.Limit, R4-M147),
    // and whether, at W's fixed point, the step from a holder whose guard is Deep would change what W writes (Changes, R4-M153; that step
    // is left out, and a Deep holder whose step changes nothing decides nothing). Every store path (a Call's rows, its untied stores
    // among them; A5-F7's capture stores, unit 2c; A5-F6's desc(c) stores, 2f) takes W here, except a 4.6 to 4.9 operation's referent store
    // whose referent destroys shallowly, which takes refs(S) alone (Referents; DECISIONS R4-M143).
    private (bool Limit, bool Deep) Writes(ulong[] state, int set)
    {
        this.Write(set, ViaRoot); // The kinds of refs(S)'s holders, which Referents put in the targets.
        return this.Passes(state);
    }

    // W's passes past its seeds (Writes'), each holder's kind set: the holders past each one whose guard passes, then the two facts.
    private (bool Limit, bool Deep) Passes(ulong[] state)
    {
        var limited = false;
        for (var i = 0; i < this.targets.Count; i++)
        {
            var kind = this.writingKind[this.targets[i]];
            limited |= kind == MirWrites.Limit;
            if (kind == MirWrites.Through)
            {
                this.Write((int)state[this.targets[i]], ViaPast);
            }
        }

        var deep = false;
        for (var i = 0; i < this.targets.Count && !deep; i++)
        {
            deep = this.writingKind[this.targets[i]] == MirWrites.Deep && this.Changes((int)state[this.targets[i]]);
        }

        return (limited, deep);
    }

    // Whether W's step from a set past a holder whose guard is Deep, which W leaves out (Writes), would change what the store writes, by
    // Written's ways (R4-M153, R4-M154): reach a holder W does not, one W reaches only at its first step, or one past a holder by another
    // Place's Type; E, which takes Reached.All by either way, only where W does not reach it.
    private bool Changes(int set)
    {
        this.Work += this.sets.Count(set);
        foreach (var element in this.sets.Elements(set))
        {
            if (element == Escaped ? this.writing[Escaped] != this.walks :
                element >= this.loanBase && this.loans[element - this.loanBase] is { Direct: true, Named: >= 0 } loan && this.Changes(loan))
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

    // A step of W from a set (Writes), at its first step (ViaRoot) or past a holder (ViaPast): E where it carries Φ_E, and the holder each direct
    // Loan or address names. A value that may refer to storage the body does not own carries Φ_E where it is formed (an untied result or
    // store, a raw pointer formed untracked, an integer's pointer, a parameter and E's own content at entry), so a set naming nothing
    // writes nothing.
    private void Write(int set, byte via)
    {
        this.Work += this.sets.Count(set);
        foreach (var element in this.sets.Elements(set))
        {
            if (element == Escaped)
            {
                this.Written(Escaped, MirWrites.None, via, null);
            }
            else if (element >= this.loanBase && this.loans[element - this.loanBase] is { Direct: true, Named: >= 0 } loan)
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
    private BoundType PlaceType(in Loan loan) => types.Erase(cases.Type(this.body.TypeOf(loan.Place)!));
}
