// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's rule W (SPEC 15.6.5, 23.1.2; DECISIONS R4-M132, design C): a Loan conflict caused only by Refuted Origin relations is derived from
// them, recorded with their problems as Cause facts; nothing is dropped and no verdict changes.
// - Responsibility: the run's W elements, its Loans whose Ref MirOrigins gives Refuted causes covering the run's views and the guard
//   markers γ_H every Ref binding H gives such causes; one indirect marker per W element, an element between the per-local markers and
//   the Loans, which a value carries where the element may have arrived other than as the borrow or a Copy or Reborrow of it stored into a
//   part with a fixed Origin (Indirect, Arrive), joined by union, so the marker is a may-fact, and ended with its Loan; the escape holders
//   (E, _0 and MirLocalRole.Result temps, U2); whether a carrier keeps a W element (Kept: a transit value, a holder other than an escape
//   holder, or an escape holder holding its marker); a conflict's or err(ended)'s causes (Derived, EndedCauses), which the problem's
//   record appends as Cause facts (CauseFacts).
// - Transfer rules, each where a value is formed (MirBorrows.Values, .Reach, .Calls, .Flow): an aggregate, a part step and a raw Deref
//   mark; a read through a reference keeps a whole holder's state and marks a part's or E's (Parts, for content and chain alike, a
//   Closure call's receiver content included, DECISIONS R4-M148); a Call's Value row keeps its argument; an arrival into an escape
//   holder is kept where MirOrigins.Fixed holds for the receiving Type, into a local where it replaces the local whole or writes through a
//   reference whose direct Loans naming the local borrow its whole root.
// - Inputs: MirOrigins' Causes, Refutes and Fixed for the body Judge just judged (read, never re-derived); the program's problems' views.
// - Invariants: where MirOrigins.Refutes is false there is no W element, no marker and no query, so the run is exactly the rule-free one; a
//   conflict is derived exactly when every conflicting candidate the check set takes is a W element no carrier keeps (R4-M96), its causes
//   their causes' union; a located Unsupported_Kd, a limit's included, carries no cause.
// - Prohibitions: no Origin judged or class read here (MirOrigins'), no problem dropped, no merge of reports (MirReport's).
internal sealed partial class MirBorrows
{
    private readonly List<int> wCauses = []; // The W elements' causes (indices in MirReport.All): element w's from wStart[w] to wStart[w + 1].
    private readonly List<int> marks = []; // Indirect's scratch.
    private readonly List<int> causes = []; // The causes of the conflict being recorded, consumed by its record.
    private int[] wStart = [];
    private int[] wOfLoan = []; // By Loan: its W index, or -1.
    private int[] wOfLocal = []; // By local: its guard marker's W index, or -1.
    private int[] guards = []; // By guard candidate: whether every Ref binding it has causes covering the run's views (1), not (2), none (0).
    private int[] guardStart = []; // By local: the end of its binding Refs' run in guardRefs (a counting sort), the next local's start.
    private int[] guardRefs = [];
    private bool[] escape = []; // By slot: E, _0's or a Result temp's.
    private int[] memo = []; // By set: its Indirect at memoRun.
    private int[] memoRun = [];
    private int[] markKept = []; // By marker: the check that found it kept (Based and Stored passes).
    private int wCount;
    private int run; // The runs made, which memoRun stamps.
    private int planLocation; // The location being planned, whose subject MirOrigins.Fixed resolves Origins at.

