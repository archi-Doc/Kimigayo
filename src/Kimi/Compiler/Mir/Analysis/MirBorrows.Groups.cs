// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's Loans in group order (E8; A3 4): the view's Loans renumbered stably by their group, the Move Path of their Place's longest inline
// prefix (MirMoveData's pre-order) or, after the paths, a static's, so the Loans a Move Path's subtree prefixes are one run of ids, which
// MirBorrows.Endings' Subtree reads as a MirLoanSets.Range.
// Input: the view's Loans in issue order and the table of the Loans its sites issue (MirBorrows.Loans).
// Output: the Loans in group order, each group's first Loan and the end after the last group (loanStart), and the sites' Loans renumbered.
// Invariants: stable within a group, so one group's Loans keep their issue order.
// Must not issue a Loan, read the body's statements or decide which Loans a location ends (MirBorrows.Endings).
internal sealed partial class MirBorrows
{
    private int[] loanStart = []; // By group (Loan.Group): its first Loan, the Loans being in group order.
    private Loan[] ordered = []; // The Loans being put in group order, as long as `loans`, whose role it takes (Order).
    private int[] renumbered = []; // By Loan in issue order: its id in group order.

    // Orders the Loans by group, stably, so the Loans a Move Path prefixes are one run of ids (Subtree). The two Loan arrays swap roles
    // here, so `ordered` is kept as long as `loans`: a later pass, whichever role each array then has, grows neither beyond what the
    // longest view needed (a warm run allocates nothing).
    private void Order(int groups)
    {
        MirDataflow.Grow(ref this.loanStart, groups + 1);
        MirDataflow.Grow(ref this.ordered, this.loans.Length);
        MirDataflow.Grow(ref this.renumbered, this.loanCount);
        Array.Clear(this.loanStart, 0, groups + 1);
        for (var l = 0; l < this.loanCount; l++)
        {
            this.loanStart[this.loans[l].Group + 1]++;
        }

        for (var g = 1; g <= groups; g++)
        {
            this.loanStart[g] += this.loanStart[g - 1]; // Each group's start.
        }

        for (var l = 0; l < this.loanCount; l++)
        {
            var id = this.loanStart[this.loans[l].Group]++; // Each group's start becomes its end.
            (this.renumbered[l], this.ordered[id]) = (id, this.loans[l]);
        }

        this.loanStart.AsSpan(0, groups).CopyTo(this.loanStart.AsSpan(1));
        this.loanStart[0] = 0;
        (this.loans, this.ordered) = (this.ordered, this.loans);
        this.Work += groups + (2 * this.loanCount);
    }

    // The sites' Loans in group order.
    private void Renumber(Span<int> sites)
    {
        foreach (ref var loan in sites)
        {
            loan = loan >= 0 ? this.renumbered[loan] : -1;
        }

        this.Work += sites.Length;
    }
}
