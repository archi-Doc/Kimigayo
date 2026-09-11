// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // The call plan accompanies its binder.

/// <summary>The committed target and argument mapping; storage is reused when the same call is rebound.</summary>
public sealed class BoundCall
{
    private int[] mapping = [];
    private BoundType[] typeArguments = [];

    /// <summary>Gets the selected function Symbol.</summary>
    public BindingSymbol Target { get; private set; } = null!;

    /// <summary>Gets the complete return type after substitution.</summary>
    public BoundType ReturnType { get; private set; } = null!;

    /// <summary>Gets the explicit receiver when member-call syntax supplies it.</summary>
    public Koto? Receiver { get; private set; }

    /// <summary>Gets the symbolic conforming Type for a definition-bound requirement call.</summary>
    public BoundType? ConformingType { get; private set; }

    /// <summary>Gets source-argument index to parameter-slot mappings.</summary>
    public ReadOnlySpan<int> ArgumentToParameter => this.mapping;

    /// <summary>Gets the selected complete type arguments.</summary>
    public ReadOnlySpan<BoundType> TypeArguments => this.typeArguments;

    internal void Set(BindingSymbol target, BoundType result, Koto? receiver, ReadOnlySpan<int> mapping, ReadOnlySpan<BoundType?> typeArguments, BoundType? conformingType = null)
    {
        this.Target = target;
        this.ReturnType = result;
        this.Receiver = receiver;
        this.ConformingType = conformingType;
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
        var qualifier = this.TypeName(member.Left, scope, false);
        if (qualifier?.Type is { } type && qualifier.Declaration is not ContractKoto)
        {
            typeMember = this.RequirementMember(member, scope, type, true);
        }

        if (qualifier is not null && qualifier.Declaration is not ContractKoto && this.scopes.TryGetValue(qualifier.Declaration, out var typeScope))
        {
            if (qualifier.Type is { } qualifiedType)
            {
                typeSelection = this.LookupTypeMember(qualifiedType, right.IdentifierName);
                typeMember = typeSelection.Member ?? typeMember;
            }
            else if (typeScope.Values.TryGetValue(right.IdentifierName, out var candidate))
            {
                typeMember = candidate;
            }
        }

        var valuePossible = member.Left is not IdentifierNameKoto leftName || this.Lookup(leftName.IdentifierName, scope, member.Left, false) is not null;
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

        BindingSymbol? winner = null;
        var requirementGroup = callee is MemberAccessKoto requirementMember && this.requirementGroups.TryGetValue(requirementMember, out var foundGroup) && foundGroup.Active ? foundGroup : null;
        var candidates = new CallCandidates(group, requirementGroup);
        var self = requirementGroup?.Self;
        var applicable = 0;
        var pending = false;
        var maxParameters = 0;
        var maxGenerics = 0;
        var maxOrigins = 0;
        foreach (var candidate in candidates)
        {
            if (candidate.Declaration is not FunctionKoto function)
            {
                continue;
            }

            maxParameters = Math.Max(maxParameters, function.Parameters.Count);
            maxGenerics = Math.Max(maxGenerics, function.GenericArguments.Count);
            maxOrigins = Math.Max(maxOrigins, function.Origins.Count);
        }

        var scratch = this.typeScratch.Rent(Math.Max(1, maxGenerics));
        var mapping = this.indexScratch.Rent(Math.Max(1, call.ArgumentNodes.Count));
        var used = this.flagScratch.Rent(Math.Max(1, maxParameters));
        var origins = self is null ? Array.Empty<BoundOrigin>() : this.originScratch.Rent(maxOrigins);
        var inputs = self is null ? Array.Empty<BoundOrigin>() : this.originScratch.Rent(maxParameters);
        try
        {
            foreach (var candidate in candidates)
            {
                if (!this.Accessible(candidate, scope) || candidate.Declaration is not FunctionKoto function)
                {
                    continue;
                }

                this.BindHeader(candidate);
                var match = this.TryCandidate(call, function, generic, scope, scratch, mapping, used, expected, self, origins, inputs);
                if (match is null)
                {
                    pending = true;
                }

                if (match != true)
                {
                    continue;
                }

                applicable++;
                winner = candidate;
            }

            // An unresolved candidate can change the final winner, so do not commit around it.
            if (pending)
            {
                return Fail(call, BindingFailure.NoApplicableCandidate, true);
            }

            if (applicable == 0)
            {
                return Fail(call, BindingFailure.NoApplicableCandidate, true);
            }

            if (applicable != 1)
            {
                winner = this.SelectBest(call, candidates, generic, scope, expected, scratch, mapping, used, maxGenerics, self, origins, inputs);
                if (winner is null)
                {
                    return Fail(call, BindingFailure.Ambiguous, true);
                }
            }

            var selected = (FunctionKoto)winner!.Declaration;
            this.TryCandidate(call, selected, generic, scope, scratch, mapping, used, expected, self, origins, inputs);
            if (selected.GenericArguments.Count != 0 && expected is not null && winner.Type is { } returnPattern)
            {
                this.Infer(returnPattern, expected, selected, scratch);
            }

            for (var i = 0; i < selected.GenericArguments.Count; i++)
            {
                if (scratch[i] is null)
                {
                    return Fail(call, BindingFailure.MissingType, true);
                }
            }

            var result = winner.Type is { } returnType ? this.CallType(returnType, selected, scratch, scope, self, origins, inputs) : null;
            if (result is null)
            {
                return Fail(call, BindingFailure.MissingType, true);
            }

            for (var i = 0; i < call.ArgumentNodes.Count; i++)
            {
                var type = this.CallType(selected.Parameters[mapping[i]].Type.BoundType!, selected, scratch, scope, self, origins, inputs);
                this.RequireType(call.ArgumentNodes[i], scope, type);
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
            (call.CallStorage ??= new()).Set(winner, result, this.CallReceiver(callee), mapping.AsSpan(0, call.ArgumentNodes.Count), scratch.AsSpan(0, selected.GenericArguments.Count), self);
            return Complete(call, result);
        }
        finally
        {
            this.typeScratch.Return(scratch, clearArray: true);
            this.indexScratch.Return(mapping);
            this.flagScratch.Return(used);
            if (self is not null)
            {
                this.originScratch.Return(inputs, clearArray: true);
                this.originScratch.Return(origins, clearArray: true);
            }
        }
    }

    private Koto? CallReceiver(Koto callee)
        => callee is MemberAccessKoto member && (this.requirementGroups.TryGetValue(member, out var group) && group.Active ? !group.TypeAccess : member.Left.BoundSymbol?.Kind is not (BindingSymbolKind.Type or BindingSymbolKind.Container)) ? member.Left : null;

    private bool? TryCandidate(InvocationKoto call, FunctionKoto function, GenericsKoto? generic, BindingScope scope, BoundType?[] arguments, int[] mapping, bool[] used, BoundType? expected, BoundType? self, BoundOrigin[] origins, BoundOrigin[] inputs)
    {
        Array.Clear(arguments, 0, function.GenericArguments.Count);
        Array.Clear(used, 0, function.Parameters.Count);
        if (self is not null)
        {
            Array.Clear(origins, 0, function.Origins.Count);
            Array.Clear(inputs, 0, function.Parameters.Count);
        }

        if (function.IsSpecialization)
        {
            return null;
        }

        // Call-site Origin solving belongs to applicability, not string lookup or type erasure.
        // Whole generic slots remain supported: their substitution preserves the supplied Origins.
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            if (self is null && function.Parameters[i].Type.BoundType is { } input && HasDeclaredOrigins(input))
            {
                return null;
            }
        }

        if (self is null && function.ReturnType?.BoundType is { } output && HasDeclaredOrigins(output))
        {
            return null;
        }

        for (var i = 0; i < function.GenericArguments.Count; i++)
        {
            if (function.GenericArguments[i] is not GenericParameterKoto)
            {
                return null;
            }
        }

        if (generic is not null)
        {
            if (generic.TypeArguments.Count != function.GenericArguments.Count)
            {
                return false;
            }

            for (var i = 0; i < generic.TypeArguments.Count; i++)
            {
                arguments[i] = generic.TypeArguments[i].BoundType;
                if (arguments[i] is null)
                {
                    return null;
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
                return false;
            }

            if (function.Parameters[p].Type.BoundType is not { } parameterType)
            {
                return null;
            }

            if (!InferInput(parameterType, receiverType))
            {
                return false;
            }

            used[p] = true;
            receiverSlot = p;
        }

        if (receiver is not null && receiverSlot < 0 && self is not null)
        {
            return false;
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
                return false;
            }

            used[slot] = true;
            mapping[i] = slot;
            var type = function.Parameters[slot].Type.BoundType;
            if (type is null)
            {
                return null;
            }

            if (call.ArgumentNodes[i].BoundType is { } actual && !InferInput(type, actual))
            {
                return false;
            }
        }

        for (var i = 0; i < function.Parameters.Count; i++)
        {
            if (!used[i] && !function.Parameters[i].IsOptional)
            {
                return false;
            }
        }

        // Established input types cannot change; expectations only fill unresolved slots.
        if (expected is not null && function.BoundSymbol?.Type is { } returnPattern)
        {
            this.Infer(self is null ? returnPattern : this.ContractType(returnPattern, scope, self), expected, function, arguments);
        }

        if (self is not null && receiverSlot >= 0 && receiver?.BoundType is { } actualReceiver && this.CallType(function.Parameters[receiverSlot].Type.BoundType!, function, arguments, scope, self, origins, inputs) is { } requiredReceiver && !FitsType(actualReceiver, requiredReceiver))
        {
            return false;
        }

        for (var i = 0; i < call.ArgumentNodes.Count; i++)
        {
            var argument = KotoHelper.UnwrapParentheses(call.ArgumentNodes[i]);
            var type = this.CallType(function.Parameters[mapping[i]].Type.BoundType!, function, arguments, scope, self, origins, inputs);
            if (type is null)
            {
                // Default only otherwise unconstrained literals; all established inputs were processed above.
                var literal = argument is NumberLiteralKoto number ? number : argument is PrefixMinusKoto or PrefixPlusKoto ? ((UnaryKoto)argument).Operand as NumberLiteralKoto : null;
                if (literal is null)
                {
                    return null;
                }

                if (!InferInput(function.Parameters[mapping[i]].Type.BoundType!, BoundType.Primitives[literal.IsInteger ? "i32" : "f64"]))
                {
                    return false;
                }

                type = this.CallType(function.Parameters[mapping[i]].Type.BoundType!, function, arguments, scope, self, origins, inputs);
                if (type is null)
                {
                    return null;
                }
            }

            if (argument is NumberLiteralKoto numeric && !FitsLiteral(numeric, type, false, this.compilation.PointerWidth))
            {
                return false;
            }

            if (argument is PrefixMinusKoto { Operand: NumberLiteralKoto negative } && !FitsLiteral(negative, type, true, this.compilation.PointerWidth))
            {
                return false;
            }

            if (argument is PrefixPlusKoto { Operand: NumberLiteralKoto positive } && !FitsLiteral(positive, type, false, this.compilation.PointerWidth))
            {
                return false;
            }

            if (argument is NullLiteralKoto && type is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Unsafe })
            {
                return false;
            }

            if (self is not null && argument.BoundType is { } actual && !FitsType(actual, type))
            {
                return false;
            }
        }

