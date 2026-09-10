// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
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

    /// <summary>Gets source-argument index to parameter-slot mappings.</summary>
    public ReadOnlySpan<int> ArgumentToParameter => this.mapping;

    /// <summary>Gets the selected complete type arguments.</summary>
    public ReadOnlySpan<BoundType> TypeArguments => this.typeArguments;

    internal void Set(BindingSymbol target, BoundType result, Koto? receiver, ReadOnlySpan<int> mapping, ReadOnlySpan<BoundType?> typeArguments)
    {
        this.Target = target;
        this.ReturnType = result;
        this.Receiver = receiver;
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
        var qualifier = this.TypeName(member.Left, scope, false);
        if (qualifier is not null && this.scopes.TryGetValue(qualifier.Declaration, out var typeScope))
        {
            if (typeScope.Values.TryGetValue(right.IdentifierName, out var candidate) && this.Accessible(candidate, scope))
            {
                typeMember = candidate;
            }
        }

        var valuePossible = member.Left is not IdentifierNameKoto leftName || this.Lookup(leftName.IdentifierName, scope, member.Left, false) is not null;
        if (valuePossible)
        {
            var receiverType = this.BindNode(member.Left, scope);
            if (receiverType?.Symbol is { } typeSymbol && this.scopes.TryGetValue(typeSymbol.Declaration, out var valueScope) && valueScope.Values.TryGetValue(right.IdentifierName, out var candidate) && this.Accessible(candidate, scope))
            {
                valueMember = candidate;
            }
        }

        if (typeMember is not null && valueMember is not null)
        {
            Fail(member, BindingFailure.Ambiguous, true);
            return null;
        }

        var selected = typeMember ?? valueMember;
        if (selected is not null)
        {
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
        var applicable = 0;
        var pending = false;
        var maxParameters = 0;
        var maxGenerics = 0;
        for (var candidate = group; candidate is not null; candidate = candidate.Next)
        {
            if (candidate.Declaration is not FunctionKoto function)
            {
                continue;
            }

            maxParameters = Math.Max(maxParameters, function.Parameters.Count);
            maxGenerics = Math.Max(maxGenerics, function.GenericArguments.Count);
        }

        var scratch = ArrayPool<BoundType?>.Shared.Rent(Math.Max(1, maxGenerics));
        var mapping = ArrayPool<int>.Shared.Rent(Math.Max(1, call.ArgumentNodes.Count));
        var used = ArrayPool<bool>.Shared.Rent(Math.Max(1, maxParameters));
        try
        {
            for (var candidate = group; candidate is not null; candidate = candidate.Next)
            {
                if (!this.Accessible(candidate, scope) || candidate.Declaration is not FunctionKoto function)
                {
                    continue;
                }

                this.BindHeader(candidate);
                var match = this.TryCandidate(call, function, generic, scope, scratch, mapping, used, expected);
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
                winner = this.SelectBest(call, group, generic, scope, expected, scratch, mapping, used, maxGenerics);
                if (winner is null)
                {
                    return Fail(call, BindingFailure.Ambiguous, true);
                }
            }

            var selected = (FunctionKoto)winner!.Declaration;
            this.TryCandidate(call, selected, generic, scope, scratch, mapping, used, expected);
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

            var result = winner.Type is { } returnType ? this.Substitute(returnType, selected, scratch) : null;
            if (result is null)
            {
                return Fail(call, BindingFailure.MissingType, true);
            }

            for (var i = 0; i < call.ArgumentNodes.Count; i++)
            {
                var type = this.Substitute(selected.Parameters[mapping[i]].Type.BoundType!, selected, scratch);
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
            (call.CallStorage ??= new()).Set(winner, result, this.CallReceiver(callee), mapping.AsSpan(0, call.ArgumentNodes.Count), scratch.AsSpan(0, selected.GenericArguments.Count));
            return Complete(call, result);
        }
        finally
        {
            ArrayPool<BoundType?>.Shared.Return(scratch, clearArray: true);
            ArrayPool<int>.Shared.Return(mapping);
            ArrayPool<bool>.Shared.Return(used);
        }
    }

    private Koto? CallReceiver(Koto callee)
        => callee is MemberAccessKoto member && member.Left.BoundSymbol?.Kind is not (BindingSymbolKind.Type or BindingSymbolKind.Container) ? member.Left : null;

    private bool? TryCandidate(InvocationKoto call, FunctionKoto function, GenericsKoto? generic, BindingScope scope, BoundType?[] arguments, int[] mapping, bool[] used, BoundType? expected)
    {
        Array.Clear(arguments, 0, function.GenericArguments.Count);
        Array.Clear(used, 0, function.Parameters.Count);
        if (function.IsSpecialization || function.TypeConstraints.Count != 0)
        {
            return null;
        }

        // Call-site Origin solving belongs to applicability, not string lookup or type erasure.
        // Whole generic slots remain supported: their substitution preserves the supplied Origins.
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            if (function.Parameters[i].Type.BoundType is { } input && HasDeclaredOrigins(input))
            {
                return null;
            }
        }

        if (function.ReturnType?.BoundType is { } output && HasDeclaredOrigins(output))
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

            if (!this.Infer(parameterType, receiverType, function, arguments))
            {
                return false;
            }

            used[p] = true;
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

            if (call.ArgumentNodes[i].BoundType is { } actual && !this.Infer(type, actual, function, arguments))
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
            this.Infer(returnPattern, expected, function, arguments);
        }

        for (var i = 0; i < call.ArgumentNodes.Count; i++)
        {
            var argument = KotoHelper.UnwrapParentheses(call.ArgumentNodes[i]);
            var type = this.Substitute(function.Parameters[mapping[i]].Type.BoundType!, function, arguments);
            if (type is null)
            {
                // Default only otherwise unconstrained literals; all established inputs were processed above.
                var literal = argument is NumberLiteralKoto number ? number : argument is PrefixMinusKoto or PrefixPlusKoto ? ((UnaryKoto)argument).Operand as NumberLiteralKoto : null;
                if (literal is null)
                {
                    return null;
                }

                if (!this.Infer(function.Parameters[mapping[i]].Type.BoundType!, BoundType.Primitives[literal.IsInteger ? "i32" : "f64"], function, arguments))
                {
                    return false;
                }

                type = this.Substitute(function.Parameters[mapping[i]].Type.BoundType!, function, arguments);
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
        }

        return true;
    }

    private BindingSymbol? SelectBest(InvocationKoto call, BindingSymbol group, GenericsKoto? generic, BindingScope scope, BoundType? expected, BoundType?[] aTypes, int[] aMap, bool[] used, int maxGenerics)
    {
        var bTypes = ArrayPool<BoundType?>.Shared.Rent(Math.Max(1, maxGenerics));
        var bMap = ArrayPool<int>.Shared.Rent(Math.Max(1, call.ArgumentNodes.Count));
        try
        {
            for (var a = group; a is not null; a = a.Next)
            {
                if (a.Declaration is not FunctionKoto fa || !this.Accessible(a, scope) || this.TryCandidate(call, fa, generic, scope, aTypes, aMap, used, expected) != true)
                {
                    continue;
                }

                var dominates = true;
                for (var b = group; b is not null; b = b.Next)
                {
                    if (ReferenceEquals(a, b) || b.Declaration is not FunctionKoto fb || !this.Accessible(b, scope) || this.TryCandidate(call, fb, generic, scope, bTypes, bMap, used, expected) != true)
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
            ArrayPool<BoundType?>.Shared.Return(bTypes, clearArray: true);
            ArrayPool<int>.Shared.Return(bMap);
        }
    }

    private bool Infer(BoundType pattern, BoundType actual, FunctionKoto function, BoundType?[] arguments)
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

        if (pattern.Kind != actual.Kind || pattern.Symbol != actual.Symbol || pattern.Semantics != actual.Semantics || pattern.Length != actual.Length || !ReferenceEquals(pattern.LengthExpression, actual.LengthExpression) || !ReferenceEquals(pattern.Origin, actual.Origin) || pattern.OriginArguments.Count != actual.OriginArguments.Count || pattern.Components.Count != actual.Components.Count || pattern.Components.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < pattern.OriginArguments.Count; i++)
        {
            if (!ReferenceEquals(pattern.OriginArguments[i], actual.OriginArguments[i]))
            {
                return false;
            }
        }

        for (var i = 0; i < pattern.Components.Count; i++)
        {
            if (!this.Infer(pattern.Components[i], actual.Components[i], function, arguments))
            {
                return false;
            }
        }

        return true;
    }

    private BoundType? Substitute(BoundType type, FunctionKoto function, BoundType?[] arguments)
        => this.SubstituteType(type, function, arguments);
}
