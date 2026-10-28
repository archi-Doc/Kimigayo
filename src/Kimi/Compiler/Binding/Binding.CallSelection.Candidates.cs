// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// SPEC 10.2-10.8: evaluates one named-call candidate from prepared argument slots, expected result and Constraints.
// Returns its existing applicability and inference facts. The candidate scope restores saved inference state on every exit;
// lookup, ranking and final call publication remain with their existing owners.
public sealed partial class Binding
{
    // SPEC 10.8, 15.3.6: the Type slots of the candidate being evaluated whose binding comes from an invariant position.
    private SlotSet invariantSlots;
    private FunctionKoto? activeInferenceFunction;

    // Set when a known call signature's result over its closure's hidden environment receiver supplied no evidence for a slot: that slot
    // is not unsolved but judged by the Callable proof or the conversion (SPEC 8.10, 7.7.4).
    private bool environmentEvidence;

    // The invariant slot bindings belong to one candidate; a nested call bound while it is evaluated keeps its own.
    // `unsolved` holds the required structural slots that no evidence binds (SPEC 10.8); an Applicable or Waiting candidate with such
    // slots stays rankable, and its selection is the inference-boundary record (FailUnboundSlots).
    private CandidateApplicability TryCandidate(
        SlotSolution solution,
        BoundLength?[] explicitLengths,
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

        var saved = this.invariantSlots;
        var savedInferenceFunction = this.activeInferenceFunction;
        this.activeInferenceFunction = solution.Function;
        var savedEnvironment = this.environmentEvidence;
        var savedAcquisition = this.acquisitionFailure;
        this.acquisitionFailure = null;
        this.invariantSlots = this.RentSlotSet(CallSlotCount(solution.Function));
        this.environmentEvidence = false;
        try
        {
            var result = this.TryCandidateCore(
                ref solution,
                explicitLengths,
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

                return CandidateApplicability.Inapplicable;
            }

            return result;
        }
        finally
        {
            this.ReturnSlotSet(this.invariantSlots);
            this.invariantSlots = saved;
            this.activeInferenceFunction = savedInferenceFunction;
            this.environmentEvidence = savedEnvironment;
            this.acquisitionFailure = savedAcquisition;
        }
    }

