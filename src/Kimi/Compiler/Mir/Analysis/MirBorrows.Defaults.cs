// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

// A5-F9's template rules (SPEC 7.2.3; DECISIONS R4-M90, R4-M95): in a default template, a preceding parameter's PreparedInput p_i references
// the caller's pending slot, which the default may only inspect, shared. A Deref directly on p_i is the slot itself (A3 E17, the slot step of
// MirBorrows.Walk), so a Borrow of the slot forms a Loan of the template; a Place is slot-rooted when it is rooted at a PreparedInput, passes
// that step and no raw pointer's referent after it (D2; SPEC 15.6.2, 5.2.1: a raw Place has its own anchor), which both rules read.
// - R26-borrow: at each Return of the checks replay, one gather with the checks' Based pass and closure from E and _0's holder (the result
//   and storage the body does not own); each slot-rooted Loan it finds is DefaultArgumentBorrow_Kd at the Loan's site, once per Loan and
//   view.
// - R26-access: a write to a slot-rooted Place is DefaultArgumentAccess_Kd at the write, found where the location's accesses are listed
//   (every destination, before the borrowed-root filter; MirBorrows.Locations); after the replay, each exclusive slot-rooted Loan R26-borrow
//   did not report is DefaultArgumentAccess_Kd at its site, one code per failure. The replay reaches every Loan's issue: MirValidator rejects
//   a block runtime and check edges do not reach, so the forward solve reaches every head.
// Input: the view's Loans and their steps, the checks replay's state at a Return and the location's writes. Output: each record with the
// parameter, the PreparedInput's own location (its Type node, R4-M90 D6), as its Anchor fact, through Record, so a held view's are
// Unsupported_Kd (R4-M73).
// Must not report a Move of a slot (A1's DefaultArgumentMove_Kd, R4-M69), a write or Loan past a raw pointer's referent, or the same Loan
// under both codes.
internal sealed partial class MirBorrows
{
    private bool[] defaulted = []; // By Loan: whether R26-borrow reported it in the view; R26-access's pass reads and clears it.

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
                this.Default(DiagnosticCode.DefaultArgumentBorrow_Kd, OwnershipRequirement.DefaultArgumentBorrow, this.loans[loan].Site, this.loans[loan].Root);
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
                this.Default(DiagnosticCode.DefaultArgumentAccess_Kd, OwnershipRequirement.DefaultArgumentAccess, this.loans[loan].Site, this.loans[loan].Root);
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
}
