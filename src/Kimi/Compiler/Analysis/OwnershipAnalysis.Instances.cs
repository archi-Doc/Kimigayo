// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

/// <summary>The Semantics-case substitution of a definition run and its pair layers (SPEC 8.15, 13.5.5).</summary>
public sealed partial class OwnershipAnalysis
{
    // SPEC 8.15: the Semantics case of the current definition run, one admitted Semantics per resolved pair binder in scope;
    // empty outside a case run. A case run substitutes the declared Types (Concrete).
    private PairCase[] cases = [];
    private int caseCount;

    // Whether declared Types are substituted in this run: by a Semantics case (SPEC 8.15).
    private bool Substituting => this.caseCount != 0;

    // SPEC 13.5.5: a @follow that selects the referent of a reference. A followed pair layer does so in a Semantics case or an
    // instance whose binding is ref or uniq; for owner it selects the operand Place itself.
    private bool FollowsReference(ConversionKoto conversion)
    {
        if (conversion.ConversionBinding == ConversionBinding.Follow)
        {
            return true;
        }

        if (conversion.ConversionBinding != ConversionBinding.PairFollow)
        {
            return false;
        }

        var operand = this.Resolve(conversion.Left.BoundType, this.Active);
        if (Binding.TryPairLayer(operand, out _, out _))
        {
            // SPEC 8.15: a followed layer belongs to a resolved binder, which every case run and instance substitutes; a layer
            // that survives the substitution is an internal invariant failure, never a silent owner view.
            this.body.ReportIssue(new(conversion, this.Substituting ? OwnershipRequirement.Internal : OwnershipRequirement.Unsupported));
            return false;
        }

        return operand is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components: [var referent] } &&
            ReferenceEquals(referent, this.Resolve(conversion.BoundType, this.Active));
    }

    // A pair layer exists in an instance exactly when its operand is a reference to the selected target: an owner binding of
    // s/(t/U), or of s/T with a reference T, is no layer although its concrete operand is a reference (SPEC 13.5.5).
    private bool PairLayerExists(BoundType? operand, BoundType? target)
        => this.Resolve(operand, this.Active) is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components: [var referent] } &&
            ReferenceEquals(referent, this.Resolve(target, this.Active));

    // The number of concrete safe reference layers of an operand above a target in an instance, or -1 when they do not end
    // in it (SPEC 13.5.5: nested pair layers exist only for their ref or uniq bindings).
    private int ReferenceLayers(BoundType? operand, BoundType? target)
    {
        var terminal = this.Resolve(target, this.Active);
        var layers = 0;
        for (var type = this.Resolve(operand, this.Active); type is not null && layers < 16; type = type.Components[0], layers++)
        {
            if (ReferenceEquals(type, terminal))
            {
                return layers;
            }

            if (type is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 })
            {
                break;
            }
        }

        return -1;
    }

    // SPEC 13.5.5, 13.5.6: a followed reference stored in a field or element Place lends through the stored reference:
    // the slot is borrowed only to load the pointer, which is neither moved nor copied as an owner. A pair follows this
    // path in a ref/uniq instance; a local operand is read in place by the ordinary reference paths.
    private bool ReadsStoredReference(ConversionKoto conversion)
    {
        var left = KotoHelper.UnwrapParentheses(conversion.Left);
        // A subscript's element acquires its complete stored reference through the reference its selected call returns.
        return left is not IdentifierNameKoto && this.FollowsReference(conversion) &&
            ((conversion.ConversionBinding == ConversionBinding.PairFollow && this.Substituting) ||
                (conversion.ConversionBinding == ConversionBinding.Follow && (left is not IndexKoto || ElementAccess.IsUserIndex(left))));
    }

    // SPEC 3.4.3, 7.3: a receiver selected through a pair layer lends through the reference stored in it in a ref or uniq
    // case or instance; an owner case or instance borrows the receiver Place itself.
    private bool ImplicitlyFollowsReference(Koto node, BoundType type)
        => this.Substituting && this.compilation.Binding.ImplicitPairAdmitted(node) != SemanticsMask.None &&
            this.PairLayerExists(node.BoundType, type.Components[0]);

    private int StoredReference(ConversionKoto pair) => this.StoredReference(KotoHelper.UnwrapParentheses(pair.Left), SemanticsKind.Ref);

    private int StoredReference(Koto left, SemanticsKind mode, int reservation = -1)
    {
        // SPEC 13.5.5, 15.6.2: a reference stored in an inline Field, Tuple element or static element of an owned root is
        // reached by borrowing that slot in place; let restricts replacing the slot, not the stored reference's capability.
        var ownedSlot = left is BinaryKoto path && !Binding.IsGetterResult(path) && !this.SpecialField(path) && ElementAccess.OwnedPathRoot(path) is not null;
        if (ElementAccess.IsUserIndex(left) || ElementAccess.IsIndexerCall(left) || ownedSlot)
        {
            var designated = ElementAccess.DesignatedType(left)!;
            var stored = this.Resolve(designated, this.Active)!;
            // SPEC 15.6.7: the exclusive slot borrow and the loaded reference of a reserved argument are reserved with it,
            // so a later argument may still inspect the same stored reference until the call activates all three.
            var reserved = ownedSlot && reservation >= 0 && mode == SemanticsKind.Uniq;
            var slotType = ownedSlot ? this.compilation.Types.Reference(mode, designated)
                : this.compilation.Binding.PreparedBorrowType(left, this.compilation.Types.Reference(mode, designated));
            var slot = this.BorrowIntermediate(left, slotType, reserved ? reservation : -1);
            if (slot < 0)
            {
                return -1;
            }

            // The slot is inspected only for its address value. A shared path yields the stored reference's shared
            // capability, with the referent's own Origin; an exclusive path can lend its exclusive capability.
            var acquired = mode == SemanticsKind.Ref && stored.Semantics == SemanticsKind.Uniq
                ? this.compilation.Types.Reference(SemanticsKind.Ref, stored.Components[0], stored.Origin) : stored;
            var pointer = this.Place(left, acquired, OwnershipPlaceKind.Temporary, false, AcquisitionKind.Copy);
            this.Emit(OwnershipOperationKind.Produce, left, pointer);
            this.SetValue(this.Value(pointer), OwnershipValueKind.PointerLoad, [this.Value(slot)]);
            if (reserved)
            {
                this.body.CallReservations[reservation] = this.body.CallReservations[reservation] with { Loaded = pointer };
            }

            return this.RegisterTemporary(pointer);
        }

        if (left is MemberAccessKoto field && !Binding.IsGetterResult(field) && ElementAccess.BorrowedPathRoot(field) is { } root)
        {
            // The stored reference is loaded from the field for the Reborrow; it is not a Copy of an exclusive reference.
            var receiver = this.Receiver(root, mode == SemanticsKind.Uniq);
            if (receiver < 0)
            {
                return -1;
            }

            var loaded = this.Place(field, field.BoundType, OwnershipPlaceKind.Temporary, false, AcquisitionKind.Copy);
            this.Emit(OwnershipOperationKind.Produce, field, loaded);
            this.SetValue(this.Value(loaded), OwnershipValueKind.BorrowedField, [this.Value(receiver)]);
            return this.RegisterTemporary(loaded);
        }

        if (left is ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PairFollow } followed &&
            (followed.ConversionBinding == ConversionBinding.Follow || this.FollowsReference(followed)) &&
            this.Resolve(left.BoundType, this.Active) is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } inner)
        {
            // SPEC 3.4, 13.5.5: a selected referent that is itself a reference (r@follow with r: uniq/uniq/T, or a pair layer
            // followed in a case or instance where it exists) is read only for its address through the outer reference, as a
            // stored slot is, not copied; an exclusive inner reference is not Copy, and a shared outer path yields its shared
            // capability.
            var outer = this.StoredReference(KotoHelper.UnwrapParentheses(followed.Left), mode);
            if (outer < 0)
            {
                return -1;
            }

            var acquired = mode == SemanticsKind.Ref && inner.Semantics == SemanticsKind.Uniq
                ? this.compilation.Types.Reference(SemanticsKind.Ref, inner.Components[0], inner.Origin) : inner;
            var pointer = this.Place(left, acquired, OwnershipPlaceKind.Temporary, false, AcquisitionKind.Copy);
            this.Emit(OwnershipOperationKind.Produce, left, pointer);
            this.SetValue(this.Value(pointer), OwnershipValueKind.PointerLoad, [this.Value(outer)]);
            return this.RegisterTemporary(pointer);
        }

        return this.ExpressionCore(left, PlaceUseKind.Read, null);
    }

    // SPEC 13.5.6: a Reborrow of the referent through a stored reference; the borrowed address is the reference's value.
    private int BorrowStoredReference(ConversionKoto pair, BoundType type, int reservation)
        => this.BorrowStoredReference(pair, KotoHelper.UnwrapParentheses(pair.Left), type, reservation);

    private int BorrowStoredReference(Koto source, Koto left, BoundType type, int reservation)
    {
        var reference = this.StoredReference(left, type.Semantics == SemanticsKind.Uniq ? SemanticsKind.Uniq : SemanticsKind.Ref, reservation);
        return reference < 0 ? -1 : this.BorrowThrough(source, reference, type, reservation);
    }

    // A Reborrow through an evaluated reference: the borrowed address is the reference's value. An instance sees the
    // prepared Type in its closed substitution, whose Origins are the caller's.
    private int BorrowThrough(Koto source, int reference, BoundType type, int reservation)
    {
        var result = this.Place(source, this.Resolve(type, this.Active)!, OwnershipPlaceKind.Temporary, false);
        var operation = this.Emit(OwnershipOperationKind.Borrow, source, reference, result, loanMode: type.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq ? LoanRequirement.Uniq : LoanRequirement.Ref, reservation: reservation);
        this.SetValue(operation, OwnershipValueKind.Address, [this.Value(reference)], constant: reference);
        return this.RegisterTemporary(result);
    }

    // SPEC 10.2, 13.5.5: two or more existing layers below a pair Subject in a case or instance are loaded into one reference
    // to their terminal target in the Subject's mode; each uniq layer meets its Origin into it and a ref layer restarts it.
    // Returns -2 when the case or instance has fewer layers, which the single-layer paths handle.
    private int ThroughPairLayers(Koto node, SemanticsKind mode)
    {
        if (!this.Substituting || this.compilation.Binding.PairTerminalOf(node) is not { } target || this.ReferenceLayers(node.BoundType, target) < 2 ||
            this.Resolve(node.BoundType, this.Active) is not { } concrete ||
            this.compilation.Binding.SharedReferenceThroughLayers(concrete, this.Resolve(target, this.Active)!, out _) is not { } shared)
        {
            return -2;
        }

        return this.ReadReference(node, mode == SemanticsKind.Uniq ? this.compilation.Types.Reference(SemanticsKind.Uniq, shared.Components[0], shared.Origin) : shared);
    }

    // The Place a node designates once owner pair layers are removed (SPEC 13.5.5).
    private Koto SelectedPlace(Koto node)
    {
        while (node is ConversionKoto { ConversionBinding: ConversionBinding.PairFollow } pair && !this.FollowsReference(pair))
        {
            node = KotoHelper.UnwrapParentheses(pair.Left);
        }

        return node;
    }

    // A declared Type of the analyzed body interpreted in a context (SPEC 7.2.3, 8.15): the context's default substitution, then
    // the Semantics case's Types; a case substitution cannot fail.
    private BoundType? Resolve(BoundType? type, InterpretationContext context)
    {
        type = this.body.SubstituteDefaults(type, context);
        return type is null || this.caseCount == 0 ? type : this.compilation.Binding.CaseType(type, this.cases.AsSpan(0, this.caseCount));
    }
}
