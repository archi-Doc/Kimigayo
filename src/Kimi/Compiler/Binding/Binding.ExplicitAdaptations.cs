// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly ScratchBuffers<PairCase> adaptationCaseScratch = new();

    // Inputs and acquisition were verified at definition. A context substitutes them and selects the canonical factory.
    internal CallPlan ResolveObjectCreation(ConversionKoto conversion, BoundType source, BoundType target, CallPlan destination)
    {
        var factory = this.Library.GetSymbol(ObjectFactoryId(target.Semantics))!;
        var argument = new BoundArgumentOperation(conversion.Left, source, source, ArgumentOperationKind.Value, ArgumentAdaptation.Exact, ParameterIndex: 0);
        destination.Set(factory, target, null, [0], [source], inputOrigins: [this.PlaceOrigin(conversion.Left)], operations: [argument]);
        destination.AdaptationSource = conversion;
        return destination;
    }

    // Short generic targets apply the selected Semantics, not a second round of operand inference.
    private BoundType? BindSemanticsAdaptation(ConversionKoto conversion, BindingScope scope, BindingSymbol semantics)
    {
        var source = this.BindNode(conversion.Left, scope);
        if (source is null)
        {
            return Complete(conversion, null);
        }

        if (ReferenceEquals(source, BoundType.Never))
        {
            Complete(conversion.Right, source);
            conversion.ConversionBinding = ConversionBinding.Abrupt;
            return Complete(conversion, source);
        }

        var whole = semantics.Pair!.WholeType!;
        var admitted = this.AdmittedSemantics(whole, scope);
        var payload = source;
        var origin = (admitted & SemanticsMask.Borrow) == 0 ? null : this.SlotOrigin(conversion.Left);
        const SemanticsMask objectFamily = SemanticsMask.Object | SemanticsMask.ObjectBorrow;
        if ((admitted & ~(SemanticsMask.Owner | objectFamily)) == 0 &&
            TryPairLayer(source, out var original, out _) && ReferenceEquals(original, whole))
        {
            return this.BindCaseAdaptation(conversion, scope, source, source, true);
        }

        if ((admitted & SemanticsMask.ObjectBorrow) != 0 && (TryAdaptationSelector(source, out _) || IsObjectSemantics(source.Semantics)))
        {
            return this.BindCaseAdaptation(conversion, scope, source, this.SemanticsAdaptation(semantics.Pair, source, origin), true);
        }

        if ((admitted & ~objectFamily) == 0)
        {
            if (IsObjectSemantics(source.Semantics))
            {
                payload = source.Components[0];
            }
            else if (TryPairLayer(source, out var inputWhole, out var inputTarget) &&
                (this.AdmittedSemantics(inputWhole, scope) & ~(SemanticsMask.Owner | objectFamily)) == 0)
            {
                payload = inputTarget;
            }
        }
        else if ((admitted & ~(SemanticsMask.Owner | SemanticsMask.Object)) == 0 &&
            TryPairLayer(source, out var same, out var projection) && ReferenceEquals(same, whole))
        {
            payload = projection;
        }
        else if ((admitted & objectFamily) != 0 && (TryAdaptationSelector(source, out _) || IsObjectSemantics(source.Semantics)))
        {
            return this.BindCaseAdaptation(conversion, scope, source, this.SemanticsAdaptation(semantics.Pair, source, origin), true);
        }

        var target = ReferenceEquals(payload, semantics.Pair.Type) && origin is null
            ? whole : this.InternType(BoundTypeKind.SemanticsApplication, semantics.Pair, SemanticsKind.Parameter, [payload], origin: origin);
        return this.BindCaseAdaptation(conversion, scope, source, target, true);
    }

    // Select only the outer modes needed by the operation. Complete payload Types retain their symbolic proofs.
    private BoundType? BindCaseAdaptation(ConversionKoto conversion, BindingScope scope, BoundType source, BoundType target, bool shorthand = false)
    {
        var caseProduct = this.AdaptationCaseProduct(conversion);
        if (caseProduct > OwnershipAnalysis.CaseBound)
        {
            return this.FailCaseLimit(conversion, scope, caseProduct);
        }

        uint operations = 0;
        BoundType? creationSource = null;
        BoundType? creationTarget = null;
        var rows = 0;
        var caseCount = this.adaptationBinders!.Count;
        var cases = this.adaptationCaseScratch.Rent(caseCount);
        try
        {
            if (!this.CheckAdaptationRows(conversion, scope, source, target, source, target, shorthand, cases.AsSpan(0, caseCount), 0, ref rows, ref operations, ref creationSource, ref creationTarget))
            {
                return null;
            }
        }
        finally
        {
            this.adaptationCaseScratch.Return(cases, clearArray: true);
        }

        if (creationSource is not null && this.BindObjectCreationCall(conversion, scope, creationSource, creationTarget!, selectedCase: true) is null)
        {
            return Complete(conversion, null);
        }

        var plan = conversion.AdaptationStorage ??= new();
        plan.Source = source;
        plan.Target = target;
        plan.Operations = operations;
        plan.IsShorthand = shorthand;
        plan.AddressBorrow = (operations & (1U << (int)ConversionBinding.Address)) == 0 ? null
            : this.SharedReference(source, this.SlotOrigin(conversion.Left));
        if (plan.Creates)
        {
            Complete(conversion.CreationStorage!, target);
        }

        Complete(conversion.Right, target);
        conversion.ConversionBinding = ConversionBinding.CaseAdaptation;
        return Complete(conversion, target);
    }

    private bool CheckAdaptationRows(ConversionKoto conversion, BindingScope scope, BoundType source, BoundType target, BoundType writtenSource, BoundType writtenTarget, bool shorthand, Span<PairCase> cases, int caseCount, ref int rows, ref uint operations, ref BoundType? creationSource, ref BoundType? creationTarget)
    {
        // Stop before the 65th feasible row; exhaustion never certifies a partial operation plan.
        if (rows == OwnershipAnalysis.CaseBound)
        {
            this.FailCaseLimit(conversion, scope, (long)rows + 1, lowerBound: true);
            return false;
        }

        if (TryAdaptationSelector(source, out var whole) || TryAdaptationSelector(target, out whole))
        {
            var admitted = this.AdmittedSemantics(whole, scope);
            if (admitted == SemanticsMask.None || caseCount == cases.Length)
            {
                this.Fail(conversion, BindingFailure.UnprovenConstraint);
                return false;
            }

            foreach (var mode in SemanticsOrder)
            {
                if (admitted.Contains(mode))
                {
                    cases[caseCount] = new(whole.Symbol!, mode);
                    var selected = cases.Slice(caseCount, 1);
                    var actual = this.CaseType(source, selected);
                    var expected = this.CaseType(target, selected);
                    if (ReferenceEquals(actual, source) && ReferenceEquals(expected, target))
                    {
                        this.Fail(conversion, BindingFailure.UnprovenConstraint);
                        return false;
                    }

                    if (!this.CheckAdaptationRows(conversion, scope, actual, expected, writtenSource, writtenTarget, shorthand, cases, caseCount + 1, ref rows, ref operations, ref creationSource, ref creationTarget))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        rows++;
        var operation = ExplicitAdaptationPlan.Select(source, target, shorthand);
        if (operation == ConversionBinding.None)
        {
            this.FailMismatch(conversion, conversion, writtenSource, writtenTarget);
            return false;
        }

        if (!this.CheckAdaptationCase(conversion, scope, source, target, operation, cases[..caseCount]))
        {
            return false;
        }

        operations |= 1U << (int)operation;
        if (operation == ConversionBinding.ObjectCreation)
        {
            creationSource ??= source;
            creationTarget ??= target;
        }

        return true;
    }

    private bool CheckAdaptationCase(ConversionKoto conversion, BindingScope scope, BoundType source, BoundType target, ConversionBinding operation, ReadOnlySpan<PairCase> cases)
    {
        if (operation == ConversionBinding.None)
        {
            this.FailMismatch(conversion, conversion, source, target);
            return false;
        }

        if (operation == ConversionBinding.Identity && !this.CanSelectIdentity(source, scope, cases))
        {
            this.Fail(conversion, BindingFailure.UnprovenConstraint);
            return false;
        }

        if (operation == ConversionBinding.ObjectCreation)
        {
            if (source.Symbol?.ObjectPayloadOptOut is { } renounced)
            {
                this.FailObjectPayload(conversion, renounced);
                return false;
            }

            var proof = this.RequestCapability(source, this.Library.ObjectPayload, scope);
            if (proof != ConstraintProof.Proven)
            {
                this.RequireConstraint(conversion, proof, this.capabilityMode);
                return false;
            }
        }

        if (operation == ConversionBinding.ObjectUpcast && this.BindObjectUpcast(conversion, scope, source, target) is null)
        {
            return false;
        }

        if (operation is ConversionBinding.Borrow or ConversionBinding.Address ||
            (operation == ConversionBinding.ObjectUpcast && target.IsObjectBorrow))
        {
            var exclusive = target.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq;
            if (exclusive && !this.BorrowablePlace(conversion.Left, scope, true) && !BorrowableTemporary(conversion.Left, true, true, false))
            {
                this.Fail(conversion, AccessFailure(conversion.Left));
                return false;
            }
        }
        else if (operation != ConversionBinding.ObjectUpcast && IsBarePlace(conversion.Left) && this.ProveCopy(source, conversion) != ConstraintProof.Proven)
        {
            this.FailAcquisition(conversion, BindingFailure.TransferRequired, conversion.Left);
            return false;
        }

        return true;
    }
}
