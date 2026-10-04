// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // A nongeneric declaration is the complete identity of its zero-sized Item. Its signature is a call contract,
    // not stored environment data; in particular, per-call input Origins do not make the Item borrow anything.
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
                if (function.Parameters[i].Type.BoundType is not { } parameter)
                {
                    return null;
                }

                parameters[i] = parameter;
            }

            var inputs = function.Parameters.Count == 0 ? BoundType.Unit : this.InternType(BoundTypeKind.Tuple, null, SemanticsKind.Owner, parameters.AsSpan(0, function.Parameters.Count));
            return this.InternType(BoundTypeKind.Function, null, SemanticsKind.Owner, [inputs, result]);
        }
        finally
        {
            this.typeScratch.Return(parameters, clearArray: true);
        }
    }

    private static bool IsWaitingFunctionReference(Koto node)
        => KotoHelper.UnwrapParentheses(node) is not FunctionKoto and { BoundType: null, BindingState: BindingState.Resolved, BoundSymbol.Kind: BindingSymbolKind.Function };

    private static bool IsWaitingCallable(Koto node)
        => KotoHelper.UnwrapParentheses(node) is FunctionKoto { IsAnonymous: true, BoundType: null } || IsWaitingFunctionReference(node);

    private static bool IndependentFunctionItem(BindingSymbol symbol)
        => symbol.Next is null && symbol.Declaration is FunctionKoto { GenericArguments.Count: 0, TypeConstraints.Count: 0, IsDestructor: false } &&
            symbol.ReceiverIndex < 0 && symbol.Scope.Owner.BoundSymbol?.Schema is not { GenericSlots.Count: > 0 } and not { Origins.Count: > 0 } &&
            symbol.Intrinsic == IntrinsicKind.None;

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

    private BoundType? BindFunctionItem(Koto use, BindingSymbol symbol)
    {
        // Overload selection, bound generic arguments and requirement references need their own retained selection plan.
        if (!IndependentFunctionItem(symbol))
        {
            return this.Fail(use, BindingFailure.Unsupported, true);
        }

        return this.CompleteFunctionItem(use, symbol);
    }

    private BoundType CompleteFunctionItem(Koto use, BindingSymbol symbol)
    {
        var type = this.InternType(BoundTypeKind.FunctionItem, symbol, SemanticsKind.Owner, []);
        while (use is ParenthesizedKoto parentheses)
        {
            use.BoundSymbol = symbol;
            Complete(use, type);
            use = parentheses.Operand;
        }

        use.BoundSymbol = symbol;
        if (use is MemberAccessKoto member)
        {
            member.Right.BoundSymbol = symbol;
            Complete(member.Right, type);
        }

        Complete(use, type);
        return type;
    }
}
