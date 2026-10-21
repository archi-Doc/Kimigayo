// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Numerics;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

// A5-F9's default rules (SPEC 7.2.3, 15.6; DECISIONS R4-M90, R4-M97, R4-M98). In a default template, a preceding parameter's PreparedInput p_i references
// the caller's pending slot, which the default may only inspect, shared. A Deref directly on p_i is the slot itself, an inline step (A3 E17, the
// slot step of MirBorrows.Walk), so a Borrow of the slot forms a Loan of the template; a Place is slot-rooted when it is rooted at a PreparedInput,
// passes that step and no raw pointer's referent after it (D2; SPEC 15.6.2, 5.2.1: a raw Place has its own anchor), which both rules read.
// - R26-borrow: at each Return of the checks replay, one gather with the checks' Based pass and closure from E and _0's holder (the result
//   and storage the body does not own); each slot-rooted Loan it finds is DefaultArgumentBorrow_Kd at the Loan's site, once per Loan and
//   view.
// - R26-access: a write to a slot-rooted Place is DefaultArgumentAccess_Kd at the write, found where the location's accesses are listed
//   (every destination, before the borrowed-root filter; MirBorrows.Locations); after the replay, each exclusive slot-rooted Loan R26-borrow
//   did not report is DefaultArgumentAccess_Kd at its site, one code per failure. The replay reaches every Loan's issue: MirValidator rejects
//   a block runtime and check edges do not reach, so the forward solve reaches every head.
// - The copy disposition (D11, D12): a failure found in an inline default copy is its template's when every event taking part lies in one
//   evaluation of one copy (Covered): the copy B, the innermost Inlined scope holding the operation's scope and the issues of its Loan and
//   reservation, moves outward while either is in Entry(B), the Loans whose issue lies in B that the state before B's entry carries in E and
//   the holders live into it (an earlier evaluation's); a copy remaining covers it. err(ended) and R3 take no Loan. Entry(B) is gathered
//   once per entry location in the checks replay (Entering), before its plan, at the entries the pre-pass finds (Entries: each copy's
//   first location in the replay's order, which dominates the copy; MirValidator's Entry rule, R5-M90).
// Input: the view's Loans with their steps and issue locations, the checks replay's state and live bits, the location's writes. Output:
// each R26 record with the parameter, the PreparedInput's own location (its Type node, R4-M90 D6), as its Anchor fact, through Record, so
// a held view's are Unsupported_Kd (R4-M73); Covered, which the conflicts relation's selection, err(ended) and R3 read (MirBorrows.Checks,
// MirBorrows.Effects).
// Invariants: Covered holds only where a copy's template reports the failure in the view its case selects (the package's 5.6); a copy
// that issues no Loan gets no gather, and a body without one pays one scan of its scopes.
// Must not report a Move of a slot (A1's DefaultArgumentMove_Kd, R4-M69), a write or Loan past a raw pointer's referent, or the same Loan
// under both codes; or record a failure (MirBorrows.Reports).
internal sealed partial class MirBorrows
{
    private bool[] defaulted = []; // By Loan: whether R26-borrow reported it in the view; R26-access's pass reads and clears it.
    private int[] entrySet = []; // By scope: an issuing copy's Entry(B) (0 until its gather), -1 for a copy that issues no Loan.
    private int[] entryAt = []; // By scope: an Inlined scope's entry location, -1 before the pre-pass reaches it.
    private bool[] entering = []; // By location: whether an issuing copy is entered there.
    private bool copying; // Whether a copy issues a Loan in the view, so entering and the entry sets hold.

    // Whether a root, a local or a Loan's root key, is a default template's PreparedInput.
    private bool Prepared(int root) => (uint)root < (uint)this.body.Locals.Length && this.body.Locals[root].Role == MirLocalRole.PreparedInput;

    // Whether a Place's steps on a root are slot-rooted (D2): a PreparedInput's, past its slot step, through no raw pointer's referent.
    private bool SlotRooted(int root, ReadOnlySpan<Step> steps)
    {
        if (!this.Prepared(root) || steps.IsEmpty || steps[0] is not { Kind: MirProjectionKind.Deref, Class: StepClass.Inline })
        {
            return false;
        }

        foreach (var step in steps)
        {
            if (step.Class == StepClass.Raw)
            {
                return false;
            }
        }

        return true;
    }

