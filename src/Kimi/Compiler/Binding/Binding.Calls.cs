// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly ScratchBuffers<BoundDefaultArgument> defaultArgumentScratch = new();

    private BoundType? BindCall(InvocationKoto call, BindingScope scope, BoundType? expected)
    {
        // The bound requirement reference of a candidate or winner belongs to its own call (SPEC 8.4.1, 8.6.4): a call bound inside
        // it, such as a later argument, neither sees nor clears the enclosing call's reference.
        var enclosingRequirementContract = this.activeRequirementContract;
        var enclosingAcquisition = this.acquisitionFailure;
        this.activeRequirementContract = null;
        try
        {
            return this.CompleteUnselected(call, scope, this.BindCallCore(call, scope, expected));
        }
        finally
        {
            this.activeRequirementContract = enclosingRequirementContract;
            this.acquisitionFailure = enclosingAcquisition;
        }
    }

    private BoundType? BindCallCore(InvocationKoto call, BindingScope scope, BoundType? expected)
    {
        var callee = call.Method;
        var generic = callee as GenericsKoto;
        if (generic is not null)
        {
            callee = generic.Identifier!;
        }

        BindingSymbol? group;
        if (callee is BaseReferenceKoto baseReference)
        {
            this.BindNode(baseReference, scope);
            group = null;
        }
        else if (callee is IdentifierNameKoto name)
        {
            group = this.Lookup(name.IdentifierName, scope, name, false);
        }
        else if (callee is MemberAccessKoto member && member.Right is not NumberLiteralKoto)
        {
            // The callee is checked in its own frame: what its lookup consults is its prerequisite, not the call's.
            var frame = this.BeginConsultation(member);
            group = this.Member(member, scope, expected);
            this.EndConsultation(member, frame);
        }
        else if (callee is SyntaxFormKoto { Akind: KotoKind.InferredCase } inferred)
        {
            group = this.InferredCase(inferred, scope, expected);
        }
        else
        {
            this.BindNode(callee, scope);
            group = callee.BoundSymbol;
            var inner = KotoHelper.UnwrapParentheses(callee);
            var named = inner is GenericsKoto { Identifier: { } identifier } ? identifier : inner;
            if (callee is ParenthesizedKoto && named is IdentifierNameKoto or MemberAccessKoto && named.BoundSymbol is { Kind: BindingSymbolKind.Function } function)
            {
                // SPEC 12.5.2: parentheses stay part of a referenced function name, so `(f)(1)` is the call `f(1)`; a member is
                // selected again as a callee, with its receiver.
                callee = named;
                generic = inner as GenericsKoto;
                group = function;
                if (named is MemberAccessKoto calleeMember)
                {
                    var frame = this.BeginConsultation(calleeMember);
                    group = this.Member(calleeMember, scope, expected);
                    this.EndConsultation(calleeMember, frame);
                }
            }
        }

        if (group?.EnumCase is not null)
        {
            return generic is null && callee is MemberAccessKoto or SyntaxFormKoto { Akind: KotoKind.InferredCase }
                ? this.BindEnumConstruction(call, callee, group, call, scope, expected)
                : this.Fail(call, BindingFailure.NotCallable);
        }

        if (callee is InvocationKoto || group?.Kind != BindingSymbolKind.Function)
        {
            var callableType = callee is InvocationKoto || group is null ? callee.BoundType : this.BindReference(callee, group, scope);
            var several = false;
            if (callableType is not null && this.TryCallable(callableType, scope, out var signature, out var receiverKind, out several))
            {
                return this.BindValueCall(call, scope, signature, receiverKind);
            }

            if (several)
            {
                return this.SelectValueCall(call, scope, callableType!, expected);
            }

            if (callableType is not null && this.FailedCallableClause(callableType, scope) is { } failedClause)
            {
                return this.CompleteDependent(call, failedClause);
            }
        }

        var unknownArgument = !this.PrepareCallArguments(call, scope, group);

        if (group is null)
        {
            if (this.ReportUnavailableQualifier(callee, scope) is { } qualifier)
            {
                this.CompleteDependent(callee, qualifier);
            }
            else
            {
                this.Fail(callee, callee.BindingFailure == BindingFailure.Ambiguous ? BindingFailure.Ambiguous : this.MissingFailure(callee, scope, BindingFailure.MissingName), true);
            }

            return Complete(call, null);
        }

        if (group.Kind != BindingSymbolKind.Function)
        {
            return this.BindReference(callee, group, scope) is null ? Complete(call, null) : this.Fail(call, BindingFailure.NotCallable);
        }

        if (unknownArgument)
        {
            return Complete(call, null);
        }

        callee.BoundSymbol = group;
        callee.BindingState = BindingState.Resolved;
        var requirementGroup = callee is MemberAccessKoto requirementMember && this.requirementGroups.TryGetValue(requirementMember, out var foundGroup) && foundGroup.Active ? foundGroup : null;
        var imports = this.importCandidates?.GetValueOrDefault(callee);
        if (imports is { Count: > 1 } && this.CheckGatheredParameterShapes(callee, imports, aliasStage: true))
        {
            // SPEC 7.4, 9.5.1: a function group gathered at the alias stage from several declaration sources is checked at the use.
            return Complete(call, null);
        }

        var candidates = new CallCandidates(group, requirementGroup, imports);
        var self = requirementGroup?.Self ?? (callee as RequirementCalleeKoto)?.Self;
        var shape = MeasureCandidates(candidates, self is not null || group.Scope.Owner is StructKoto or EnumKoto);
        var candidateCount = shape.Count;
        var maxParameters = shape.Parameters;
        var maxGenerics = shape.Generics;
        var solveOrigins = shape.SolveOrigins;
        var argumentCount = call.ArgumentNodes.Count;
        var originSlots = solveOrigins ? shape.Origins : 0;
        var inputSlots = solveOrigins ? shape.InputOrigins : 0;
        var savedCandidates = candidateCount > 1 ? candidateCount : 0;
        var scratch = this.typeScratch.Rent(Math.Max(1, maxGenerics));
        var lengthArguments = this.lengthScratch.Rent(maxGenerics);
        var explicitLengths = this.lengthScratch.Rent(generic?.TypeArguments.Count ?? 0);
        var mapping = this.indexScratch.Rent(Math.Max(1, argumentCount));
        var used = this.flagScratch.Rent(Math.Max(1, maxParameters));
        var completedWaiting = this.flagScratch.Rent(argumentCount);
        var waitingContexts = this.typeScratch.Rent(argumentCount);
        completedWaiting.AsSpan(0, argumentCount).Clear();
        var origins = this.originScratch.Rent(originSlots);
        var inputs = this.originScratch.Rent(inputSlots);
        var evaluated = this.candidateScratch.Rent(candidateCount);
        var allTypes = this.typeScratch.Rent(savedCandidates * maxGenerics);
        var allLengths = this.lengthScratch.Rent(savedCandidates * maxGenerics);
        var allMaps = this.indexScratch.Rent(savedCandidates * argumentCount);
        var allOrigins = this.originScratch.Rent(savedCandidates * originSlots);
        var allInputs = this.originScratch.Rent(savedCandidates * inputSlots);
        var operationStride = argumentCount + 1;
        var operations = this.argumentOperationScratch.Rent(candidateCount * operationStride);
        BoundDefaultArgument[]? defaults = null;
        try
        {
            if (generic is not null)
            {
                for (var i = 0; i < generic.TypeArguments.Count; i++)
                {
                    var syntax = generic.TypeArguments[i];
                    var isLength = this.IsLengthArgument(syntax, scope);
                    var argumentName = UnwrapTypeSyntax(syntax);
                    while (argumentName is ParenthesizedTypeKoto parentheses)
                    {
                        argumentName = UnwrapTypeSyntax(parentheses.Type);
                    }

                    if (isLength && this.TypeName(argumentName, scope, false) is not null)
                    {
                        var typeSlot = false;
                        var lengthSlot = false;
                        foreach (var candidate in candidates)
                        {
                            if (candidate.Declaration is FunctionKoto declaration && declaration.GenericArguments.Count == generic.TypeArguments.Count &&
                                (callee is SyntheticKoto || this.Accessible(candidate, scope, receiverType: this.CallReceiver(callee)?.BoundType)))
                            {
                                lengthSlot |= declaration.GenericArguments[i] is LengthParameterKoto;
                                typeSlot |= declaration.GenericArguments[i] is GenericParameterKoto;
                            }
                        }

                        if (typeSlot && lengthSlot)
                        {
                            // Both namespaces can supply this spelling. Do not silently choose
                            // a kind before candidate-local dual-namespace binding is available.
                            return this.Fail(call, BindingFailure.Unsupported, true);
                        }

                        isLength = !typeSlot;
                    }

                    explicitLengths[i] = isLength ? this.BindLength(syntax, scope) : null;
                    if (explicitLengths[i] is null && syntax.BindingFailure == BindingFailure.None)
                    {
                        this.BindType(syntax, scope);
                    }
                }
            }

            var evaluation = new CallEvaluation
            {
                Call = call,
                Callee = callee,
                Generic = generic,
                Scope = scope,
                Expected = expected,
                Self = self,
                Requirements = requirementGroup,
                Scratch = scratch,
                LengthArguments = lengthArguments,
                ExplicitLengths = explicitLengths,
                Mapping = mapping,
                Used = used,
                Origins = origins,
                Inputs = inputs,
                Evaluated = evaluated,
                Operations = operations,
                OperationStride = operationStride,
                SavedCandidates = savedCandidates,
                AllTypes = allTypes,
                AllLengths = allLengths,
                AllMaps = allMaps,
                AllOrigins = allOrigins,
                AllInputs = allInputs,
                MaxGenerics = maxGenerics,
                OriginSlots = originSlots,
                InputSlots = inputSlots,
                Winner = -1,
                PremisesOnly = true,
            };
            this.acquisitionFailure = null;
            this.failedCause = null;
            this.EvaluateCallCandidates(ref evaluation, candidates);
            var count = evaluation.Count;
            var applicable = evaluation.Applicable;
            if (evaluation.Error)
            {
                // A candidate in a failed declaration cannot be judged, nor one whose Constraint fails on a conformance resting on a failed
                // associated specification or limited by OCC-X, so the selection rests on that failure (SPEC 23.1.2).
                return (evaluation.InvalidDeclaration ?? this.TakeFailedCause().Use) is { } cause ? this.CompleteDependent(call, cause)
                    : this.Fail(call, BindingFailure.InvalidConstraint);
            }

            if (!this.CheckCallShapeContracts(call, generic, scope, expected, evaluated.AsSpan(0, count), allMaps, maxGenerics))
            {
                return Complete(call, null);
            }

            var (selection, winnerIndex) = this.ClassifySelection(ref evaluation);
            if (selection == CallSelection.FailedSignature)
            {
                // A candidate whose own signature failed, such as a nested borrow without its Origin (`ref/uniq/i32`), stays
                // pending at every call; the selection rests on that failure (SPEC 23.1.2).
                return this.CompleteDependent(call, evaluation.FailedPendingSignature!);
            }

            if (selection == CallSelection.Unproven)
            {
                // SPEC 8.3.3: an Unknown premise that can affect the selection defers it. Omitted header Types rest on the call.
                return evaluation.PendingCount == 1 && evaluation.CallableFailure is { } callable ? this.FailCallableSelection(call, callable)
                    : evaluation.PendingCount == 1 && evaluation.ConstraintFailure is { } constraint ? this.FailPendingConstraint(call, constraint)
                    : this.FailWaitingSelection(call, BindingFailure.UnprovenConstraint);
            }

            if (selection == CallSelection.IncompleteSignature)
            {
                // A candidate whose signature failed cannot be judged, so the selection rests on that failure.
                return this.CompleteDependent(call, evaluation.IncompleteSignature!);
            }

            if (selection == CallSelection.NoneApplicable)
            {
                // SPEC 15.1.5: name the missing spelling when a bare Place was the only obstacle.
                var failure = this.acquisitionFailure?.Kind ?? BindingFailure.NoApplicableCandidate;
                if (failure == BindingFailure.NoApplicableCandidate && call.BindingFailure == BindingFailure.None)
                {
                    // The candidates that were considered explain the failed selection; recorded only when it fails.
                    var rejected = new RejectedCandidate[count];
                    for (var i = 0; i < count; i++)
                    {
                        rejected[i] = new((FunctionKoto)evaluated[i].Symbol!.Declaration, null, null, ConstraintFailure: evaluated[i].ConstraintFailure);
                        var rejectedReceiver = operations[(i * operationStride) + operationStride - 1];
                        if (rejectedReceiver.SourceType is { } receiverActual && rejectedReceiver.ParameterType is { } receiverExpected && SharedObjectAuthorityMismatch(receiverActual, receiverExpected))
                        {
                            rejected[i] = rejected[i] with { Actual = receiverActual, Expected = receiverExpected, SharedReceiver = true };
                        }

                        if (evaluated[i].Symbol!.LibraryDeclaration == KimiDeclarationId.Clone && call.ArgumentNodes is [var cloneInput] &&
                            cloneInput.BoundType is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref, Components: [{ Semantics: SemanticsKind.Obj }] })
                        {
                            rejected[i] = rejected[i] with { ObjectClone = true };
                        }

                        if (evaluated[i].ClosureReceiver is { } receivers)
                        {
                            rejected[i] = rejected[i] with { ActualReceiver = receivers.Actual, RequiredReceiver = receivers.Required, ReceiverParameter = receivers.Parameter };
                        }

                        for (var a = 0; a < argumentCount; a++)
                        {
                            var operation = operations[(i * operationStride) + a];
                            if (operation.SourceType is { } actual && operation.ParameterType is { } parameter && DifferentRangeShapes(actual, parameter))
                            {
                                rejected[i] = rejected[i] with { Actual = actual, Expected = parameter };
                                break;
                            }

                            // Signature-refusal facts have no selected parameter operation.
                            if (operation.ParameterIndex == -1 && operation.SourceType is { Kind: BoundTypeKind.Function } signature && operation.ParameterType is { Kind: BoundTypeKind.Function } required)
                            {
                                rejected[i] = rejected[i] with { Actual = signature, Expected = required, CallableSignature = true };
                                break;
                            }
                        }
                    }

                    (this.rejectedCandidates ??= new(ReferenceEqualityComparer.Instance))[call] = rejected;
                }
                else if (this.acquisitionFailure is { } acquisition)
                {
                    return this.FailAcquisition(call, failure, acquisition.Place, acquisition.Object, true);
                }

                return this.FailWaitingSelection(call, failure);
            }

            if (selection == CallSelection.Ambiguous)
            {
                if (this.DeferNestedCall(call, expected))
                {
                    return Complete(call, null);
                }

                var remaining = new RejectedCandidate[applicable];
                var next = 0;
                var erasure = ErasureIncomparable(evaluated.AsSpan(0, count), operations, operationStride, argumentCount);
                for (var i = 0; i < count; i++)
                {
                    if (evaluated[i].State is CandidateApplicability.Applicable or CandidateApplicability.Waiting)
                    {
                        remaining[next++] = new((FunctionKoto)evaluated[i].Symbol!.Declaration, null, null, ErasureIncomparable: erasure);
                    }
                }

                (this.rejectedCandidates ??= new(ReferenceEqualityComparer.Instance))[call] = remaining;
                return this.FailWaitingSelection(call, BindingFailure.Ambiguous);
            }

            var winner = evaluated[winnerIndex].Symbol!;
            var selected = (FunctionKoto)winner.Declaration;
            this.activeRequirementContract = requirementGroup?.Contracts[winnerIndex] ?? (callee as RequirementCalleeKoto)?.Contract; // Reset by the finally block.
            var selectedType = evaluated[winnerIndex].DeclaringType;
            if (savedCandidates != 0)
            {
                allTypes.AsSpan(winnerIndex * maxGenerics, CallSlotCount(selected)).CopyTo(scratch);
                allLengths.AsSpan(winnerIndex * maxGenerics, CallSlotCount(selected)).CopyTo(lengthArguments);
                allMaps.AsSpan(winnerIndex * argumentCount, argumentCount).CopyTo(mapping);
                allOrigins.AsSpan(winnerIndex * originSlots, originSlots).CopyTo(origins);
                allInputs.AsSpan(winnerIndex * inputSlots, inputSlots).CopyTo(inputs);
            }

            if (!this.SolveSelected(ref evaluation, winnerIndex, selectedType))
            {
                return null; // SPEC 10.5.1, 10.2.1: the nested call waits, or the call rests on its record (SolveSelected).
            }

            if (evaluated[winnerIndex].State == CandidateApplicability.Waiting)
            {
                // SPEC 10.5: selection precedes bodies. Complete only this candidate; a body or capture failure
                // never retries another overload, and its inferred result never supplies outer signature evidence.
                var waitingOperations = operations.AsSpan(winnerIndex * operationStride, operationStride);
                if (!this.CompleteWaiting(ref evaluation, winnerIndex, completedWaiting, waitingContexts))
                {
                    return Complete(call, null);
                }

                var state = this.TryCandidate(
                    this.Solution(evaluation, selected, selectedType),
                    explicitLengths,
                    mapping,
                    evaluated[winnerIndex].ArgumentMap,
                    expected,
                    waitingOperations,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _);
                if (state != CandidateApplicability.Applicable)
                {
                    var rejected = new RejectedCandidate(selected, null, null, Selected: true);
                    for (var i = 0; i < argumentCount; i++)
                    {
                        if (KotoHelper.UnwrapParentheses(call.ArgumentNodes[i]) is FunctionKoto { ClosureDefinition: { } closure } &&
                            this.TryCallable(selected.Parameters[mapping[i]].Type.BoundType!, this.ConstraintScope(selected), out _, out var required) &&
                            !CallableReceiverFits(closure.Receiver, CallableReceiverMask(required)))
                        {
                            rejected = rejected with { ActualReceiver = closure.Receiver, RequiredReceiver = required };
                            break;
                        }
                    }

                    (this.rejectedCandidates ??= new(ReferenceEqualityComparer.Instance))[call] = [rejected];
                    return this.Fail(call, state == CandidateApplicability.Pending ? BindingFailure.UnprovenConstraint : BindingFailure.NoApplicableCandidate, true);
                }
            }

            for (var i = 0; i < CallOwnSlots(selected).Count; i++)
            {
                if (CallOwnSlots(selected)[i] is LengthParameterKoto ? lengthArguments[i] is null : scratch[i] is null)
                {
                    return this.Fail(call, BindingFailure.MissingType, true);
                }
            }

            if (selected.IsConstructor && selectedType is not null)
            {
                selectedType = this.ConstructionType(selected, selectedType, scratch);
            }

            if (selected.IsConstructor && selectedType is not null && callee is MemberAccessKoto constructorMember && this.inferredConstructorTargets.Contains(constructorMember.Left) &&
                !this.ValidateFixedConstruction(call, scope, expected, selectedType, evaluated.AsSpan(0, count), winnerIndex, allMaps, mapping, scratch, lengthArguments, origins, inputs, allOrigins, allInputs, originSlots, inputSlots, operations, operationStride, completedWaiting, waitingContexts))
            {
                return Complete(call, null);
            }

            if (selected.IsConstructor && selectedType is not null && callee is MemberAccessKoto constructedMember && this.inferredConstructorTargets.Contains(constructedMember.Left))
            {
                Complete(constructedMember.Left, selectedType);
            }

            var result = winner.Type is { } returnType ? this.CallType(returnType, selected, scratch, scope, self, origins, inputs, selectedType, lengthArguments) : null;
            if (result is null)
            {
                return this.Fail(call, BindingFailure.MissingType, true);
            }

            var selectedOperations = operations.AsSpan(winnerIndex * operationStride, operationStride);
            var receiverOperation = selectedOperations[argumentCount];
            if (receiverOperation.Source is not null)
            {
                if (receiverOperation.BasePath is { } objectBase && receiverOperation.ParameterType is { IsObjectBorrow: true } &&
                    !this.RequireObjectErasure(call, receiverOperation.Source, ReferenceTypes.ViewTarget(receiverOperation.SourceType)!, objectBase.Type))
                {
                    return null;
                }

                receiverOperation = receiverOperation with { ObjectCompatibility = receiverOperation.BasePath is null ? ConstraintProof.Proven : ProjectedReceiverProof(winner) };
                this.receiverOperations[call] = receiverOperation;
                if (receiverOperation.ObjectCompatibility != ConstraintProof.Proven)
                {
                    // Pending effect verification is an implementation boundary, not a
                    // completed public NotProven guarantee (SPEC 12.7).
                    var pendingEffects = receiverOperation.ObjectCompatibility == ConstraintProof.Unknown;
                    return this.Fail(call, pendingEffects ? BindingFailure.Unsupported : BindingFailure.UnprovenConstraint, pendingEffects);
                }
            }

            for (var i = 0; i < argumentCount; i++)
            {
                if (selectedOperations[i].MissingOrigin)
                {
                    // SPEC 15.3.6, 10.8: judged after selection; it never made the candidate inapplicable.
                    return this.FailPerCallOrigin(call, selected, scratch, call.ArgumentNodes[i]);
                }
            }

            if (!this.CompleteArguments(call, scope, selectedOperations))
            {
                return Complete(call, null);
            }

            // SPEC 15.6.1: the selected candidate's Origin relations, never part of its applicability, are judged at their sources, a
            // default's at the call (SPEC 15.6.4 step 3); the written inputs are judged also when a default has no Type (SPEC 23.1.2).
            var defaultCount = evaluated[winnerIndex].DefaultsUsed;
            var receiverSlot = receiverOperation.Source is null ? -1 : receiverOperation.ParameterIndex;
            var solution = this.Solution(evaluation, selected, selectedType);
            var formed = this.SelectedDefaults(solution, mapping.AsSpan(0, argumentCount), receiverSlot, defaultCount, out defaults);
            this.JudgeSelectedCall(call, selected, selectedOperations, origins, inputs, selectedType, defaults.AsSpan(0, formed ? defaultCount : 0));
            if (!formed)
            {
                return this.Fail(call, BindingFailure.MissingType, true);
            }

            this.RequireResultPremises(selected, result, call); // SPEC 15.3.7: the callee's result premises.
            callee.BoundSymbol = winner;
            callee.BindingState = BindingState.Resolved;
            if (callee is MemberAccessKoto selectedMember)
            {
                selectedMember.Right.BoundSymbol = winner;
            }

            if (generic is not null)
            {
                generic.BoundSymbol = winner;
                generic.BindingState = BindingState.Resolved;
            }

            for (var wrapper = call.Method; wrapper is ParenthesizedKoto parentheses; wrapper = parentheses.Operand)
            {
                wrapper.BoundSymbol = winner;
                wrapper.BindingState = BindingState.Resolved;
            }

            call.BoundSymbol = winner;
            if (selected.IsConstructor)
            {
                result = selectedType!;
            }

            if (this.ExecutedTarget(winner, self) is not { } executed)
            {
                return this.Fail(call, BindingFailure.Internal); // SPEC 22.1: the catalog links every linked constructor.
            }

            var basePath = callee is MemberAccessKoto memberCallee && this.memberSelections.TryGetValue(memberCallee, out var memberSelection) ? memberSelection.Path : null;
            (call.CallStorage ??= new()).Set(executed, result, this.CallReceiver(callee), mapping.AsSpan(0, argumentCount), scratch.AsSpan(0, selected.GenericArguments.Count), self, selectedType, origins.AsSpan(0, solveOrigins ? selected.Origins.Count : 0), inputs.AsSpan(0, solveOrigins ? InputOriginCount(selected) : 0), selectedOperations[..argumentCount], receiverOperation, basePath, defaults.AsSpan(0, defaultCount), lengthArguments.AsSpan(0, selected.GenericArguments.Count));
            if (selected.IsRequirement)
            {
                // Retain the bound Contract selected by member lookup or by an operator.
                call.CallStorage.RequirementContract = requirementGroup is not null && this.activeRequirementContract is { } requirementContract ? requirementContract :
                    callee is RequirementCalleeKoto requirementCallee ? requirementCallee.Contract : null;
            }

            return Complete(call, result);
        }
        finally
        {
            this.activeRequirementContract = null;
            if (defaults is not null)
            {
                this.defaultArgumentScratch.Return(defaults, clearArray: true);
            }

            this.argumentOperationScratch.Return(operations, clearArray: true);
            this.originScratch.Return(allInputs, clearArray: true);
            this.originScratch.Return(allOrigins, clearArray: true);
            this.indexScratch.Return(allMaps);
            this.typeScratch.Return(allTypes, clearArray: true);
            this.lengthScratch.Return(allLengths, clearArray: true);
            this.lengthScratch.Return(explicitLengths, clearArray: true);
            this.lengthScratch.Return(lengthArguments, clearArray: true);
            this.candidateScratch.Return(evaluated, clearArray: true);
            this.originScratch.Return(inputs, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
            this.typeScratch.Return(waitingContexts, clearArray: true);
            this.flagScratch.Return(completedWaiting);
            this.flagScratch.Return(used);
            this.indexScratch.Return(mapping);
            this.typeScratch.Return(scratch, clearArray: true);
        }
    }

    // SPEC 7.2.3, 10.1 step 2: the `count` defaults the selected candidate uses (DefaultSlots), each with its instantiated parameter Type;
    // `defaults` is rented for the caller to return. False when one has no Type.
    private bool SelectedDefaults(in SlotSolution solution, ReadOnlySpan<int> mapping, int receiverSlot, int count, out BoundDefaultArgument[]? defaults)
    {
        defaults = null;
        if (count == 0)
        {
            return true;
        }

        var function = solution.Function;
        var slots = this.RentSlotSet(function.Parameters.Count);
        try
        {
            DefaultSlots(function, mapping, receiverSlot, ref slots);
            defaults = this.defaultArgumentScratch.Rent(count);
            var next = 0;
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                if (slots.Contains(i))
                {
                    var parameter = function.Parameters[i];
                    if (parameter.DefaultValue is not { } expression || parameter.Type.BoundType is not { } pattern ||
                        solution.Type(pattern) is not { } parameterType)
                    {
                        return false;
                    }

                    defaults[next++] = new(expression, this.ParameterSymbol(function, i), parameterType);
                }
            }

            return true;
        }
        finally
        {
            this.ReturnSlotSet(slots);
        }
    }

    private Koto? CallReceiver(Koto callee)
        => callee is SyntheticKoto synthetic ? synthetic.Receiver : callee is MemberAccessKoto member && (this.requirementGroups.TryGetValue(member, out var group) && group.Active ? !group.TypeAccess : member.Left.BoundSymbol?.Kind is not (BindingSymbolKind.Type or BindingSymbolKind.Container)) ? member.Left : null;
}
