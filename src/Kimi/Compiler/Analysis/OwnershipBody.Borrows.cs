// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipBody
{
    private readonly List<(int To, int Next)> checkingBorrowEdges = new();

    // SPEC 8.4.10.4: each Place that keeps the result of a generic requirement call, with that result's index.
    private readonly List<(int Holder, int Result)> requirementHolders = new();
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

    // Each acquisition reported against a holder's Loan, with the Place that received the rejected Loan.
    private List<(int Holder, int Result)>? rejectedAcquisitions;

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
        this.ClearStoredBorrows();
        this.borrowRoots?.Clear();
        this.preparedLoanConflicts?.Clear();
        this.activatedLoans?.Clear();
        this.rejectedAcquisitions?.Clear();
        this.reservationOverlaps?.Clear();
        this.overlapActivations?.Clear();
        var count = this.Places.Count;
        var dependent = this.ConditionalReborrows is { Count: > 0 } || this.RequirementResults is { Count: > 0 };
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

        this.requirementHolders.Clear();
        if (this.ConditionalReborrows is { Count: > 0 } || this.RequirementResults is { Count: > 0 })
        {
            AddExplicitDependencies();
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
                        this.AddStoredBorrowStart(entry.Place, root, id);
                    }
                }
            }
        }

        this.PrepareSlicePaths();
        this.PrepareCheckingBorrowEdges();
        this.PrepareStoredBorrowActivity();
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
                    if (!DefinesBorrowHolder(this.Operations[op], p))
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
                    if (!this.borrowLive.IsSet((op * liveWidth) + slot) || (activating && this.SameReservedArgument(r, p)))
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
                        if (this.Places[root].Kind == OwnershipPlaceKind.EffectRegion)
                        {
                            continue; // Only the derived effects of requirement calls reach a region (VerifyRequirementEffects).
                        }

                        var mode = this.BorrowModeAt(p, root, op, this.borrowDependencies[(p * count) + root]);
                        if (mode == LoanRequirement.None)
                        {
                            continue;
                        }

                        var external = this.Places[root].Kind == OwnershipPlaceKind.Anchor || (this.Places[root].Kind == OwnershipPlaceKind.Parameter && (ReferenceTypes.IsBorrow(this.Places[root].Type) || ReferenceTypes.IsString(this.Places[root].Type) || this.IsPairInput(root)));
                        var authority = this.BorrowModeAt(p, root, op, this.retainedBorrowAuthority[(p * count) + root]);
                        var accessConflict = ConflictsWithComparison(operation.Kind, operation.Place, operation.Input, operation.Acquisition, root, authority, accessMode) ||
                            this.ElementAccessConflicts(operation, root, authority);
                        if (accessConflict && operation.Kind is OwnershipOperationKind.Borrow or OwnershipOperationKind.ProjectElement or OwnershipOperationKind.WriteElement or OwnershipOperationKind.Produce &&
                            this.IsDisjointProjection(accessId, p, root))
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
                            var holder = p;
                            var reserved = this.reservationPlaces[p];
                            if (reserved >= 0 && this.RetainingTarget(reserved, root, count) is >= 0 and var target)
                            {
                                // The reserved target's own Loan on another root, not an overlap with the reservation.
                                holder = target;
                                reserved = this.reservationPlaces[target];
                            }

                            var own = activating ? -1 : operation.Reservation >= 0 ? operation.Reservation : this.LocatedReservation(accessId);
                            var at = own >= 0 && operation.Reservation < 0 ? this.Operations[accessId + 1].Source : operation.Source;
                            if (own >= 0 && reserved < 0)
                            {
                                // The reserved input itself meets a retained Loan; its activation decides the record.
                                (this.preparedLoanConflicts ??= new()).Add((own, new(at, OwnershipFailure.ComparisonLoanConflict, Place: holder, LoanSource: this.Places[holder].Source, Input: this.Lending(own))));
                                continue;
                            }

                            if (activating)
                            {
                                var issue = new OwnershipIssue(this.Operations[op].Source, OwnershipFailure.ComparisonLoanConflict, Place: holder, Reservation: r, Activation: true, LoanSource: this.Places[holder].Source, Input: this.Lending(r));
                                if (reserved >= 0)
                                {
                                    // Another reserved input of an enclosing or the same call; its preparation may already state the overlap.
                                    this.HoldOverlapActivation(r, reserved, issue);
                                }
                                else
                                {
                                    (this.activatedLoans ??= new()).Add((issue.Source, issue.LoanSource));
                                    this.ReportIssue(issue);
                                }

                                continue;
                            }

                            if (reserved >= 0 && ReferenceEquals(operation.Source, this.CallReservations[reserved].Call) && this.ReservationMode(reserved, op) == LoanRequirement.Uniq)
                            {
                                // The call's own effect after activation, such as an intrinsic update through one target while
                                // another target is lent, is part of activating that call.
                                this.HoldOverlapActivation(reserved, -1, new(operation.Source, OwnershipFailure.ComparisonLoanConflict, Place: holder, Reservation: reserved, Activation: true, LoanSource: this.Places[holder].Source));
                                continue;
                            }

                            if (reserved >= 0)
                            {
                                // SPEC 15.6.7: the operation conflicts with a reservation, which is shown as its lending point.
                                this.ReportReservationConflict(new(at, OwnershipFailure.ComparisonLoanConflict, Place: holder, Reservation: reserved), own, reserved);
                                continue;
                            }

                            if (this.RestatesRejectedAcquisition(holder, value.Count > 0 ? this.ValueOperands[value.Start] : accessId))
                            {
                                continue; // The rejected acquisition's record already states this overlap of the same two Loans.
                            }

                            if (operation.Kind == OwnershipOperationKind.Borrow && operation.Input >= 0)
                            {
                                (this.rejectedAcquisitions ??= new()).Add((holder, operation.Input));
                            }

                            this.ReportIssue(new(operation.Source, OwnershipFailure.ComparisonLoanConflict, Place: holder, LoanSource: this.Places[holder].Source));
                        }
                    }
                }
            }
        }

        this.ReportPreparedLoanConflicts();
        this.VerifyRequirementEffects(count, liveWidth);

        void AddType(int place, BoundType type, LoanRequirement bound = LoanRequirement.Uniq)
        {
            // SPEC 13.5.5.1: a pair parameter's Origin is the caller-side dependency of a reference it may hold, never a local root.
            if (type.Origin is { } origin && !(this.Places[place].Kind == OwnershipPlaceKind.Parameter && Binding.TryPairLayer(type, out _, out _)))
            {
                // SPEC 15.6.2: a value borrow reaches only its referent, whose own Origins are added below; a part of an
                // owned root, such as item.1@ref, does not acquire what the root's other parts depend on.
                var referent = type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 };
                AddOrigin(place, origin, type.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq ? bound : LoanRequirement.Ref, referent);
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                AddOrigin(place, type.OriginArguments[i], (LoanRequirement)Math.Min((int)bound, (int)(type.Symbol?.Schema?.Origins[i].LoanRequirement ?? LoanRequirement.Ref)));
            }

            // SPEC 3.3.6: shared access cannot use an inner uniq exclusively, so a Type below a shared layer (ref/uniq/T) keeps
            // its inner Loans only in shared mode.
            var inner = type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.ObjRef } ? LoanRequirement.Ref : bound;
            for (var i = 0; i < type.Components.Count; i++)
            {
                AddType(place, type.Components[i], inner);
            }
        }

        void AddOrigin(int place, BoundOrigin origin, LoanRequirement mode, bool referent = false)
        {
            if (origin.Kind == OriginKind.Intersection)
            {
                for (var i = 0; i < origin.Operands.Count; i++)
                {
                    AddOrigin(place, origin.Operands[i], mode, referent);
                }
            }
            else if (origin.Kind is OriginKind.Projection or OriginKind.Anchor || (origin.Kind == OriginKind.Input && (ReferenceEquals(origin.Binder, this.Function) || ReferenceEquals(origin.Binder, this.Function.Accessor?.Declaration))))
            {
                if (origin.Kind == OriginKind.Anchor)
                {
                    // SPEC 5.2.2: the raw Place borrow's own anchor, an external root like a borrowed parameter's referent.
                    if (this.AnchorPlace(origin) is >= 0 and var anchor)
                    {
                        Record(anchor);
                    }

                    return;
                }

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
                        if (origin.Kind == OriginKind.Input && this.Places[entry.Value].Type.Semantics == SemanticsKind.Owner &&
                            (origin.Occurrence is not null || this.Places[entry.Value].Type.Kind == BoundTypeKind.Slice))
                        {
                            // SPEC 15.2.1, 15.3.1, 4.6.5: the slot of an owned input, such as value.source or a by-value Slice's
                            // source, names the caller's Loan, not the input's storage; a result retaining it depends on no
                            // local root, so the input itself may be moved into that result.
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
                    else if (!referent)
                    {
                        // Shared access to a dependent owner does not acquire its stored exclusive references.
                        // RetainBorrowAuthority preserves that owner's stronger authority separately.
                        AddType(place, source, mode);
                    }
                }
            }
        }

        // SPEC 8.9, 8.4.10.4: dependencies that no Origin of a Type carries. A conditional plan's Reborrow case is checked like a
        // concrete exclusive reference: each pair parameter is an external capability root, and the reborrowed value inherits the
        // dependencies of the Place it Reborrows; a Place with no known root keeps itself as the root, and the Copy cases add no
        // Loan. The result of a generic requirement call keeps the Loans of the effect regions its call reached. Both follow the
        // value, whole or as a part, into the Places whose Types have abstract parts.
        void AddExplicitDependencies()
        {
            var conditional = this.ConditionalReborrows;
            if (conditional is { Count: > 0 })
            {
                for (var p = 0; p < count; p++)
                {
                    if (this.IsPairInput(p))
                    {
                        RecordDependency(p, p, LoanRequirement.Uniq);
                    }
                }
            }

            if (this.RequirementResults is { Count: > 0 } results)
            {
                for (var i = 0; i < results.Count; i++)
                {
                    RecordDependency(results[i].Result, results[i].Region, results[i].Mode);
                    this.requirementHolders.Add((results[i].Result, i));
                }
            }

            Propagate();
            var unrooted = false;
            for (var i = 0; conditional is not null && i < conditional.Count; i++)
            {
                if (!HasDependency(conditional[i].Place))
                {
                    unrooted |= RecordDependency(conditional[i].Place, conditional[i].Root, LoanRequirement.Uniq);
                }
            }

            if (unrooted)
            {
                Propagate();
            }

            void Propagate()
            {
                bool changed;
                do
                {
                    changed = false;
                    for (var i = 0; conditional is not null && i < conditional.Count; i++)
                    {
                        changed |= Inherit(conditional[i].Place, conditional[i].Root);
                    }

                    for (var id = 0; id < this.Operations.Count; id++)
                    {
                        var (destination, source) = this.Operations[id] switch
                        {
                            { Kind: OwnershipOperationKind.Write or OwnershipOperationKind.PayloadPlacement or OwnershipOperationKind.InitializeSubject, Place: >= 0, Input: >= 0 } stored => (stored.Place, stored.Input),
                            { Kind: OwnershipOperationKind.Consume, Acquisition: AcquisitionKind.Move, Place: >= 0, Input: >= 0 } moved => (moved.Input, moved.Place),
                            { Kind: OwnershipOperationKind.AcquirePattern, Place: >= 0, Input: >= 0 } bound => (bound.Input, bound.Place),
                            _ => (-1, -1),
                        };
                        if (destination >= 0 && Carries(destination))
                        {
                            changed |= Inherit(destination, source);
                        }
                    }

                    for (var i = 0; i < this.Constructions.Count; i++)
                    {
                        var plan = this.Constructions[i];
                        for (var p = 0; Carries(plan.Place) && p < plan.PayloadCount; p++)
                        {
                            changed |= Inherit(plan.Place, plan.PayloadStart + p);
                        }
                    }

                    for (var i = 0; i < this.Decompositions.Count; i++)
                    {
                        var plan = this.Decompositions[i];
                        for (var p = 0; p < plan.PayloadCount; p++)
                        {
                            if (Carries(plan.PayloadStart + p))
                            {
                                changed |= Inherit(plan.PayloadStart + p, plan.Place);
                            }
                        }
                    }
                }
                while (changed);
            }

            bool Carries(int place)
                => this.Places[place].Kind is not (OwnershipPlaceKind.Parameter or OwnershipPlaceKind.EffectRegion or OwnershipPlaceKind.Anchor) && AbstractTypes.HasAbstractPart(this.Places[place].Type);

            bool Inherit(int destination, int source)
            {
                var changed = false;
                for (var rootIndex = 0; rootIndex < (this.borrowRoots?.Count ?? 0); rootIndex++)
                {
                    var root = this.borrowRoots![rootIndex];
                    changed |= RecordDependency(destination, root, this.borrowDependencies[(source * count) + root]);
                }

                // The requirement results a source keeps are kept by its destination too.
                for (var i = 0; i < this.requirementHolders.Count; i++)
                {
                    if (this.requirementHolders[i].Holder == source && !this.requirementHolders.Contains((destination, this.requirementHolders[i].Result)))
                    {
                        this.requirementHolders.Add((destination, this.requirementHolders[i].Result));
                        changed = true;
                    }
                }

                return changed;
            }

            bool HasDependency(int place)
            {
                for (var rootIndex = 0; rootIndex < (this.borrowRoots?.Count ?? 0); rootIndex++)
                {
                    if (this.borrowDependencies[(place * count) + this.borrowRoots![rootIndex]] != LoanRequirement.None)
                    {
                        return true;
                    }
                }

                return false;
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
        if (origin?.Kind is OriginKind.Projection or OriginKind.Input or OriginKind.Anchor)
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

    // An operation after which the Place holds a new value, or none: its earlier value, and every dependency of that value,
    // ends there. Liveness stops at it, and so does a stored dependency.
    private static bool DefinesBorrowHolder(OwnershipOperation operation, int place)
        => (operation.Place == place && operation.Kind is OwnershipOperationKind.Declare or OwnershipOperationKind.Produce or OwnershipOperationKind.InitializeReceiverField or OwnershipOperationKind.InitializeSubject or OwnershipOperationKind.Write or OwnershipOperationKind.Cleanup or OwnershipOperationKind.CallEntry or OwnershipOperationKind.Deliver or OwnershipOperationKind.StorePointer) ||
            (operation.Input == place && operation.Kind is OwnershipOperationKind.Consume or OwnershipOperationKind.Borrow or OwnershipOperationKind.AcquirePattern) ||
            (operation.Place == place && operation.Kind == OwnershipOperationKind.Consume && operation.Acquisition == AcquisitionKind.Move);

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

    // SPEC 8.4.10.4: a generic requirement call's derived effects reach every Loan that its abstract inputs' Types may denote,
    // in the inputs' modes. A live Place keeping an earlier requirement result with Loans of the same region conflicts, unless
    // both only read, or preserves results excludes the earlier results of the same requirement on the same receiver. A
    // conflict with the receiver borrow itself keeps the existing diagnostics, so a call that already has one is skipped.
    private void VerifyRequirementEffects(int count, int liveWidth)
    {
        if (this.RequirementEffects is not { Count: > 0 } effects || this.RequirementResults is not { Count: > 0 } results || this.liveBorrowPlaces is null)
        {
            return;
        }

        var checkedCall = -1;
        for (var e = 0; e < effects.Count; e++)
        {
            var effect = effects[e];
            var op = effect.Call;
            if (op == checkedCall || (!this.IsReachable(op) && !this.HasCheckingState(op)) || this.HasIssueAt(this.Operations[op].Source))
            {
                continue;
            }

            var loaded = false;
            for (var slot = 0; slot < liveWidth; slot++)
            {
                var p = this.liveBorrowPlaces[slot];
                if (!this.borrowLive.IsSet((op * liveWidth) + slot) || this.borrowDependencies[(p * count) + effect.Region] == LoanRequirement.None ||
                    (effect.Receiver.Root >= 0 && this.borrowDependencies[(p * count) + effect.Receiver.Root] != LoanRequirement.None))
                {
                    continue;
                }

                if (!loaded)
                {
                    this.LoadInput(op, !this.IsReachable(op));
                    loaded = true;
                }

                if ((this.CompleteState(p) & PlaceState.MayInit) == 0 || this.HeldRequirementConflict(p, effect, results, count) is not { } earlier)
                {
                    continue;
                }

                this.ReportIssue(new(this.Operations[op].Source, OwnershipFailure.CallEffectConflict, Place: p, LoanSource: this.Places[p].Source, Related: this.Operations[earlier.Call].Source));
                checkedCall = op;
                break;
            }
        }
    }

    // The earlier requirement result kept by `holder` whose Loans the effect may conflict with, if any: one reached through a
    // related value, unless both only read or preserves results excludes it for the same receiver.
    private OwnershipRequirementResult? HeldRequirementConflict(int holder, in OwnershipRequirementEffect effect, List<OwnershipRequirementResult> results, int count)
    {
        for (var h = 0; h < this.requirementHolders.Count; h++)
        {
            if (this.requirementHolders[h].Holder != holder)
            {
                continue;
            }

            var result = results[this.requirementHolders[h].Result];
            if (result.Region != effect.Region || result.Call == effect.Call || (result.Mode != LoanRequirement.Uniq && effect.Mode != LoanRequirement.Uniq) ||
                !this.RelatedValues(result.Input, effect.Input, count, false) ||
                (effect.Preserves && ReferenceEquals(result.Requirement, effect.Requirement) && this.RelatedValues(result.Receiver, effect.Receiver, count, true)))
            {
                continue;
            }

            return result;
        }

        return null;
    }

    // Whether two input values may be the same: an unknown value may be any (but is never proven the same), the same root may be
    // reached through the same Field, and roots are related through a dependency or a common root.
    private bool RelatedValues(OwnershipValueIdentity left, OwnershipValueIdentity right, int count, bool same)
    {
        if (left.Root < 0 || right.Root < 0)
        {
            return !same;
        }

        if (left.Root == right.Root)
        {
            return ReferenceEquals(left.Field, right.Field) || (!same && (left.Field is null || right.Field is null));
        }

        if (left.Field is not null || right.Field is not null)
        {
            return !same && this.RootsRelated(left.Root, right.Root, count);
        }

        return this.RootsRelated(left.Root, right.Root, count);
    }

    private bool RootsRelated(int left, int right, int count)
    {
        if (this.borrowDependencies[(left * count) + right] != LoanRequirement.None || this.borrowDependencies[(right * count) + left] != LoanRequirement.None)
        {
            return true;
        }

        for (var rootIndex = 0; rootIndex < this.borrowRoots!.Count; rootIndex++)
        {
            var root = this.borrowRoots[rootIndex];
            if (this.Places[root].Kind != OwnershipPlaceKind.EffectRegion && this.borrowDependencies[(left * count) + root] != LoanRequirement.None &&
                this.borrowDependencies[(right * count) + root] != LoanRequirement.None)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasIssueAt(Koto source)
    {
        for (var i = 0; i < this.IssueStorage.Count; i++)
        {
            if (ReferenceEquals(this.IssueStorage[i].Source, source))
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 5.2.2: the anchor Place that an Anchor Origin names, or -1.
    private int AnchorPlace(BoundOrigin origin)
    {
        var anchors = this.Anchors;
        for (var i = 0; anchors is not null && i < anchors.Count; i++)
        {
            if (ReferenceEquals(this.Places[anchors[i]].Source, origin.Binder))
            {
                return anchors[i];
            }
        }

        return -1;
    }

    // SPEC 8.9: a pair parameter whose Reborrow case a definition's conditional plan checks; its referent is external.
    private bool IsPairInput(int place)
        => this.ConditionalReborrows is { Count: > 0 } && this.Places[place].Kind == OwnershipPlaceKind.Parameter && Binding.TryPairLayer(this.Places[place].Type, out _, out _);

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
                        Merge(operation.Input, operation.Place, id);
                        break;
                    case OwnershipOperationKind.Write or OwnershipOperationKind.InitializeSubject or OwnershipOperationKind.PayloadPlacement:
                        Merge(operation.Place, operation.Input, id);
                        break;
                    case OwnershipOperationKind.StoreDictionaryEntry:
                        Merge(operation.Place, operation.Input, id);
                        Merge(operation.Place, this.OperationSteps[id], id);
                        break;
                    case OwnershipOperationKind.CompleteConstruction:
                        var construction = this.Constructions[this.OperationSteps[id]];
                        for (var p = 0; p < construction.PayloadCount; p++)
                        {
                            Merge(construction.Place, construction.PayloadStart + p, id);
                        }

                        break;
                    case OwnershipOperationKind.DecomposeCase:
                        var decomposition = this.Decompositions[this.OperationSteps[id]];
                        for (var p = 0; p < decomposition.PayloadCount; p++)
                        {
                            Merge(decomposition.PayloadStart + p, decomposition.Place, id);
                        }

                        break;
                    case OwnershipOperationKind.Call:
                        for (var entry = id - 1; entry >= 0 && this.Operations[entry] is { Kind: OwnershipOperationKind.CallEntry } input && ReferenceEquals(input.Source, operation.Source); entry--)
                        {
                            Merge(operation.Place, input.Place, id);
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

                                    Merge(input.Place, incoming.Place, id, storage);
                                    for (var borrow = entry - 1; borrow >= 0; borrow--)
                                    {
                                        if (this.Operations[borrow] is { Kind: OwnershipOperationKind.Borrow } receiver && receiver.Input == input.Place)
                                        {
                                            Merge(receiver.Place, incoming.Place, id, storage);
                                        }
                                    }
                                }
                            }
                        }

                        break;
                }

                if (this.Values[id] is { Kind: OwnershipValueKind.Alias, Count: 1 } alias && this.ValueOperands[alias.Start] is >= 0 and var value)
                {
                    Merge(ValuePlaceForBorrow(operation), ValuePlaceForBorrow(this.Operations[value]), id);
                }
                else if (this.Values[id] is { Kind: OwnershipValueKind.PointerLoad or OwnershipValueKind.BorrowedField, Count: 1 } loaded)
                {
                    Merge(ValuePlaceForBorrow(operation), ValuePlaceForBorrow(this.Operations[this.ValueOperands[loaded.Start]]), id);
                }
                else if (this.Values[id] is { Kind: OwnershipValueKind.Sequence, Constant: var sequence })
                {
                    Merge(ValuePlaceForBorrow(operation), this.Sequences[(int)sequence].Receiver, id);
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

        // Records when the destination's dependency on root exists: after the call at `at` that retains the referent through the
        // storage Type of its contract, or after `at` where the source holds a stored dependency; null when it exists everywhere.
        bool? Store(int destination, int source, int root, int at, BoundType? storage)
            => storage is not null ? this.AddStoredBorrowStart(destination, root, at)
                : this.IsStoredBorrow(source, root) ? this.AddStoredBorrowTransfer(source, destination, root, at) : null;

        // The destination takes the source's dependencies at operation `at`.
        void Merge(int destination, int source, int at, BoundType? storage = null)
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
                if (input != LoanRequirement.None && target == LoanRequirement.None && this.IsExclusiveBorrowInput(root) &&
                    NamedOriginRequirement(this.Places[destination].Type, this.Places[root].Type.Origin!) is not LoanRequirement.None and var requirement)
                {
                    this.borrowDependencies[(destination * count) + root] = requirement;
                    target = requirement;
                    changed = true;
                    _ = Store(destination, source, root, at, storage);
                }
                else if (input != LoanRequirement.None && target != LoanRequirement.None && this.HasStoredBorrowRecord(destination, root))
                {
                    changed |= Store(destination, source, root, at, storage) ?? this.SetStoredBorrowEverywhere(destination, root);
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

    // SPEC 15.6.7: the Places that one reserved argument prepares (its Reborrow, and the slot borrow and loaded reference of
    // a stored reference it Reborrows through) activate together and never conflict with one another.
    private bool SameReservedArgument(int reservation, int place)
    {
        var other = this.reservationPlaces[place];
        return other >= 0 && Group(other) == Group(reservation);

        int Group(int id) => this.CallReservations[id].Argument >= 0 ? this.CallReservations[id].Argument : id;
    }

    // An access through the Loan of an acquisition already reported against this holder meets the same overlap again
    // (`let other = value@uniq` while `inner` borrows `value`, then `other@follow = 2` while `inner` lives).
    private bool RestatesRejectedAcquisition(int holder, int access)
    {
        for (var i = 0; this.rejectedAcquisitions is { } rejected && i < rejected.Count; i++)
        {
            if (rejected[i].Holder != holder)
            {
                continue;
            }

            for (var place = 0; place < this.Places.Count; place++)
            {
                if (this.ReceivedBorrow(place, rejected[i].Result) && this.IsBorrowAncestor(access, place))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Whether a reference Place holds the result of a borrow, directly or through the Writes and Moves that transferred it.
    private bool ReceivedBorrow(int place, int result)
    {
        for (var remaining = this.Places.Count; remaining > 0; remaining--)
        {
            if (place == result)
            {
                return true;
            }

            if (!ReferenceTypes.IsBorrow(this.Places[place].Type) || this.borrowDefinitions[place] is not (>= 0 and var definition))
            {
                return false;
            }

            var transferred = this.Operations[definition] switch
            {
                { Kind: OwnershipOperationKind.Write, Input: >= 0 } write => write.Input,
                { Kind: OwnershipOperationKind.Consume, Acquisition: AcquisitionKind.Move, Place: >= 0 } moved => moved.Place,
                _ => -1,
            };
            if (transferred < 0 || transferred == place)
            {
                return false;
            }

            place = transferred;
        }

        return false;
    }

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
                        this.Places[operation.Place] is { Kind: OwnershipPlaceKind.Local, Mutable: false } local &&
                        (ReferenceTypes.IsBorrow(local.Type) || (this.ConditionalReborrows is { Count: > 0 } && Binding.TryPairLayer(local.Type, out _, out _))) => operation.Place,
                    OwnershipOperationKind.AcquirePattern => this.PayloadSubject(operation.Place),
                    OwnershipOperationKind.InitializeSubject => operation.Input,
                    OwnershipOperationKind.Consume when operation.Acquisition == AcquisitionKind.Copy && operation.Place >= 0 &&
                        this.Places[operation.Place].Type.Semantics == SemanticsKind.Ref => operation.Place,
                    OwnershipOperationKind.CallEntry => operation.Place,
                    OwnershipOperationKind.Produce when operation.Place >= 0 => operation.Place,
                    // SPEC 15.6.2: an in-place slot borrow of an owned root, such as the slot of a stored reference
                    // followed through item.0, descends from wherever that root's value came from; so does a Move of it.
                    OwnershipOperationKind.Borrow when node.Kind == OwnershipValueKind.Address && node.Count == 0 && operation.Place >= 0 &&
                        !ReferenceTypes.IsBorrow(this.Places[operation.Place].Type) => operation.Place,
                    OwnershipOperationKind.Consume when operation.Acquisition == AcquisitionKind.Move && operation.Place >= 0 &&
                        !ReferenceTypes.IsBorrow(this.Places[operation.Place].Type) => operation.Place,
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
    // With a Loan root, a holder's or access's reference loaded from a slot of that root is placed at the slot's path:
    // it reaches the root only through that slot. Its referent belongs to other roots, where the path proves nothing.
    private bool IsDisjointProjection(int access, int place, int loanRoot = -1)
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
        var loaded = false;
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
                if (ReferenceEquals(level.Left, root) || ReferenceEquals(ElementAccess.FollowedReference(level.Left), root))
                {
                    break; // The base is the borrowed root, or its referent selected with @follow.
                }
            }

            value = this.ValueOperands[node.Start];
        }

        if (leftRoot < 0)
        {
            leftRoot = this.ProjectionPath(value, left, ref leftDepth, loanRoot >= 0, ref loaded);
        }

        var rightDepth = 0;
        var rightRoot = this.ProjectionPath(this.borrowDefinitions[place], right, ref rightDepth, loanRoot >= 0, ref loaded);
        if (leftRoot < 0 || leftRoot != rightRoot || (loaded && leftRoot != loanRoot))
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
        var loaded = false;
        return this.ProjectionPath(value, selectors, ref depth, false, ref loaded);
    }

    private int ProjectionPath(int value, Span<int> selectors, ref int depth, bool throughLoads, ref bool loaded)
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

                if (throughLoads && this.Values[this.ValueOperands[node.Start]].Kind == OwnershipValueKind.PointerLoad)
                {
                    // A Reborrow through a reference loaded from a slot reaches the slot's root only through that slot.
                    value = this.ValueOperands[node.Start];
                    continue;
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
            else if (throughLoads && operation.Kind == OwnershipOperationKind.Produce && node.Kind == OwnershipValueKind.PointerLoad && node.Count == 1)
            {
                // SPEC 15.6.2: a reference loaded from an inline slot depends on the slot's root only through that slot.
                loaded = true;
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
            if (this.Operations[call].Place >= 0 && this.SoleResultInput(call, this.Places[this.Operations[call].Place].Type) is >= 0 and var sole)
            {
                return sole; // The sole input naming the result's Origins supplies every dependency, including those nested in a generic Item.
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

    // SPEC 15.6.3: a result whose instantiated Origins are all named by one input's Type, such as the
    // `Option<(K, V)>` that `Dictionary.remove` returns from its receiver, descends from that input, because
    // values carrying those Origins reach the result only from it. When another input also names one of them,
    // as `insertOrReplace`'s `value: V` does, the result may come from either and descends from neither.
    private int SoleResultInput(int call, BoundType result)
    {
        var sole = -1;
        for (var entry = call - 1; entry >= 0 && this.Operations[entry] is { Kind: OwnershipOperationKind.CallEntry } input && ReferenceEquals(input.Source, this.Operations[call].Source); entry--)
        {
            if (input.Place < 0)
            {
                return -1;
            }

            var type = this.Places[input.Place].Type;
            if (this.ResultOriginsFromInput(result, type, out var retained) && retained)
            {
                if (sole >= 0)
                {
                    return -1;
                }

                sole = entry;
            }
            else if (NamesResultOrigin(result, type))
            {
                return -1;
            }
        }

        return sole;

        static bool NamesResultOrigin(BoundType type, BoundType input)
        {
            if (type.Origin is { Kind: not OriginKind.Static } origin && NamedOriginRequirement(input, origin) != LoanRequirement.None)
            {
                return true;
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                if (type.OriginArguments[i].Kind != OriginKind.Static && NamedOriginRequirement(input, type.OriginArguments[i]) != LoanRequirement.None)
                {
                    return true;
                }
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                if (NamesResultOrigin(type.Components[i], input))
                {
                    return true;
                }
            }

            return false;
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
