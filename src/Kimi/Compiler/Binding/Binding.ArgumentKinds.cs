// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Argument kinds (SPEC 10.5.1; DECISIONS BU-2): what each argument part is to the candidates of a call.
// - Responsibility: the kind of every argument part: a call argument and, recursively, an element of a Tuple or array literal
//   argument, a written boundary of a mixed range and a payload of a contextual `.Case(...)`. Literal: an unfitted literal (SPEC
//   12.3), or control flow whose result sources are all such (SPEC 14.9.1). Composite: a Tuple or array literal, a mixed range or a
//   contextual Case, whose parts are classified instead. Waiting: an anonymous function, a function reference that needs S, a nested
//   call the argument probe deferred, or control flow whose literals have only Never or unknown sources beside them or whose
//   source call the probe deferred, until the selected candidate completes it. Typed: every other part.
// - Inputs: the part's syntax and binding state; ControlFlow's survey of a control-flow part's result sources (SurveyResult); the
//   argument probe (BindIndependentArgument), which reaches the result sources of a control-flow part (UnderArgumentProbe) and under
//   which each part that is not a Composite is bound: a nested call that only an outer expectation could type defers (DeferNestedCall)
//   and its kind is recorded, and a function name stays unbound (AtArgumentPosition); arithmetic operands have their own probe (SPEC
//   13.3.4) and share the Literal control-flow predicate (ResultSurvey.AllLiteral).
// - Outputs: KindOf; the preparation of a call's arguments and of each part (PrepareArgument, PrepareComposite); the fit walk, a
//   part's fit to the parameter Type at its position (FitArgument, Binding.ArgumentKinds.Fit.cs) for call arguments, elements,
//   contextual Case payloads and value calls; SolveSelected, the selected candidate of a call whose slot no evidence binds: a nested
//   call waits, or the Literal parts' defaults apply to it alone and its checks run once more (SPEC 10.2.1); CompleteWaiting, the
//   selected candidate's waiting arguments and waiting elements at its F; SlotSolution (Binding.ArgumentKinds.Solution.cs), one
//   candidate's SPEC 10.8 solve state, whose evidence walk takes each part's evidence for the slots by its kind (InferArgument,
//   Binding.ArgumentKinds.Evidence.cs), applies the defaults (DefaultArguments), substitutes the solution (Type) and judges the
//   Constraints over it (Prove).
// - Invariants: a recorded kind and a Literal's default are keyed by syntax identity (SourceRef) and live for one pass; a deferred
//   part stays waiting after its completion, so its Type is never evidence for an outer slot (WasWaitingPart); a Literal
//   is evidence only through the default recorded for the selected candidate; a Never-typed part is no evidence (SPEC 10.8).
// - Prohibitions: a consumer reads a part's kind from KindOf and tells only the forms of one kind apart (an anonymous function from a
//   reference) to fit them; the call, element, contextual payload and value-call paths keep no evidence or fit dispatch of their own;
//   no Koto-keyed table (COMPILER_ARCHITECTURE, Code rules: Syntax identity).
public sealed partial class Binding
{
    private readonly Dictionary<SourceRef, ArgumentFact> argumentKinds = new();
    private Koto? argumentProbe;
    private InvocationKoto? nestedOperandProbe;

    private enum ArgumentKind : byte
    {
        Typed,
        Literal,
        Waiting,
        Composite,
    }

    private static bool IsAggregateArgument(Koto source)
        => KotoHelper.UnwrapParentheses(source) is ArrayLiteralKoto or TupleLiteralKoto { Elements.Count: > 0 } || IsMixedRange(source);

    // SPEC 4.6.2: a range with a literal-only and a typed written boundary. Like a tuple element, each literal-only boundary
    // takes the S or E of the candidate's parameter, while a typed boundary keeps its independent Type.
    private static bool IsMixedRange(Koto source)
        => KotoHelper.UnwrapParentheses(source) is RangeKoto range && !IsLiteralOnlyRange(range) &&
            ((range.Start is { } start && IsLiteralOnlyPosition(start)) || (range.End is { } end && IsLiteralOnlyPosition(end)));

