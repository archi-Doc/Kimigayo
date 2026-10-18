// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Argument kinds (SPEC 10.5.1; DECISIONS BU-2): what each argument part is to the candidates of a call.
// - Responsibility: the kind of every argument part: a call argument and, recursively, an element of a Tuple or array literal
//   argument, a written boundary of a mixed range and a payload of a contextual `.Case(...)`. Literal: an unfitted literal (SPEC
//   12.3). Composite: a Tuple or array literal, a mixed range or a contextual Case, whose parts are classified instead. Waiting: an
//   anonymous function, a function reference that needs S, or a nested call the argument probe deferred, until the selected
//   candidate completes it. Typed: every other part.
// - Inputs: the part's syntax and binding state; the argument probe (BindIndependentArgument), under which a nested call that only an
//   outer expectation could type defers (DeferNestedCall) and its kind is recorded; arithmetic operands have their own probe (SPEC 13.3.4).
// - Outputs: KindOf; the preparation of a call's arguments (PrepareCallArguments) and of a Composite's parts; a Composite's fit to a
//   candidate's parameter Type (ProbeAggregateArgument); SolveSelected, the selected candidate of a call whose slot no evidence binds:
//   a nested call waits, or the Literal parts' defaults apply to it alone and its checks run once more (SPEC 10.2.1); SlotSolution
//   (Binding.ArgumentKinds.Solution.cs), one candidate's SPEC 10.8 solve state, whose operations take a part's evidence for the slots
//   (InferInput, InferAggregateCall for a Composite, InferClosureHeader, InferCallableSignatures), apply the defaults
//   (DefaultArguments), substitute the solution (Type) and judge the Constraints over it (Prove).
// - Invariants: a recorded kind and a Literal's default are keyed by syntax identity (SourceRef) and live for one pass; a deferred
//   nested call stays waiting after its completion, so its Type is never evidence for an outer slot (WasWaitingNestedCall); a Literal
//   is evidence only through the default recorded for the selected candidate.
// - Prohibitions: a consumer reads a part's kind from KindOf and tells only the forms of one kind apart (an anonymous function from a
//   reference) to fit them; no Koto-keyed table (COMPILER_ARCHITECTURE, Code rules: Syntax identity).
public sealed partial class Binding
{
    private readonly Dictionary<SourceRef, ArgumentFact> argumentKinds = new();
    private InvocationKoto? nestedArgumentProbe;
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

    // SPEC 10.5.1: the kind of an argument part as this pass has decided it so far.
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

