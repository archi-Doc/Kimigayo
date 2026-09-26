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
            (conversion.ConversionBinding == ConversionBinding.PairFollow &&
            this.Concrete(conversion.Left.BoundType) is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 });

    // SPEC 13.5.5.1: universal verification of a value use through a pair layer. Every admitted case reads the direct target
    // from the selected Place, so the operand is shared-borrowed for the read and a Copy of the target is produced; the stored
    // pair Type itself need not be Copy. A Copy-unproven target keeps the operand's own acquisition.
    private int CopyPairTarget(ConversionKoto pair)
    {
        var target = pair.BoundType!;
        var left = KotoHelper.UnwrapParentheses(pair.Left);
        if (this.compilation.Binding.ProveCopy(target, pair) != ConstraintProof.Proven || left.BoundType is not { } stored)
        {
            return this.ExpressionCore(left, PlaceUseKind.Consume, null);
        }

        var reference = this.BorrowStruct(left, this.compilation.Binding.SharedReference(stored));
        if (reference < 0)
        {
            return -1;
        }

        var loaded = this.Place(pair, target, OwnershipPlaceKind.Temporary, true, AcquisitionKind.Copy);
        this.Emit(OwnershipOperationKind.Produce, pair, loaded);
        this.SetValue(this.Value(loaded), OwnershipValueKind.PointerLoad, [this.Value(reference)]);
        return this.RegisterTemporary(loaded);
    }

    // SPEC 13.5.5.1, 13.5.5.2: in an instance whose pair binding is ref or uniq, a followed pair stored in a field or element
    // Place lends through the stored reference: the slot is shared-borrowed only to load the pointer, which is neither moved
    // nor copied as an owner. A local operand is read in place by the ordinary reference paths.
    private bool ReadsStoredReference(ConversionKoto conversion)
        => conversion.ConversionBinding == ConversionBinding.PairFollow && this.instance is not null &&
            KotoHelper.UnwrapParentheses(conversion.Left) is not IdentifierNameKoto && this.FollowsReference(conversion);

    private int StoredReference(ConversionKoto pair)
    {
        var left = KotoHelper.UnwrapParentheses(pair.Left);
        if (left is MemberAccessKoto field && !Binding.IsGetterResult(field) && ElementAccess.BorrowedPathRoot(field) is { } root)
        {
            // The stored reference is loaded from the field for the Reborrow; it is not a Copy of an exclusive reference.
            var receiver = this.Receiver(root);
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
    {
        var reference = this.StoredReference(pair);
        if (reference < 0)
        {
            return -1;
        }

        var result = this.Place(pair, type, OwnershipPlaceKind.Temporary, false);
        var operation = this.Emit(OwnershipOperationKind.Borrow, pair, reference, result, loanMode: type.Semantics == SemanticsKind.Uniq ? LoanRequirement.Uniq : LoanRequirement.Ref, reservation: reservation);
        this.SetValue(operation, OwnershipValueKind.Address, [this.Value(reference)], constant: reference);
        return this.RegisterTemporary(result);
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
