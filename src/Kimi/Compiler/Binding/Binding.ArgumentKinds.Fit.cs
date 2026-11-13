// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 10.2.2 (Literal fitting), 10.2 row 2: a literal value's class at `type`: fitted by value, or at ref/U an owner temporary
    // materialized and shared-borrowed, whose borrow's class applies.
    private static ArgumentAdaptation LiteralFitting(BoundType type)
        => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref } ? ArgumentAdaptation.CrossSemanticsBorrow : ArgumentAdaptation.Literal;

    // SPEC 4.3, 10.5.1: the one shape rule of a call argument's Tuple or array literal at a value Type or pattern: a Tuple literal fits a
    // Tuple of its arity; an array literal the fixed array of exactly its length (a fill: of its count), or the owned Array (not a fill).
    // A length an unbound slot decides is open in an open fit (OpenLength): judged after selection.
    private bool LiteralShapeAt(Koto literal, BoundType expected) => literal switch
    {
        TupleLiteralKoto tuple => TupleShapeAt(tuple, expected),
        ArrayLiteralKoto array => expected is { Kind: BoundTypeKind.FixedArray or BoundTypeKind.Array, Semantics: SemanticsKind.Owner } &&
            (array.FillLength is null || expected.Kind == BoundTypeKind.FixedArray) &&
            (expected.Kind != BoundTypeKind.FixedArray || this.OpenLength(expected) || (array.FillLength is null ? expected.Length == array.Elements.Count :
                array.FillCount is { } count && (count.IsConstant
                    ? expected.LengthExpression is null && expected.Length == count.Value
                    : ReferenceEquals(expected.LengthExpression, count)))),
        _ => false,
    };

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
            case var _ when this.TakenWhole(type) || (this.OpenFunction(type) && IsFunctionValued(kind, node)):
                return this.FitTakenWhole(part, node, type, out operation);
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

    // SPEC 10.8, 10.4 step 1 (PLAN G814; O-240 (A)): an argument at a parameter pattern that holds slots no evidence binds goes through
    // the closed fit itself at its open Type (SlotSolution.OpenType: the solution substituted, those slots kept), so every rule of the fit
    // applies and only a part such a slot takes whole is judged after selection (TakenWhole). The operation keeps no Type, so step 2
    // finds the open parameter identical to none (SPEC 10.8). Where the bound slots give the pattern no image (SPEC 4.4.2: a false
    // dependent length obligation eliminates the candidate) or the height limit refuses it (IMPL A.4, recorded at its site), the
    // candidate is inapplicable, as on the closed path.
    private CandidateApplicability FitOpenArgument(
        Koto argument, BoundType pattern, in SlotSolution solution, SlotSet unbound, BindingScope scope, Koto use, out BoundArgumentOperation operation)
    {
        if (solution.OpenType(pattern, out var open) != SubstitutionOutcome.Formed)
        {
            operation = default;
            return CandidateApplicability.Inapplicable;
        }

        var saved = this.openFit;
        this.openFit = (solution.SlotOwner, unbound);
        try
        {
            var fit = this.FitArgument(argument, open!, scope, use, out operation);
            operation = operation with { SourceType = null, ParameterType = null, AdaptedType = null };
            return fit;
        }
        finally
        {
            this.openFit = saved;
        }
    }

    // SPEC 10.8, 10.2.1 (PLAN G814; O-240 (A); the owner's O-251 (a)): while a candidate's open argument is fitted at its open Type
    // (SlotSolution.OpenType), whether `type`, or the referent of `ref/type`, is a form whose whole a slot no evidence binds decides: the
    // slot itself (a target projection, an `s/U` adaptation's cases), or a projection of one: the slot takes the part there whole, and its
    // fits are judged after selection. Every other form's closed parts fit as the closed fit requires (SPEC 10.8: other parts must fit):
    // a fixed array's kind and element (its length's own term is open, OpenLength), a Function Type's kind (OpenFunction), an `s/U`
    // application's target (the open substitution gives it). Read where a closed rule meets a Type: the fit's entry, a literal's
    // leaf, a range boundary, a join position, the ref/U gate and the Type relation's walk (a typed part's components).
    private bool TakenWhole(BoundType type)
    {
        if (this.openFit.Owner is not { } owner)
        {
            return false;
        }

        var target = LiteralInputType(type);
        return (OwnSlot(target, owner) is >= 0 and var slot && this.openFit.Slots.Contains(slot)) ||
            (target.Kind == BoundTypeKind.AssociatedProjection && MentionsSlots(target, owner, this.openFit.Slots));
    }

    // SPEC 10.8 (PLAN G814; O-251 (a)): whether `type` is a Function Type that names a slot no evidence binds, in an open fit: a part of a
    // function's kind meets it by call-signature matching, which judges the parts holding the slot after selection; a part of another
    // kind misfits its kind, as at a closed Function Type.
    private bool OpenFunction(BoundType type)
        => this.openFit.Owner is { } owner && type.Kind == BoundTypeKind.Function && MentionsSlots(type, owner, this.openFit.Slots);

    // Whether a part has a function's kind: an anonymous function, a function group, or a Closure, Function Item or Function value.
    private static bool IsFunctionValued(PartKind kind, Koto node)
        => kind is PartKind.Function or PartKind.Reference || node.BoundType is { Kind: BoundTypeKind.Closure or BoundTypeKind.FunctionItem or BoundTypeKind.Function };

    // SPEC 4.4.1, 10.8 (PLAN G814; O-251 (a)): whether a fixed array's length is a term an unbound length slot decides, in an open fit:
    // the length alone is open, judged after selection; the array's kind and element fit as the closed fit requires.
    private bool OpenLength(BoundType type)
        => this.openFit.Owner is { } owner && type.LengthExpression is { } length && MentionsLength(length, owner, this.openFit.Slots);

    // SPEC 10.8, 10.2.1, 10.4 step 1 (PLAN G814; O-240 (A)): a part that an open slot takes whole fits it before selection, with no
    // position compared there: each position a Tuple or array literal holds is Unjudged (AddUnjudged), so its count is a closed fit's
    // (DECISIONS BU-164). Its own class is its row's at the slot: Literal fitting for a literal kind (by value, or a borrowed temporary at
    // ref/), an erasure for a waiting function at a Function Type, else Exact.
    private CandidateApplicability FitTakenWhole(Koto part, Koto node, BoundType type, out BoundArgumentOperation operation)
    {
        var first = this.positionClasses.Count;
        this.AddUnjudged(node);
        var adaptation = this.KindOf(node) is PartKind.Literal or PartKind.Aggregate or PartKind.Join ? LiteralFitting(type)
            : type.Kind == BoundTypeKind.Function && this.IsWaitingCallable(node) ? ArgumentAdaptation.Erasure : ArgumentAdaptation.Exact;
        operation = new(part, null, type, ArgumentOperationKind.Value, adaptation, Positions: first..this.positionClasses.Count);
        return CandidateApplicability.Applicable;
    }

    // SPEC 10.2.1, 10.4 step 1: the positions a Tuple or array literal holds, in a closed fit's order (each element's own positions, then
    // the element; a fill's elements are none), as Unjudged.
    private void AddUnjudged(Koto literal)
    {
        var elements = literal switch
        {
            TupleLiteralKoto tuple => tuple.Elements,
            ArrayLiteralKoto { FillLength: null } array => array.Elements,
            _ => null,
        };
        for (var i = 0; elements is not null && i < elements.Count; i++)
        {
            this.AddUnjudged(KotoHelper.UnwrapParentheses(elements[i]));
            this.positionClasses.Add(ArgumentAdaptation.Unjudged);
        }
    }

    // SPEC 4.3, 4.6.2, 10.5.1: a Tuple or array literal or a mixed range fits a candidate's Type by its shape and parts, read only: each
    // element as an argument at its position (FitElement), so a nested literal fits a ref/(...) position as a borrowed temporary.
    private CandidateApplicability ProbeAggregateArgument(Koto source, BoundType expected, BindingScope scope)
    {
        source = KotoHelper.UnwrapParentheses(source);
        if (source is ArrayLiteralKoto array)
        {
            if (!this.LiteralShapeAt(array, expected))
            {
                return CandidateApplicability.Inapplicable;
            }

            var first = this.positionClasses.Count;
            for (var i = 0; i < array.Elements.Count; i++)
            {
                var result = this.FitElement(array.Elements[i], expected.Components[0], scope);
                if (result != CandidateApplicability.Applicable)
                {
                    return result;
                }
            }

            if (array.FillLength is not null)
            {
                this.positionClasses.RemoveRange(first, this.positionClasses.Count - first); // SPEC 10.4: a fill stays one argument.
            }

            return CandidateApplicability.Applicable;
        }

        if (source is TupleLiteralKoto tuple)
        {
            if (!this.LiteralShapeAt(tuple, expected))
            {
                return CandidateApplicability.Inapplicable;
            }

            for (var i = 0; i < tuple.Elements.Count; i++)
            {
                var result = this.FitElement(tuple.Elements[i], expected.Components[i], scope);
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

        throw new InvalidOperationException("A composite argument is a Tuple or array literal, a join or a mixed range (InternalInvariant).");
    }

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

        if (this.TakenWhole(expected))
        {
            return CandidateApplicability.Applicable; // SPEC 10.8: an open slot takes the boundary; judged after selection.
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
    // header filtering here; an Aggregate one by each part's shape and then its child positions; any other by each part's late Type
    // (LateFits), which applies no late write: the writes the late fits record are cleared per candidate, as no reader applies them
    // (TryPropagate computes its own).
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
        if (this.TakenWhole(type))
        {
            return true; // SPEC 10.8, 10.2.1: an open slot takes the position's parts; judged after selection.
        }

        var end = this.positionParts.Count;
        var kind = this.PositionKind(start, end, out _);
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

    // SPEC 10.4 step 1, 10.5.1: a Tuple or array literal argument is ranked by its own fit (Literal by value, as where its slot stays open,
    // or a borrowed temporary) and by each element position its fit visits (Positions, FitElement), recursively, a nested literal's own
    // fit among them; a fill's element is no position (it stays one argument). A join formed part by part records no position here:
    // SPEC 10.5.1 ranks each of its positions as one control-flow argument (PLAN G729).
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
            LiteralFitting(type),
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
        if (this.TakenWhole(type))
        {
            return true; // SPEC 10.8 (PLAN G814): an open slot takes the part; judged after selection.
        }

        var core = KotoHelper.UnwrapParentheses(part);
        if (core is RangeKoto range && IsMixedRange(range))
        {
            return this.KeepsBoundaryType(range.Start, type.Components[0], this.IndependentRangeBoundary(range.End, this.EndType)) &&
                this.KeepsBoundaryType(range.End, type.Components[1], this.IndependentRangeBoundary(range.Start, this.StartType));
        }

        if (core is not (TupleLiteralKoto or ArrayLiteralKoto))
        {
            return this.KindOf(core) != PartKind.Literal || ReferenceEquals(this.PartDefault(core, scope), type);
        }

        var elements = core is TupleLiteralKoto tuple ? tuple.Elements : ((ArrayLiteralKoto)core).Elements;
        for (var i = 0; i < elements.Count; i++)
        {
            if (ChildPattern(core, i, type) is not { } element || !this.HasTypeWithoutExpectation(elements[i], element, scope))
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
            operation = new(part, target, type, ArgumentOperationKind.Borrow, LiteralFitting(type), AdaptedType: literalBorrow);
            return CandidateApplicability.Applicable;
        }

        if (this.FitsLiteralPart(node, type, scope))
        {
            operation = new(part, null, type, ArgumentOperationKind.Value, LiteralFitting(type));
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
