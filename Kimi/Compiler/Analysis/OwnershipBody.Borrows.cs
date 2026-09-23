// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipBody
{
    private readonly List<(int To, int Next)> checkingBorrowEdges = new();
    private bool[] borrowLive = [];
    private int[] checkingBorrowHeads = [];
    private LoanRequirement[] borrowDependencies = [];
    private LoanRequirement[] retainedBorrowAuthority = [];
    private bool[] borrowRootLoss = [];
    private int[] borrowDefinitions = [];
    private int[] slicePaths = [];

    internal PlaceState GetBorrowInputState(int operation)
    {
        if (!this.IsReachable(operation) && !this.HasCheckingState(operation))
        {
            return PlaceState.None;
        }

        this.LoadInput(operation, !this.IsReachable(operation));
        var borrow = this.Operations[operation];
        return this.TryOwnedBorrowState(borrow, out var state) ? state : this.CompleteState(borrow.Place);
    }

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
        Grow(ref this.borrowRootLoss, count);
        this.borrowRootLoss.AsSpan(0, count).Clear();
        var any = false;
        for (var p = 0; p < count; p++)
        {
            AddType(p, this.Places[p].Type);
        }

        if (!any)
        {
            return;
        }

        this.RetainBorrowAuthority(count);
        this.PrepareSlicePaths();
        this.PrepareCheckingBorrowEdges();
        Grow(ref this.borrowDefinitions, count);
        this.borrowDefinitions.AsSpan(0, count).Fill(-1);
        for (var id = 0; id < this.Operations.Count; id++)
        {
            var defined = this.Operations[id] switch
            {
                { Kind: OwnershipOperationKind.Write, Place: >= 0 } write => write.Place,
                { Kind: OwnershipOperationKind.Borrow, Input: >= 0 } borrow => borrow.Input,
                { Kind: OwnershipOperationKind.Produce, Place: >= 0 } produce when this.Values[id] is { Kind: OwnershipValueKind.Alias, Count: 1 } => produce.Place,
                _ => -1,
            };

            if (defined >= 0)
            {
                ref var definition = ref this.borrowDefinitions[defined];
                definition = definition == -1 ? id : -2;
            }
        }

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

                        for (var e = this.checkingBorrowHeads[op]; e >= 0 && !live; e = this.checkingBorrowEdges[e].Next)
                        {
                            live |= this.borrowLive[(this.checkingBorrowEdges[e].To * count) + p];
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

            var activating = this.Operations[op].Kind == OwnershipOperationKind.ActivateCallBorrows;
            var loaded = false;
            for (var r = activating ? this.Operations[op].Reservation : -2; r != -1; r = r >= 0 ? this.CallReservations[r].Next : -1)
            {
                var accessId = activating ? this.CallReservations[r].Borrow : op;
                var operation = this.Operations[accessId];
                var accessMode = !activating && operation.Reservation >= 0 ? LoanRequirement.Ref : operation.LoanMode;
                for (var p = 0; p < count; p++)
                {
                    if (!this.borrowLive[(op * count) + p] || (activating && p == this.CallReservations[r].Place))
                    {
                        continue;
                    }

                    if (!loaded)
                    {
                        // All validity/footprint queries below are read-only. Replay
                        // this block prefix once, only if some Loan needs its state.
                        this.LoadInput(op, !this.IsReachable(op));
                        loaded = true;
                    }

                    if ((this.CompleteState(p) & PlaceState.MayInit) == 0)
                    {
                        continue;
                    }

                    for (var root = 0; root < count; root++)
                    {
                        var mode = this.BorrowModeAt(p, root, op, this.borrowDependencies[(p * count) + root]);
                        if (mode == LoanRequirement.None)
                        {
                            continue;
                        }

                        var external = this.Places[root].Kind == OwnershipPlaceKind.Parameter && (ReferenceTypes.IsBorrow(this.Places[root].Type) || ReferenceTypes.IsString(this.Places[root].Type));
                        var authority = this.BorrowModeAt(p, root, op, this.retainedBorrowAuthority[(p * count) + root]);
                        var accessConflict = ConflictsWithComparison(operation.Kind, operation.Place, operation.Input, operation.Acquisition, root, authority, accessMode) ||
                            this.ElementAccessConflicts(operation, root, authority);
                        if (accessConflict && operation.Kind is OwnershipOperationKind.Borrow or OwnershipOperationKind.ProjectElement or OwnershipOperationKind.WriteElement or OwnershipOperationKind.Produce &&
                            this.IsDisjointProjection(accessId, p))
                        {
                            accessConflict = false; // SPEC 15.6.2: disjoint static paths below one owned root.
                        }

                        if (this.Places[p].Type.Kind == BoundTypeKind.Slice && operation.Projection >= 0 && this.Projections[operation.Projection].Root == root)
                        {
                            var modifies = operation.Kind == OwnershipOperationKind.WriteElement ||
                                (operation.Kind == OwnershipOperationKind.Produce && operation.Acquisition is AcquisitionKind.Move or AcquisitionKind.CopyOrMove);
                            if (modifies)
                            {
                                accessConflict = this.slicePaths[p] < 0 || this.ElementPathsOverlap(operation.Projection, this.slicePaths[p]);
                            }
                        }

                        var rootLost = !external && (this.BorrowRootState(p, root) & PlaceState.MustInit) == 0;
                        var conflict = rootLost || (!external && accessConflict);
                        var value = this.Values[accessId];
                        if (value.Kind is OwnershipValueKind.BorrowedField or OwnershipValueKind.BorrowedFieldWrite or OwnershipValueKind.BorrowedUpdate or OwnershipValueKind.Address or OwnershipValueKind.Sequence && value.Count > 0)
                        {
                            var receiver = this.ValueOperands[value.Start];
                            var sourcePlace = ValuePlaceForBorrow(this.Operations[receiver]);
                            var access = value.Kind is OwnershipValueKind.BorrowedFieldWrite or OwnershipValueKind.BorrowedUpdate ? LoanRequirement.Uniq
                                : value.Kind == OwnershipValueKind.Address ? accessMode : LoanRequirement.Ref;
                            if (sourcePlace >= 0 && this.borrowDependencies[(sourcePlace * count) + root] != LoanRequirement.None &&
                                (mode == LoanRequirement.Uniq || access == LoanRequirement.Uniq) && !this.IsBorrowAncestor(receiver, p) && !this.IsDisjointProjection(accessId, p))
                            {
                                conflict = true;
                            }
                        }

                        if (conflict)
                        {
                            // One diagnostic per invalidated root: the operation that invalidates it
                            // (a Move or an access conflict) is reported, not every later use of a
                            // Loan that depends on the dangling root (PLAN G13).
                            ref var reported = ref this.borrowRootLoss[root];
                            if (reported && rootLost)
                            {
                                continue;
                            }

                            reported = true;
                            var reservation = activating ? r : operation.Reservation >= 0 ? operation.Reservation : this.reservationPlaces[p];
                            this.ReportIssue(new(activating ? this.Operations[op].Source : operation.Source, OwnershipFailure.ComparisonLoanConflict, Reservation: reservation, Activation: activating));
                        }
                    }
                }
            }
        }

        void AddType(int place, BoundType type)
        {
            if (ReferenceTypes.IsString(type))
            {
                return; // Argument references use the existing call/guard Loan plans; dependent results retain their own Origins.
            }

            if (type.Origin is { } origin)
            {
                AddOrigin(place, origin, type.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq ? LoanRequirement.Uniq : LoanRequirement.Ref);
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
                    if (ReferenceEquals(entry.Key.Declaration, origin.Binder) && entry.Key.Slot == (origin.Kind == OriginKind.Input ? origin.InputIndex : origin.Slot) &&
                        (origin.Kind != OriginKind.Input || entry.Key.Kind == BindingSymbolKind.Parameter))
                    {
                        Record(entry.Value);
                    }
                }

                for (var root = 0; root < count; root++)
                {
                    var candidate = this.Places[root];
                    if (origin.Kind == OriginKind.Projection && candidate.Kind is OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result && (ReferenceEquals(candidate.Type, BoundType.String) || StructStorage.IsStruct(candidate.Type) || candidate.Type.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Tuple or BoundTypeKind.Array || ScalarTypes.Supports(candidate.Type)) &&
                        ReferenceEquals(candidate.Source, origin.Binder) && (!ScalarTypes.Supports(candidate.Type) || this.IsBorrowedPlace(root)))
                    {
                        Record(root);
                    }
                }

                void Record(int root)
                {
                    var index = (place * count) + root;
                    if (this.borrowDependencies[index] >= mode)
                    {
                        return;
                    }

                    this.borrowDependencies[index] = (LoanRequirement)Math.Max((int)this.borrowDependencies[index], (int)mode);
                    any = true;
                    var source = this.Places[root].Type;
                    if (ReferenceTypes.IsBorrow(source) || ReferenceTypes.IsString(source))
                    {
                        // A reborrow's access mode comes from its own Type. Only
                        // nested dependencies survive here; retain the parent's
                        // stronger authority separately through value transfer.
                        for (var i = 0; i < source.Components.Count; i++)
                        {
                            AddType(place, source.Components[i]);
                        }
                    }
                    else
                    {
                        AddType(place, source);
                    }
                }
            }
        }

        bool Uses(int id, int place)
        {
            var operation = this.Operations[id];
            if (operation.Kind == OwnershipOperationKind.ActivateCallBorrows)
            {
                for (var r = operation.Reservation; r >= 0; r = this.CallReservations[r].Next)
                {
                    if (this.Operations[this.CallReservations[r].Borrow].Place == place)
                    {
                        return true; // Keep existing parent authority active until the child is activated.
                    }
                }
            }

            if (operation.Kind == OwnershipOperationKind.UpdateBorrowed)
            {
                return this.Values[id].Constant == place || operation.Input == place;
            }

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
                OwnershipOperationKind.Read or OwnershipOperationKind.Consume or OwnershipOperationKind.Borrow or OwnershipOperationKind.CallEntry or OwnershipOperationKind.Deliver or OwnershipOperationKind.LocateReceiver or OwnershipOperationKind.WriteBorrowedField or OwnershipOperationKind.StorePointer => true,
                OwnershipOperationKind.Cleanup => Observes(this.Places[place].Type),
                _ => false,
            };
        }

        static bool Kills(OwnershipOperation operation, int place)
            => (operation.Place == place && operation.Kind is OwnershipOperationKind.Declare or OwnershipOperationKind.Produce or OwnershipOperationKind.InitializeReceiverField or OwnershipOperationKind.Write or OwnershipOperationKind.Cleanup or OwnershipOperationKind.CallEntry or OwnershipOperationKind.Deliver or OwnershipOperationKind.StorePointer) ||
                (operation.Input == place && operation.Kind is OwnershipOperationKind.Consume or OwnershipOperationKind.Borrow) ||
                (operation.Place == place && operation.Kind == OwnershipOperationKind.Consume && operation.Acquisition == AcquisitionKind.Move);

        static bool Observes(BoundType type, int depth = 0)
        {
            if (depth > 64)
            {
                return true; // Conservatively retain a dependency through recursive destruction.
            }

            if (StructStorage.IsStruct(type))
            {
                if (StructStorage.Destructor(type) is not null)
                {
                    return true;
                }

                for (var i = 0; i < StructStorage.Count(type); i++)
                {
                    if (StructStorage.FieldType(type, i) is not { } field || Observes(field, depth + 1))
                    {
                        return true;
                    }
                }
            }

            if (ObjectTypes.IsOwner(type) || type.Kind is BoundTypeKind.Closure or BoundTypeKind.Tuple or BoundTypeKind.FixedArray)
            {
                for (var i = 0; i < type.Components.Count; i++)
                {
                    if (Observes(type.Components[i], depth + 1))
                    {
                        return true;
                    }
                }
            }

            if (type.StoredCases is { } cases)
            {
                foreach (var payload in cases)
                {
                    if (Observes(payload, depth + 1))
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

    private static int Selector(BinaryKoto field) => ElementAccess.PathSelector(field, out _, out _);

    // A compound/increment update reborrows its base once; its footprint is the
    // updated inline field path (SPEC 15.6.2), not the whole base.
    private static MemberAccessKoto? UpdateTarget(Koto root)
    {
        var node = root.Parent;
        while (node is MemberAccessKoto { Parent: MemberAccessKoto outer } && ReferenceEquals(ElementAccess.BorrowedPathRoot(outer), root))
        {
            node = outer;
        }

        return node is MemberAccessKoto field && ReferenceEquals(ElementAccess.BorrowedPathRoot(field), root) &&
            ElementAccess.UpdateOperator(field.Parent?.Akind ?? KotoKind.Invalid) != KotoKind.Invalid &&
            (field.Parent is BinaryKoto binary ? ReferenceEquals(KotoHelper.UnwrapParentheses(binary.Left), field) : field.Parent is UnaryKoto) ? field : null;
    }

    private static bool AddPath(BinaryKoto field, Koto root, Span<int> selectors, ref int depth)
    {
        for (var level = field; ;)
        {
            if (depth == selectors.Length || Selector(level) is not (>= 0 and var selector))
            {
                return false;
            }

            selectors[depth++] = selector;
            var receiver = KotoHelper.UnwrapParentheses(level.Left);
            if (ReferenceEquals(receiver, KotoHelper.UnwrapParentheses(root)))
            {
                return true;
            }

            if (receiver is not BinaryKoto parent)
            {
                return false;
            }

            level = parent;
        }
    }

    // Origin equality preserves identity, not permission: a shared result can
    // still retain the exclusive authority acquired by an input. Transfer only
    // authority for roots already present in the result's declared dependencies.
    private void RetainBorrowAuthority(int count)
    {
        Grow(ref this.retainedBorrowAuthority, checked(count * count));
        this.borrowDependencies.AsSpan(0, count * count).CopyTo(this.retainedBorrowAuthority);
        bool changed;
        do
        {
            changed = false;
            for (var id = 0; id < this.Operations.Count; id++)
            {
                var operation = this.Operations[id];
                switch (operation.Kind)
                {
                    case OwnershipOperationKind.Consume or OwnershipOperationKind.Borrow or OwnershipOperationKind.AcquirePattern:
                        Merge(operation.Input, operation.Place);
                        break;
                    case OwnershipOperationKind.Write or OwnershipOperationKind.InitializeSubject or OwnershipOperationKind.PayloadPlacement:
                        Merge(operation.Place, operation.Input);
                        break;
                    case OwnershipOperationKind.Call:
                        for (var entry = id - 1; entry >= 0 && this.Operations[entry] is { Kind: OwnershipOperationKind.CallEntry } input && ReferenceEquals(input.Source, operation.Source); entry--)
                        {
                            Merge(operation.Place, input.Place);
                        }

                        break;
                }

                if (this.Values[id] is { Kind: OwnershipValueKind.Alias, Count: 1 } alias && this.ValueOperands[alias.Start] is >= 0 and var value)
                {
                    Merge(ValuePlaceForBorrow(operation), ValuePlaceForBorrow(this.Operations[value]));
                }
            }

            for (var i = 0; i < this.Constructions.Count; i++)
            {
                var plan = this.Constructions[i];
                for (var p = 0; p < plan.PayloadCount; p++)
                {
                    Merge(plan.Place, plan.PayloadStart + p);
                }
            }

            for (var i = 0; i < this.Decompositions.Count; i++)
            {
                var plan = this.Decompositions[i];
                for (var p = 0; p < plan.PayloadCount; p++)
                {
                    Merge(plan.PayloadStart + p, plan.Place);
                }
            }
        }
        while (changed);

        void Merge(int destination, int source)
        {
            if (destination < 0 || source < 0 || destination == source)
            {
                return;
            }

            for (var root = 0; root < count; root++)
            {
                ref var target = ref this.retainedBorrowAuthority[(destination * count) + root];
                var input = this.retainedBorrowAuthority[(source * count) + root];
                if (target != LoanRequirement.None && input > target)
                {
                    target = input;
                    changed = true;
                }
            }
        }
    }

    private void PrepareCheckingBorrowEdges()
    {
        Grow(ref this.checkingBorrowHeads, this.Operations.Count);
        this.checkingBorrowHeads.AsSpan(0, this.Operations.Count).Fill(-1);
        this.checkingBorrowEdges.Clear();
        for (var i = 1; i < this.CheckingRegions.Count; i++)
        {
            var region = this.CheckingRegions[i];
            if (region.Entry < 0 || !this.HasCheckingState(region.Entry))
            {
                continue;
            }

            for (var s = 0; s < Math.Max(1, region.SeedCount); s++)
            {
                var seed = region.SeedCount == 0 ? new OwnershipCheckingSeed(region.Seed, region.Target, region.Replay) : this.CheckingSeeds[region.SeedStart + s];
                var next = region.Entry;
                for (var replay = seed.Replay; replay >= 0; replay = this.CheckingReplays[replay].Previous)
                {
                    var path = this.CheckingReplays[replay];
                    Add(path.End, next);
                    next = path.Entry;
                }

                Add(seed.Operation, next);
            }
        }

        // Liveness follows the same pre-cleanup seeds and replay order as state
        // checking. These links never become runtime or cleanup-plan edges.
        void Add(int from, int to)
        {
            this.checkingBorrowEdges.Add((to, this.checkingBorrowHeads[from]));
            this.checkingBorrowHeads[from] = this.checkingBorrowEdges.Count - 1;
        }
    }

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

    // ProjectElement only locates storage; a prefix of a deeper projection does
    // not read its whole subtree. Final reads and Moves retain their own footprint.
    private bool ElementAccessConflicts(OwnershipOperation operation, int root, LoanRequirement mode)
    {
        if (operation.Projection < 0 || this.Projections[operation.Projection].Root != root)
        {
            return false;
        }

        var projection = this.Projections[operation.Projection];
        return operation.Kind switch
        {
            OwnershipOperationKind.ProjectElement => mode == LoanRequirement.Uniq && (projection.Output >= 0 || projection.Borrow >= 0 || projection.Write >= 0),
            OwnershipOperationKind.Produce => mode == LoanRequirement.Uniq || operation.Acquisition is AcquisitionKind.Move or AcquisitionKind.CopyOrMove,
            _ => false,
        };
    }

    // Read the borrowed subtree and its containing storage from the same converged
    // runtime/checking snapshot. A disjoint sibling Move does not end this Loan.
    private PlaceState BorrowRootState(int place, int root)
    {
        if (this.moveRoots[root] >= 0 && this.HasSingleBorrowDefinition(place) && ReferenceTypes.IsStorage(this.Places[place].Type))
        {
            Span<int> selectors = stackalloc int[16];
            var depth = 0;
            if (this.ProjectionPath(this.borrowDefinitions[place], selectors, ref depth) == root)
            {
                return this.InlinePathState(root, selectors[..depth]);
            }
        }

        return this.CompleteState(root);
    }

    private bool TryOwnedBorrowState(OwnershipOperation operation, out PlaceState state)
    {
        state = PlaceState.None;
        if (operation.Source is not BinaryKoto part || ElementAccess.OwnedPathRoot(part) is not { } owner)
        {
            return false;
        }

        Span<int> selectors = stackalloc int[16];
        var depth = 0;
        if (!AddPath(part, owner, selectors, ref depth))
        {
            return false;
        }

        state = this.InlinePathState(operation.Place, selectors[..depth]);
        return true;
    }

    // A scalar temporary is a Loan root only where a borrow materializes it (SPEC 3.6.2).
    private bool IsBorrowedPlace(int place)
    {
        for (var i = 0; i < this.Operations.Count; i++)
        {
            if (this.Operations[i] is { Kind: OwnershipOperationKind.Borrow } borrow && borrow.Place == place)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsBorrowAncestor(int value, int place)
    {
        // Follow the actual value's reborrow chain; equal Origin names alone do
        // not authorize use of a parent while a sibling/child Loan is required.
        for (var remaining = this.Values.Count; remaining > 0 && (uint)value < (uint)this.Values.Count; remaining--)
        {
            var operation = this.Operations[value];
            if (ValuePlaceForBorrow(operation) == place ||
                (operation.Kind is OwnershipOperationKind.Read or OwnershipOperationKind.Consume && operation.Place == place))
            {
                return true;
            }

            var node = this.Values[value];
            if (node.Kind == OwnershipValueKind.Call && operation.Kind == OwnershipOperationKind.Call)
            {
                // A returned reference descends from the one acquired argument
                // its public result Origin names; any other contract stops here.
                value = this.ResultArgument(value);
                continue;
            }

            if (node.Kind is not (OwnershipValueKind.Alias or OwnershipValueKind.Address) || node.Count != 1)
            {
                // An immutable reference local retains the ancestry of its one
                // initialization. Follow the actual stored value, never merely
                // a matching Origin (which could name a sibling Loan).
                if (operation.Kind == OwnershipOperationKind.Read && operation.Place >= 0 &&
                    this.Places[operation.Place] is { Kind: OwnershipPlaceKind.Local, Mutable: false } local && ReferenceTypes.IsBorrow(local.Type))
                {
                    var definition = this.borrowDefinitions[operation.Place];
                    if (definition >= 0 && definition < value)
                    {
                        value = definition;
                        continue;
                    }
                }

                return false;
            }

            value = this.ValueOperands[node.Start];
        }

        return false;
    }

    private bool HasSingleBorrowDefinition(int place)
        => this.borrowDefinitions[place] >= 0 &&
        (this.Places[place].Kind == OwnershipPlaceKind.Temporary || this.Places[place] is { Kind: OwnershipPlaceKind.Local, Mutable: false });

    // SPEC 15.6.2: distinct inline field/Tuple selectors under the same root
    // designate disjoint places. Unknown steps, nonliteral subscripts and different
    // roots conservatively overlap.
    private bool IsDisjointProjection(int access, int place)
    {
        if (!this.HasSingleBorrowDefinition(place))
        {
            return false;
        }

        const int Limit = 16;
        Span<int> left = stackalloc int[Limit];
        Span<int> right = stackalloc int[Limit];
        var leftDepth = 0;
        var value = access;
        var node = this.Values[access];
        var projection = this.Operations[access].Projection;
        var leftRoot = -1;
        if (projection >= 0 && this.Operations[access].Kind is OwnershipOperationKind.ProjectElement or OwnershipOperationKind.WriteElement or OwnershipOperationKind.Produce)
        {
            // Only the static prefix of an element path is a precise footprint.
            for (var path = this.Projections[projection].Path; path >= 0; path = this.Projections[path].Parent)
            {
                if (leftDepth == Limit)
                {
                    return false;
                }

                left[leftDepth++] = this.Projections[path].Selector;
            }

            leftRoot = this.Projections[projection].Root;
        }
        else if (node.Kind == OwnershipValueKind.BorrowedUpdate)
        {
            // This access is through the first prepared receiver. Other targets
            // are independently checked when their reservations activate.
            value = this.ValueOperands[node.Start];
        }
        else if (node.Kind is OwnershipValueKind.BorrowedField or OwnershipValueKind.BorrowedFieldWrite)
        {
            var source = KotoHelper.UnwrapParentheses(this.Operations[access].Source);
            source = source switch
            {
                MemberAccessKoto => source,
                BinaryKoto binary => KotoHelper.UnwrapParentheses(binary.Left),
                UnaryKoto unary => KotoHelper.UnwrapParentheses(unary.Operand),
                _ => source,
            };

            if (source is not MemberAccessKoto field || ElementAccess.BorrowedPathRoot(field) is not { } root)
            {
                return false;
            }

            // Record every inline level from the accessed field up to the borrowed base.
            for (var level = field; ; level = (MemberAccessKoto)level.Left)
            {
                if (leftDepth == Limit || Selector(level) is not (>= 0 and var selector))
                {
                    return false;
                }

                left[leftDepth++] = selector;
                if (ReferenceEquals(level.Left, root))
                {
                    break;
                }
            }

            value = this.ValueOperands[node.Start];
        }

        if (leftRoot < 0)
        {
            leftRoot = this.ProjectionPath(value, left, ref leftDepth);
        }

        var rightDepth = 0;
        var rightRoot = this.ProjectionPath(this.borrowDefinitions[place], right, ref rightDepth);
        if (leftRoot < 0 || leftRoot != rightRoot)
        {
            return false;
        }

        // Selectors were collected leaf first; compare from the shared root.
        for (int l = leftDepth - 1, r = rightDepth - 1; l >= 0 && r >= 0; l--, r--)
        {
            if (left[l] != right[r])
            {
                return true;
            }
        }

        return false;
    }

    private int ProjectionPath(int value, Span<int> selectors, ref int depth)
    {
        for (var remaining = this.Values.Count; remaining > 0 && (uint)value < (uint)this.Values.Count; remaining--)
        {
            var operation = this.Operations[value];
            var node = this.Values[value];
            if (operation.Kind == OwnershipOperationKind.Borrow && node.Kind == OwnershipValueKind.Address)
            {
                if (node.Count == 0)
                {
                    // An owned inline part's footprint is its static path below the root.
                    return KotoHelper.UnwrapParentheses(operation.Source) is BinaryKoto part && ElementAccess.OwnedPathRoot(part) is { } owner &&
                        !AddPath(part, owner, selectors, ref depth) ? -1 : operation.Place;
                }

                if (node.Count != 1)
                {
                    return -1;
                }

                if (KotoHelper.UnwrapParentheses(operation.Source) is MemberAccessKoto field)
                {
                    // Only inline parts; a stored reference's referent is not part of this root.
                    if (ReferenceTypes.IsStorage(field.BoundType) || ElementAccess.BorrowedPathRoot(field) is not { } root)
                    {
                        return -1;
                    }

                    if (!AddPath(field, root, selectors, ref depth))
                    {
                        return -1;
                    }
                }
                else if (depth == 0 && UpdateTarget(operation.Source) is { } target && !AddPath(target, operation.Source, selectors, ref depth))
                {
                    return -1;
                }

                value = this.ValueOperands[node.Start];
            }
            else if (operation.Kind == OwnershipOperationKind.Call && node.Kind == OwnershipValueKind.Call)
            {
                // The result contract preserves the acquired input's footprint,
                // not a field offset within it. Discard result-side selectors:
                // a callee may return any permitted subpart of that input.
                depth = 0;
                value = this.ResultArgument(value);
            }
            else if (node.Kind == OwnershipValueKind.Alias && node.Count == 1)
            {
                value = this.ValueOperands[node.Start];
            }
            else if (operation.Kind == OwnershipOperationKind.Read && operation.Place >= 0)
            {
                var definition = this.Places[operation.Place] is { Kind: OwnershipPlaceKind.Local, Mutable: false } local && ReferenceTypes.IsBorrow(local.Type)
                    ? this.borrowDefinitions[operation.Place] : -1;
                if (definition < 0 || definition >= value)
                {
                    return operation.Place;
                }

                value = definition;
            }
            else if (operation.Kind == OwnershipOperationKind.Produce && node.Kind == OwnershipValueKind.Parameter)
            {
                return operation.Place;
            }
            else
            {
                return -1;
            }
        }

        return -1;
    }

    private int ResultArgument(int call)
    {
        if (this.Operations[call].Source is not InvocationKoto { BoundValueCall: null, BoundCall: { Target: { CompilerFunction: CompilerFunctionKind.None, Declaration: FunctionKoto target } } plan } ||
            target.ReturnType?.BoundType is not { Kind: BoundTypeKind.Semantics, Origin: { Kind: OriginKind.Input } origin } || !ReferenceEquals(origin.Binder, target))
        {
            return -1;
        }

        // CallEntry operations immediately precede Call: receiver, explicit
        // arguments in source order, then defaults. A default has no caller Loan.
        var entries = 0;
        while (entries < call && this.Operations[call - entries - 1] is { Kind: OwnershipOperationKind.CallEntry } entry && ReferenceEquals(entry.Source, this.Operations[call].Source))
        {
            entries++;
        }

        if (entries != target.Parameters.Count)
        {
            return -1;
        }

        var index = -1;
        var offset = 0;
        if (plan.Receiver is not null)
        {
            offset = 1;
            index = plan.ReceiverOperation.ParameterIndex == origin.Slot ? 0 : -1;
        }

        var mapping = plan.ArgumentToParameter;
        for (var i = 0; i < mapping.Length && index < 0; i++)
        {
            index = mapping[i] == origin.Slot ? offset + i : -1;
        }

        return index < 0 ? -1 : call - entries + index;
    }
}
