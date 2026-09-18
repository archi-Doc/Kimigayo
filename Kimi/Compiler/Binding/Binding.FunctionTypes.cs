// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private static bool PerCallSignature(BoundType signature)
    {
        if (HasDeclaredOrigins(signature.Components[1]))
        {
            return false;
        }

        var inputs = signature.Components[0];
        for (var i = 0; i < inputs.Components.Count; i++)
        {
            var input = inputs.Components[i];
            if (HasDeclaredOrigins(input) && !(input is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref, Components.Count: 1, OriginArguments.Count: 0, Origin.Kind: OriginKind.Input } &&
                input.Origin.Slot == i && !HasDeclaredOrigins(input.Components[0])))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CallableSignatureFits(BoundType actual, BoundType expected)
    {
        if (FitsType(actual, expected))
        {
            return true;
        }

        if (!PerCallSignature(actual) || !PerCallSignature(expected) || !FitsType(actual.Components[1], expected.Components[1]))
        {
            return false;
        }

        var a = actual.Components[0];
        var b = expected.Components[0];
        if (a.Components.Count != b.Components.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Components.Count; i++)
        {
            var input = a.Components[i];
            var required = b.Components[i];
            if (!FitsType(required, input) && !(input.Origin is { Kind: OriginKind.Input } && required.Origin is { Kind: OriginKind.Input } &&
                input.Semantics == SemanticsKind.Ref && required.Semantics == SemanticsKind.Ref && ReferenceEquals(input.Components[0], required.Components[0])))
            {
                return false;
            }
        }

        return true;
    }

    private BoundType? BindFunctionType(FunctionTypeKoto function, BindingScope scope, TypeBindingContext context)
    {
        var tuple = function.Parameters as TupleTypeKoto;
        var count = tuple?.ElementNodes.Count ?? 1;
        var scratch = this.RentTypes(count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                var syntax = tuple is null ? function.Parameters : tuple.ElementNodes[i];
                if (this.BindType(syntax, scope, new(TypePosition.Parameter, function, i, true)) is not { } input)
                {
                    return null;
                }

                scratch[i] = input;
            }

            var parameters = count == 0 ? BoundType.Unit : this.InternType(BoundTypeKind.Tuple, null, SemanticsKind.Owner, scratch.AsSpan(0, count));
            if (tuple is not null)
            {
                Complete(tuple, parameters);
            }

            var result = this.BindType(function.ReturnType, scope, context.Nested);
            return result is null ? null : this.InternType(BoundTypeKind.Function, null, SemanticsKind.Owner, [parameters, result]);
        }
        finally
        {
            this.typeScratch.Return(scratch, clearArray: true);
        }
    }
}
