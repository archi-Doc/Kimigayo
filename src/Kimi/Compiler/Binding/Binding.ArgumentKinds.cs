// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Argument kinds (SPEC 10.5.1; DECISIONS BU-2, BU-43): what each argument part is to the candidates of a call.
// - Responsibility: the kind of every argument part: a call argument and, recursively, an element of a Tuple or array literal
//   argument, a written boundary of a mixed range and a payload of a Case construction, as one closed PartKind. Literal: an unfitted
//   literal (SPEC 12.3), or control flow whose result sources other than Never are all such, at least one (SPEC 14.9.1). Aggregate: a
//   Tuple or array literal or a mixed range, whose parts are classified instead. Join: a control-flow part its call completes, one
//   whose source call the probe deferred (P3) and one whose join supplies no candidate and has Tuple or array results or results that
//   hold a part awaiting its call (ClassifiedByCall; DECISIONS BU-43), whose results are classified position by position
//   (Binding.ArgumentKinds.Positions.cs). Function: an anonymous function. Reference: a function reference that needs S, or a function
//   name at a result of the probe's control flow (P2), unless every such result is the same declaration that needs no S, which P4
//   binds as its Function Item (BindAgreeingReferences). Case: a contextual `.Case(...)` (it resolves only in its expected enum).
//   Call: a nested call or construction the argument probe deferred.
//   A part its producer records as a Join, Case, Reference, Function or Call awaits its call (AwaitsCall) until its call completes it,
//   its core bound or failed; all but a Join are SPEC 10.5.1's waiting parts, which the selected candidate completes. Typed: every
//   other part.
// - Inputs: the part's syntax and binding state; ControlFlow's survey of a control-flow part's result sources (SurveyResult); the
//   argument probe (BindIndependentArgument), which reaches the result sources of a control-flow part (UnderArgumentProbe) and under
//   which each part that is not an Aggregate or Join is bound: a nested call that only an outer expectation could type defers
//   (DeferNestedCall) and its kind is recorded, and a function name stays unbound (AtArgumentPosition); arithmetic operands have their
//   own probe (SPEC 13.3.4) and share the Literal control-flow predicate (ResultSurvey.AllLiteral).
// - Outputs: KindOf, ClassifyPart and AwaitsCall; the preparation of a call's arguments and of each part (PrepareArgument, PrepareComposite); the
//   fit walk, a part's fit to the parameter Type at its position (FitArgument, Binding.ArgumentKinds.Fit.cs) for call arguments,
//   elements, Case payloads and value calls, and the erasure of a callable completed there (ErasePart); SolveSelected, the selected
//   candidate of a call whose slot no evidence binds: a nested call waits, or the Literal parts' defaults apply to it alone and its
//   checks run once more (SPEC 10.2.1); CompleteWaiting, the selected candidate's waiting arguments, and CompleteArguments, its
//   arguments completed at its operations, and CompleteUnselected, the parts of a call that completes none, bound on their own (SPEC
//   23.1.2; Binding.ArgumentKinds.Completion.cs); the waiting-binds-F rule (TakeWaiting, Binding.ArgumentKinds.Waiting.cs), the one
//   walk to the waiting callables at an F that the call's queries and the completions share; SlotSolution
//   (Binding.ArgumentKinds.Solution.cs), the SPEC 10.8 solve state of one candidate or one Case construction, whose evidence walk takes
//   each part's evidence for the slots by its kind (InferArgument, Binding.ArgumentKinds.Evidence.cs) and applies the defaults
//   (DefaultArguments, DefaultPayloads), and whose candidate operations substitute the solution (Type) and judge the Constraints over it
//   (Prove).
// - Invariants: a recorded part (ArgumentPart: its kind and a Literal's default) is keyed by its core's syntax identity (PartCore,
//   SourceRef) and lives for one pass; a producer records every part that awaits its call and a Literal control-flow part: P1
//   (PrepareArgument) a Function, Case, Reference or Literal, P2 a Function or Case result (AwaitsCallAtResult) or a Reference one
//   (AtArgumentPosition), P3 (DeferNestedCall) a Call and its Join, P4 (ClassifiedByCall) a Join, and the operand probe
//   (BindIndependentOperand) a Function or Case; ClassifyPart derives Literal, Aggregate, Function, Case and Typed; KindOf alone
//   combines the two; a deferred part (WasDeferred, a recorded Call) stays so after its
//   completion, so its Type is never evidence for an outer slot; a Literal is evidence only through the default recorded for the
//   selected candidate; a Never-typed part is no evidence (SPEC 10.8).
// - Prohibitions: a consumer reads a part's kind from KindOf and whether it awaits its call from AwaitsCall, and tells only the forms
//   of one kind apart to fit them; whether a part waits for its position, not its call, is ResultPropagation's position waiting
//   (Deferred, AwaitsFor; SPEC 14.9.2, 4.3); the call, element, Case-payload and value-call paths keep no evidence or fit dispatch of
//   their own; no Koto-keyed table (COMPILER_ARCHITECTURE, Code rules: Syntax identity).
public sealed partial class Binding
{
    private readonly Dictionary<SourceRef, ArgumentPart> argumentParts = new();
    private Koto? argumentProbe;
    private InvocationKoto? nestedOperandProbe;