        return IsWaitingCallable(part) || this.IsWaitingNestedCall(part) ? ArgumentKind.Waiting : ArgumentKind.Typed;
    }

    private bool WasWaitingNestedCall(Koto node)
        => KotoHelper.UnwrapParentheses(node) is InvocationKoto call && this.RecordedFact(call).Kind == ArgumentKind.Waiting;

    // SPEC 10.2.1: the default a Literal part takes for the selected candidate of its call, once DefaultArguments applied it.
    private BoundType? DefaultOf(Koto part)
        => this.RecordedFact(KotoHelper.UnwrapParentheses(part)) is { Kind: ArgumentKind.Literal, Default: { } literalDefault } ? literalDefault : null;

    private ArgumentFact RecordedFact(Koto node)
        => node.SyntaxOwner is { } owner && this.argumentKinds.TryGetValue(new(owner.Module, node.SyntaxId), out var fact) ? fact : default;

    private bool IsWaitingNestedCall(Koto node) => node.BoundType is null && this.WasWaitingNestedCall(node);

    // Independent argument evidence is prepared once for named and constraint-based calls.
    private bool PrepareCallArguments(InvocationKoto call, BindingScope scope, BindingSymbol? group = null, bool callableContext = false)
    {
        var unknownArgument = false;
        for (var i = 0; i < call.ArgumentNodes.Count; i++)
        {
            var argument = call.ArgumentNodes[i];
            if (KotoHelper.UnwrapParentheses(argument) is FunctionKoto { IsAnonymous: true })
            {
                continue; // A common-function argument supplies its fixed signature after selection.
            }

            if (this.KindOf(argument) == ArgumentKind.Composite)
            {
                unknownArgument |= !this.PrepareAggregateArgument(argument, scope);
                continue;
            }

            // A literal is fitted after selection, unless the parser kept it as a recovery: that argument fails here, so the call rests on its Error.
            if ((this.KindOf(argument) != ArgumentKind.Literal || argument.CodeContext.RecoveryCause(argument) is not null) &&
                this.BindIndependentArgument(argument, scope) is null)
            {
                if (this.IsWaitingNestedCall(argument))
                {
                    continue; // SPEC 10.5: only a determined outer candidate supplies the missing expectation.
                }

                if (KotoHelper.UnwrapParentheses(argument) is { BindingState: BindingState.Resolved, BoundSymbol: { Kind: BindingSymbolKind.Function } item })
                {
                    if ((callableContext || this.TakesCallableContext(group)) &&
                        !IndependentFunctionItem(item, this.UnboundMemberReference(argument), this.ReferenceDeclaringType(argument)) &&
                        !this.ExplicitReferenceValue(argument, item, scope))
                    {
                        continue; // SPEC 10.5: a waiting reference uses the selected fixed call signature once.
                    }

                    // SPEC 10.5: a single closed declaration supplies its own Item Type as generic argument evidence.
                    // A generic Callable contract checks that Type without erasing it to a common Function handle.
                    if (this.BindFunctionItem(argument, item, scope) is not null)
                    {
                        continue;
                    }
                }

                unknownArgument = true;
            }
        }

        return !unknownArgument;
    }

    // SPEC 10.5: first obtain independent evidence. Only an inner call that would need a result expectation waits; failures of
    // its own inputs or declarations are still diagnosed here. Parentheses preserve the same boundary.
    private BoundType? BindIndependentArgument(Koto argument, BindingScope scope)
    {
        var previous = this.nestedArgumentProbe;
        this.nestedArgumentProbe = KotoHelper.UnwrapParentheses(argument) as InvocationKoto;
        try
        {
            return this.BindNode(argument, scope);
        }
        finally
        {
            this.nestedArgumentProbe = previous;
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
        if (expected is not null || !(ReferenceEquals(this.nestedArgumentProbe, call) || ReferenceEquals(this.nestedOperandProbe, call)))
        {
            return false;
        }

        this.argumentKinds[SourceRef.Of(call)] = new(ArgumentKind.Waiting, null);
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
        var argument = evaluation.Expected is null && ReferenceEquals(this.nestedArgumentProbe, call);
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
            return actual;
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

    private bool PrepareAggregateArgument(Koto source, BindingScope scope)
    {
        source = KotoHelper.UnwrapParentheses(source);
        if (source is RangeKoto range && IsMixedRange(range))
        {
            // A typed boundary is bound on its own now; a literal-only one waits for the candidate (SPEC 4.6.2).
            return (range.Start is null || this.PrepareAggregateArgument(range.Start, scope)) &
                (range.End is null || this.PrepareAggregateArgument(range.End, scope));
        }

        var elements = source switch
        {
            ArrayLiteralKoto array => array.Elements,
            TupleLiteralKoto tuple => tuple.Elements,
            _ => null,
        };
        if (elements is null)
        {
            return NeedsEnumContext(source) ? this.PrepareContextualEnumInputs(source, scope)
                : this.KindOf(source) == ArgumentKind.Literal || this.BindNode(source, scope) is not null;
        }

        var valid = true;
        if (source is ArrayLiteralKoto { FillLength: { } length } fill)
        {
            valid = (fill.FillCount = this.BindLength(length, scope)) is not null;
        }

        for (var i = 0; i < elements.Count; i++)
        {
            valid &= this.PrepareAggregateArgument(elements[i], scope);
        }

        return valid;
    }

    // Read-only candidate probing: bind only the winning literal's aggregate shape.
    // General element expressions already have independent Types and cannot be retried.
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
            if (expected.Kind != BoundTypeKind.Tuple || expected.Components.Count != tuple.Elements.Count)
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

        if (NeedsEnumContext(source))
        {
            return this.ProbeContextualEnum(source, expected, scope);
        }

        if (this.KindOf(source) == ArgumentKind.Literal)
        {
            return this.FitsInputLiteral(source, expected, scope) ? CandidateApplicability.Applicable : CandidateApplicability.Inapplicable;
        }

        // SPEC 15.6.1: the structural part decides applicability; the element's Origin relations are judged when it is bound.
        return source.BoundType is not { } actual ? CandidateApplicability.Pending :
            this.FitsStructurallyAt(actual, expected, source) ? CandidateApplicability.Applicable : CandidateApplicability.Inapplicable;
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

    // A part's kind as the pass recorded it where its syntax does not give it, and the default a Literal part takes for the selected
    // candidate (SPEC 10.2.1).
    private readonly record struct ArgumentFact(ArgumentKind Kind, BoundType? Default);
}
