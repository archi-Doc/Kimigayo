// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

// A5's problem codes and witness sites (A3 5's A6 table, 6.4): a conflict's code (a Loan found in its reservation holder is a reservation
// conflict, else the check's), a location's subject and source (its statement's or terminator's), and a witness holder's next use in its
// chain, which MirBorrows.Reports records as a problem's subject and facts.
// Must not record a problem or decide which problem a conflict becomes (MirBorrows.Reports).
internal sealed partial class MirBorrows
{
    // A conflict's code (A3 5's A6 table): a Loan found in its reservation holder is a reservation conflict, else the check's code.
    private DiagnosticCode Code(in Candidate candidate, DiagnosticCode code) => this.Reserved(candidate) ? DiagnosticCode.CallReservationConflict_Kd : code;

    // A location's subject: its statement's or terminator's.
    private SourceRef Subject(MirLocation at) => this.body.Subject(this.Source(at));

    // A location's source: its statement's or terminator's.
    private SourceInfo Source(MirLocation at)
    {
        ref readonly var data = ref this.body.Blocks[at.Block];
        return at.Index < data.Count ? this.body.Statements[data.Start + at.Index].Source : data.Terminator.Source;
    }

    // The witness holder's first use at or after a location in its chain (A3 6.4), or null where it is used only in a later chain.
    private SourceRef? NextUse(int slot, MirLocation from)
    {
        var (block, first) = (from.Block, from.Index);
        for (var visited = 0; visited < this.body.Blocks.Length; visited++)
        {
            ref readonly var data = ref this.body.Blocks[block];
            for (var i = first; i <= data.Count; i++)
            {
                var (start, count) = this.touched[this.Location(block, i)];
                for (var t = start; t < start + count; t++)
                {
                    if (this.touches[t].Slot == slot && (this.touches[t].Flags & Uses) != 0)
                    {
                        return this.Subject(new(block, i));
                    }
                }
            }

            var terminator = data.Terminator;
            var next = MirUses.Successors(terminator) == 1 && terminator.CheckTarget < 0 ? MirUses.Successor(this.body, terminator, 0) : -1;
            if (next < 0 || next == this.chainFirst || flow.Head(next) != flow.Head(block))
            {
                return null;
            }

            (block, first) = (next, 0);
        }

        return null;
    }
}