    private enum PartKind : byte
    {
        Typed,
        Literal,
        Aggregate,
        Join,
        Case,
        Reference,
        Function,
        Call,
    }

    // SPEC 10.5.1: whether a part whose producer recorded it as `recorded` awaits its call, whose selected candidate completes it: a Join,
    // Case, Reference, Function or Call (no Typed, Literal or Aggregate part waits) until its core is bound or has a failure of its own. Of
    // these, all but a Join are SPEC 10.5.1's waiting parts (the SPEC word, which CompleteWaiting, TakeWaiting and IsWaitingPart keep); a
    // Join keeps its own rows (FitArgument, CompleteWaiting).
    private static bool Awaits(Koto core, PartKind recorded)
        => recorded is not (PartKind.Typed or PartKind.Literal or PartKind.Aggregate) && core.BoundType is null &&
            core.BindingFailure == BindingFailure.None;

    // SPEC 10.5.1: the kind a part's form gives it: an unfitted literal, a Tuple or array literal or mixed range, an anonymous function, a
    // contextual Case, or Typed. A Join, a Call, a Reference and a Literal control-flow part are their producers' records instead
    // (RecordPart; KindOf combines the two).
    private static PartKind ClassifyPart(Koto part) => part switch
    {
        _ when IsUnfittedLiteral(part) => PartKind.Literal,
        _ when IsAggregateArgument(part) => PartKind.Aggregate,
        _ when KotoHelper.UnwrapParentheses(part) is FunctionKoto { IsAnonymous: true } => PartKind.Function,
        _ when NeedsEnumContext(part) => PartKind.Case,
        _ => PartKind.Typed,
    };

    private static bool IsAggregateArgument(Koto source)
        => KotoHelper.UnwrapParentheses(source) is ArrayLiteralKoto or TupleLiteralKoto { Elements.Count: > 0 } || IsMixedRange(source);

    // SPEC 4.6.2: a range with a literal-only and a typed written boundary. Like a tuple element, each literal-only boundary
    // takes the S or E of the candidate's parameter, while a typed boundary keeps its independent Type.
    private static bool IsMixedRange(Koto source)
        => KotoHelper.UnwrapParentheses(source) is RangeKoto range && !IsLiteralOnlyRange(range) &&
            ((range.Start is { } start && IsLiteralOnlyPosition(start)) || (range.End is { } end && IsLiteralOnlyPosition(end)));

    // SPEC 10.5.1: whether the part awaits its call, from its producer's record (Awaits): the one query of call waiting.
    private bool AwaitsCall(Koto part) => this.AwaitedKind(part) is not null;

    // The kind its producer recorded for a part that awaits its call (Awaits), else null: the record is read once, and only for an
    // unbound core, for AwaitsCall and the readers that also ask the kind (IsWaiting, IsWaitingCallable). An awaiting part's KindOf is
    // that recorded kind.
    private PartKind? AwaitedKind(Koto part)
        => PartCore(part) is { BoundType: null } core && this.RecordedPart(core).Kind is var recorded && Awaits(core, recorded) ? recorded : null;

