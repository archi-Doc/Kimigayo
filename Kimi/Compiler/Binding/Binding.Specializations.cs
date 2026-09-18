// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<FunctionKoto, Specialization> specializations = new(ReferenceEqualityComparer.Instance);

    internal bool IsVerifiedSpecialization(FunctionKoto function) => this.specializations.ContainsKey(function);

    internal BoundCall? InstantiateForwardedCall(BoundCall inner, BoundCall outer)
    {
        var types = new BoundType?[inner.TypeArguments.Length];
        var lengths = new BoundLength?[inner.LengthArguments.Length];
        for (var i = 0; i < types.Length; i++)
        {
            if (inner.TypeArguments[i] is { } type && (types[i] = this.InstantiateStorageType(type, outer)) is null)
            {
                return null;
            }
        }

        for (var i = 0; i < lengths.Length; i++)
        {
            if (inner.LengthArguments[i] is { } length && (lengths[i] = this.SubstituteLength(length, outer.Target.Declaration, outer.LengthArguments)) is null)
            {
                return null;
            }
        }

        var result = this.InstantiateStorageType(inner.ReturnType, outer);
        var declaring = inner.DeclaringType is { } owner ? this.InstantiateStorageType(owner, outer) : null;
        if (result is null || (inner.DeclaringType is not null && declaring is null) || inner.DefaultArguments.Length != 0)
        {
            return null;
        }

        var call = new BoundCall();
        call.Set(inner.Target, result, inner.Receiver, inner.ArgumentToParameter, types, declaringType: declaring, origins: Origins(inner.Origins), inputOrigins: Origins(inner.InputOrigins), operations: inner.ArgumentOperations, receiverOperation: inner.ReceiverOperation, lengthArguments: lengths);
        return call;

        BoundOrigin[] Origins(ReadOnlySpan<BoundOrigin> origins)
        {
            var values = origins.ToArray();
            for (var i = 0; i < values.Length; i++)
            {
                if (values[i] is not { } origin)
                {
                    continue;
                }

                if (outer.DeclaringType is { Symbol: { } symbol } container)
                {
                    origin = this.SubstituteStoredOrigin(origin, symbol.Declaration, container.Kind == BoundTypeKind.Slice && container.Origin is { } source ? [source] : (BoundOrigin[])container.OriginArguments);
                }

                values[i] = this.SubstituteStoredOrigin(origin, outer.Target.Declaration, outer.Origins, outer.InputOrigins);
            }

            return values;
        }
    }

    internal FunctionKoto? SelectSpecialization(BoundCall call)
    {
        foreach (var pair in this.specializations)
        {
            if (ReferenceEquals(pair.Value.Original, call.Target) && SameSpecializationArguments(pair.Value.Arguments, call.TypeArguments))
            {
                return pair.Key;
            }
        }

        return null;
    }

    private static bool SameSpecializationArguments(ReadOnlySpan<BoundType?> left, ReadOnlySpan<BoundType?> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] is not { } a || right[i] is not { } b || !SignatureEquals(a, b, null!, null!))
            {
                return false;
            }
        }

        return true;
    }

    private void PrepareSpecializations()
    {
        foreach (var node in this.nodes)
        {
            if (node is not FunctionKoto { IsSpecialization: true, BoundSymbol: { } symbol } function)
            {
                continue;
            }

            // Receiver/length/constraint specializations require their own inherited contract
            // certificates. Do not accept their syntax by merely erasing the generic header.
            if (symbol.ReceiverIndex >= 0 || function.Modifier != ModifierKind.NoModifier || function.AttributeChain is not null ||
                function.Origins.Count != 0 || function.TypeConstraints.Count != 0 || function.GenericArguments.Count == 0 ||
                function.Parameters.Any(x => x.IsOptional || x.DefaultValue is not null || x.AttributeChain is not null))
            {
                Fail(function, BindingFailure.Unsupported, true);
                continue;
            }

            var arguments = new BoundType?[function.GenericArguments.Count];
            var closed = true;
            for (var i = 0; i < arguments.Length; i++)
            {
                arguments[i] = this.BindType(function.GenericArguments[i], this.scopes[function]);
                closed &= arguments[i] is { } type && Closed(type);
            }

            if (!closed)
            {
                Fail(function, BindingFailure.InvalidTypeFormation);
                continue;
            }

            BindingSymbol? original = null;
            var matches = 0;
            for (var candidate = symbol.Scope.Values.GetValueOrDefault(function.Name); candidate is not null; candidate = candidate.Next)
            {
                if (candidate.Declaration is not FunctionKoto { IsSpecialization: false } ordinary ||
                    ordinary.GenericArguments.Count != arguments.Length || ordinary.Parameters.Count != function.Parameters.Count ||
                    ordinary.GenericArguments.Any(x => x is not GenericParameterKoto) || candidate.ReceiverIndex >= 0)
                {
                    continue;
                }

                var equal = true;
                for (var p = 0; p < function.Parameters.Count; p++)
                {
                    equal &= ordinary.Parameters[p].Type.BoundType is { } input &&
                        this.SubstituteType(input, ordinary, arguments) is { } substituted && function.Parameters[p].Type.BoundType is { } actual &&
                        SignatureEquals(substituted, actual, ordinary, function);
                }

                if (equal)
                {
                    original = candidate;
                    matches++;
                }
            }

            if (matches != 1)
            {
                Fail(function, matches == 0 ? BindingFailure.MissingImplementation : BindingFailure.Ambiguous);
                continue;
            }

            var definition = (FunctionKoto)original!.Declaration;
            if (definition.Origins.Count != 0 || definition.TypeConstraints.Count != 0 || definition.AttributeChain is not null ||
                definition.Parameters.Any(x => x.IsOptional || x.DefaultValue is not null || x.AttributeChain is not null))
            {
                Fail(function, BindingFailure.Unsupported, true);
                continue;
            }

            var valid = original.Type is { } result && this.SubstituteType(result, definition, arguments) is { } expected &&
                symbol.Type is { } actualResult && SameContract(expected, actualResult, definition, function);
            for (var p = 0; p < function.Parameters.Count; p++)
            {
                valid &= function.Parameters[p].ExternalName == definition.Parameters[p].ExternalName &&
                    SameContract(this.SubstituteType(definition.Parameters[p].Type.BoundType!, definition, arguments)!, function.Parameters[p].Type.BoundType!, definition, function);
            }

            if (!valid)
            {
                Fail(function, BindingFailure.IncompatibleImplementation);
                continue;
            }

            foreach (var previous in this.specializations)
            {
                if (ReferenceEquals(previous.Value.Original, original) && SameSpecializationArguments(previous.Value.Arguments, arguments))
                {
                    Fail(previous.Key, BindingFailure.Duplicate);
                    Fail(function, BindingFailure.Duplicate);
                    valid = false;
                }
            }

            if (valid)
            {
                this.specializations.Add(function, new(original, arguments));
            }
        }

        static bool Closed(BoundType type)
            => type.Kind is not (BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication or BoundTypeKind.AssociatedProjection) &&
                type.LengthExpression is null && type.Components.All(Closed);

        static bool SameContract(BoundType a, BoundType b, FunctionKoto left, FunctionKoto right)
        {
            if (!SignatureEquals(a, b, left, right) || !SameOrigin(a.Origin, b.Origin, left, right) || a.OriginArguments.Count != b.OriginArguments.Count)
            {
                return false;
            }

            for (var i = 0; i < a.OriginArguments.Count; i++)
            {
                if (!SameOrigin(a.OriginArguments[i], b.OriginArguments[i], left, right))
                {
                    return false;
                }
            }

            for (var i = 0; i < a.Components.Count; i++)
            {
                if (!SameContract(a.Components[i], b.Components[i], left, right))
                {
                    return false;
                }
            }

            return true;
        }

        static bool SameOrigin(BoundOrigin? a, BoundOrigin? b, FunctionKoto left, FunctionKoto right)
            => ReferenceEquals(a, b) || (a is { Kind: OriginKind.Input } && b is { Kind: OriginKind.Input } &&
                ReferenceEquals(a.Binder, left) && ReferenceEquals(b.Binder, right) && a.Slot == b.Slot);
    }

    private sealed record Specialization(BindingSymbol Original, BoundType?[] Arguments);
}
