// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// SPEC 10.2-10.8: evaluates one named-call candidate from prepared argument slots, expected result and Constraints.
// Returns its existing applicability and inference facts. The candidate scope restores saved inference state on every exit;
// lookup, ranking and final call publication remain with their existing owners.
public sealed partial class Binding
{
    // A candidate's environment judgments (DECISIONS BU-86) and slot bindings are its own frame's (SlotFrame.EnvironmentJudgment,
    // Bindings), so a nested call bound while it is evaluated keeps its own. `unsolved` holds the required structural slots that no
    // evidence binds (SPEC 10.8); an Applicable or Waiting candidate with such slots stays rankable, and its selection is the
    // inference-boundary record (FailUnboundSlots).
    private CandidateApplicability TryCandidate(
        SlotSolution solution,
        int[] mapping,
        CallArgumentMap argumentMap,
        BoundType? expected,
        Span<BoundArgumentOperation> operations,
        out int defaultsUsed,
        out bool unsolved,
        out ClosureReceiverRefutation? closureReceiver,
        out bool independentRejection,
        out ReferenceConstraintFailure? constraintFailure,
        out bool premiseUnknown)
    {
        if (this.MeasureCallInference)
        {
            this.inferenceCandidateChecks++;
        }

        var savedAcquisition = this.acquisitionFailure;
        this.acquisitionFailure = null;
        var unformedMark = this.unformedWrites;
        var enclosing = this.OpenTrial(); // A candidate trial is a frame (design-r2 Amendment 5; r2-6).
        var wholesMark = this.wholes.Count;
        try
        {
            var result = this.TryCandidateCore(
                ref solution,
                mapping,
                argumentMap,
                expected,
                operations,
                out defaultsUsed,
                out unsolved,
                out closureReceiver,
                out independentRejection,
                out constraintFailure,
                out premiseUnknown);
            if (result is CandidateApplicability.Applicable or CandidateApplicability.Waiting && this.acquisitionFailure is { } acquisition)
            {
                // Only a candidate whose other inputs, result and Constraints fit can explain the absent spelling.
                if (result == CandidateApplicability.Applicable)
                {
                    savedAcquisition = acquisition;
                }

                result = CandidateApplicability.Inapplicable;
            }

            // Rule W at the candidate's own verdict (DECISIONS BU-79; SPEC 23.1.2; PLAN G206): a parameter or result Type that failed to
            // bind is a wildcard in every fit, so only a candidate that would apply rests on it (IncompleteSignature) instead. The same
            // verdict over an unformed split (BU-240, PLAN G886; SPEC 10.1's unproven candidate): a trial that formed a Type no binding
            // represents rests on it (Pending, UnformedSplit) only where every check reading no unformed Type passed; an inapplicable
            // candidate is judged without it, its writes dropped. A proof's Error without one stays (InvalidDeclarationContext).
            if (this.UnformedSince(unformedMark) && result is not (CandidateApplicability.Inapplicable or CandidateApplicability.Error))
            {
                this.trialPending = new(PendingJudgmentKind.UnformedSplit, solution.Function) { Epoch = this.frameEpoch };
                premiseUnknown = false;
                return CandidateApplicability.Pending;
            }

            this.unformedWrites = unformedMark;
            return result is CandidateApplicability.Applicable or CandidateApplicability.Waiting && IncompleteSignature(solution.Function) is not null
                ? CandidateApplicability.Pending : result;
        }
        finally
        {
            this.wholes.RemoveRange(wholesMark, this.wholes.Count - wholesMark);
            this.CloseTrial(enclosing);
            this.acquisitionFailure = savedAcquisition;
        }
    }

    // DECISIONS BU-86: a Pending return that a known signature's environment result decided publishes that judgment as the trial's.
    private CandidateApplicability PendingOnEnvironment(PendingJudgment? environment)
    {
        this.trialPending = environment is { } judgment ? judgment with { Epoch = this.frameEpoch } : null;
        return CandidateApplicability.Pending;
    }

    // DECISIONS BU-86: with every piece of evidence in, the environment judgment that decides: that of the first slot, in slot order, that
    // an environment result left without evidence (Infer, in the candidate's frame) and no other evidence binds (SPEC 10.8; never argument
    // order). A bound slot's part is no inference part, and the Callable proof or the conversion judges it (SPEC 10.8, 8.10).
    private PendingJudgment? EnvironmentJudgment(FunctionKoto function, SlotFrame frame, in SlotSet unsolved)
    {
        for (var g = 0; g < CallSlotCount(function); g++)
        {
            if (frame.EnvironmentJudgment(g) is { } judgment && unsolved.Contains(g))
            {
                return judgment;
            }
        }

        return null;
    }

    // SPEC 10.8 ("collected together"; traversal order never chooses; every exact source of a binder (IsExactSource), an application
    // of a bare slot or a closed leaf Type included, fixes it before the expected result fills): an input whose pattern holds an
    // application of a binder no source has fixed waits. A waiting input that no longer holds one is inferred; when none can be, every waiting input not yet a source is
    // inferred once as one (its exact sources fix its own binders; a structured application of a binder still unfixed supplies
    // nothing), and those it formed are inferred again. Called before the expected result, after it (for a binder only the
    // expected result fixes) and after the late pass (for one only a Literal's default fixes); one still open keeps its open parts
    // unbound. An input whose own pattern forms no Type is judged by UnformedPart instead.
    private bool SettleWaiting(ref SlotSolution solution, int[] mapping, ref SlotSet waiting, ref SlotSet sourced)
    {
        var call = solution.Call;
        var function = solution.Function;
        var owner = CallSlotOwner(function);
        while (!waiting.IsEmpty)
        {
            var settled = false;
            for (var i = 0; i < call.ArgumentNodes.Count; i++)
            {
                if (waiting.Contains(i) && !this.HasOpenApplication(function.Parameters[mapping[i]].Type.BoundType!, owner, solution.Frame))
                {
                    if (!this.InferWaiting(ref solution, mapping, i))
                    {
                        return false;
                    }

                    waiting.Remove(i);
                    settled = true;
                }
            }

            if (settled)
            {
                continue;
            }

            var bound = SourcedSlots(solution.Frame);
            for (var i = 0; i < call.ArgumentNodes.Count; i++)
            {
                if (waiting.Contains(i) && !sourced.Contains(i))
                {
                    sourced.Add(i);
                    if (!this.InferWaiting(ref solution, mapping, i))
                    {
                        return false;
                    }
                }
            }

            if (SourcedSlots(solution.Frame) == bound)
            {
                return true;
            }
        }

        return true;
    }

