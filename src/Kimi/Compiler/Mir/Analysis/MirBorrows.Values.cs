// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's values on a state (A3 5's val, issued, content and refs, with E3): what an rvalue, an operand or a Place carries, a borrow's
// parents and Loan, and the holders a set refers to. On the state a location's plan reads, a set's content is read once (E13 (5)),
// however many operands and retention rows read it.
internal sealed partial class MirBorrows
{
    private readonly List<int> targets = []; // The holders a reference's value refers to (refs).
    private readonly List<int> contentsRead = []; // The sets whose content the location read, reset by Plan.
    private int[] contents = []; // By set: its content at the location plus 1 (0: not read there).

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
        var count = this.Walk(place, reborrow: false, ref this.walk);
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
        var count = this.Walk(place, reborrow, ref this.walk);
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
}
