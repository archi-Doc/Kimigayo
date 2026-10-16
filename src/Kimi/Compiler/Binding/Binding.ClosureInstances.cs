// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly List<(FunctionKoto Function, CallPlan Context)> closureContexts = new();

    internal BoundType? ClosureSignature(BoundType type)
        => type.Symbol?.Declaration is FunctionKoto { BoundClosure: { } closure }
            ? type.ClosureContext is { } context ? this.InstantiateStorageType(closure.Signature, context) : closure.Signature : null;

    private BoundType? CloseClosureTypes(BoundType type, BindingSymbol target, BoundType? declaring, ReadOnlySpan<BoundType?> types, ReadOnlySpan<BoundLength?> lengths, ReadOnlySpan<BoundOrigin> origins, ReadOnlySpan<BoundOrigin> inputs)
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
                if (this.CloseClosureTypes(type.Components[i], target, declaring, types, lengths, origins, inputs) is not { } part)
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
                while (enclosing is { IsAnonymous: true } && !ReferenceEquals(enclosing, target.Declaration))
                {
                    enclosing = enclosing.BoundSymbol?.Scope.Function;
                }

                if (enclosing is not null && ReferenceEquals(enclosing, target.Declaration))
                {
                    var result = this.InstantiateStorageType(closure.Signature.Components[1], target, declaring, types, lengths, origins, inputs);
                    if (result is null)
                    {
                        return null;
                    }

                    foreach (var candidate in this.closureContexts)
                    {
                        var previous = candidate.Context;
                        if (ReferenceEquals(candidate.Function, function) && ReferenceEquals(previous.Target, target) &&
                            ReferenceEquals(previous.ReturnType, result) && ReferenceEquals(previous.DeclaringType, declaring) &&
                            previous.TypeArguments.SequenceEqual(types) && previous.LengthArguments.SequenceEqual(lengths) &&
                            previous.Origins.SequenceEqual(origins) && previous.InputOrigins.SequenceEqual(inputs))
                        {
                            context = previous;
                            break;
                        }
                    }

                    if (context is null)
                    {
                        context = new();
                        context.Set(target, result, null, [], types, declaringType: declaring, origins: origins, inputOrigins: inputs, lengthArguments: lengths);
                        this.closureContexts.Add((function, context));
                    }
                }
            }

            return changed || context is not null
                ? this.types.Intern(type.Kind, type.Symbol, type.Semantics, scratch.AsSpan(0, type.Components.Count), type.Length, type.Origin, (BoundOrigin[])type.OriginArguments, type.LengthExpression, context, type.LengthArguments)
                : type;
        }
        finally
        {
            this.typeScratch.Return(scratch, clearArray: true);
        }
    }
}
