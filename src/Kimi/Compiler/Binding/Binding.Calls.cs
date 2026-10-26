// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly ScratchBuffers<BoundDefaultArgument> defaultArgumentScratch = new();

    private static Koto? IncompleteSignature(FunctionKoto function)
    {
        if (function.ReturnType is { BindingState: not BindingState.Resolved } result)
        {
            return result;
        }

        for (var i = 0; i < function.Parameters.Count; i++)
        {
            if (function.Parameters[i].Type is { BindingState: not BindingState.Resolved } type)
            {
                return type;
            }
        }

        return null;
    }

    // The first failed node inside a parameter Type whose outer layer resolved.
    private Koto? FailedSignaturePart(FunctionKoto function)
    {
        var visitor = this.failedSignaturePartVisitor ??= new();
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            if (visitor.Find(function.Parameters[i].Type, invalidState: true) is { } part)
            {
                return part;
            }
        }

        return null;
    }

    private BindingSymbol? Member(MemberAccessKoto member, BindingScope scope, BoundType? expected = null)
    {
        if (member.Left is BaseReferenceKoto baseReference)
        {
            return this.BaseMember(member, baseReference, scope);
        }

        if (member.Right.Akind == KotoKind.ConstructorReference)
        {
            return this.ConstructorMember(member, scope);
        }

        if (member.Right is not IdentifierNameKoto right)
        {
            return null;
        }

        BindingSymbol? typeMember = null;
        BindingSymbol? valueMember = null;
        MemberSelection typeSelection = default;
        MemberSelection valueSelection = default;
        var qualifier = this.TypeName(member.Left, scope, false);
        if (qualifier?.Type is { } type && qualifier.Declaration is not ContractKoto)
        {
            typeMember = this.RequirementMember(member, scope, type, true);
        }

        if (qualifier is not null && qualifier.Declaration is not ContractKoto && this.scopes.TryGetValue(qualifier.Declaration, out var typeScope))
        {
            if (qualifier.Type is not null)
            {
                var originQualifier = qualifier.Declaration is not EnumKoto && qualifier.Schema is { Origins.Count: > 0 };
                var callQualifier = originQualifier && IsCallee(member);
                if ((qualifier.Declaration is EnumKoto ? this.EnumQualifierType(member.Left, qualifier, scope, expected)
                    : this.BindType(member.Left, scope, this.TypeContext(member.Left, scope) with { CallQualifier = callQualifier })) is not { } qualifiedType)
                {
                    return null;
                }

                typeSelection = this.LookupTypeMember(qualifiedType, right.IdentifierName, scope);
                typeMember = typeSelection.Member ?? typeMember;
                if (originQualifier && this.RejectedOriginQualifier(member, qualifiedType, callQualifier && !typeSelection.Ambiguous ? typeSelection.Member : null))
                {
                    return null;
                }
            }
            else if (qualifier.Declaration is GroupKoto { IsRoot: false } &&
                this.BindContainerQualifier(member.Left, qualifier, scope, this.TypeContext(member.Left, scope)) is { } groupType)
            {
                if (groupType.OriginArguments.Count != (qualifier.Schema?.Origins.Count ?? 0) || groupType.OriginArguments.Contains(null!))
                {
                    this.Fail(member.Left, BindingFailure.InvalidOrigin);
                    return null;
                }

                typeSelection = this.LookupTypeMember(groupType, right.IdentifierName, scope);
                typeMember = typeSelection.Member;
            }
            else if (typeScope.Values.TryGetValue(right.IdentifierName, out var candidate))
            {
                typeMember = candidate;
            }
        }

        var valuePossible = this.MayBeValueQualifier(member.Left is GenericsKoto constructed ? constructed.Identifier! : member.Left, scope);
        if (valuePossible)
        {
            var receiverType = this.BindNode(member.Left, scope);
            this.ReceiverThroughLayers(member.Left, receiverType);
            this.ReceiverElement(member.Left, receiverType);
            // SPEC 3.4.3: selection continues at the referent of each safe value-reference layer and, under the
            // object view rules, at the payload of an object handle or view.
            while (true)
            {
                while (receiverType is { Kind: BoundTypeKind.Semantics, Components.Count: 1 } && (IsBorrow(receiverType.Semantics) || IsObjectSemantics(receiverType.Semantics)))
                {
                    receiverType = receiverType.Components[0];
                }

                if (receiverType is not null)
                {
                    // A nominal member takes priority over a requirement member of the same receiver.
                    valueMember = this.RequirementMember(member, scope, receiverType, false);
                    valueSelection = this.LookupTypeMember(receiverType, right.IdentifierName, scope, receiverType);
                    valueMember = valueSelection.Member ?? valueMember;
                }

                // SPEC 3.4.3: a qualifying pair layer without the member, declared or published, continues at its target.
                if (valueMember is not null || receiverType is null || this.FollowablePair(receiverType, scope, out var pairTarget) == SemanticsMask.None)
                {
                    break;
                }

                receiverType = pairTarget;
            }
        }

        if (typeSelection.Ambiguous || valueSelection.Ambiguous || (typeMember is not null && valueMember is not null))
        {
            this.Fail(member, BindingFailure.Ambiguous, true);
            return null;
        }

        var selected = typeMember ?? valueMember;
        if (selected is null && (typeSelection.Hidden ?? valueSelection.Hidden) is { } hidden)
        {
            member.BoundSymbol = hidden;
            this.Fail(member, BindingFailure.Access);
            return null;
        }

        if (selected is not null)
        {
            if (typeMember is not null && selected.EnumCase is null && qualifier?.Declaration is EnumKoto)
            {
                // Only Case construction may infer the enum qualifier's missing arguments.
                var qualifiedType = this.BindTypeStructure(member.Left, scope, this.TypeContext(member.Left, scope));
                if (qualifiedType is null || this.CompleteOrigins(qualifiedType, member.Left as TypeSemanticsKoto, member.Left, scope, this.TypeContext(member.Left, scope)) is not { } completeType)
                {
                    return null;
                }

                typeSelection = typeSelection with { DeclaringType = completeType };
                Complete(member.Left, completeType);
            }

            if (selected.Kind != BindingSymbolKind.Function && !this.Accessible(selected, scope, receiverType: typeMember is null ? member.Left.BoundType : null))
            {
                this.Fail(member, BindingFailure.Access);
                return null;
            }

            this.memberSelections[member] = typeMember is not null ? typeSelection : valueSelection;
            if (typeMember is not null)
            {
                member.Left.BoundSymbol = qualifier;
                member.Left.BindingState = BindingState.Resolved;
            }

            member.BoundSymbol = selected;
            right.BoundSymbol = selected;
            right.BindingState = BindingState.Resolved;
        }

        return selected;
    }

    private bool MayBeValueQualifier(Koto node, BindingScope scope)
    {
        if (node is TypeKoto)
        {
            return false;
        }

        if (node is IdentifierNameKoto name)
        {
            return this.Lookup(name.IdentifierName, scope, node, false) is not null;
        }

        if (node is SyntaxFormKoto { Akind: KotoKind.RootName })
        {
            return false;
        }

        if (node is GenericsKoto generic)
        {
            return this.MayBeValueQualifier(generic.Identifier!, scope);
        }

        if (node is MemberAccessKoto member)
        {
            return this.MayBeValueQualifier(member.Left, scope) ||
                (this.TypeName(member.Left, scope, false) is { } type && this.scopes.TryGetValue(type.Declaration, out var members) && member.Right is IdentifierNameKoto right && members.Values.ContainsKey(right.IdentifierName));
        }

        return true;
    }

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
                return (evaluation.InvalidDeclaration ?? this.TakeFailedCause()) is { } cause ? this.CompleteDependent(call, cause) : this.Fail(call, BindingFailure.InvalidConstraint);
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
                        if (KotoHelper.UnwrapParentheses(call.ArgumentNodes[i]) is FunctionKoto { BoundClosure: { } closure } &&
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

            // SPEC 10.8.1: a constructor whose target Type the call infers from its arguments.
            if (selected.IsConstructor && selectedType is not null && this.InferredConstructorTarget(callee) is { } target)
            {
                if (!this.ValidateFixedConstruction(call, scope, expected, selectedType, evaluated.AsSpan(0, count), winnerIndex, allMaps, mapping, scratch, lengthArguments, origins, inputs, allOrigins, allInputs, originSlots, inputSlots, operations, operationStride, completedWaiting, waitingContexts))
                {
                    return Complete(call, null);
                }

                Complete(target, selectedType);
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

            // SPEC 15.6.1: the selected candidate's Origin relations, never part of its applicability, are judged at their sources.
            this.JudgeSelectedCall(call, selected, selectedOperations, argumentCount, origins, inputs, selectedType);
            var defaultCount = evaluated[winnerIndex].DefaultsUsed;
            if (defaultCount != 0)
            {
                // Candidate evaluation reuses used[]; reconstruct only the winner's
                // slots from its retained mapping, without repeating selection.
                used.AsSpan(0, selected.Parameters.Count).Clear();
                for (var i = 0; i < argumentCount; i++)
                {
                    used[mapping[i]] = true;
                }

                if (receiverOperation.Source is not null)
                {
                    used[receiverOperation.ParameterIndex] = true;
                }

                defaults = this.defaultArgumentScratch.Rent(defaultCount);
                var defaultIndex = 0;
                for (var i = 0; i < selected.Parameters.Count; i++)
                {
                    if (!used[i])
                    {
                        var parameter = selected.Parameters[i];
                        if (parameter.DefaultValue is not { } expression || parameter.Type.BoundType is not { } pattern ||
                            this.CallType(pattern, selected, scratch, scope, self, origins, inputs, selectedType, lengthArguments) is not { } parameterType)
                        {
                            return this.Fail(call, BindingFailure.MissingType, true);
                        }

                        defaults[defaultIndex++] = new(expression, this.ParameterSymbol(selected, i), parameterType);
                    }
                }
            }

            // SPEC 15.3.7, 15.6.1: the callee's instantiated result and the Type arguments the call infers, none where it writes them.
            this.RequireResultPremises(selected, result, call, generic is null ? scratch.AsSpan(0, CallSlotCount(selected)) : default);

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

    private Koto? CallReceiver(Koto callee)
        => callee is SyntheticKoto synthetic ? synthetic.Receiver : callee is MemberAccessKoto member && (this.requirementGroups.TryGetValue(member, out var group) && group.Active ? !group.TypeAccess : member.Left.BoundSymbol?.Kind is not (BindingSymbolKind.Type or BindingSymbolKind.Container)) ? member.Left : null;

    // SPEC 8.1.2: an s/U whose s is already inferred is matched as the Type it forms: U for owner, one reference layer with
    // the occurrence's outer-Origin slot for ref or uniq. An application whose s is still open is returned as it is.
    private BoundType FormedApplication(BoundType pattern, Koto function, BoundType?[] arguments)
    {
        for (var depth = 0; depth < 16; depth++)
        {
            if (pattern is not { Kind: BoundTypeKind.SemanticsApplication, Symbol: { } selector, Components: [var target] } ||
                ContainerSlot(function, selector) is not (>= 0 and var slot) || slot >= arguments.Length || arguments[slot] is not { } whole)
            {
                break;
            }

            if (whole.Semantics == SemanticsKind.Owner)
            {
                pattern = target;
            }
            else if (whole.Semantics is SemanticsKind.Ref or SemanticsKind.Uniq)
            {
                pattern = this.types.Intern(BoundTypeKind.Semantics, null, whole.Semantics, [target], origin: pattern.Origin);
                break;
            }
            else
            {
                break;
            }
        }

        return pattern;
    }

    // With `evidence`, the actual is a known call signature (SPEC 10.5, 10.8): an argument's, or the parts an anonymous header writes.
    // Its structure and Semantics are evidence, and its Origins are not compared: applicability judges no Origin part (SPEC 15.6.1),
    // and the argument fits the substituted parameter after selection. An Origin the signature itself quantifies, such as a per-call
    // input, lies beyond the call and never solves an Origin in a slot: it becomes an open region that other evidence fills.
    // With `relateLater`, the actual is a value's Type whose Origin relations to the solution are judged after selection (SPEC 15.6.1),
    // so Origin evidence never fails a structurally equal slot binding; `invariant` marks a position below an exclusive layer or an
    // invariant schema slot.
    private bool Infer(BoundType pattern, BoundType actual, Koto function, BoundType?[] arguments, bool inferOrigins = false, BoundLength?[]? lengths = null, bool commonOrigins = false, bool structural = false, SignatureEvidence? evidence = null, bool relateLater = false, bool invariant = false)
    {
        if (pattern.Kind == BoundTypeKind.AssociatedProjection)
        {
            // An associated Type is not an injective constructor: infer its receiver from
            // other inputs, then check the substituted complete parameter in candidate fitting.
            return true;
        }

        if (pattern.Kind == BoundTypeKind.TargetProjection && pattern.Symbol is { } projected &&
            ContainerSlot(function, projected) is >= 0 and var projectionSlot)
        {
            // The target alone does not determine a pair's Semantics. Once another input, an explicit argument or the
            // result fixes the whole Type, match its direct target; final candidate fitting checks a still-open projection.
            return projectionSlot < arguments.Length && (arguments[projectionSlot] is not { } whole ||
                this.Infer(this.DirectTarget(whole), actual, function, arguments, inferOrigins, lengths, commonOrigins, structural, evidence, relateLater, invariant));
        }

        if (pattern.Kind == BoundTypeKind.SemanticsApplication && pattern.Symbol is { } selector &&
            ContainerSlot(function, selector) is var selectorSlot && selectorSlot >= 0 && selectorSlot < arguments.Length)
        {
            if (arguments[selectorSlot] is not { } whole)
            {
                return true; // SPEC 10.2: solved together; the substituted parameter is checked once another argument fixes s/T.
            }

            if (whole.Semantics == SemanticsKind.Owner)
            {
                return this.Infer(pattern.Components[0], actual, function, arguments, inferOrigins, lengths, commonOrigins, structural, evidence, relateLater, invariant);
            }

            return actual.Kind == BoundTypeKind.Semantics && actual.Semantics == whole.Semantics &&
                this.Infer(pattern.Components[0], actual.Components[0], function, arguments, inferOrigins, lengths, commonOrigins, structural, evidence, relateLater, invariant || whole.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq or SemanticsKind.Raw);
        }

        // SPEC 3.1.1: the wrapping Scalar Wrapping<u8> is the instance of the pattern Wrapping<T>, so T is inferred from
        // the Scalar's integer argument; the substituted parameter then normalizes to the same Scalar.
        if (actual.IsWrappingInteger && pattern is { Kind: BoundTypeKind.Constructed, Components.Count: 1 } && pattern.Symbol?.LibraryDeclaration == KimiDeclarationId.Wrapping)
        {
            return this.Infer(pattern.Components[0], actual.Underlying, function, arguments, inferOrigins, lengths, commonOrigins, structural, evidence, relateLater, invariant);
        }

        if (pattern.Kind == BoundTypeKind.Parameter && ContainerSlot(function, pattern.Symbol!) is var slot && slot >= 0)
        {
            if ((uint)slot >= (uint)arguments.Length)
            {
                return false;
            }

            if (HasArrayLengthHole(actual))
            {
                // An incomplete annotation cannot become a generic argument. Established argument evidence is checked
                // against it at the ordinary result fit; without such evidence the slot remains unsolved.
                return arguments[slot] is not { } establishedArray || IsStructuralSubtype(establishedArray, actual);
            }

            // SPEC 10.2.2: a parameter constrained to Position, PositionRange or PrimitiveInteger binds the referent; the
            // argument is then value-read.
            if (!structural && actual is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } &&
                ComparisonReferent(actual) is var referent && !ReferenceEquals(referent, actual) && this.InfersReadReferent(pattern, this.activeInferenceFunction is { } inference && ReferenceEquals(CallSlotOwner(inference), function) ? inference : function))
            {
                actual = referent;
            }

            if (evidence is { Binder: FunctionKoto { BoundClosure: not null } closure } && HasEnvironmentOrigin(actual, closure))
            {
                // SPEC 8.10, 7.7.4: a result over the closure's hidden environment receiver satisfies no Callable signature and no
                // erasure, so that part is no evidence for the slot; the Callable proof or the conversion judges it against the rest.
                this.environmentEvidence = true;
                return true;
            }

            if (evidence is { Binder: { } binder } known)
            {
                actual = this.OpenKnownOrigins(actual, binder, known.Source);
            }

            if (arguments[slot] is { } previous)
            {
                if (!ReferenceEquals(previous, actual) && (HasOpenOrigin(previous) || HasOpenOrigin(actual)))
                {
                    // SPEC 10.8: no argument fixes a slot first by traversal order. Another argument's Origin at an open position
                    // solves it, whichever comes first; two open positions keep the earlier region.
                    previous = this.FillOpenOrigins(previous, actual, false);
                    actual = this.FillOpenOrigins(actual, previous, true);
                }

                if (relateLater && !structural && !ReferenceEquals(previous, actual) &&
                    IsStructuralSubtype(actual, previous) && IsStructuralSubtype(previous, actual))
                {
                    // SPEC 10.8, 15.3.6: Origin evidence for a slot never makes the candidate inapplicable; each argument's relation to
                    // the solution is judged after selection (SPEC 15.6.1). An explicit Type argument is kept, an invariant binding is
                    // kept over covariant ones and the first invariant one over later ones, whatever the argument order, and covariant
                    // bindings meet.
                    if (!commonOrigins || this.invariantSlots.Contains(slot))
                    {
                        arguments[slot] = previous;
                    }
                    else if (invariant)
                    {
                        arguments[slot] = actual;
                        this.invariantSlots.Add(slot);
                    }
                    else
                    {
                        arguments[slot] = IsSubtype(actual, previous) ? previous : this.CommonOriginType(previous, actual) ?? previous;
                    }

                    return true;
                }

                // SPEC 10.8: structural matching compares normalized Types, so two spellings of one Function Type, whose per-call
                // inputs have distinct binders, are one binding.
                if (ReferenceEquals(previous, actual) || (!structural && inferOrigins && IsSubtype(actual, previous)) ||
                    (structural && previous.Kind == BoundTypeKind.Function && actual.Kind == BoundTypeKind.Function &&
                    IsSubtype(actual, previous) && IsSubtype(previous, actual)))
                {
                    arguments[slot] = previous;
                    return true;
                }

                if (commonOrigins && this.CommonOriginType(previous, actual) is { } common)
                {
                    arguments[slot] = common;
                    return true;
                }

                return false;
            }

            arguments[slot] = actual;
            if (relateLater && invariant)
            {
                this.invariantSlots.Add(slot);
            }

            return true;
        }

        if (ReferenceEquals(pattern, actual))
        {
            return true;
        }

        if (pattern.Kind != actual.Kind || pattern.Symbol != actual.Symbol || pattern.Semantics != actual.Semantics ||
            !(lengths is not null && pattern.Kind == BoundTypeKind.FixedArray ? this.InferLength(pattern, actual, function, lengths) : pattern.Length == actual.Length && ReferenceEquals(pattern.LengthExpression, actual.LengthExpression)) ||
            (!inferOrigins && evidence is null && !ReferenceEquals(pattern.Origin, actual.Origin)) || pattern.OriginArguments.Count != actual.OriginArguments.Count || pattern.Components.Count != actual.Components.Count || pattern.LengthArguments.Length != actual.LengthArguments.Length || (pattern.Components.Count == 0 && pattern.LengthArguments.Length == 0 && pattern.OriginArguments.Count == 0))
        {
            return false;
        }

        for (var i = 0; i < pattern.LengthArguments.Length; i++)
        {
            var required = pattern.LengthArguments[i];
            var supplied = actual.LengthArguments[i];
            if (!ReferenceEquals(required, supplied) && (required is null || supplied is null || lengths is null || !this.InferLength(required, supplied, function, lengths)))
            {
                return false;
            }
        }

        for (var i = 0; i < pattern.OriginArguments.Count; i++)
        {
            if (!inferOrigins && evidence is null && !ReferenceEquals(pattern.OriginArguments[i], actual.OriginArguments[i]))
            {
                return false;
            }
        }

        for (var i = 0; i < pattern.Components.Count; i++)
        {
            var below = invariant || IsInvariantLayer(pattern, this) ||
                (pattern.Kind == BoundTypeKind.Constructed && pattern.Symbol?.Schema is { } schema && i < schema.GenericSlots.Count && schema.GenericSlots[i].OriginVariance is OriginVariance.Invariant or OriginVariance.Unused);
            if (!this.Infer(
                pattern.Components[i],
                actual.Components[i],
                function,
                arguments,
                inferOrigins,
                lengths,
                commonOrigins,
                structural,
                evidence,
                relateLater,
                below))
            {
                return false;
            }
        }

        return true;
    }
}
