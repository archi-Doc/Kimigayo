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

        this.LoadInput(operation, false);
        return this.CompleteState(place);
    }

    internal void Solve()
    {
        var count = this.OperationStorage.Count;
        this.PrepareMovePaths();
        this.words = (this.PlaceStorage.Count + (this.MovePathCount * 2) + 63) >> 6;
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
        this.Converge(0, false);

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

    private int NextInBlock(int operation, int block, bool checking = false)
    {
        if (checking)
        {
            return this.checkingNext[operation];
        }

        var next = this.ChainSuccessor(operation);
        return next >= 0 && this.BlockOf[next] == block ? next : -1;
    }

    private int LoadBlock(int block, bool checking = false, ulong[]? replayStates = null)
    {
        var width = this.words * Lanes;
        var states = replayStates ?? (checking ? this.checkingStates : this.BlockStates);
        states.AsSpan(block * width, width).CopyTo(this.Scratch);
        return checking ? this.checkingLeaders[block] : this.BlockLeaders[block];
    }

    private int RunBlock(int block, bool finalize, bool checking = false, int stop = -1, ulong[]? replayStates = null)
    {
        var operation = this.LoadBlock(block, checking, replayStates);
        while (true)
        {
            if (finalize)
            {
                if (!checking)
                {
                    this.Reachable[operation] = true;
                    this.Finalize(operation);
                }
                else if (this.OperationStorage[operation].Kind != OwnershipOperationKind.Deliver)
                {
                    this.CheckOperation(operation);
                }
            }

            this.Transfer(operation);
            var next = this.NextInBlock(operation, block, checking);
            if (next < 0 || operation == stop)
            {
                return operation;
            }

            operation = next;
        }
    }

    private void Finalize(int index)
    {
        this.CheckOperation(index);
        var operation = this.OperationStorage[index];
        if (operation.Place < 0)
        {
            return;
        }

        var state = this.State(operation.Place);
        if (operation.Kind == OwnershipOperationKind.Cleanup)
        {
            var step = this.OperationSteps[index];
            this.CleanupStepStorage[step] = this.CleanupStepStorage[step] with { Action = Destruction(state) };
        }
        else if (operation.Kind == OwnershipOperationKind.Write)
        {
            var place = this.PlaceStorage[operation.Place];
            // The input state already follows RHS acquisition, so x = x skips the moved old value.
            var placement = (state & PlaceState.MustInit) != 0 ? PlacementKind.Replacement :
                (state & PlaceState.MayInit) != 0 ? PlacementKind.ConditionalReplacement :
                (state & PlaceState.MayAssigned) == 0 ? PlacementKind.Initialization :
                (state & PlaceState.MayMoved) != 0 ? PlacementKind.Reinitialization : PlacementKind.EmptyPlacement;
            this.OperationStorage[index] = operation with { Placement = placement };
            if (place.Kind == OwnershipPlaceKind.Result)
            {
                return;
            }

            // Replacement belongs to the Write point, after all incoming states join.
            this.OperationSteps[index] = this.CleanupStepStorage.Count;
            this.CleanupStepStorage.Add(new(index, operation.Place, place.Source, Destruction(state)));
        }
    }

    // Shared diagnostics only: checking continuations must never finalize runtime plans.
    private void CheckOperation(int index)
    {
        var operation = this.OperationStorage[index];
        if (operation.Place < 0)
        {
            return;
        }

        var state = this.State(operation.Place);
        switch (operation.Kind)
        {
            case OwnershipOperationKind.WriteBorrowedField:
                this.CheckInitialized(operation, operation.Place, this.CompleteState(operation.Place));
                this.CheckInitialized(operation, operation.Input, this.CompleteState(operation.Input));
                break;
            case OwnershipOperationKind.PayloadPlacement:
            case OwnershipOperationKind.InitializeSubject:
                if (operation.Input >= 0)
                {
                    this.CheckInitialized(operation, operation.Input, this.CompleteState(operation.Input));
                }

                break;
            case OwnershipOperationKind.CompleteConstruction:
                var construction = this.ConstructionStorage[this.OperationSteps[index]];
                for (var i = 0; i < construction.PayloadCount; i++)
                {
                    var payload = construction.PayloadStart + i;
                    this.CheckInitialized(operation, payload, this.CompleteState(payload));
                }

                break;
            case OwnershipOperationKind.Produce or OwnershipOperationKind.Read or OwnershipOperationKind.Borrow when operation.Projection >= 0:
                this.CheckInitialized(operation, this.Projections[operation.Projection].Root, this.ElementState(operation.Projection, false));
                break;
            case OwnershipOperationKind.LocateReceiver:
                this.CheckInitialized(operation, operation.Place, state);
                break;
            case OwnershipOperationKind.ProjectElement:
                this.CheckInitialized(operation, operation.Place, this.ElementState(operation.Projection, true));
                break;
            case OwnershipOperationKind.Read or OwnershipOperationKind.Consume or OwnershipOperationKind.Borrow or OwnershipOperationKind.CallEntry or OwnershipOperationKind.Deliver or OwnershipOperationKind.DecomposeCase or OwnershipOperationKind.AcquirePattern or OwnershipOperationKind.PatternTest:
            case OwnershipOperationKind.CheckReceiverField:
                this.CheckInitialized(operation, operation.Place, this.CompleteState(operation.Place));
                break;
            case OwnershipOperationKind.Write:
            case OwnershipOperationKind.WriteElement:
                var place = this.PlaceStorage[operation.Place];
                if (operation.Kind == OwnershipOperationKind.WriteElement)
                {
                    this.CheckInitialized(operation, operation.Place, this.ElementState(operation.Projection, true));
                }

                if (place.Kind == OwnershipPlaceKind.Local && !place.Mutable && (state & PlaceState.MayAssigned) != 0)
                {
                    this.ReportIssue(new(operation.Source, OwnershipFailure.ReassignedLet, operation.Place));
                }

                if (operation.Input >= 0)
                {
                    this.CheckInitialized(operation, operation.Input, this.CompleteState(operation.Input));
                }

                break;
        }
    }

    private void CheckInitialized(OwnershipOperation operation, int place, PlaceState state)
    {
        if ((state & PlaceState.MustInit) == 0)
        {
            this.ReportIssue(new(
                operation.Source,
                (state & PlaceState.MayMoved) != 0 ? OwnershipFailure.PossiblyMovedUse : OwnershipFailure.UninitializedUse,
                place));
        }
    }

    private void Transfer(int index)
    {
        var operation = this.OperationStorage[index];
        var place = operation.Place;
        if (place < 0)
        {
            return;
        }

        switch (operation.Kind)
        {
            case OwnershipOperationKind.DecomposeCase:
                var decomposition = this.DecompositionStorage[this.OperationSteps[index]];
                this.Move(place);
                for (var i = 0; i < decomposition.PayloadCount; i++)
                {
                    this.Initialize(decomposition.PayloadStart + i);
                }

                break;
            case OwnershipOperationKind.CompleteConstruction:
                var construction = this.ConstructionStorage[this.OperationSteps[index]];
                for (var i = 0; i < construction.PayloadCount; i++)
                {
                    this.Move(construction.PayloadStart + i);
                }

                this.Initialize(place);
                break;
            case OwnershipOperationKind.Declare:
                // A fresh dynamic binding lifetime, including each loop iteration.
                this.Clear(place, MustLane);
                this.Clear(place, MayLane);
                this.Clear(place, MovedLane);
                this.Clear(place, AssignedLane);
                if (this.moveRoots[place] >= 0)
                {
                    this.SetPathState(this.moveRoots[place], false, true);
                }

                break;
            case OwnershipOperationKind.Produce:
                if (operation.Projection >= 0 && operation.Acquisition is AcquisitionKind.Move or AcquisitionKind.CopyOrMove && this.projectionPaths[operation.Projection] >= 0)
                {
                    this.SetPathState(this.projectionPaths[operation.Projection], false, conditional: operation.Acquisition == AcquisitionKind.CopyOrMove);
                }

                this.Initialize(place);
                break;
            case OwnershipOperationKind.Consume:
            case OwnershipOperationKind.AcquirePattern:
            case OwnershipOperationKind.Borrow:
            case OwnershipOperationKind.Read:
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
            case OwnershipOperationKind.PayloadPlacement:
            case OwnershipOperationKind.InitializeSubject:
            case OwnershipOperationKind.InitializeReceiverField:
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
            case OwnershipOperationKind.WriteElement:
                this.Move(operation.Input);
                if (this.projectionPaths[operation.Projection] >= 0 && this.Projections[operation.Projection].Path == operation.Projection)
                {
                    this.SetPathState(this.projectionPaths[operation.Projection], true);
                }

                break;
            case OwnershipOperationKind.Cleanup:
                this.Clear(place, MustLane);
                this.Clear(place, MayLane);
                if (this.moveRoots[place] >= 0)
                {
                    this.SetPathState(this.moveRoots[place], false, cleanup: true);
                }

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
        if (place < this.Places.Count && this.moveRoots[place] >= 0)
        {
            this.SetPathState(this.moveRoots[place], true);
        }
    }

    private void Move(int place)
    {
        // Assignment history survives, so moving a let never permits assigning it again.
        this.Clear(place, MustLane);
        this.Clear(place, MayLane);
        this.Set(place, MovedLane);
        if (place < this.Places.Count && this.moveRoots[place] >= 0)
        {
            this.SetPathState(this.moveRoots[place], false);
        }
    }

    private void Set(int place, int lane) => this.Scratch[(lane * this.words) + (place >> 6)] |= 1UL << place;

    private void Clear(int place, int lane) => this.Scratch[(lane * this.words) + (place >> 6)] &= ~(1UL << place);
}