    // SPEC 10.5.1: the kind of an argument part as this pass has decided it so far: a literal or an aggregate by its form, an unbound
    // part by the kind its producer recorded while it awaits its call (Awaits) or a Literal one, else ClassifyPart's. The only reader
    // that combines the two; for a part no producer recorded, the form gives its kind, never its waiting.
    private PartKind KindOf(Koto part)
    {
        part = PartCore(part);
        var kind = ClassifyPart(part);
        if (kind is PartKind.Literal or PartKind.Aggregate || part.BoundType is not null)
        {
            return kind;
        }

        var recorded = this.RecordedPart(part).Kind;
        return recorded == PartKind.Literal || Awaits(part, recorded) ? recorded : kind;
    }

    // Whether the argument probe deferred the part (a recorded Call): it stays so after its completion, so its Type is never evidence
    // for an outer slot.
    private bool WasDeferred(Koto node) => this.RecordedPart(node).Kind == PartKind.Call;

    // SPEC 10.5.1: whether the part is waiting, which the selected candidate completes: it awaits its call and is not a Join, which keeps
    // its own rows.
    private bool IsWaiting(Koto part) => this.AwaitedKind(part) is not (null or PartKind.Join);

    // SPEC 10.5.1 (DECISIONS BU-43), P2: whether an anonymous function or a contextual Case reached without an expected Type is a result
    // of a call's control-flow argument at a position its call completes (CallableTarget): it then awaits that call, its kind recorded
    // once, and the call completes it as a waiting argument (TakeWaiting).
    private bool AwaitsCallAtResult(Koto node, BoundType? expected)
    {
        if (expected is not null || node.BindingState != BindingState.Unvisited)
        {
            return false;
        }

        var kind = ClassifyPart(node);
        if (kind is not (PartKind.Function or PartKind.Case) || CallableTarget(node) is not { } target || !this.UnderArgumentProbe(target))
        {
            return false;
        }

        this.RecordPart(node, kind);
        return true;
    }

    // SPEC 10.5: whether the part is a waiting callable, a Function or Reference that awaits its call.
    private bool IsWaitingCallable(Koto part) => this.AwaitedKind(part) is PartKind.Function or PartKind.Reference;

    // SPEC 10.2.1: the default a Literal part takes for the selected candidate of its call, once DefaultArguments applied it.
    private BoundType? DefaultOf(Koto part)
        => this.RecordedPart(part) is { Kind: PartKind.Literal, Default: { } literalDefault } ? literalDefault : null;

    // The one writer of the kind a producer decides for a part (P1 PrepareArgument, P2 AwaitsCallAtResult and AtArgumentPosition, P3
    // DeferNestedCall, P4 ClassifiedByCall), keyed by its core's syntax identity (PartCore) for this pass; DefaultLiteral adds a
    // Literal's default later.
    private void RecordPart(Koto part, PartKind kind) => this.argumentParts[SourceRef.Of(PartCore(part))] = new(kind, null);

    private ArgumentPart RecordedPart(Koto node)
        => PartCore(node) is { SyntaxOwner: { } owner } core && this.argumentParts.TryGetValue(new(owner.Module, core.SyntaxId), out var part)
            ? part : default;

    // Whether the part still waits for its call as a deferred Call: not yet completed and with no failure of its own (KindOf).
    private bool IsWaitingPart(Koto node) => this.KindOf(node) == PartKind.Call;