    private CandidateApplicability TryCandidateCore(
        ref SlotSolution solution,
        BoundLength?[] explicitLengths,
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
        var arguments = solution.Types;
        var lengths = solution.Lengths;
        var generic = solution.Generic;
        var self = solution.Self;
        var origins = solution.Origins;
        var inputs = solution.Inputs;
        var declaringType = solution.DeclaringType;
        var waiting = this.RentSlotSet(call.ArgumentNodes.Count);
        var open = this.RentSlotSet(call.ArgumentNodes.Count);
        var openSignatures = this.RentSlotSet(call.ArgumentNodes.Count);
        var unboundSlots = this.RentSlotSet(CallSlotCount(function));
        var unsolvedSlots = this.RentSlotSet(CallSlotCount(function));
        var defaults = this.RentSlotSet(function.Parameters.Count);
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

            // SPEC 8.13.3: specializations never enter the candidate set, so an invalid one cannot fail a call.
            if (function.IsSpecialization)
            {
                return CandidateApplicability.Inapplicable;
            }

            if (this.InvalidDeclarationContext(function))
            {
                return CandidateApplicability.Error;
            }

            this.InitializeCallSlots(call, function, declaringType, arguments, solution.FixedConstruction);
            Array.Clear(lengths, 0, CallSlotCount(function));
            Array.Clear(origins, 0, function.Origins.Count);
            Array.Clear(inputs, 0, Math.Min(inputs.Length, InputOriginCount(function)));

            for (var i = 0; i < CallOwnSlots(function).Count; i++)
            {
                if (CallOwnSlots(function)[i] is not (GenericParameterKoto or LengthParameterKoto))
                {
                    return CandidateApplicability.Pending;
                }
            }

            if (generic is not null)
            {
                if (generic.TypeArguments.Count != CallOwnSlots(function).Count)
                {
                    return CandidateApplicability.Inapplicable;
                }

                for (var i = 0; i < generic.TypeArguments.Count; i++)
                {
                    if (CallOwnSlots(function)[i] is LengthParameterKoto)
                    {
                        if ((lengths[i] = explicitLengths[i]) is null)
                        {
                            return CandidateApplicability.Inapplicable;
                        }

                        continue;
                    }

                    if (explicitLengths[i] is not null)
                    {
                        return CandidateApplicability.Inapplicable;
                    }

                    arguments[i] = generic.TypeArguments[i].BoundType;
                    if (arguments[i] is null)
                    {
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

                if (function.Parameters[receiverSlot].Type.BoundType is not { } parameterType)
                {
                    return CandidateApplicability.Pending;
                }

                if (!solution.InferInput(parameterType, receiverType, receiver, receiverPath, receiver: true))
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
            for (var i = 0; i < call.ArgumentNodes.Count; i++)
            {
                var part = call.ArgumentNodes[i];
                contextualInputs |= this.KindOf(part) == ArgumentKind.Composite;
                contextualCallables |= KotoHelper.UnwrapParentheses(part) is FunctionKoto { IsAnonymous: true } || IsWaitingFunctionReference(part);
                var type = function.Parameters[mapping[i]].Type.BoundType;
                if (type is null)
                {
                    return CandidateApplicability.Pending;
                }

                if (this.FormedApplication(type, CallSlotOwner(function), arguments).Kind == BoundTypeKind.SemanticsApplication)
                {
                    waiting.Add(i); // SPEC 10.2: solved together; s/U is matched once another argument fixes s.
                    continue;
                }

                if (solution.Pattern(type) is { } evidencePattern
                    ? !solution.InferArgument(evidencePattern, part, late: false) : part.BoundType is not null)
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

            for (var i = 0; !waiting.IsEmpty && i < call.ArgumentNodes.Count; i++)
            {
                if (waiting.Contains(i) && (solution.Pattern(function.Parameters[mapping[i]].Type.BoundType!) is { } evidencePattern
                    ? !solution.InferArgument(evidencePattern, call.ArgumentNodes[i], late: false) : call.ArgumentNodes[i].BoundType is not null))
                {
                    return CandidateApplicability.Inapplicable;
                }
            }

            if (!solution.InferCallableSignatures(mapping, operations))
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
            var expectedStructure = true; // SPEC 10.8: the slot-free structure of a result that keeps an unsolved slot is still checked.
            if (expected is not null && (function.IsConstructor && declaringType is not null ? this.SelfType(declaringType.Symbol!) : function.BoundSymbol?.Type) is { } returnPattern)
            {
                // Result expectations use the selected receiver's container Origins, just like inputs.
                // An abstract container binder must not become a rigid call-site lifetime constraint.
                returnPattern = this.CallMemberPattern(self is null ? returnPattern : this.ContractType(returnPattern, scope, self), function, declaringType)!;

                // SPEC 10.1 step 4, 15.6.1: the expected result fills the still-unbound slots by its structure; its Origin relations to
                // the completed result are judged at the destination.
                expectedStructure = this.Infer(returnPattern, expected, CallSlotOwner(function), arguments, true, lengths);
                this.MatchResultOrigins(returnPattern, expected, function, origins, inputs);
                if (returnPattern.CarriesOrigin)
                {
                    solution.OriginInference ??= this.BeginOriginInference(call, function);
                    this.CollectOriginInference(returnPattern, expected, solution.OriginInference, result: true);
                }
            }

            if (argumentMap.DefaultsUsed != 0)
            {
                DefaultSlots(function, mapping.AsSpan(0, call.ArgumentNodes.Count), receiverSlot, ref defaults);
            }

            if (!this.SolveLate(ref solution, mapping, defaults))
            {
                return CandidateApplicability.Inapplicable;
            }

            if (receiverSlot >= 0)
            {
                var requiredReceiver = solution.Type(function.Parameters[receiverSlot].Type.BoundType!);
                if (requiredReceiver is null)
                {
                    return CandidateApplicability.Pending;
                }

                // SPEC 15.6.1: applicability uses the structural part of each fit; its Origin relations are judged after selection.
                if (!this.AdaptInput(receiver!, requiredReceiver, receiver!.BoundType!, scope, receiverPath, declaringType, out var adaptedReceiver, out var quality, out var kind, receiver: true, deferAcquisition: true) || !this.FitsStructurallyAt(adaptedReceiver, requiredReceiver, call))
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
                    if (argument is FunctionKoto { IsAnonymous: true } || IsWaitingFunctionReference(argument) ? pass != ordinaryPasses :
                        pass == ordinaryPasses || (contextualInputs && (this.KindOf(argument) == ArgumentKind.Composite) != (pass == 1)))
                    {
                        continue;
                    }

                    var type = solution.Type(function.Parameters[mapping[i]].Type.BoundType!);
                    if (type is null)
                    {
                        var pattern = function.Parameters[mapping[i]].Type.BoundType!;
                        if (IsWaitingCallable(argument) && solution.CallableSlot(pattern) is { } slotType &&
                            arguments[ContainerSlot(solution.SlotOwner, slotType.Symbol!)] is null)
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
                                    if (!MentionsSlots(required, CallSlotOwner(function), UnboundTypeSlots(function, arguments, ref unboundSlots)))
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
                            if (this.KindOf(argument) != ArgumentKind.Literal &&
                                MentionsSlots(pattern, CallSlotOwner(function), UnboundTypeSlots(function, arguments, ref unboundSlots)))
                            {
                                // SPEC 10.8: an argument that supplies no evidence, such as an omitted header at `(T) -> i32` or `.None` at
                                // Option<T>, at a parameter Type that holds a slot no evidence binds: the position stays open and the
                                // candidate stays applicable on its other checks; it is completed only if the candidate is selected. Waiting
                                // parts that bind the position's slots are completed with the selected candidate instead (SPEC 10.5.4).
                                var adaptation = pattern.Kind == BoundTypeKind.Function && IsWaitingCallable(argument)
                                    ? ArgumentAdaptation.Erasure : ArgumentAdaptation.Exact;
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

                            return solution.IsComplete() ? CandidateApplicability.Inapplicable : CandidateApplicability.Pending;
                        }

                        if (!solution.FitsLiteralShape(pattern, shape))
                        {
                            if (DifferentRangeShapes(shape, pattern))
                            {
                                operations[i] = new(
                                    call.ArgumentNodes[i], shape, pattern, ArgumentOperationKind.Value, ArgumentAdaptation.Literal, ParameterIndex: mapping[i]);
                            }

                            return CandidateApplicability.Inapplicable;
                        }

                        type = solution.Type(pattern);
                        if (type is null)
                        {
                            if (MentionsSlots(pattern, CallSlotOwner(function), UnboundTypeSlots(function, arguments, ref unboundSlots)))
                            {
                                // A Literal waits for its default, which cannot invert a projection or an unresolved selector either.
                                open.Add(i);
                                operations[i] = new(call.ArgumentNodes[i], null, null, ArgumentOperationKind.Value, ArgumentAdaptation.Literal, ParameterIndex: mapping[i]);
                                continue;
                            }

                            return CandidateApplicability.Pending;
                        }
                    }

                    var applicability = this.FitArgument(call.ArgumentNodes[i], type, scope, call, out var operation);
                    if (applicability == CandidateApplicability.Inapplicable && type.Kind == BoundTypeKind.Function)
                    {
                        applicability = solution.FitPerCall(call.ArgumentNodes[i], type, out operation);
                    }

                    if (operation.Source is not null)
                    {
                        operations[i] = operation with { ParameterIndex = mapping[i] };
                    }

                    waitingCallables |= applicability == CandidateApplicability.Waiting;
                    if (applicability is not (CandidateApplicability.Applicable or CandidateApplicability.Waiting))
                    {
                        return applicability;
                    }

                    // SPEC 10.2: a borrowed literal temporary's Place Origin binds the parameter's input Origin like any other borrowed temporary.
                    if (operation is { Kind: ArgumentOperationKind.Borrow, AdaptedType: not null } && argument.BoundType is null &&
                        !solution.InferInput(function.Parameters[mapping[i]].Type.BoundType!, operation.SourceType!, argument))
                    {
                        return CandidateApplicability.Inapplicable;
                    }
                }
            }

            // Fitted literals can add Origin evidence after the first contextual pass.
            // Publish only the final substituted parameter Types, never preliminary binders.
            if (solution.OriginInference is not null &&
                !this.SolveCallOriginInference(function, solution.OriginInference, origins, inputs, call, declaringType, select: true))
            {
                return CandidateApplicability.Inapplicable;
            }

            // SPEC 10.8: required structural slots remain rankable. An unresolved length or an
            // open position closed by a later literal default still keeps the candidate pending.
            this.UnsolvedSlots(call, function, arguments, lengths, mapping, ref unsolvedSlots, out var unrepresentable);
            unrepresentable |= this.environmentEvidence;
            if ((!open.IsEmpty || !openSignatures.IsEmpty) &&
                (unrepresentable || !this.OpenPositionsHold(solution, mapping, open, openSignatures, unsolvedSlots)))
            {
                return CandidateApplicability.Pending;
            }

            if (unrepresentable)
            {
                unsolvedSlots.Clear();
            }

            for (var i = 0; i < function.Parameters.Count; i++)
            {
                var completed = solution.Type(function.Parameters[i].Type.BoundType!);
                // SPEC 10.8: a parameter Type that holds an unsolved slot, an open position or an unused default, is no failure of its own.
                if (completed is null ? !(waitingCallables || MentionsSlots(function.Parameters[i].Type.BoundType!, CallSlotOwner(function), unsolvedSlots)) : !this.ProveTypeLengths(completed, scope.Function))
                {
                    return CandidateApplicability.Inapplicable;
                }
            }

            solution.CompleteOperationTypes(operations);

            var resultPattern = function.IsConstructor && declaringType is not null ? this.SelfType(declaringType.Symbol!) : function.BoundSymbol!.Type;
            var result = resultPattern is not null ? solution.Type(resultPattern) : null;
            if (result is not null && !this.ProveTypeLengths(result, scope.Function))
            {
                return CandidateApplicability.Inapplicable;
            }

            if (result is not null && (HasUnsubstitutedOrigin(result, function) || !defaults.IsEmpty) &&
                this.OpenResultOnlyOrigins(call, function, origins, defaults))
            {
                // SPEC 15.3.6: an Origin of the callee that nothing at the call bounds, such as the result-only `s` of `constant() ->
                // ref/i32 during s` initializing an unannotated local, or one only a default names, is a local region of the call, never
                // a pending candidate.
                result = solution.Type(resultPattern!);
            }

            // SPEC 10.8: a result that keeps an unsolved slot is no failure of its own; the parts of its fit that do not contain the slot are
            // still checked against an expected result (SPEC 10.3).
            var openResult = result is null && resultPattern is not null && MentionsSlots(resultPattern, CallSlotOwner(function), unsolvedSlots);
            if ((result is null && !waitingCallables && !openResult) || (result is not null && HasUnsubstitutedOrigin(result, function)))
            {
                // Result-only Origin inference needs the later call-site solver. Never retain a
                // requirement's abstract binder as though it were this call's concrete Origin.
                return result is null && solution.IsComplete() ? CandidateApplicability.Inapplicable : CandidateApplicability.Pending;
            }

            if (expected is not null && result is not null && !this.FitsStructurallyAt(result, this.ContractType(expected, scope), call) &&
                this.ExpectedAdaptation(call, result, this.ContractType(expected, scope), recordPair: false) is null)
            {
                return CandidateApplicability.Inapplicable;
            }
            else if (expected is not null && result is null && openResult && !expectedStructure)
            {
                return CandidateApplicability.Inapplicable;
            }

            var proof = solution.Prove(arguments.AsSpan(0, CallOwnSlots(function).Count), waitingCallables, unsolvedSlots);
            if (proof is ConstraintProof.Refuted or ConstraintProof.Unknown && !waitingCallables && solution.PerCallCallables(mapping, operations))
            {
                proof = ConstraintProof.Proven;
            }

            if (waitingCallables && proof == ConstraintProof.Unknown &&
                this.ResolvedClauseUnknown(function.TypeConstraints, function, arguments.AsSpan(0, CallSlotCount(function)), scope, self, function.IsConstructor ? null : declaringType, lengths.AsSpan(0, CallOwnSlots(function).Count), slotOwner: CallSlotOwner(function)))
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
                if (unrepresentable)
                {
                    return CandidateApplicability.Pending;
                }

                unsolved = !unsolvedSlots.IsEmpty;
                return CandidateApplicability.Waiting;
            }

            if (proof == ConstraintProof.Refuted && this.ClosureReceiverMismatch(call, function, mapping) is { } mismatch)
            {
                // SPEC 7.7.3, 8.10: a closure's minimum call receiver is the candidate's failure only when the candidate applies once that
                // receiver is permitted; a rejected candidate then names both receivers (NoApplicableOverload_Kd).
                this.permitClosureReceivers = true;
                try
                {
                    var permitted = solution.Prove(arguments.AsSpan(0, CallOwnSlots(function).Count), waitingCallables);
                    closureReceiver = permitted == ConstraintProof.Proven ? mismatch : null;
                }
                finally
                {
                    this.permitClosureReceivers = false;
                }
            }

            // Retain only a condition actually reached after the argument/result checks, before scratch is reused.
            if (proof == ConstraintProof.Refuted && closureReceiver is null)
            {
                constraintFailure = this.CandidateConstraintFailure(function, arguments, lengths, scope, self, declaringType, ConstraintProof.Refuted);
            }

            unsolved = proof == ConstraintProof.Proven && !unsolvedSlots.IsEmpty;
            premiseUnknown = proof == ConstraintProof.Unknown; // SPEC 8.3.3: every argument and result check has passed.
            return proof switch
            {
                ConstraintProof.Proven => CandidateApplicability.Applicable,
                ConstraintProof.Refuted => CandidateApplicability.Inapplicable,
                ConstraintProof.Error => CandidateApplicability.Error,
                _ => CandidateApplicability.Pending,
            };
        }
        finally
        {
            this.ReturnSlotSet(defaults);
            this.ReturnSlotSet(unsolvedSlots);
            this.ReturnSlotSet(unboundSlots);
            this.ReturnSlotSet(openSignatures);
            this.ReturnSlotSet(open);
            this.ReturnSlotSet(waiting);
        }
    }

    // SPEC 10.2.1, 4.6.2, 15.6.4 steps 1, 3: after the expected result, the late pass of the evidence walk (the Literal parts' defaults
    // applied to the selected candidate and range typing), then the first solve of the candidate's Origins, which the well-formedness of
    // each default it uses (`defaults`, DefaultSlots) shortens as a written part's does; false when the candidate is inapplicable.
    private bool SolveLate(ref SlotSolution solution, int[] mapping, in SlotSet defaults)
    {
        var call = solution.Call;
        var function = solution.Function;
        for (var i = 0; i < call.ArgumentNodes.Count; i++)
        {
            if (solution.Pattern(function.Parameters[mapping[i]].Type.BoundType!) is { } latePattern &&
                !solution.InferArgument(latePattern, call.ArgumentNodes[i], late: true))
            {
                return false;
            }
        }

        if (solution.OriginInference is null && this.originDeclarations.GetValueOrDefault(function)?.Relations.Count > 0)
        {
            solution.OriginInference = this.BeginOriginInference(call, function);
        }

        if (solution.OriginInference is not { } inference)
        {
            return true;
        }

        for (var i = 0; !defaults.IsEmpty && i < function.Parameters.Count; i++)
        {
            if (defaults.Contains(i) && solution.Pattern(function.Parameters[i].Type.BoundType!) is { } pattern)
            {
                this.CollectInputPremises(this.FormedApplication(pattern, solution.SlotOwner, solution.Types), inference, call);
            }
        }

        return this.SolveCallOriginInference(function, inference, solution.Origins, solution.Inputs, call, solution.DeclaringType, select: true);
    }
}
