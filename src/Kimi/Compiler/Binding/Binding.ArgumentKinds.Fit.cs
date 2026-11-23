// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 10.5.1 (G812 rulings 122, 123; ArgFlow ruling 146): the parts awaiting their call (a contextual Case, a named Call, a Case
    // construction) that the last argument fit kept, each with the Type at its position, the components down to it from the argument
    // and its checked verdict's classification there (WaitingPart). The outermost fit (FitArgument) starts it; a part's fit within it
    // (FitArgumentPart) adds to it, and the arms that add a step add it to these too. A value call's run on the split reads it right
    // after the fit (RunFitFails), and a construction's check below its payloads (FitConstructionAt): UnprovenWaiting.
    private readonly List<WaitingPart> waitingParts = new();

    // Where the current fit's waiting parts start: those below are an enclosing fit's, which a query inside it keeps
    // (BeginSelectionQuery sets it, EndSelectionQuery restores it).
    private int waitingBase;

    // DECISIONS BU-240 (the owner's O-392 (c), G812 rulings 109, 111; temporary, removed with (R)): the components down to the part
    // where the last argument fit failed (FitArgument's Inapplicable): a Tuple or array literal's element (ProbeAggregateArgument)
    // or a join's child position (FitsPosition), below the layer it is borrowed at (FitComposite), then, below a typed part, the
    // components its structural fit names (FitTyped), below a part awaiting its call, its checked verdict's (FitChecked,
    // CheckedVerdict.Misfit); none where the failure is the argument's own. A value call on the split reads it right after the fit and
    // blames that part's bindings (ArgumentPartFails).
    private ComponentPath argumentMisfit;

    // The component of `type` (an instance of the Case's enum) that payload `i` of Case `found` takes: its slot where the payload's
    // declared Type is the enum's slot, else -1 (the position no component path names: ComponentPath).
    private static int PayloadSlot(BoundType type, BoundEnumCase found, int i)
        => i < found.Payload.Length && found.Payload[i].BoundType is { Kind: BoundTypeKind.Parameter, Symbol: { } slot }
            ? ContainerSlot(type.Symbol!.Declaration, slot) : -1;

    // SPEC 10.4 step 1 (PLAN G924, G916): the class of a part checked against its expected Type, by its Materialization there: its one
    // owner temporary of U borrowed at `ref/U`, else Expected-Type fitting by value. The one class source of FitComposite and of an
    // open position's whole literal or join (TryCandidateCore).
    private static ArgumentAdaptation ExpectedTypeClass(BoundType? temporary)
        => temporary is not null ? ArgumentAdaptation.CrossSemanticsBorrow : ArgumentAdaptation.ExpectedTypeFitting;

    // The waiting parts met below a part's position (from `from` on), as the enclosing position sees them: within its component
    // `index`; none (the part's own position, -1) where the part is no component of it.
    private void WaitingWithin(int from, int index)
    {
        for (var i = from; i < this.waitingParts.Count; i++)
        {
            this.waitingParts[i] = this.waitingParts[i] with { Path = index < 0 ? default : this.waitingParts[i].Path.Within(index) };
        }
    }

    // SPEC 10.1 (ArgFlow ruling 146): the first waiting part the current fit kept (from waitingBase) whose selection at its Type is
    // Unproven, its own or a part's below it (CheckedVerdict.Selection): an obligation of the selected candidate there; else null.
    private WaitingPart? UnprovenWaiting()
    {
        for (var i = this.waitingBase; i < this.waitingParts.Count; i++)
        {
            if (this.waitingParts[i].Selection == CallSelection.Unproven)
            {
                return this.waitingParts[i];
            }
        }

        return null;
    }

    // SPEC 3.8 (G812 ruling 116): whether a literal's or a join position's own shape fits its Type; a shape that does not is the
    // position's own failure (ComponentPath.AtShape), as the structural fit's is.
    private bool ShapeFits(bool fits)
    {
        this.argumentMisfit = fits ? this.argumentMisfit : ComponentPath.AtShape;
        return fits;
    }

    // SPEC 10.1, 10.2, 10.5.1: whether an argument part fits a candidate's closed Type at its position, by the part's kind, and the
    // operation that adapts it (unset when it does not fit, but for a literal's range shape). A part that awaits its call as a Call or
    // Case is checked against the Type by its own resolution there, once per distinct Type (FitChecked; PLAN G924 AF-3b), and stays
    // Waiting for the selected candidate's completion, the fit publishing it (waitingParts); an anonymous function (Function) fits by
    // its signature and a function group (Reference) by conversion (SPEC 7.7.4). An Aggregate or Join fits by its shape and parts; a
    // Literal by its Type (SPEC 12.3), shared-borrowed at ref/T as an owner temporary; a typed part by erasure or the common
    // adaptation, structurally (SPEC 15.6.1). Fitting publishes nothing else. Every site that decides whether an awaiting part fits a
    // closed Type calls this one (FitsPosition, ProbeRangeBoundary, LateType).
    private CandidateApplicability FitArgument(Koto part, BoundType type, BindingScope scope, Koto use, out BoundArgumentOperation operation)
    {
        this.waitingParts.RemoveRange(this.waitingBase, this.waitingParts.Count - this.waitingBase);
        return this.FitArgumentPart(part, type, scope, use, out operation);
    }

    // FitArgument within an enclosing fit: a literal's element, a Case's payload, a join's late part.
    private CandidateApplicability FitArgumentPart(Koto part, BoundType type, BindingScope scope, Koto use, out BoundArgumentOperation operation)
    {
        var node = KotoHelper.UnwrapParentheses(part);
        operation = default;
        this.argumentMisfit = default;
        var kind = this.KindOf(node);
        switch (kind)
        {
            case var _ when node.BindingState == BindingState.Invalid && this.PartFailed(node):
                // SPEC 23.1.2 (DECISIONS BU-43, U2.1 r2-3, BU-79): a failed argument whose failure is final (Invalid: one that kept its
                // Type, or a nested call the defaults resolved alone whose failure is reported, its own record or one it rests on at any
                // depth, EndAwait) fits every candidate the evidence walk keeps, with the operation its kept Type, if any, takes at the
                // parameter by structure; its record stays its own, and the call rests on it. Rule W at the argument's top. A provisional
                // failure (Unresolved), such as an operator's anonymous function whose omitted header rests on its call, is completed by
                // the call and stays FitTyped's (C19's reason, kept; PLAN G924, ruling 115).
                var erases = node.BoundType is { Kind: BoundTypeKind.Closure or BoundTypeKind.FunctionItem } && type.Kind == BoundTypeKind.Function;
                operation = new(part, this.ArgumentTypeAt(node), type, ArgumentOperationKind.Value, erases ? ArgumentAdaptation.Erasure : ArgumentAdaptation.Exact);
                return CandidateApplicability.Applicable;
            case var _ when type.ContainsOpenSlot && this.PlacedAtOpenSlot(node, kind, type, scope):
                // SPEC 10.5.1, 10.8 (Binding.OpenSlots' place rule): a checked part that is not a nested call holds at a bare atom and
                // binds nothing; it is completed after selection. Ranked as an owner value by its form, an awaited callable by erasure.
                var placed = kind is PartKind.Function or PartKind.Reference ? ArgumentAdaptation.Erasure : this.CheckedClass(type, ownerResult: true);
                operation = new(part, null, type, ArgumentOperationKind.Value, placed);
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
                return this.FitChecked(part, node, kind, type, scope, out operation);
            default:
                return this.FitTyped(part, node, type, scope, use, out operation);
        }
    }

    // SPEC 4.3, 4.6.2, 10.5.1: a Tuple or array literal or a mixed range fits a candidate's Type by its shape and parts, read only: each
    // element as an argument at its position (FitElement), so a nested literal fits a ref/(...) position as a borrowed temporary.
    private CandidateApplicability ProbeAggregateArgument(Koto source, BoundType expected, BindingScope scope)
    {
        source = KotoHelper.UnwrapParentheses(source);
        if (source is ArrayLiteralKoto array)
        {
            // SPEC 4.3: a call-argument literal fits the candidate's fixed array of exactly its length, or its owned Array.
            var shaped = expected is { Kind: BoundTypeKind.FixedArray or BoundTypeKind.Array, Semantics: SemanticsKind.Owner } &&
                (array.FillLength is null || expected.Kind == BoundTypeKind.FixedArray) &&
                (expected.Kind != BoundTypeKind.FixedArray || (array.FillLength is null ? expected.Length == array.Elements.Count :
                    array.FillCount is { } count && (count.IsConstant
                        ? expected.LengthExpression is null && expected.Length == count.Value
                        : ReferenceEquals(expected.LengthExpression, count))));
            if (!this.ShapeFits(shaped))
            {
                return CandidateApplicability.Inapplicable;
            }

            var first = this.positionOperations.Count;
            for (var i = 0; i < array.Elements.Count; i++)
            {
                var met = this.waitingParts.Count;
                var result = this.FitElement(array.Elements[i], ExpectedChild(array, i, expected)!, scope);
                this.WaitingWithin(met, 0);
                if (result != CandidateApplicability.Applicable)
                {
                    this.argumentMisfit = this.argumentMisfit.Within(0);
                    return result;
                }
            }

            if (array.FillLength is not null)
            {
                this.positionOperations.RemoveRange(first, this.positionOperations.Count - first); // SPEC 10.4: a fill stays one argument.
            }

            return CandidateApplicability.Applicable;
        }

        if (source is TupleLiteralKoto tuple)
        {
            if (!this.ShapeFits(TupleShapeAt(tuple, expected)))
            {
                return CandidateApplicability.Inapplicable;
            }

            for (var i = 0; i < tuple.Elements.Count; i++)
            {
                var met = this.waitingParts.Count;
                var result = this.FitElement(tuple.Elements[i], ExpectedChild(tuple, i, expected)!, scope);
                this.WaitingWithin(met, i);
                if (result != CandidateApplicability.Applicable)
                {
                    this.argumentMisfit = this.argumentMisfit.Within(i);
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
            if (!this.ShapeFits(expected is { Kind: BoundTypeKind.Constructed, Semantics: SemanticsKind.Owner, Components.Count: 2 } &&
                ReferenceEquals(expected.Symbol, range.IsInclusive ? this.Library.ClosedRange : this.Library.Range)))
            {
                return CandidateApplicability.Inapplicable;
            }

            var start = this.ProbeRangeBoundary(range.Start, ExpectedChild(range, 0, expected)!, this.StartType, scope);
            return start != CandidateApplicability.Applicable ? start : this.ProbeRangeBoundary(range.End, ExpectedChild(range, 1, expected)!, this.EndType, scope);
        }

        throw new InvalidOperationException("A composite argument is a Tuple or array literal, a join or a mixed range (InternalInvariant).");
    }

    // SPEC 10.5.1, 10.4 step 1: a Tuple or array literal argument's element at its position, fitted as an argument (FitArgument), its
    // operation appended to the positions the candidate is ranked by and step 5 reads (positionOperations) after a nested literal's own
    // positions: a nested literal's own class is its own fit's (FitComposite), Literal by value or a borrowed temporary at ref/(...). A
    // waiting one that checks there is Applicable here; the selected candidate completes it.
    private CandidateApplicability FitElement(Koto part, BoundType type, BindingScope scope)
    {
        var fit = this.FitArgumentPart(part, type, scope, part, out var operation);
        if (fit is CandidateApplicability.Applicable or CandidateApplicability.Waiting)
        {
            this.positionOperations.Add(operation);
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

        // SPEC 4.6.2, 10.5.1 (PLAN G924 rule A): a boundary that awaits its call is checked against S or E as an argument part.
        var syntax = KotoHelper.UnwrapParentheses(boundary);
        if (this.AwaitsCall(syntax))
        {
            return this.FitArgumentPart(boundary, expected, scope, boundary, out _) == CandidateApplicability.Inapplicable
                ? CandidateApplicability.Inapplicable : CandidateApplicability.Applicable;
        }

        return syntax.BoundType is not { } actual ? CandidateApplicability.Pending :
            ReferenceEquals(this.ReadTypeReferent(actual, syntax) ?? actual, expected) ? CandidateApplicability.Applicable
            : CandidateApplicability.Inapplicable;
    }

    // SPEC 10.5.1, 10.5.4, 14.9.2 (DECISIONS BU-43): a join its call classifies fits position by position, each position taking the parts
    // its sources hold there (PositionKind, ChildPositions), as its evidence does: a failed part fits every candidate (SPEC 23.1.2); a
    // waiting part (a Call, Case, Function or Reference that awaits its call) fits as an argument part (FitArgument: SPEC 10.5.1 classifies
    // each source as that argument), at any position; an Aggregate position by each part's shape and then its child positions; a Typed
    // one by each typed part's own Type (FitsTypedPart), ranked once; any other part by its late Type (LateFits), which applies no late
    // write: the writes the
    // late fits record are dropped per candidate, as no reader applies them (TryPropagate computes its own).
    private bool FitsJoin(Koto join, BoundType type)
    {
        var (writes, adaptations) = (this.lateWrites.Count, this.lateAdaptations.Count);
        var start = this.positionParts.Count;
        this.AddResults(join);
        var fits = this.FitsPosition(start, type);
        this.ClosePosition(start);
        this.TruncateLate(writes, adaptations);
        return fits;
    }

    // SPEC 10.5.1 ¶2, 4.3.1 Places (PLAN G924 rule A): a Tuple or array literal part beside a typed part at a position is fitted by its own
    // parts at the position's Type, as an aggregate position is (each element still fitted to it), never whole.
    private bool FitsWholeAt(Koto part, BoundType type)
    {
        var start = this.OpenPosition(part);
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
                _ when this.IsWaiting(part) => this.FitArgumentPart(part, type, this.ConstraintScope(part), part, out _) != CandidateApplicability.Inapplicable,
                (PartKind.Aggregate, TupleLiteralKoto tuple) => this.ShapeFits(TupleShapeAt(tuple, type)),
                (PartKind.Aggregate, ArrayLiteralKoto array) => this.ShapeFits(this.ArrayShapeAt(array, type)),
                (PartKind.Typed, _) when common is not null && this.TypedPositionPart(part) is { } own =>
                    this.FitsTypedPart(part, own, type, common, ref positionClass),
                (PartKind.Typed, TupleLiteralKoto or ArrayLiteralKoto) when this.IsAggregatePart(part) => this.FitsWholeAt(part, type),
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
            // owner temporary is borrowed (10.2 row 2). The position's operation has no source: its parts are the sources of one
            // control-flow argument, each adapted after selection (an erased part's environment judged there, at its value), so step 5
            // (ErasedArgumentsEnvironment) sees none of the parts' erasures: the limit of SPEC 10.5.1's last paragraph.
            var adaptation = this.SameStructure(common, type) ? ArgumentAdaptation.Exact : positionClass ?? ArgumentAdaptation.CrossSemanticsBorrow;
            this.positionOperations.Add(new(null, common, type, ArgumentOperationKind.Value, adaptation));
        }

        var children = kind == PartKind.Aggregate && this.PartsShape(start, end) is not DictionaryLiteralKoto ? this.ChildPositions(start, end) : 0;
        for (var i = 0; i < children; i++)
        {
            var child = this.positionParts.Count;
            var met = this.waitingParts.Count;
            var fits = this.AddChildPosition(start, end, i, type) is { } element && this.FitsPosition(child, element);
            this.ClosePosition(child);
            this.WaitingWithin(met, type.Kind == BoundTypeKind.Tuple ? i : 0);
            if (!fits)
            {
                // ComponentPath (G812 rulings 112, 114): the failing child leaves the position at its component, a Tuple's element or
                // an array's element (ChildPattern), as a literal's element does (ProbeAggregateArgument), so the path is the argument's.
                this.argumentMisfit = this.argumentMisfit.Within(type.Kind == BoundTypeKind.Tuple ? i : 0);
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

    // SPEC 10.4 step 1, 10.5.1, 10.2 row 2 (PLAN G924 rules A and B): a Tuple or array literal, a fill, a mixed range or a join its call
    // classifies is checked against its expected Type (ExpectedTypeOf): its shape and parts at U, each element as an argument at its
    // position (FitElement), so a nested literal fits a ref/(...) position as a borrowed temporary. At `ref/U` it is one owner temporary
    // of U (Materialization), shared-borrowed once; by value it is fitted as it stands. It is ranked by its own fit (Expected-Type
    // fitting by value, or a borrowed temporary) and by each element position its fit visits (Positions, FitElement), recursively; a
    // fill's element is no position (it stays one argument). A join formed part by part records one class per typed position, as one
    // control-flow argument (FitsPosition; SPEC 10.5.1, PLAN G729).
    private CandidateApplicability FitComposite(Koto part, Koto node, BoundType type, BindingScope scope, out BoundArgumentOperation operation)
    {
        var temporary = this.Materialization(node, type, scope);
        var valueType = temporary ?? type;
        var first = this.positionOperations.Count;
        var met = this.waitingParts.Count;
        var result = this.ProbeAggregateArgument(node, valueType, scope);
        if (temporary is not null)
        {
            this.WaitingWithin(met, 0); // The literal is the layer's target, the owner temporary of U at `ref/U` (Materialization).
        }

        if (result != CandidateApplicability.Applicable)
        {
            this.positionOperations.RemoveRange(first, this.positionOperations.Count - first);
            this.argumentMisfit = temporary is not null ? this.argumentMisfit.Within(0) : this.argumentMisfit; // The literal is the layer's target.
        }

        operation = result != CandidateApplicability.Applicable ? default : new(
            part,
            valueType,
            type,
            temporary is not null ? ArgumentOperationKind.Borrow : ArgumentOperationKind.Value,
            ExpectedTypeClass(temporary),
            Positions: first..this.positionOperations.Count);
        return result;
    }

    private CandidateApplicability FitLiteral(Koto part, Koto node, BoundType type, BindingScope scope, out BoundArgumentOperation operation)
    {
        if (this.Materialization(node, type, scope) is { } target && this.FitsLiteralPart(node, target, scope))
        {
            // SPEC 10.2: a literal owner temporary is fitted to T, materialized once and shared-borrowed.
            var literalBorrow = this.types.Intern(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [target], origin: this.PlaceOrigin(node));
            operation = new(part, target, type, ArgumentOperationKind.Borrow, ArgumentAdaptation.CrossSemanticsBorrow, AdaptedType: literalBorrow);
            return CandidateApplicability.Applicable;
        }

        if (this.FitsLiteralPart(node, type, scope))
        {
            operation = new(part, null, type, ArgumentOperationKind.Value, ArgumentAdaptation.ExpectedTypeFitting);
            return CandidateApplicability.Applicable;
        }

        // The rejected candidate's range shape, which explains a failed selection.
        operation = this.PartDefault(node, scope) is { } literalType && DifferentRangeShapes(literalType, type)
            ? new(part, literalType, type, ArgumentOperationKind.Value, ArgumentAdaptation.ExpectedTypeFitting) : default;
        return CandidateApplicability.Inapplicable;
    }

    private CandidateApplicability FitTyped(Koto part, Koto node, BoundType type, BindingScope scope, Koto use, out BoundArgumentOperation operation)
    {
        operation = default;
        if (this.ArgumentTypeAt(node) is not { } actual)
        {
            return CandidateApplicability.Pending;
        }

        if (this.AwaitsPosition(node) && this.LateTypeOf(node, type) is { } late && this.FitsStructuralPartAt(late, type, use))
        {
            operation = new(part, late, type, ArgumentOperationKind.Value, ArgumentAdaptation.Exact); // SPEC 14.9.2 step 3: it takes the Type.
            return CandidateApplicability.Applicable;
        }

        var rows = this.comparisonRows.Count; // SPEC 15.6.1: a selection takes and drops the region conjuncts.
        var erases = this.ErasureSignatureFits(actual, type, node, this.comparisonRows, out _);
        this.DropComparisonRows(rows);
        if (erases)
        {
            // SPEC 7.7.4: a concrete Closure value converts to the parameter's common Function Type, as at an initialization; its Owned
            // environment is a Constraint of the candidate (ErasedArgumentsEnvironment, SPEC 10.1 step 5), its rest judged once the call is
            // selected (SPEC 10.5).
            operation = new(part, actual, type, ArgumentOperationKind.Value, ArgumentAdaptation.Erasure);
            return CandidateApplicability.Applicable;
        }

        if (!this.AdaptInput(
                node, FixedPattern.Open(type), actual, scope, null, null, out var adapted, out var quality, out var kind, recordPair: false, deferAcquisition: true) ||
            !this.FitsStructuralPartAt(adapted, type, use, out this.argumentMisfit))
        {
            return CandidateApplicability.Inapplicable;
        }

        operation = new(part, actual, type, kind, quality, AdaptedType: adapted);
        return CandidateApplicability.Applicable;
    }

    // A part awaiting its call that a fit met: the part, the Type at its position and the components down to it, with its checked
    // verdict's classification there and the components from it down to an unproven part (CheckedVerdict.Selection, Misfit).
    private readonly record struct WaitingPart(Koto Part, BoundType Type, ComponentPath Path, CallSelection Selection = CallSelection.Selected, ComponentPath Misfit = default)
    {
        // The components from the fitted argument down to the unproven part.
        internal ComponentPath Unproven => this.Misfit.Within(this.Path);
    }
}
