// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Per-substitution plans of universally verified generic bodies (SPEC 21.3.1 step 3).</summary>
public sealed partial class OwnershipAnalysis
{
    private readonly List<OwnershipBody> instancePool = new();
    private int instanceCount;
    private OwnershipBody? instanceBody;
    private BoundCall? instance;
    private bool instanceFailed;

    /// <summary>Releases the instance plans of the previous generation request.</summary>
    internal void ClearInstances() => this.instanceCount = 0;

    /// <summary>
    /// Rebuilds the ownership plan of a verified generic body under one closed call substitution. The
    /// universal verification remains the acceptance proof; the instance plan only fixes acquisitions,
    /// value flow and cleanup for concrete lowering, and a refused instance returns null.
    /// </summary>
    /// <param name="generic">The universally verified generic body.</param>
    /// <param name="call">A closed call whose substitution selects the instance.</param>
    /// <returns>The verified instance plan, valid until <see cref="ClearInstances"/>.</returns>
    internal OwnershipBody? AnalyzeInstance(OwnershipBody generic, BoundCall call)
    {
        if (!generic.IsVerified || this.flow is null || this.instance is not null)
        {
            return null;
        }

        if (this.instanceCount == this.instancePool.Count)
        {
            this.instancePool.Add(new());
        }

        var saved = this.body;
        var issueCount = this.issues.Count;
        var target = this.instancePool[this.instanceCount];
        this.instanceBody = target;
        this.instance = call;
        this.instanceFailed = false;
        try
        {
            this.Build(generic.Function);
            if (this.instanceFailed || target.IssueStorage.Count != 0 || !target.IsVerified)
            {
                target.IsVerified = false;
                return null;
            }

            // Validations that run later, during lowering, see the instance's substitution through the body itself.
            target.Instance = call;
            target.InstanceBinding = this.compilation.Binding;
            this.instanceCount++;
            return target;
        }
        finally
        {
            this.issues.RemoveRange(issueCount, this.issues.Count - issueCount);
            this.body = saved;
            this.instanceBody = null;
            this.instance = null;
        }
    }

    // SPEC 13.5.5.1: a @follow that selects the referent of a reference. A followed pair layer does so in an instance whose
    // binding is ref or uniq; for owner, and in the universal verification of the generic body, it selects the operand
    // Place itself, whose Loans cover every admitted case.
    private bool FollowsReference(ConversionKoto conversion)
        => conversion.ConversionBinding == ConversionBinding.Follow ||
            (conversion.ConversionBinding == ConversionBinding.PairFollow && this.PairLayerExists(conversion.Left.BoundType, conversion.BoundType));

