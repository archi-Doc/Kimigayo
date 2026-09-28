// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

public sealed partial class OwnershipBody
{
    // Canonical, sparse paths. Storage existence and the untracked remainder are
    // separate facts: moving every child does not undo its parent's construction.
    private readonly List<MovePath> movePaths = new();
    private readonly Dictionary<(int Parent, int Selector), int> movePathIndex = new();
    private readonly List<(int Parent, int DescendingSelector, int Path)> movePathOrder = new();
    private int[] moveRoots = [];
    private int[] projectionPaths = [];

    internal int MovePathCount => this.movePaths.Count;

    internal MovePath GetMovePath(int path) => this.movePaths[path];

    internal int MoveRoot(int place) => this.moveRoots[place];

    internal int ProjectionPath(int projection) => this.projectionPaths[projection];

    internal int PathSlot(int path, bool remainder = true) => this.Places.Count + (path * 2) + (remainder ? 1 : 0);

    internal PlaceState GetStorageState(int operation, int place)
    {
        if (!this.Reachable[operation])
        {
            return PlaceState.None;
        }

        this.LoadInput(operation, false);
        return this.State(place);
    }

    internal PlaceState GetElementState(int operation, int projection, bool receiver = false)
    {
        if (!this.Reachable[operation])
        {
            return PlaceState.None;
        }

        this.LoadInput(operation, false);
        return this.ElementState(projection, receiver);
    }

    // The cleanup collector loads one converged snapshot, then reads every sparse
    // remainder from it without replaying the block separately for each element.
    internal void LoadPathInput(int operation) => this.LoadInput(operation, false);

    internal PlaceState CurrentPathState(int path) => this.PathState(path);

    internal PlaceState CurrentRemainderState(int path) => this.State(this.PathSlot(path));

