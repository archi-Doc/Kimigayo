// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly ScratchBuffers<PairCase> adaptationCaseScratch = new();

    // SPEC 13.5.6, 15.1.5, 8.14: whether an explicit adaptation borrows its operand exclusively (AdaptationBorrow), a case adaptation in
    // some admitted case: every case of its plan holds, and a case whose target is an exclusive reference or object view borrows there.
    internal bool BorrowsExclusively(ConversionKoto conversion)
        => conversion.ConversionBinding == ConversionBinding.CaseAdaptation
            ? conversion.Adaptation is { } plan &&
                (this.ResultSemantics(plan.Target, this.ConstraintScope(conversion)) & (SemanticsMask.Uniq | SemanticsMask.ObjUniq)) != 0
            : conversion.BoundType is { } target && AdaptationBorrow(conversion.ConversionBinding, target) == true;

    // Inputs and acquisition were verified at definition. A context substitutes them and selects the canonical factory.
    internal CallPlan ResolveObjectCreation(ConversionKoto conversion, BoundType source, BoundType target, CallPlan destination)
    {
        var factory = this.Library.GetSymbol(ObjectFactoryId(target.Semantics))!;
        var argument = new BoundArgumentOperation(conversion.Left, source, source, ArgumentOperationKind.Value, ArgumentAdaptation.Exact, ParameterIndex: 0);
        destination.Set(factory, target, null, [0], [source], inputOrigins: [this.PlaceOrigin(conversion.Left)], operations: [argument]);
        destination.AdaptationSource = conversion;
        return destination;
    }

    // SPEC 13.5.6, 15.1.5: the Borrow an explicit adaptation's operation to a target forms of its operand: exclusive (true) to an
    // exclusive reference or object view, shared (false) to any other reference or view, Address included, or none (null).
    private static bool? AdaptationBorrow(ConversionBinding operation, BoundType target)
        => operation is ConversionBinding.Borrow or ConversionBinding.Address || (operation == ConversionBinding.ObjectUpcast && target.IsObjectBorrow)
            ? IsExclusive(target.Semantics) : null;

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
            ? whole : this.types.Intern(BoundTypeKind.SemanticsApplication, semantics.Pair, SemanticsKind.Parameter, [payload], origin: origin);
        return this.BindCaseAdaptation(conversion, scope, source, target, true);
    }

    // Select only the outer modes needed by the operation. Complete payload Types retain their symbolic proofs.
    private BoundType? BindCaseAdaptation(ConversionKoto conversion, BindingScope scope, BoundType source, BoundType target, bool shorthand = false)
    {
        // A definition beyond the case bound verifies none of its cases (Binding.CaseLimits); within it, a row is one of its cases.
        if (Definition(conversion) is { BindingFailure: BindingFailure.CaseLimit } definition)
        {
            return this.CompleteDependent(conversion, definition);
        }

        this.PairBinders(conversion, this.caseBinders ??= new()); // A row selects at most one case of each binder in scope.
        uint operations = 0;
        BoundType? creationSource = null;
        BoundType? creationTarget = null;
        var caseCount = this.caseBinders.Count;
        var cases = this.adaptationCaseScratch.Rent(caseCount);
        try
        {
            var verdict = this.CheckAdaptationRows(
                conversion,
                scope,
                source,
                target,
                source,
                target,
                shorthand,
                cases.AsSpan(0, caseCount),
                0,
                ref operations,
                ref creationSource,
                ref creationTarget);
            if (verdict != true)
            {
                return verdict is null ? this.Fail(conversion, BindingFailure.Unsupported) : null;
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
            : this.types.Reference(SemanticsKind.Ref, source, this.SlotOrigin(conversion.Left));
        if (plan.Creates)
        {
            Complete(conversion.CreationStorage!, target);
        }

        Complete(conversion.Right, target);
        conversion.ConversionBinding = ConversionBinding.CaseAdaptation;
        return Complete(conversion, target);
    }

    // Judges the cases a row selects up to the first that fails (false); otherwise null when some hold only but for a Borrow Binding
    // cannot form yet, true when all hold (CheckAdaptationCase).
    private bool? CheckAdaptationRows(
        ConversionKoto conversion,
        BindingScope scope,
        BoundType source,
        BoundType target,
        BoundType writtenSource,
        BoundType writtenTarget,
        bool shorthand,
        Span<PairCase> cases,
        int caseCount,
        ref uint operations,
        ref BoundType? creationSource,
        ref BoundType? creationTarget)
    {
        if (TryAdaptationSelector(source, out var whole) || TryAdaptationSelector(target, out whole))
        {
            var admitted = this.AdmittedSemantics(whole, scope);
            if (admitted == SemanticsMask.None || caseCount == cases.Length)
            {
                this.Fail(conversion, BindingFailure.UnprovenConstraint);
                return false;
            }

            var unsupported = false;
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

                    var row = this.CheckAdaptationRows(
                        conversion,
                        scope,
                        actual,
                        expected,
                        writtenSource,
                        writtenTarget,
                        shorthand,
                        cases,
                        caseCount + 1,
                        ref operations,
                        ref creationSource,
                        ref creationTarget);
                    if (row == false)
                    {
                        return false;
                    }

                    unsupported |= row is null;
                }
            }

            return unsupported ? null : true;
        }

        var operation = ExplicitAdaptationPlan.Select(source, target, shorthand);
        if (operation == ConversionBinding.None)
        {
            this.FailMismatch(conversion, conversion, writtenSource, writtenTarget, ComparisonFailure.Shape);
            return false;
        }

        var verdict = this.CheckAdaptationCase(conversion, scope, source, target, operation, cases[..caseCount]);
        if (verdict == false)
        {
            return false;
        }

        operations |= 1U << (int)operation;
        if (operation == ConversionBinding.ObjectCreation)
        {
            creationSource ??= source;
            creationTarget ??= target;
        }

        return verdict;
    }

    // Judges one case: true when it holds, null when it holds but for a Borrow SPEC lends and Binding cannot form yet (BorrowFailure:
    // Unsupported, not recorded, so that another case's Language error is reported instead, SPEC 23.1.1), false once its failure is
    // recorded or it depends on one.
    private bool? CheckAdaptationCase(
        ConversionKoto conversion, BindingScope scope, BoundType source, BoundType target, ConversionBinding operation, ReadOnlySpan<PairCase> cases)
    {
        if (operation == ConversionBinding.None)
        {
            this.FailMismatch(conversion, conversion, source, target, ComparisonFailure.Shape);
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

        // An upcast's Types are judged before its Borrow, whose Place may only be unsupported (null), and its result after.
        if (operation == ConversionBinding.ObjectUpcast && !this.ObjectUpcastFits(conversion, source, target))
        {
            return false;
        }

        if (AdaptationBorrow(operation, target) is { } exclusive)
        {
            // SPEC 13.5.6, 15.1.5: a borrowing case, shared or exclusive, borrows a Place that lends in its mode or a Temporary Value.
            if (!this.BorrowablePlace(conversion.Left, scope, exclusive) && !BorrowableTemporary(conversion.Left, exclusive, true, false))
            {
                var failure = this.BorrowFailure(conversion.Left, scope, exclusive);
                if (failure == BindingFailure.Unsupported)
                {
                    return null;
                }

                this.Fail(conversion, failure);
                return false;
            }
        }
        else if (operation != ConversionBinding.ObjectUpcast && IsBarePlace(conversion.Left) && this.ProveCopy(source, conversion) != ConstraintProof.Proven)
        {
            this.FailAcquisition(conversion, BindingFailure.TransferRequired, conversion.Left);
            return false;
        }

        return operation != ConversionBinding.ObjectUpcast || this.ObjectUpcastResult(conversion, scope, source, target) is not null;
    }
}
