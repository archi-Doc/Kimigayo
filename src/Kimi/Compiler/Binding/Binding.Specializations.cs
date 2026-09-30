// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<FunctionKoto, Specialization> specializations = new(ReferenceEqualityComparer.Instance);

    // The intermediate call of a forwarded requirement call, before its witness is resolved into the destination.
    private readonly BoundCall forwardedRequirement = new();
    // SPEC 8.8.3: selection is keyed by the original; each original lists its verified specializations.
    private readonly Dictionary<BindingSymbol, List<FunctionKoto>> specializationsByOriginal = new(ReferenceEqualityComparer.Instance);
    // Retained across binds: a warm rebind reuses each specialization's slot arrays and the sibling lists.
    private readonly Dictionary<FunctionKoto, Specialization> specializationStorage = new(ReferenceEqualityComparer.Instance);
    private readonly Stack<List<FunctionKoto>> siblingPool = new();

    internal bool IsVerifiedSpecialization(FunctionKoto function) => this.specializations.ContainsKey(function);

    internal FunctionKoto? GetSpecializationOriginal(FunctionKoto function)
        => this.specializations.TryGetValue(function, out var specialization) ? (FunctionKoto)specialization.Original.Declaration : null;

    // SPEC 8.4.8.2: generic dispatch reaches a verified implementation without a direct call, so a use of `type` as a Type
    // argument needs every implementation that its verified conformances map a requirement to.
    internal void CollectWitnesses(BindingSymbol type, List<FunctionKoto> destination)
    {
        if (!this.conformancesByType.TryGetValue(type, out var identities))
        {
            return;
        }

        for (var i = 0; i < identities.Count; i++)
        {
            var paths = identities[i].PathStorage;
            for (var p = 0; p < paths.Count; p++)
            {
                if (!paths[p].IsVerified)
                {
                    continue;
                }

                var witnesses = paths[p].WitnessStorage;
                for (var w = 0; w < witnesses.Count; w++)
                {
                    if (witnesses[w].Implementation.Declaration is FunctionKoto function && !destination.Contains(function))
                    {
                        destination.Add(function);
                    }
                }
            }
        }
    }

    /// <summary>Instantiates a call forwarded by a generic body under the closed context of its caller.</summary>
    /// <param name="inner">The call in the generic body.</param>
    /// <param name="outer">The closed call context.</param>
    /// <param name="destination">A call to overwrite, such as the previous emission's result for the same position, or null.</param>
    /// <returns>The concrete call, or null when the context has no complete instantiation.</returns>
    internal BoundCall? InstantiateForwardedCall(BoundCall inner, BoundCall outer, BoundCall? destination = null)
    {
        var types = this.typeScratch.Rent(inner.TypeArguments.Length);
        var lengths = this.lengthScratch.Rent(inner.LengthArguments.Length);
        var origins = this.originScratch.Rent(inner.Origins.Length);
        var inputs = this.originScratch.Rent(inner.InputOrigins.Length);
        var defaults = this.defaultArgumentScratch.Rent(inner.DefaultArguments.Length);
        try
        {
            for (var i = 0; i < inner.TypeArguments.Length; i++)
            {
                if (inner.TypeArguments[i] is { } type && (types[i] = this.InstantiateStorageType(type, outer)) is null)
                {
                    return null;
                }
            }

            for (var i = 0; i < inner.LengthArguments.Length; i++)
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

            for (var i = 0; i < inner.DefaultArguments.Length; i++)
            {
                var omitted = inner.DefaultArguments[i];
                if (this.InstantiateStorageType(omitted.ParameterType, outer) is not { } parameterType)
                {
                    return null;
                }

                defaults[i] = omitted with { ParameterType = parameterType };
            }

            Origins(inner.Origins, origins);
            Origins(inner.InputOrigins, inputs);

            // A requirement call is resolved from an intermediate call into the destination, so the two never share storage.
            var requirement = inner.Target.Declaration is FunctionKoto { IsRequirement: true };
            var call = requirement ? this.forwardedRequirement : destination ?? new BoundCall();
            call.Set(inner.Target, result, inner.Receiver, inner.ArgumentToParameter, types.AsSpan(0, inner.TypeArguments.Length), conformingType: inner.ConformingType is { } self ? this.InstantiateStorageType(self, outer) : null, declaringType: declaring, origins: origins.AsSpan(0, inner.Origins.Length), inputOrigins: inputs.AsSpan(0, inner.InputOrigins.Length), operations: inner.ArgumentOperations, receiverOperation: inner.ReceiverOperation, lengthArguments: lengths.AsSpan(0, inner.LengthArguments.Length), defaults: defaults.AsSpan(0, inner.DefaultArguments.Length));
            call.TupleOperator = inner.TupleOperator;
            call.RequirementContract = inner.RequirementContract;
            return requirement ? this.InstantiateRequirementCall(call, outer, destination) : call;
        }
        finally
        {
            this.defaultArgumentScratch.Return(defaults, clearArray: true);
            this.originScratch.Return(inputs, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
            this.lengthScratch.Return(lengths, clearArray: true);
            this.typeScratch.Return(types, clearArray: true);
        }

        void Origins(ReadOnlySpan<BoundOrigin> source, Span<BoundOrigin> values)
        {
            for (var i = 0; i < source.Length; i++)
            {
                if (source[i] is not { } origin)
                {
                    values[i] = null!;
                    continue;
                }

                if (outer.DeclaringType is { Symbol: { } symbol } container)
                {
                    // A Slice stores its source as its own Origin; substitute through a span over it, not a new array per use.
                    origin = container.Kind == BoundTypeKind.Slice && container.Origin is { } sliceSource
                        ? this.SubstituteStoredOrigin(origin, symbol.Declaration, new ReadOnlySpan<BoundOrigin>(ref sliceSource))
                        : this.SubstituteStoredOrigin(origin, symbol.Declaration, (BoundOrigin[])container.OriginArguments);
                }

                values[i] = this.SubstituteStoredOrigin(origin, outer.Target.Declaration, outer.Origins, outer.InputOrigins);
            }
        }
    }

    /// <summary>Lists the verified specializations of a generic original (SPEC 8.8).</summary>
    /// <param name="original">The original generic function.</param>
    /// <returns>The specializations, or null when it has none.</returns>
    internal List<FunctionKoto>? Specializations(BindingSymbol original) => this.specializationsByOriginal.GetValueOrDefault(original);

    internal FunctionKoto? SelectSpecialization(BoundCall call)
    {
        if (!this.specializationsByOriginal.TryGetValue(call.Target, out var candidates))
        {
            return null;
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            var specialization = this.specializations[candidates[i]];
            if (SameSpecializationArguments(specialization.Arguments, call.TypeArguments) && SameSpecializationLengths(specialization.Lengths, call.LengthArguments))
            {
                return candidates[i];
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

    private void ResetSpecializations()
    {
        this.specializations.Clear();
        foreach (var siblings in this.specializationsByOriginal.Values)
        {
            siblings.Clear();
            this.siblingPool.Push(siblings);
        }

        this.specializationsByOriginal.Clear();
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
            if (function.AttributeChain is not null || function.GenericArguments.Count == 0 || HasParameterAttributes(function))
            {
                this.Fail(function, BindingFailure.Unsupported, true);
                continue;
            }

            // SPEC 8.8.2: a specialization header inherits the original's access, boundary, defaults and
            // Constraints; it redeclares none of them.
            if (function.Modifier != ModifierKind.NoModifier || function.NameBoundaryIndex >= 0 || function.TypeConstraints.Count != 0 ||
                HasParameterDefaults(function))
            {
                this.Fail(function, BindingFailure.IncompatibleImplementation);
                continue;
            }

            if (!this.specializationStorage.TryGetValue(function, out var entry) || entry.Arguments.Length != function.GenericArguments.Count)
            {
                this.specializationStorage[function] = entry = new(new BoundType?[function.GenericArguments.Count], new BoundLength?[function.GenericArguments.Count]);
            }

            var arguments = entry.Arguments;
            var lengths = entry.Lengths;
            Array.Clear(arguments);
            Array.Clear(lengths);
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
                this.Fail(function, BindingFailure.InvalidTypeFormation);
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
                this.Fail(function, matches > 1 ? BindingFailure.Ambiguous : candidates == 0 ? BindingFailure.MissingSpecializationTarget : BindingFailure.SpecializationInputMismatch);
                continue;
            }

            var definition = (FunctionKoto)original!.Declaration;
            if (definition.AttributeChain is not null || HasParameterAttributes(definition))
            {
                this.Fail(function, BindingFailure.Unsupported, true);
                continue;
            }

            // SPEC 8.8.2: the original's generic Constraints are inherited; the closed arguments must satisfy them.
            var constraints = this.CheckConstraints(definition.TypeConstraints, definition, arguments, scope, null, null, lengths);
            if (constraints != ConstraintProof.Proven)
            {
                this.Fail(function, constraints == ConstraintProof.Error ? BindingFailure.InvalidConstraint : constraints == ConstraintProof.Refuted ? BindingFailure.UnsatisfiedConstraint : BindingFailure.UnprovenConstraint, constraints == ConstraintProof.Unknown);
                continue;
            }

            var valid = this.CompleteSpecializationOrigins(function, definition, arguments, lengths);
            for (var p = 0; p < function.Parameters.Count; p++)
            {
                valid &= function.Parameters[p].ExternalName == definition.Parameters[p].ExternalName;
            }

            if (!valid)
            {
                this.Fail(function, BindingFailure.IncompatibleImplementation);
                continue;
            }

            if (!this.specializationsByOriginal.TryGetValue(original!, out var siblings))
            {
                siblings = this.siblingPool.Count != 0 ? this.siblingPool.Pop() : new();
                this.specializationsByOriginal.Add(original!, siblings);
            }

            for (var i = 0; i < siblings.Count; i++)
            {
                var previous = this.specializations[siblings[i]];
                if (SameSpecializationArguments(previous.Arguments, arguments) && SameSpecializationLengths(previous.Lengths, lengths))
                {
                    this.Fail(siblings[i], BindingFailure.Duplicate);
                    this.Fail(function, BindingFailure.Duplicate);
                    valid = false;
                }
            }

            if (valid)
            {
                entry.Original = original!;
                this.specializations.Add(function, entry);
                siblings.Add(function);
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
        {
            if (type.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication or BoundTypeKind.AssociatedProjection ||
                type.LengthExpression is not null)
            {
                return false;
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                if (!Closed(type.Components[i]))
                {
                    return false;
                }
            }

            return true;
        }

        static bool HasParameterAttributes(FunctionKoto function)
        {
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                if (function.Parameters[i].AttributeChain is not null)
                {
                    return true;
                }
            }

            return false;
        }

        static bool HasParameterDefaults(FunctionKoto function)
        {
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                if (function.Parameters[i].DefaultValue is not null)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private sealed class Specialization(BoundType?[] arguments, BoundLength?[] lengths)
    {
        internal BindingSymbol Original { get; set; } = null!;

        internal BoundType?[] Arguments { get; } = arguments;

        internal BoundLength?[] Lengths { get; } = lengths;
    }
}
