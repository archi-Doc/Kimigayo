// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly List<(FunctionKoto Function, CallPlan Context)> closureContexts = new();

    internal BoundType? ClosureSignature(BoundType type)
        => type.Symbol?.Declaration is FunctionKoto { BoundClosure: { } closure }
            ? type.ClosureContext is { } context ? this.InstantiateStorageType(closure.Signature, context) : closure.Signature : null;

    private BoundType? CloseClosureTypes(BoundType type, CallPlan call)
    {
        if (type.ClosureContext is not null || (type.Components.Count == 0 && type.Kind != BoundTypeKind.Closure))
        {
            return type;
        }

        var scratch = this.RentTypes(type.Components.Count);
        try
        {
            var changed = false;
            for (var i = 0; i < type.Components.Count; i++)
            {
                if (this.CloseClosureTypes(type.Components[i], call) is not { } part)
                {
                    return null;
                }

                scratch[i] = part;
                changed |= !ReferenceEquals(part, type.Components[i]);
            }

            CallPlan? context = null;
            if (type.Kind == BoundTypeKind.Closure && type.Symbol?.Declaration is FunctionKoto { RequiresInstantiation: true, BoundClosure: { } closure } function)
            {
                var enclosing = function.BoundSymbol!.Scope.Function;
                while (enclosing is { IsAnonymous: true } && !ReferenceEquals(enclosing, call.Target.Declaration))
                {
                    enclosing = enclosing.BoundSymbol?.Scope.Function;
                }

                if (enclosing is not null && ReferenceEquals(enclosing, call.Target.Declaration))
                {
                    var result = this.InstantiateStorageType(closure.Signature.Components[1], call);
                    if (result is null)
                    {
                        return null;
                    }

                    foreach (var candidate in this.closureContexts)
                    {
                        var previous = candidate.Context;
                        if (ReferenceEquals(candidate.Function, function) && ReferenceEquals(previous.Target, call.Target) &&
                            ReferenceEquals(previous.ReturnType, result) && ReferenceEquals(previous.DeclaringType, call.DeclaringType) &&
                            previous.TypeArguments.SequenceEqual(call.TypeArguments) && previous.LengthArguments.SequenceEqual(call.LengthArguments) &&
                            previous.Origins.SequenceEqual(call.Origins) && previous.InputOrigins.SequenceEqual(call.InputOrigins))
                        {
                            context = previous;
                            break;
                        }
                    }

                    if (context is null)
                    {
                        context = new();
                        context.Set(call.Target, result, null, [], call.TypeArguments, declaringType: call.DeclaringType, origins: call.Origins, inputOrigins: call.InputOrigins, lengthArguments: call.LengthArguments);
                        this.closureContexts.Add((function, context));
                    }
                }
            }

            return changed || context is not null
                ? this.InternType(type.Kind, type.Symbol, type.Semantics, scratch.AsSpan(0, type.Components.Count), type.Length, type.Origin, (BoundOrigin[])type.OriginArguments, type.LengthExpression, context, type.LengthArguments, type.ResultMode)
                : type;
        }
        finally
        {
            this.typeScratch.Return(scratch, clearArray: true);
        }
    }
}
