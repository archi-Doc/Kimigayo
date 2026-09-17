// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipBody
{
    private bool[] borrowLive = [];
    private LoanRequirement[] borrowDependencies = [];
    private int[] slicePaths = [];

    // Borrow validity follows CFG uses, including the implicit use by deinit.
    // Types retain Origin identity through Copy, Move, calls and field storage.
    internal void VerifyBorrows()
    {
        var count = this.Places.Count;
        var dependent = false;
        for (var p = 0; p < count && !dependent; p++)
        {
            dependent = HasProjection(this.Places[p].Type);
        }

        if (!dependent)
        {
            return;
        }

        Grow(ref this.borrowDependencies, checked(count * count));
        this.borrowDependencies.AsSpan(0, count * count).Clear();
        var any = false;
        for (var p = 0; p < count; p++)
        {
            AddType(p, this.Places[p].Type);
        }

        if (!any)
        {
            return;
        }

        this.PrepareSlicePaths();

        Grow(ref this.borrowLive, checked(count * this.Operations.Count));
        this.borrowLive.AsSpan(0, count * this.Operations.Count).Clear();
        bool changed;
        do
        {
            changed = false;
            for (var op = this.Operations.Count - 1; op >= 0; op--)
            {
                for (var p = 0; p < count; p++)
                {
                    var live = Uses(op, p);
                    if (!Kills(this.Operations[op], p))
                    {
                        for (var e = this.EdgeHeads[op]; e >= 0 && !live; e = this.Edges[e].Next)
                        {
                            var edge = this.Edges[e];
                            live |= edge.Kind != OwnershipEdgeKind.Abort && this.borrowLive[(edge.To * count) + p];
                        }
                    }

                    var at = (op * count) + p;
                    changed |= live != this.borrowLive[at];
                    this.borrowLive[at] = live;
                }
            }
        }
        while (changed);

        for (var op = 0; op < this.Operations.Count; op++)
        {
            if (!this.IsReachable(op) && !this.HasCheckingState(op))
            {
                continue;
            }

            var operation = this.Operations[op];
            for (var p = 0; p < count; p++)
            {
                if (!this.borrowLive[(op * count) + p] || (BorrowState(op, p) & PlaceState.MayInit) == 0)
                {
                    continue;
                }

                for (var root = 0; root < count; root++)
                {
                    var mode = this.borrowDependencies[(p * count) + root];
                    if (mode == LoanRequirement.None)
                    {
                        continue;
                    }

                    var external = this.Places[root].Kind == OwnershipPlaceKind.Parameter && ReferenceTypes.IsStorage(this.Places[root].Type);
                    var accessConflict = ConflictsWithComparison(operation.Kind, operation.Place, operation.Input, operation.Acquisition, root, mode, operation.LoanMode);
                    if (this.Places[p].Type.Kind == BoundTypeKind.Slice && operation.Projection >= 0 && this.Projections[operation.Projection].Root == root)
                    {
                        var modifies = operation.Kind == OwnershipOperationKind.WriteElement ||
                            (operation.Kind == OwnershipOperationKind.Produce && operation.Acquisition is AcquisitionKind.Move or AcquisitionKind.CopyOrMove);
                        if (modifies)
                        {
                            accessConflict = this.slicePaths[p] < 0 || this.ElementPathsOverlap(operation.Projection, this.slicePaths[p]);
                        }
                    }

                    var conflict = !external && ((BorrowState(op, root) & PlaceState.MustInit) == 0 || accessConflict);
                    var value = this.Values[op];
                    if (value.Kind is OwnershipValueKind.BorrowedField or OwnershipValueKind.BorrowedFieldWrite or OwnershipValueKind.Address or OwnershipValueKind.Sequence && value.Count > 0)
                    {
                        var receiver = this.ValueOperands[value.Start];
                        var sourcePlace = ValuePlaceForBorrow(this.Operations[receiver]);
                        var access = value.Kind == OwnershipValueKind.BorrowedFieldWrite ? LoanRequirement.Uniq
                            : value.Kind == OwnershipValueKind.Address ? operation.LoanMode : LoanRequirement.Ref;
                        if (sourcePlace >= 0 && this.borrowDependencies[(sourcePlace * count) + root] != LoanRequirement.None &&
                            (mode == LoanRequirement.Uniq || access == LoanRequirement.Uniq || this.Places[sourcePlace].Type.Semantics == SemanticsKind.Uniq) && !this.IsBorrowAncestor(receiver, p))
                        {
                            conflict = true;
                        }
                    }

                    if (conflict)
                    {
                        this.ReportIssue(new(operation.Source, OwnershipFailure.ComparisonLoanConflict));
                    }
                }
            }
        }

        PlaceState BorrowState(int operation, int place) => this.IsReachable(operation) ? this.GetInputState(operation, place) : this.GetCheckingInputState(operation, place);

        void AddType(int place, BoundType type)
        {
            if (ReferenceTypes.IsString(type))
            {
                return; // The existing projection-aware call/guard Loan plans verify this representation.
            }

            if (type.Origin is { } origin)
            {
                AddOrigin(place, origin, type.Semantics == SemanticsKind.Uniq ? LoanRequirement.Uniq : LoanRequirement.Ref);
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                AddOrigin(place, type.OriginArguments[i], type.Symbol?.Schema?.Origins[i].LoanRequirement ?? LoanRequirement.Ref);
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                AddType(place, type.Components[i]);
            }
        }

        void AddOrigin(int place, BoundOrigin origin, LoanRequirement mode)
        {
            if (origin.Kind == OriginKind.Intersection)
            {
                for (var i = 0; i < origin.Operands.Count; i++)
                {
                    AddOrigin(place, origin.Operands[i], mode);
                }
            }
            else if (origin.Kind == OriginKind.Projection || (origin.Kind == OriginKind.Input && ReferenceEquals(origin.Binder, this.Function)))
            {
                foreach (var entry in this.SymbolPlaces)
                {
                    if (ReferenceEquals(entry.Key.Declaration, origin.Binder) && entry.Key.Slot == origin.Slot)
                    {
                        Record(entry.Value);
                    }
                }

                for (var root = 0; root < count; root++)
                {
                    var candidate = this.Places[root];
                    if (candidate.Kind is OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result && (StructStorage.IsStruct(candidate.Type) || candidate.Type.Kind == BoundTypeKind.FixedArray) &&
                        ReferenceEquals(candidate.Source, origin.Binder))
                    {
                        Record(root);
                    }
                }

                void Record(int root)
                {
                    var index = (place * count) + root;
                    this.borrowDependencies[index] = (LoanRequirement)Math.Max((int)this.borrowDependencies[index], (int)mode);
                    any = true;
                }
            }
        }

        bool Uses(int id, int place)
        {
            var operation = this.Operations[id];
            if (this.Values[id] is { Kind: OwnershipValueKind.Sequence, Constant: var sequence } && this.Sequences[(int)sequence].Receiver == place)
            {
                return true;
            }

            if (operation.Input == place && operation.Kind is OwnershipOperationKind.Write or OwnershipOperationKind.WriteElement or OwnershipOperationKind.WriteBorrowedField or OwnershipOperationKind.PayloadPlacement)
            {
                return true;
            }

            return operation.Place == place && operation.Kind switch
            {
                OwnershipOperationKind.Read or OwnershipOperationKind.Consume or OwnershipOperationKind.Borrow or OwnershipOperationKind.CallEntry or OwnershipOperationKind.Deliver or OwnershipOperationKind.LocateReceiver or OwnershipOperationKind.WriteBorrowedField => true,
                OwnershipOperationKind.Cleanup => Observes(this.Places[place].Type),
                _ => false,
            };
        }

        static bool Kills(OwnershipOperation operation, int place)
            => (operation.Place == place && operation.Kind is OwnershipOperationKind.Declare or OwnershipOperationKind.Produce or OwnershipOperationKind.InitializeReceiverField or OwnershipOperationKind.Write or OwnershipOperationKind.Cleanup or OwnershipOperationKind.CallEntry or OwnershipOperationKind.Deliver) ||
                (operation.Input == place && operation.Kind is OwnershipOperationKind.Consume or OwnershipOperationKind.Borrow) ||
                (operation.Place == place && operation.Kind == OwnershipOperationKind.Consume && operation.Acquisition == AcquisitionKind.Move);

        static bool Observes(BoundType type)
        {
            if (StructStorage.IsStruct(type))
            {
                if (StructStorage.Destructor(type) is not null)
                {
                    return true;
                }

                for (var i = 0; i < StructStorage.Count(type); i++)
                {
                    if (Observes(StructStorage.FieldType(type, i)!))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    private static bool HasProjection(BoundType type)
    {
        if (ReferenceTypes.IsString(type))
        {
            return false;
        }

        if (ContainsProjection(type.Origin))
        {
            return true;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (ContainsProjection(type.OriginArguments[i]))
            {
                return true;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (HasProjection(type.Components[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsProjection(BoundOrigin? origin)
    {
        if (origin?.Kind is OriginKind.Projection or OriginKind.Input)
        {
            return true;
        }

        if (origin is not null)
        {
            for (var i = 0; i < origin.Operands.Count; i++)
            {
                if (ContainsProjection(origin.Operands[i]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int ValuePlaceForBorrow(OwnershipOperation operation) => operation.Kind is OwnershipOperationKind.Consume or OwnershipOperationKind.Borrow ? operation.Input : operation.Place;

    private void PrepareSlicePaths()
    {
        Grow(ref this.slicePaths, this.Places.Count);
        this.slicePaths.AsSpan(0, this.Places.Count).Fill(-2);
        bool changed;
        do
        {
            changed = false;
            for (var id = 0; id < this.Operations.Count; id++)
            {
                var operation = this.Operations[id];
                if (this.Values[id] is { Kind: OwnershipValueKind.Sequence, Constant: var sequence } && this.Sequences[(int)sequence] is { Kind: SequenceOperation.Slice } plan)
                {
                    Merge(operation.Place, this.Places[plan.Receiver].Type.Kind == BoundTypeKind.Slice ? this.slicePaths[plan.Receiver] : plan.Projection);
                }
                else if (operation.Place >= 0 && operation.Input >= 0 && this.Places[operation.Place].Type.Kind == BoundTypeKind.Slice)
                {
                    if (operation.Kind == OwnershipOperationKind.Consume)
                    {
                        Merge(operation.Input, this.slicePaths[operation.Place]);
                    }
                    else if (operation.Kind == OwnershipOperationKind.Write)
                    {
                        Merge(operation.Place, this.slicePaths[operation.Input]);
                    }
                }
            }
        }
        while (changed);

        void Merge(int place, int path)
        {
            if (path == -2 || this.slicePaths[place] == path || this.slicePaths[place] == -1)
            {
                return;
            }

            var previous = this.slicePaths[place];
            this.slicePaths[place] = previous == -2 ? path : -1;
            changed = true;
        }
    }

    private bool IsBorrowAncestor(int value, int place)
    {
        // Follow the actual value's reborrow chain; equal Origin names alone do
        // not authorize use of a parent while a sibling/child Loan is required.
        while ((uint)value < (uint)this.Values.Count)
        {
            var operation = this.Operations[value];
            if (ValuePlaceForBorrow(operation) == place ||
                (operation.Kind is OwnershipOperationKind.Read or OwnershipOperationKind.Consume && operation.Place == place))
            {
                return true;
            }

            var node = this.Values[value];
            if (node.Kind is not (OwnershipValueKind.Alias or OwnershipValueKind.Address) || node.Count != 1)
            {
                return false;
            }

            value = this.ValueOperands[node.Start];
        }

        return false;
    }
}