    // SPEC 10.5.1, 14.9.2: whether the part is the argument probe's, or a result source of a control-flow expression that is: nested
    // results propagate the same way.
    private bool UnderArgumentProbe(Koto part)
    {
        for (Koto? node = part; this.argumentProbe is not null && node is not null and not FunctionKoto; node = PartParent(node))
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
        => this.argumentProbe is { } probe && this.RecordedPart(probe).Kind is PartKind.Literal or PartKind.Call or PartKind.Join &&
            this.UnderArgumentProbe(target);

    // SPEC 10.5.1 (the owner, 2026-10-26: element by element; DECISIONS BU-43): whether a join that supplies no candidate is the argument
    // probe's control flow, which its call classifies and completes at the selected candidate's Type. One of Tuple or array results
    // (`shape`, its first aggregate part), or one whose results hold a part awaiting its call (AwaitsCall), is Join: its results are
    // classified position by position (Binding.ArgumentKinds.Positions.cs); a scalar one of literals keeps the kind its survey gave it.
    private bool ClassifiedByCall(Koto join, Koto? shape)
    {
        if (this.argumentProbe is not { } probe || !ReferenceEquals(ControlFlowExpression(probe), join))
        {
            return false;
        }

        if (shape is TupleLiteralKoto or ArrayLiteralKoto || this.HoldsPartAwaitingCall(join))
        {
            this.RecordPart(probe, PartKind.Join);
        }

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

        return this.KindOf(part) == PartKind.Literal ? this.SurveyResult(flow, scope).Default : null;
    }

    // SPEC 10.5.1, 12.3, 14.9.2: whether a Literal part fits the Type; a control-flow one when every literal source does.
    private bool FitsLiteralPart(Koto part, BoundType type, BindingScope scope)
        => ControlFlowExpression(part) is { } flow ? this.SurveyResult(flow, scope, type).Fits : this.FitsInputLiteral(part, type, scope);

    // SPEC 10.5: a function name that is a call argument or the part under the argument probe is not bound as its Item without an
    // expectation; PrepareArgument decides whether it is evidence or waits for S. P2 (SPEC 10.5.3, 14.9.2): a function name that is a
    // result of the probe's control flow, at a position its call completes (CallableTarget), stays unbound as a Reference.
    private bool AtArgumentPosition(Koto node)
    {
        if ((TryNameRoot(node, out var root) && root.Parent is InvocationKoto) || ReferenceEquals(this.argumentProbe, node))
        {
            return true;
        }

        if (CallableTarget(node) is not { } target || !this.UnderArgumentProbe(target))
        {
            return false;
        }

        this.RecordPart(node, PartKind.Reference);
        return true;
    }

    // P4 (SPEC 10.5.3, 14.9.2 step 2): when every source of the join still unbound is a Reference to one declaration that needs no S
    // (IndependentFunctionItem, P1's rule), they are bound as that Function Item before the join judges its sources, so the join is
    // typed and checked per candidate; otherwise they wait for the call, which completes them at its Function Type.
    private void BindAgreeingReferences(ResultContext context)
    {
        BindingSymbol? item = null;
        for (var i = 0; i < context.Sources.Count; i++)
        {
            if (context.Sources[i] is null && context.SourceNodes[i] is { } source)
            {
                var core = PartCore(source);
                if (this.KindOf(core) != PartKind.Reference || (item is not null && !ReferenceEquals(item, core.BoundSymbol)) ||
                    !IndependentFunctionItem(core.BoundSymbol!, this.UnboundMemberReference(core), this.ReferenceDeclaringType(core)))
                {
                    return;
                }

                item = core.BoundSymbol;
            }
        }

        BoundType? type = null;
        for (var i = 0; item is not null && i < context.Sources.Count; i++)
        {
            if (context.Sources[i] is null && context.SourceNodes[i] is { } source &&
                (type ??= this.BindFunctionItem(PartCore(source), item, this.ConstraintScope(source))) is not null)
            {
                context.Sources[i] = this.BindWaiting(source, type); // Through the forms that deliver it, at the Item's Type.
            }
        }
    }

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

    // SPEC 10.5.1: prepares an argument part once per pass, by its kind. An anonymous function waits for a candidate's signature; an
    // Aggregate or Join prepares its parts; control flow is classified by the survey of its result sources (SPEC 14.9.1); a Literal is
    // fitted after selection, unless the parser kept it as a recovery, which fails here so that the call rests on its Error; every other
    // part is bound under the argument probe, where a nested call that only an expectation could type waits, also as a result source of
    // a control-flow part, which then waits with it, and a function reference that needs S waits for the selected candidate's (10.5.3).
    private bool PrepareArgument(Koto part, BindingScope scope, BindingSymbol? group, bool callableContext)
    {
        var kind = ClassifyPart(part);
        if (kind is PartKind.Function or PartKind.Case)
        {
            this.RecordPart(part, kind);
            return true; // SPEC 10.5.1, 6.5.2: it waits for a candidate's Type, as a contextual Case resolves only in its expected enum.
        }

        if (this.KindOf(part) == PartKind.Join)
        {
            return !this.PartFailed(part); // Prepared once per pass, by its probe; the call rests on a failed one.
        }

        if (kind == PartKind.Aggregate)
        {
            return this.PrepareComposite(part, scope);
        }

        if (part.CodeContext.RecoveryCause(part) is null && ControlFlowExpression(part) is { } flow &&
            this.SurveyResult(flow, scope) is { Literal: true, Typed: false, Unknown: false })
        {
            // SPEC 10.5.1 (DECISIONS BU-2, its owner decision (1) as the owner revised it on 2026-10-26, BU-43): control flow is Literal
            // when every result source other than Never is a literal, since Never supplies nothing at any position. It is bound once
            // its call selects a candidate, as an anonymous function is. With an unknown source beside its literals it is bound under
            // the probe as any other part: that source call defers and the join waits with it as a Join (P3). A recovered part is
            // bound as any other.
            this.RecordPart(part, PartKind.Literal);
            return true;
        }

        if ((this.KindOf(part) == PartKind.Literal && part.CodeContext.RecoveryCause(part) is null) ||
            this.BindIndependentArgument(part, scope) is not null || this.IsWaitingPart(part) || this.KindOf(part) is PartKind.Aggregate or PartKind.Join)
        {
            return true;
        }

        if (KotoHelper.UnwrapParentheses(part) is not { BindingState: BindingState.Resolved, BoundSymbol: { Kind: BindingSymbolKind.Function } item })
        {
            return false;
        }

        // SPEC 10.5: a single closed declaration supplies its own Item Type as generic argument evidence. A generic Callable contract
        // checks that Type without erasing it to a common Function handle.
        if ((callableContext || this.TakesCallableContext(group)) &&
            !IndependentFunctionItem(item, this.UnboundMemberReference(part), this.ReferenceDeclaringType(part)) &&
            !this.ExplicitReferenceValue(part, item, scope))
        {
            this.RecordPart(part, PartKind.Reference); // SPEC 10.5.3: it needs S, so it stays unbound until its call completes it.
            return true;
        }

        return this.BindFunctionItem(part, item, scope) is not null;
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
    // waits only when it is ambiguous or no default fixes its slots (SolveSelected), never because a default would; an anonymous
    // function or a contextual Case is recorded as an argument's is (P1), and awaits its operator while it stays unbound.
    private BoundType? BindIndependentOperand(Koto operand, BindingScope scope)
    {
        if (ClassifyPart(operand) is var kind && kind is PartKind.Function or PartKind.Case)
        {
            this.RecordPart(operand, kind);
        }

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

        this.RecordPart(call, PartKind.Call);
        if (this.argumentProbe is { } probe && this.UnderArgumentProbe(call))
        {
            this.RecordPart(probe, ReferenceEquals(PartCore(probe), call) ? PartKind.Call : PartKind.Join); // SPEC 14.9.2: its join waits with it.
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
                PartKind.Typed => type,
                PartKind.Literal => fitLiterals ? this.DefaultOf(this.FirstLiteral(position, end)) : null,
                PartKind.Aggregate => this.IndependentShapeType(position, end, this.PartsShape(position, end)!, fitLiterals),
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

    // The independent Type an aggregate shape forms from its child positions' (ShapeType), as a join's Type is formed (SPEC 14.9.2
    // step 2: an array's one element position, AddPartsAt), none when one has none.
    private BoundType? IndependentShapeType(int start, int end, Koto shape, bool fitLiterals)
    {
        var count = ShapeChildren(shape);
        var children = this.RentTypes(count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                var position = this.positionParts.Count;
                this.AddPartsAt(start, end, i);
                var child = this.IndependentPositionType(position, fitLiterals);
                this.ClosePosition(position);
                if (child is null)
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

    // SPEC 10.5.1, 4.6.2: an Aggregate's parts: each element of a Tuple or array literal is prepared as an argument at its position (a
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
    private readonly record struct ArgumentPart(PartKind Kind, BoundType? Default);
}
