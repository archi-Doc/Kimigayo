// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipBody
{
#if DEBUG
    private readonly List<(int Entry, int Exit, bool CanComplete)> completionChecks = new();
#endif
    private readonly HashSet<(Koto Source, OwnershipOperationKind Kind, int Place)> checkedUses = new();
    private int[] checkingBlockOf = [];
    private int[] checkingLeaders = [];
    private int[] checkingNext = [];
    private int[] checkingIncoming = [];
    private int[] checkingPredecessor = [];
    private bool[] checkingReached = [];
    private ulong[] checkingStates = [];
    private int checkingBlockCount;
    private bool checkingSolved;

    /// <summary>Gets whether an operation has a separate, converged source-checking state.</summary>
    /// <param name="operation">The operation ID.</param>
    /// <returns>False for ordinary runtime operations and unsupported continuation roots.</returns>
    public bool HasCheckingState(int operation)
        => this.checkingSolved && this.checkingBlockOf[operation] is >= 0 and var block && this.checkingReached[block];

    /// <summary>Gets the input of an unreachable source-checking operation, without changing runtime reachability.</summary>
    /// <param name="operation">The operation ID.</param>
    /// <param name="place">The Place ID.</param>
    /// <returns>The converged checking state, or None when no checking state exists.</returns>
    public PlaceState GetCheckingInputState(int operation, int place)
    {
        if (!this.HasCheckingState(operation))
        {
            return PlaceState.None;
        }

        this.LoadInput(operation, true);
        return this.State(place);
    }

    internal void InvalidateChecking() => this.checkingSolved = false;

    internal void CheckUnreachable()
    {
        this.AssertCompletion();
        // Preserve the existing fallback's exact domain. In particular, an orphan
        // synthetic result delivery after a Never body is not an unchecked source use.
        var needsChecking = false;
        for (var i = 0; i < this.OperationStorage.Count; i++)
        {
            if (!this.Reachable[i] && this.NeedsSourceState(this.OperationStorage[i]))
            {
                needsChecking = true;
                break;
            }
        }

        if (!needsChecking)
        {
            return;
        }

        if (this.CheckingRegions.Count > 1)
        {
            this.SolveChecking();
        }

        this.checkedUses.Clear();
        for (var i = 0; i < this.OperationStorage.Count; i++)
        {
            var operation = this.OperationStorage[i];
            if ((this.Reachable[i] || this.HasCheckingState(i)) && this.NeedsSourceState(operation))
            {
                this.checkedUses.Add((operation.Source, operation.Kind, operation.Place));
            }
        }

        for (var i = 0; i < this.OperationStorage.Count; i++)
        {
            var operation = this.OperationStorage[i];
            // An orphan replica at lexical completion is not a new source use. Every
            // actual runtime/checking execution was checked above, including failures.
            if (!this.Reachable[i] && !this.HasCheckingState(i) && this.NeedsSourceState(operation) &&
                !this.checkedUses.Contains((operation.Source, operation.Kind, operation.Place)))
            {
                this.ReportIssue(new(operation.Source, OwnershipFailure.Unsupported, operation.Place));
            }
        }
    }

    [Conditional("DEBUG")]
    internal void RecordCompletion(int entry, int exit, bool canComplete)
    {
#if DEBUG
        this.completionChecks.Add((entry, exit, canComplete));
#endif
    }

    [Conditional("DEBUG")]
    internal void ResetCompletion()
    {
#if DEBUG
        this.completionChecks.Clear();
#endif
    }

    [Conditional("DEBUG")]
    private void AssertCompletion()
    {
#if DEBUG
        for (var i = 0; i < this.completionChecks.Count; i++)
        {
            var check = this.completionChecks[i];
            Debug.Assert(
                check.Entry < 0 || !this.Reachable[check.Entry] ||
                check.CanComplete == (check.Exit >= 0 && this.Reachable[check.Exit]),
                "Control-flow completion and ownership runtime predecessors disagree.");
        }
#endif
    }

    private bool NeedsSourceState(OwnershipOperation operation)
        => operation.Place >= 0 && this.PlaceStorage[operation.Place].Kind is OwnershipPlaceKind.Local or OwnershipPlaceKind.Parameter &&
            (operation.Kind is OwnershipOperationKind.Read or OwnershipOperationKind.Consume or OwnershipOperationKind.Borrow ||
                (operation.Kind == OwnershipOperationKind.Write && operation.Source is BinaryKoto));

    private void SolveChecking()
    {
        this.PartitionCheckingBlocks();
        var width = this.words * Lanes;
        Grow(ref this.checkingStates, checked(this.checkingBlockCount * width));
        Grow(ref this.checkingReached, this.checkingBlockCount);
        Grow(ref this.BlockQueue, this.checkingBlockCount);
        Grow(ref this.BlockQueued, this.checkingBlockCount);
        this.checkingReached.AsSpan(0, this.checkingBlockCount).Clear();
        this.BlockQueued.AsSpan(0, this.checkingBlockCount).Clear();
        this.checkingSolved = true;

        // Regions are created after their seed operations. A nested continuation
        // reads its parent's fixed point, never a worklist's intermediate state.
        for (var i = 1; i < this.CheckingRegions.Count; i++)
        {
            var region = this.CheckingRegions[i];
            if (region.Entry < 0 || region.Seed < 0)
            {
                continue;
            }

            Debug.Assert(this.OperationRegions[region.Seed] < i);
            var runtime = this.Reachable[region.Seed];
            if (!runtime && !this.HasCheckingState(region.Seed))
            {
                continue;
            }

            Debug.Assert(!this.Reachable[region.Entry]);
            this.LoadInput(region.Seed, !runtime);
            this.Transfer(region.Seed);
            var block = this.checkingBlockOf[region.Entry];
            Debug.Assert(block >= 0 && !this.checkingReached[block]);
            this.Scratch.AsSpan(0, width).CopyTo(this.checkingStates.AsSpan(block * width, width));
            this.checkingReached[block] = true;
            this.Converge(block, true);
        }

        // Diagnostics share the runtime rules; this never calls Finalize, marks
        // runtime Reachable, changes Placement or appends executable cleanup plans.
        for (var block = 0; block < this.checkingBlockCount; block++)
        {
            if (this.checkingReached[block])
            {
                this.RunBlock(block, true, true);
            }
        }
    }

    private bool IsCheckingEdge(OwnershipEdge edge)
        => this.OperationRegions[edge.From] > 0 && this.OperationRegions[edge.From] == this.OperationRegions[edge.To] &&
            edge.Kind != OwnershipEdgeKind.Abort;

    private void PartitionCheckingBlocks()
    {
        var count = this.OperationStorage.Count;
        Grow(ref this.checkingBlockOf, count);
        Grow(ref this.checkingNext, count);
        Grow(ref this.checkingIncoming, count);
        Grow(ref this.checkingPredecessor, count);
        this.checkingBlockOf.AsSpan(0, count).Fill(-1);
        this.checkingNext.AsSpan(0, count).Fill(-1);
        this.checkingIncoming.AsSpan(0, count).Clear();

        // Filter ordinary edges by lexical continuation identity. Seed edges stay
        // solely in CheckingRegions; runtime adjacency and incoming edges are untouched.
        for (var i = 0; i < this.EdgeStorage.Count; i++)
        {
            var edge = this.EdgeStorage[i];
            if (this.IsCheckingEdge(edge))
            {
                this.checkingIncoming[edge.To]++;
                this.checkingPredecessor[edge.To] = edge.From;
                this.checkingNext[edge.From] = this.checkingNext[edge.From] == -1 ? edge.To : -2;
            }
        }

        for (var i = 0; i < count; i++)
        {
            var next = this.checkingNext[i];
            if (next < 0 || this.checkingIncoming[next] != 1 || this.CheckingRegions[this.OperationRegions[next]].Entry == next)
            {
                this.checkingNext[i] = -1;
            }
        }

        this.checkingBlockCount = 0;
        for (var operation = 0; operation < count; operation++)
        {
            if (this.OperationRegions[operation] == 0 ||
                (this.checkingIncoming[operation] == 1 && this.checkingNext[this.checkingPredecessor[operation]] == operation))
            {
                continue;
            }

            Grow(ref this.checkingLeaders, this.checkingBlockCount + 1);
            this.checkingLeaders[this.checkingBlockCount] = operation;
            for (var cursor = operation; cursor >= 0; cursor = this.checkingNext[cursor])
            {
                Debug.Assert(this.checkingBlockOf[cursor] < 0 && !this.Reachable[cursor]);
                this.checkingBlockOf[cursor] = this.checkingBlockCount;
            }

            this.checkingBlockCount++;
        }
    }

    private void LoadInput(int operation, bool checking)
    {
        // Seeds may be in the middle of a block. Retain only block inputs and
        // replay its prefix, sharing precisely the same transfer rules as solving.
        var block = checking ? this.checkingBlockOf[operation] : this.BlockOf[operation];
        var cursor = this.LoadBlock(block, checking);
        while (cursor != operation)
        {
            this.Transfer(cursor);
            cursor = this.NextInBlock(cursor, block, checking);
            Debug.Assert(cursor >= 0);
        }
    }

    private void Converge(int entry, bool checking)
    {
        var blocks = checking ? this.checkingBlockCount : this.blockCount;
        var states = checking ? this.checkingStates : this.BlockStates;
        var reached = checking ? this.checkingReached : this.BlockReachable;
        var blockOf = checking ? this.checkingBlockOf : this.BlockOf;
        var width = this.words * Lanes;
        this.BlockQueued[entry] = true;
        this.BlockQueue[0] = entry;
        var head = 0;
        var size = 1;
        while (size > 0)
        {
            var block = this.BlockQueue[head];
            head = head + 1 == blocks ? 0 : head + 1;
            size--;
            this.BlockQueued[block] = false;
            var last = this.RunBlock(block, false, checking);
            var output = this.Scratch.AsSpan(0, width);
            for (var e = this.EdgeHeads[last]; e >= 0; e = this.EdgeStorage[e].Next)
            {
                var edge = this.EdgeStorage[e];
                if (checking && !this.IsCheckingEdge(edge))
                {
                    continue;
                }

                var target = blockOf[edge.To];
                Debug.Assert(target >= 0);
                var destination = states.AsSpan(target * width, width);
                bool changed;
                if (!reached[target])
                {
                    output.CopyTo(destination);
                    reached[target] = true;
                    changed = true;
                }
                else
                {
                    changed = Join(destination, output, this.words);
                }

                if (changed && !this.BlockQueued[target])
                {
                    this.BlockQueued[target] = true;
                    var tail = head + size;
                    this.BlockQueue[tail >= blocks ? tail - blocks : tail] = target;
                    size++;
                }
            }
        }
    }
}