    // SPEC 10.8: infers waiting input i; false when it fails, unless it failed through its own pattern's formation (UnformedPart).
    private bool InferWaiting(ref SlotSolution solution, int[] mapping, int i)
    {
        var type = solution.Function.Parameters[mapping[i]].Type.BoundType!;
        var part = solution.Call.ArgumentNodes[i];
        var mark = this.unformedWrites;
        return (solution.Pattern(type) is { } evidencePattern ? solution.InferArgument(evidencePattern, part, late: false) : part.BoundType is null) ||
            this.UnformedPart(ref solution, type, i, this.FormsNone(type, mark)) == true;
    }

    // DECISIONS BU-240 with rule W (BU-79; G812 ruling 88, design revision 1): argument i's check failed; whether it failed through its own
    // pattern's formation, `formsNone` (FormsNone over a mark taken at that part's evidence check, which forms only the part's own
    // pattern: its adaptation pattern, application, direct target; or around its `solution.Type` in the pass loop). Null where its
    // pattern formed: the part's own failure. A Literal is judged at its target (FitsLiteralShape, SPEC 10.2.1): false, the part's own
    // failure, or unknown. A typed part at a closed leaf that disagrees with its binder's evidence fails (IsSource, the owner's
    // O-338 (a); F's O1 (a)), and so does one that no case of the split layer adapts (FitsOpenLayer; the owner's O-366 (a)). Any other
    // part is unknown, a wildcard in every later check as a parameter Type that failed to bind is (SPEC 10.1's Unknown applicability),
    // and the trial goes on.
    private bool? UnformedPart(ref SlotSolution solution, BoundType type, int i, bool formsNone)
    {
        if (!formsNone)
        {
            return null;
        }

        var part = solution.Call.ArgumentNodes[i];
        if (this.KindOf(part) == PartKind.Literal
            ? this.PartDefault(part, solution.Scope) is { } shape && !solution.FitsLiteralShape(type, shape, part, out _)
            : this.TypedPartFails(ref solution, type, part))
        {
            return false;
        }

        solution.Unknown.Add(i);
        return true;
    }

    // DECISIONS BU-240 (the part predicate's one spelling): whether a pattern formed since `mark` formed no Type or kept an unrepresented slot;
    // a closed Type forms under no binding, while a call's Type also reads every pair's outer slot (CallType), so only a pattern with
    // an abstract part counts.
    private bool FormsNone(BoundType pattern, int mark) => pattern.HasAbstractPart && this.UnformedSince(mark);

    // SPEC 10.8, 10.2: whether a typed part whose pattern forms no Type fails case-independently: at a closed leaf by IsSource
    // (LeafDisagrees), or where no case of the split layer adapts it (FitsOpenLayer); otherwise it rests on the split.
    private bool TypedPartFails(ref SlotSolution solution, BoundType type, Koto part)
        => this.LeafDisagrees(ref solution, type, part) || !this.FitsOpenLayer(ref solution, type, part);

    // SPEC 7.7.4, 10.5 (C-359 C1): whether a Function Item part whose own signature forms no Type (DECISIONS BU-240) fits no case of its
    // split at the Function Type the frame completes (ItemSignatureFits); one that some case fits, or whose parameter is still open,
    // rests on the split. Read once every part's evidence is in (TryCandidateCore), so the argument order decides nothing.
    private bool UnformedItemFails(ref SlotSolution solution, BoundType type, Koto part)
    {
        if (part.BoundType is not { Kind: BoundTypeKind.FunctionItem } item)
        {
            return false;
        }

        var mark = this.unformedWrites;
        _ = this.FunctionItemSignature(item);
        var unformed = this.UnformedSince(mark);
        this.unformedWrites = mark;
        if (!unformed || solution.Type(type) is not { Kind: BoundTypeKind.Function } required)
        {
            return false;
        }

        var rows = this.comparisonRows.Count; // SPEC 15.6.1: a selection takes and drops the region conjuncts.
        var fits = this.ItemSignatureFits(item, required, part, this.comparisonRows, out _);
        this.DropComparisonRows(rows);
        return !fits;
    }

    // SPEC 10.2, 10.8 (O3; the owner's O-366 (a)): whether a typed part fits its parameter with the split layer open: the common
    // adaptation itself (AdaptInput) judges the layer case by case, as it judges a pair layer. A part that no case adapts fails
    // case-independently; one that some case adapts, or whose Type keeps an unbound slot, rests on the split.
    // - A binder that sources gave evidence (an own slot with selector evidence): an exact source is LeafDisagrees'; a structured
    //   application's layer over the target the frame completes is judged in each case of the callee's admitted set (a typed bool
    //   at `s/Option<Box>`).
    // - A binder fixed before any evidence, whose parameter forms no Type: explicit Type arguments (a Tag at the closed leaf `s/Box`,
    //   C-359 C1) or the receiver's container binder (`x.g(g)` over `S<U>`, E1). The part is judged at the Type its parameter forms
    //   in each combination of split cases (RestsOnSplitCases over its member pattern and the frame, as SlotSolution.Pattern and
    //   the substitution form it), whichever binding that formation meets.
    private bool FitsOpenLayer(ref SlotSolution solution, BoundType type, Koto part)
    {
        if (part.BoundType is not { } actual)
        {
            return true;
        }

        var frame = solution.Frame;
        var pattern = solution.Pattern(type);
        if (pattern is { Kind: BoundTypeKind.SemanticsApplication, Symbol: { WholeType: { } whole } pair, Components: [var open] } &&
            ContainerSlot(solution.SlotOwner, pair) is >= 0 and var slot && slot < frame.Types.Length && frame.Selectors[slot] is not null)
        {
            if (IsExactSource(pattern, solution.SlotOwner, frame) || this.SubstituteType(open, solution.SlotOwner, solution.Types, solution.Lengths) is not { } target)
            {
                return true;
            }

            var admitted = this.AdmittedSemantics(whole, this.ConstraintScope(solution.Function));
            foreach (var mode in SemanticsOrder)
            {
                if ((admitted & mode.ToMask()) != 0 && this.AdaptsAtLayer(part, actual, this.Form(mode, target, null), solution.Scope, solution.DeclaringType))
                {
                    return true;
                }
            }

            return false;
        }

        return pattern is not null and not { Kind: BoundTypeKind.SemanticsApplication } || this.RestsOnSplitCases(
            this.unformedWrites,
            (type, solution.Binder, solution.SlotOwner, frame, solution.Scope, solution.Self, solution.DeclaringType, part, actual),
            static (binding, check) =>
                binding.CallMemberPattern(check.type, check.Binder, check.DeclaringType) is { } member &&
                binding.SubstituteType(binding.ContractType(member, check.Scope, check.Self), check.SlotOwner, check.frame.Types, check.frame.Lengths) is { } formed &&
                !binding.AdaptsAtLayer(check.part, check.actual, formed, check.Scope, check.DeclaringType));
    }

