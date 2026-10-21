// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's values on a state (A3 5's val, issued, content and refs, with E3 and E16): what an rvalue, an operand or a Place carries, a
// borrow's parents and Loan, the holders a set refers to, and those a call's store through it writes (W). On the state a location's plan
// reads, a set's content is read once (E13 (5)), however many operands and retention rows read it.
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

    // val(rv) (A3 5) and the Loans it issues: an operand's, a Ref's, the union of an aggregate's or a value-keeping Cast's operands, an Opaque
    // site's {Φ_E} (R4-M70: the site rests on a recorded error, so a problem only its roots' Loans would give never arises); under the view
    // an Adapt is the rvalue its case selects (none: its operand's value).
    private (int Value, int Own) Value(ulong[] state, in MirRvalue rvalue, int location)
    {
        switch (rvalue.Kind)
        {
            case MirRvalueKind.Use:
            case MirRvalueKind.Adapt:
            case MirRvalueKind.Cast when (MirCastKind)rvalue.Op is MirCastKind.Erasure or MirCastKind.Upcast or MirCastKind.Pointer:
                return (this.Operand(state, rvalue.Start), 0);
            case MirRvalueKind.Ref when (MirRefKind)rvalue.Op != MirRefKind.Raw:
                return this.Issued(state, rvalue.Place, reborrow: false, this.loanAt[location]);
            case MirRvalueKind.Aggregate:
                var value = 0;
                for (var o = rvalue.Start; o < rvalue.Start + rvalue.Count; o++)
                {
                    value = this.sets.Union(value, this.Operand(state, o));
                }

                return (value, 0);
            case MirRvalueKind.Opaque:
                return (this.escapedSet, 0);
            default:
                return (0, 0);
        }
    }

    // An operand's value: a constant's none, a Place's read (a PairAcquire's that Copies; one that Reborrows is its Use's issue, Plan).
    private int Operand(ulong[] state, int index)
        => this.body.Operands[index] is { Kind: not MirOperandKind.Const } operand ? this.Read(state, operand.Place) : 0;

    // A root's state: a holder's, a static's {Φ_E}, none for another local.
    private int Root(ulong[] state, int root)
    {
        if (root < 0)
        {
            return this.escapedSet;
        }

        return this.slots[root] >= 0 ? (int)state[this.slots[root]] : 0;
    }

    // val(X) = content^k(σ(root(X))) over its k borrow Derefs; a raw pointer's referent is {Φ_E}.
    private int Read(ulong[] state, MirPlace place)
    {
        var value = this.Root(state, place.Root);
        var count = this.placeSteps.Walk(this.body, place, reborrow: false, ref this.walk);
        for (var i = 0; i < count; i++)
        {
            value = this.walk[i].Class switch
            {
                StepClass.Shared or StepClass.Uniq => this.Content(state, value),
                StepClass.Raw => this.escapedSet,
                _ => value,
            };
        }

        return value;
    }

    // A borrow's value (A3 5 issued, E3): its Loan with the parents of every borrow layer it follows, from the last shared layer on.
    private (int Value, int Own) Issued(ulong[] state, MirPlace place, bool reborrow, int loan)
    {
        var (layer, parents) = (this.Root(state, place.Root), 0);
        var count = this.placeSteps.Walk(this.body, place, reborrow, ref this.walk);
        for (var i = 0; i < count; i++)
        {
            switch (this.walk[i].Class)
            {
                case StepClass.Shared:
                    (parents, layer) = (layer, this.Content(state, layer));
                    break;
                case StepClass.Uniq:
                    (parents, layer) = (this.sets.Union(parents, layer), this.Content(state, layer));
                    break;
                case StepClass.Raw:
                    (parents, layer) = (this.sets.Union(parents, this.escapedSet), this.escapedSet);
                    break;
            }
        }

        return (parents, loan >= 0 ? this.sets.Add(0, this.loanBase + loan) : 0);
    }

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
        return MirTypes.HoldsLoans(cases.Type(this.body.Locals[local].Type), this.walked, out this.writable[local]);
    }

    // W(S) (A3 E16, DECISIONS R4-M73) into the targets, the holders a call's store through S writes: refs(S) (E14); E for a direct Loan
    // of S naming a local that holds no Loan but a raw pointer whose pointee may (MirWrites.Raw); and, from each holder h ≠ E of W whose
    // Type passes the guard, the holder each direct Loan of σ(h) names, in any mode, E where σ(h) carries Φ_E, and E for a direct Loan
    // naming such a raw-only local. The first pass steps from the holders that pass it concretely (MirWrites.Through), the second from
    // those that pass it only through a Type argument, a pair layer or an abstract part (MirWrites.ThroughArgument) and from what only
    // they reach: those targets come last, from the index returned (the count where none), and a Call that stores a non-empty value into
    // one holds its view (P13's interim, R4-M73). Every store path (a Call's rows; A5-F7's capture stores, unit 2c; A5-F6's desc(c)
    // stores, 2f) takes W here and sets the hold so.
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
        this.Write(set); // The kinds of refs(S)'s holders, and E for a raw-only local.
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

    // A step of W from a set (Writes): E where it carries Φ_E, the holder each direct Loan names and E for a direct Loan naming a raw-only
    // local.
    private void Write(int set)
    {
        this.Work += this.sets.Count(set);
        foreach (var element in this.sets.Elements(set))
        {
            if (element == Escaped)
            {
                this.Written(Escaped, MirWrites.None);
            }
            else if (element >= this.loanBase && this.loans[element - this.loanBase] is { Direct: true } loan)
            {
                if (loan.Named >= 0)
                {
                    this.Written(loan.Named, loan.Named == Escaped ? MirWrites.None : this.writable[loan.Root]); // A static's names E.
                }
                else if (loan.Root < this.body.Locals.Length && this.writable[loan.Root] == MirWrites.Raw)
                {
                    this.Written(Escaped, MirWrites.None);
                }
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
