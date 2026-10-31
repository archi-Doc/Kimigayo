// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 10.1, 10.2, 10.5.1: whether an argument part fits a candidate's closed Type at its position, by the part's kind, and the
    // operation that adapts it (unset when it does not fit, but for a literal's range shape). Another part that awaits its call (a
    // Case, a Call) is checked only by the selected candidate; an anonymous function (Function) fits by its signature and a function
    // group (Reference) by conversion (SPEC 7.7.4). An Aggregate or Join fits by its shape and parts; a Literal by its Type (SPEC
    // 12.3), shared-borrowed at ref/T as an owner temporary; a typed part by erasure or the common adaptation, structurally (SPEC
    // 15.6.1). Fitting publishes nothing.
    private CandidateApplicability FitArgument(Koto part, BoundType type, BindingScope scope, Koto use, out BoundArgumentOperation operation)
    {
        var node = KotoHelper.UnwrapParentheses(part);
        operation = default;
        var kind = this.KindOf(node);
        switch (kind)
        {
            case PartKind.Function when this.AwaitsCall(node) && node is FunctionKoto closure:
                if (!this.ClosureSignatureFits(closure, type))
                {
                    return CandidateApplicability.Inapplicable;
                }

                operation = new(part, null, type, ArgumentOperationKind.Value, ArgumentAdaptation.Erasure);
                return CandidateApplicability.Applicable;
            case PartKind.Reference:
                if (type.Kind != BoundTypeKind.Function || !this.FunctionGroupFits(node, node.BoundSymbol!, type, scope))
                {
                    return CandidateApplicability.Inapplicable;
                }

                operation = new(part, null, type, ArgumentOperationKind.Value, ArgumentAdaptation.Erasure);
                return CandidateApplicability.Applicable;
            case PartKind.Aggregate or PartKind.Join:
                return this.FitComposite(part, node, type, scope, out operation);
            case PartKind.Literal:
                return this.FitLiteral(part, node, type, scope, out operation);
            case PartKind.Case or PartKind.Call when this.AwaitsCall(node):
                operation = new(part, null, type, ArgumentOperationKind.Value, ArgumentAdaptation.Exact);
                return CandidateApplicability.Waiting;
            default:
                return this.FitTyped(part, node, type, scope, use, out operation);
        }
    }

    // SPEC 4.3, 4.6.2, 10.5.1: a Tuple or array literal or a mixed range fits a candidate's Type by its shape and parts, read only.
    private CandidateApplicability ProbeAggregateArgument(Koto source, BoundType expected, BindingScope scope)
    {
        source = KotoHelper.UnwrapParentheses(source);
        if (source is ArrayLiteralKoto array)
        {
            // SPEC 4.3: a call-argument literal fits the candidate's fixed array of exactly its length, or its owned Array.
            if (expected is not { Kind: BoundTypeKind.FixedArray or BoundTypeKind.Array, Semantics: SemanticsKind.Owner } ||
                (array.FillLength is not null && expected.Kind != BoundTypeKind.FixedArray) ||
                (expected.Kind == BoundTypeKind.FixedArray && (array.FillLength is null ? expected.Length != array.Elements.Count :
                    array.FillCount is not { } count || (count.IsConstant
                        ? expected.LengthExpression is not null || expected.Length != count.Value
                        : !ReferenceEquals(expected.LengthExpression, count)))))
            {
                return CandidateApplicability.Inapplicable;
            }

            for (var i = 0; i < array.Elements.Count; i++)
            {
                var result = this.ProbeAggregateArgument(array.Elements[i], expected.Components[0], scope);
                if (result != CandidateApplicability.Applicable)
                {
                    return result;
                }
            }

            return CandidateApplicability.Applicable;
        }

        if (source is TupleLiteralKoto tuple)
        {
            if (!TupleShapeAt(tuple, expected))
            {
                return CandidateApplicability.Inapplicable;
            }

            for (var i = 0; i < tuple.Elements.Count; i++)
            {
                var result = this.ProbeAggregateArgument(tuple.Elements[i], expected.Components[i], scope);
                if (result != CandidateApplicability.Applicable)
                {
                    return result;
                }
            }

            return CandidateApplicability.Applicable;
        }

        if (ControlFlowExpression(source) is { } join && this.KindOf(source) == PartKind.Join)
        {
            return this.FitsJoin(join, expected) ? CandidateApplicability.Applicable : CandidateApplicability.Inapplicable;
        }

        if (source is RangeKoto range && IsMixedRange(range))
        {
            // SPEC 4.6.2: the candidate's range of this shape supplies each literal-only boundary's S or E; a typed boundary
            // must supply exactly its Type argument, through a value read.
            if (expected is not { Kind: BoundTypeKind.Constructed, Semantics: SemanticsKind.Owner, Components.Count: 2 } ||
                !ReferenceEquals(expected.Symbol, range.IsInclusive ? this.Library.ClosedRange : this.Library.Range))
            {
                return CandidateApplicability.Inapplicable;
            }

            var start = this.ProbeRangeBoundary(range.Start, expected.Components[0], this.StartType, scope);
            return start != CandidateApplicability.Applicable ? start : this.ProbeRangeBoundary(range.End, expected.Components[1], this.EndType, scope);
        }

        // SPEC 10.5.1: every other part is an argument at its element position; a waiting one is checked by the selected candidate.
        var fit = this.FitArgument(source, expected, scope, source, out _);
        return fit == CandidateApplicability.Waiting ? CandidateApplicability.Applicable : fit;
    }

    private CandidateApplicability ProbeRangeBoundary(Koto? boundary, BoundType expected, BoundType omitted, BindingScope scope)
    {
        if (boundary is null)
        {
            return ReferenceEquals(expected, omitted) ? CandidateApplicability.Applicable : CandidateApplicability.Inapplicable;
        }

        if (IsUnfittedLiteral(boundary))
        {
            return this.FitsInputLiteral(boundary, expected, scope) ? CandidateApplicability.Applicable : CandidateApplicability.Inapplicable;
        }

        var syntax = KotoHelper.UnwrapParentheses(boundary);
        return syntax.BoundType is not { } actual ? CandidateApplicability.Pending :
            ReferenceEquals(this.ReadTypeReferent(actual, syntax) ?? actual, expected) ? CandidateApplicability.Applicable
            : CandidateApplicability.Inapplicable;
    }

    // SPEC 10.5.1, 10.5.4, 14.9.2 (DECISIONS BU-43): a join its call classifies fits position by position, each position taking the parts
    // its sources hold there (PositionKind, ChildPositions), as its evidence does: a position that waits (no typed part, one awaiting its
    // call) is checked by the selected candidate, only a waiting anonymous function's header filtering here; an Aggregate one by each
    // part's shape and then its child positions; any other by each part's late Type (LateFits), which applies no late write: the writes
    // the late fits record are cleared per candidate, as no reader applies them (TryPropagate computes its own).
    private bool FitsJoin(Koto join, BoundType type)
    {
        this.lateWrites.Clear();
        this.lateAdaptations.Clear();
        var start = this.positionParts.Count;
        this.AddResults(join);
        var fits = this.FitsPosition(start, type);
        this.ClosePosition(start);
        return fits;
    }

    private bool FitsPosition(int start, BoundType type)
    {
        var end = this.positionParts.Count;
        var kind = this.PositionKind(start, end, out _);
        for (var i = start; i < end; i++)
        {
            var part = this.positionParts[i];
            var fits = (kind, KotoHelper.UnwrapParentheses(part)) switch
            {
                (PartKind.Call, FunctionKoto closure) => !this.AwaitsCall(closure) || this.ClosureSignatureFits(closure, type),
                (PartKind.Call, _) => true,
                (PartKind.Aggregate, TupleLiteralKoto tuple) => TupleShapeAt(tuple, type),
                (PartKind.Aggregate, ArrayLiteralKoto array) => this.ArrayShapeAt(array, type),
                _ => this.LateFits(part, type), // A Dictionary part whole (BU-43), its key and value not walked.
            };
            if (!fits)
            {
                return false;
            }
        }

        var children = kind == PartKind.Aggregate && this.PartsShape(start, end) is not DictionaryLiteralKoto ? this.ChildPositions(start, end) : 0;
        for (var i = 0; i < children; i++)
        {
            var child = this.positionParts.Count;
            var fits = this.AddChildPosition(start, end, i, type) is { } element && this.FitsPosition(child, element);
            this.ClosePosition(child);
            if (!fits)
            {
                return false;
            }
        }

        return true;
    }

    private CandidateApplicability FitComposite(Koto part, Koto node, BoundType type, BindingScope scope, out BoundArgumentOperation operation)
    {
        var borrow = type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref };
        var valueType = borrow ? type.Components[0] : type;
        var result = this.ProbeAggregateArgument(node, valueType, scope);
        operation = result != CandidateApplicability.Applicable ? default : new(
            part,
            valueType,
            type,
            borrow ? ArgumentOperationKind.Borrow : ArgumentOperationKind.Value,
            borrow ? ArgumentAdaptation.CrossSemanticsBorrow : ArgumentAdaptation.Literal);
        return result;
    }

    private CandidateApplicability FitLiteral(Koto part, Koto node, BoundType type, BindingScope scope, out BoundArgumentOperation operation)
    {
        var target = type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref, Components.Count: 1 } ? type.Components[0] : null;
        if (target is not null &&
            (ScalarTypes.Supports(target) || this.TakesGenericLiterals(target, scope) || this.IsSyntaxPositionType(target)) &&
            this.FitsLiteralPart(node, target, scope))
        {
            // SPEC 10.2: a literal owner temporary is fitted to T, materialized once and shared-borrowed.
            var literalBorrow = this.types.Intern(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [target], origin: this.PlaceOrigin(node));
            operation = new(part, target, type, ArgumentOperationKind.Borrow, ArgumentAdaptation.CrossSemanticsBorrow, AdaptedType: literalBorrow);
            return CandidateApplicability.Applicable;
        }

        if (this.FitsLiteralPart(node, type, scope))
        {
            operation = new(part, null, type, ArgumentOperationKind.Value, ArgumentAdaptation.Literal);
            return CandidateApplicability.Applicable;
        }

        // The rejected candidate's range shape, which explains a failed selection.
        operation = this.PartDefault(node, scope) is { } literalType && DifferentRangeShapes(literalType, type)
            ? new(part, literalType, type, ArgumentOperationKind.Value, ArgumentAdaptation.Literal) : default;
        return CandidateApplicability.Inapplicable;
    }

    private CandidateApplicability FitTyped(Koto part, Koto node, BoundType type, BindingScope scope, Koto use, out BoundArgumentOperation operation)
    {
        operation = default;
        if (node.BoundType is not { } actual)
        {
            return CandidateApplicability.Pending;
        }

        if (this.AwaitsPosition(node) && this.LateTypeOf(node, type) is { } late && this.FitsStructurallyAt(late, type, use))
        {
            operation = new(part, late, type, ArgumentOperationKind.Value, ArgumentAdaptation.Exact); // SPEC 14.9.2 step 3: it takes the Type.
            return CandidateApplicability.Applicable;
        }

        if (this.ErasureSignatureFits(actual, type, node))
        {
            // SPEC 7.7.4: a concrete Closure value converts to the parameter's common Function Type, as at an initialization; its receiver
            // and Owned conditions are judged once the call is selected (SPEC 10.5).
            operation = new(part, actual, type, ArgumentOperationKind.Value, ArgumentAdaptation.Erasure);
            return CandidateApplicability.Applicable;
        }

        if (!this.AdaptInput(
                node, type, actual, scope, null, null, out var adapted, out var quality, out var kind, recordPair: false, deferAcquisition: true) ||
            !this.FitsStructurallyAt(adapted, type, use))
        {
            return CandidateApplicability.Inapplicable;
        }

        operation = new(part, actual, type, kind, quality, AdaptedType: adapted);
        return CandidateApplicability.Applicable;
    }
}