    internal bool IsPathWithin(int path, int ancestor)
    {
        for (; path >= 0; path = this.movePaths[path].Parent)
        {
            if (path == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    private static PlaceState Combine(PlaceState left, PlaceState right)
        => ((left | right) & ~PlaceState.MustInit) | (left & right & PlaceState.MustInit);

    private void PrepareMovePaths()
    {
        this.movePaths.Clear();
        this.movePathIndex.Clear();
        this.movePathOrder.Clear();
        Grow(ref this.moveRoots, this.Places.Count);
        Grow(ref this.projectionPaths, this.Projections.Count);
        this.moveRoots.AsSpan(0, this.Places.Count).Fill(-1);
        this.projectionPaths.AsSpan(0, this.Projections.Count).Fill(-1);
        foreach (var plan in this.Projections)
        {
            if (plan.Output >= 0 && this.Operations[plan.Output].Acquisition is AcquisitionKind.Move or AcquisitionKind.CopyOrMove && this.moveRoots[plan.Root] < 0)
            {
                this.moveRoots[plan.Root] = this.movePaths.Count;
                this.movePaths.Add(new(plan.Root, -1, -1, this.Places[plan.Root].Type));
            }
        }

        for (var i = 0; i < this.Projections.Count; i++)
        {
            var plan = this.Projections[i];
            var parent = plan.Parent < 0 ? this.moveRoots[plan.Root] : this.projectionPaths[plan.Parent];
            if (parent < 0)
            {
                continue;
            }

            if (plan.Path != i)
            {
                this.projectionPaths[i] = parent;
                continue;
            }

            var key = (parent, plan.Selector);
            if (!this.movePathIndex.TryGetValue(key, out var path))
            {
                path = this.movePaths.Count;
                this.movePathIndex.Add(key, path);
                var owner = this.movePaths[parent];
                this.movePaths.Add(new(plan.Root, parent, plan.Selector, this.Operations[plan.Operation].Source.BoundType!));
                this.movePaths[parent] = owner with { Children = owner.Children + 1 };
                this.movePathOrder.Add((parent, -plan.Selector, path));
            }

            this.projectionPaths[i] = path;
        }

        // Sort once; inserting ascending array selectors into a linked list would
        // otherwise make canonicalization quadratic in the number of source paths.
        this.movePathOrder.Sort();
        for (var i = 0; i < this.movePathOrder.Count; i++)
        {
            var link = this.movePathOrder[i];
            if (i == 0 || this.movePathOrder[i - 1].Parent != link.Parent)
            {
                this.movePaths[link.Parent] = this.movePaths[link.Parent] with { Child = link.Path };
            }
            else
            {
                var previous = this.movePathOrder[i - 1].Path;
                this.movePaths[previous] = this.movePaths[previous] with { Next = link.Path };
            }
        }
    }

    private PlaceState CompleteState(int place)
        => this.moveRoots[place] is >= 0 and var path ? Combine(this.State(place), this.PathState(path)) : this.State(place);

    // Selectors are leaf-first. Untracked children share the parent's remainder;
    // tracked siblings do not affect the selected child's initialization.
    private PlaceState InlinePathState(int place, ReadOnlySpan<int> selectors)
    {
        var state = this.State(place);
        var path = this.moveRoots[place];
        if (path < 0)
        {
            return state;
        }

        for (var i = selectors.Length - 1; i >= 0; i--)
        {
            state = Combine(state, this.State(this.PathSlot(path, false)));
            if (!this.movePathIndex.TryGetValue((path, selectors[i]), out var child))
            {
                return this.movePaths[path].HasRemainder ? Combine(state, this.State(this.PathSlot(path))) : PlaceState.None;
            }

            path = child;
        }

        return Combine(state, this.PathState(path));
    }

    private PlaceState PathState(int path)
    {
        var node = this.movePaths[path];
        var state = this.State(this.PathSlot(path, false));
        if (node.HasRemainder)
        {
            state = Combine(state, this.State(this.PathSlot(path)));
        }

        for (var child = node.Child; child >= 0; child = this.movePaths[child].Next)
        {
            state = Combine(state, this.PathState(child));
        }

        return state;
    }

    private PlaceState ElementState(int projection, bool receiver)
    {
        var plan = this.Projections[projection];
        var path = this.projectionPaths[projection];
        if (path < 0)
        {
            return this.State(plan.Root);
        }

        if (!receiver || plan.Path != projection)
        {
            return this.PathState(path);
        }

        var state = this.State(plan.Root);
        for (var parent = this.movePaths[path].Parent; parent >= 0; parent = this.movePaths[parent].Parent)
        {
            state = Combine(state, this.State(this.PathSlot(parent, false)));
        }

        return state;
    }

    private void SetPathState(int path, bool initialized, bool declare = false, bool cleanup = false, bool conditional = false)
    {
        for (var slot = this.PathSlot(path, false); slot <= this.PathSlot(path); slot++)
        {
            if (initialized)
            {
                this.Initialize(slot);
            }
            else
            {
                if (cleanup)
                {
                    this.Clear(slot, MustLane);
                    this.Clear(slot, MayLane);
                }
                else if (conditional)
                {
                    this.Clear(slot, MustLane);
                    this.Set(slot, MovedLane);
                }
                else
                {
                    this.Move(slot);
                }

                if (declare)
                {
                    this.Clear(slot, MovedLane);
                    this.Clear(slot, AssignedLane);
                }
            }
        }

        for (var child = this.movePaths[path].Child; child >= 0; child = this.movePaths[child].Next)
        {
            this.SetPathState(child, initialized, declare, cleanup, conditional);
        }
    }
}

internal readonly record struct MovePath(int Root, int Parent, int Selector, BoundType Type, int Child = -1, int Next = -1, int Children = 0)
{
    internal int Count => this.Type.Kind == BoundTypeKind.FixedArray ? (int)this.Type.Length : this.Type.Kind == BoundTypeKind.Tuple ? this.Type.Components.Count : StructStorage.Count(this.Type);

    internal bool HasRemainder => this.Children < this.Count || this.Count == 0 || this.Type.StoredBase is not null;
}