    // SPEC 10.2: whether a typed part adapts at one case's layer of its application and fits it (FitsOpenLayer); none where the layer
    // forms no Type.
    private bool AdaptsAtLayer(Koto part, BoundType actual, BoundType? layer, BindingScope scope, BoundType? declaringType)
        => layer is not null && this.AdaptInput(part, FixedPattern.Open(layer), actual, scope, null, declaringType, out var adapted, out _, out _, deferAcquisition: true) &&
            this.FitsStructuralPartAt(adapted, layer, part);

    // SPEC 10.8 (the owner's O-338 (a)), 8.1.2: whether a typed part at an application over a closed leaf (IsExactSource) disagrees with
    // the evidence its binder's sources gave (IsSource): a Language verdict that reads no unformed Type. A binder fixed before any
    // evidence (explicit Type arguments) compares nothing.
    private bool LeafDisagrees(ref SlotSolution solution, BoundType type, Koto part)
    {
        if (part.BoundType is not { } actual ||
            solution.Pattern(type) is not { Kind: BoundTypeKind.SemanticsApplication, Symbol: { } selector, Components: [var target] } member)
        {
            return false;
        }

        var owner = solution.SlotOwner;
        var frame = solution.Frame;
        return IsEvidencedSource(member, owner, frame) && !this.IsSource(frame, ContainerSlot(owner, selector), actual, target);
    }

    // SPEC 10.8 Solving ("collected together": every source before the expected result fills; G812 ruling 147): the waiting inputs
    // (SettleWaiting) and the wholes recorded since `wholes` (BindWholes) settle each other. A whole's meet can fix a pair binder whose
    // application a waiting input holds (a Tuple literal at an occurrence of the original `s/T`), and an input inferred only then can
    // record a whole. So they alternate until a round's wholes fix no slot. The measure is SourcedSlots, the slots with a Type or a
    // selector: neither pass ever unbinds one within a candidate's trial, so the count never decreases, a round that raises it continues
    // and one that does not ends the settle, so the rounds are at most the call's slots. False when the candidate is inapplicable.
    private bool SettleSources(ref SlotSolution solution, int[] mapping, ref SlotSet waiting, ref SlotSet sourced, int wholes, bool late)
    {
        while (true)
        {
            if (!this.SettleWaiting(ref solution, mapping, ref waiting, ref sourced))
            {
                return false;
            }

            var bound = SourcedSlots(solution.Frame);
            if (!solution.BindWholes(wholes, late))
            {
                return false;
            }

            if (waiting.IsEmpty || SourcedSlots(solution.Frame) == bound)
            {
                return true;
            }
        }
    }

    // SPEC 10.1 step 4, 15.6.1: the expected result fills a candidate's still-unbound slots by its structure, and its Origins are
    // matched and collected; its Origin relations to the completed result are judged at the destination. Returns whether the
    // structure fits; true without an expected result (SPEC 10.8: the slot-free structure of a result that keeps an unsolved slot is
    // still checked).
    private bool InferExpectedResult(ref SlotSolution solution, BoundType? expected)
    {
        var function = solution.Function;
        var declaringType = solution.DeclaringType;
        if (expected is null ||
            (function.IsConstructor && declaringType is not null ? this.SelfType(declaringType.Symbol!) : function.BoundSymbol?.Type) is not { } returnPattern)
        {
            return true;
        }

        // Result expectations use the selected receiver's container Origins, just like inputs.
        // An abstract container binder must not become a rigid call-site lifetime constraint.
        var self = solution.Self;
        returnPattern = this.CallMemberPattern(self is null ? returnPattern : this.ContractType(returnPattern, solution.Scope, self), function, declaringType)!;

        // SPEC 10.8 (two phases): a structured application in the result is formed after the result's own exact sources, so a pattern
        // that held an open application is inferred again while a pass fixed something, whatever the order its parts are written in.
        // SPEC 10.2.2 item 2, 10.8.1, 12.3.1 (PLAN G924): at an expected `ref/U` a returned value that is no borrow value (OwnerResult)
        // is the owner temporary of U, so the expected result is U (ExpectedTypeOf), which inverts nothing; a returned `ref/T` is
        // matched against `ref/U`. A bare slot is what its evidence bound it to, and while unbound a returned value, which U then fills.
        // The result's declared outer Semantics decides (SPEC 10.3 step 2): a declared `s/T` is a Semantics parameter's, matched against
        // `ref/U` by structure (`T = U`, `s = ref`) whatever binder its sources fix.
        var referent = this.ExpectedTypeOf(expected);
        var resultExpected = !ReferenceEquals(referent, expected) &&
            OwnerResult(returnPattern is { Kind: BoundTypeKind.Parameter, IsPairLayer: false } ? solution.Type(returnPattern) ?? returnPattern : returnPattern) ? referent : expected;
        var binder = solution.Solve;
        bool structure;
        bool open;
        int sourced;
        do
        {
            open = this.HasOpenApplication(returnPattern, binder.SlotOwner, solution.Frame);
            sourced = SourcedSlots(solution.Frame);
            structure = this.Infer(returnPattern, resultExpected, binder, solution.Frame, true, true);
        }
        while (structure && open && SourcedSlots(solution.Frame) != sourced);

        this.MatchResultOrigins(returnPattern, resultExpected, function, solution.Origins, solution.Inputs);
        if (returnPattern.CarriesOrigin)
        {
            solution.OriginInference ??= this.BeginOriginInference(solution.Call, function);
            this.CollectOriginInference(returnPattern, resultExpected, solution.OriginInference, solution.Frame.Node, result: true);
        }

        return structure;
    }

