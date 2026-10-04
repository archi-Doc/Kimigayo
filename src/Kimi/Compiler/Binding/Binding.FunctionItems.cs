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

    private BoundType? BindFunctionItem(Koto use, BindingSymbol symbol)
    {
        // Overload selection, bound generic arguments and requirement references need their own retained selection plan.
        if (symbol.Next is not null || symbol.Declaration is not FunctionKoto function ||
            function.GenericArguments.Count != 0 || function.TypeConstraints.Count != 0 || symbol.ReceiverIndex >= 0 ||
            symbol.Scope.Owner.BoundSymbol?.Schema is { GenericSlots.Count: > 0 } or { Origins.Count: > 0 } ||
            symbol.Intrinsic != IntrinsicKind.None || function.IsDestructor)
        {
            return this.Fail(use, BindingFailure.Unsupported, true);
        }

        var type = this.InternType(BoundTypeKind.FunctionItem, symbol, SemanticsKind.Owner, []);
        if (use is MemberAccessKoto member)
        {
            Complete(member.Right, type);
        }

        return Complete(use, type);
    }
}
