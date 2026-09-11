// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // The call plan accompanies its binder.

/// <summary>The committed target and argument mapping; storage is reused when the same call is rebound.</summary>
public sealed class BoundCall
{
    private int[] mapping = [];
    private BoundType[] typeArguments = [];
    private BoundOrigin[] origins = [];
    private BoundOrigin[] inputOrigins = [];

    /// <summary>Gets the selected function Symbol.</summary>
    public BindingSymbol Target { get; private set; } = null!;

    /// <summary>Gets the complete return type after substitution.</summary>
    public BoundType ReturnType { get; private set; } = null!;

    /// <summary>Gets the explicit receiver when member-call syntax supplies it.</summary>
    public Koto? Receiver { get; private set; }

    /// <summary>Gets the symbolic conforming Type for a definition-bound requirement call.</summary>
    public BoundType? ConformingType { get; private set; }

    /// <summary>Gets the instantiated Type declaring a nominal member, including an inherited member's base Type.</summary>
    public BoundType? DeclaringType { get; private set; }

    /// <summary>Gets the committed substitutions for the function's declared Origins.</summary>
    public ReadOnlySpan<BoundOrigin> Origins => this.origins;

    /// <summary>Gets the committed input Origins in parameter-slot order.</summary>
    public ReadOnlySpan<BoundOrigin> InputOrigins => this.inputOrigins;

    /// <summary>Gets source-argument index to parameter-slot mappings.</summary>
    public ReadOnlySpan<int> ArgumentToParameter => this.mapping;

    /// <summary>Gets the selected complete type arguments.</summary>
    public ReadOnlySpan<BoundType> TypeArguments => this.typeArguments;

    internal void Set(BindingSymbol target, BoundType result, Koto? receiver, ReadOnlySpan<int> mapping, ReadOnlySpan<BoundType?> typeArguments, BoundType? conformingType = null, BoundType? declaringType = null, ReadOnlySpan<BoundOrigin> origins = default, ReadOnlySpan<BoundOrigin> inputOrigins = default)
    {
        this.Target = target;
        this.ReturnType = result;
        this.Receiver = receiver;
        this.ConformingType = conformingType;
        this.DeclaringType = declaringType;
        if (this.origins.Length != origins.Length)
        {
            this.origins = new BoundOrigin[origins.Length];
        }

        if (this.inputOrigins.Length != inputOrigins.Length)
        {
            this.inputOrigins = new BoundOrigin[inputOrigins.Length];
        }

        origins.CopyTo(this.origins);
        inputOrigins.CopyTo(this.inputOrigins);
        if (this.mapping.Length != mapping.Length)
        {
            this.mapping = new int[mapping.Length];
        }

        mapping.CopyTo(this.mapping);
        if (this.typeArguments.Length != typeArguments.Length)
        {
            this.typeArguments = new BoundType[typeArguments.Length];
        }

        for (var i = 0; i < typeArguments.Length; i++)
        {
            this.typeArguments[i] = typeArguments[i]!;
        }
    }
}

