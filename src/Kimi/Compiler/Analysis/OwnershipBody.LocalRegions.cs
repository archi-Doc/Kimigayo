// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

public sealed partial class OwnershipBody
{
    // One root at a time: storage is bounded by operations * Places, independent of the number of borrowed roots.
    private int[] loanFlow = [];
    private bool[] loanQueued = [];
    private int loanFlowRoot = -1;

    private void PrepareLoanFlow(int root, int count)
    {
        this.loanFlowRoot = -1;
        var needed = false;
        for (var p = 0; p < count && !needed; p++)
        {
            needed = Binding.HasLocalRegion(this.Places[p].Type);
        }

        if (!needed)
        {
            return;
        }

        var operations = this.Operations.Count;
        Grow(ref this.loanFlow, OwnershipStorage.Cells(operations, count, 32, "local Loan flow"));
        this.loanFlow.AsSpan(0, operations * count).Fill(-1);
        Grow(ref this.loanQueued, operations);
        this.loanQueued.AsSpan(0, operations).Clear();
        var work = this.carryingWork;
        work.Clear();
        for (var id = operations - 1; id >= 0; id--)
        {
            if (this.IsReachable(id) || this.HasCheckingState(id))
            {
                work.Add(id);
                this.loanQueued[id] = true;
            }
        }

        while (work.Count != 0)
        {
            var id = work[^1];
            work.RemoveAt(work.Count - 1);
            this.loanQueued[id] = false;
            for (var edge = this.EdgeHeads[id]; edge >= 0; edge = this.Edges[edge].Next)
            {
                if (this.Edges[edge].Kind != OwnershipEdgeKind.Abort)
                {
                    Merge(id, this.Edges[edge].To);
                }
            }

            for (var edge = this.checkingBorrowHeads[id]; edge >= 0; edge = this.checkingBorrowEdges[edge].Next)
            {
                Merge(id, this.checkingBorrowEdges[edge].To);
            }
        }

        this.loanFlowRoot = root;

        void Merge(int from, int to)
        {
            var changed = false;
            for (var holder = 0; holder < count; holder++)
            {
                if (this.borrowDependencies[(holder * count) + root] == LoanRequirement.None)
                {
                    continue;
                }

                var value = Outgoing(from, holder);
                var at = (to * count) + holder;
                var previous = this.loanFlow[at];
                if (value != -1 && previous != -2 && (previous == -1 || value < previous))
                {
                    this.loanFlow[at] = value;
                    changed = true;
                }
            }

            if (changed && !this.loanQueued[to])
            {
                this.loanQueued[to] = true;
                work.Add(to);
            }
        }

        int Outgoing(int id, int holder)
        {
            if (this.Places[holder].Kind is not (OwnershipPlaceKind.Local or OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result) ||
                this.HasExplicitDependency(holder) || this.HasStoredBorrowRecord(holder, root))
            {
                return -2;
            }

            var operation = this.Operations[id];
            return this.HolderChangeAt(id, holder, root, count) switch
            {
                HolderChange.None => this.loanFlow[(id * count) + holder],
                HolderChange.Ended => -1,
                HolderChange.Unknown => -2,
                _ => operation.Kind switch
                {
                    OwnershipOperationKind.Write => Source(id, operation.Input),
                    OwnershipOperationKind.Consume or OwnershipOperationKind.Borrow => Source(id, operation.Place),
                    _ => id,
                },
            };
        }

        int Source(int id, int place)
            => place < 0 ? -2 : place == root ? id
            : this.borrowDependencies[(place * count) + root] == LoanRequirement.None ? -1
            : this.Places[place].Kind is not (OwnershipPlaceKind.Local or OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result) ? -2
            : this.loanFlow[(id * count) + place];
    }
}