    // SPEC 10.5.1: the kind of an argument part as this pass has decided it so far; an unbound part that PrepareArgument or the argument
    // probe classified (a control-flow part, a deferred nested call) by its recorded fact.
    private ArgumentKind KindOf(Koto part)
    {
        if (IsUnfittedLiteral(part))
        {
            return ArgumentKind.Literal;
        }

        if (IsAggregateArgument(part) || NeedsEnumContext(part))
        {
            return ArgumentKind.Composite;
        }

        if (IsWaitingCallable(part) || this.IsWaitingPart(part))
        {
            return ArgumentKind.Waiting;
        }

        var literal = part.BoundType is null && this.RecordedFact(KotoHelper.UnwrapParentheses(part)).Kind == ArgumentKind.Literal;
        return literal ? ArgumentKind.Literal : ArgumentKind.Typed;
    }

    // Whether the part waited for its call's selected candidate: a deferred part stays waiting after its completion, so its Type is
    // never evidence for an outer slot.
    private bool WasWaitingPart(Koto node) => this.RecordedFact(KotoHelper.UnwrapParentheses(node)).Kind == ArgumentKind.Waiting;

    // SPEC 10.2.1: the default a Literal part takes for the selected candidate of its call, once DefaultArguments applied it.
    private BoundType? DefaultOf(Koto part)
        => this.RecordedFact(KotoHelper.UnwrapParentheses(part)) is { Kind: ArgumentKind.Literal, Default: { } literalDefault } ? literalDefault : null;

    private ArgumentFact RecordedFact(Koto node)
        => node.SyntaxOwner is { } owner && this.argumentKinds.TryGetValue(new(owner.Module, node.SyntaxId), out var fact) ? fact : default;

    private bool IsWaitingPart(Koto node) => node.BoundType is null && this.WasWaitingPart(node);

