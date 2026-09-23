// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<FunctionKoto, Specialization> specializations = new(ReferenceEqualityComparer.Instance);

    internal bool IsVerifiedSpecialization(FunctionKoto function) => this.specializations.ContainsKey(function);

    internal FunctionKoto? GetSpecializationOriginal(FunctionKoto function)
        => this.specializations.TryGetValue(function, out var specialization) ? (FunctionKoto)specialization.Original.Declaration : null;

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
        if (result is null || (inner.DeclaringType is not null && declaring is null))
        {
            return null;
        }

        var defaults = this.defaultArgumentScratch.Rent(inner.DefaultArguments.Length);
        try
        {
            for (var i = 0; i < inner.DefaultArguments.Length; i++)
            {
                var omitted = inner.DefaultArguments[i];
                if (this.InstantiateStorageType(omitted.ParameterType, outer) is not { } parameterType)
                {
                    return null;
                }

                defaults[i] = omitted with { ParameterType = parameterType };
            }

            var call = new BoundCall();
            call.Set(inner.Target, result, inner.Receiver, inner.ArgumentToParameter, types, conformingType: inner.ConformingType is { } self ? this.InstantiateStorageType(self, outer) : null, declaringType: declaring, origins: Origins(inner.Origins), inputOrigins: Origins(inner.InputOrigins), operations: inner.ArgumentOperations, receiverOperation: inner.ReceiverOperation, lengthArguments: lengths, defaults: defaults.AsSpan(0, inner.DefaultArguments.Length));
            if (inner.Target.Declaration is FunctionKoto { IsRequirement: true })
            {
                return this.InstantiateRequirementCall(call, outer);
            }

            return call;
        }
        finally
        {
            this.defaultArgumentScratch.Return(defaults, clearArray: true);
        }

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
            if (ReferenceEquals(pair.Value.Original, call.Target) && SameSpecializationArguments(pair.Value.Arguments, call.TypeArguments) &&
                SameSpecializationLengths(pair.Value.Lengths, call.LengthArguments))
            {
                return pair.Key;
            }
        }

        return null;
    }

    // SPEC 8.8.3: the selection key holds every slot; a length slot is its evaluated value (LengthKey(N)).
    private static bool SameSpecializationLengths(ReadOnlySpan<BoundLength?> left, ReadOnlySpan<BoundLength?> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (!SameLengthSignature(left[i], right[i], null!, null!))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameSpecializationArguments(ReadOnlySpan<BoundType?> left, ReadOnlySpan<BoundType?> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] is null && right[i] is null)
            {
                continue; // A length slot; compared by SameSpecializationLengths.
            }

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

            // Constraint and attribute specializations require their own inherited contract certificates.
            // Do not accept their syntax by merely erasing the generic header. Written binder names
            // are inherited by CompleteSpecializationOrigins (SPEC 8.8.2); a receiver is an ordinary
            // restated parameter matched at the original's position (SPEC 8.8.1).
            if (function.AttributeChain is not null || function.GenericArguments.Count == 0 || function.Parameters.Any(x => x.AttributeChain is not null))
            {
                Fail(function, BindingFailure.Unsupported, true);
                continue;
            }

            // SPEC 8.8.2: a specialization header inherits the original's access, boundary, defaults and
            // Constraints; it redeclares none of them.
            if (function.Modifier != ModifierKind.NoModifier || function.NameBoundaryIndex >= 0 || function.TypeConstraints.Count != 0 ||
                function.Parameters.Any(x => x.DefaultValue is not null))
            {
                Fail(function, BindingFailure.IncompatibleImplementation);
                continue;
            }

            var arguments = new BoundType?[function.GenericArguments.Count];
            var lengths = new BoundLength?[function.GenericArguments.Count];
            var closed = true;
            var scope = this.scopes[function];
            for (var i = 0; i < arguments.Length; i++)
            {
                // SPEC 8.8: a length slot takes an evaluated constant (LengthKey(N)); a Type slot takes one closed Type.
                if (this.IsLengthArgument(function.GenericArguments[i], scope))
                {
                    lengths[i] = this.BindLength(function.GenericArguments[i], scope);
                    closed &= lengths[i] is { IsConstant: true };
                }
                else
                {
                    arguments[i] = this.BindType(function.GenericArguments[i], scope);
                    closed &= arguments[i] is { } type && Closed(type);
                }
            }

            if (!closed)
            {
                Fail(function, BindingFailure.InvalidTypeFormation);
                continue;
            }

            BindingSymbol? original = null;
            var matches = 0;
            var candidates = 0; // SPEC 8.8.1: same Name, kind and generic slots; input structure is matched below.
            for (var candidate = symbol.Scope.Values.GetValueOrDefault(function.Name); candidate is not null; candidate = candidate.Next)
            {
                if (candidate.Declaration is not FunctionKoto { IsSpecialization: false } ordinary ||
                    ordinary.GenericArguments.Count != arguments.Length || !SameSlotKinds(ordinary, lengths) || candidate.ReceiverIndex != symbol.ReceiverIndex)
                {
                    continue;
                }

                candidates++;
                if (ordinary.Parameters.Count != function.Parameters.Count)
                {
                    continue; // An input-structure mismatch, not a missing target.
                }

                var equal = true;
                for (var p = 0; p < function.Parameters.Count; p++)
                {
                    equal &= ordinary.Parameters[p].Type.BoundType is { } input &&
                        this.SubstituteType(input, ordinary, arguments, lengths) is { } substituted && function.Parameters[p].Type.BoundType is { } actual &&
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
                // SPEC 8.8.1: no target, an input-structure mismatch and multiple targets are distinct diagnostics.
                Fail(function, matches > 1 ? BindingFailure.Ambiguous : candidates == 0 ? BindingFailure.MissingSpecializationTarget : BindingFailure.SpecializationInputMismatch);
                continue;
            }

            var definition = (FunctionKoto)original!.Declaration;
            if (definition.AttributeChain is not null ||
                definition.Parameters.Any(x => x.AttributeChain is not null))
            {
                Fail(function, BindingFailure.Unsupported, true);
                continue;
            }

            // SPEC 8.8.2: the original's generic Constraints are inherited; the closed arguments must satisfy them.
            var constraints = this.CheckConstraints(definition.TypeConstraints, definition, arguments, scope, null, null, lengths);
            if (constraints != ConstraintProof.Proven)
            {
                Fail(function, constraints == ConstraintProof.Error ? BindingFailure.InvalidConstraint : constraints == ConstraintProof.Refuted ? BindingFailure.UnsatisfiedConstraint : BindingFailure.UnprovenConstraint, constraints == ConstraintProof.Unknown);
                continue;
            }

            var valid = this.CompleteSpecializationOrigins(function, definition, arguments, lengths);
            for (var p = 0; p < function.Parameters.Count; p++)
            {
                valid &= function.Parameters[p].ExternalName == definition.Parameters[p].ExternalName;
            }

            if (!valid)
            {
                Fail(function, BindingFailure.IncompatibleImplementation);
                continue;
            }

            foreach (var previous in this.specializations)
            {
                if (ReferenceEquals(previous.Value.Original, original) && SameSpecializationArguments(previous.Value.Arguments, arguments) &&
                    SameSpecializationLengths(previous.Value.Lengths, lengths))
                {
                    Fail(previous.Key, BindingFailure.Duplicate);
                    Fail(function, BindingFailure.Duplicate);
                    valid = false;
                }
            }

            if (valid)
            {
                this.specializations.Add(function, new(original, arguments, lengths));
            }
        }

        // SPEC 8.8.1: every slot is supplied with its original kind, in declaration order.
        static bool SameSlotKinds(FunctionKoto ordinary, BoundLength?[] lengths)
        {
            for (var i = 0; i < lengths.Length; i++)
            {
                if (lengths[i] is not null ? ordinary.GenericArguments[i] is not LengthParameterKoto : ordinary.GenericArguments[i] is not GenericParameterKoto)
                {
                    return false;
                }
            }

            return true;
        }

        static bool Closed(BoundType type)
            => type.Kind is not (BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication or BoundTypeKind.AssociatedProjection) &&
                type.LengthExpression is null && type.Components.All(Closed);
    }

    private sealed record Specialization(BindingSymbol Original, BoundType?[] Arguments, BoundLength?[] Lengths);
}