    private CandidateApplicability TryCandidateCore(
        ref SlotSolution solution,
        int[] mapping,
        CallArgumentMap argumentMap,
        BoundType? expected,
        Span<BoundArgumentOperation> operations,
        out int defaultsUsed,
        out bool unsolved,
        out ClosureReceiverRefutation? closureReceiver,
        out bool independentRejection,
        out ReferenceConstraintFailure? constraintFailure,
        out bool premiseUnknown)
    {
        var call = solution.Call;
        var function = solution.Function;
        var scope = solution.Scope;
        var frame = solution.Frame;
        var generic = solution.Generic;
        var self = solution.Self;
        var origins = solution.Origins;
        var inputs = solution.Inputs;
        var declaringType = solution.DeclaringType;
        var sourced = this.RentSlotSet(call.ArgumentNodes.Count);
        var waiting = this.RentSlotSet(call.ArgumentNodes.Count);
        var open = this.RentSlotSet(call.ArgumentNodes.Count);
        var openSignatures = this.RentSlotSet(call.ArgumentNodes.Count);
        var unboundSlots = this.RentSlotSet(CallSlotCount(function));
        var unsolvedSlots = this.RentSlotSet(CallSlotCount(function));
        var defaults = this.RentSlotSet(function.Parameters.Count);
        solution.Unknown = this.RentSlotSet(call.ArgumentNodes.Count); // The parts whose own pattern forms no Type (UnformedPart).
        try
        {
            defaultsUsed = argumentMap.DefaultsUsed;
            closureReceiver = null;
            unsolved = false;
            independentRejection = false;
            constraintFailure = null;
            premiseUnknown = false;
            if (!argumentMap.InputsValid || (function.IsConstructor && declaringType is null))
            {
                return CandidateApplicability.Inapplicable;
            }

            if (this.InvalidDeclarationContext(function))
            {
                return CandidateApplicability.Error;
            }

            solution.Fixed = this.FixCallSlots(call, function, declaringType, solution.ExplicitTypes, solution.ExplicitLengths, frame, solution.FixedConstruction);
            origins.Slice(0, function.Origins.Count).Clear();
            solution.InputSpan().Clear();

            for (var i = 0; i < CallOwnSlots(function).Count; i++)
            {
                if (CallOwnSlots(function)[i] is not (GenericParameterKoto or LengthParameterKoto))
                {
                    return CandidateApplicability.Pending;
                }
            }

            if (generic is not null)
            {
                if (!ExplicitTypeArgumentsFit(function, generic, solution.ExplicitLengths))
                {
                    return CandidateApplicability.Inapplicable;
                }

                for (var i = 0; i < generic.TypeArguments.Count; i++)
                {
                    if (CallOwnSlots(function)[i] is not LengthParameterKoto && solution.ExplicitTypes[i] is null)
                    {
                        this.trialPending = new(PendingJudgmentKind.FailedTypeArgument, generic.TypeArguments[i]) { Epoch = this.frameEpoch };
                        return CandidateApplicability.Pending;
                    }
                }
            }

            var receiver = this.CallReceiver(generic?.Identifier ?? KotoHelper.UnwrapParentheses(call.Method));
            if (receiver is null && function.BoundSymbol!.ReceiverIndex >= 0 && (generic?.Identifier ?? KotoHelper.UnwrapParentheses(call.Method)) is not (MemberAccessKoto or RequirementCalleeKoto or FormattingKoto { Operation: FormattingOperation.Callee } or SyntheticKoto))
            {
                return CandidateApplicability.Inapplicable;
            }

            var receiverSlot = receiver is null ? -1 : function.BoundSymbol!.ReceiverIndex;
            var receiverPath = receiver is not null && (generic?.Identifier ?? KotoHelper.UnwrapParentheses(call.Method)) is MemberAccessKoto member && this.memberSelections.TryGetValue(member, out var selection) ? selection.Path : null;
            if (receiver is not null)
            {
                if (receiverSlot < 0 || receiver.BoundType is not { } receiverType)
                {
                    return CandidateApplicability.Inapplicable;
                }

                if (function.Parameters[receiverSlot].Type.BoundType is { } parameterType &&
                    !solution.InferInput(parameterType, receiverType, receiver, receiverPath, receiver: true))
                {
                    if (this.CallMemberPattern(parameterType, function, declaringType) is { } memberType && this.ContractType(memberType, scope, self) is { } required && SharedObjectAuthorityMismatch(receiverType, required))
                    {
                        operations[^1] = new(receiver, receiverType, required, ArgumentOperationKind.Value, ArgumentAdaptation.Exact, receiverPath, receiverSlot);
                    }

                    return CandidateApplicability.Inapplicable;
                }
            }

            var contextualInputs = false;
            var contextualCallables = false;
            var waitingCallables = false;
            var wholes = this.wholes.Count;
            for (var i = 0; i < call.ArgumentNodes.Count; i++)
            {
                var part = call.ArgumentNodes[i];
                var kind = this.KindOf(part);
                contextualInputs |= kind is PartKind.Aggregate or PartKind.Join;
                contextualCallables |= kind is PartKind.Function or PartKind.Reference;
                if (function.Parameters[mapping[i]].Type.BoundType is not { } type)
                {
                    continue; // A parameter Type that failed to bind is a wildcard (rule W; TryCandidate reads it at the verdict).
                }

                if (this.HasOpenApplication(type, CallSlotOwner(function), frame))
                {
                    waiting.Add(i); // SPEC 10.8: an application is formed only after every source of its binder (SettleWaiting).
                    continue;
                }

                var mark = this.unformedWrites;
                if ((solution.Pattern(type) is { } evidencePattern
                    ? !solution.InferArgument(evidencePattern, part, late: false) : part.BoundType is not null) &&
                    this.UnformedPart(ref solution, type, i, this.FormsNone(type, mark)) is not true)
                {
                    if (part.BoundType is { } actual)
                    {
                        independentRejection = function.IsConstructor && IndependentInputPattern(type) && !actual.CarriesOrigin;
                        // Retain this failed comparison in existing candidate scratch space. It is used only if no candidate
                        // applies; a successful overload selection publishes nothing from rejected alternatives.
                        if (DifferentRangeShapes(actual, type))
                        {
                            operations[i] = new(part, actual, type, ArgumentOperationKind.Value, ArgumentAdaptation.Exact, ParameterIndex: mapping[i]);
                        }
                    }

                    return CandidateApplicability.Inapplicable;
                }
            }

            if (!this.SettleSources(ref solution, mapping, ref waiting, ref sourced, wholes, late: false) ||
                !solution.InferCallableSignatures(mapping, operations))
            {
                return CandidateApplicability.Inapplicable;
            }

            // Supplied inputs have an independent mapping even when a required input is absent.
            // Preserve their known signature-refusal facts before rejecting the incomplete call.
            if (!argumentMap.Valid)
            {
                return CandidateApplicability.Inapplicable;
            }

            // Established input types cannot change; expectations only fill unresolved slots.
            var expectedStructure = this.InferExpectedResult(ref solution, expected);

            // SPEC 10.8: the expected result is the last source of a binder; an input formed only now is inferred now.
            if (!this.SettleSources(ref solution, mapping, ref waiting, ref sourced, wholes, late: false))
            {
                return CandidateApplicability.Inapplicable;
            }

            if (argumentMap.DefaultsUsed != 0)
            {
                DefaultSlots(function, mapping.AsSpan(0, call.ArgumentNodes.Count), receiverSlot, ref defaults);
            }

            if (!this.SolveLate(ref solution, mapping, defaults, ref waiting, ref sourced, ref unboundSlots))
            {
                return CandidateApplicability.Inapplicable;
            }

            if (receiverSlot >= 0 && function.Parameters[receiverSlot].Type.BoundType is { } receiverPattern)
            {
                var requiredReceiver = solution.Type(receiverPattern);
                if (requiredReceiver is null)
                {
                    return CandidateApplicability.Pending;
                }

                // SPEC 15.6.1: applicability uses the structural part of each fit; its Origin relations are judged after selection.
                if (!this.AdaptInput(
                        receiver!,
                        FixedPattern.Open(requiredReceiver),
                        receiver!.BoundType!,
                        scope,
                        receiverPath,
                        declaringType,
                        out var adaptedReceiver,
                        out var quality,
                        out var kind,
                        receiver: true,
                        deferAcquisition: true) ||
                    !this.FitsStructuralPartAt(adaptedReceiver, requiredReceiver, call))
                {
                    if (SharedObjectAuthorityMismatch(receiver.BoundType!, requiredReceiver))
                    {
                        operations[^1] = new(receiver, receiver.BoundType, requiredReceiver, ArgumentOperationKind.Value, ArgumentAdaptation.Exact, receiverPath, receiverSlot);
                    }

                    return CandidateApplicability.Inapplicable;
                }

                operations[^1] = new(receiver, receiver.BoundType, requiredReceiver, kind, quality, receiverPath, receiverSlot, AdaptedType: adaptedReceiver);
            }

            var ordinaryPasses = contextualInputs ? 2 : 1;
            for (var pass = 0; pass < ordinaryPasses + (contextualCallables ? 1 : 0); pass++)
            {
                for (var i = 0; i < call.ArgumentNodes.Count; i++)
                {
                    var argument = KotoHelper.UnwrapParentheses(call.ArgumentNodes[i]);
                    var kind = this.KindOf(argument);
                    if (solution.Unknown.Contains(i) || function.Parameters[mapping[i]].Type.BoundType is not { } pattern ||
                        (kind is PartKind.Function or PartKind.Reference ? pass != ordinaryPasses :
                        pass == ordinaryPasses || (contextualInputs && (kind is PartKind.Aggregate or PartKind.Join) != (pass == 1))))
                    {
                        continue;
                    }

                    var mark = this.unformedWrites;
                    var type = solution.Type(pattern);
                    var formsNone = this.FormsNone(pattern, mark); // BU-240: its own Type forms none or keeps an unrepresented slot.
                    if (type is null)
                    {
                        if (this.IsWaitingCallable(argument) && solution.CallableSlot(pattern) is { } slotType &&
                            frame.Types[ContainerSlot(solution.SlotOwner, slotType.Symbol!)] is null)
                        {
                            BoundType? signature = null;
                            if (this.TryCallable(slotType, this.ConstraintScope(function), out var required, out _))
                            {
                                signature = solution.Type(required);
                                if (signature is null)
                                {
                                    // SPEC 10.8: every other piece of evidence is in, so a slot of S that is still unbound stays unsolved; the
                                    // candidate stays applicable on its other checks, and the header parts it writes were matched by
                                    // InferClosureHeader. Anything else S lacks keeps the candidate pending.
                                    if (!MentionsSlots(required, CallSlotOwner(function), UnboundSlots(function, frame.Types, frame.Lengths, ref unboundSlots)))
                                    {
                                        return CandidateApplicability.Pending;
                                    }

                                    openSignatures.Add(i);
                                }
                                else if (argument is FunctionKoto anonymous && !this.ClosureSignatureFits(anonymous, signature))
                                {
                                    return CandidateApplicability.Inapplicable;
                                }
                            }

                            // SPEC 10.5: without a Callable Constraint on F the candidate supplies no expectation; it is compared under
                            // 10.4 without the body, and only if it is selected is the argument checked without S.
                            waitingCallables = true;
                            operations[i] = new(call.ArgumentNodes[i], null, signature, ArgumentOperationKind.Value, ArgumentAdaptation.Exact, ParameterIndex: mapping[i]);
                            continue;
                        }

                        // SPEC 10.2.1, 10.5.1: a Literal is no evidence; its default is, once applied to the selected candidate (DefaultArguments),
                        // and before that only checks the slot-free shape of the fit. All established inputs were processed above.
                        if (this.PartDefault(argument, scope) is not { } shape)
                        {
                            if (this.KindOf(argument) != PartKind.Literal &&
                                MentionsSlots(pattern, CallSlotOwner(function), UnboundSlots(function, frame.Types, frame.Lengths, ref unboundSlots)))
                            {
                                // SPEC 10.8: an argument that supplies no evidence, such as an omitted header at `(T) -> i32` or `.None` at
                                // Option<T>, at a parameter Type that holds a slot no evidence binds: the position stays open and the
                                // candidate stays applicable on its other checks; it is completed only if the candidate is selected. Waiting
                                // parts that bind the position's slots are completed with the selected candidate instead (SPEC 10.5.4).
                                // A part checked against its expected Type ranks as its fit there ranks it (ExpectedTypeClass; PLAN G924,
                                // G916): the open position's whole literal or join is the same operation as at a closed one.
                                var adaptation = pattern.Kind == BoundTypeKind.Function && this.IsWaitingCallable(argument) ? ArgumentAdaptation.Erasure
                                    : this.KindOf(argument) is PartKind.Aggregate or PartKind.Join ? ExpectedTypeClass(this.Materialization(argument, pattern, scope)) : ArgumentAdaptation.Exact;
                                operations[i] = new(call.ArgumentNodes[i], null, null, ArgumentOperationKind.Value, adaptation, ParameterIndex: mapping[i]);
                                if (this.WaitingPartsBind(argument, pattern, function, unboundSlots, ref unsolvedSlots))
                                {
                                    waitingCallables = true;
                                }
                                else
                                {
                                    open.Add(i);
                                }

                                continue;
                            }

                            if (this.UnformedPart(ref solution, pattern, i, formsNone) == true)
                            {
                                continue;
                            }

                            return solution.IsComplete() ? CandidateApplicability.Inapplicable : CandidateApplicability.Pending;
                        }

                        if (!solution.FitsLiteralShape(pattern, shape, argument, out var met))
                        {
                            if (DifferentRangeShapes(shape, pattern))
                            {
                                operations[i] = new(
                                    call.ArgumentNodes[i], shape, pattern, ArgumentOperationKind.Value, ArgumentAdaptation.ExpectedTypeFitting, ParameterIndex: mapping[i]);
                            }

                            return CandidateApplicability.Inapplicable;
                        }

                        if (met is null && this.UnformedPart(ref solution, pattern, i, formsNone) == true)
                        {
                            continue; // A Literal that fits the target of a pattern that forms no Type (FitsLiteralShape): unknown.
                        }

                        type = solution.Type(pattern);
                        if (type is null)
                        {
                            if (MentionsSlots(pattern, CallSlotOwner(function), UnboundSlots(function, frame.Types, frame.Lengths, ref unboundSlots)))
                            {
                                // A Literal waits for its default, which cannot invert a projection or an unresolved selector either. SPEC
                                // 10.2.2's Literal row, 10.2.1 (PLAN G915): the open position is ranked by the operation the pattern it meets
                                // needs whatever the slot binds: the borrow of its temporary under a ref layer (TemporaryTarget, as FitLiteral
                                // ranks a closed one), else the Literal fit; so it ties with a candidate that fits the Literal, and step 2 decides.
                                open.Add(i);
                                var adaptation = this.Materialization(argument, met, scope) is null ? ArgumentAdaptation.ExpectedTypeFitting : ArgumentAdaptation.CrossSemanticsBorrow;
                                operations[i] = new(call.ArgumentNodes[i], null, null, ArgumentOperationKind.Value, adaptation, ParameterIndex: mapping[i]);
                                continue;
                            }

                            return CandidateApplicability.Pending;
                        }
                    }

                    // SPEC 10.7, 10.8: an argument with a per-call stand-in is decided by it (FitPerCall), a fact of the solve, never by
                    // its fit failing first.
                    BoundArgumentOperation operation;
                    var applicability = solution.HasPerCallStandIn(call.ArgumentNodes[i], type)
                        ? solution.FitPerCall(call.ArgumentNodes[i], type, out operation)
                        : this.FitArgument(call.ArgumentNodes[i], type, scope, call, out operation);

                    if (operation.Source is not null)
                    {
                        operations[i] = operation with { ParameterIndex = mapping[i] };
                    }

                    waitingCallables |= applicability == CandidateApplicability.Waiting;
                    if (applicability is not (CandidateApplicability.Applicable or CandidateApplicability.Waiting))
                    {
                        if (this.UnformedPart(ref solution, pattern, i, formsNone) == true)
                        {
                            continue; // BU-240: the fit failed at a Type formed with an unrepresented slot (HasOuterSlot).
                        }

                        return applicability;
                    }
                }
            }

            for (var i = 0; i < call.ArgumentNodes.Count; i++)
            {
                // DECISIONS BU-240 (C-359 C1): a Function Item part left unknown through its own signature fails where no case fits it.
                if (solution.Unknown.Contains(i) && this.UnformedItemFails(ref solution, function.Parameters[mapping[i]].Type.BoundType!, call.ArgumentNodes[i]))
                {
                    return CandidateApplicability.Inapplicable;
                }
            }

            // Fitted literals can add Origin evidence after the first contextual pass.
            // Publish only the final substituted parameter Types, never preliminary binders.
            if (solution.OriginInference is not null &&
                !this.SolveCallOriginInference(solution.OriginInference, origins, inputs, [], declaringType, select: true))
            {
                return CandidateApplicability.Inapplicable;
            }

            // SPEC 15.3.6: an Origin without a candidate is a local region of the call.
            this.OpenUnsolvedOrigins(call, origins.Slice(0, function.Origins.Count), solution.InputSpan());

            // SPEC 10.8: required structural slots, length slots included, remain rankable. An open position closed by a later literal
            // default, or a slot an environment result left without evidence, still keeps the candidate pending.
            this.UnsolvedSlots(call, function, frame.Types, frame.Lengths, mapping, ref unsolvedSlots);
            var environment = this.EnvironmentJudgment(function, frame, unsolvedSlots);
            if ((!open.IsEmpty || !openSignatures.IsEmpty) &&
                (environment is not null || !this.OpenPositionsHold(solution, mapping, open, openSignatures, unsolvedSlots)))
            {
                return this.PendingOnEnvironment(environment);
            }

            if (environment is not null)
            {
                unsolvedSlots.Clear();
            }

            for (var i = 0; i < function.Parameters.Count; i++)
            {
                // SPEC 10.8: a parameter Type that holds an unsolved slot, an open position or an unused default, is no failure of its own;
                // nor one that forms no Type (BU-240: its part is unknown, UnformedPart).
                var mark = this.unformedWrites;
                if (function.Parameters[i].Type.BoundType is { } parameter && (solution.Type(parameter) is not { } completed
                    ? !(waitingCallables || MentionsSlots(parameter, CallSlotOwner(function), unsolvedSlots) || this.FormsNone(parameter, mark))
                    : !this.ProveTypeLengths(completed, scope.Function)))
                {
                    return CandidateApplicability.Inapplicable;
                }
            }

            solution.CompleteOperationTypes(operations);

            var resultPattern = function.IsConstructor && declaringType is not null ? this.SelfType(declaringType.Symbol!) : function.BoundSymbol!.Type;
            var resultMark = this.unformedWrites;
            var result = resultPattern is not null ? solution.Type(resultPattern) : null;
            if (result is not null && !this.ProveTypeLengths(result, scope.Function))
            {
                return CandidateApplicability.Inapplicable;
            }

            // SPEC 10.8: a result that keeps an unsolved slot is no failure of its own; the parts of its fit that do not contain the slot are
            // still checked against an expected result (SPEC 10.3). Nor is one that forms no Type (BU-240, as a parameter Type above).
            var openResult = result is null && resultPattern is not null &&
                (MentionsSlots(resultPattern, CallSlotOwner(function), unsolvedSlots) || this.FormsNone(resultPattern, resultMark));
            if (resultPattern is not null && result is null && !waitingCallables && !openResult)
            {
                // A result Type names no Origin of the callee: OpenUnsolvedOrigins filled every Origin and input slot (SPEC 15.3.6; PLAN
                // G410). DECISIONS BU-86: only a result without a Type rests on the environment judgment.
                return solution.IsComplete() ? CandidateApplicability.Inapplicable : this.PendingOnEnvironment(environment);
            }

            if (expected is not null && result is not null && !this.FitsStructuralPartAt(result, this.ContractType(expected, scope), call) &&
                this.ExpectedAdaptation(call, result, this.ContractType(expected, scope), recordPair: false) is null)
            {
                return CandidateApplicability.Inapplicable;
            }
            else if (expected is not null && result is null && openResult && !expectedStructure)
            {
                return CandidateApplicability.Inapplicable;
            }

            var proofMark = this.unformedWrites;
            var slots = frame.Types[..CallOwnSlots(function).Count];
            var proof = solution.Prove(slots, waitingCallables, unsolvedSlots);
            var splitDecided = this.ReadsSplitDecided(proofMark, slots);
            if (proof is ConstraintProof.Refuted or ConstraintProof.Error && splitDecided)
            {
                // BU-240: a proof that read a position a split binding decides (a Type no binding represents, dt2; a slot holding the open
                // formed Type's mark, G812 ruling 156) is Unknown through the split.
                proof = ConstraintProof.Unknown;
            }

            // BU-240 (G812 rulings 141, 156): an Unknown proof over pair slots a binding that forms none sources (L2), or over a position a split
            // binding decides, reads the split without its case.
            this.premiseReadsSplit = proof == ConstraintProof.Unknown && (splitDecided || this.SelectsUnformed(frame.Selectors[..CallSlotCount(function)]));

            var failure = ClauseFailure(this.TakeTrialClause(call), function.BoundSymbol); // The trial's clause fact, before a later proof's.
            if (proof is ConstraintProof.Refuted or ConstraintProof.Unknown && !waitingCallables && solution.PerCallCallables(mapping, operations))
            {
                proof = ConstraintProof.Proven;
            }

            // SPEC 10.1 step 5, 7.7.4: one ordering for the step: a candidate its own Constraints or premises refute is inapplicable whatever
            // an erased argument's limit says, so the erasures' environment (which orders its own arguments so) is read only when nothing
            // has refuted the candidate; the located limit only where nothing does.
            if (proof != ConstraintProof.Refuted)
            {
                proof = CombineProof(proof, this.ErasedArgumentsEnvironment(operations), true);
            }

            constraintFailure = proof == ConstraintProof.Unknown ? failure : null; // SPEC 8.3: a Pending candidate's explanation.

            if (waitingCallables && proof == ConstraintProof.Unknown &&
                this.ResolvedClauseUnknown(function.TypeConstraints, solution.Substitution(frame.Types[..CallSlotCount(function)]), scope, self, call))
            {
                // SPEC 10.5, 8.3: a Constraint that no waiting argument leaves open, such as a Callable clause on an F that an independent
                // argument binds, waits on nothing; Unknown proves neither applicability nor negation, so another candidate is never
                // selected past it.
                return CandidateApplicability.Pending;
            }

            if (waitingCallables && proof is ConstraintProof.Proven or ConstraintProof.Unknown)
            {
                // Only the concrete Types of waiting anonymous arguments may remain open. A body cannot solve an outer input/result slot,
                // even if its inferred signature would happen to provide that Type: such a slot is unsolved (SPEC 10.8).
                if (environment is not null)
                {
                    return CandidateApplicability.Pending;
                }

                unsolved = !unsolvedSlots.IsEmpty;
                return CandidateApplicability.Waiting;
            }

            if (proof == ConstraintProof.Refuted)
            {
                closureReceiver = this.PermittedClosureReceiver(mapping, solution, waitingCallables);
            }

            // Retain only a condition actually reached after the argument/result checks.
            if (proof == ConstraintProof.Refuted && closureReceiver is null)
            {
                constraintFailure = failure;
            }

            unsolved = proof is ConstraintProof.Proven or ConstraintProof.Unknown or ConstraintProof.Refuted && !unsolvedSlots.IsEmpty; // Refuted: CheckDefaulted's selected candidate.
            premiseUnknown = proof == ConstraintProof.Unknown; // SPEC 8.3.3: every argument and result check has passed.
            return proof switch
            {
                // SPEC 10.1: an Unknown is an obligation checked after selection.
                ConstraintProof.Proven or ConstraintProof.Unknown => CandidateApplicability.Applicable,
                ConstraintProof.Refuted => CandidateApplicability.Inapplicable,
                ConstraintProof.Error => CandidateApplicability.Error,
                _ => CandidateApplicability.Pending,
            };
        }
        finally
        {
            this.ReturnSlotSet(solution.Unknown);
            this.ReturnSlotSet(defaults);
            this.ReturnSlotSet(unsolvedSlots);
            this.ReturnSlotSet(unboundSlots);
            this.ReturnSlotSet(openSignatures);
            this.ReturnSlotSet(open);
            this.ReturnSlotSet(waiting);
            this.ReturnSlotSet(sourced);
        }
    }