    // SPEC 10.5.1, 14.9.2: whether the part is the argument probe's, or a result source of a control-flow expression that is: nested
    // results propagate the same way.
    private bool UnderArgumentProbe(Koto part)
    {
        for (Koto? node = part; this.argumentProbe is not null && node is not null and not FunctionKoto; node = ResultTarget(node))
        {
            if (ReferenceEquals(this.argumentProbe, node))
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 10.5.1, 12.3, 14.9.2: the default of a Literal part; a control-flow one takes its first literal source's, which every other
    // source must fit once it applies.
    private BoundType? PartDefault(Koto part, BindingScope scope)
    {
        if (ControlFlowExpression(part) is not { } flow)
        {
            return this.LiteralDefault(part);
        }

        return this.KindOf(part) == ArgumentKind.Literal ? this.SurveyResult(flow, scope).Default : null;
    }

    // SPEC 10.5.1, 12.3, 14.9.2: whether a Literal part fits the Type; a control-flow one when every literal source does.
    private bool FitsLiteralPart(Koto part, BoundType type, BindingScope scope)
        => ControlFlowExpression(part) is { } flow ? this.SurveyResult(flow, scope, type).Fits : this.FitsInputLiteral(part, type, scope);

    // SPEC 10.5: a function name that is a call argument or the part under the argument probe is not bound as its Item without an
    // expectation; PrepareArgument decides whether it is evidence or waits for S.
    private bool AtArgumentPosition(Koto node)
        => (TryNameRoot(node, out var root) && root.Parent is InvocationKoto) || ReferenceEquals(this.argumentProbe, node);

    // Independent argument evidence is prepared once for named and constraint-based calls.
    private bool PrepareCallArguments(InvocationKoto call, BindingScope scope, BindingSymbol? group = null, bool callableContext = false)
    {
        var valid = true;
        for (var i = 0; i < call.ArgumentNodes.Count; i++)
        {
            valid &= this.PrepareArgument(call.ArgumentNodes[i], scope, group, callableContext);
        }

        return valid;
    }

    // SPEC 10.5.1: prepares an argument part once per pass, by its kind. An anonymous function waits for a candidate's signature; a
    // Composite prepares its parts; control flow is classified by the survey of its result sources (SPEC 14.9.1); a Literal is fitted
    // after selection, unless the parser kept it as a recovery, which fails here so that the call rests on its Error; every other part
    // is bound under the argument probe, where a nested call that only an expectation could type waits, also as a result source of
    // a control-flow part, which then waits with it, and a function reference that needs S waits for the selected candidate's (10.5.3).
    private bool PrepareArgument(Koto part, BindingScope scope, BindingSymbol? group, bool callableContext)
    {
        if (KotoHelper.UnwrapParentheses(part) is FunctionKoto { IsAnonymous: true })
        {
            return true;
        }

        if (this.KindOf(part) == ArgumentKind.Composite)
        {
            return this.PrepareComposite(part, scope);
        }

        if (part.CodeContext.RecoveryCause(part) is null && ControlFlowExpression(part) is { } flow &&
            this.SurveyResult(flow, scope) is { Literal: true, Typed: false } survey)
        {
            // SPEC 10.5.1 (owner decision (1), DECISIONS BU-2): control flow is Literal when every result source is a literal; with only
            // Never or unknown sources beside its literals it needs an expectation or a default, so it waits. It is bound once its call
            // selects a candidate, as an anonymous function is. A recovered part is bound as any other.
            this.argumentKinds[SourceRef.Of(KotoHelper.UnwrapParentheses(part))] = new(survey.Other ? ArgumentKind.Waiting : ArgumentKind.Literal, null);
            return true;
        }

        if ((this.KindOf(part) == ArgumentKind.Literal && part.CodeContext.RecoveryCause(part) is null) ||
            this.BindIndependentArgument(part, scope) is not null || this.IsWaitingPart(part))
        {
            return true;
        }

        if (KotoHelper.UnwrapParentheses(part) is not { BindingState: BindingState.Resolved, BoundSymbol: { Kind: BindingSymbolKind.Function } item })
        {
            return false;
        }

        // SPEC 10.5: a single closed declaration supplies its own Item Type as generic argument evidence. A generic Callable contract
        // checks that Type without erasing it to a common Function handle.
        return ((callableContext || this.TakesCallableContext(group)) &&
            !IndependentFunctionItem(item, this.UnboundMemberReference(part), this.ReferenceDeclaringType(part)) &&
            !this.ExplicitReferenceValue(part, item, scope)) || this.BindFunctionItem(part, item, scope) is not null;
    }

    // SPEC 10.5: first obtain independent evidence. Only an inner call that would need a result expectation waits; failures of
    // its own inputs or declarations are still diagnosed here. Parentheses preserve the same boundary.
    private BoundType? BindIndependentArgument(Koto argument, BindingScope scope)
    {
        var previous = this.argumentProbe;
        this.argumentProbe = KotoHelper.UnwrapParentheses(argument);
        try
        {
            return this.BindNode(argument, scope);
        }
        finally
        {
            this.argumentProbe = previous;
        }
    }

    // SPEC 13.3.4: an arithmetic operand is typed independently as an argument is, but by its operator's own rule: a nested call
    // waits only when it is ambiguous or no default fixes its slots (SolveSelected), never because a default would.
    private BoundType? BindIndependentOperand(Koto operand, BindingScope scope)
    {
        var previous = this.nestedOperandProbe;
        this.nestedOperandProbe = KotoHelper.UnwrapParentheses(operand) as InvocationKoto;
        try
        {
            return this.BindNode(operand, scope);
        }
        finally
        {
            this.nestedOperandProbe = previous;
        }
    }

    private bool DeferNestedCall(InvocationKoto call, BoundType? expected)
    {
        if (expected is not null || !(this.UnderArgumentProbe(call) || ReferenceEquals(this.nestedOperandProbe, call)))
        {
            return false;
        }

        this.argumentKinds[SourceRef.Of(call)] = new(ArgumentKind.Waiting, null);
        if (this.argumentProbe is { } probe && this.UnderArgumentProbe(call))
        {
            this.argumentKinds[SourceRef.Of(probe)] = new(ArgumentKind.Waiting, null); // SPEC 14.9.2: so does its control-flow argument.
        }

        return true;
    }

    // SPEC 10.5.1, 10.2.1, 10.8: the selected candidate of a call whose slot no evidence binds, or of a nested call with another applicable
    // candidate. Under the argument probe the nested call waits when another candidate is applicable or its result holds an unsolved
    // slot. Otherwise the Literal parts' defaults apply to the selected candidate and its checks run once more; a failure reports that
    // candidate, never another, and a slot still unbound is the inference boundary. False when the call is complete: waiting or failed.
    private bool SolveSelected(ref CallEvaluation evaluation, int winner, BoundType? declaringType)
    {
        var call = evaluation.Call;
        var selected = (FunctionKoto)evaluation.Evaluated[winner].Symbol!.Declaration;
        var solution = this.Solution(evaluation, selected, declaringType);
        var argument = evaluation.Expected is null && this.UnderArgumentProbe(call);
        if (argument && (evaluation.Applicable > 1 || (evaluation.Evaluated[winner].Unsolved && solution.ResultHoldsUnsolved(evaluation.Mapping))))
        {
            this.DeferNestedCall(call, evaluation.Expected);
            Complete(call, null);
            return false;
        }

        if (!evaluation.Evaluated[winner].Unsolved)
        {
            return true;
        }

        if (solution.DefaultArguments(evaluation.Mapping, out var applied) is { } misfit)
        {
            this.CompleteDependent(call, misfit); // SPEC 12.3: the literal's own record, on which the call rests.
            return false;
        }

        var operations = evaluation.Operations.AsSpan(winner * evaluation.OperationStride, evaluation.OperationStride);
        if (applied && !this.CheckDefaulted(ref evaluation, winner, solution, operations))
        {
            return false;
        }

        if (!evaluation.Evaluated[winner].Unsolved)
        {
            return true;
        }

        if (this.DeferNestedCall(call, evaluation.Expected))
        {
            Complete(call, null);
        }
        else
        {
            // SPEC 10.8, 10.6: the selected candidate's unsolved slot is the inference boundary; the checks that need it are derived.
            this.FailUnboundSlots(
                call,
                selected,
                evaluation.Scope,
                evaluation.Scratch,
                evaluation.LengthArguments,
                evaluation.Mapping,
                operations,
                evaluation.Expected,
                evaluation.Self,
                evaluation.Origins,
                evaluation.Inputs,
                declaringType);
        }

        return false;
    }

    // SPEC 10.5, 10.5.4: completes the selected candidate's waiting arguments against its Types, and the waiting callables of its Tuple
    // and array literal arguments that bind an F, `ref/F` or `uniq/F` of it. A body or capture failure never retries another
    // candidate, and an inferred result never supplies outer signature evidence. False when one fails.
    private bool CompleteWaiting(ref CallEvaluation evaluation, int winner, bool[] completedWaiting, BoundType?[] waitingContexts)
    {
        var call = evaluation.Call;
        var selected = (FunctionKoto)evaluation.Evaluated[winner].Symbol!.Declaration;
        var solution = this.Solution(evaluation, selected, evaluation.Evaluated[winner].DeclaringType);
        var operations = evaluation.Operations.AsSpan(winner * evaluation.OperationStride, evaluation.OperationStride);
        var contract = this.activeRequirementContract;
        this.activeRequirementContract = null; // The arguments are expressions of the caller's context.
        try
        {
            for (var i = 0; i < call.ArgumentNodes.Count; i++)
            {
                var argument = call.ArgumentNodes[i];
                var pattern = selected.Parameters[evaluation.Mapping[i]].Type.BoundType!;
                if (this.KindOf(argument) == ArgumentKind.Waiting)
                {
                    completedWaiting[i] = true;
                    waitingContexts[i] = operations[i].ParameterType;
                    if (this.CompleteWaitingPart(argument, pattern, selected, operations[i].ParameterType, evaluation.Scope) is null)
                    {
                        return false;
                    }
                }
                else if (!this.CompleteWaitingElements(argument, pattern, solution))
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            this.activeRequirementContract = contract;
        }
    }

    // SPEC 10.5: a waiting part at F, `ref/F` or `uniq/F`: an anonymous function with the fixed expected call signature S (`context`), a
    // reference selected by S, or without S a value only when it needs none (FailUnfixedReference otherwise); any other at its Type.
    private BoundType? CompleteWaitingPart(Koto part, BoundType pattern, FunctionKoto selected, BoundType? context, BindingScope scope)
    {
        var slotType = pattern is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq } ? pattern.Components[0] : pattern;
        var concrete = slotType.Kind == BoundTypeKind.Parameter && ContainerSlot(CallSlotOwner(selected), slotType.Symbol!) >= 0;
        var node = KotoHelper.UnwrapParentheses(part);
        if (concrete && node is FunctionKoto { IsAnonymous: true } closure)
        {
            return this.BindClosureArgument(part, closure, scope, context);
        }

        if (!concrete || !IsWaitingFunctionReference(part))
        {
            return this.BindNode(part, scope, context);
        }

        return context is { } signature
            ? this.BindFunctionReference(part, node.BoundSymbol!, signature, scope, erase: false) : this.BindFunctionItem(part, node.BoundSymbol!, scope);
    }

    // SPEC 10.5.1: the elements of a Tuple or array literal argument at their positions: a waiting callable at an F of the candidate is
    // completed with that F's signature; the others are completed when the literal is bound at its Type.
    private bool CompleteWaitingElements(Koto part, BoundType pattern, in SlotSolution solution)
    {
        var node = KotoHelper.UnwrapParentheses(part);
        var elements = node switch
        {
            TupleLiteralKoto tuple when pattern.Kind == BoundTypeKind.Tuple && pattern.Components.Count == tuple.Elements.Count => tuple.Elements,
            ArrayLiteralKoto array when pattern.Kind is BoundTypeKind.Array or BoundTypeKind.FixedArray => array.Elements,
            _ => null,
        };
        for (var i = 0; elements is not null && i < elements.Count; i++)
        {
            var element = elements[i];
            var component = pattern.Components[pattern.Kind == BoundTypeKind.Tuple ? i : 0];
            var slotType = component is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq }
                ? component.Components[0] : component;
            if (!IsWaitingCallable(element) || slotType.Kind != BoundTypeKind.Parameter || ContainerSlot(solution.SlotOwner, slotType.Symbol!) < 0)
            {
                if (!this.CompleteWaitingElements(element, component, solution))
                {
                    return false;
                }

                continue;
            }

            var signature = this.TryCallable(slotType, this.ConstraintScope(solution.Function), out var required, out _) ? solution.Type(required) : null;
            if (this.CompleteWaitingPart(element, component, solution.Function, signature, solution.Scope) is null)
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 10.2.1: the selected candidate's checks run once more with its Literal parts' defaults; a failure is its own record and
    // never reselects.
    private bool CheckDefaulted(ref CallEvaluation evaluation, int winner, SlotSolution solution, Span<BoundArgumentOperation> operations)
    {
        var state = this.TryCandidate(
            solution,
            evaluation.ExplicitLengths,
            evaluation.Mapping,
            evaluation.Evaluated[winner].ArgumentMap,
            evaluation.Expected,
            operations,
            out _,
            out var unsolved,
            out _,
            out _,
            out var failure,
            out _);
        if (state is CandidateApplicability.Applicable or CandidateApplicability.Waiting)
        {
            evaluation.Evaluated[winner] = evaluation.Evaluated[winner] with { State = state, Unsolved = unsolved };
            return true;
        }

        var call = evaluation.Call;
        (this.rejectedCandidates ??= new(ReferenceEqualityComparer.Instance))[call] = [new(solution.Function, null, null, ConstraintFailure: failure)];
        this.FailWaitingSelection(call, state == CandidateApplicability.Pending ? BindingFailure.UnprovenConstraint : BindingFailure.NoApplicableCandidate);
        return false;
    }

    // Obtain independent tuple/fill Types; with `fitLiterals`, a Literal part gives its default once DefaultArguments applied it.
    private BoundType? IndependentAggregateType(Koto source, bool fitLiterals)
    {
        source = KotoHelper.UnwrapParentheses(source);
        if (source.BoundType is { } actual)
        {
            return ReferenceEquals(actual, BoundType.Never) ? null : actual; // SPEC 10.8: a Never part supplies no evidence.
        }

        if (source is RangeKoto range && IsMixedRange(range))
        {
            // SPEC 4.6.2: without an expected range, a literal-only boundary takes the integer Type of the typed one; its default (rule 3)
            // applies only to the selected candidate (SPEC 10.2.1).
            var start = this.IndependentRangeBoundary(range.Start, this.StartType);
            var end = this.IndependentRangeBoundary(range.End, this.EndType);
            start ??= this.DefaultOf(range.Start!) ?? this.RangeTyping(range.Start!, end);
            end ??= this.DefaultOf(range.End!) ?? this.RangeTyping(range.End!, start);
            if (start is null || end is null)
            {
                return null;
            }

            var symbol = range.IsInclusive ? this.Library.ClosedRange : this.Library.Range;
            return this.types.Intern(BoundTypeKind.Constructed, symbol, SemanticsKind.Owner, [start, end]);
        }

        if (source is ArrayLiteralKoto { FillCount: { } length } fill &&
            this.IndependentAggregateType(fill.Elements[0], fitLiterals) is { } elementType)
        {
            var count = length.IsConstant ? length.Value : 0;
            var expression = length.IsConstant ? null : length;
            return this.types.Intern(BoundTypeKind.FixedArray, null, SemanticsKind.Owner, [elementType], count, lengthExpression: expression);
        }

        if (source is TupleLiteralKoto tuple)
        {
            var elements = this.RentTypes(tuple.Elements.Count);
            try
            {
                for (var i = 0; i < tuple.Elements.Count; i++)
                {
                    if (this.IndependentAggregateType(tuple.Elements[i], fitLiterals) is not { } element)
                    {
                        return null;
                    }

                    elements[i] = element;
                }

                return this.types.Tuple(elements.AsSpan(0, tuple.Elements.Count));
            }
            finally
            {
                this.typeScratch.Return(elements, clearArray: true);
            }
        }

        return fitLiterals ? this.DefaultOf(source) : null;
    }

    // The independent Type of a written range boundary: Start or End when omitted, the read Type of a typed one, or null for a
    // literal-only one.
    private BoundType? IndependentRangeBoundary(Koto? boundary, BoundType omitted)
    {
        if (boundary is null)
        {
            return omitted;
        }

        var syntax = KotoHelper.UnwrapParentheses(boundary);
        return IsLiteralOnlyPosition(syntax) || syntax.BoundType is not { } actual ? null : this.ReadTypeReferent(actual, syntax) ?? actual;
    }

    // A literal-only boundary beside a typed one: that boundary's integer Type, or its T for FromEnd<T>, else i32 (SPEC 4.6.2).
    private BoundType OtherBoundaryType(Koto boundary, BoundType? other)
        => this.RangeTyping(boundary, other) ?? this.RangeTyping(boundary, BoundType.I32)!;

    // SPEC 4.6.2 rule 2: a literal-only boundary beside one with an established integer Type takes that Type, or its T for FromEnd<T>;
    // null when the other boundary has none.
    private BoundType? RangeTyping(Koto boundary, BoundType? other)
    {
        if ((other is { IsInteger: true } ? other : this.FromEndOffsetType(other)) is not { } integer)
        {
            return null;
        }

        return KotoHelper.UnwrapParentheses(boundary) is FromEndIndexKoto ? this.FromEndType(integer) : integer;
    }

    // SPEC 10.5.1, 4.6.2: a Composite's parts: each element of a Tuple or array literal is prepared as an argument at its position (a
    // function reference among them waits when it needs S), the payloads of a contextual Case likewise, and the written boundaries of a
    // mixed range on their own, since a boundary is never waiting.
    private bool PrepareComposite(Koto part, BindingScope scope)
    {
        var source = KotoHelper.UnwrapParentheses(part);
        if (source is RangeKoto range && IsMixedRange(range))
        {
            return (range.Start is null || IsUnfittedLiteral(range.Start) || this.BindNode(range.Start, scope) is not null) &
                (range.End is null || IsUnfittedLiteral(range.End) || this.BindNode(range.End, scope) is not null);
        }

        if (NeedsEnumContext(source))
        {
            return this.PrepareContextualEnumInputs(source, scope);
        }

        var elements = source is ArrayLiteralKoto array ? array.Elements : ((TupleLiteralKoto)source).Elements;
        var valid = true;
        if (source is ArrayLiteralKoto { FillLength: { } length } fill)
        {
            valid = (fill.FillCount = this.BindLength(length, scope)) is not null;
        }

        for (var i = 0; i < elements.Count; i++)
        {
            valid &= this.PrepareArgument(elements[i], scope, null, callableContext: true);
        }

        return valid;
    }

    // A part's kind as the pass recorded it where its syntax does not give it, and the default a Literal part takes for the selected
    // candidate (SPEC 10.2.1).
    private readonly record struct ArgumentFact(ArgumentKind Kind, BoundType? Default);
}
