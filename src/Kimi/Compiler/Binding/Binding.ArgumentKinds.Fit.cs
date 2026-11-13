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
            case var _ when node.BoundType is not null && this.PartFailed(node):
                // SPEC 23.1.2 (DECISIONS BU-43, U2.1 r2-3): a failed argument that kept its Type fits every candidate the evidence walk
                // keeps, with the operation that Type takes at the parameter by structure; its record stays its own, and the call rests
                // on it. Rule W (DECISIONS BU-79) at the argument's top, one rule with FitsPosition's failed-part row; it takes only a
                // part that kept its Type (C19).
                var erases = node.BoundType is { Kind: BoundTypeKind.Closure or BoundTypeKind.FunctionItem } && type.Kind == BoundTypeKind.Function;
                operation = new(part, node.BoundType, type, ArgumentOperationKind.Value, erases ? ArgumentAdaptation.Erasure : ArgumentAdaptation.Exact);
                return CandidateApplicability.Applicable;
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

    // SPEC 4.3, 4.6.2, 10.5.1: a Tuple or array literal or a mixed range fits a candidate's Type by its shape and parts, read only: each
    // element as an argument at its position (FitElement), so a nested literal fits a ref/(...) position as a borrowed temporary.
    private CandidateApplicability ProbeAggregateArgument(Koto source, BoundType expected, BindingScope scope)
    {
        source = KotoHelper.UnwrapParentheses(source);
        var parts = new AggregateParts(source);
        if (parts.Exist)
        {
            if (!this.AggregateShapeAt(source, expected))
            {
                return CandidateApplicability.Inapplicable;
            }

            var first = this.positionClasses.Count;
            for (var i = 0; i < parts.Count; i++)
            {
                var result = this.FitElement(parts[i], ChildPattern(source, i, expected)!, scope);
                if (result != CandidateApplicability.Applicable)
                {
                    return result;
                }
            }

            if (source is ArrayLiteralKoto { FillLength: not null })
            {
                this.positionClasses.RemoveRange(first, this.positionClasses.Count - first); // SPEC 10.4: a fill stays one argument.
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

        throw new InvalidOperationException("A composite argument is a Tuple or array literal, a join or a mixed range (InternalInvariant).");
    }

    // SPEC 3.8, 4.3, 10.5.1: whether an aggregate literal argument (AggregateParts) has the candidate's shape: a Tuple of its length
    // (TupleShapeAt); the owned fixed array of exactly its length or its owned Array, a fill only a fixed array of its count.
    private bool AggregateShapeAt(Koto source, BoundType expected) => source switch
    {
        TupleLiteralKoto tuple => TupleShapeAt(tuple, expected),
        ArrayLiteralKoto array => expected is { Kind: BoundTypeKind.FixedArray or BoundTypeKind.Array, Semantics: SemanticsKind.Owner } &&
            (array.FillLength is null || expected.Kind == BoundTypeKind.FixedArray) &&
            (expected.Kind != BoundTypeKind.FixedArray || (array.FillLength is null ? expected.Length == array.Elements.Count :
                array.FillCount is { } count && (count.IsConstant
                    ? expected.LengthExpression is null && expected.Length == count.Value
                    : ReferenceEquals(expected.LengthExpression, count)))),
        _ => throw new InvalidOperationException("An aggregate literal argument is a Tuple or array literal (InternalInvariant)."),
    };

    // SPEC 10.5.1, 10.4 step 1: a Tuple or array literal argument's element at its position, fitted as an argument (FitArgument), its
    // adaptation class appended to the positions the candidate is ranked by (positionClasses) after a nested literal's own positions:
    // a nested literal's own class is its own fit's (FitComposite), Literal by value or a borrowed temporary at ref/(...). A waiting one
    // is checked by the selected candidate (Applicable here).
    private CandidateApplicability FitElement(Koto part, BoundType type, BindingScope scope)
    {
        var fit = this.FitArgument(part, type, scope, part, out var operation);
        if (fit is CandidateApplicability.Applicable or CandidateApplicability.Waiting)
        {
            this.positionClasses.Add(operation.Adaptation);
        }

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
    // its sources hold there (PositionKind, ChildPositions), as its evidence does: a failed part fits every candidate (SPEC 23.1.2); a
    // position that waits (no typed part, one awaiting its call) is checked by the selected candidate, only a waiting anonymous function's
    // header filtering here; an Aggregate one by each part's shape and then its child positions; a Typed one by each typed part's own
    // Type (FitsTypedPart), ranked once; any other part by its late Type (LateFits), which applies no late write: the writes the
    // late fits record are cleared per candidate, as no reader applies them (TryPropagate computes its own).
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
        var kind = this.PositionKind(start, end, out var common);
        ArgumentAdaptation? positionClass = null;
        for (var i = start; i < end; i++)
        {
            var part = this.positionParts[i];
            var fits = (kind, KotoHelper.UnwrapParentheses(part)) switch
            {
                _ when this.PartFailed(part) => true, // SPEC 23.1.2: what only a failed part would cause is dependent.
                (PartKind.Call, FunctionKoto closure) => !this.AwaitsCall(closure) || this.ClosureSignatureFits(closure, type),
                (PartKind.Call, _) => true,
                (PartKind.Aggregate, TupleLiteralKoto tuple) => TupleShapeAt(tuple, type),
                (PartKind.Aggregate, ArrayLiteralKoto array) => this.ArrayShapeAt(array, type),
                (PartKind.Typed, _) when common is not null && this.TypedPositionPart(part) is { } own =>
                    this.FitsTypedPart(part, own, type, common, ref positionClass),
                _ => this.LateFits(part, type), // A Dictionary part whole (BU-43), its key and value not walked.
            };
            if (!fits)
            {
                return false;
            }
        }

        if (common is not null && kind == PartKind.Typed)
        {
            // SPEC 10.4 step 1, 10.2.2: one class per typed position of a join formed part by part (a row iff the position is Typed with
            // a common Type), its common Type's at the position's Type: Exact where no operation is needed (Exact concerns Types);
            // otherwise a supplying part's (FitsTypedPart); with no supplying part every part was value-read into a read Type, whose
            // owner temporary is borrowed (10.2 row 2).
            this.positionClasses.Add(this.SameStructure(common, type) ? ArgumentAdaptation.Exact : positionClass ?? ArgumentAdaptation.CrossSemanticsBorrow);
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

    // SPEC 10.5.1 last paragraph, 10.2, 10.4 step 1 (PLAN G729): a typed part of a join's typed position, one source of one control-flow
    // argument, fits at the position's Type by the typed row (FitTyped: its own Type whole, its Place-ness and Loans its own), as its
    // evidence adapts it whole (AdaptPart): a typed Tuple or array literal part is typed whole (BU-43), no element fitted or ranked.
    // The position is ranked once, by the class of a temporary of its common Type there (10.2.2), read from a part whose own Type has
    // the common Type's structure (it supplies that Type, 14.9.2 step 2): every such part that fits takes that one class, so the part
    // read is order-free; a part's value read into the common Type is the join's, not the call's.
    private bool FitsTypedPart(Koto part, BoundType own, BoundType type, BoundType common, ref ArgumentAdaptation? positionClass)
    {
        if (this.FitTyped(part, KotoHelper.UnwrapParentheses(part), type, this.ConstraintScope(part), part, out var operation) != CandidateApplicability.Applicable)
        {
            return false;
        }

        if (positionClass is null && this.SameStructure(own, common))
        {
            positionClass = operation.Adaptation;
        }

        return true;
    }

    // SPEC 10.4 step 1, 10.5.1: a Tuple or array literal argument is ranked by its own fit (Literal by value, as where its slot stays open,
    // or a borrowed temporary) and by each element position its fit visits (Positions, FitElement), recursively, a nested literal's own
    // fit among them; a fill's element is no position (it stays one argument). A join formed part by part records one class per typed
    // position, as one control-flow argument (FitsPosition; SPEC 10.5.1, PLAN G729).
    private CandidateApplicability FitComposite(Koto part, Koto node, BoundType type, BindingScope scope, out BoundArgumentOperation operation)
    {
        var borrow = type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref };
        var valueType = borrow ? type.Components[0] : type;
        var first = this.positionClasses.Count;
        var result = this.ProbeAggregateArgument(node, valueType, scope);
        if (result == CandidateApplicability.Applicable && borrow && this.KindOf(node) == PartKind.Aggregate &&
            this.TemporaryTarget(node, type, scope) is null && !this.HasTypeWithoutExpectation(node, valueType, scope))
        {
            result = CandidateApplicability.Inapplicable; // A fill or mixed range at `ref/U` whose own Type is not U (K2, K3).
        }

        if (result != CandidateApplicability.Applicable)
        {
            this.positionClasses.RemoveRange(first, this.positionClasses.Count - first);
        }

        operation = result != CandidateApplicability.Applicable ? default : new(
            part,
            valueType,
            type,
            borrow ? ArgumentOperationKind.Borrow : ArgumentOperationKind.Value,
            borrow ? ArgumentAdaptation.CrossSemanticsBorrow : ArgumentAdaptation.Literal,
            Positions: first..this.positionClasses.Count);
        return result;
    }

    // SPEC 10.2 row 2, 12.3.1, 14.9.2 step 3 (PLAN G585; the owner's O-201 (1) (a), (2) (a)): the Type U of the owner temporary that a
    // source at `ref/U` is, materialized once and shared-borrowed, the one rule of every position (an argument's fit, BindAndAdaptNode
    // at any expected Type, the evidence walk, a value call's solve): a Tuple or array literal (a fill is none, 4.3.3), an untyped literal
    // or a literal-only expression (12.3; a mixed range is none, 4.6.2), or an unbound selection, `do` or `loop` with such a source,
    // directly or nested (its survey), whose value is the one temporary at its position. The source is fitted to U as at an expected U,
    // so a misfit is its own record at U; a call's fit tests it (FitLiteral). A form that delivers its operand (a label, an expression
    // body) passes the position to it; null at any other Type or part.
    private BoundType? TemporaryTarget(Koto part, BoundType? type, BindingScope scope)
    {
        var core = KotoHelper.UnwrapParentheses(part);
        if (type is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref, Components: [var target] } || DeliveredOperand(core) is not null)
        {
            return null;
        }

        return core is TupleLiteralKoto { Elements.Count: > 0 } or ArrayLiteralKoto { FillLength: null } || IsUnfittedLiteral(core) ||
            (core.BoundType is null && ControlFlowExpression(core) is { } join && this.SurveyResult(join, scope).Literal) ? target : null;
    }

    // SPEC 10.2.1, 12.3.1, 4.3.3, 4.6.2 rules 2 and 3 (PLAN G585, K's K2 and K3): a composite at `ref/U` that is no temporary of U
    // (TemporaryTarget: a fill, a mixed range) takes nothing from U: it is typed with no expected Type, as binding it with none types it
    // (BindAndAdaptNode, a let), and fits only where that Type is U. Whether `part` has the Type `type` so, read only, where the probe
    // fitted its shape and parts (ProbeAggregateArgument): a Literal part at any depth keeps its default (PartDefault), a literal-only
    // boundary takes its other boundary's Type, else i32 (OtherBoundaryType); a typed, waiting or failed part is the probe's.
    private bool HasTypeWithoutExpectation(Koto part, BoundType type, BindingScope scope)
    {
        var core = KotoHelper.UnwrapParentheses(part);
        if (core is RangeKoto range && IsMixedRange(range))
        {
            return this.KeepsBoundaryType(range.Start, type.Components[0], this.IndependentRangeBoundary(range.End, this.EndType)) &&
                this.KeepsBoundaryType(range.End, type.Components[1], this.IndependentRangeBoundary(range.Start, this.StartType));
        }

        var parts = new AggregateParts(core);
        if (!parts.Exist)
        {
            return this.KindOf(core) != PartKind.Literal || ReferenceEquals(this.PartDefault(core, scope), type);
        }

        for (var i = 0; i < parts.Count; i++)
        {
            if (ChildPattern(core, i, type) is not { } element || !this.HasTypeWithoutExpectation(parts[i], element, scope))
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 4.6.2 rules 2 and 3: whether a written boundary that is literal-only has the Type `type` beside the other boundary's Type.
    private bool KeepsBoundaryType(Koto? boundary, BoundType type, BoundType? other)
        => boundary is null || !IsLiteralOnlyPosition(boundary) || ReferenceEquals(this.OtherBoundaryType(boundary, other), type);

    private CandidateApplicability FitLiteral(Koto part, Koto node, BoundType type, BindingScope scope, out BoundArgumentOperation operation)
    {
        if (this.TemporaryTarget(node, type, scope) is { } target && this.FitsLiteralPart(node, target, scope))
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

        if (this.AwaitsPosition(node) && this.LateTypeOf(node, type) is { } late && this.FitsStructuralPartAt(late, type, use))
        {
            operation = new(part, late, type, ArgumentOperationKind.Value, ArgumentAdaptation.Exact); // SPEC 14.9.2 step 3: it takes the Type.
            return CandidateApplicability.Applicable;
        }

        if (this.ErasureSignatureFits(actual, type, node, out _))
        {
            // SPEC 7.7.4: a concrete Closure value converts to the parameter's common Function Type, as at an initialization; its receiver
            // and Owned conditions are judged once the call is selected (SPEC 10.5).
            operation = new(part, actual, type, ArgumentOperationKind.Value, ArgumentAdaptation.Erasure);
            return CandidateApplicability.Applicable;
        }

        if (!this.AdaptInput(
                node, FixedPattern.Open(type), actual, scope, null, null, out var adapted, out var quality, out var kind, recordPair: false, deferAcquisition: true) ||
            !this.FitsStructuralPartAt(adapted, type, use))
        {
            return CandidateApplicability.Inapplicable;
        }

        operation = new(part, actual, type, kind, quality, AdaptedType: adapted);
        return CandidateApplicability.Applicable;
    }
}