    // A pair layer exists in an instance exactly when its operand is a reference to the selected target: an owner binding of
    // s/(t/U), or of s/T with a reference T, is no layer although its concrete operand is a reference (SPEC 13.5.5.1).
    private bool PairLayerExists(BoundType? operand, BoundType? target)
        => this.Concrete(operand) is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components: [var referent] } &&
            ReferenceEquals(referent, this.Concrete(target));

    // The number of concrete safe reference layers of an operand above a target in an instance, or -1 when they do not end
    // in it (SPEC 13.5.5.1: nested pair layers exist only for their ref or uniq bindings).
    private int ReferenceLayers(BoundType? operand, BoundType? target)
    {
        var terminal = this.Concrete(target);
        var layers = 0;
        for (var type = this.Concrete(operand); type is not null && layers < 16; type = type.Components[0], layers++)
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

    // SPEC 13.5.5.1: universal verification of a value use through a pair layer. Every admitted case reads the direct target
    // from the selected Place, so the operand is shared-borrowed for the read and a Copy of the target is produced; the stored
    // pair Type itself need not be Copy. A Copy-unproven target keeps the operand's own acquisition.
    private int CopyPairTarget(ConversionKoto pair) => this.CopyThroughPair(pair, KotoHelper.UnwrapParentheses(pair.Left), pair.BoundType!);

    private int CopyThroughPair(Koto source, Koto left, BoundType target)
    {
        if (this.compilation.Binding.ProveCopy(target, source) != ConstraintProof.Proven || left.BoundType is not { } stored)
        {
            return this.ExpressionCore(left, PlaceUseKind.Consume, null);
        }

        var reference = this.BorrowStruct(left, this.compilation.Binding.SharedReference(stored));
        if (reference < 0)
        {
            return -1;
        }

        var loaded = this.Place(source, target, OwnershipPlaceKind.Temporary, true, AcquisitionKind.Copy);
        this.Emit(OwnershipOperationKind.Produce, source, loaded);
        this.SetValue(this.Value(loaded), OwnershipValueKind.PointerLoad, [this.Value(reference)]);
        return this.RegisterTemporary(loaded);
    }

    // SPEC 13.5.5.1, 13.5.5.2: a followed reference stored in a field or element Place lends through the stored reference:
    // the slot is borrowed only to load the pointer, which is neither moved nor copied as an owner. A pair follows this
    // path in a ref/uniq instance; a local operand is read in place by the ordinary reference paths.
    private bool ReadsStoredReference(ConversionKoto conversion)
    {
        var left = KotoHelper.UnwrapParentheses(conversion.Left);
        // Published Places acquire their complete stored reference through the ordinary Place call.
        return left is not IdentifierNameKoto && this.FollowsReference(conversion) &&
            ((conversion.ConversionBinding == ConversionBinding.PairFollow && this.instance is not null) ||
                (conversion.ConversionBinding == ConversionBinding.Follow && (left is not IndexKoto || ElementAccess.IsUserIndex(left))));
    }

    // SPEC 3.4.1, 7.3: a receiver selected through a pair layer lends through the reference stored in it in a ref or uniq
    // instance; the universal verification and an owner instance borrow the receiver Place itself.
    private bool ImplicitlyFollowsReference(Koto node, BoundType type)
        => this.instance is not null && this.compilation.Binding.ImplicitPairAdmitted(node) != SemanticsMask.None &&
            this.PairLayerExists(node.BoundType, type.Components[0]);

    private int StoredReference(ConversionKoto pair) => this.StoredReference(KotoHelper.UnwrapParentheses(pair.Left), SemanticsKind.Ref);

    private int StoredReference(Koto left, SemanticsKind mode)
    {
        if (ElementAccess.IsUserIndex(left) || ElementAccess.IsPlaceCall(left))
        {
            var stored = this.Concrete(left.BoundType)!;
            var slotType = this.compilation.Binding.PreparedBorrowType(left, this.compilation.Binding.Reference(mode, left.BoundType!));
            var slot = this.BorrowStruct(left, slotType);
            if (slot < 0)
            {
                return -1;
            }

            // The slot is inspected only for its address value. A shared path yields the stored reference's shared
            // capability, with the referent's own Origin; an exclusive path can lend its exclusive capability.
            var acquired = mode == SemanticsKind.Ref && stored.Semantics == SemanticsKind.Uniq
                ? this.compilation.Binding.SharedReference(stored.Components[0], stored.Origin) : stored;
            var pointer = this.Place(left, acquired, OwnershipPlaceKind.Temporary, false, AcquisitionKind.Copy);
            this.Emit(OwnershipOperationKind.Produce, left, pointer);
            this.SetValue(this.Value(pointer), OwnershipValueKind.PointerLoad, [this.Value(slot)]);
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

        return this.ExpressionCore(left, PlaceUseKind.Read, null);
    }

    // SPEC 13.5.5.2: a Reborrow of the referent through a stored reference; the borrowed address is the reference's value.
    private int BorrowStoredReference(ConversionKoto pair, BoundType type, int reservation)
        => this.BorrowStoredReference(pair, KotoHelper.UnwrapParentheses(pair.Left), type, reservation);

    private int BorrowStoredReference(Koto source, Koto left, BoundType type, int reservation)
    {
        var reference = this.StoredReference(left, type.Semantics == SemanticsKind.Uniq ? SemanticsKind.Uniq : SemanticsKind.Ref);
        return reference < 0 ? -1 : this.BorrowThrough(source, reference, type, reservation);
    }

    // A Reborrow through an evaluated reference: the borrowed address is the reference's value. An instance sees the
    // prepared Type in its closed substitution, whose Origins are the caller's.
    private int BorrowThrough(Koto source, int reference, BoundType type, int reservation)
    {
        var result = this.Place(source, this.Concrete(type)!, OwnershipPlaceKind.Temporary, false);
        var operation = this.Emit(OwnershipOperationKind.Borrow, source, reference, result, loanMode: type.Semantics == SemanticsKind.Uniq ? LoanRequirement.Uniq : LoanRequirement.Ref, reservation: reservation);
        this.SetValue(operation, OwnershipValueKind.Address, [this.Value(reference)], constant: reference);
        return this.RegisterTemporary(result);
    }

    // SPEC 10.2, 13.5.5.1: two or more existing layers below a pair Subject in an instance are loaded into one reference to
    // their terminal target in the Subject's mode; each uniq layer meets its Origin into it and a ref layer restarts it.
    // Returns -2 when the instance has fewer layers, which the single-layer paths handle.
    private int ThroughPairLayers(Koto node, SemanticsKind mode)
    {
        if (this.instance is null || this.compilation.Binding.PairTerminalOf(node) is not { } target || this.ReferenceLayers(node.BoundType, target) < 2 ||
            this.Concrete(node.BoundType) is not { } concrete ||
            this.compilation.Binding.SharedReferenceThroughLayers(concrete, this.Concrete(target)!, out _) is not { } shared)
        {
            return -2;
        }

        return this.ReadReference(node, mode == SemanticsKind.Uniq ? this.compilation.Binding.Reference(SemanticsKind.Uniq, shared.Components[0], shared.Origin) : shared);
    }

    // The Place a node designates once owner pair layers are removed (SPEC 13.5.5.1).
    private Koto SelectedPlace(Koto node)
    {
        while (node is ConversionKoto { ConversionBinding: ConversionBinding.PairFollow } pair && !this.FollowsReference(pair))
        {
            node = KotoHelper.UnwrapParentheses(pair.Left);
        }

        return node;
    }

    // Declared Types of the analyzed body; an instance sees its closed substitution.
    private BoundType? Concrete(BoundType? type)
    {
        if (type is null || this.instance is not { } call)
        {
            return type;
        }

        if (this.compilation.Binding.InstantiateStorageType(type, call) is { } concrete)
        {
            return concrete;
        }

        this.instanceFailed = true;
        return type;
    }
}
