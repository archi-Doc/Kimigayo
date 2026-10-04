// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly List<(BoundType Item, BoundCall Context)> functionItemContexts = new();

    // A declaration and its bound generic arguments are the complete identity of its zero-sized Item. Its signature is a
    // call contract, not stored environment data; in particular, per-call input Origins do not make the Item borrow anything.
    internal BoundType? FunctionItemSignature(BoundType type)
    {
        if (type.Kind != BoundTypeKind.FunctionItem || type.Symbol is not { Type: { } result, Declaration: FunctionKoto function })
        {
            return null;
        }

        var parameters = this.RentTypes(function.Parameters.Count);
        try
        {
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                if (function.Parameters[i].Type.BoundType is not { } parameter || this.ItemType(parameter, type, function) is not { } bound)
                {
                    return null;
                }

                parameters[i] = bound;
            }

            if (this.ItemType(result, type, function) is not { } boundResult)
            {
                return null;
            }

            var inputs = function.Parameters.Count == 0 ? BoundType.Unit : this.InternType(BoundTypeKind.Tuple, null, SemanticsKind.Owner, parameters.AsSpan(0, function.Parameters.Count));
            return this.InternType(BoundTypeKind.Function, null, SemanticsKind.Owner, [inputs, boundResult]);
        }
        finally
        {
            this.typeScratch.Return(parameters, clearArray: true);
        }
    }

    // The call context of a generic Item whose bound arguments are closed: the instance its calls and erasure enter. One
    // context is retained per Item Type, so repeated preparation finds the same generic entry and allocates nothing.
    internal BoundCall? FunctionItemContext(BoundType type)
    {
        if (type.Kind != BoundTypeKind.FunctionItem || type.Components.Count == 0 || type.ContainsParameter ||
            type.Symbol is not { Type: { } result, Declaration: FunctionKoto function })
        {
            return null;
        }

        foreach (var (item, context) in this.functionItemContexts)
        {
            if (ReferenceEquals(item, type))
            {
                return context;
            }
        }

        if (this.ItemType(result, type, function) is not { } boundResult)
        {
            return null;
        }

        var created = new BoundCall();
        created.Set(type.Symbol, boundResult, null, [], (BoundType[])type.Components);
        this.functionItemContexts.Add((type, created));
        return created;
    }

    private static bool IsWaitingFunctionReference(Koto node)
        => KotoHelper.UnwrapParentheses(node) is not FunctionKoto and { BoundType: null, BindingState: BindingState.Resolved, BoundSymbol.Kind: BindingSymbolKind.Function };

    private static bool IsWaitingCallable(Koto node)
        => KotoHelper.UnwrapParentheses(node) is FunctionKoto { IsAnonymous: true, BoundType: null } || IsWaitingFunctionReference(node);

    private static bool IndependentFunctionItem(BindingSymbol symbol)
        => symbol.Next is null && symbol.Declaration is FunctionKoto { GenericArguments.Count: 0, TypeConstraints.Count: 0, IsDestructor: false } &&
            symbol.ReceiverIndex < 0 && symbol.Scope.Owner.BoundSymbol?.Schema is not { GenericSlots.Count: > 0 } and not { Origins.Count: > 0 } &&
            symbol.Intrinsic == IntrinsicKind.None;

    // The bound arguments of a generic Item are its Components, in declaration-slot order; per-call Origins stay in place.
    private BoundType? ItemType(BoundType type, BoundType item, FunctionKoto function)
        => item.Components.Count == 0 ? type : this.SubstituteType(type, function, (BoundType[])item.Components);

    // A fixed Function or Callable signature can select a function reference after the outer candidate is determined.
    private bool TakesCallableContext(BindingSymbol? group)
    {
        for (var candidate = group; candidate is not null; candidate = candidate.Next)
        {
            if (candidate.Declaration is FunctionKoto function)
            {
                for (var i = 0; i < function.Parameters.Count; i++)
                {
                    if (function.Parameters[i].Type.BoundType is { } type && this.TryCallable(type, this.ConstraintScope(function), out _, out _))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private BoundType? BindFunctionItem(Koto use, BindingSymbol symbol, BindingScope scope)
    {
        if (KotoHelper.UnwrapParentheses(use) is GenericsKoto explicitReference)
        {
            return this.BindExplicitFunctionItem(use, explicitReference, symbol, scope);
        }

        if (!IndependentFunctionItem(symbol))
        {
            return this.FailUnfixedReference(use, symbol);
        }

        return this.CompleteFunctionItem(use, symbol);
    }

    // SPEC 10.5: without a fixed expected call signature, a reference is a value only when one candidate remains and that
    // candidate has no unbound own slots. Forms whose references are not yet selected stay explicitly unsupported.
    private BoundType? FailUnfixedReference(Koto use, BindingSymbol symbol)
    {
        var count = 0;
        for (var candidate = symbol; candidate is not null; candidate = candidate.Next)
        {
            this.BindHeader(candidate);
            if (candidate.Declaration is not FunctionKoto function || UnsupportedReference(candidate, function) || function.IsDestructor ||
                candidate.Intrinsic != IntrinsicKind.None || (function.GenericArguments.Count == 0 && function.TypeConstraints.Count != 0))
            {
                return this.Fail(use, BindingFailure.Unsupported, true);
            }

            count++;
        }

        if (count == 1)
        {
            return this.Fail(use, BindingFailure.UnboundTypeArgument);
        }

        var rejected = new RejectedCandidate[count];
        var next = 0;
        for (var candidate = symbol; candidate is not null; candidate = candidate.Next)
        {
            var item = this.InternType(BoundTypeKind.FunctionItem, candidate, SemanticsKind.Owner, []);
            rejected[next++] = new((FunctionKoto)candidate.Declaration, this.FunctionItemSignature(item), null, UnfixedReference: true);
        }

        (this.rejectedCandidates ??= new(ReferenceEqualityComparer.Instance))[use] = rejected;
        return this.Fail(use, BindingFailure.Ambiguous);
    }

    // SPEC 10.5: without a fixed expected call signature, a reference with explicit Type arguments is a value when exactly one
    // candidate takes those arguments; its Constraints are proven for them.
    private BoundType? BindExplicitFunctionItem(Koto use, GenericsKoto explicitReference, BindingSymbol symbol, BindingScope scope)
    {
        var count = explicitReference.TypeArguments.Count;
        BindingSymbol? selected = null;
        var candidates = 0;
        for (var candidate = symbol; candidate is not null; candidate = candidate.Next)
        {
            this.BindHeader(candidate);
            if (candidate.Declaration is not FunctionKoto function || function.GenericArguments.Count != count || !this.Accessible(candidate, scope))
            {
                continue;
            }

            if (UnsupportedReference(candidate, function))
            {
                return this.Fail(use, BindingFailure.Unsupported, true);
            }

            selected = candidate;
            candidates++;
        }

        if (candidates != 1)
        {
            return this.Fail(use, candidates == 0 ? BindingFailure.NoApplicableCandidate : BindingFailure.Ambiguous);
        }

        var target = (FunctionKoto)selected!.Declaration;
        if ((target.Modifier & ModifierKind.Unsafe) != 0)
        {
            return this.Fail(use, BindingFailure.UnsafeFunctionValue);
        }

        var arguments = this.typeScratch.Rent(count);
        try
        {
            if (!this.ExplicitReferenceArguments(explicitReference, arguments))
            {
                return null;
            }

            var proof = this.ReferenceArgumentProof(target, arguments, scope);
            if (proof != ConstraintProof.Proven)
            {
                this.RequireConstraint(use, proof, this.capabilityMode);
                return null;
            }

            return this.CompleteFunctionItem(use, selected, arguments.AsSpan(0, count));
        }
        finally
        {
            this.typeScratch.Return(arguments, clearArray: true);
        }
    }

    // The bound Types of a reference's explicit Type arguments; a length argument is not yet referenced.
    private bool ExplicitReferenceArguments(GenericsKoto explicitReference, BoundType?[] arguments)
    {
        for (var i = 0; i < explicitReference.TypeArguments.Count; i++)
        {
            if ((arguments[i] = explicitReference.TypeArguments[i].BoundType) is null)
            {
                return false;
            }
        }

        return true;
    }

    private BoundType CompleteFunctionItem(Koto use, BindingSymbol symbol, ReadOnlySpan<BoundType?> typeArguments = default)
    {
        var type = this.FunctionItemType(symbol, typeArguments);
        while (use is ParenthesizedKoto parentheses)
        {
            use.BoundSymbol = symbol;
            Complete(use, type);
            use = parentheses.Operand;
        }

        use.BoundSymbol = symbol;
        var name = use is GenericsKoto { Identifier: { } identifier } ? identifier : use;
        if (!ReferenceEquals(name, use))
        {
            name.BoundSymbol = symbol;
            Complete(name, type);
        }

        if (name is MemberAccessKoto member)
        {
            member.Right.BoundSymbol = symbol;
            Complete(member.Right, type);
        }

        Complete(use, type);
        return type;
    }

    private BoundType FunctionItemType(BindingSymbol symbol, ReadOnlySpan<BoundType?> typeArguments)
    {
        if (typeArguments.IsEmpty)
        {
            return this.InternType(BoundTypeKind.FunctionItem, symbol, SemanticsKind.Owner, []);
        }

        var components = this.RentTypes(typeArguments.Length);
        try
        {
            for (var i = 0; i < typeArguments.Length; i++)
            {
                components[i] = typeArguments[i]!;
            }

            return this.InternType(BoundTypeKind.FunctionItem, symbol, SemanticsKind.Owner, components.AsSpan(0, typeArguments.Length));
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
        }
    }
}