        if (self is not null && function.BoundSymbol!.Type is { } resultPattern && this.CallType(resultPattern, function, arguments, scope, self, origins, inputs) is { } result && HasUnsubstitutedOrigin(result, function))
        {
            // Result-only Origin inference needs the later call-site solver. Never retain a
            // requirement's abstract binder as though it were this call's concrete Origin.
            return null;
        }

        return this.CheckConstraints(function.TypeConstraints, function, arguments.AsSpan(0, function.GenericArguments.Count), scope, self) switch
        {
            ConstraintProof.Proven => true,
            ConstraintProof.Refuted or ConstraintProof.Error => false,
            _ => null,
        };

        bool InferInput(BoundType pattern, BoundType actual)
        {
            if (self is not null)
            {
                pattern = this.ContractType(pattern, scope, self);
                this.MatchInputOrigins(pattern, actual, function, origins, inputs);
                pattern = this.SubstituteStoredOrigins(pattern, function, origins.AsSpan(0, function.Origins.Count), inputs.AsSpan(0, function.Parameters.Count));
            }

            return this.Infer(pattern, actual, function, arguments, self is not null);
        }
    }

    private BindingSymbol? SelectBest(InvocationKoto call, CallCandidates candidates, GenericsKoto? generic, BindingScope scope, BoundType? expected, BoundType?[] aTypes, int[] aMap, bool[] used, int maxGenerics, BoundType? self, BoundOrigin[] origins, BoundOrigin[] inputs)
    {
        var bTypes = this.typeScratch.Rent(Math.Max(1, maxGenerics));
        var bMap = this.indexScratch.Rent(Math.Max(1, call.ArgumentNodes.Count));
        try
        {
            foreach (var a in candidates)
            {
                if (a.Declaration is not FunctionKoto fa || !this.Accessible(a, scope) || this.TryCandidate(call, fa, generic, scope, aTypes, aMap, used, expected, self, origins, inputs) != true)
                {
                    continue;
                }

                var dominates = true;
                foreach (var b in candidates)
                {
                    if (ReferenceEquals(a, b) || b.Declaration is not FunctionKoto fb || !this.Accessible(b, scope) || this.TryCandidate(call, fb, generic, scope, bTypes, bMap, used, expected, self, origins, inputs) != true)
                    {
                        continue;
                    }

                    // This initial binder admits exact arguments and literal fitting. Distinct
                    // parameter types are incomparable here, never ranked by numeric width.
                    var equal = true;
                    for (var i = 0; i < call.ArgumentNodes.Count; i++)
                    {
                        equal &= ReferenceEquals(this.Substitute(fa.Parameters[aMap[i]].Type.BoundType!, fa, aTypes), this.Substitute(fb.Parameters[bMap[i]].Type.BoundType!, fb, bTypes));
                    }

                    var aGeneric = fa.GenericArguments.Count != 0;
                    var bGeneric = fb.GenericArguments.Count != 0;
                    var better = equal && (aGeneric != bGeneric ? !aGeneric : fa.Parameters.Count < fb.Parameters.Count);
                    if (!better)
                    {
                        dominates = false;
                        break;
                    }
                }

                if (dominates)
                {
                    return a;
                }
            }

            return null;
        }
        finally
        {
            this.typeScratch.Return(bTypes, clearArray: true);
            this.indexScratch.Return(bMap);
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