    // The run's W elements, guard markers first so that their indirect markers ascend with the elements (Indirect), then Loans; loanBase
    // after their markers. None where MirOrigins.Refutes is false.
    private void Caused()
    {
        var locals = this.body.Locals.Length;
        (this.wCount, this.run) = (0, this.run + 1);
        this.wCauses.Clear();
        if (!origins.Refutes(this.body))
        {
            return;
        }

        MirDataflow.Grow(ref this.wOfLoan, this.loanCount);
        MirDataflow.Grow(ref this.wOfLocal, locals);
        MirDataflow.Grow(ref this.guards, locals);
        MirDataflow.Grow(ref this.guardStart, locals + 1);
        MirDataflow.Grow(ref this.wStart, this.loanCount + locals + 1);
        Array.Fill(this.wOfLoan, -1, 0, this.loanCount);
        Array.Fill(this.wOfLocal, -1, 0, locals);
        Array.Clear(this.guards, 0, locals);
        this.guardStart.AsSpan(0, locals + 1).Clear();
        this.wStart[0] = 0;
        var statements = this.body.Statements;
        for (var s = 0; s < statements.Length; s++)
        {
            if (this.Binds(statements[s]) is var h and >= 0)
            {
                this.guards[h] = this.guards[h] == 2 || !this.Covers(origins.Causes(s), adding: false) ? 2 : 1;
                this.guardStart[h + 1]++;
            }
        }

        for (var h = 0; h < locals; h++)
        {
            this.guardStart[h + 1] += this.guardStart[h];
        }

        MirDataflow.Grow(ref this.guardRefs, this.guardStart[locals]);
        for (var s = 0; s < statements.Length; s++)
        {
            if (this.Binds(statements[s]) is var h and >= 0)
            {
                this.guardRefs[this.guardStart[h]++] = s; // Then guardStart[h] ends h's run, which guardStart[h - 1] starts.
            }
        }

        for (var h = 0; h < locals; h++)
        {
            if (this.guards[h] == 1)
            {
                var start = h == 0 ? 0 : this.guardStart[h - 1];
                foreach (var s in this.guardRefs.AsSpan(start, this.guardStart[h] - start))
                {
                    this.Covers(origins.Causes(s), adding: true);
                }

                this.wOfLocal[h] = this.wCount;
                this.wStart[++this.wCount] = this.wCauses.Count;
            }
        }

        for (var l = 0; l < this.loanCount; l++)
        {
            var location = this.loans[l].Location;
            if (location < statements.Length && this.Covers(origins.Causes(location), adding: true))
            {
                this.wOfLoan[l] = this.wCount;
                this.wStart[++this.wCount] = this.wCauses.Count;
            }
        }

        this.loanBase += this.wCount;
        this.Work += statements.Length + this.loanCount;
    }

    // The guard candidate a statement's Ref binds (E7: its Borrows are the Refs binding it, a Loan only past no shared Deref), or -1.
    private int Binds(in MirStatement statement)
        => statement is { Kind: MirStatementKind.Assign, Place: { Projection: 0, Root: >= 0 and var root }, Rvalue.Kind: MirRvalueKind.Ref } &&
            this.MarkerOf(root) == Marker.Guard ? root : -1;

    // Whether a Ref's causes cover the run's views; adding, those with a view of the run join the element being made, each once.
    private bool Covers(ReadOnlySpan<int> found, bool adding)
    {
        var problems = this.program.Problems.All;
        var covered = 0UL;
        foreach (var p in found)
        {
            covered |= problems[p].Views;
        }

        if (found.IsEmpty || (covered & this.views) != this.views)
        {
            return false;
        }

        for (var i = 0; adding && i < found.Length; i++)
        {
            if ((problems[found[i]].Views & this.views) != 0 && this.wCauses.IndexOf(found[i], this.wStart[this.wCount]) < 0)
            {
                this.wCauses.Add(found[i]);
            }
        }

        return true;
    }

    // The escape holders (after Holders): E, _0's slot and the Result temps' (MirLocalRole.Result, the body's result in transit).
    private void Escapes()
    {
        if (this.wCount == 0)
        {
            return;
        }

        MirDataflow.Grow(ref this.escape, this.holderCount);
        Array.Clear(this.escape, 0, this.holderCount);
        this.escape[Escaped] = true;
        for (var i = 0; i < this.body.Locals.Length; i++)
        {
            if (this.slots[i] >= 0 && (i == 0 || this.body.Locals[i].Role == MirLocalRole.Result))
            {
                this.escape[this.slots[i]] = true;
            }
        }
    }

    // An element's W index, or -1: a Loan's or a guard marker's.
    private int W(int element)
        => element >= this.loanBase ? (element - this.loanBase < this.loanCount ? this.wOfLoan[element - this.loanBase] : -1)
            : element >= Markers && element < Markers + this.body.Locals.Length ? this.wOfLocal[element - Markers] : -1;

    // A W element's indirect marker.
    private int Mark(int w) => Markers + this.body.Locals.Length + w;

    // A set with the indirect marker of each of its W elements; the gate is inlined where the run has none (a measured cost, U3).
    private int Indirect(int set) => this.wCount == 0 || set == 0 ? set : this.Marked(set);

