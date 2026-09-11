// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

public sealed partial class OwnershipBody
{
    // One bit lane per PlaceState flag, in flag order. A block input state is Lanes * words ulongs.
    private const int Lanes = 4;
    private const int MustLane = 0;
    private const int MayLane = 1;
    private const int MovedLane = 2;
    private const int AssignedLane = 3;

    private int words;
    private int blockCount;

    /// <summary>Gets a converged Place state before an operation, or None when the operation is unreachable.</summary>
    /// <param name="operation">The operation ID.</param>
    /// <param name="place">The Place ID.</param>
    /// <returns>The joined state flags.</returns>
    public PlaceState GetInputState(int operation, int place)
    {
        if (!this.Reachable[operation])
        {
            return PlaceState.None;
        }

        // Only block inputs are stored; replay the block prefix instead of retaining a per-operation matrix.
        var block = this.BlockOf[operation];
        var cursor = this.LoadBlock(block);
        while (cursor != operation)
        {
            this.Transfer(this.OperationStorage[cursor]);
            cursor = this.NextInBlock(cursor, block);
        }

        return this.State(place);
    }

    internal void Solve()
    {
        var count = this.OperationStorage.Count;
        this.words = (this.PlaceStorage.Count + 63) >> 6;
        var width = this.words * Lanes;
        Grow(ref this.Reachable, count);
        this.Reachable.AsSpan(0, count).Clear();
        this.PartitionBlocks(count);
        var blocks = this.blockCount;
        Grow(ref this.BlockStates, checked(blocks * width));
        Grow(ref this.Scratch, width);
        Grow(ref this.BlockReachable, blocks);
        Grow(ref this.BlockQueued, blocks);
        Grow(ref this.BlockQueue, blocks);
        this.BlockReachable.AsSpan(0, blocks).Clear();
        this.BlockQueued.AsSpan(0, blocks).Clear();

        // Operation 0 is Entry, so block 0 starts the body with every Place empty.
        this.BlockStates.AsSpan(0, width).Clear();
        this.BlockReachable[0] = true;
        this.BlockQueued[0] = true;
        this.BlockQueue[0] = 0;
        var head = 0;
        var size = 1;
        while (size > 0)
        {
            var block = this.BlockQueue[head];
            head = head + 1 == blocks ? 0 : head + 1;
            size--;
            this.BlockQueued[block] = false;
            var last = this.RunBlock(block, false);
            var output = this.Scratch.AsSpan(0, width);
            for (var e = this.EdgeHeads[last]; e >= 0; e = this.EdgeStorage[e].Next)
            {
                // Every successor of the last operation in a block starts a block.
                var target = this.BlockOf[this.EdgeStorage[e].To];
                var destination = this.BlockStates.AsSpan(target * width, width);
                bool changed;
                if (!this.BlockReachable[target])
                {
                    output.CopyTo(destination);
                    this.BlockReachable[target] = true;
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

        // Diagnose and finalize plans only after convergence, never from intermediate loop states.
        for (var block = 0; block < blocks; block++)
        {
            if (this.BlockReachable[block])
            {
                this.RunBlock(block, true);
            }
        }
    }

    private static void Grow<T>(ref T[] array, int length)
    {
        if (array.Length < length)
        {
            Array.Resize(ref array, Math.Max(length, Math.Max(16, array.Length * 2)));
        }
    }

    private static bool Join(Span<ulong> destination, ReadOnlySpan<ulong> source, int words)
    {
        // Must facts intersect; possible histories union. No guessed winner path.
        var changed = false;
        for (var i = 0; i < words; i++)
        {
            var old = destination[i];
            var merged = old & source[i];
            changed |= merged != old;
            destination[i] = merged;
        }

        for (var i = words; i < destination.Length; i++)
        {
            var old = destination[i];
            var merged = old | source[i];
            changed |= merged != old;
            destination[i] = merged;
        }

        return changed;
    }

    private static CleanupAction Destruction(PlaceState state)
        => (state & PlaceState.MustInit) != 0 ? CleanupAction.Destroy : (state & PlaceState.MayInit) != 0 ? CleanupAction.Conditional : CleanupAction.Skip;

    private void PartitionBlocks(int count)
    {
        Grow(ref this.IncomingCounts, count);
        Grow(ref this.BlockOf, count);
        this.IncomingCounts.AsSpan(0, count).Clear();
        this.BlockOf.AsSpan(0, count).Fill(-1);
        for (var i = 0; i < this.EdgeStorage.Count; i++)
        {
            this.IncomingCounts[this.EdgeStorage[i].To]++;
        }

        // A block is a maximal single-entry, single-exit chain. Operations of an unreachable
        // cycle without a leader stay unassigned and are never reachable.
        this.blockCount = 0;
        for (var operation = 0; operation < count; operation++)
        {
            if (operation != 0 && this.IncomingCounts[operation] == 1 && this.HasSingleSuccessor(this.EdgeStorage[this.IncomingEdges[operation]].From))
            {
                continue;
            }

            Grow(ref this.BlockLeaders, this.blockCount + 1);
            this.BlockLeaders[this.blockCount] = operation;
            for (var cursor = operation; cursor >= 0; cursor = this.ChainSuccessor(cursor))
            {
                this.BlockOf[cursor] = this.blockCount;
            }

            this.blockCount++;
        }
    }

    private bool HasSingleSuccessor(int operation)
    {
        var edge = this.EdgeHeads[operation];
        return edge >= 0 && this.EdgeStorage[edge].Next < 0;
    }

    private int ChainSuccessor(int operation)
    {
        if (!this.HasSingleSuccessor(operation))
        {
            return -1;
        }

        var next = this.EdgeStorage[this.EdgeHeads[operation]].To;
        return next != 0 && this.IncomingCounts[next] == 1 ? next : -1;
    }

    private int NextInBlock(int operation, int block)
    {
        var next = this.ChainSuccessor(operation);
        return next >= 0 && this.BlockOf[next] == block ? next : -1;
    }

    private int LoadBlock(int block)
    {
        var width = this.words * Lanes;
        this.BlockStates.AsSpan(block * width, width).CopyTo(this.Scratch);
        return this.BlockLeaders[block];
    }

    private int RunBlock(int block, bool finalize)
    {
        var operation = this.LoadBlock(block);
        while (true)
        {
            if (finalize)
            {
                this.Reachable[operation] = true;
                this.Finalize(operation);
            }

            this.Transfer(this.OperationStorage[operation]);
            var next = this.NextInBlock(operation, block);
            if (next < 0)
            {
                return operation;
            }

            operation = next;
        }
    }

    private void Finalize(int index)
    {
        var operation = this.OperationStorage[index];
        if (operation.Place < 0)
        {
            return;
        }

        var state = this.State(operation.Place);
        switch (operation.Kind)
        {
            case OwnershipOperationKind.Read or OwnershipOperationKind.Consume or OwnershipOperationKind.Borrow or OwnershipOperationKind.CallEntry or OwnershipOperationKind.Deliver:
                this.CheckInitialized(operation, operation.Place, state);
                break;
            case OwnershipOperationKind.Cleanup:
                var step = this.OperationSteps[index];
                this.CleanupStepStorage[step] = this.CleanupStepStorage[step] with { Action = Destruction(state) };
                break;
            case OwnershipOperationKind.Write:
                var place = this.PlaceStorage[operation.Place];
                if (place.Kind == OwnershipPlaceKind.Local && !place.Mutable && (state & PlaceState.MayAssigned) != 0)
                {
                    this.IssueStorage.Add(new(operation.Source, OwnershipFailure.ReassignedLet, operation.Place));
                }

                if (operation.Input >= 0)
                {
                    this.CheckInitialized(operation, operation.Input, this.State(operation.Input));
                }

                // The input state already follows RHS acquisition, so x = x skips the moved old value.
                var placement = (state & PlaceState.MustInit) != 0 ? PlacementKind.Replacement :
                    (state & PlaceState.MayInit) != 0 ? PlacementKind.ConditionalReplacement :
                    (state & PlaceState.MayAssigned) == 0 ? PlacementKind.Initialization :
                    (state & PlaceState.MayMoved) != 0 ? PlacementKind.Reinitialization : PlacementKind.EmptyPlacement;
                this.OperationStorage[index] = operation with { Placement = placement };
                this.CleanupPlanStorage.Add(new(this.IncomingEdges[index], this.CleanupStepStorage.Count, 1, CleanupReason.Replacement));
                this.CleanupStepStorage.Add(new(index, operation.Place, place.Source, Destruction(state)));
                break;
        }
    }

    private void CheckInitialized(OwnershipOperation operation, int place, PlaceState state)
    {
        if ((state & PlaceState.MustInit) == 0)
        {
            this.IssueStorage.Add(new(
                operation.Source,
                (state & PlaceState.MayMoved) != 0 ? OwnershipFailure.PossiblyMovedUse : OwnershipFailure.UninitializedUse,
                place));
        }
    }

    private void Transfer(OwnershipOperation operation)
    {
        var place = operation.Place;
        if (place < 0)
        {
            return;
        }

        switch (operation.Kind)
        {
            case OwnershipOperationKind.Declare:
                // A fresh dynamic binding lifetime, including each loop iteration.
                this.Clear(place, MustLane);
                this.Clear(place, MayLane);
                this.Clear(place, MovedLane);
                this.Clear(place, AssignedLane);
                break;
            case OwnershipOperationKind.Produce:
                this.Initialize(place);
                break;
            case OwnershipOperationKind.Consume:
                if (operation.Acquisition == AcquisitionKind.Move)
                {
                    this.Move(place);
                }
                else if (operation.Acquisition == AcquisitionKind.CopyOrMove)
                {
                    // Keep the possible Copy's initialization, but no later use may rely on it.
                    this.Clear(place, MustLane);
                    this.Set(place, MovedLane);
                }

                if (operation.Input >= 0)
                {
                    this.Initialize(operation.Input);
                }

                break;
            case OwnershipOperationKind.Write:
                if (operation.Input >= 0)
                {
                    this.Move(operation.Input);
                }

                this.Initialize(place);
                break;
            case OwnershipOperationKind.CallEntry:
            case OwnershipOperationKind.Deliver:
                this.Move(place);
                break;
            case OwnershipOperationKind.Cleanup:
                this.Clear(place, MustLane);
                this.Clear(place, MayLane);
                break;
        }
    }

    private PlaceState State(int place)
    {
        var word = place >> 6;
        var bit = 1UL << place;
        var scratch = this.Scratch;
        return (PlaceState)(((scratch[word] & bit) != 0 ? (int)PlaceState.MustInit : 0) |
            ((scratch[this.words + word] & bit) != 0 ? (int)PlaceState.MayInit : 0) |
            ((scratch[(MovedLane * this.words) + word] & bit) != 0 ? (int)PlaceState.MayMoved : 0) |
            ((scratch[(AssignedLane * this.words) + word] & bit) != 0 ? (int)PlaceState.MayAssigned : 0));
    }

    private void Initialize(int place)
    {
        this.Set(place, MustLane);
        this.Set(place, MayLane);
        this.Clear(place, MovedLane);
        this.Set(place, AssignedLane);
    }

    private void Move(int place)
    {
        // Assignment history survives, so moving a let never permits assigning it again.
        this.Clear(place, MustLane);
        this.Clear(place, MayLane);
        this.Set(place, MovedLane);
    }

    private void Set(int place, int lane) => this.Scratch[(lane * this.words) + (place >> 6)] |= 1UL << place;

    private void Clear(int place, int lane) => this.Scratch[(lane * this.words) + (place >> 6)] &= ~(1UL << place);
}
