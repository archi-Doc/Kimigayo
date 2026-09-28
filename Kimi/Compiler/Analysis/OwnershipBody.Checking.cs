// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipBody
{
    private readonly List<(int Entry, int Exit, bool CanComplete)> completionChecks = new();
    private readonly HashSet<(Koto Source, OwnershipOperationKind Kind, int Place)> checkedUses = new();
    private readonly List<int> checkingReplayProof = new();
    private readonly List<(int Source, int Next)> checkingReplayReverse = new();
    private int[] checkingBlockOf = [];
    private int[] checkingLeaders = [];
    private int[] checkingNext = [];
    private int[] checkingIncoming = [];
    private int[] checkingPredecessor = [];
    private bool[] checkingReached = [];
    private ulong[] checkingStates = [];
    private int[] checkingReplayStack = [];
    private byte[] checkingReplayMarks = [];
    private int[] checkingReplayHeads = [];
    private ulong[] checkingReplayStates = [];
    private bool[] checkingReplayReached = [];
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
        return this.CompleteState(place);
    }

    internal void InvalidateChecking() => this.checkingSolved = false;

    internal void CheckUnreachable()
    {
        this.CheckCompletion();
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

    internal int LinearCheckingSuccessor(int operation)
    {
        var next = -1;
        for (var e = this.EdgeHeads[operation]; e >= 0; e = this.EdgeStorage[e].Next)
        {
            var edge = this.EdgeStorage[e];
            if (edge.Kind == OwnershipEdgeKind.Abort)
            {
                continue;
            }

            if (next >= 0)
            {
                return -1;
            }

            next = edge.To;
        }

        return next;
    }

    internal bool CanReplayCheckingGraph(int entry, int end)
    {
        var owner = this.OperationRegions[entry];
        Grow(ref this.checkingReplayMarks, this.Operations.Count);
        Grow(ref this.checkingReplayHeads, this.Operations.Count);
        this.checkingReplayMarks.AsSpan(0, this.Operations.Count).Clear();
        this.checkingReplayHeads.AsSpan(0, this.Operations.Count).Fill(-1);
        this.checkingReplayProof.Clear();
        this.checkingReplayReverse.Clear();
        this.checkingReplayProof.Add(entry);
        this.checkingReplayMarks[entry] = 1;
        for (var i = 0; i < this.checkingReplayProof.Count; i++)
        {
            var operation = this.checkingReplayProof[i];
            if (operation == end)
            {
                continue;
            }

            var successor = false;
            for (var e = this.EdgeHeads[operation]; e >= 0; e = this.EdgeStorage[e].Next)
            {
                var edge = this.EdgeStorage[e];
                if (edge.Kind == OwnershipEdgeKind.Abort)
                {
                    continue;
                }

                if (this.OperationRegions[edge.To] != owner)
                {
                    return false;
                }

                successor = true;
                this.checkingReplayReverse.Add((operation, this.checkingReplayHeads[edge.To]));
                this.checkingReplayHeads[edge.To] = this.checkingReplayReverse.Count - 1;
                if (this.checkingReplayMarks[edge.To] == 0)
                {
                    this.checkingReplayMarks[edge.To] = 1;
                    this.checkingReplayProof.Add(edge.To);
                }
            }

            if (!successor)
            {
                return false;
            }
        }

        if (this.checkingReplayMarks[end] == 0)
        {
            return false;
        }

        // Every retained node must have an exit to the endpoint. A cycle with an
        // exit is solved to a fixed point; a closed terminal component is not lost.
        this.checkingReplayProof.Clear();
        this.checkingReplayProof.Add(end);
        this.checkingReplayMarks[end] = 2;
        for (var i = 0; i < this.checkingReplayProof.Count; i++)
        {
            for (var e = this.checkingReplayHeads[this.checkingReplayProof[i]]; e >= 0; e = this.checkingReplayReverse[e].Next)
            {
                var source = this.checkingReplayReverse[e].Source;
                if (this.checkingReplayMarks[source] == 1)
                {
                    this.checkingReplayMarks[source] = 2;
                    this.checkingReplayProof.Add(source);
                }
            }
        }

        return this.checkingReplayMarks.AsSpan(0, this.Operations.Count).IndexOf((byte)1) < 0;
    }

    internal void RecordCompletion(int entry, int exit, bool canComplete)
        => this.completionChecks.Add((entry, exit, canComplete));

    internal void ResetCompletion()
        => this.completionChecks.Clear();

    // Control-flow completion and the ownership graph's runtime reachability must agree for every
    // recorded construct; a disagreement is an internal issue at that construct.
    private void CheckCompletion()
    {
        for (var i = 0; i < this.completionChecks.Count; i++)
        {
            var check = this.completionChecks[i];
            this.Invariant(
                check.Entry < 0 || !this.Reachable[check.Entry] || check.CanComplete == (check.Exit >= 0 && this.Reachable[check.Exit]),
                check.Entry >= 0 ? this.Operations[check.Entry].Source : null);
        }
    }

    private bool NeedsSourceState(OwnershipOperation operation)
        => operation.Place >= 0 && this.PlaceStorage[operation.Place].Kind is OwnershipPlaceKind.Local or OwnershipPlaceKind.Parameter &&
            (operation.Kind is OwnershipOperationKind.LocateReceiver or OwnershipOperationKind.Read or OwnershipOperationKind.Consume or OwnershipOperationKind.Borrow or OwnershipOperationKind.WriteElement or OwnershipOperationKind.WriteBorrowedField ||
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

            var block = this.checkingBlockOf[region.Entry];
            if (!this.Invariant(!this.Reachable[region.Entry] && block >= 0 && !this.checkingReached[block]))
            {
                continue;
            }

            var ready = true;
            for (var s = 0; s < Math.Max(1, region.SeedCount); s++)
            {
                var seed = region.SeedCount == 0 ? region.Seed : this.CheckingSeeds[region.SeedStart + s].Operation;
                if (!this.Invariant(this.OperationRegions[seed] < i))
                {
                    ready = false;
                    break;
                }

                var runtime = this.Reachable[seed];
                if (!runtime && !this.HasCheckingState(seed))
                {
                    ready = false; // Never discard an unavailable path from a checking join.
                    break;
                }

                this.LoadInput(seed, !runtime);
                this.Transfer(seed);
                this.ReplayChecking(region.SeedCount == 0 ? region.Replay : this.CheckingSeeds[region.SeedStart + s].Replay);
                var destination = this.checkingStates.AsSpan(block * width, width);
                if (s == 0)
                {
                    this.Scratch.AsSpan(0, width).CopyTo(destination);
                }
                else
                {
                    Join(destination, this.Scratch.AsSpan(0, width), this.words);
                }
            }

            if (!ready)
            {
                continue;
            }

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

    private void ReplayChecking(int replay)
    {
        var count = 0;
        for (var current = replay; current >= 0; current = this.CheckingReplays[current].Previous)
        {
            Grow(ref this.checkingReplayStack, count + 1);
            this.checkingReplayStack[count++] = current;
        }

        while (count > 0)
        {
            var path = this.CheckingReplays[this.checkingReplayStack[--count]];
            if (path.Graph)
            {
                this.ReplayCheckingGraph(path);
                continue;
            }

            for (var cursor = path.Entry; ; cursor = this.LinearCheckingSuccessor(cursor))
            {
                if (!this.Invariant(cursor >= 0 && !this.Reachable[cursor]))
                {
                    break;
                }

                this.Transfer(cursor);
                if (cursor == path.End)
                {
                    break;
                }
            }
        }
    }

    private void ReplayCheckingGraph(OwnershipCheckingReplay path)
    {
        var width = this.words * Lanes;
        Grow(ref this.checkingReplayStates, checked(this.checkingBlockCount * width));
        Grow(ref this.checkingReplayReached, this.checkingBlockCount);
        this.checkingReplayReached.AsSpan(0, this.checkingBlockCount).Clear();
        var entry = this.checkingBlockOf[path.Entry];
        if (!this.Invariant(entry >= 0 && this.checkingLeaders[entry] == path.Entry))
        {
            return;
        }

        this.Scratch.AsSpan(0, width).CopyTo(this.checkingReplayStates.AsSpan(entry * width, width));
        this.checkingReplayReached[entry] = true;
        this.Converge(entry, true, path.End, this.checkingReplayStates, this.checkingReplayReached);
        var end = this.checkingBlockOf[path.End];
        if (!this.Invariant(end >= 0 && this.checkingReplayReached[end]))
        {
            return;
        }

        this.RunBlock(end, false, true, path.End, this.checkingReplayStates);
    }

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
                if (!this.Invariant(this.checkingBlockOf[cursor] < 0 && !this.Reachable[cursor]))
                {
                    break;
                }

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
            if (!this.Invariant(cursor >= 0))
            {
                break;
            }
        }
    }

    private void Converge(int entry, bool checking, int stop = -1, ulong[]? replayStates = null, bool[]? replayReached = null)
    {
        var blocks = checking ? this.checkingBlockCount : this.blockCount;
        var states = replayStates ?? (checking ? this.checkingStates : this.BlockStates);
        var reached = replayReached ?? (checking ? this.checkingReached : this.BlockReachable);
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
            var last = this.RunBlock(block, false, checking, stop, replayStates);
            if (last == stop)
            {
                continue;
            }

            var output = this.Scratch.AsSpan(0, width);
            for (var e = this.EdgeHeads[last]; e >= 0; e = this.EdgeStorage[e].Next)
            {
                var edge = this.EdgeStorage[e];
                if (checking && !this.IsCheckingEdge(edge))
                {
                    continue;
                }

                var target = blockOf[edge.To];
                if (!this.Invariant(target >= 0))
                {
                    continue;
                }

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