    // SPEC 10.2.1, 4.6.2, 15.6.4 steps 1, 3: after the expected result, the late pass of the evidence walk (the Literal parts' defaults
    // applied to the selected candidate and range typing), then the arguments' root rows (Contribute) and the first solve of the
    // candidate's Origins, which the well-formedness of each instantiated input (F1, R4-M164: each leaf row of an argument and the
    // receiver, CollectPositionPremises, and each default it uses, `defaults`, DefaultSlots; PLAN G1115) shortens once the slots are
    // solved, after the variables that a waiting part bounds too are opened (J2, SlotSolution.OpenWaited); false when the candidate is
    // inapplicable. Before the walk it publishes the slots that steps 3 and 4 left unbound in `left` (SlotSolution.Left): only they take
    // their literals whole in it (SPEC 4.3.1 Places, 10.8).
    private bool SolveLate(ref SlotSolution solution, int[] mapping, in SlotSet defaults, ref SlotSet waiting, ref SlotSet sourced, ref SlotSet left)
    {
        var call = solution.Call;
        var function = solution.Function;
        var wholes = this.wholes.Count;
        solution.LeaveUnbound(ref left);
        for (var i = 0; i < call.ArgumentNodes.Count; i++)
        {
            if (solution.Unknown.Contains(i))
            {
                continue;
            }

            if (function.Parameters[mapping[i]].Type.BoundType is not { } parameter)
            {
                continue;
            }

            var mark = this.unformedWrites;
            if (solution.Pattern(parameter) is { } latePattern && !solution.InferArgument(latePattern, call.ArgumentNodes[i], late: true) &&
                this.UnformedPart(ref solution, parameter, i, this.FormsNone(parameter, mark)) is not true)
            {
                return false;
            }
        }

        // SPEC 10.8, 10.2.1: the defaults bind what no evidence bound (the owner's O-343 (a), O-347 (a)); an input formed only now is
        // inferred now, and the wholes the defaults or it give are bound (SettleSources).
        if (!this.SettleSources(ref solution, mapping, ref waiting, ref sourced, wholes, late: true))
        {
            return false;
        }

        solution.OpenSlotVariables(); // G1128: before the rows read the slots.
        solution.Contribute(mapping);
        solution.FirstSolve = !solution.FixedConstruction && !solution.ChildSlice.IsEmpty && solution.ChildSlice[1] < 0; // Before the rows are composed.
        this.CollectPositionPremises(ref solution);
        try
        {
            if (solution.OriginInference is null && this.originDeclarations.GetValueOrDefault(function) is { Relations.Length: > 0 })
            {
                solution.OriginInference = this.BeginOriginInference(call, function);
            }

            for (var i = 0; solution.OriginInference is { } inference && i < function.Parameters.Count; i++)
            {
                if (defaults.Contains(i) && function.Parameters[i].Type.BoundType is { } parameter && solution.Pattern(parameter) is { } pattern &&
                    this.SubstituteType(pattern, solution.SlotOwner, solution.Types, solution.Lengths) is { } instantiated)
                {
                    this.DiscoverPremiseOrigins(solution, inference, instantiated);
                    this.CollectInputPremises(pattern, instantiated, inference, call); // Rule C: over the instantiated parameter Type (SPEC 15.6.4 step 3).
                }
            }

            solution.OpenWaited(); // J2: the Origin parameters' half, after every premise of the solve, before the solve.
            // G1128 R1.3 (e) 2: the slot variables' values, a span of the width measured at the slot step, for this solve only (the
            // common path rents nothing).
            var slotCount = solution.OriginInference?.SlotVariableCount ?? 0;
            var slotValues = this.originScratch.Rent(slotCount);
            bool applicable;
            try
            {
                applicable = solution.OriginInference is not { } solved ||
                    this.SolveCallOriginInference(solved, solution.Origins, solution.Inputs, slotValues.AsSpan(0, slotCount), solution.DeclaringType, select: true);
                // G1128: the solve's values replace the slot variables; the root rows read them, then J2 opens the waited slots over them.
                if (solution.RestoreSlotValues(slotValues.AsSpan(0, slotCount)))
                {
                    solution.Contribute(mapping);
                }
            }
            finally
            {
                this.originScratch.Return(slotValues, clearArray: true);
            }

            solution.OpenWaitedSlots(); // J2: the Type slots' half, before the operations' Types.
            return applicable;
        }
        finally
        {
            this.ReturnSlotSet(solution.EvidenceNamed);
        }
    }
}
