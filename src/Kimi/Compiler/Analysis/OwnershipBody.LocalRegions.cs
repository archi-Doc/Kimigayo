// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

public sealed partial class OwnershipBody
{
    private readonly List<LoanFlowPart> loanParts = new();
    private readonly Dictionary<(int Parent, int Selector), int> loanPartIndex = new();

    // One root at a time: storage is bounded by operations * relevant storage paths, independent of the number of borrowed roots.
    private int[] loanFlow = [];
    private bool[] loanQueued = [];
    private int loanFlowRoot = -1;
    private int[] loanSlots = [];
    private int[] loanProjectionSlots = [];

    private static int Combine(int left, int right) => left == -1 ? right : right == -1 ? left : Math.Min(left, right);

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

        this.PrepareLoanParts(root, count);
        var width = this.loanParts.Count;
        var operations = this.Operations.Count;
        Grow(ref this.loanFlow, OwnershipStorage.Cells(operations, width, 32, "local Loan flow"));
        this.loanFlow.AsSpan(0, operations * width).Fill(-1);
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
            for (var slot = 0; slot < width; slot++)
            {
                var value = Outgoing(from, slot);
                var at = (to * width) + slot;
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

        int Outgoing(int id, int slot)
        {
            var part = this.loanParts[slot];
            if (!part.Remainder)
            {
                return -1;
            }

            var holder = part.Place;
            if (this.Places[holder].Kind is not (OwnershipPlaceKind.Local or OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result) ||
                this.HasExplicitDependency(holder) || this.HasStoredBorrowRecord(holder, root))
            {
                return -2;
            }

            var operation = this.Operations[id];
            if (operation.Place == holder && operation.Kind is OwnershipOperationKind.LocateReceiver or OwnershipOperationKind.Read)
            {
                return State(id, slot); // Computing an address or lending a value does not replace its contents.
            }

            if (operation.Projection >= 0 && this.Projections[operation.Projection].Root == holder)
            {
                var written = this.loanProjectionSlots[operation.Projection];
                if (operation.Kind == OwnershipOperationKind.WriteElement)
                {
                    return written >= 0 ? this.LoanPartWithin(slot, written) ? Transfer(id, operation.Input, slot, written) : State(id, slot)
                        : Combine(State(id, slot), Source(id, operation.Input));
                }

                if (operation.Acquisition is AcquisitionKind.Move or AcquisitionKind.CopyOrMove)
                {
                    return written >= 0 && this.LoanPartWithin(slot, written) ? -1 : State(id, slot);
                }

                return State(id, slot);
            }

            // Payload slots are ordinary complete values. Construction gathers their current Loans; an empty Case has none.
            if (operation.Place == holder && FlowOf(operation.Kind) == OperationFlow.PlaceFromInput)
            {
                return Transfer(id, operation.Input, slot, this.loanSlots[holder]);
            }

            if (operation.Place == holder && FlowOf(operation.Kind) == OperationFlow.Construction)
            {
                var construction = this.Constructions[this.OperationSteps[id]];
                var result = -1;
                for (var p = 0; p < construction.PayloadCount; p++)
                {
                    if (part.Parent < 0)
                    {
                        if (!this.loanPartIndex.ContainsKey((slot, p)))
                        {
                            result = Combine(result, Source(id, construction.PayloadStart + p));
                        }
                    }
                    else
                    {
                        var first = slot;
                        while (this.loanParts[first].Parent != this.loanSlots[holder])
                        {
                            first = this.loanParts[first].Parent;
                        }

                        if (this.loanParts[first].Selector == p)
                        {
                            result = Combine(result, Transfer(id, construction.PayloadStart + p, slot, first));
                        }
                    }
                }

                return result;
            }

            return this.HolderChangeAt(id, holder, root, count) switch
            {
                HolderChange.None => State(id, slot),
                HolderChange.Ended => -1,
                HolderChange.Unknown => -2,
                _ => operation.Kind switch
                {
                    OwnershipOperationKind.Write => Transfer(id, operation.Input, slot, this.loanSlots[holder]),
                    OwnershipOperationKind.Consume or OwnershipOperationKind.Borrow => Transfer(id, operation.Place, slot, this.loanSlots[holder]),
                    _ => part.Parent < 0 || this.TypeKeepsRoot(part.Type, root) ? Produced(id, holder) : -1,
                },
            };
        }

        int Produced(int id, int holder)
        {
            var value = this.Values[id];
            if (FlowOf(value.Kind) == ValueFlow.Entries && this.Places[holder].Type.Kind == BoundTypeKind.Closure)
            {
                var result = -1;
                for (var i = 0; i < value.Count; i++)
                {
                    var entry = this.ValueOperands[value.Start + i];
                    var incoming = Source(entry, this.Operations[entry].Place);
                    if (incoming != -1 && (result == -1 || incoming < result))
                    {
                        result = incoming;
                    }
                }

                return result;
            }

            return id;
        }

        int State(int id, int slot) => this.loanFlow[(id * width) + slot];

        int Source(int id, int place)
            => place < 0 ? -2 : place == root ? id
            : this.loanSlots[place] < 0 ? -1
            : this.Places[place].Kind is not (OwnershipPlaceKind.Local or OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result) ? -2
            : this.LoanPartValue(id, this.loanSlots[place]);

        int Transfer(int id, int place, int target, int boundary)
        {
            if (place < 0 || place == root || this.loanSlots[place] < 0 || target == boundary)
            {
                return Source(id, place);
            }

            var matched = Match(this.loanSlots[place], target);
            return matched >= 0 ? this.LoanPartValue(id, matched)
                : this.TypeKeepsRoot(this.loanParts[target].Type, root) ? Source(id, place) : -1;

            int Match(int source, int part)
            {
                if (part == boundary)
                {
                    return source;
                }

                var parent = Match(source, this.loanParts[part].Parent);
                return parent >= 0 && this.loanPartIndex.TryGetValue((parent, this.loanParts[part].Selector), out var child) ? child : -1;
            }
        }
    }

