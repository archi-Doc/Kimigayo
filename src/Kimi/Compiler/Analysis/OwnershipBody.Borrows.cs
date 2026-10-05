// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipBody
{
    private readonly List<(int To, int Next)> checkingBorrowEdges = new();

    // SPEC 8.4.10.4: each Place that keeps the result of a generic requirement call, with that result's index.
    private readonly List<(int Holder, int Result)> requirementHolders = new();

    // SPEC 15.6.5: each destroyed root and holder already reported, and the work list of the carrying-definition walk.
    private readonly List<(int Root, int Holder)> destroyedLoans = new();
    private readonly List<int> carryingWork = new();
    private PackedAnalysisTable borrowLive = new(1);
    private int[] checkingBorrowHeads = [];
    private PackedAnalysisTable borrowDependencies = new(2);
    private PackedAnalysisTable retainedBorrowAuthority = new(2);
    private bool[] borrowRootLoss = [];
    private int[] borrowDefinitions = [];

    // Per operation, the carrying definition whose value reaches its input, or -1 (LoanCarryingDefinition).
    private int[] carryingFrom = [];
    private int[] slicePaths = [];
    private bool[] inspectionBorrows = [];
    private bool[] borrowedPlaces = [];
    private bool[] dependencyRoots = [];
    private List<int>? borrowRoots;
    private List<int>? liveBorrowPlaces;

    // SPEC 15.6.1, 15.6.4: Origins of borrowed call arguments that a call result names; a value over one reaches the whole root.
    private List<BoundOrigin>? wholeReferentOrigins;

    // Each acquisition reported against a holder's Loan, with the Place that received the rejected Loan.
    private List<(int Holder, int Result)>? rejectedAcquisitions;

    // How one operation changes what a holder keeps of a root's Loan (LoanCarryingDefinition).
    private enum HolderChange : byte
    {
        None,
        Ended,
        Carrying,
        Unknown,
    }

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
        this.CollectWholeReferentOrigins();
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

        // SPEC 15.6.3 (PLAN G53): a construction payload holds the value placed into it, so it holds that value's Loans and
        // no more, while the payload slot's Type names the Origins of every element of the literal.
        for (var id = 0; id < this.Operations.Count; id++)
        {
            if (this.Operations[id] is { Kind: OwnershipOperationKind.PayloadPlacement, Place: >= 0, Input: >= 0 } placement)
            {
                for (var rootIndex = 0; rootIndex < this.borrowRoots!.Count; rootIndex++)
                {
                    var root = this.borrowRoots[rootIndex];
                    var placed = this.borrowDependencies[(placement.Input * count) + root];
                    if (placed < this.borrowDependencies[(placement.Place * count) + root])
                    {
                        this.borrowDependencies[(placement.Place * count) + root] = placed;
                    }
                }
            }
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
                { Kind: OwnershipOperationKind.PayloadPlacement, Place: >= 0 } placement => placement.Place,
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

        // SPEC 15.6.2, 15.6.5, 16.2.2: the destructions of lent roots are checked first, the reachable ones before those that only
        // checking code reaches (passes 0 and 1), and then every operation in order (pass 2). A destruction's record states the
        // Loan, so a later use of the dangling value, including a join that precedes the destruction in operation order, does
        // not restate it.
        this.destroyedLoans.Clear();
        var operationCount = this.Operations.Count;
        for (var step = 0; step < 3 * operationCount; step++)
        {
            var pass = Math.DivRem(step, operationCount, out var op);
            var destroyed = this.DestroyedRoot(op);
            if (pass < 2 && destroyed < 0)
            {
                continue;
            }

            var reachable = this.IsReachable(op);
            if ((!reachable && !this.HasCheckingState(op)) || (pass < 2 && reachable != (pass == 0)))
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
                    if (!this.borrowLive.IsSet((op * liveWidth) + slot) || (activating && this.SameReservedArgument(r, p)) ||
                        (pass < 2 && this.borrowDependencies[(p * count) + destroyed] == LoanRequirement.None))
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

                        if (pass < 2 && root != destroyed)
                        {
                            continue;
                        }

                        var mode = this.BorrowModeAt(p, root, op, this.borrowDependencies[(p * count) + root]);
                        if (mode == LoanRequirement.None)
                        {
                            continue;
                        }

                        var external = this.Places[root].Kind == OwnershipPlaceKind.Anchor || (this.Places[root].Kind == OwnershipPlaceKind.Parameter && (ReferenceTypes.IsBorrow(this.Places[root].Type) || ReferenceTypes.IsString(this.Places[root].Type) || this.IsPairInput(root))) ||
                            this.IsEnvironmentBorrow(root);
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

                        // SPEC 15.6.2: destroying the borrowed Place itself, checked only by the destruction passes.
                        var destruction = root == destroyed && !rootLost;
                        if (destruction != (pass < 2))
                        {
                            continue;
                        }

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
                                (mode == LoanRequirement.Uniq || access == LoanRequirement.Uniq) && !this.IsBorrowAncestor(receiver, p) &&
                                !this.IsDisjointProjection(accessId, p) && !this.IsDisjointSplitChild(receiver, p))
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

                            var lending = -1;
                            if (destruction)
                            {
                                // SPEC 15.6.5: one record per destroyed root and holder, at a destruction that a definition of the
                                // holder carrying the root's Loan reaches; a reachable destruction is preferred by the pass order.
                                // Where the walk cannot tell which definition reaches (-2), every destruction the Loan reaches keeps
                                // its record, so the escaping one is never deduplicated away.
                                lending = this.LoanCarryingDefinition(p, root, op, count);
                                if (lending == -1 || (lending >= 0 && this.destroyedLoans.Contains((root, p))))
                                {
                                    continue;
                                }

                                if (lending >= 0)
                                {
                                    this.destroyedLoans.Add((root, p));
                                }
                            }

                            reported = true;
                            var holder = p;
                            var reserved = this.HeldReservation(p, op);
                            if (reserved >= 0 && this.RetainingTarget(reserved, root, count) is >= 0 and var target)
                            {
                                // The reserved target's own Loan on another root, not an overlap with the reservation.
                                holder = target;
                                reserved = this.HeldReservation(target, op);
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
                                    if (this.CallReservations[r].Place >= 0)
                                    {
                                        // The activated input's Loan was rejected against this holder, as an acquisition is.
                                        (this.rejectedAcquisitions ??= new()).Add((holder, this.CallReservations[r].Place));
                                    }

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

                            this.ReportIssue(destruction ? this.DestructionIssue(operation.Source, holder, root, lending)
                                : new(operation.Source, OwnershipFailure.ComparisonLoanConflict, Place: holder, LoanSource: this.Places[holder].Source));
                        }
                    }
                }
            }
        }

        this.ReportPreparedLoanConflicts();
        this.VerifyRequirementEffects(count, liveWidth);

        void AddType(int place, BoundType type, LoanRequirement bound = LoanRequirement.Uniq)
        {
            if (type.Kind == BoundTypeKind.Function)
            {
                return; // SPEC 7.6.4: a common Function value holds no Loan; its signature's Origins constrain its calls only.
            }

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
            var rooted = false;
            if (origin.Kind == OriginKind.Intersection)
            {
                for (var i = 0; i < origin.Operands.Count; i++)
                {
                    AddOrigin(place, origin.Operands[i], mode, referent);
                }
            }
            else if (origin.Kind is OriginKind.Projection or OriginKind.Anchor or OriginKind.Input)
            {
                if (origin.Kind == OriginKind.Anchor)
                {
                    // SPEC 5.2.2: the raw Place borrow's own anchor, an external root like a borrowed parameter's referent.
                    if (this.AnchorPlace(origin) is >= 0 and var anchor)
                    {
                        Record(anchor);
                    }

                    RecordCarriers();
                    return;
                }

                if (origin.Kind == OriginKind.Projection)
                {
                    // SPEC 15.6.1 well-formedness: a call result over a borrow of the root may hold what the borrowed referent
                    // holds, so a value over that Origin keeps every Loan of the root, not only those of its own referent.
                    referent &= !this.IsWholeReferentOrigin(origin);
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

                // Only the body's own inputs name its Places; an enclosing function's input reaches a closure body through its
                // environment bindings alone (RecordCarriers).
                var own = origin.Kind != OriginKind.Input || ReferenceEquals(origin.Binder, this.Function) || ReferenceEquals(origin.Binder, this.Function.Accessor?.Declaration);
                foreach (var entry in this.SymbolPlaces)
                {
                    if (own && ReferenceEquals(entry.Key.Declaration, origin.Binder) && (origin.Kind == OriginKind.Input ? entry.Key.Slot == origin.InputIndex : Binding.SymbolOriginSlot(entry.Key) == origin.Slot) &&
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

                if (origin.Kind == OriginKind.Projection && origin.Slot == Binding.ClosureValueSlot)
                {
                    // SPEC 15.8.2: the receiver of a call on a closure literal is that literal's temporary closure value.
                    for (var root = 0; root < count; root++)
                    {
                        if (this.Places[root] is { Kind: OwnershipPlaceKind.Temporary, Type.Kind: BoundTypeKind.Closure } candidate && ReferenceEquals(candidate.Source, origin.Binder))
                        {
                            Record(root);
                        }
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

                RecordCarriers();

                // SPEC 7.6.2, 15.8.2: in a closure body an Origin that names no Place of the body, such as the referent of a captured
                // exclusive reference or an enclosing function's input, is reached only through the environment bindings whose
                // Types carry it. Those bindings are Fields of the closure's receiver, so the Loans fall on their Places.
                void RecordCarriers()
                {
                    if (rooted || this.Function is not { IsAnonymous: true, ClosureStorage: { } plan })
                    {
                        return;
                    }

                    for (var i = 0; i < plan.Storage.Count; i++)
                    {
                        var environment = plan.Storage[i].Environment;
                        if (environment.Type is { } carried && Binding.ContainsOrigin(carried, origin) && this.SymbolPlaces.TryGetValue(environment, out var binding))
                        {
                            Record(binding);
                        }
                    }
                }

                void Record(int root)
                {
                    rooted = true;
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

            if (operation.Kind == OwnershipOperationKind.CompleteConstruction)
            {
                // SPEC 15.6.3, 15.6.7 (PLAN G53): a literal takes its payloads when it completes, so a payload placed
                // earlier holds its Loan while the later elements are evaluated, and a parent acquired exclusively by a
                // later placement meets its Reborrow child placed in the same literal.
                var construction = this.Constructions[this.OperationSteps[id]];
                return place >= construction.PayloadStart && place < construction.PayloadStart + construction.PayloadCount;
            }

            if (operation.Kind == OwnershipOperationKind.Call && operation.Input == place)
            {
                return true; // SPEC 15.6.4: a value call's receiver and its environment's Loans stay protected through the call.
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

            if (ObjectTypes.HandleMode(type) is not null || type.Kind is BoundTypeKind.Closure or BoundTypeKind.Tuple or BoundTypeKind.FixedArray or BoundTypeKind.Array or BoundTypeKind.Dictionary)
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

    // Whether an input Type names any non-static Origin of a result Type.
    private static bool NamesResultOrigin(BoundType type, BoundType input)
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

    // An operation after which the Place holds a new value, or none: its earlier value, and every dependency of that value,
    // ends there. Liveness stops at it, and so does a stored dependency.
    private static bool DefinesBorrowHolder(OwnershipOperation operation, int place)
        => (operation.Place == place && operation.Kind is OwnershipOperationKind.Declare or OwnershipOperationKind.Produce or OwnershipOperationKind.InitializeReceiverField or OwnershipOperationKind.InitializeSubject or OwnershipOperationKind.Write or OwnershipOperationKind.Cleanup or OwnershipOperationKind.CallEntry or OwnershipOperationKind.Deliver or OwnershipOperationKind.StorePointer or OwnershipOperationKind.PayloadPlacement) ||
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
            if (ReferenceEquals(receiver, KotoHelper.UnwrapParentheses(root)) || ReferenceEquals(ElementAccess.FollowedReference(receiver), root))
            {
                return true; // The base is the root, or the root's referent selected with @follow.
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
    // SPEC 7.6.2, 15.8.2: an environment binding that stores a reference is, like a borrowed parameter, a root whose referent lies
    // outside the closure body; the body reaches that referent only through the binding.
    private bool IsEnvironmentBorrow(int place)
        => this.Places[place].Kind == OwnershipPlaceKind.Local && this.Function.IsAnonymous && ReferenceEquals(this.Places[place].Source, this.Function) &&
            ReferenceTypes.IsBorrow(this.Places[place].Type);

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

    // SPEC 15.6.2, 16.2: the owned root that a cleanup destroys while some Place depends on it, or -1. A root that holds a
    // reference ends a reference, not storage, and keeps the operation-order check.
    private int DestroyedRoot(int operation)
        => this.Operations[operation] is { Kind: OwnershipOperationKind.Cleanup, Place: >= 0 and var place } && place < this.Places.Count && this.dependencyRoots[place] &&
            !ReferenceTypes.IsBorrow(this.Places[place].Type) && !ObjectTypes.IsBorrow(this.Places[place].Type) ? place : -1;

    // SPEC 15.6.5: a definition of `holder` whose value may keep the Loan of `root` and reaches the input of `destruction` with no
    // other definition of the holder between them: -1 when none does, -2 when the holder may change in a way this walk does not
    // follow, which keeps the destruction a conflict. A holder's Type names the Origins of every value it holds over the body, so
    // only the flow distinguishes the definition that keeps the root's Loan from one that keeps another source's, as the two
    // exits of `label block: do` that deliver `n@ref` and `z@ref`.
    private int LoanCarryingDefinition(int holder, int root, int destruction, int count)
    {
        if (this.Places[holder].Kind is not (OwnershipPlaceKind.Local or OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result) ||
            this.HasExplicitDependency(holder) || this.HasStoredBorrowRecord(holder, root))
        {
            return -2;
        }

        var operations = this.Operations.Count;
        var work = this.carryingWork;
        work.Clear();
        for (var id = 0; id < operations; id++)
        {
            var change = this.HolderChangeAt(id, holder, root, count);
            if (change == HolderChange.Unknown)
            {
                return -2;
            }

            if (change == HolderChange.Carrying)
            {
                work.Add(id);
            }
        }

        // Liveness follows the same runtime and checking links (VerifyBorrows); the holder keeps a seed's value up to its next
        // definition, which the walk does not pass.
        Grow(ref this.carryingFrom, operations);
        var from = this.carryingFrom.AsSpan(0, operations);
        from.Fill(-1);
        var seeds = work.Count;
        for (var s = 0; s < seeds; s++)
        {
            var seed = work[s];
            work.Add(seed);
            while (work.Count > seeds)
            {
                var at = work[^1];
                work.RemoveAt(work.Count - 1);
                var runtime = this.EdgeHeads[at];
                var checking = this.checkingBorrowHeads[at];
                while (runtime >= 0 || checking >= 0)
                {
                    int next;
                    if (runtime >= 0)
                    {
                        var edge = this.Edges[runtime];
                        runtime = edge.Next;
                        if (edge.Kind == OwnershipEdgeKind.Abort)
                        {
                            continue;
                        }

                        next = edge.To;
                    }
                    else
                    {
                        next = this.checkingBorrowEdges[checking].To;
                        checking = this.checkingBorrowEdges[checking].Next;
                    }

                    if (from[next] >= 0)
                    {
                        continue;
                    }

                    from[next] = seed;
                    if (next == destruction)
                    {
                        return seed;
                    }

                    if (!DefinesBorrowHolder(this.Operations[next], holder))
                    {
                        work.Add(next);
                    }
                }
            }
        }

        return -1;
    }

    // SPEC 8.9, 8.4.10.4: whether the explicit dependencies of a generic body reach `holder` other than through the definitions the
    // carrying walk classifies: a conditional reborrow's Place, a holder of a requirement result, or a Place with an abstract part,
    // which inherits through payloads and decompositions too (AddExplicitDependencies).
    private bool HasExplicitDependency(int holder)
    {
        var conditional = this.ConditionalReborrows;
        if (conditional is not { Count: > 0 } && this.RequirementResults is not { Count: > 0 })
        {
            return false;
        }

        if (AbstractTypes.HasAbstractPart(this.Places[holder].Type))
        {
            return true;
        }

        for (var i = 0; conditional is not null && i < conditional.Count; i++)
        {
            if (conditional[i].Place == holder)
            {
                return true;
            }
        }

        for (var i = 0; i < this.requirementHolders.Count; i++)
        {
            if (this.requirementHolders[i].Holder == holder)
            {
                return true;
            }
        }

        return false;
    }

    // How operation `id` changes what `holder` keeps of the Loan of `root`. Every form outside the listed reads and complete
    // definitions is Unknown.
    private HolderChange HolderChangeAt(int id, int holder, int root, int count)
    {
        var operation = this.Operations[id];
        if ((operation.Kind == OwnershipOperationKind.UpdateBorrowed && this.Values[id].Constant == holder) ||
            (operation.Kind == OwnershipOperationKind.StoreDictionaryEntry && this.OperationSteps[id] == holder) ||
            (operation.Projection >= 0 && this.Projections[operation.Projection].Root == holder) ||
            (this.Values[id] is { Kind: OwnershipValueKind.Sequence, Constant: var sequence } && this.Sequences[(int)sequence].Receiver == holder))
        {
            return HolderChange.Unknown;
        }

        var placed = operation.Place == holder;
        if (!placed && operation.Input != holder)
        {
            return HolderChange.None;
        }

        return operation.Kind switch
        {
            OwnershipOperationKind.Declare or OwnershipOperationKind.Cleanup or OwnershipOperationKind.Deliver or OwnershipOperationKind.CallEntry when placed => HolderChange.Ended,
            OwnershipOperationKind.Write when placed => Carries(operation.Input),
            OwnershipOperationKind.Consume or OwnershipOperationKind.Borrow when !placed => Carries(operation.Place),
            OwnershipOperationKind.Consume => operation.Acquisition == AcquisitionKind.Move ? HolderChange.Ended : HolderChange.None,
            OwnershipOperationKind.Borrow or OwnershipOperationKind.Read => operation.Reservation < 0 && operation.LoanMode != LoanRequirement.Uniq ? HolderChange.None : HolderChange.Unknown,

            // A produced value, a call result and a join are taken to keep the Loan; reading the holder changes nothing.
            OwnershipOperationKind.Produce or OwnershipOperationKind.Call or OwnershipOperationKind.Branch when placed => HolderChange.Carrying,
            OwnershipOperationKind.Write or OwnershipOperationKind.Call => HolderChange.None,
            _ => HolderChange.Unknown,
        };

        // A value without a source Place is not followed.
        HolderChange Carries(int source)
            => source < 0 ? HolderChange.Unknown
            : source == root || this.borrowDependencies[(source * count) + root] != LoanRequirement.None ? HolderChange.Carrying : HolderChange.Ended;
    }

    // The Borrow of `root` whose Loan the value defined at `definition` keeps, or -1: an explicit or implicit Borrow of the root, or
    // an element borrowed through a sequence of the root (`n[0]@ref`). The walk follows the value's operands, the one definition
    // of a Place it reads (`let q = n@ref`, a nested block's value, a pattern binding), a call result to the input its Origins
    // name (ResultArgument) and a construction to its payloads (`(n@ref, 1)`); a Place with several definitions ends the walk.
    private int LendingBorrow(int definition, int root)
    {
        Span<int> pending = stackalloc int[32];
        var depth = 0;
        pending[depth++] = definition;
        for (var steps = 0; depth > 0 && steps < 256; steps++)
        {
            var id = pending[--depth];
            if ((uint)id >= (uint)this.Values.Count)
            {
                continue;
            }

            var operation = this.Operations[id];
            var value = this.Values[id];
            if ((operation.Kind == OwnershipOperationKind.Borrow && operation.Place == root) ||
                (value.Kind == OwnershipValueKind.Sequence && this.Sequences[(int)value.Constant].Receiver == root))
            {
                return id;
            }

            if (operation.Kind == OwnershipOperationKind.Call)
            {
                Push(pending, ref depth, this.ResultArgument(id));
                continue;
            }

            var followed = false;
            for (var i = value.Count - 1; i >= 0; i--)
            {
                var operand = value.Kind == OwnershipValueKind.Phi ? this.PhiInputs[value.Start + i].Value : this.ValueOperands[value.Start + i];
                followed |= operand >= 0;
                Push(pending, ref depth, operand);
            }

            // A value without operands continues at the Place it reads or stores, as the borrow ancestry does (IsBorrowAncestor).
            var stored = followed ? -1 : operation.Kind switch
            {
                OwnershipOperationKind.Read or OwnershipOperationKind.Consume or OwnershipOperationKind.Produce or OwnershipOperationKind.CallEntry => operation.Place,
                OwnershipOperationKind.Borrow when operation.Place >= 0 && !ReferenceTypes.IsBorrow(this.Places[operation.Place].Type) => operation.Place,
                OwnershipOperationKind.Write or OwnershipOperationKind.PayloadPlacement or OwnershipOperationKind.InitializeSubject => operation.Input,
                OwnershipOperationKind.AcquirePattern => this.PayloadSubject(operation.Place),
                _ => -1,
            };
            if ((uint)stored >= (uint)this.Places.Count)
            {
                continue;
            }

            if (this.borrowDefinitions[stored] is >= 0 and var only && only != id)
            {
                Push(pending, ref depth, only);
                continue;
            }

            for (var i = 0; i < this.ConstructionStorage.Count; i++)
            {
                var plan = this.ConstructionStorage[i];
                if (plan.Place != stored)
                {
                    continue;
                }

                // The payload placements lie between the construction's Declare and its use.
                for (var at = id - 1; at >= 0 && !(this.Operations[at].Kind == OwnershipOperationKind.Declare && this.Operations[at].Place == stored); at--)
                {
                    var placement = this.Operations[at];
                    if (placement.Kind == OwnershipOperationKind.PayloadPlacement && placement.Place >= plan.PayloadStart && placement.Place < plan.PayloadStart + plan.PayloadCount)
                    {
                        Push(pending, ref depth, at);
                    }
                }
            }

            Push(pending, ref depth, this.ProducingValue(stored, id));
        }

        return -1;

        static void Push(Span<int> pending, ref int depth, int operation)
        {
            if (operation >= 0 && depth < pending.Length)
            {
                pending[depth++] = operation;
            }
        }
    }

    // SPEC 15.6.2, 16.2.2: the record of destroying `root` while `holder` keeps its Loan, with the Borrow that created the Loan: a
    // conversion such as `n@ref`, an implicit Borrow at its operand, or a capture entry such as `[n@ref]` of a closure.
    private OwnershipIssue DestructionIssue(Koto source, int holder, int root, int lending)
    {
        Koto? borrowed = null;
        var capture = -1;
        if (lending >= 0 && this.LendingBorrow(lending, root) is >= 0 and var borrow)
        {
            borrowed = this.Operations[borrow].Source;
            if (borrowed is FunctionKoto { BoundClosure: { } closure, Captures: { } entries })
            {
                for (var i = 0; i < closure.Captures.Count && capture < 0; i++)
                {
                    if (this.SymbolPlaces.TryGetValue(closure.Captures[i].Source, out var captured) && captured == root)
                    {
                        for (var entry = 0; entry < entries.Length && capture < 0; entry++)
                        {
                            capture = entries[entry].Name == closure.Captures[i].Source.Name ? entry : -1;
                        }
                    }
                }
            }
            else if (borrowed.Parent is ConversionKoto { ConversionBinding: ConversionBinding.Borrow } conversion)
            {
                borrowed = conversion;
            }
        }

        string? name = null;
        foreach (var entry in this.SymbolPlaces)
        {
            if (entry.Value == root)
            {
                name = entry.Key.Name;
                break;
            }
        }

        var temporary = name is null && this.Places[root].Kind == OwnershipPlaceKind.Temporary ? this.Places[root].Source : null;
        var loan = this.Places[holder].Source;
        return new(source, OwnershipFailure.ComparisonLoanConflict, Place: holder, LoanSource: loan, Borrow: borrowed, BorrowCapture: capture, Destroyed: name ?? string.Empty, DestroyedTemporary: temporary);
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
                if (this.HoldsSameReference(place, rejected[i].Result) && this.IsBorrowAncestor(access, place))
                {
                    return true;
                }
            }
        }

        // The opposite direction: an access through the earlier holder meets a Place that holds the rejected Loan, directly or
        // as a placed part (`let q = (a@uniq, 3)` rejected while `p` holds `a`, then `p.0@follow = 5` while `q` lives).
        for (var i = 0; this.rejectedAcquisitions is { } rejected && i < rejected.Count; i++)
        {
            if (this.IsBorrowAncestor(access, rejected[i].Holder) && this.HoldsRejectedLoan(holder, rejected[i].Result))
            {
                return true;
            }
        }

        return false;
    }

    // Whether an aggregate built by a construction placed a payload that descends from a borrow result.
    private bool ConstructedFrom(int aggregate, int result)
    {
        for (var i = 0; i < this.Constructions.Count; i++)
        {
            var plan = this.Constructions[i];
            if (plan.Place != aggregate)
            {
                continue;
            }

            for (var op = 0; op < this.Operations.Count; op++)
            {
                if (this.Operations[op] is { Kind: OwnershipOperationKind.PayloadPlacement } placement &&
                    placement.Place >= plan.PayloadStart && placement.Place < plan.PayloadStart + plan.PayloadCount &&
                    (placement.Input == result || this.IsBorrowAncestor(op, result)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Whether a Place's stored value was built from the result of a rejected acquisition: its defining Write's input descends
    // from that result.
    private bool HoldsRejectedLoan(int holder, int result)
    {
        for (var op = 0; op < this.Operations.Count; op++)
        {
            if (this.Operations[op] is { Kind: OwnershipOperationKind.Write, Input: >= 0 } write && write.Place == holder)
            {
                return this.IsBorrowAncestor(op, result) || this.ConstructedFrom(write.Input, result);
            }
        }

        return false;
    }

    // SPEC 3.5, 15.6.3: moving a reference changes its slot, not the capability's identity. The slot a reference Place
    // received its one definition from (a Write, a Move or a placement), or -1 at a Borrow, which creates a distinct child
    // Loan, at a Place defined more than once, or at an owned Place.
    private int TransferSource(int place)
    {
        if (!ReferenceTypes.IsBorrow(this.Places[place].Type) || this.borrowDefinitions[place] is not (>= 0 and var definition))
        {
            return -1;
        }

        var transferred = this.Operations[definition] switch
        {
            { Kind: OwnershipOperationKind.Write, Input: >= 0 } write => write.Input,
            { Kind: OwnershipOperationKind.Consume, Acquisition: AcquisitionKind.Move, Place: >= 0 } moved => moved.Place,
            { Kind: OwnershipOperationKind.PayloadPlacement, Input: >= 0 } placement => placement.Input,
            _ => -1,
        };
        return transferred == place ? -1 : transferred;
    }

    // Whether `holder` is `slot` or received its reference from `slot`, such as the result of a borrow, through the Writes,
    // Moves and placements that transferred it (TransferSource).
    private bool HoldsSameReference(int holder, int slot)
    {
        for (var remaining = this.Places.Count; remaining > 0 && holder >= 0; remaining--)
        {
            if (holder == slot)
            {
                return true;
            }

            holder = this.TransferSource(holder);
        }

        return false;
    }

    private bool IsBorrowAncestor(int value, int place)
    {
        var original = place;
        // Moving a reference changes its slot, not the capability's identity. A child formed before the Move
        // still descends from that same parent, whichever slot of the transfer chain the child's value names; stop
        // at a Borrow, which does create a distinct child Loan.
        for (var remaining = this.Places.Count; remaining > 0 && this.TransferSource(place) is >= 0 and var transferred; remaining--)
        {
            place = transferred;
        }

        // Follow the actual value's reborrow chain; equal Origin names alone do
        // not authorize use of a parent while a sibling/child Loan is required.
        for (var remaining = this.Values.Count; remaining > 0 && (uint)value < (uint)this.Values.Count; remaining--)
        {
            var operation = this.Operations[value];
            if (this.HoldsSameReference(original, ValuePlaceForBorrow(operation)) ||
                (operation.Kind is OwnershipOperationKind.Read or OwnershipOperationKind.Consume && this.HoldsSameReference(original, operation.Place)))
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

            if (node.Kind == OwnershipValueKind.Element && operation.Projection >= 0)
            {
                value = this.ProjectedBorrowValue(operation.Projection, value);
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
                    // followed through item.0, descends from wherever that root's value came from.
                    OwnershipOperationKind.Borrow when node.Kind == OwnershipValueKind.Address && node.Count == 0 && operation.Place >= 0 &&
                        !ReferenceTypes.IsBorrow(this.Places[operation.Place].Type) => operation.Place,
                    // SPEC 3.5, 15.6.2, 15.6.3: a Move continues at the moved Place: of an owned root, like its slot borrow above;
                    // of a reference, whose value it transfers as is, so a Reborrow child moved into a reserved argument or a
                    // placed element descends from the reference's definition exactly as the holder walk above follows a
                    // holder that received a Move, and the parent it was Reborrowed from is its ancestor, not a sibling Loan.
                    OwnershipOperationKind.Consume when operation.Acquisition == AcquisitionKind.Move && operation.Place >= 0 => operation.Place,
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

            if (operation.Kind == OwnershipOperationKind.Write && operation.Input >= 0 && this.ValueOperands[node.Start] < 0)
            {
                // SPEC 15.6.3: a literal's value is its placements; the written aggregate, read back as a whole slot, descends
                // from the sources its payloads were placed from, such as the Reborrow child moved into one of them.
                return this.ConstructedFrom(operation.Input, original);
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

    // Recover a stored reference's actual input through an immutable inline path. A construction selects one
    // payload; a call must publish one source for its whole result. Equal Origin names never select siblings.
    private int ProjectedBorrowValue(int projection, int before)
    {
        var path = this.Projections[projection];
        if (path.Path != projection)
        {
            return -1;
        }

        var root = path.Root;
        var depth = 0;
        for (var id = before - 1; id >= 0; id--)
        {
            if (this.Places[root] is not { Kind: OwnershipPlaceKind.Temporary } and not { Kind: OwnershipPlaceKind.Local, Mutable: false })
            {
                return -1;
            }

            var operation = this.Operations[id];
            if (operation.Kind == OwnershipOperationKind.Call && operation.Place == root)
            {
                return this.ResultArgument(id);
            }

            if (operation.Kind == OwnershipOperationKind.Write && operation.Place == root)
            {
                if (this.borrowDefinitions[root] != id || operation.Input < 0)
                {
                    return -1;
                }

                root = operation.Input;
            }
            else if (operation.Kind == OwnershipOperationKind.Consume && operation.Input == root && operation.Place >= 0)
            {
                root = operation.Place;
            }
            else if (operation.Kind == OwnershipOperationKind.CompleteConstruction && operation.Place == root)
            {
                var selected = projection;
                for (var remaining = path.PathDepth - ++depth; remaining > 0; remaining--)
                {
                    selected = this.Projections[selected].Parent;
                }

                var payload = -1;
                for (var i = 0; i < this.Constructions.Count; i++)
                {
                    var construction = this.Constructions[i];
                    if (construction.Place == root && (uint)this.Projections[selected].Selector < (uint)construction.PayloadCount)
                    {
                        payload = construction.PayloadStart + this.Projections[selected].Selector;
                        break;
                    }
                }

                for (id--; id >= 0; id--)
                {
                    if (this.Operations[id] is { Kind: OwnershipOperationKind.PayloadPlacement } placement && placement.Place == payload)
                    {
                        if (depth == path.PathDepth)
                        {
                            return id;
                        }

                        root = placement.Input;
                        break;
                    }
                }
            }
        }

        return -1;
    }

    private bool HasSingleBorrowDefinition(int place)
        => this.borrowDefinitions[place] >= 0 &&
        (this.Places[place].Kind == OwnershipPlaceKind.Temporary || this.Places[place] is { Kind: OwnershipPlaceKind.Local, Mutable: false });

    // SPEC 15.6.3: simultaneously held exclusive references in distinct owned Tuple payloads are independent
    // capabilities. Reborrowing a child preserves that split; borrowing a shared reference's stored target does not.
    // These are reference identities, not a claim that different reference slots imply disjoint shared referents.
    private bool IsDisjointSplitChild(int value, int holder)
    {
        if (!this.HasSingleBorrowDefinition(holder) || this.SplitChild(value) is not (>= 0 and var left) ||
            this.SplitChild(this.borrowDefinitions[holder]) is not (>= 0 and var right) || left == right)
        {
            return false;
        }

        for (var i = 0; i < this.Decompositions.Count; i++)
        {
            var split = this.Decompositions[i];
            if (this.Places[split.Place].Type.Kind == BoundTypeKind.Tuple &&
                left >= split.PayloadStart && left < split.PayloadStart + split.PayloadCount &&
                right >= split.PayloadStart && right < split.PayloadStart + split.PayloadCount)
            {
                return true;
            }
        }

        return false;
    }

    private int SplitChild(int value)
    {
        for (var remaining = this.Values.Count; remaining > 0 && (uint)value < (uint)this.Values.Count; remaining--)
        {
            var operation = this.Operations[value];
            var node = this.Values[value];
            if (operation.Kind == OwnershipOperationKind.Call)
            {
                value = this.ResultArgument(value); // Only a published result contract naming one input preserves ancestry.
                continue;
            }

            if (operation.Kind == OwnershipOperationKind.AcquirePattern && node.Kind == OwnershipValueKind.None && operation.Place >= 0 &&
                this.Places[operation.Place] is { Kind: OwnershipPlaceKind.Payload, Type.Semantics: SemanticsKind.Uniq })
            {
                return operation.Place;
            }

            if (node.Kind is OwnershipValueKind.Alias or OwnershipValueKind.Address && node.Count == 1)
            {
                value = this.ValueOperands[node.Start];
                continue;
            }

            if (node.Kind is OwnershipValueKind.PointerLoad or OwnershipValueKind.BorrowedField && node.Count == 1 &&
                ValuePlaceForBorrow(operation) is >= 0 and var loaded &&
                (this.Places[loaded].Type.Semantics == SemanticsKind.Uniq ||
                    (node.Kind == OwnershipValueKind.PointerLoad && ValuePlaceForBorrow(this.Operations[this.ValueOperands[node.Start]]) is >= 0 and var pointer &&
                        this.Places[pointer].Type is { Kind: BoundTypeKind.Semantics, Components: [{ Semantics: SemanticsKind.Uniq }] })))
            {
                // A shared read can adapt a stored uniq reference to ref; the stored capability still proves independence.
                value = this.ValueOperands[node.Start];
                continue;
            }

            var source = operation.Kind switch
            {
                OwnershipOperationKind.Read or OwnershipOperationKind.Consume or OwnershipOperationKind.CallEntry or OwnershipOperationKind.Produce => operation.Place,
                OwnershipOperationKind.Write => operation.Input,
                _ => -1,
            };
            if (source < 0)
            {
                return -1;
            }

            var definition = this.HasSingleBorrowDefinition(source) ? this.borrowDefinitions[source]
                : this.Places[source].Kind == OwnershipPlaceKind.Temporary ? this.ProducingValue(source, value) : -1;
            if (definition < 0 || definition >= value)
            {
                return -1;
            }

            value = definition;
        }

        return -1;
    }

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
            (ReferenceEquals(candidate.Type, BoundType.String) || StructStorage.IsStruct(candidate.Type) || EnumStorage.IsEnum(candidate.Type) || candidate.Type.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Tuple or BoundTypeKind.Array or BoundTypeKind.Dictionary or BoundTypeKind.Closure || ScalarTypes.Supports(candidate.Type)) &&
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

    // SPEC 15.6.1, 15.6.4 steps 4-5: a call result that names the Origin of a borrowed argument whose referent carries Origins, such
    // as `h.get()` returning `ref/i32 during self` while `h` holds `ref/i32 during a`, may reach every reference that referent holds.
    private void CollectWholeReferentOrigins()
    {
        this.wholeReferentOrigins?.Clear();
        for (var call = 0; call < this.Operations.Count; call++)
        {
            if (this.Operations[call] is not { Kind: OwnershipOperationKind.Call, Place: >= 0 } operation || operation.Place >= this.Places.Count)
            {
                continue;
            }

            var result = this.Places[operation.Place].Type;
            for (var entry = call - 1; entry >= 0 && this.Operations[entry] is { Kind: OwnershipOperationKind.CallEntry } input && ReferenceEquals(input.Source, operation.Source); entry--)
            {
                if (input.Place >= 0 && this.Places[input.Place].Type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1, Origin: { Kind: OriginKind.Projection } origin } argument &&
                    HasProjection(argument.Components[0]) && Binding.ContainsOrigin(result, origin) && !this.IsWholeReferentOrigin(origin))
                {
                    (this.wholeReferentOrigins ??= new()).Add(origin);
                }
            }
        }
    }

    private bool IsWholeReferentOrigin(BoundOrigin origin)
    {
        for (var i = 0; i < (this.wholeReferentOrigins?.Count ?? 0); i++)
        {
            if (ReferenceEquals(this.wholeReferentOrigins![i], origin))
            {
                return true;
            }
        }

        return false;
    }

    // The read of a value call's receiver Place when the call's result depends on that Place.
    private int ReceiverRead(int call, Koto receiver, int result)
    {
        var count = this.Places.Count;
        for (var id = call - 1; id >= 0; id--)
        {
            if (this.Operations[id] is { Kind: OwnershipOperationKind.Read, Place: >= 0 } read && ReferenceEquals(read.Source, receiver))
            {
                return this.borrowDependencies[(result * count) + read.Place] != LoanRequirement.None ? id : -1;
            }
        }

        return -1;
    }

    private int ResultArgument(int call)
    {
        if (this.Operations[call].Source is InvocationKoto { BoundValueCall: { } valueCall })
        {
            // SPEC 15.6.3: a value call's result descends from the one argument whose Origins it names, as an ordinary call's; a result
            // bound to the call receiver (SPEC 15.8.2) descends from the receiver's read, as a method result from its receiver entry.
            if (this.Operations[call].Place < 0)
            {
                return -1;
            }

            var result = this.Operations[call].Place;
            return this.SoleResultInput(call, this.Places[result].Type) is >= 0 and var sole ? sole : this.ReceiverRead(call, valueCall.Receiver, result);
        }

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
            // `Option<uniq/T during source>`, keeps Loans the receiver's value holds, so it descends from the receiver when no
            // other input's Type names them; otherwise it may come from that input too.
            return plan.Receiver is not null && target.ReturnType?.BoundType is { } result && target.BoundSymbol?.Scope.Owner is DeclarationContainerKoto owner &&
                NamesOnlyReceiverOrigins(result, owner, out var named) && named && this.ReceiverEntry(call) is >= 0 and var receiver &&
                !this.OtherInputNamesResult(call, receiver) ? receiver : -1;
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
    }

    // Whether an input of the call other than its receiver entry names an Origin of the call's result.
    private bool OtherInputNamesResult(int call, int receiver)
    {
        if (this.Operations[call].Place < 0)
        {
            return false;
        }

        var result = this.Places[this.Operations[call].Place].Type;
        for (var entry = call - 1; entry > receiver; entry--)
        {
            var input = this.Operations[entry];
            if (input.Place < 0 || NamesResultOrigin(result, this.Places[input.Place].Type))
            {
                return true;
            }
        }

        return false;
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
