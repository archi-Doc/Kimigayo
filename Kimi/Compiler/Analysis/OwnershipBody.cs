// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

public sealed partial class OwnershipBody
{
    private const byte Initialized = (byte)(PlaceState.MustInit | PlaceState.MayInit | PlaceState.MayAssigned);

    internal void Solve()
    {
        var count = this.OperationStorage.Count;
        var placeCount = this.PlaceStorage.Count;
        var size = checked(count * placeCount);
        Grow(ref this.StateStorage, size);
        Grow(ref this.StateScratch, placeCount);
        Grow(ref this.Reachable, count);
        Grow(ref this.Queued, count);
        this.StateStorage.AsSpan(0, size).Clear();
        this.Reachable.AsSpan(0, count).Clear();
        this.Queued.AsSpan(0, count).Clear();
        this.Worklist.Clear();
        this.Reachable[0] = true;
        this.Queued[0] = true;
        this.Worklist.Add(0);
        for (var cursor = 0; cursor < this.Worklist.Count; cursor++)
        {
            var operation = this.Worklist[cursor];
            this.Queued[operation] = false;
            this.StateStorage.AsSpan(operation * placeCount, placeCount).CopyTo(this.StateScratch);
            this.Transfer(this.OperationStorage[operation]);
            for (var e = this.EdgeHeads[operation]; e >= 0; e = this.EdgeStorage[e].Next)
            {
                var target = this.EdgeStorage[e].To;
                var destination = this.StateStorage.AsSpan(target * placeCount, placeCount);
                var changed = !this.Reachable[target];
                if (changed)
                {
                    this.StateScratch.AsSpan(0, placeCount).CopyTo(destination);
                    this.Reachable[target] = true;
                }
                else
                {
                    for (var p = 0; p < placeCount; p++)
                    {
                        var old = destination[p];
                        // Must facts intersect; possible histories union. No guessed winner path.
                        var merged = (byte)(((old & this.StateScratch[p]) & (byte)PlaceState.MustInit) |
                            ((old | this.StateScratch[p]) & ~(byte)PlaceState.MustInit));
                        changed |= old != merged;
                        destination[p] = merged;
                    }
                }

                if (changed && !this.Queued[target])
                {
                    this.Queued[target] = true;
                    this.Worklist.Add(target);
                }
            }
        }

        // Diagnose and finalize plans only after convergence, never from intermediate loop StateStorage.
        for (var i = 0; i < count; i++)
        {
            if (!this.Reachable[i])
            {
                continue;
            }

            var operation = this.OperationStorage[i];
            if (operation.Place >= 0)
            {
                var state = this.GetInputState(i, operation.Place);
                if (operation.Kind is OwnershipOperationKind.Read or OwnershipOperationKind.Consume or OwnershipOperationKind.Borrow or OwnershipOperationKind.CallEntry or OwnershipOperationKind.Deliver)
                {
                    this.CheckInitialized(operation, operation.Place, state);
                }
                else if (operation.Kind == OwnershipOperationKind.Write)
                {
                    var place = this.PlaceStorage[operation.Place];
                    if (place.Kind == OwnershipPlaceKind.Local && !place.Mutable && (state & PlaceState.MayAssigned) != 0)
                    {
                        this.IssueStorage.Add(new(operation.Source, OwnershipFailure.ReassignedLet, operation.Place));
                    }

                    if (operation.Input >= 0)
                    {
                        this.CheckInitialized(operation, operation.Input, this.GetInputState(i, operation.Input));
                    }

                    var placement = (state & PlaceState.MustInit) != 0 ? PlacementKind.Replacement :
                        (state & PlaceState.MayInit) != 0 ? PlacementKind.ConditionalReplacement :
                        (state & PlaceState.MayAssigned) == 0 ? PlacementKind.Initialization :
                        (state & PlaceState.MayMoved) != 0 ? PlacementKind.Reinitialization : PlacementKind.EmptyPlacement;
                    this.OperationStorage[i] = operation with { Placement = placement };
                    var start = this.CleanupStepStorage.Count;
                    this.CleanupStepStorage.Add(new(i, operation.Place, place.Source, Destruction(state)));
                    this.CleanupPlanStorage.Add(new(this.IncomingEdges[i], start, 1, CleanupReason.Replacement));
                }
            }
        }

        for (var i = 0; i < this.CleanupStepStorage.Count; i++)
        {
            var step = this.CleanupStepStorage[i];
            if (step.Place >= 0)
            {
                this.CleanupStepStorage[i] = step with
                {
                    Action = this.Reachable[step.Operation] ? Destruction(this.GetInputState(step.Operation, step.Place)) : CleanupAction.Skip,
                };
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

    private static CleanupAction Destruction(PlaceState state)
        => (state & PlaceState.MustInit) != 0 ? CleanupAction.Destroy : (state & PlaceState.MayInit) != 0 ? CleanupAction.Conditional : CleanupAction.Skip;

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
                this.StateScratch[place] = 0; // A fresh dynamic binding lifetime, including each loop iteration.
                break;
            case OwnershipOperationKind.Produce:
                this.StateScratch[place] = Initialized;
                break;
            case OwnershipOperationKind.Consume:
                if (operation.Acquisition == AcquisitionKind.Move)
                {
                    this.Move(place);
                }
                else if (operation.Acquisition == AcquisitionKind.CopyOrMove)
                {
                    this.StateScratch[place] = (byte)((this.StateScratch[place] & ~(byte)PlaceState.MustInit) | (byte)PlaceState.MayMoved);
                }

                if (operation.Input >= 0)
                {
                    this.StateScratch[operation.Input] = Initialized;
                }

                break;
            case OwnershipOperationKind.Write:
                if (operation.Input >= 0)
                {
                    this.Move(operation.Input);
                }

                this.StateScratch[place] = Initialized;
                break;
            case OwnershipOperationKind.CallEntry:
            case OwnershipOperationKind.Deliver:
                this.Move(place);
                break;
            case OwnershipOperationKind.Cleanup:
                this.StateScratch[place] &= unchecked((byte)~(byte)(PlaceState.MustInit | PlaceState.MayInit));
                break;
        }
    }

    private void Move(int place)
        => this.StateScratch[place] = (byte)((this.StateScratch[place] & ~(byte)(PlaceState.MustInit | PlaceState.MayInit)) | (byte)PlaceState.MayMoved);
}