    private void PrepareLoanParts(int root, int count)
    {
        this.loanParts.Clear();
        this.loanPartIndex.Clear();
        Grow(ref this.loanSlots, count);
        this.loanSlots.AsSpan(0, count).Fill(-1);
        Grow(ref this.loanProjectionSlots, this.Projections.Count);
        this.loanProjectionSlots.AsSpan(0, this.Projections.Count).Fill(-1);
        for (var place = 0; place < count; place++)
        {
            if (this.borrowDependencies[(place * count) + root] != LoanRequirement.None)
            {
                this.loanSlots[place] = this.loanParts.Count;
                this.loanParts.Add(new(place, -1, -1, this.Places[place].Type));
            }
        }

        for (var p = 0; p < this.Projections.Count; p++)
        {
            var projection = this.Projections[p];
            var parent = projection.Parent < 0 ? this.loanSlots[projection.Root] : this.loanProjectionSlots[projection.Parent];
            if (parent < 0 || projection.Path != p || projection.Selector < 0)
            {
                continue;
            }

            var key = (parent, projection.Selector);
            if (!this.loanPartIndex.TryGetValue(key, out var slot))
            {
                var owner = this.loanParts[parent];
                var type = this.Concrete(this.Operations[projection.Operation].Source.BoundType)!;
                slot = this.loanParts.Count;
                this.loanPartIndex.Add(key, slot);
                this.loanParts.Add(new(projection.Root, parent, projection.Selector, type) { Next = owner.Child });
                var parts = owner.Type.Kind == BoundTypeKind.Tuple ? owner.Type.Components.Count
                    : owner.Type.Kind == BoundTypeKind.FixedArray ? owner.Type.Length
                    : StructStorage.IsStruct(owner.Type) && owner.Type.StoredBase is null ? StructStorage.Count(owner.Type) : -1;
                this.loanParts[parent] = owner with { Child = slot, Children = owner.Children + 1, Remainder = parts != owner.Children + 1 };
            }

            this.loanProjectionSlots[p] = slot;
        }

        for (var slot = 0; slot < this.loanParts.Count; slot++)
        {
            var part = this.loanParts[slot];
            if (part.Child < 0 || !part.Remainder)
            {
                continue;
            }

            var type = part.Type;
            if (type.Kind == BoundTypeKind.FixedArray)
            {
                this.loanParts[slot] = part with { Remainder = this.TypeKeepsRoot(type.Components[0], root) };
                continue;
            }

            var children = type.Kind == BoundTypeKind.Tuple ? type.Components.Count
                : StructStorage.IsStruct(type) && type.StoredBase is null ? StructStorage.Count(type) : -1;
            var retained = children < 0;
            for (var child = 0; child < children && !retained; child++)
            {
                if (!this.loanPartIndex.ContainsKey((slot, child)))
                {
                    var field = type.Kind == BoundTypeKind.Tuple ? type.Components[child] : StructStorage.FieldType(type, child);
                    retained = field is null || this.TypeKeepsRoot(field, root);
                }
            }

            this.loanParts[slot] = part with { Remainder = retained };
        }
    }

    private bool LoanPartWithin(int part, int ancestor)
    {
        for (; part >= 0; part = this.loanParts[part].Parent)
        {
            if (part == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    private int LoanPartValue(int operation, int slot)
    {
        var part = this.loanParts[slot];
        var result = part.Remainder ? this.loanFlow[(operation * this.loanParts.Count) + slot] : -1;
        for (var child = part.Child; child >= 0; child = this.loanParts[child].Next)
        {
            result = Combine(result, this.LoanPartValue(operation, child));
        }

        return result;
    }

    private bool TypeKeepsRoot(BoundType type, int root)
    {
        if (type.Kind is BoundTypeKind.Function or BoundTypeKind.FunctionItem)
        {
            return false;
        }

        if (type.Origin is { } origin && this.OriginKeepsRoot(origin, root))
        {
            return true;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (this.OriginKeepsRoot(type.OriginArguments[i], root))
            {
                return true;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (this.TypeKeepsRoot(type.Components[i], root))
            {
                return true;
            }
        }

        return false;
    }

    private bool OriginKeepsRoot(BoundOrigin origin, int root)
    {
        if (origin.Kind == OriginKind.Static)
        {
            return false;
        }

        if (Binding.IsLocalRegion(origin))
        {
            foreach (var source in this.Function.CodeContext.Compilation.Binding.LocalRegionSources(origin))
            {
                if (this.OriginKeepsRoot(source, root))
                {
                    return true;
                }
            }

            return false;
        }

        if (origin.Kind == OriginKind.Intersection)
        {
            foreach (var operand in origin.Operands)
            {
                if (this.OriginKeepsRoot(operand, root))
                {
                    return true;
                }
            }

            return false;
        }

        // A result over a borrowed holder also keeps that holder's dependencies. An external or abstract Origin has no
        // local storage to inspect; the already established whole-value dependency remains the conservative bound there.
        return this.ProjectionPlace(origin) is not (>= 0 and var place) || place == root ||
            this.borrowDependencies[(place * this.Places.Count) + root] != LoanRequirement.None;
    }

    private readonly record struct LoanFlowPart(int Place, int Parent, int Selector, BoundType Type)
    {
        internal int Child { get; init; } = -1;

        internal int Next { get; init; } = -1;

        internal int Children { get; init; }

        internal bool Remainder { get; init; } = true;
    }
}
