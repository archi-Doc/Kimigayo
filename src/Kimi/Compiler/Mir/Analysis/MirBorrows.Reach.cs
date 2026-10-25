// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's reach on a state (A3 5's content and refs, E16's W, DECISIONS R4-M73, R4-M116): the holders a set refers to (those its direct
// Loans and addresses name, and E where it carries Φ_E or no direct one), its content, the holders a call's store through it writes, and
// the guard's per-local MirWrites (MirTypes.HoldsLoans). On the state a location's plan reads, a set's content is read once (E13 (5)),
// however many operands and retention rows read it. Must not form a value or a Loan (MirBorrows.Values).
internal sealed partial class MirBorrows
{
    private readonly List<int> targets = []; // The holders a reference's value refers to (refs), or a call's store writes (W).
    private readonly List<int> contentsRead = []; // The sets whose content the location read, reset by Plan.
    private readonly Dictionary<BoundType, byte> walked = new(ReferenceEqualityComparer.Instance); // HoldsLoans' scratch.
    private int[] contents = []; // By set: its content at the location plus 1 (0: not read there).
    private MirWrites[] writable = []; // By local: what a call may write past it (MirTypes.HoldsLoans).
    private int[] writing = []; // By slot: the W walk that reached it.
    private MirWrites[] writingKind = []; // By slot: its local's MirWrites, at that walk.
    private int walks; // The W walks made.

    // content(S) (A3 5): the states of the holders its direct Loans name, and E's where it carries Φ_E or no direct Loan; memoized at the
    // location.
    private int Content(ulong[] state, int set)
    {
        if (set < this.contents.Length && this.contents[set] != 0)
        {
            return this.contents[set] - 1;
        }

        var (content, direct, escaped) = (0, false, false);
        this.Work += this.sets.Count(set);
        foreach (var element in this.sets.Elements(set))
        {
            escaped |= element == Escaped;
            if (element >= this.loanBase && this.loans[element - this.loanBase] is { Direct: true } loan)
            {
                direct = true;
                content = loan.Named >= 0 ? this.sets.Union(content, (int)state[loan.Named]) : content;
            }
        }

        content = escaped || !direct ? this.sets.Union(content, (int)state[Escaped]) : content;
        MirDataflow.Grow(ref this.contents, set + 1);
        this.contents[set] = content + 1;
        this.contentsRead.Add(set);
        return content;
    }

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

    // Whether a local's case Type may hold a Loan, with what a call may write past it (MirTypes.HoldsLoans, one walk).
    private bool Holding(int local)
    {
        MirDataflow.Grow(ref this.writable, this.body.Locals.Length);
        return MirTypes.HoldsLoans(cases.Type(this.body.Locals[local].Type), types, this.walked, out this.writable[local]);
    }

    // W(S) (A3 E16, DECISIONS R4-M73, R4-M116) into the targets, the holders a call's store through S writes: refs(S) (E14); and, from each
    // holder h ≠ E of W whose Type passes the guard, the holder each direct Loan or address of σ(h) names, in any mode, and E where σ(h)
    // carries Φ_E. The first pass steps from the holders that pass it concretely (MirWrites.Through), the second from those that pass it
    // only through a Type argument, a pair layer, an abstract part or a stored raw layer stepping Exclusive (MirWrites.ThroughArgument) and
    // from what only they reach: those targets come last, from the index returned (the count where none), and a Call that stores a
    // non-empty value into one holds its view (P13's interim, R4-M73). Every store path (a Call's rows, its untied stores among them;
    // A5-F7's capture stores, unit 2c; A5-F6's desc(c) stores, 2f) takes W here and sets the hold so.
    private int Writes(ulong[] state, int set)
    {
        this.Refs(set);
        MirDataflow.Grow(ref this.writing, this.holderCount);
        MirDataflow.Grow(ref this.writingKind, this.holderCount);
        this.walks++;
        var count = 0;
        for (var i = 0; i < this.targets.Count; i++)
        {
            if (this.targets[i] is var target && this.writing[target] != this.walks)
            {
                (this.writing[target], this.writingKind[target]) = (this.walks, MirWrites.None);
                this.targets[count++] = target;
            }
        }

        this.targets.RemoveRange(count, this.targets.Count - count);
        this.Write(set); // The kinds of refs(S)'s holders.
        var clean = int.MaxValue;
        var argument = false;
        for (var pass = 0; pass == 0 || (pass == 1 && argument); pass++)
        {
            for (var i = 0; i < this.targets.Count; i++)
            {
                var kind = this.writingKind[this.targets[i]];
                argument |= kind == MirWrites.ThroughArgument;
                if (kind == MirWrites.Through ? pass == 0 || i >= clean : kind == MirWrites.ThroughArgument && pass == 1)
                {
                    this.Write((int)state[this.targets[i]]);
                }
            }

            clean = pass == 0 ? this.targets.Count : clean;
        }

        return clean;
    }

    // A step of W from a set (Writes): E where it carries Φ_E, and the holder each direct Loan or address names. A value that may refer to
    // storage the body does not own carries Φ_E where it is formed (an untied result or store, a raw pointer formed untracked, an integer's
    // pointer, a parameter and E's own content at entry), so a set naming nothing writes nothing.
    private void Write(int set)
    {
        this.Work += this.sets.Count(set);
        foreach (var element in this.sets.Elements(set))
        {
            if (element == Escaped)
            {
                this.Written(Escaped, MirWrites.None);
            }
            else if (element >= this.loanBase && this.loans[element - this.loanBase] is { Direct: true, Named: >= 0 } loan)
            {
                this.Written(loan.Named, loan.Named == Escaped ? MirWrites.None : this.writable[loan.Root]); // A static's names E.
            }
        }
    }

    // A holder W reaches, with its local's MirWrites (E: none).
    private void Written(int slot, MirWrites kind)
    {
        if (this.writing[slot] != this.walks)
        {
            this.writing[slot] = this.walks;
            this.targets.Add(slot);
        }

        this.writingKind[slot] = kind;
    }
}