    // Indirect for a nonempty set of a W run: memoized per set, its markers one ascending run (MirLoanSets.Of).
    private int Marked(int set)
    {
        MirDataflow.Grow(ref this.memo, set + 1);
        MirDataflow.Grow(ref this.memoRun, set + 1);
        if (this.memoRun[set] == this.run)
        {
            return this.memo[set];
        }

        this.marks.Clear();
        this.Work += this.sets.Count(set);
        foreach (var element in this.sets.Elements(set))
        {
            if (this.W(element) is var w and >= 0)
            {
                this.marks.Add(this.Mark(w));
            }
        }

        var result = this.marks.Count == 0 ? set : this.sets.Union(set, this.sets.Of(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(this.marks)));
        MirDataflow.Grow(ref this.memo, result + 1);
        MirDataflow.Grow(ref this.memoRun, result + 1);
        (this.memo[set], this.memoRun[set], this.memo[result], this.memoRun[result]) = (result, this.run, result, this.run);
        return result;
    }

    // A value's arrival in a holder: into an escape holder it stays as it is where the receiving Type's Origins are all Fixed; into a local
    // where it replaces the local whole, or is written through a reference (through, 0 for none) whose direct Loans naming the local borrow
    // its whole root; else its W elements are marked. A value without an unmarked W element is not queried; the gate is inlined as Indirect's.
    private int Arrive(int slot, BoundType? receiving, bool whole, int through, int value)
        => this.wCount == 0 || value == 0 ? value : this.Arrival(slot, receiving, whole, through, value);

    // Arrive for a nonempty value of a W run.
    private int Arrival(int slot, BoundType? receiving, bool whole, int through, int value)
    {
        if (this.Marked(value) is var marked && marked == value)
        {
            return value;
        }

        this.Work++;
        var keep = this.escape[slot] ? origins.Fixed(receiving, this.body.Subject(this.Source(this.planLocation)))
            : whole || (through != 0 && this.WholeTarget(through, slot));
        return keep ? value : marked;
    }

    // Whether a reference set has a direct Loan naming a holder and every one borrows the holder's whole root: a write through it writes the
    // holder's whole value, a Copy into it.
    private bool WholeTarget(int set, int slot)
    {
        var any = false;
        foreach (var element in this.sets.Elements(set))
        {
            if (element >= this.loanBase && this.loans[element - this.loanBase] is { Direct: true } loan && loan.Named == slot)
            {
                if (loan.Count != 0)
                {
                    return false;
                }

                any = true;
            }
        }

        return any;
    }

    // Whether a carrier keeps an element found in it (a holder, -1 for a transit value): a W element, found in a transit value, in a holder
    // other than an escape holder, or in an escape holder as no Copy of the borrow (its marker).
    private bool Kept(ulong found, int element, int via)
        => this.wCount > 0 && this.W(element) is var w and >= 0 && (via < 0 || !this.escape[via] ||
            this.sets.Contains(Placed(found), this.Mark(w)) || this.sets.Contains(Shared(found), this.Mark(w)));

    // Whether a conflicting candidate is a W element no carrier keeps; its causes then join the conflict's.
    private bool Derived(int candidate)
    {
        ref readonly var found = ref this.candidates[candidate];
        if (this.wOfLoan[found.Loan] is not (var w and >= 0) || found.Kept)
        {
            return false;
        }

        this.Join(w);
        return true;
    }

    // err(ended)'s causes: its guard marker's, where it is a W element no carrier keeps.
    private void EndedCauses(int candidate, int mark)
    {
        this.causes.Clear();
        if (this.wCount > 0 && this.wOfLocal[candidate] is var w and >= 0 && this.markKept[mark] != this.stamp)
        {
            this.Join(w);
        }
    }

    // A W element's causes joined to the conflict's, each once.
    private void Join(int w)
    {
        for (var c = this.wStart[w]; c < this.wStart[w + 1]; c++)
        {
            if (!this.causes.Contains(this.wCauses[c]))
            {
                this.causes.Add(this.wCauses[c]);
            }
        }
    }

    // The record's Cause facts after its other facts (MirFact's order), the conflict's causes consumed.
    private int CauseFacts(int count)
    {
        var problems = this.program.Problems.All;
        MirDataflow.Grow(ref this.facts, count + this.causes.Count);
        foreach (var p in this.causes)
        {
            this.facts[count++] = new(MirFactKind.Cause, problems[p].Subject, p);
        }

        this.causes.Clear();
        return count;
    }
}