    private bool SlotRooted(int loan) => this.SlotRooted(this.loans[loan].Root, this.loanSteps.AsSpan(this.loans[loan].Start, this.loans[loan].Count));

    // R26-borrow (D3) at a Return of a template view: the slot-rooted Loans in the closure of σ(_0) ∪ σ(E), each the first time the view finds
    // it. The gather's Stored pass has nothing to read: a Return stores nothing.
    private void DefaultBorrows(ulong[] state)
    {
        if (this.body.Key.Kind != MirBodyKind.DefaultTemplate)
        {
            return;
        }

        this.stamp++;
        this.candidateCount = 0;
        this.Pass(Based);
        this.Reach(Escaped, scan: true);
        if (this.slots[0] >= 0)
        {
            this.Reach(this.slots[0], scan: true);
        }

        this.Drain(state);
        this.pending.Clear();
        MirDataflow.Grow(ref this.defaulted, this.loanCount);
        for (var c = 0; c < this.candidateCount; c++)
        {
            var loan = this.candidates[c].Loan;
            if (!this.defaulted[loan] && this.SlotRooted(loan))
            {
                this.defaulted[loan] = true;
                this.Default(DiagnosticCode.DefaultArgumentBorrow_Kd, OwnershipRequirement.DefaultArgumentBorrow, this.Site(loan), this.loans[loan].Root);
            }
        }

        this.Work += this.candidateCount;
    }

    // R26-access (D4 (1)): a write to a slot-rooted Place, an Assign's or a Call's destination, at the write.
    private void DefaultWrite(MirPlace place, MirLocation at)
    {
        if (this.Prepared(place.Root) && this.Walk(place, reborrow: false, ref this.walk) is var count &&
            this.SlotRooted(place.Root, this.walk.AsSpan(0, count)))
        {
            this.Default(DiagnosticCode.DefaultArgumentAccess_Kd, OwnershipRequirement.DefaultArgumentAccess, this.Subject(at), place.Root);
        }
    }

    // R26-access (D4 (2)) after the replay of a template view: an exclusive slot-rooted Loan that R26-borrow did not report, at its site.
    private void DefaultAccesses()
    {
        if (this.body.Key.Kind != MirBodyKind.DefaultTemplate)
        {
            return;
        }

        MirDataflow.Grow(ref this.defaulted, this.loanCount);
        for (var loan = 0; loan < this.loanCount; loan++)
        {
            if (this.defaulted[loan])
            {
                this.defaulted[loan] = false;
            }
            else if (this.loans[loan].Mode is MirRefKind.Uniq or MirRefKind.TwoPhaseUniq && this.SlotRooted(loan))
            {
                this.Default(DiagnosticCode.DefaultArgumentAccess_Kd, OwnershipRequirement.DefaultArgumentAccess, this.Site(loan), this.loans[loan].Root);
            }
        }

        this.Work += this.loanCount;
    }

    // An R26 record (D6, D7): at its subject, with the parameter its slot-rooted Place is on as the Anchor fact and no witness.
    private void Default(DiagnosticCode code, OwnershipRequirement requirement, SourceRef subject, int root)
    {
        this.facts[0] = new(MirFactKind.Anchor, this.body.Subject(this.body.Locals[root].Source));
        this.Record(code, requirement, subject, this.facts.AsSpan(0, 1));
    }

    // Whether a failure is its template's (D11): the copy holding the operation's scope and the issues of its Loan and reservation (-1:
    // none) moves outward while either is in its Entry(B), carried in from an earlier evaluation; covered while a copy remains.
    private bool Covered(int scope, int loan, int reservation)
    {
        scope = loan < 0 ? scope : this.body.Common(scope, this.IssueScope(loan));
        scope = reservation < 0 ? scope : this.body.Common(scope, this.IssueScope(reservation));
        var copy = this.body.Inlining(scope);
        while (copy >= 0 && (this.Carried(copy, loan) || this.Carried(copy, reservation)))
        {
            copy = this.body.Inlining(this.body.Scopes[copy].Outer);
        }

        this.Work++;
        return copy >= 0;
    }

