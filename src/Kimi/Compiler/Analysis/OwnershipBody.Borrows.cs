// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipBody
{
    private readonly List<(int To, int Next)> checkingBorrowEdges = new();
    private PackedAnalysisTable borrowLive = new(1);
    private int[] checkingBorrowHeads = [];
    private PackedAnalysisTable borrowDependencies = new(2);
    private PackedAnalysisTable retainedBorrowAuthority = new(2);
    private bool[] borrowRootLoss = [];
    private int[] borrowDefinitions = [];
    private int[] slicePaths = [];
    private bool[] inspectionBorrows = [];
    private bool[] borrowedPlaces = [];
    private bool[] dependencyRoots = [];
    private List<int>? borrowRoots;
    private List<int>? liveBorrowPlaces;
    private Dictionary<(int Place, int Root), int>? storedBorrowStarts;

    /// <summary>Gets retained cells in the local borrow dependency table.</summary>
    internal int BorrowDependencyCapacity => this.borrowDependencies.Capacity;

    internal long BorrowStorageBytes => (long)this.borrowDependencies.ByteCapacity + this.retainedBorrowAuthority.ByteCapacity + this.borrowLive.ByteCapacity;

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

    // Borrow validity follows CFG uses, including the implicit use by drop.
    // Types retain Origin identity through Copy, Move, calls and field storage.
    internal void VerifyBorrows()
    {
        this.storedBorrowStarts?.Clear();
        this.borrowRoots?.Clear();
        var count = this.Places.Count;
        var dependent = false;
        for (var p = 0; p < count && !dependent; p++)
        {
            dependent = HasProjection(this.Places[p].Type) || this.IsExclusiveBorrowInput(p);
        }

        if (!dependent)
        {
            return;
        }

        // Short-lived call/guard inspections already have an explicit Loan extent,
        // including abrupt checking continuations. A stored Copy or returned reference
        // is a separate Place and retains its ordinary Origin-based dependency.
        Grow(ref this.inspectionBorrows, count);
        this.inspectionBorrows.AsSpan(0, count).Clear();
        Grow(ref this.borrowedPlaces, count);
        this.borrowedPlaces.AsSpan(0, count).Clear();
        for (var id = 0; id < this.Operations.Count; id++)
        {
            if (this.Values[id].Kind == OwnershipValueKind.Borrow && this.Operations[id].Input >= 0)
            {
                this.inspectionBorrows[this.Operations[id].Input] = true;
            }

            if (this.Operations[id] is { Kind: OwnershipOperationKind.Borrow, Place: >= 0 } borrow && borrow.Place < count)
            {
                this.borrowedPlaces[borrow.Place] = true;
            }
        }

        var any = false;
        for (var p = 0; p < count; p++)
        {
            if (this.IsExclusiveBorrowInput(p))
            {
                // An abstract Origin names a lifetime, not an access path. Keep each input's actual capability
                // separate even when two parameters use the same declared Origin.
                RecordDependency(p, p, LoanRequirement.Uniq);
            }

            if (!this.inspectionBorrows[p])
            {
                AddType(p, this.Places[p].Type);
            }
        }

        if (!any)
        {
            return;
        }

        this.RetainBorrowAuthority(count);
        (this.liveBorrowPlaces ??= new()).Clear();
        for (var p = 0; p < count; p++)
        {
            for (var rootIndex = 0; rootIndex < this.borrowRoots!.Count; rootIndex++)
            {
                if (this.borrowDependencies[(p * count) + this.borrowRoots[rootIndex]] != LoanRequirement.None)
                {
                    this.liveBorrowPlaces.Add(p);
                    break;
                }
            }
        }

        var liveWidth = this.liveBorrowPlaces.Count;
        for (var id = 0; id < this.Operations.Count; id++)
        {
            if (this.Operations[id] is { Kind: OwnershipOperationKind.StoreDictionaryEntry } entry)
            {
                for (var rootIndex = 0; rootIndex < this.borrowRoots!.Count; rootIndex++)
                {
                    var root = this.borrowRoots[rootIndex];
                    if (this.borrowDependencies[(entry.Input * count) + root] != LoanRequirement.None ||
                        this.borrowDependencies[(this.OperationSteps[id] * count) + root] != LoanRequirement.None)
                    {
                        (this.storedBorrowStarts ??= new()).TryAdd((entry.Place, root), id);
                    }
                }
            }
        }

        this.PrepareSlicePaths();
        this.PrepareCheckingBorrowEdges();
        Grow(ref this.borrowDefinitions, count);
        this.borrowDefinitions.AsSpan(0, count).Fill(-1);
        for (var id = 0; id < this.Operations.Count; id++)
        {
            var defined = this.Operations[id] switch
            {
                { Kind: OwnershipOperationKind.Write, Place: >= 0 } write => write.Place,
                { Kind: OwnershipOperationKind.Consume, Input: >= 0, Acquisition: AcquisitionKind.Move } moved when ReferenceTypes.IsBorrow(this.Places[moved.Input].Type) => moved.Input,
                { Kind: OwnershipOperationKind.Borrow, Input: >= 0 } borrow => borrow.Input,
                { Kind: OwnershipOperationKind.Produce, Place: >= 0 } produce when this.Values[id] is { Kind: OwnershipValueKind.Alias, Count: 1 } => produce.Place,
                { Kind: OwnershipOperationKind.InitializeSubject, Place: >= 0 } subject => subject.Place,
                { Kind: OwnershipOperationKind.AcquirePattern, Input: >= 0 } binding => binding.Input,
                { Kind: OwnershipOperationKind.Produce, Place: >= 0 } item when this.Values[id].Kind == OwnershipValueKind.Sequence => item.Place,
                _ => -1,
            };

            if (defined >= 0)
            {
                ref var definition = ref this.borrowDefinitions[defined];
                definition = definition == -1 ? id : -2;
            }
        }

        this.borrowLive.Reset(OwnershipStorage.Cells(liveWidth, this.Operations.Count, 1, "borrow liveness"));
        bool changed;
        do
        {
            changed = false;
            for (var op = this.Operations.Count - 1; op >= 0; op--)
            {
                for (var slot = 0; slot < liveWidth; slot++)
                {
                    var p = this.liveBorrowPlaces[slot];
                    var live = Uses(op, p);
                    if (!Kills(this.Operations[op], p))
                    {
                        for (var e = this.EdgeHeads[op]; e >= 0 && !live; e = this.Edges[e].Next)
                        {
                            var edge = this.Edges[e];
                            live |= edge.Kind != OwnershipEdgeKind.Abort && this.borrowLive.IsSet((edge.To * liveWidth) + slot);
                        }

                        for (var e = this.checkingBorrowHeads[op]; e >= 0 && !live; e = this.checkingBorrowEdges[e].Next)
                        {
                            live |= this.borrowLive.IsSet((this.checkingBorrowEdges[e].To * liveWidth) + slot);
                        }
                    }

                    var at = (op * liveWidth) + slot;
                    changed |= live != this.borrowLive.IsSet(at);
                    this.borrowLive.Set(at, live);
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
                for (var slot = 0; slot < liveWidth; slot++)
                {
                    var p = this.liveBorrowPlaces[slot];
                    if (!this.borrowLive.IsSet((op * liveWidth) + slot) || (activating && p == this.CallReservations[r].Place))
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

                    for (var rootIndex = 0; rootIndex < this.borrowRoots!.Count; rootIndex++)
                    {
                        var root = this.borrowRoots[rootIndex];
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
                        if (value.Kind is OwnershipValueKind.BorrowedField or OwnershipValueKind.BorrowedFieldWrite or OwnershipValueKind.BorrowedUpdate or OwnershipValueKind.Address or OwnershipValueKind.Sequence or OwnershipValueKind.PointerStore or OwnershipValueKind.PointerLoad && value.Count > 0)
                        {
                            var receiver = this.ValueOperands[value.Start];
                            var sourcePlace = ValuePlaceForBorrow(this.Operations[receiver]);
                            var exclusiveElement = value.Kind == OwnershipValueKind.Sequence &&
                                this.Sequences[(int)value.Constant].Kind == SequenceOperation.Borrow &&
                                operation.Place >= 0 && this.Places[operation.Place].Type.Semantics == SemanticsKind.Uniq;
                            var access = value.Kind is OwnershipValueKind.BorrowedFieldWrite or OwnershipValueKind.BorrowedUpdate or OwnershipValueKind.PointerStore || exclusiveElement ? LoanRequirement.Uniq
                                : value.Kind == OwnershipValueKind.Address ? accessMode : LoanRequirement.Ref;
                            // Lending the slot that stores a reference does not access its external referent. The
                            // subsequent reborrow/store checks that separate capability, including call activation.
                            var referenceSlot = value.Kind is OwnershipValueKind.Sequence or OwnershipValueKind.Address && operation.Place >= 0 &&
                                ReferenceTypes.IsBorrow(this.Places[operation.Place].Type) &&
                                ReferenceTypes.IsBorrow(this.Places[operation.Place].Type.Components[0]) && this.IsExclusiveBorrowInput(root) &&
                                !ReferenceEquals(this.Places[root].Type.Components[0], this.Places[operation.Place].Type.Components[0]);
                            if (!referenceSlot && sourcePlace >= 0 && this.BorrowModeAt(sourcePlace, root, op, this.borrowDependencies[(sourcePlace * count) + root]) != LoanRequirement.None &&
                                (value.Kind is not (OwnershipValueKind.PointerStore or OwnershipValueKind.PointerLoad) || sourcePlace == root ||
                                    (ReferenceTypes.IsBorrow(this.Places[sourcePlace].Type) &&
                                        ReferenceEquals(
                                            this.IsExclusiveBorrowInput(root) ? this.Places[root].Type.Components[0] : this.Places[root].Type,
                                            this.Places[sourcePlace].Type.Components[0])) ||
                                    this.IsBorrowAncestor(this.borrowDefinitions[p] >= 0 ? this.borrowDefinitions[p] : this.ProducingValue(p, op), sourcePlace)) &&
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
                            this.ReportIssue(new(activating ? this.Operations[op].Source : operation.Source, OwnershipFailure.ComparisonLoanConflict, Place: p, Reservation: reservation, Activation: activating, LoanSource: this.Places[p].Source));
                        }
                    }
                }
            }
        }

        void AddType(int place, BoundType type, LoanRequirement bound = LoanRequirement.Uniq)
        {
            // SPEC 13.5.5.1: a pair parameter's Origin is the caller-side dependency of a reference it may hold, never a local root.
            if (type.Origin is { } origin && !(this.Places[place].Kind == OwnershipPlaceKind.Parameter && Binding.TryPairLayer(type, out _, out _)))
            {
                AddOrigin(place, origin, type.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq ? bound : LoanRequirement.Ref);
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                AddOrigin(place, type.OriginArguments[i], (LoanRequirement)Math.Min((int)bound, (int)(type.Symbol?.Schema?.Origins[i].LoanRequirement ?? LoanRequirement.Ref)));
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                AddType(place, type.Components[i], bound);
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
            else if (origin.Kind == OriginKind.Projection || (origin.Kind == OriginKind.Input && (ReferenceEquals(origin.Binder, this.Function) || ReferenceEquals(origin.Binder, this.Function.Accessor?.Declaration))))
            {
                if (origin.Kind == OriginKind.Projection)
                {
                    var guardCandidate = false;
                    for (var m = 0; m < this.Matches.Count; m++)
                    {
                        var match = this.Matches[m];
                        for (var n = 0; n < match.Binding.Positions.Count; n++)
                        {
                            var position = match.Binding.Positions[n];
                            if (position.CandidateSymbol is { } candidate && ReferenceEquals(Binding.CandidateOriginBinder(candidate), origin.Binder) && candidate.Slot == origin.Slot)
                            {
                                Record(match.Subject);
                                guardCandidate = true;
                            }
                        }
                    }

                    if (guardCandidate)
                    {
                        return; // The selected body binding shares syntax, but is not the guard's storage.
                    }
                }

                foreach (var entry in this.SymbolPlaces)
                {
                    if (ReferenceEquals(entry.Key.Declaration, origin.Binder) && entry.Key.Slot == (origin.Kind == OriginKind.Input ? origin.InputIndex : origin.Slot) &&
                        (origin.Kind != OriginKind.Input || entry.Key.Kind == BindingSymbolKind.Parameter))
                    {
                        if (origin.Kind == OriginKind.Input && this.Places[entry.Value].Type is { Kind: BoundTypeKind.Slice, Semantics: SemanticsKind.Owner })
                        {
                            // SPEC 4.6.5, 4.6.6: a by-value Slice parameter is a Copy handle whose source is the caller's
                            // storage; a result retaining that source depends on no local root.
                            continue;
                        }

                        Record(entry.Value);
                    }
                }

                // A parameter's Projection Origin is bound by its function and resolves through SymbolPlaces above;
                // the function's Result place shares that Source and is never the borrowed storage.
                var hasDeferredRoot = false;
                if (this.Places[place].DeferredExecution >= 0 && origin.Binder is not Parsing.FunctionKoto)
                {
                    for (var root = 0; root < count && !hasDeferredRoot; root++)
                    {
                        hasDeferredRoot = this.IsTemporaryOriginRoot(root, origin) && this.SharesDeferredExecution(place, root);
                    }
                }

                for (var root = 0; root < count && origin.Binder is not Parsing.FunctionKoto; root++)
                {
                    if (this.IsTemporaryOriginRoot(root, origin) && (!hasDeferredRoot || this.SharesDeferredExecution(place, root)))
                    {
                        Record(root);
                    }
                }

                void Record(int root)
                {
                    if (mode == LoanRequirement.None)
                    {
                        return;
                    }

                    if (!RecordDependency(place, root, mode))
                    {
                        return;
                    }

                    var source = this.Places[root].Type;
                    if (ReferenceTypes.IsBorrow(source) || ReferenceTypes.IsString(source))
                    {
                        // A reborrow's access mode comes from its own Type. Only
                        // nested dependencies survive here; retain the parent's
                        // stronger authority separately through value transfer.
                        for (var i = 0; i < source.Components.Count; i++)
                        {
                            AddType(place, source.Components[i], mode);
                        }
                    }
                    else
                    {
                        // Shared access to a dependent owner does not acquire its stored exclusive references.
                        // RetainBorrowAuthority preserves that owner's stronger authority separately.
                        AddType(place, source, mode);
                    }
                }
            }
        }

        bool RecordDependency(int place, int root, LoanRequirement mode)
        {
            if (!any)
            {
                // Allocate only for an actual local dependency or a retained input capability.
                this.borrowDependencies.Reset(OwnershipStorage.Cells(count, count, 2, "borrow dependencies"));
                Grow(ref this.dependencyRoots, count);
                this.dependencyRoots.AsSpan(0, count).Clear();
                Grow(ref this.borrowRootLoss, count);
                this.borrowRootLoss.AsSpan(0, count).Clear();
                any = true;
            }

            var dependency = this.borrowDependencies[(place * count) + root];
            if (dependency >= mode)
            {
                return false;
            }

            this.borrowDependencies[(place * count) + root] = mode;
            if (!this.dependencyRoots[root])
            {
                this.dependencyRoots[root] = true;
                (this.borrowRoots ??= new()).Add(root);
            }

            return true;
        }

        bool Uses(int id, int place)
        {
            var operation = this.Operations[id];
            if (operation.Kind is OwnershipOperationKind.CheckDictionaryKey or OwnershipOperationKind.StoreDictionaryEntry)
            {
                return operation.Place == place || operation.Input == place ||
                    (operation.Kind == OwnershipOperationKind.StoreDictionaryEntry && this.OperationSteps[id] == place);
            }

            if (this.Values[id] is { Kind: OwnershipValueKind.Formatting, Count: 1 } formatting &&
                ValuePlaceForBorrow(this.Operations[this.ValueOperands[formatting.Start]]) == place)
            {
                return true;
            }

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

            if (operation.Kind == OwnershipOperationKind.EndComparisonLoans)
            {
                for (var loan = this.LoanInputs[id]; loan >= 0; loan = this.ComparisonLoans[loan].Parent)
                {
                    if (this.ComparisonLoans[loan] is { Guard: >= 0 } guard && guard.Place == place)
                    {
                        return true; // Subject and referents remain protected through guard cleanup.
                    }
                }
            }

            if (this.Values[id] is { Kind: OwnershipValueKind.Sequence, Constant: var sequence } && this.Sequences[(int)sequence].Receiver == place)
            {
                return true;
            }

            if (operation.Input == place && operation.Kind is OwnershipOperationKind.Write or OwnershipOperationKind.WriteElement or OwnershipOperationKind.WriteBorrowedField or OwnershipOperationKind.PayloadPlacement or OwnershipOperationKind.InitializeSubject)
            {
                return true;
            }

            return operation.Place == place && operation.Kind switch
            {
                OwnershipOperationKind.Read or OwnershipOperationKind.Consume or OwnershipOperationKind.Borrow or OwnershipOperationKind.CallEntry or OwnershipOperationKind.Deliver or OwnershipOperationKind.LocateReceiver or OwnershipOperationKind.WriteBorrowedField or OwnershipOperationKind.StorePointer or OwnershipOperationKind.PatternTest or OwnershipOperationKind.AcquirePattern => true,
                OwnershipOperationKind.Cleanup => Observes(this.Places[place].Type),
                _ => false,
            };
        }

        static bool Kills(OwnershipOperation operation, int place)
            => (operation.Place == place && operation.Kind is OwnershipOperationKind.Declare or OwnershipOperationKind.Produce or OwnershipOperationKind.InitializeReceiverField or OwnershipOperationKind.InitializeSubject or OwnershipOperationKind.Write or OwnershipOperationKind.Cleanup or OwnershipOperationKind.CallEntry or OwnershipOperationKind.Deliver or OwnershipOperationKind.StorePointer) ||
                (operation.Input == place && operation.Kind is OwnershipOperationKind.Consume or OwnershipOperationKind.Borrow or OwnershipOperationKind.AcquirePattern) ||
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

                if (type.StoredBase is { } parent && Observes(parent, depth + 1))
                {
                    return true;
                }
            }

            if (ObjectTypes.IsOwner(type) || type.Kind is BoundTypeKind.Closure or BoundTypeKind.Tuple or BoundTypeKind.FixedArray or BoundTypeKind.Array or BoundTypeKind.Dictionary)
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

    private static LoanRequirement NamedOriginRequirement(BoundType type, BoundOrigin origin)
    {
        var result = Contains(type.Origin, origin)
            ? type.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq ? LoanRequirement.Uniq : LoanRequirement.Ref
            : LoanRequirement.None;
        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (Contains(type.OriginArguments[i], origin))
            {
                result = (LoanRequirement)Math.Max((int)result, (int)(type.Symbol?.Schema?.Origins[i].LoanRequirement ?? LoanRequirement.Ref));
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            result = (LoanRequirement)Math.Max((int)result, (int)NamedOriginRequirement(type.Components[i], origin));
        }

        return result;

        static bool Contains(BoundOrigin? expression, BoundOrigin origin)
        {
            if (ReferenceEquals(expression, origin))
            {
                return true;
            }

            if (expression is not null)
            {
                for (var i = 0; i < expression.Operands.Count; i++)
                {
                    if (Contains(expression.Operands[i], origin))
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

    // Whether every Origin that `type` names is a slot of `owner` (the receiver's declaring Type), and whether it names one.
    private static bool NamesOnlyReceiverOrigins(BoundType type, DeclarationContainerKoto owner, out bool named)
    {
        named = false;
        return Visit(type, owner, ref named);

        static bool Visit(BoundType type, DeclarationContainerKoto owner, ref bool named)
        {
            if (!Receiver(type.Origin, owner, ref named))
            {
                return false;
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                if (!Receiver(type.OriginArguments[i], owner, ref named))
                {
                    return false;
                }
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                if (!Visit(type.Components[i], owner, ref named))
                {
                    return false;
                }
            }

            return true;
        }

        static bool Receiver(BoundOrigin? origin, DeclarationContainerKoto owner, ref bool named)
        {
            if (origin is null || origin.Kind == OriginKind.Static)
            {
                return true;
            }

            named = true;
            return origin.Kind == OriginKind.Parameter && ReferenceEquals(origin.Binder, owner);
        }
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

    // Declared lifetime parameters need a capability root independent of their shared Origin name.
    private bool IsExclusiveBorrowInput(int place)
        => this.Places[place].Kind == OwnershipPlaceKind.Parameter &&
            this.Places[place].Type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq, Origin.Kind: OriginKind.Parameter };

    // Retain stronger input authority separately from the shared access a child actually acquires.
    private void RetainBorrowAuthority(int count)
    {
        this.retainedBorrowAuthority.CopyFrom(this.borrowDependencies, OwnershipStorage.Cells(count, count, 2, "retained borrow authority"));
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
                    case OwnershipOperationKind.StoreDictionaryEntry:
                        Merge(operation.Place, operation.Input);
                        Merge(operation.Place, this.OperationSteps[id]);
                        break;
                    case OwnershipOperationKind.Call:
                        for (var entry = id - 1; entry >= 0 && this.Operations[entry] is { Kind: OwnershipOperationKind.CallEntry } input && ReferenceEquals(input.Source, operation.Source); entry--)
                        {
                            Merge(operation.Place, input.Place);
                            // A writable referent may retain another argument when its complete stored Type names that
                            // argument's Origin. Carry actual input capabilities through that public contract, not its body.
                            if (input.Place >= 0 && this.Places[input.Place].Type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq, Components: [var storage] } && RetainsInput(storage))
                            {
                                for (var argument = id - 1; argument >= 0 && this.Operations[argument] is { Kind: OwnershipOperationKind.CallEntry } incoming && ReferenceEquals(incoming.Source, operation.Source); argument--)
                                {
                                    if (incoming.Place == input.Place)
                                    {
                                        continue;
                                    }

                                    Merge(input.Place, incoming.Place, storage, id);
                                    for (var borrow = entry - 1; borrow >= 0; borrow--)
                                    {
                                        if (this.Operations[borrow] is { Kind: OwnershipOperationKind.Borrow } receiver && receiver.Input == input.Place)
                                        {
                                            Merge(receiver.Place, incoming.Place, storage, id);
                                        }
                                    }
                                }
                            }
                        }

                        break;
                }

                if (this.Values[id] is { Kind: OwnershipValueKind.Alias, Count: 1 } alias && this.ValueOperands[alias.Start] is >= 0 and var value)
                {
                    Merge(ValuePlaceForBorrow(operation), ValuePlaceForBorrow(this.Operations[value]));
                }
                else if (this.Values[id] is { Kind: OwnershipValueKind.PointerLoad or OwnershipValueKind.BorrowedField, Count: 1 } loaded)
                {
                    Merge(ValuePlaceForBorrow(operation), ValuePlaceForBorrow(this.Operations[this.ValueOperands[loaded.Start]]));
                }
                else if (this.Values[id] is { Kind: OwnershipValueKind.Sequence, Constant: var sequence })
                {
                    Merge(ValuePlaceForBorrow(operation), this.Sequences[(int)sequence].Receiver);
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

        bool RetainsInput(BoundType storage)
        {
            for (var rootIndex = 0; rootIndex < this.borrowRoots!.Count; rootIndex++)
            {
                var root = this.borrowRoots[rootIndex];
                if (this.IsExclusiveBorrowInput(root) && NamedOriginRequirement(storage, this.Places[root].Type.Origin!) != LoanRequirement.None)
                {
                    return true;
                }
            }

            return false;
        }

        void Merge(int destination, int source, BoundType? storage = null, int storedAt = 0)
        {
            if (destination < 0 || source < 0 || destination == source)
            {
                return;
            }

            for (var rootIndex = 0; rootIndex < this.borrowRoots!.Count; rootIndex++)
            {
                var root = this.borrowRoots[rootIndex];
                if (storage is not null && (!this.IsExclusiveBorrowInput(root) ||
                    NamedOriginRequirement(storage, this.Places[root].Type.Origin!) == LoanRequirement.None))
                {
                    continue;
                }

                var original = this.retainedBorrowAuthority[(destination * count) + root];
                var target = original;
                var input = this.retainedBorrowAuthority[(source * count) + root];
                var start = this.storedBorrowStarts?.GetValueOrDefault((source, root)) ?? 0;
                start = Math.Max(start, storedAt);
                if (input != LoanRequirement.None && target == LoanRequirement.None && this.IsExclusiveBorrowInput(root) &&
                    NamedOriginRequirement(this.Places[destination].Type, this.Places[root].Type.Origin!) is not LoanRequirement.None and var requirement)
                {
                    this.borrowDependencies[(destination * count) + root] = requirement;
                    target = requirement;
                    changed = true;
                    if (start > 0)
                    {
                        (this.storedBorrowStarts ??= new())[(destination, root)] = start;
                    }
                }
                else if (input != LoanRequirement.None && target != LoanRequirement.None &&
                    this.storedBorrowStarts is { } starts && starts.TryGetValue((destination, root), out var previous) && start < previous)
                {
                    starts[(destination, root)] = start;
                    changed = true;
                }

                if (target != LoanRequirement.None && input > target)
                {
                    target = input;
                    changed = true;
                }

                if (target != original)
                {
                    this.retainedBorrowAuthority[(destination * count) + root] = target;
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
    // Recorded once per preparation; the Origin mapping queries it for every root of every Place.
    private bool IsBorrowedPlace(int place) => this.borrowedPlaces[place];

    private bool IsBorrowAncestor(int value, int place)
    {
        var original = place;
        // Moving a reference changes its slot, not the capability's identity. A child formed before the Move
        // still descends from that same parent; stop at a Borrow, which does create a distinct child Loan.
        for (var remaining = this.Places.Count; remaining > 0 && ReferenceTypes.IsBorrow(this.Places[place].Type) &&
            this.borrowDefinitions[place] is >= 0 and var definition; remaining--)
        {
            var transferred = this.Operations[definition] switch
            {
                { Kind: OwnershipOperationKind.Write, Input: >= 0 } write => write.Input,
                { Kind: OwnershipOperationKind.Consume, Acquisition: AcquisitionKind.Move, Place: >= 0 } moved => moved.Place,
                _ => -1,
            };
            if (transferred < 0 || transferred == place)
            {
                break;
            }

            place = transferred;
        }

        // Follow the actual value's reborrow chain; equal Origin names alone do
        // not authorize use of a parent while a sibling/child Loan is required.
        for (var remaining = this.Values.Count; remaining > 0 && (uint)value < (uint)this.Values.Count; remaining--)
        {
            var operation = this.Operations[value];
            if (ValuePlaceForBorrow(operation) == place || ValuePlaceForBorrow(operation) == original ||
                (operation.Kind is OwnershipOperationKind.Read or OwnershipOperationKind.Consume && operation.Place == place))
            {
                return true;
            }

            var node = this.Values[value];
            if (operation.Kind == OwnershipOperationKind.Borrow && operation.Place >= 0 && node.Kind == OwnershipValueKind.Address && node.Count == 0 &&
                this.Places[operation.Place].Type.Semantics == SemanticsKind.Owner &&
                (operation.Place == place || this.borrowDependencies[(operation.Place * this.Places.Count) + place] != LoanRequirement.None))
            {
                // Borrowing an owned dependent handle delegates its stored input
                // authority. The dependency must name this actual ancestor Place;
                // a sibling with the same Origin is not an ancestor.
                return true;
            }

            if (operation.Kind == OwnershipOperationKind.Call)
            {
                // A returned reference descends from the one acquired argument
                // its public result Origin names, and a result naming only the
                // receiver Type's Origins from the receiver; any other contract
                // stops here.
                value = this.ResultArgument(value);
                continue;
            }

            if (node.Kind == OwnershipValueKind.Sequence && operation.Kind == OwnershipOperationKind.Produce)
            {
                // SPEC 14.6.2, 4.6: an element borrowed or read through a sequence descends from its receiver: a borrowed
                // receiver value, or the owned receiver Place of a view such as the implicit Slice of a loop.
                if (this.Sequences[(int)node.Constant].Receiver == place)
                {
                return true;
                }

                if (node.Count != 1)
                {
                return false;
                }

                value = this.ValueOperands[node.Start];
                continue;
            }

            if (node.Kind is OwnershipValueKind.PointerLoad or OwnershipValueKind.BorrowedField && node.Count == 1)
            {
                // SPEC 3.4.1, 10.2: a reference loaded through another reference descends from that reference.
                value = this.ValueOperands[node.Start];
                continue;
            }

            if (node.Kind is not (OwnershipValueKind.Alias or OwnershipValueKind.Address) || node.Count != 1)
            {
                // An immutable reference local retains the ancestry of its one
                // initialization. Follow the actual stored value, never merely
                // a matching Origin (which could name a sibling Loan). A pattern
                // binding continues at its Subject, a Subject at its source.
                var stored = operation.Kind switch
                {
                    OwnershipOperationKind.Read when operation.Place >= 0 &&
                        this.Places[operation.Place] is { Kind: OwnershipPlaceKind.Local, Mutable: false } local && ReferenceTypes.IsBorrow(local.Type) => operation.Place,
                    OwnershipOperationKind.AcquirePattern => this.PayloadSubject(operation.Place),
                    OwnershipOperationKind.InitializeSubject => operation.Input,
                    OwnershipOperationKind.Consume when operation.Acquisition == AcquisitionKind.Copy && operation.Place >= 0 &&
                        this.Places[operation.Place].Type.Semantics == SemanticsKind.Ref => operation.Place,
                    OwnershipOperationKind.CallEntry => operation.Place,
                    OwnershipOperationKind.Produce when operation.Place >= 0 => operation.Place,
                    _ => -1,
                };
                if (stored >= 0 && this.borrowDefinitions[stored] is >= 0 and var definition && definition < value)
                {
                    value = definition;
                    continue;
                }

                if (stored >= 0 && this.ProducingValue(stored, value) is >= 0 and var producer)
                {
                    value = producer; // A Subject or binding initialized by a call result or a Move continues at its source.
                    continue;
                }

                return false;
            }

            value = this.ValueOperands[node.Start];
        }

        return false;
    }

    // A Case payload Place continues at the Subject it was decomposed from (SPEC 15.1.6); a component of a nested
    // pattern, such as `.Some((let key, let value))`, continues through each enclosing payload to the outermost Subject.
    private int PayloadSubject(int place)
    {
        for (var depth = 0; depth < this.DecompositionStorage.Count && place >= 0 && this.Places[place].Kind == OwnershipPlaceKind.Payload; depth++)
        {
            var subject = -1;
            for (var i = 0; i < this.DecompositionStorage.Count && subject < 0; i++)
            {
                var decomposition = this.DecompositionStorage[i];
                if (place >= decomposition.PayloadStart && place < decomposition.PayloadStart + decomposition.PayloadCount)
                {
                    subject = decomposition.Place;
                }
            }

            if (subject < 0)
            {
                return place;
            }

            place = subject;
        }

        return place;
    }

    // The operation before `before` that produced `place`: the call whose result it is, or, for a Move of a local into it,
    // that local's single definition.
    private int ProducingValue(int place, int before)
    {
        for (var id = before - 1; id >= 0; id--)
        {
            var operation = this.Operations[id];
            if (operation.Kind == OwnershipOperationKind.Call && operation.Place == place)
            {
                return id;
            }

            if (operation.Kind == OwnershipOperationKind.Consume && operation.Input == place && operation.Place >= 0)
            {
                return this.borrowDefinitions[operation.Place] is >= 0 and var definition && definition < id ? definition : this.ProducingValue(operation.Place, id);
            }
        }

        return -1;
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
        else if (node.Kind is OwnershipValueKind.BorrowedUpdate or OwnershipValueKind.PointerLoad or OwnershipValueKind.PointerStore)
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
            else if (operation.Kind == OwnershipOperationKind.AcquirePattern && node.Kind == OwnershipValueKind.PatternProjection)
            {
                var arm = this.MatchArms[this.OperationSteps[value]];
                var positions = this.Matches[arm.Match].Binding.Positions;
                for (var position = (int)node.Constant; position != arm.Pattern;)
                {
                    var child = positions[position];
                    if (child.Parent < 0 || depth == selectors.Length)
                    {
                        return -1;
                    }

                    selectors[depth++] = child.Element;
                    var parent = positions[child.Parent];
                    if (parent.ImplicitFollows != 0)
                    {
                        // Parts of the same borrowed Tuple are inline and disjoint. A further stored reference
                        // changes the referent, so it cannot provide a structural non-overlap proof.
                        return ReferenceEquals(parent.MatchedType, this.Places[operation.Place].Type) ? operation.Place : -1;
                    }

                    position = child.Parent;
                }

                return operation.Place;
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

    private bool IsTemporaryOriginRoot(int root, BoundOrigin origin)
    {
        var candidate = this.Places[root];
        return origin.Kind == OriginKind.Projection && candidate.Kind is OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result &&
            (ReferenceEquals(candidate.Type, BoundType.String) || StructStorage.IsStruct(candidate.Type) || EnumStorage.IsEnum(candidate.Type) || candidate.Type.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Tuple or BoundTypeKind.Array or BoundTypeKind.Dictionary || ScalarTypes.Supports(candidate.Type)) &&
            ReferenceEquals(candidate.Source, origin.Binder) && (!ScalarTypes.Supports(candidate.Type) || this.IsBorrowedPlace(root));
    }

    private bool SharesDeferredExecution(int place, int root)
    {
        var required = this.Places[root].DeferredExecution;
        if (required < 0)
        {
            return true;
        }

        var current = this.Places[place].DeferredExecution;
        for (var remaining = this.DeferredPlans.Count; remaining > 0 && (uint)current < (uint)this.DeferredPlans.Count; remaining--)
        {
            if (current == required)
            {
                return true;
            }

            current = this.DeferredPlans[current].Parent;
        }

        return false;
    }

    // The receiver's CallEntry, the first of the entries that immediately precede the Call.
    private int ReceiverEntry(int call)
    {
        var entries = 0;
        while (entries < call && this.Operations[call - entries - 1] is { Kind: OwnershipOperationKind.CallEntry } entry && ReferenceEquals(entry.Source, this.Operations[call].Source))
        {
            entries++;
        }

        return entries == 0 ? -1 : call - entries;
    }

    private int ResultArgument(int call)
    {
        if (this.Operations[call].Source is not InvocationKoto { BoundValueCall: null, BoundCall: { Target: { CompilerFunction: CompilerFunctionKind.None, Declaration: FunctionKoto target } } plan })
        {
            return -1;
        }

        var inputSlot = -1;
        if (target.ReturnType?.BoundType is not { } declaredResult || !FindInput(declaredResult) || inputSlot < 0)
        {
            var entry = this.ReceiverEntry(call);
            if (target.Parameters.Count == 1 && entry >= 0 && this.Operations[call].Place >= 0 &&
                this.ResultOriginsFromInput(this.Places[this.Operations[call].Place].Type, this.Places[this.Operations[entry].Place].Type, out var retained) && retained)
            {
                return entry; // The sole acquired input supplies every dependency, including those nested in a generic Item.
            }

            // SPEC 15.6.3, 22.1.2.4: a result that names only Origins of the receiver's own Type, such as an Iterator's item
            // `Option<uniq/T during source>`, keeps Loans the receiver's value holds, so it descends from the receiver.
            return plan.Receiver is not null && target.ReturnType?.BoundType is { } result && target.BoundSymbol?.Scope.Owner is DeclarationContainerKoto owner &&
                NamesOnlyReceiverOrigins(result, owner, out var named) && named ? this.ReceiverEntry(call) : -1;
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
            index = plan.ReceiverOperation.ParameterIndex == inputSlot ? 0 : -1;
        }

        var mapping = plan.ArgumentToParameter;
        for (var i = 0; i < mapping.Length && index < 0; i++)
        {
            index = mapping[i] == inputSlot ? offset + i : -1;
        }

        return index < 0 ? -1 : call - entries + index;

        // A contract may wrap its borrowed result in Option, a Tuple or another dependent Type.
        // Follow one explicitly named input through every layer, never choose between inputs
        // because their instantiated Origins happen to be equal.
        bool FindInput(BoundType type)
        {
            if (type.Origin is { } origin && !VisitOrigin(origin))
            {
                return false;
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                if (!VisitOrigin(type.OriginArguments[i]))
                {
                    return false;
                }
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                if (!FindInput(type.Components[i]))
                {
                    return false;
                }
            }

            return true;
        }

        bool VisitOrigin(BoundOrigin origin)
        {
            if (origin.Kind == OriginKind.Intersection)
            {
                for (var i = 0; i < origin.Operands.Count; i++)
                {
                    if (!VisitOrigin(origin.Operands[i]))
                    {
                        return false;
                    }
                }
            }
            else if (origin.Kind == OriginKind.Input && ReferenceEquals(origin.Binder, target))
            {
                if (inputSlot >= 0 && inputSlot != origin.Slot)
                {
                    return false;
                }

                inputSlot = origin.Slot;
            }

            return true;
        }
    }

    private bool ResultOriginsFromInput(BoundType result, BoundType input, out bool retained)
    {
        retained = false;
        return Visit(result, ref retained);

        bool Visit(BoundType type, ref bool found)
        {
            if (type.Origin is { Kind: not OriginKind.Static } origin)
            {
                found = true;
                if (NamedOriginRequirement(input, origin) == LoanRequirement.None)
                {
                    return false;
                }
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                if (type.OriginArguments[i].Kind != OriginKind.Static)
                {
                    found = true;
                    if (NamedOriginRequirement(input, type.OriginArguments[i]) == LoanRequirement.None)
                    {
                        return false;
                    }
                }
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                if (!Visit(type.Components[i], ref found))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
