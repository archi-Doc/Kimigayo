// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    // One verdict per Binding obligation in order, and the Binding state version they were computed from; -1 before analysis.
    private readonly List<ObligationVerdict> obligationVerdicts = new();
    private int obligationVerdictVersion = -1;

    // Whether every Binding obligation is discharged, from the verdicts of this analysis (PLAN G74 U4); false before analysis.
    internal bool SupportsOriginObligations()
    {
        if (!this.CurrentVerdicts())
        {
            return false;
        }

        for (var i = 0; i < this.obligationVerdicts.Count; i++)
        {
            if (this.obligationVerdicts[i].Unchecked)
            {
                return false;
            }
        }

        return true;
    }

    // Whether the verdicts are computed for the current Binding. Binding invalidates this analysis whenever it rebinds, so verdicts
    // whose obligation or inference state has changed since were computed from other inputs: an internal invariant, never a reason
    // to judge again.
    private bool CurrentVerdicts()
    {
        if (this.obligationVerdictVersion < 0)
        {
            return false;
        }

        var binding = this.compilation.Binding;
        if (this.obligationVerdictVersion != binding.OriginStateVersion || this.obligationVerdicts.Count != binding.Obligations.Count)
        {
            throw new InvalidOperationException("Binding's obligations or inference state changed after their verdicts were computed (PLAN G74 U4).");
        }

        return true;
    }

    // SPEC 15.6.1: every obligation receives one verdict once Binding is final (PLAN G74 U4), and every Origin obligation that
    // remains unchecked is its own record, reported without adding constraints. A failed Origin relation leaves the Loan, destruction
    // and result checks to proceed without it; any other unchecked obligation, such as an unsolved inference, stops the analysis.
    private bool ReportUnprovenOriginObligations()
    {
        var binding = this.compilation.Binding;
        var obligations = binding.Obligations;
        this.obligationVerdicts.Clear();
        var stop = false;
        for (var i = 0; i < obligations.Count; i++)
        {
            var verdict = this.JudgeObligation(obligations[i]);
            this.obligationVerdicts.Add(verdict);
            if (verdict.Unchecked)
            {
                stop |= this.ReportJudgedOriginObligation(obligations[i], verdict.Judgment, verdict.Reversed, false);
            }
        }

        this.obligationVerdictVersion = binding.OriginStateVersion;
        return stop;
    }

    // The verdict of one obligation, by the check that owns it: Binding's length verification, call-site borrow formation for a
    // borrowed input's well-formedness, or the proof and region judgment of SPEC 15.6.5.
    private ObligationVerdict JudgeObligation(in BindingObligation obligation)
    {
        var binding = this.compilation.Binding;
        binding.OriginProofMetrics?.Verdict();
        if (binding.IsVerifiedLengthObligation(obligation))
        {
            return default; // Definition conditions and each call's substituted lengths were checked by Binding.
        }

        binding.CompareDischarge(obligation);
        if (obligation.Discharge == OriginDischarge.CallBorrows || binding.IsVerifiedOriginObligation(obligation))
        {
            return default;
        }

        if (obligation is not { Kind: BindingObligationKind.OriginOutlives, Longer: not null, Shorter: not null })
        {
            return new(true, default, false);
        }

        var judgment = binding.JudgeOriginObligation(obligation, out var reversed);
        return judgment == OriginJudgment.Proven ? default : new(true, judgment, reversed);
    }

    // SPEC 15.3.6, 15.6.1 Identity: a meet at the longer end of an `outlives` relation outlives an Origin exactly when each operand
    // does, so each failing operand of a judged relation is its own chain and record, and a proven operand adds none; an `==` stays
    // whole. True when the analysis must stop.
    private bool ReportUnprovenOriginObligation(in BindingObligation obligation, bool operand)
    {
        var reversed = false;
        var judgment = obligation is { Kind: BindingObligationKind.OriginOutlives, Longer: not null, Shorter: not null }
            ? this.compilation.Binding.JudgeOriginObligation(obligation, out reversed) : default;
        return this.ReportJudgedOriginObligation(obligation, judgment, reversed, operand);
    }

    private bool ReportJudgedOriginObligation(in BindingObligation obligation, OriginJudgment judgment, bool reversed, bool operand)
    {
        if (obligation is { Kind: BindingObligationKind.OriginOutlives, Longer: { } longer, Shorter: not null, Use: var use })
        {
            if (operand && judgment == OriginJudgment.Proven)
            {
                return false;
            }

            if (judgment == OriginJudgment.Unrepresentable)
            {
                // A chain between two body Origins needs region inference over Loan edges; it is a located limit, not a proof.
                var at = use is VariableKoto { InitializerKoto: { } initializer } && Binding.IsFitObligation(obligation) ? initializer : use;
                this.issues.Add(new(at, OwnershipFailure.Unsupported));
                return false;
            }

            if (reversed && Binding.IsLocalRegion(obligation.Shorter))
            {
                return this.ReportUnprovenOriginObligation(obligation with { Longer = obligation.Shorter, Shorter = longer, Equality = false }, true);
            }

            if (Binding.IsLocalRegion(longer))
            {
                foreach (var source in this.compilation.Binding.LocalRegionSources(longer))
                {
                    this.ReportUnprovenOriginObligation(obligation with { Longer = source, Equality = false }, true);
                }

                return false;
            }

            if (!obligation.Equality && longer.Kind == OriginKind.Intersection)
            {
                var count = this.issues.Count;
                for (var i = 0; i < longer.Operands.Count; i++)
                {
                    this.ReportUnprovenOriginObligation(obligation with { Longer = longer.Operands[i] }, true);
                }

                if (this.issues.Count != count)
                {
                    return false;
                }
            }

            // An `==` that only its reverse direction fails is shown in that direction, which also decides whether it is Refuted.
            if (reversed)
            {
                this.issues.Add(new(use, OwnershipFailure.UnprovenOrigin, Obligation: obligation with { Longer = obligation.Shorter, Shorter = longer }));
                return false;
            }
        }

        this.issues.Add(new(obligation.Use, OwnershipFailure.UnprovenOrigin, Obligation: obligation));
        return obligation is not { Kind: BindingObligationKind.OriginOutlives, Longer: not null, Shorter: not null };
    }

    // SPEC 3.4: each branch below is one Place route (raw, stored reference, published Place, Reborrow, object payload, sequence
    // element, borrowed path, owned static path), chosen from the selection's form; designator outcomes are alternatives, never
    // fallback stages. Only the final Value route evaluates its source, and a Place selection that no route reached fails closed
    // there (PLAN G59 U1). An address-producing use (`@raw`, an address adaptation) observes the borrowed address itself.
    private int BorrowStruct(Koto source, BoundType type, int reservation = -1)
    {
        var unwrapped = this.SelectedPlace(KotoHelper.UnwrapParentheses(source));
        if (ElementAccess.IsRawPlace(unwrapped))
        {
            // Like a selected element, a reserved argument Reborrows the converted reference, so its reservation and
            // activation refer to a real exclusive borrow (SPEC 15.6.7).
            var raw = this.BorrowRawPlace(source, unwrapped, type);
            return raw >= 0 && reservation >= 0 ? this.BorrowThrough(source, raw, type, reservation) : raw;
        }

        if (this.ImplicitlyFollowsReference(unwrapped, type))
        {
            return this.BorrowStoredReference(unwrapped, unwrapped, type, reservation); // SPEC 7.3: a receiver through a pair layer.
        }

        if (this.Substituting && this.compilation.Binding.ImplicitPairAdmitted(unwrapped) != SemanticsMask.None &&
            this.ReferenceLayers(unwrapped.BoundType, type.Components[0]) > 1)
        {
            // SPEC 3.4.1, 10.2, 13.5.5.1: several existing layers yield one reference in the receiver's mode (exclusive only
            // through exclusive layers, which Binding checked), which the receiver borrows through.
            var reference = this.ReadReference(unwrapped, type);
            return reference < 0 ? -1 : this.BorrowThrough(unwrapped, reference, type, reservation);
        }

        if ((ElementAccess.IsUserIndex(unwrapped) || ElementAccess.IsPlaceCall(unwrapped)) && ReferenceTypes.IsBorrow(unwrapped.BoundType) &&
            ReferenceEquals(unwrapped.BoundType!.Components[0], type.Components[0]))
        {
            return this.BorrowStoredReference(unwrapped, unwrapped, type, reservation);
        }

        if (unwrapped is IndexKoto userIndex && this.compilation.Binding.IndexerCall(userIndex, type.Semantics == SemanticsKind.Uniq) is { } indexer)
        {
            return this.BorrowStruct(indexer, type, reservation); // SPEC 4.6.9: a borrow of receiver[key] selects index or indexUniq.
        }

        if (unwrapped is InvocationKoto placeCall && ElementAccess.IsPlaceCall(placeCall))
        {
            // SPEC 7.1.1: a borrow of a published Place is a Reborrow through the reference the call returns; the call is
            // evaluated as that reference below, and the borrowed address is its value.
            this.referenceCalls.Add(placeCall);
        }

        if (unwrapped is ConversionKoto storedFollow && this.ReadsStoredReference(storedFollow))
        {
            return this.BorrowStoredReference(storedFollow, type, reservation); // SPEC 13.5.5.1: a Reborrow through the stored reference.
        }

        if (unwrapped is ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PayloadFollow or ConversionBinding.PairFollow } selected)
        {
            // SPEC 13.5.5.2: a Reborrow or payload borrow lends the referent's capability through the parent
            // reference or handle, which is read but never moved; the borrowed address is the parent's value.
            return this.BorrowStruct(selected.Left, type, reservation);
        }

        if (unwrapped is BinaryKoto objectPart && !Binding.IsGetterResult(objectPart) && !this.SpecialField(objectPart) &&
            this.Resolve(objectPart.BoundType, this.Active) is { } storedObject && ObjectTypes.HandleMode(storedObject) is not null && ReferenceEquals(storedObject.Components[0], type.Components[0]) &&
            (ElementAccess.OwnedPathRoot(objectPart) is not null || (objectPart is MemberAccessKoto objectField && ElementAccess.BorrowedPathRoot(objectField) is not null) ||
                (objectPart is IndexKoto objectIndex && (objectIndex.Left.BoundType?.Kind == BoundTypeKind.FixedArray || ReferenceTypes.IsArray(objectIndex.Left.BoundType)))))
        {
            return this.BorrowStoredObject(objectPart, storedObject, type, reservation);
        }

        if (unwrapped is IndexKoto element && element.Right is not RangeKoto && type.Semantics == SemanticsKind.Uniq &&
            (element.Left.BoundType?.Kind == BoundTypeKind.Array || ElementAccess.IsExclusiveArrayElement(element)))
        {
            // SPEC 4.6.9, 4.5: an exclusive element borrow uses the array's existing exclusive capability (an owned
            // Array is borrowed like an exclusive receiver; a fixed/dynamic array reference is read) and selects the element
            // through that reference, as exclusive enumeration does; the element keeps the reference's dependency.
            var depth = this.comparisonDepth++;
            var handle = element.Left.BoundType!.Kind == BoundTypeKind.Array
                ? this.BorrowStruct(element.Left, this.compilation.Binding.ExclusiveArrayHandle(element.Left))
                : this.Receiver(element.Left, true);
            var subscript = handle < 0 ? -1 : this.Value(this.SelectionKey(element, handle));
            var borrowedElement = handle < 0 || subscript < 0 ? -1 : this.SequenceValue(element, type, SequenceOperation.Borrow, handle, index: subscript);
            this.EndComparisonLoans(depth, element);
            this.comparisonDepth = depth;
            // A call reservation needs a real Reborrow of the selected address, not the Sequence operation that
            // computed it. This keeps the parent capability and makes activation refer to the acquired element.
            return borrowedElement >= 0 && reservation >= 0 ? this.BorrowThrough(source, borrowedElement, type, reservation) : borrowedElement;
        }

        if (unwrapped is IndexKoto slice && type.Semantics is SemanticsKind.Ref or SemanticsKind.ObjRef &&
            (slice.Left.BoundType?.Kind is BoundTypeKind.Slice or BoundTypeKind.Array || ReferenceTypes.IsDynamicArray(slice.Left.BoundType) || ReferenceTypes.IsArray(slice.Left.BoundType)))
        {
            // A shared Reborrow of a stored exclusive reference or an objref view of a stored handle loads the stored
            // pointer (SPEC 10.2, 4.6.9); any other shared borrow takes the element slot's address.
            var reborrow = slice.BoundType is { } stored && SharedReadTypes.ReadsStoredPointer(stored, type);
            var depth = this.comparisonDepth++;
            var handle = this.SequenceReceiver(slice.Left, out var projection, type.Origin);
            var subscript = this.Value(this.SelectionKey(slice, handle, projection));
            var borrowedElement = handle < 0 || subscript < 0 ? -1 : this.SequenceValue(slice, type, reborrow ? SequenceOperation.Read : SequenceOperation.Borrow, handle, projection, index: subscript);
            this.EndComparisonLoans(depth, slice);
            this.comparisonDepth = depth;
            return borrowedElement;
        }

        if (unwrapped is IndexKoto index && !ElementAccess.IsSlicing(index) && ((index.Left.BoundType?.Kind == BoundTypeKind.FixedArray && ElementAccess.StaticSelector(index) < 0) ||
            (ElementAccess.AccessType(index.Left) is { Semantics: SemanticsKind.Ref } array && ReferenceTypes.IsArray(array))) &&
            (type.Semantics is SemanticsKind.Ref or SemanticsKind.ObjRef || (type.Semantics == SemanticsKind.Uniq && index.Left.BoundType?.Kind == BoundTypeKind.FixedArray)))
        {
            // SPEC 4.6.9: the fixed array is a declared reference or an element Place borrowed as the receiver, in the use's mode for
            // a runtime index into an owned fixed array (PLAN G59). A stored exclusive reference or handle is read as its shared view;
            // any other element is borrowed at its address.
            if (index.BoundType is { } stored && SharedReadTypes.ReadsStoredPointer(stored, type))
            {
                if (!ReferenceTypes.IsArray(index.Left.BoundType))
                {
                    this.Unsupported(index); // An element Place borrowed as the receiver holds no stored pointer to read.
                    return -1;
                }

                var depth = this.comparisonDepth++;
                var handle = this.Expression(index.Left, PlaceUseKind.Read);
                var position = handle < 0 ? -1 : this.Value(this.SelectionKey(index, handle));
                var read = position < 0 ? -1 : this.SequenceValue(index, type, SequenceOperation.Read, handle, index: position);
                this.EndComparisonLoans(depth, index);
                this.comparisonDepth = depth;
                return read;
            }

            if (type.Semantics == SemanticsKind.ObjRef)
            {
                this.Unsupported(index);
                return -1;
            }

            var receiver = index.Left.BoundType?.Kind == BoundTypeKind.FixedArray
                ? this.BorrowStruct(index.Left, this.compilation.Binding.Reference(type.Semantics, index.Left.BoundType, type.Origin))
                : this.Receiver(index.Left);
            var receiverValue = this.Value(receiver);
            var subscript = this.SelectionKey(index, receiver);
            if (receiver < 0 || subscript < 0)
            {
                return -1;
            }

            var projected = this.Place(index, type, OwnershipPlaceKind.Temporary, false);
            var elementBorrow = this.Emit(OwnershipOperationKind.Borrow, index, receiver, projected, loanMode: type.Semantics == SemanticsKind.Uniq ? LoanRequirement.Uniq : LoanRequirement.Ref, reservation: reservation);
            this.SetValue(elementBorrow, OwnershipValueKind.Address, [receiverValue, this.Value(subscript)], constant: receiver);
            return this.RegisterTemporary(projected);
        }

        if (unwrapped is MemberAccessKoto storedField && !Binding.IsGetterResult(storedField) && !this.SpecialField(storedField) &&
            ReferenceTypes.IsBorrow(storedField.BoundType) && ReferenceEquals(storedField.BoundType!.Components[0], type.Components[0]) &&
            ElementAccess.BorrowedPathRoot(storedField) is not null)
        {
            // SPEC 10.2: a fixed expected reference reborrows the stored pointer; an explicit borrow of the slot has
            // the complete field Type as its referent and therefore continues through the ordinary projection below.
            return this.BorrowStoredReference(storedField, storedField, type, reservation);
        }

        if (unwrapped is BinaryKoto storedPart && !Binding.IsGetterResult(storedPart) && !this.SpecialField(storedPart) &&
            ReferenceTypes.IsBorrow(storedPart.BoundType) && ReferenceEquals(storedPart.BoundType!.Components[0], type.Components[0]) &&
            ElementAccess.OwnedPathRoot(storedPart) is not null)
        {
            // SPEC 10.2, 13.5.5.1: the same Reborrow of a pointer stored in an inline part of an owned root.
            return this.BorrowStoredReference(storedPart, storedPart, type, reservation);
        }

        // A field holding a reference is borrowed here only as its slot (`p.0@ref` of `p: ref/(ref/i32, i32)`), never through
        // a copy of the stored reference, whose temporary would not outlive the call (SPEC 3.3.6, 15.6.2).
        if (unwrapped is MemberAccessKoto field && !Binding.IsGetterResult(field) && !this.SpecialField(field) &&
            (!ReferenceTypes.IsStorage(field.BoundType) || ReferenceEquals(type.Components[0], field.BoundType)) &&
            ElementAccess.BorrowedPathRoot(field) is { } root)
        {
            var receiver = this.Receiver(root, type.Semantics == SemanticsKind.Uniq, reservation);
            if (receiver < 0)
            {
                return -1;
            }

            return this.BorrowFieldAddress(field, receiver, type, reservation);
        }

        if (unwrapped is BinaryKoto path && !Binding.IsGetterResult(path) && !this.SpecialField(path) && ReferenceEquals(type.Components[0], path.BoundType) &&
            ElementAccess.OwnedPathRoot(path) is { } owner)
        {
            // Borrow the inline part in place; its Loan footprint is the static path (SPEC 15.6.2).
            var ownerPlace = this.Local(owner);
            if (ownerPlace < 0)
            {
                return -1;
            }

            var borrowed = this.Place(path, type, OwnershipPlaceKind.Temporary, false);
            var borrow = this.Emit(OwnershipOperationKind.Borrow, path, ownerPlace, borrowed, loanMode: type.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq ? LoanRequirement.Uniq : LoanRequirement.Ref, reservation: reservation);
            this.SetValue(borrow, OwnershipValueKind.Address, [], constant: ownerPlace);
            return this.RegisterTemporary(borrowed);
        }

        if (unwrapped is BinaryKoto part && ElementAccess.ElementPathBase(part) is { } elementBase && type.Semantics is SemanticsKind.Ref or SemanticsKind.Uniq &&
            ReferenceEquals(type.Components[0], part.BoundType))
        {
            // SPEC 3.4.1, 4.6.9 (PLAN G59): a stored part below an Array element is borrowed in place. The element is borrowed in the
            // use's mode through its own route, keeping the Array's Loan, and the part through that borrow, as `(xs[i]@m).part@m` is.
            var receiver = this.BorrowStruct(elementBase, this.compilation.Binding.ElementPlaceReference(type.Semantics, elementBase));
            return receiver < 0 ? -1 : this.BorrowFieldAddress(part, receiver, type, reservation);
        }

        // The Value route: a value expression, including a getter's result or a range selection's Slice, is evaluated once and its
        // temporary borrowed (SPEC 10.2, 4.6.6). A stored Field, Tuple or element selection that reaches here has no Place route:
        // borrowing a temporary read of it would update, expose or lend a copy (SPEC 3.4, 5.4), so it fails closed before any state
        // is emitted and names the route it lacks (PLAN G59). Only a Reborrow through a read stored reference or handle, which
        // addresses its referent rather than the selection's slot, reads its selection.
        if (this.UnroutedSelection(unwrapped) is { } selection && !this.ReborrowsStoredReference(selection, type))
        {
            this.ReadSelectionInputs(selection);
            this.UnsupportedRoute(selection);
            return -1;
        }

        var direct = (StructStorage.IsStruct(source.BoundType) || EnumStorage.IsEnum(source.BoundType) || ReferenceEquals(source.BoundType, BoundType.String) || source.BoundType?.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Tuple or BoundTypeKind.Parameter or BoundTypeKind.AssociatedProjection or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication || ScalarTypes.Supports(source.BoundType) || ReferenceTypes.IsPointer(source.BoundType)) &&
            unwrapped is IdentifierNameKoto && unwrapped.BoundSymbol?.Kind != BindingSymbolKind.PatternCandidate;
        var place = this.SpecialField(unwrapped) ? this.Local(unwrapped)
            : source is FormattingKoto hidden && !ReferenceTypes.IsBorrow(hidden.BoundType) && this.formattingPlaces.TryGetValue(hidden, out var prepared) ? prepared
            : direct ? this.Local(unwrapped) : this.Expression(source, PlaceUseKind.Read);
        if (place < 0)
        {
            return -1;
        }

        if (direct && ReferenceTypes.IsBorrow(this.body.Places[place].Type))
        {
            // SPEC 8.9: an instance's generic Place storing a reference is Reborrowed through the stored pointer it reads.
            this.Emit(OwnershipOperationKind.Read, source, place);
        }

        var result = this.Place(source, type, OwnershipPlaceKind.Temporary, false);
        var operation = this.Emit(OwnershipOperationKind.Borrow, source, place, result, loanMode: type.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq ? LoanRequirement.Uniq : LoanRequirement.Ref, reservation: reservation);
        // A scalar temporary is materialized at the borrow from its one prepared value (SPEC 3.6.2, 10.2), and so is the result
        // of a selection, `do` or short-circuit join, such as the slot a pending call prepared from one (SPEC 7.2.3).
        var actual = this.body.Places[place];
        var materialized = ScalarTypes.Supports(actual.Type) && actual.Kind is OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result;
        this.SetValue(operation, OwnershipValueKind.Address, ReferenceTypes.IsBorrow(actual.Type) || materialized ? [this.Value(place)] : [], constant: place);
        return this.RegisterTemporary(result);
    }

    // A stored Field, Tuple or element Place selection, other than a receiver's own Field (a named Place), a getter or accessor
    // result, or a range selection's Slice value.
    private BinaryKoto? UnroutedSelection(Koto unwrapped)
        => KotoHelper.UnwrapParentheses(unwrapped) is BinaryKoto selection && ElementAccess.IsSyntax(selection) && !ElementAccess.IsSlicing(selection) &&
            !Binding.IsGetterResult(selection) && this.compilation.Binding.PropertyCall(selection, PropertyAccessorKind.Get) is null &&
            !this.SpecialField(selection) ? selection : null;

    // SPEC 10.2, 13.5.5.1: a borrow whose referent is not the selection's own stored Type Reborrows the reference or handle the
    // selection stores.
    private bool ReborrowsStoredReference(BinaryKoto selection, BoundType type)
        => this.Resolve(selection.BoundType, this.Active) is { } part && type.Components.Count == 1 &&
            !ReferenceTypes.StorageMatches(part, this.Resolve(type.Components[0], this.Active));

    // SPEC 23.3.6.4 (PLAN G59): an unsupported Place borrow names the route it lacks, which follows the root: a reference or Slice
    // receiver on the path needs the borrowed path; otherwise a runtime selector needs the owned projection, and a value root the
    // temporary's Place. The segment that decides it is related.
    private void UnsupportedRoute(BinaryKoto selection)
    {
        var feature = OwnershipFeature.TemporaryPartBorrow;
        Koto segment = selection;
        Koto? runtime = null;
        for (var level = selection; ;)
        {
            var receiver = ElementAccess.AccessType(level.Left);
            if (receiver?.Kind == BoundTypeKind.Slice || ReferenceTypes.IsBorrow(receiver) || ObjectTypes.HandleMode(receiver) is not null || ObjectTypes.IsBorrow(receiver))
            {
                feature = OwnershipFeature.ReferencedElementBorrow;
                segment = level;
                break;
            }

            if (runtime is null && level is IndexKoto && ElementAccess.StaticSelector(level) < 0)
            {
                runtime = level;
            }

            if (KotoHelper.UnwrapParentheses(level.Left) is not BinaryKoto parent || !ElementAccess.IsSyntax(parent) || Binding.IsGetterResult(parent))
            {
                if (runtime is not null)
                {
                    feature = OwnershipFeature.RuntimeElementBorrow;
                    segment = runtime;
                }
                else if (level.Left is { } root)
                {
                    segment = root;
                }

                break;
            }

            level = parent;
        }

        this.Emit(OwnershipOperationKind.Unsupported, selection);
        this.body.ReportIssue(new(selection, OwnershipFailure.Unsupported, Related: segment, Feature: feature));
    }

    // SPEC 23.3.6.4 (PLAN G59 U1): a rejected selection still reads its root and runtime keys in evaluation order, so their own
    // diagnostics remain; no element is located, protected or acquired, and no value is consumed as an acquisition.
    private void ReadSelectionInputs(BinaryKoto level)
    {
        if (KotoHelper.UnwrapParentheses(level.Left) is BinaryKoto parent && ElementAccess.IsSyntax(parent) && !Binding.IsGetterResult(parent))
        {
            this.ReadSelectionInputs(parent);
        }
        else
        {
            this.Expression(level.Left, PlaceUseKind.Read);
        }

        if (level is IndexKoto index && ElementAccess.StaticSelector(index) < 0)
        {
            this.Expression(index.Right, PlaceUseKind.Read);
        }
    }

    private int BorrowFieldAddress(BinaryKoto field, int receiver, BoundType type, int reservation = -1)
    {
        var projected = this.Place(field, type, OwnershipPlaceKind.Temporary, false);
        var address = this.Emit(OwnershipOperationKind.Borrow, field, receiver, projected, loanMode: type.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq ? LoanRequirement.Uniq : LoanRequirement.Ref, reservation: reservation);
        this.SetValue(address, OwnershipValueKind.Address, [this.Value(receiver)], constant: receiver);
        return this.RegisterTemporary(projected);
    }

    // SPEC 3.4.1: a receiver with a recorded adaptation is evaluated to its one reference; any other receiver is read.
    private int Receiver(Koto root, bool exclusive = false, int reservation = -1)
    {
        if (this.Resolve(root.BoundType, this.Active) is { } handle && ObjectTypes.HandleMode(handle) is not null)
        {
            return this.BorrowIntermediate(root, this.compilation.Binding.ObjectView(root, handle, exclusive), reservation);
        }

        if (this.compilation.Binding.TryGetAdaptation(root, out var adaptation) && adaptation.Kind == ExpectedAdaptationKind.SharedBorrow)
        {
            return this.BorrowStruct(root, ElementAccess.AccessType(root, exclusive)!);
        }

        // SPEC 13.5.5.1, 15.6.2: for a shared access the reference of an explicitly selected referent (`p@follow.x`) lends its
        // referent as the adapted receiver of `p.x` does; an exclusive access uses the reference itself, so a write is judged
        // by its own path and disjoint parts stay separate.
        if (!exclusive && ElementAccess.IsFollowedRoot(root) && this.Resolve(root.BoundType, this.Active) is { Components.Count: 1 } reference)
        {
            return this.BorrowStruct(root, this.compilation.Binding.Reference(exclusive ? SemanticsKind.Uniq : SemanticsKind.Ref, reference.Components[0], reference.Origin));
        }

        return this.Expression(root, PlaceUseKind.Read);
    }

    private int ReadBorrowedField(MemberAccessKoto field)
    {
        var receiver = this.Receiver(ElementAccess.BorrowedPathRoot(field)!);
        if (receiver < 0)
        {
            return -1;
        }

        if (this.Resolve(field.BoundType, this.Active) is not { } type)
        {
            this.Unsupported(field);
            return -1;
        }

        if (this.compilation.Binding.ProveCopy(type, field) != ConstraintProof.Proven)
        {
            // SPEC 3.5: a Place is never moved by bare acquisition; a non-Copy field needs an explicit borrow or @move, which
            // the shared path refutes. The read is still modeled, so later uses are checked and the result is delivered.
            this.body.ReportIssue(new(field, OwnershipFailure.TransferRequired));
        }
        else if (!this.SupportsCopySnapshot(type, field))
        {
            this.Unsupported(field);
            return -1;
        }

        var result = this.Temporary(field);
        this.SetValue(this.Value(result), OwnershipValueKind.BorrowedField, [this.Value(receiver)]);
        return result;
    }

    private int WriteBorrowedField(Koto source, MemberAccessKoto field)
    {
        // SPEC 13.7: secure the RHS, then acquire the field's exclusive address once. References and owning object
        // paths share the same read/update/replacement, including destruction of the old value.
        var root = ElementAccess.BorrowedPathRoot(field)!;
        var receiverType = this.Resolve(ElementAccess.AccessType(root, true), this.Active);
        var operation = source.Akind == KotoKind.Equals ? KotoKind.Equals : ElementAccess.UpdateOperator(source.Akind);
        if (!(receiverType?.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq || ObjectTypes.HandleMode(receiverType) is { PayloadAuthority: LoanRequirement.Uniq }) ||
            this.Resolve(field.BoundType, this.Active) is not { } stored || (operation != KotoKind.Equals && !this.SupportsUpdate(field, stored, operation)))
        {
            this.Unsupported(source);
            return -1;
        }

        var right = source is BinaryKoto binary ? this.Expression(binary.Right) : -1;
        if (source is BinaryKoto && right < 0)
        {
            return -1;
        }

        var referenceType = this.compilation.Binding.Reference(SemanticsKind.Uniq, stored, receiverType!.Origin);
        var address = this.BorrowStruct(field, referenceType);
        if (address < 0)
        {
            return -1;
        }

        var input = right;
        var previous = -1;
        if (operation != KotoKind.Equals)
        {
            previous = this.Value(this.LoadThrough(field, address, 1, referenceType));
            var operand = source is BinaryKoto ? this.Value(right) : previous >= 0 ? this.IncrementOne(source) : -1;
            input = previous >= 0 && operand >= 0 && this.flow!.Nodes[source].CanCompleteNormally
                ? this.ComputeUpdate(source, stored, previous, operand, operation) : -1;
        }

        if (input < 0)
        {
            return -1;
        }

        this.StorePointer(field, this.Value(address), input);
        return operation == KotoKind.Equals ? this.Temporary(source) : this.UpdateResult(source, previous, input);
    }

    // A generic integer or generic wrapping integer in a definition body (SPEC 8.4.7.3): numeric in every instance. Universal
    // verification accepts it, and each instance plan sees the concrete Scalar.
    private bool GenericInteger(Koto source)
        => this.instance is null && source.BoundType is { } type &&
        ((type.Kind is BoundTypeKind.Parameter or BoundTypeKind.AssociatedProjection && this.compilation.Binding.ProvePrimitiveInteger(type, source) == ConstraintProof.Proven) ||
            this.compilation.Binding.IsGenericWrappingInteger(type, source));

    // SPEC 13.7: every compound update computes `target op right` through the same plan, which exists for a numeric Scalar target,
    // a generic integer, and a raw pointer displaced by + or - (SPEC 5.3) where the target may hold one. Any other target, such
    // as a string, is outside the implemented subset on every path that reaches it.
    private bool SupportsUpdate(Koto target, BoundType? type, KotoKind operation, bool pointer = false)
        => operation != KotoKind.Invalid && this.Resolve(type, this.Active) is { } concrete &&
            (concrete.IsNumeric || this.GenericInteger(target) || (pointer && ReferenceTypes.IsPointer(concrete) && operation is KotoKind.Plus or KotoKind.Minus));

    // An obligation's verdict: whether it remains unchecked and, for an Origin relation, its judgment and whether its reverse
    // direction decides it.
    private readonly record struct ObligationVerdict(bool Unchecked, OriginJudgment Judgment, bool Reversed);
}