public sealed partial class Binding
{
    private BindingSymbol? Member(MemberAccessKoto member, BindingScope scope)
    {
        if (member.Right is not IdentifierNameKoto right)
        {
            return null;
        }

        BindingSymbol? typeMember = null;
        BindingSymbol? valueMember = null;
        MemberSelection typeSelection = default;
        MemberSelection valueSelection = default;
        var qualifierName = member.Left is GenericsKoto constructed ? constructed.Identifier! : member.Left;
        var qualifier = this.TypeName(qualifierName, scope, false);
        if (qualifier?.Type is { } type && qualifier.Declaration is not ContractKoto)
        {
            typeMember = this.RequirementMember(member, scope, type, true);
        }

        if (qualifier is not null && qualifier.Declaration is not ContractKoto && this.scopes.TryGetValue(qualifier.Declaration, out var typeScope))
        {
            if (qualifier.Type is not null)
            {
                if (this.BindType(member.Left, scope) is not { } qualifiedType)
                {
                    return null;
                }

                typeSelection = this.LookupTypeMember(qualifiedType, right.IdentifierName);
                typeMember = typeSelection.Member ?? typeMember;
            }
            else if (typeScope.Values.TryGetValue(right.IdentifierName, out var candidate))
            {
                typeMember = candidate;
            }
        }

        var valuePossible = qualifierName is not IdentifierNameKoto leftName || this.Lookup(leftName.IdentifierName, scope, member.Left, false) is not null;
        if (valuePossible)
        {
            var receiverType = this.BindNode(member.Left, scope);
            if (receiverType?.Kind == BoundTypeKind.Semantics && IsBorrow(receiverType.Semantics))
            {
                receiverType = receiverType.Components[0];
            }

            if (receiverType is not null)
            {
                valueMember = this.RequirementMember(member, scope, receiverType, false);
            }

            if (receiverType is not null)
            {
                valueSelection = this.LookupTypeMember(receiverType, right.IdentifierName);
                valueMember = valueSelection.Member ?? valueMember;
            }
        }

        if (typeSelection.Ambiguous || valueSelection.Ambiguous || (typeMember is not null && valueMember is not null))
        {
            Fail(member, BindingFailure.Ambiguous, true);
            return null;
        }

        var selected = typeMember ?? valueMember;
        if (selected is not null)
        {
            if (selected.Kind != BindingSymbolKind.Function && !this.Accessible(selected, scope))
            {
                Fail(member, BindingFailure.Access);
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

    private BoundType? BindCall(InvocationKoto call, BindingScope scope, BoundType? expected)
    {
        var callee = call.Method;
        var generic = callee as GenericsKoto;
        if (generic is not null)
        {
            callee = generic.Identifier!;
        }

        BindingSymbol? group;
        if (callee is IdentifierNameKoto name)
        {
            group = this.Lookup(name.IdentifierName, scope, name, false);
        }
        else if (callee is MemberAccessKoto member)
        {
            group = this.Member(member, scope);
        }
        else
        {
            this.BindNode(callee, scope);
            group = callee.BoundSymbol;
        }

        var unknownArgument = false;
        for (var i = 0; i < call.ArgumentNodes.Count; i++)
        {
            var argument = call.ArgumentNodes[i];
            if (!IsUnfittedLiteral(argument) && this.BindNode(argument, scope) is null)
            {
                unknownArgument = true;
            }
        }

        if (group is null)
        {
            Fail(callee, callee.BindingFailure == BindingFailure.Ambiguous ? BindingFailure.Ambiguous : BindingFailure.MissingName, true);
            return Complete(call, null);
        }

        if (group.Kind != BindingSymbolKind.Function)
        {
            this.BindReference(callee, group, scope);
            return Fail(call, BindingFailure.NotCallable);
        }

        if (unknownArgument)
        {
            return Complete(call, null);
        }

        callee.BoundSymbol = group;
        callee.BindingState = BindingState.Resolved;
        if (generic is not null)
        {
            for (var i = 0; i < generic.TypeArguments.Count; i++)
            {
                this.BindType(generic.TypeArguments[i], scope);
            }
        }

        var requirementGroup = callee is MemberAccessKoto requirementMember && this.requirementGroups.TryGetValue(requirementMember, out var foundGroup) && foundGroup.Active ? foundGroup : null;
        var candidates = new CallCandidates(group, requirementGroup);
        var self = requirementGroup?.Self;
        var candidateCount = 0;
        var maxParameters = 0;
        var maxGenerics = 0;
        var maxOrigins = 0;
        foreach (var candidate in candidates)
        {
            if (candidate.Declaration is FunctionKoto function)
            {
                candidateCount++;
                maxParameters = Math.Max(maxParameters, function.Parameters.Count);
                maxGenerics = Math.Max(maxGenerics, function.GenericArguments.Count);
                maxOrigins = Math.Max(maxOrigins, function.Origins.Count);
            }
        }

        var argumentCount = call.ArgumentNodes.Count;
        var solveOrigins = self is not null || group.Scope.Owner is StructKoto or EnumKoto;
        var originSlots = solveOrigins ? maxOrigins : 0;
        var inputSlots = solveOrigins ? maxParameters : 0;
        var savedCandidates = candidateCount > 1 ? candidateCount : 0;
        var scratch = this.typeScratch.Rent(Math.Max(1, maxGenerics));
        var mapping = this.indexScratch.Rent(Math.Max(1, argumentCount));
        var used = this.flagScratch.Rent(Math.Max(1, maxParameters));
        var origins = this.originScratch.Rent(originSlots);
        var inputs = this.originScratch.Rent(inputSlots);
        var evaluated = this.candidateScratch.Rent(candidateCount);
        var allTypes = this.typeScratch.Rent(savedCandidates * maxGenerics);
        var allParameters = this.typeScratch.Rent(candidateCount * argumentCount);
        var allMaps = this.indexScratch.Rent(savedCandidates * argumentCount);
        var allOrigins = this.originScratch.Rent(savedCandidates * originSlots);
        var allInputs = this.originScratch.Rent(savedCandidates * inputSlots);
        try
        {
            var count = 0;
            var applicable = 0;
            var winnerIndex = -1;
            var pending = false;
            var error = false;
            foreach (var candidate in candidates)
            {
                if (candidate.Declaration is not FunctionKoto function)
                {
                    continue;
                }

                var index = count++;
                var declaringType = self is null ? this.CallDeclaringType(callee, candidate) : null;
                var state = CandidateApplicability.Inapplicable;
                if (this.Accessible(candidate, scope))
                {
                    this.BindHeader(candidate);
                    state = this.TryCandidate(call, function, generic, scope, scratch, mapping, used, expected, self, origins, inputs, declaringType);
                }

                evaluated[index] = new(candidate, state, declaringType);
                pending |= state == CandidateApplicability.Pending;
                error |= state == CandidateApplicability.Error;
                if (state != CandidateApplicability.Applicable)
                {
                    continue;
                }

                applicable++;
                winnerIndex = index;
                if (savedCandidates != 0)
                {
                    scratch.AsSpan(0, function.GenericArguments.Count).CopyTo(allTypes.AsSpan(index * maxGenerics));
                    mapping.AsSpan(0, argumentCount).CopyTo(allMaps.AsSpan(index * argumentCount));
                    origins.AsSpan(0, originSlots).CopyTo(allOrigins.AsSpan(index * originSlots));
                    inputs.AsSpan(0, inputSlots).CopyTo(allInputs.AsSpan(index * inputSlots));
                }

                for (var i = 0; i < argumentCount; i++)
                {
                    allParameters[(index * argumentCount) + i] = this.CallType(function.Parameters[mapping[i]].Type.BoundType!, function, scratch, scope, self, origins, inputs, declaringType);
                }
            }

            if (error)
            {
                return Fail(call, BindingFailure.InvalidConstraint);
            }

            if (pending)
            {
                return Fail(call, BindingFailure.UnprovenConstraint, true);
            }

            if (applicable == 0)
            {
                return Fail(call, BindingFailure.NoApplicableCandidate, true);
            }

            if (applicable > 1)
            {
                winnerIndex = SelectBest(evaluated.AsSpan(0, count), allParameters, argumentCount);
                if (winnerIndex < 0)
                {
                    return Fail(call, BindingFailure.Ambiguous, true);
                }
            }

            var winner = evaluated[winnerIndex].Symbol;
            var selected = (FunctionKoto)winner.Declaration;
            var selectedType = evaluated[winnerIndex].DeclaringType;
            if (savedCandidates != 0)
            {
                allTypes.AsSpan(winnerIndex * maxGenerics, selected.GenericArguments.Count).CopyTo(scratch);
                allMaps.AsSpan(winnerIndex * argumentCount, argumentCount).CopyTo(mapping);
                allOrigins.AsSpan(winnerIndex * originSlots, originSlots).CopyTo(origins);
                allInputs.AsSpan(winnerIndex * inputSlots, inputSlots).CopyTo(inputs);
            }

            for (var i = 0; i < selected.GenericArguments.Count; i++)
            {
                if (scratch[i] is null)
                {
                    return Fail(call, BindingFailure.MissingType, true);
                }
            }

            var result = winner.Type is { } returnType ? this.CallType(returnType, selected, scratch, scope, self, origins, inputs, selectedType) : null;
            if (result is null)
            {
                return Fail(call, BindingFailure.MissingType, true);
            }

            for (var i = 0; i < argumentCount; i++)
            {
                this.RequireType(call.ArgumentNodes[i], scope, allParameters[(winnerIndex * argumentCount) + i]);
            }

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

            call.BoundSymbol = winner;
            (call.CallStorage ??= new()).Set(winner, result, this.CallReceiver(callee), mapping.AsSpan(0, argumentCount), scratch.AsSpan(0, selected.GenericArguments.Count), self, selectedType, origins.AsSpan(0, solveOrigins ? selected.Origins.Count : 0), inputs.AsSpan(0, solveOrigins ? selected.Parameters.Count : 0));
            return Complete(call, result);
        }
        finally
        {
            this.originScratch.Return(allInputs, clearArray: true);
            this.originScratch.Return(allOrigins, clearArray: true);
            this.indexScratch.Return(allMaps);
            this.typeScratch.Return(allParameters, clearArray: true);
            this.typeScratch.Return(allTypes, clearArray: true);
            this.candidateScratch.Return(evaluated, clearArray: true);
            this.originScratch.Return(inputs, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
            this.flagScratch.Return(used);
            this.indexScratch.Return(mapping);
            this.typeScratch.Return(scratch, clearArray: true);
        }
    }

    private Koto? CallReceiver(Koto callee)
        => callee is MemberAccessKoto member && (this.requirementGroups.TryGetValue(member, out var group) && group.Active ? !group.TypeAccess : member.Left.BoundSymbol?.Kind is not (BindingSymbolKind.Type or BindingSymbolKind.Container)) ? member.Left : null;

    private CandidateApplicability TryCandidate(InvocationKoto call, FunctionKoto function, GenericsKoto? generic, BindingScope scope, BoundType?[] arguments, int[] mapping, bool[] used, BoundType? expected, BoundType? self, BoundOrigin[] origins, BoundOrigin[] inputs, BoundType? declaringType)
    {
        Array.Clear(arguments, 0, function.GenericArguments.Count);
        Array.Clear(used, 0, function.Parameters.Count);
        if (self is not null || declaringType is not null)
        {
            Array.Clear(origins, 0, function.Origins.Count);
            Array.Clear(inputs, 0, function.Parameters.Count);
        }

        if (function.IsSpecialization)
        {
            return CandidateApplicability.Pending;
        }

        // Call-site Origin solving belongs to applicability, not string lookup or type erasure.
        // Whole generic slots remain supported: their substitution preserves the supplied Origins.
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            if (self is null && declaringType is null && function.Parameters[i].Type.BoundType is { } input && HasDeclaredOrigins(input))
            {
                return CandidateApplicability.Pending;
            }
        }

        if (self is null && declaringType is null && function.ReturnType?.BoundType is { } output && HasDeclaredOrigins(output))
        {
            return CandidateApplicability.Pending;
        }

        for (var i = 0; i < function.GenericArguments.Count; i++)
        {
            if (function.GenericArguments[i] is not GenericParameterKoto)
            {
                return CandidateApplicability.Pending;
            }
        }

        if (generic is not null)
        {
            if (generic.TypeArguments.Count != function.GenericArguments.Count)
            {
                return CandidateApplicability.Inapplicable;
            }

            for (var i = 0; i < generic.TypeArguments.Count; i++)
            {
                arguments[i] = generic.TypeArguments[i].BoundType;
                if (arguments[i] is null)
                {
                    return CandidateApplicability.Pending;
                }
            }
        }

        var receiver = this.CallReceiver(generic?.Identifier ?? call.Method);
        var receiverSlot = -1;
        for (var p = 0; p < function.Parameters.Count; p++)
        {
            if (function.Parameters[p].InternalName != "self")
            {
                continue;
            }

            if (receiver?.BoundType is not { } receiverType)
            {
                return CandidateApplicability.Inapplicable;
            }

            if (function.Parameters[p].Type.BoundType is not { } parameterType)
            {
                return CandidateApplicability.Pending;
            }

            if (!InferInput(parameterType, receiverType))
            {
                return CandidateApplicability.Inapplicable;
            }

            used[p] = true;
            receiverSlot = p;
        }

        if (receiver is not null && receiverSlot < 0 && self is not null)
        {
            return CandidateApplicability.Inapplicable;
        }

        var next = 0;
        for (var i = 0; i < call.ArgumentNodes.Count; i++)
        {
            var label = call.GetArgumentLabel(i);
            var slot = -1;
            if (label is not null)
            {
                for (var p = 0; p < function.Parameters.Count; p++)
                {
                    if (function.Parameters[p].ExternalName == label)
                    {
                        slot = p;
                        break;
                    }
                }
            }
            else
            {
                while (next < function.Parameters.Count && used[next])
                {
                    next++;
                }

                slot = next++;
            }

            if (slot < 0 || slot >= function.Parameters.Count || used[slot])
            {
                return CandidateApplicability.Inapplicable;
            }

            used[slot] = true;
            mapping[i] = slot;
            var type = function.Parameters[slot].Type.BoundType;
            if (type is null)
            {
                return CandidateApplicability.Pending;
            }

            if (call.ArgumentNodes[i].BoundType is { } actual && !InferInput(type, actual))
            {
                return CandidateApplicability.Inapplicable;
            }
        }

        for (var i = 0; i < function.Parameters.Count; i++)
        {
            if (!used[i] && !function.Parameters[i].IsOptional)
            {
                return CandidateApplicability.Inapplicable;
            }
        }

        // Established input types cannot change; expectations only fill unresolved slots.
        if (expected is not null && function.BoundSymbol?.Type is { } returnPattern)
        {
            this.Infer(this.MemberType(self is null ? returnPattern : this.ContractType(returnPattern, scope, self), declaringType)!, expected, function, arguments);
        }

        if ((self is not null || declaringType is not null) && receiverSlot >= 0 && receiver?.BoundType is { } actualReceiver && this.CallType(function.Parameters[receiverSlot].Type.BoundType!, function, arguments, scope, self, origins, inputs, declaringType) is { } requiredReceiver && !FitsType(actualReceiver, requiredReceiver))
        {
            return CandidateApplicability.Inapplicable;
        }

        for (var i = 0; i < call.ArgumentNodes.Count; i++)
        {
            var argument = KotoHelper.UnwrapParentheses(call.ArgumentNodes[i]);
            var type = this.CallType(function.Parameters[mapping[i]].Type.BoundType!, function, arguments, scope, self, origins, inputs, declaringType);
            if (type is null)
            {
                // Default only otherwise unconstrained literals; all established inputs were processed above.
                var literal = argument is NumberLiteralKoto number ? number : argument is PrefixMinusKoto or PrefixPlusKoto ? ((UnaryKoto)argument).Operand as NumberLiteralKoto : null;
                if (literal is null)
                {
                    return CandidateApplicability.Pending;
                }

                if (!InferInput(function.Parameters[mapping[i]].Type.BoundType!, BoundType.Primitives[literal.IsInteger ? "i32" : "f64"]))
                {
                    return CandidateApplicability.Inapplicable;
                }

                type = this.CallType(function.Parameters[mapping[i]].Type.BoundType!, function, arguments, scope, self, origins, inputs, declaringType);
                if (type is null)
                {
                    return CandidateApplicability.Pending;
                }
            }

            if (argument is NumberLiteralKoto numeric && !FitsLiteral(numeric, type, false, this.compilation.PointerWidth))
            {
                return CandidateApplicability.Inapplicable;
            }

            if (argument is PrefixMinusKoto { Operand: NumberLiteralKoto negative } && !FitsLiteral(negative, type, true, this.compilation.PointerWidth))
            {
                return CandidateApplicability.Inapplicable;
            }

            if (argument is PrefixPlusKoto { Operand: NumberLiteralKoto positive } && !FitsLiteral(positive, type, false, this.compilation.PointerWidth))
            {
                return CandidateApplicability.Inapplicable;
            }

            if (argument is NullLiteralKoto && type is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Unsafe })
            {
                return CandidateApplicability.Inapplicable;
            }

            if ((self is not null || declaringType is not null) && argument.BoundType is { } actual && !FitsType(actual, type))
            {
                return CandidateApplicability.Inapplicable;
            }
        }

        if ((self is not null || declaringType is not null) && function.BoundSymbol!.Type is { } resultPattern && this.CallType(resultPattern, function, arguments, scope, self, origins, inputs, declaringType) is { } result && HasUnsubstitutedOrigin(result, function))
        {
            // Result-only Origin inference needs the later call-site solver. Never retain a
            // requirement's abstract binder as though it were this call's concrete Origin.
            return CandidateApplicability.Pending;
        }

        var proof = this.CheckConstraints(function.TypeConstraints, function, arguments.AsSpan(0, function.GenericArguments.Count), scope, self, declaringType);
        proof = CombineProof(proof, this.ProveMemberConditions(function.BoundSymbol!, declaringType, scope), true);
        return proof switch
        {
            ConstraintProof.Proven => CandidateApplicability.Applicable,
            ConstraintProof.Refuted => CandidateApplicability.Inapplicable,
            ConstraintProof.Error => CandidateApplicability.Error,
            _ => CandidateApplicability.Pending,
        };
        bool InferInput(BoundType pattern, BoundType actual)
        {
            if (this.MemberType(pattern, declaringType) is not { } memberPattern)
            {
                return false;
            }

            pattern = memberPattern;

            if (self is not null || declaringType is not null)
            {
                pattern = this.ContractType(pattern, scope, self);
                this.MatchInputOrigins(pattern, actual, function, origins, inputs);
                pattern = this.SubstituteStoredOrigins(pattern, function, origins.AsSpan(0, function.Origins.Count), inputs.AsSpan(0, function.Parameters.Count));
            }

            return this.Infer(pattern, actual, function, arguments, self is not null || declaringType is not null);
        }
    }

    private bool Infer(BoundType pattern, BoundType actual, FunctionKoto function, BoundType?[] arguments, bool inferOrigins = false)
    {
        if (ReferenceEquals(actual, BoundType.Never))
        {
            return true;
        }

        if (pattern.Kind == BoundTypeKind.Parameter && pattern.Symbol!.Scope.Owner == function)
        {
            var slot = pattern.Symbol.Slot;
            if (arguments[slot] is { } previous)
            {
                return ReferenceEquals(previous, actual);
            }

            arguments[slot] = actual;
            return true;
        }

        if (ReferenceEquals(pattern, actual))
        {
            return true;
        }

        if (pattern.Kind != actual.Kind || pattern.Symbol != actual.Symbol || pattern.Semantics != actual.Semantics || pattern.Length != actual.Length || !ReferenceEquals(pattern.LengthExpression, actual.LengthExpression) || (!inferOrigins && !ReferenceEquals(pattern.Origin, actual.Origin)) || pattern.OriginArguments.Count != actual.OriginArguments.Count || pattern.Components.Count != actual.Components.Count || pattern.Components.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < pattern.OriginArguments.Count; i++)
        {
            if (!inferOrigins && !ReferenceEquals(pattern.OriginArguments[i], actual.OriginArguments[i]))
            {
                return false;
            }
        }

        for (var i = 0; i < pattern.Components.Count; i++)
        {
            if (!this.Infer(pattern.Components[i], actual.Components[i], function, arguments, inferOrigins))
            {
                return false;
            }
        }

        return true;
    }

    private BoundType? Substitute(BoundType type, FunctionKoto function, BoundType?[] arguments)
        => this.SubstituteType(type, function, arguments);
}
