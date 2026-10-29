// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Argument kinds (SPEC 10.5.1; DECISIONS BU-2, BU-43): what each argument part is to the candidates of a call.
// - Responsibility: the kind of every argument part: a call argument and, recursively, an element of a Tuple or array literal
//   argument, a written boundary of a mixed range and a payload of a Case construction. Literal: an unfitted literal (SPEC 12.3), or
//   control flow whose result sources other than Never are all such, at least one (SPEC 14.9.1). Composite: a Tuple or array literal
//   or a mixed range, whose parts are classified instead, and a control-flow part whose join supplies no candidate and has Tuple or
//   array results (ClassifiedByCall), whose results are classified position by position (Binding.ArgumentKinds.Positions.cs).
//   Waiting: an anonymous function, a function reference that needs S, a contextual `.Case(...)` (it resolves only in its expected
//   enum), a nested call or construction the argument probe deferred, or control flow whose literals have an unknown source beside
//   them or whose source call the probe deferred, until the selected candidate completes it. Typed: every other part.
// - Inputs: the part's syntax and binding state; ControlFlow's survey of a control-flow part's result sources (SurveyResult); the
//   argument probe (BindIndependentArgument), which reaches the result sources of a control-flow part (UnderArgumentProbe) and under
//   which each part that is not a Composite is bound: a nested call that only an outer expectation could type defers (DeferNestedCall)
//   and its kind is recorded, and a function name stays unbound (AtArgumentPosition); arithmetic operands have their own probe (SPEC
//   13.3.4) and share the Literal control-flow predicate (ResultSurvey.AllLiteral).
// - Outputs: KindOf; the preparation of a call's arguments and of each part (PrepareArgument, PrepareComposite); the fit walk, a
//   part's fit to the parameter Type at its position (FitArgument, Binding.ArgumentKinds.Fit.cs) for call arguments, elements, Case
//   payloads and value calls, and the erasure of a callable completed there (ErasePart); SolveSelected, the selected candidate of a call
//   whose slot no evidence binds: a nested call waits, or the Literal parts' defaults apply to it alone and its checks run once more
//   (SPEC 10.2.1); CompleteWaiting, the selected candidate's waiting arguments, and CompleteArguments, its arguments completed at its
//   operations; the waiting-binds-F rule (TakeWaiting, Binding.ArgumentKinds.Waiting.cs), the one walk to the waiting callables at an F
//   that the call's queries and the completions share; SlotSolution (Binding.ArgumentKinds.Solution.cs), the SPEC 10.8 solve state of
//   one candidate or one Case construction, whose evidence walk takes each part's evidence for the slots by its kind (InferArgument,
//   Binding.ArgumentKinds.Evidence.cs) and applies the defaults (DefaultArguments, DefaultPayloads), and whose candidate operations
//   substitute the solution (Type) and judge the Constraints over it (Prove); CompleteUnselected, the parts of a call that completes
//   none, bound on their own (SPEC 23.1.2).
// - Invariants: a recorded kind and a Literal's default are keyed by syntax identity (SourceRef) and live for one pass; a deferred
//   part stays waiting after its completion, so its Type is never evidence for an outer slot (WasWaitingPart); a Literal
//   is evidence only through the default recorded for the selected candidate; a Never-typed part is no evidence (SPEC 10.8).
// - Prohibitions: a consumer reads a part's kind from KindOf and tells only the forms of one kind apart (an anonymous function from a
//   reference) to fit them; the call, element, Case-payload and value-call paths keep no evidence or fit dispatch of their own;
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

    // The Type a part bound at an operation's source Type takes: at `ref/T`, whose source is the parameter's referent, the referent of the
    // Type the call hands (`handed`); any other source Type is the part's own.
    private static BoundType HandedReferent(BoundType source, BoundType? parameter, BoundType? handed)
        => parameter is { Components.Count: 1 } && ReferenceEquals(source, parameter.Components[0]) ? handed!.Components[0] : source;

    // SPEC 10.5.1: the kind of an argument part as this pass has decided it so far; an unbound part that PrepareArgument or the argument
    // probe classified (a control-flow part, a deferred nested call) by its recorded fact.
    private ArgumentKind KindOf(Koto part)
    {
        if (IsUnfittedLiteral(part))
        {
            return ArgumentKind.Literal;
        }

        if (IsAggregateArgument(part) || (part.BoundType is null && this.RecordedFact(KotoHelper.UnwrapParentheses(part)).Kind == ArgumentKind.Composite))
        {
            return ArgumentKind.Composite;
        }

        if (IsWaitingCallable(part) || (NeedsEnumContext(part) && KotoHelper.UnwrapParentheses(part).BoundType is null) || this.IsWaitingPart(part))
        {
            return ArgumentKind.Waiting;
        }

        var literal = part.BoundType is null && this.RecordedFact(KotoHelper.UnwrapParentheses(part)).Kind == ArgumentKind.Literal;
        return literal ? ArgumentKind.Literal : ArgumentKind.Typed;
    }

    // Whether the part waited for its call's selected candidate: a deferred part stays waiting after its completion, so its Type is
    // never evidence for an outer slot.
    private bool WasWaitingPart(Koto node) => this.RecordedFact(KotoHelper.UnwrapParentheses(node)).Kind == ArgumentKind.Waiting;

    // SPEC 10.5.1, 15.6.4 (G216 (a) U7): whether a part takes its Type from `call`: a waiting part that the selected candidate completes,
    // a contextual Case among them (SPEC 6.5.2: it resolves only in its expected enum), or a part whose Type holds a region the call
    // handed it (OpenCallPosition). Such a part is no Origin evidence for that call, as a deferred part is no slot evidence; a waiting
    // callable's written header is (SPEC 10.5.4).
    private bool TookCallType(Koto part, Koto call)
        => !IsWaitingCallable(part) && (this.KindOf(part) == ArgumentKind.Waiting || this.WasWaitingPart(part) || NeedsEnumContext(part) ||
            (KotoHelper.UnwrapParentheses(part).BoundType is { } type && HasOpenOriginFrom(type, call)));

    // SPEC 15.6.4, 10.2 (G216 (a) U7 as the planning session revised it, 2026-10-30, for `value + make()`): the Origin evidence a part that
    // took its Type from the call still gives, the layer the call's own adaptation forms over the part's value (an implicit Borrow), as the
    // pattern's and the adapted Type's layers over Unit, which holds none of the part's Type; null when the adaptation forms no layer.
    private (BoundType Pattern, BoundType Actual)? AdaptationLayer(BoundType pattern, BoundType adapted, Koto part)
        => KotoHelper.UnwrapParentheses(part).BoundType is { } own && adapted.Components.Count == 1 && ReferenceEquals(adapted.Components[0], own) &&
            pattern.Kind == adapted.Kind && pattern.Components.Count == 1
            ? (this.types.Rebuild(pattern, [BoundType.Unit], pattern.Origin, (BoundOrigin[])pattern.OriginArguments),
                this.types.Rebuild(adapted, [BoundType.Unit], adapted.Origin, (BoundOrigin[])adapted.OriginArguments))
            : null;

    // SPEC 10.2.1: the default a Literal part takes for the selected candidate of its call, once DefaultArguments applied it.
    private BoundType? DefaultOf(Koto part)
        => this.RecordedFact(KotoHelper.UnwrapParentheses(part)) is { Kind: ArgumentKind.Literal, Default: { } literalDefault } ? literalDefault : null;

    private ArgumentFact RecordedFact(Koto node)
        => node.SyntaxOwner is { } owner && this.argumentKinds.TryGetValue(new(owner.Module, node.SyntaxId), out var fact) ? fact : default;

    // Whether the part still waits for its call: deferred, not yet completed and with no failure of its own.
    private bool IsWaitingPart(Koto node) => node.BoundType is null && node.BindingFailure == BindingFailure.None && this.WasWaitingPart(node);

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

    // SPEC 10.5.1, 14.9.2: whether a join is a Literal or waiting control-flow part under the argument probe, or nested in one, so that
    // its call decides the Type its literal sources take (DecidedLater).
    private bool WaitsForCall(Koto target)
        => this.argumentProbe is { } probe && this.RecordedFact(probe).Kind is ArgumentKind.Literal or ArgumentKind.Waiting && this.UnderArgumentProbe(target);

    // SPEC 10.5.1 (the owner, 2026-10-26: element by element; DECISIONS BU-43): whether a join that supplies no candidate is the argument
    // probe's control flow, which its call classifies and completes at the selected candidate's Type. One of Tuple or array results
    // (`shape`, its first aggregate part), or one whose results hold an anonymous function, which waits for the call, is Composite: its
    // results are classified position by position (Binding.ArgumentKinds.Positions.cs), a position holding the anonymous function
    // Waiting, which replaces a Waiting a nested call deferred in one of its parts gave the probe; a scalar one of literals keeps the
    // kind its survey gave it.
    private bool ClassifiedByCall(Koto join, Koto? shape)
    {
        if (this.argumentProbe is not { } probe || !ReferenceEquals(ControlFlowExpression(probe), join))
        {
            return false;
        }

        if (shape is TupleLiteralKoto or ArrayLiteralKoto || this.HoldsWaitingFunction(join))
        {
            this.argumentKinds[SourceRef.Of(probe)] = new(ArgumentKind.Composite, null);
        }

        return true;
    }

    // SPEC 23.1.2, 10.1: a call that ends unresolved, with no selected candidate or one that failed before completing its arguments,
    // binds each argument part still unbound on its own under the argument probe, so that the part's own problems are reported once
    // and none that only the missing expectation causes: a control-flow part's literal sources and its source calls that would wait
    // stay unbound, and the parts of a Tuple or array literal, a contextual Case and a waiting nested call are completed alike. A
    // control-flow part already bound in this pass is not bound again. Anonymous functions stay unbound (PLAN R1e); no candidate is selected
    // again (SPEC 10.1); a deferred call is completed by its outer call.
    private BoundType? CompleteUnselected(InvocationKoto call, BindingScope scope, BoundType? type)
    {
        for (var i = 0; type is null && !this.IsWaitingPart(call) && i < call.ArgumentNodes.Count; i++)
        {
            this.CompleteUnselectedPart(call.ArgumentNodes[i], scope);
        }

        return type;
    }

    private void CompleteUnselectedPart(Koto part, BindingScope scope)
    {
        var node = KotoHelper.UnwrapParentheses(part);
        if (node.BoundType is not null)
        {
            return;
        }

        if (ControlFlowExpression(node) is { } flow)
        {
            if (!this.resultContexts.ContainsKey(flow))
            {
                this.BindIndependentArgument(node, scope); // Not yet bound in this pass: a Literal or waiting part.
            }

            return;
        }

        IReadOnlyList<Koto>? parts = node switch
        {
            TupleLiteralKoto tuple => tuple.Elements,
            ArrayLiteralKoto array => array.Elements,
            InvocationKoto nested when this.WasWaitingPart(nested) || NeedsEnumContext(nested) => nested.ArgumentNodes,
            _ => null,
        };
        for (var i = 0; parts is not null && i < parts.Count; i++)
        {
            this.CompleteUnselectedPart(parts[i], scope);
        }
    }

    // SPEC 7.7.4, 10.5: a Closure or Function Item part completed at a common Function Type is converted to it; its signature, Shared
    // receiver and Owned environment are judged here, after selection, and a failure is the part's own record, an environment that is
    // not Owned the Constraint record and a receiver that is not Shared a Type mismatch with its Note (SPEC 23.1.2).
    private bool ErasePart(Koto part, BoundType actual, BoundType erased)
    {
        if (!this.ErasesToFunction(part, actual, erased))
        {
            this.RecordMismatch(part, part, actual, erased);
            return false;
        }

        part.ErasedFunctionType = erased;
        return true;
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
        if (KotoHelper.UnwrapParentheses(part) is FunctionKoto { IsAnonymous: true } || NeedsEnumContext(part))
        {
            return true; // SPEC 10.5.1, 6.5.2: it waits for a candidate's Type, as a contextual Case resolves only in its expected enum.
        }

        if (this.KindOf(part) == ArgumentKind.Composite)
        {
            return this.PrepareComposite(part, scope);
        }

        if (part.CodeContext.RecoveryCause(part) is null && ControlFlowExpression(part) is { } flow &&
            this.SurveyResult(flow, scope) is { Literal: true, Typed: false } survey)
        {
            // SPEC 10.5.1 (DECISIONS BU-2, its owner decision (1) as the owner revised it on 2026-10-26, BU-43): control flow is Literal
            // when every result source other than Never is a literal, since Never supplies nothing at any position; with an unknown
            // source beside its literals it needs an expectation or a default, so it waits. It is bound once its call selects a
            // candidate, as an anonymous function is. A recovered part is bound as any other.
            this.argumentKinds[SourceRef.Of(KotoHelper.UnwrapParentheses(part))] = new(survey.Unknown ? ArgumentKind.Waiting : ArgumentKind.Literal, null);
            return true;
        }

        if ((this.KindOf(part) == ArgumentKind.Literal && part.CodeContext.RecoveryCause(part) is null) ||
            this.BindIndependentArgument(part, scope) is not null || this.IsWaitingPart(part) || this.KindOf(part) == ArgumentKind.Composite)
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

    // SPEC 10.5.1: under the argument probe and without an expectation, a nested call or construction waits for its outer call.
    private bool DeferNestedCall(Koto call, BoundType? expected)
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

    // SPEC 10.5.1, 10.2.1, 10.8: the selected candidate of a call whose slot no evidence binds or that holds a waiting part, or of a
    // nested call with another applicable candidate. Under the argument probe the nested call waits when another candidate is applicable
    // or its result holds a slot that neither evidence nor a waiting callable fixes (ResultHoldsUnsolved). Otherwise the Literal parts'
    // defaults apply to the selected candidate and its checks run once more; a failure reports that candidate, never another, and a slot
    // still unbound is the inference boundary. False when the call is complete: waiting or failed.
    private bool SolveSelected(ref CallEvaluation evaluation, int winner, BoundType? declaringType)
    {
        if (evaluation.Applicable <= 1 && evaluation.Evaluated[winner] is { Unsolved: false, State: not CandidateApplicability.Waiting })
        {
            return true;
        }

        var call = evaluation.Call;
        var selected = (FunctionKoto)evaluation.Evaluated[winner].Symbol!.Declaration;
        var solution = this.Solution(evaluation, selected, declaringType);
        var argument = evaluation.Expected is null && this.UnderArgumentProbe(call);
        if (argument && (evaluation.Applicable > 1 || solution.ResultHoldsUnsolved(evaluation.Mapping)))
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
    // and array literal arguments and of its control-flow arguments' results that bind an F, `ref/F` or `uniq/F` of it, a join they alone
    // kept waiting then decided (DecideTaken; DECISIONS BU-43). A body or capture failure never retries another candidate, and an
    // inferred result never supplies outer signature evidence. False when one fails.
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
                    // SPEC 15.6.4 step 3: the part takes the Type the call hands it; the fixed construction's re-check compares P′ itself.
                    completedWaiting[i] = true;
                    waitingContexts[i] = operations[i].ParameterType;
                    var handed = operations[i].ParameterType is { } parameter ? this.OpenCallPosition(parameter, call, i) : null;
                    if (this.CompleteWaitingPart(argument, pattern, solution.SlotOwner, handed, evaluation.Scope) is null)
                    {
                        return false;
                    }
                }
                else
                {
                    var awaited = this.AwaitsPosition(argument);
                    if (!this.TakeWaiting(argument, pattern, solution.SlotOwner, solution) || (awaited && !this.DecideTaken(argument)))
                    {
                        return false;
                    }
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
    private BoundType? CompleteWaitingPart(Koto part, BoundType pattern, Koto owner, BoundType? context, BindingScope scope)
    {
        var concrete = CallableSlotType(pattern, owner) is not null;
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

    // SPEC 10.5, 10.2: completes the selected candidate's arguments at its operations: a part still unbound, or one that awaits its
    // position, takes the Type the call hands it (OpenCallPosition; at `ref/T` its referent), and a Closure or Function Item the candidate
    // erases is converted to the common Function Type. A Composite bound here publishes the Type its elements formed as its adapted Type
    // (at `ref/T` under the parameter's layer), which the call judges against P′. False when one fails; every failing erasure is reported,
    // since the call rests on it and another candidate is never selected.
    private bool CompleteArguments(InvocationKoto call, BindingScope scope, Span<BoundArgumentOperation> operations)
    {
        var erased = true;
        for (var i = 0; i < call.ArgumentNodes.Count; i++)
        {
            var argument = call.ArgumentNodes[i];
            var parameter = operations[i].ParameterType;
            var handed = parameter is not null && (argument.BoundType is null || this.AwaitsPosition(argument))
                ? this.OpenCallPosition(parameter, call, i) : parameter;
            var composite = argument.BoundType is null && this.KindOf(argument) == ArgumentKind.Composite;
            if (this.PropagateBound(argument, handed) is null)
            {
                // The argument is an expression of the caller's context, not of the selected requirement.
                var selectedContract = this.activeRequirementContract;
                this.activeRequirementContract = null;
                var referent = operations[i].SourceType is { } source && !ReferenceEquals(source, parameter);
                this.RequireType(argument, scope, referent ? HandedReferent(operations[i].SourceType!, parameter, handed) : handed);
                this.activeRequirementContract = selectedContract;
                if (argument.BindingState != BindingState.Resolved)
                {
                    return false;
                }

                operations[i] = operations[i] with { SourceType = argument.BoundType };
                if (composite && operations[i].AdaptedType is null && argument.BoundType is { } built && parameter is not null)
                {
                    var layered = referent ? this.types.Rebuild(parameter, [built], parameter.Origin, (BoundOrigin[])parameter.OriginArguments) : built;
                    operations[i] = operations[i] with { AdaptedType = layered };
                }
            }
            else if (argument.BoundType is { Kind: BoundTypeKind.Closure or BoundTypeKind.FunctionItem } closureType &&
                operations[i].ParameterType is { Kind: BoundTypeKind.Function } erasedType && operations[i].Adaptation == ArgumentAdaptation.Erasure)
            {
                erased &= this.ErasePart(argument, closureType, erasedType);
                operations[i] = operations[i] with { SourceType = erasedType };
            }
        }

        return erased;
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

    // The independent Type of the parts at a position (Binding.ArgumentKinds.Positions.cs): a Tuple's, a fill's, a join's or a join
    // position's from its children; with `fitLiterals`, a Literal part gives its default once DefaultArguments applied it. A part of
    // Type Never, also a position Never in every source, gives none (SPEC 10.8; DECISIONS BU-2 U3's D6).
    private BoundType? IndependentPositionType(int position, bool fitLiterals)
    {
        var end = this.positionParts.Count;
        if (end - position != 1)
        {
            return this.PositionKind(position, end, out var type) switch
            {
                ArgumentKind.Typed => type,
                ArgumentKind.Literal => fitLiterals ? this.DefaultOf(this.FirstLiteral(position, end)) : null,
                ArgumentKind.Composite => this.IndependentShapeType(position, end, this.PartsShape(position, end)!, fitLiterals),
                _ => null,
            };
        }

        var source = KotoHelper.UnwrapParentheses(this.positionParts[position]);
        if (source.BoundType is { } actual && !this.AwaitsPosition(source))
        {
            return ReferenceEquals(actual, BoundType.Never) ? null : actual; // SPEC 10.8: a Never part supplies no evidence.
        }

        if (source is RangeKoto range && IsMixedRange(range))
        {
            // SPEC 4.6.2: without an expected range, a literal-only boundary takes the integer Type of the typed one; its default (rule 3)
            // applies only to the selected candidate (SPEC 10.2.1).
            var start = this.IndependentRangeBoundary(range.Start, this.StartType);
            var last = this.IndependentRangeBoundary(range.End, this.EndType);
            start ??= this.DefaultOf(range.Start!) ?? this.RangeTyping(range.Start!, last);
            last ??= this.DefaultOf(range.End!) ?? this.RangeTyping(range.End!, start);
            if (start is null || last is null)
            {
                return null;
            }

            var symbol = range.IsInclusive ? this.Library.ClosedRange : this.Library.Range;
            return this.types.Intern(BoundTypeKind.Constructed, symbol, SemanticsKind.Owner, [start, last]);
        }

        if (source is TupleLiteralKoto or ArrayLiteralKoto { FillCount: not null })
        {
            return this.IndependentShapeType(position, end, source, fitLiterals);
        }

        if (this.ComposedJoin(source) is not null)
        {
            return this.IndependentChildType(position, end, 0, fitLiterals);
        }

        return fitLiterals ? this.DefaultOf(source) : null;
    }

    // The independent Type an aggregate shape forms from its child positions' (ShapeType), none when one has none.
    private BoundType? IndependentShapeType(int start, int end, Koto shape, bool fitLiterals)
    {
        var count = this.ChildPositions(start, end);
        var children = this.RentTypes(count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                if (this.IndependentChildType(start, end, i, fitLiterals) is not { } child)
                {
                    return null;
                }

                children[i] = child;
            }

            return this.ShapeType(shape, children.AsSpan(0, count));
        }
        finally
        {
            this.typeScratch.Return(children, clearArray: true);
        }
    }

    // The independent Type of child position `index` of [start, end).
    private BoundType? IndependentChildType(int start, int end, int index, bool fitLiterals)
    {
        var child = this.positionParts.Count;
        this.AddChildPosition(start, end, index, null);
        var type = this.IndependentPositionType(child, fitLiterals);
        this.ClosePosition(child);
        return type;
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