    // Whether a Loan (-1: none) is in a copy's Entry(B): a copy that holds the Loan's issue issues a Loan, so its set is the view's.
    private bool Carried(int copy, int loan) => loan >= 0 && this.sets.Contains(this.entrySet[copy], this.loanBase + loan);

    // A Loan's issue scope (D14): its issuing location's.
    private int IssueScope(int loan) => this.Source(this.loans[loan].Location).Scope;

    // The copies' entries: each Inlined scope a Loan of the view is issued in, an issuing copy, and every Inlined scope's entry location,
    // its first in the replay's order (MirDataflow.Order: reached heads in reverse postorder, each chain's blocks in order), where Entering
    // gathers; nothing more for a body without an Inlined scope.
    private void Entries()
    {
        var scopes = this.body.Scopes;
        var copies = false;
        foreach (ref readonly var scope in scopes)
        {
            copies |= scope.Kind == MirScopeKind.Inlined;
        }

        this.Work += scopes.Length;
        this.copying = false;
        if (!copies)
        {
            return;
        }

        MirDataflow.Grow(ref this.entrySet, scopes.Length);
        MirDataflow.Grow(ref this.entryAt, scopes.Length);
        Array.Fill(this.entrySet, -1, 0, scopes.Length);
        Array.Fill(this.entryAt, -1, 0, scopes.Length);
        for (var loan = 0; loan < this.loanCount; loan++)
        {
            for (var copy = this.body.Inlining(this.IssueScope(loan)); copy >= 0 && this.entrySet[copy] < 0; copy = this.body.Inlining(scopes[copy].Outer))
            {
                (this.entrySet[copy], this.copying) = (0, true);
            }
        }

        this.Work += this.loanCount;
        if (!this.copying)
        {
            return;
        }

        var locations = this.body.Statements.Length + this.body.Blocks.Length;
        MirDataflow.Grow(ref this.entering, locations);
        Array.Clear(this.entering, 0, locations);
        foreach (var block in flow.Order)
        {
            for (var i = 0; i <= this.body.Blocks[block].Count; i++)
            {
                var location = this.Location(block, i);
                var first = this.body.Inlining(this.Source(location).Scope);
                for (var copy = first; copy >= 0 && this.entryAt[copy] < 0; copy = this.body.Inlining(scopes[copy].Outer))
                {
                    this.entryAt[copy] = location;
                    this.entering[location] |= this.entrySet[copy] >= 0;
                }
            }
        }

        this.Work += locations;
    }

    // Entry(B) at a location where issuing copies are entered, before its plan: one gather from E and the holders live into it (the replay's
    // live bits before Advance) on the state before it, closed as a check set closes; each copy entered there keeps the Loans found whose
    // issue lies in it.
    private void Entering(ulong[] state, int location)
    {
        this.stamp++;
        this.candidateCount = 0;
        this.Pass(Based);
        this.Reach(Escaped, scan: true);
        for (var word = 0; word < (this.holderCount + 63) >> 6; word++)
        {
            for (var bits = this.live[word]; bits != 0; bits &= bits - 1)
            {
                this.Reach((word << 6) + BitOperations.TrailingZeroCount(bits), scan: true);
            }
        }

        this.Drain(state);
        this.pending.Clear();
        for (var c = 0; c < this.candidateCount; c++)
        {
            var loan = this.candidates[c].Loan;
            for (var copy = this.body.Inlining(this.IssueScope(loan)); copy >= 0; copy = this.body.Inlining(this.body.Scopes[copy].Outer))
            {
                if (this.entryAt[copy] == location)
                {
                    this.entrySet[copy] = this.sets.Add(this.entrySet[copy], this.loanBase + loan);
                }
            }
        }

        this.Work += this.candidateCount + ((this.holderCount + 63) >> 6);
    }
}
