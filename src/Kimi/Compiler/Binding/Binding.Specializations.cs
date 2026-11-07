// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<FunctionKoto, Specialization> specializations = new(ReferenceEqualityComparer.Instance);

    // SPEC 8.13.3: selection is keyed by the original; each original lists its verified specializations.
    private readonly Dictionary<BindingSymbol, List<FunctionKoto>> specializationsByOriginal = new(ReferenceEqualityComparer.Instance);
    // Retained across binds: a warm rebind reuses each specialization's slot arrays and the sibling lists.
    private readonly Dictionary<FunctionKoto, Specialization> specializationStorage = new(ReferenceEqualityComparer.Instance);
    private readonly Stack<List<FunctionKoto>> siblingPool = new();

    internal FunctionKoto? GetSpecializationOriginal(FunctionKoto function)
        => this.specializations.TryGetValue(function, out var specialization) ? (FunctionKoto)specialization.Original.Declaration : null;

    /// <summary>Gets an Origin of a generic body in the instance a call's substitution makes (TyCtxt.Compose too): through the stored
    /// Origins of the Type declaring the callee, then the call's Origins and inputs and the pair Origins its Type arguments bind.</summary>
    /// <param name="origin">The body's Origin.</param>
    /// <param name="target">The callee.</param>
    /// <param name="declaring">The instantiated Type declaring the callee.</param>
    /// <param name="origins">The substitution of the callee's declared Origins.</param>
    /// <param name="inputs">The input Origins in parameter-slot order.</param>
    /// <param name="types">The Type arguments by slot.</param>
    /// <returns>The Origin in the instance.</returns>
    internal BoundOrigin ForwardOrigin(BoundOrigin origin, BindingSymbol target, BoundType? declaring, ReadOnlySpan<BoundOrigin> origins, ReadOnlySpan<BoundOrigin> inputs, ReadOnlySpan<BoundType?> types)
    {
        if (declaring is { Symbol: { } symbol } container)
        {
            // A Slice stores its source as its own Origin; substitute through a span over it, not a new array per use.
            origin = container.Kind == BoundTypeKind.Slice && container.Origin is { } sliceSource
                ? this.SubstituteStoredOrigin(origin, symbol.Declaration, new ReadOnlySpan<BoundOrigin>(ref sliceSource))
                : this.SubstituteStoredOrigin(origin, symbol.Declaration, (BoundOrigin[])container.OriginArguments, types: (BoundType[])container.Components);
        }

        // A body's Origin may name a pair's `o` (SPEC 13.5.5), which the instance binds through its Type arguments.
        return this.SubstituteStoredOrigin(origin, target.Declaration, origins, inputs, types);
    }

    /// <summary>Lists the verified specializations of a generic original (SPEC 8.13).</summary>
    /// <param name="original">The original generic function.</param>
    /// <returns>The specializations, or null when it has none.</returns>
    internal List<FunctionKoto>? Specializations(BindingSymbol original) => this.specializationsByOriginal.GetValueOrDefault(original);

    // SPEC 8.13, 8.13.3: a body a call of a function under its substitution may run, by index, null past the last: the specialization a
    // closed key selects, else the function itself and each specialization an open key may still select (no substitution: every one).
    internal BindingSymbol? Implementation(BindingSymbol function, Subst? subst, int index)
    {
        var types = subst is null ? default : subst.Types;
        var lengths = subst is null ? default : subst.Lengths;
        if (this.SelectSpecialization(function, types, lengths) is { } selected)
        {
            return index == 0 ? selected.BoundSymbol : null;
        }

        var candidates = this.Specializations(function);
        for (var i = 0; index > 0 && candidates is not null && i < candidates.Count; i++)
        {
            if (MaySelect(this.specializations[candidates[i]], types, lengths) && --index == 0)
            {
                return candidates[i].BoundSymbol;
            }
        }

        return index == 0 ? function : null;
    }

    // SPEC 8.13.3: the selection key holds every slot; a length slot is its evaluated value (LengthKey(N)).
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

    // SPEC 8.13.3: whether a call whose key is still open may select a specialization: each argument it fixes may match the
    // specialization's, and each constant length equals it.
    private static bool MaySelect(Specialization candidate, ReadOnlySpan<BoundType?> types, ReadOnlySpan<BoundLength?> lengths)
    {
        for (var i = 0; i < types.Length; i++)
        {
            if (types[i] is { } argument && candidate.Arguments[i] is { } closed && !MayMatch(argument, closed))
            {
                return false;
            }
        }

        for (var i = 0; i < lengths.Length; i++)
        {
            if (lengths[i] is { IsConstant: true } length && !SameLengthSignature(length, candidate.Lengths[i], null!, null!))
            {
                return false;
            }
        }

        return true;
    }

    // Whether an instance of a pattern may be a closed Type: an abstract part matches anything, any other part the same shape.
    private static bool MayMatch(BoundType pattern, BoundType closed)
    {
        if (ReferenceEquals(pattern, closed) || pattern.IsAbstract)
        {
            return true;
        }

        if (pattern.Kind == BoundTypeKind.Primitive || pattern.Kind != closed.Kind || pattern.Semantics != closed.Semantics)
        {
            return false;
        }

        if (!ReferenceEquals(pattern.Symbol, closed.Symbol) || pattern.Components.Count != closed.Components.Count ||
            (pattern.LengthExpression is null && pattern.Length != closed.Length))
        {
            return false;
        }

        for (var i = 0; i < pattern.Components.Count; i++)
        {
            if (!MayMatch(pattern.Components[i], closed.Components[i]))
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

    private FunctionKoto? SelectSpecialization(BindingSymbol original, ReadOnlySpan<BoundType?> types, ReadOnlySpan<BoundLength?> lengths)
    {
        if (!this.specializationsByOriginal.TryGetValue(original, out var candidates))
        {
            return null;
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            var specialization = this.specializations[candidates[i]];
            if (SameSpecializationArguments(specialization.Arguments, types) && SameSpecializationLengths(specialization.Lengths, lengths))
            {
                return candidates[i];
            }
        }

        return null;
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
            // are inherited by CompleteImplementationOrigins (SPEC 8.13.2); a receiver is an ordinary
            // restated parameter matched at the original's position (SPEC 8.13.1).
            if (function.AttributeChain is not null || function.GenericArguments.Count == 0 || HasParameterAttributes(function))
            {
                this.Fail(function, BindingFailure.Unsupported, true);
                continue;
            }

            // SPEC 8.13.2: a specialization header inherits the original's access, boundary, defaults and
            // Constraints; it redeclares none of them. The parser judges its modifiers, publishing none (ParseFuncDeclaration),
            // and its `!` boundary (TryParseParameterList).
            if (function.TypeConstraints.Count != 0 || HasParameterDefaults(function))
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
                // SPEC 8.13: a length slot takes an evaluated constant (LengthKey(N)); a Type slot takes one closed Type.
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
            var candidates = 0; // SPEC 8.13.1: same Name, kind and generic slots; input structure is matched below.
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
                // SPEC 8.13.1: no target, an input-structure mismatch and multiple targets are distinct diagnostics.
                this.Fail(function, matches > 1 ? BindingFailure.Ambiguous : candidates == 0 ? BindingFailure.MissingSpecializationTarget : BindingFailure.SpecializationInputMismatch);
                continue;
            }

            var definition = (FunctionKoto)original!.Declaration;
            if (definition.AttributeChain is not null || HasParameterAttributes(definition))
            {
                this.Fail(function, BindingFailure.Unsupported, true);
                continue;
            }

            // SPEC 8.13.2: the original's generic Constraints are inherited; the closed arguments must satisfy them.
            var constraints = this.CheckConstraints(definition.TypeConstraints, definition, arguments, scope, null, null, lengths);
            if (constraints != ConstraintProof.Proven)
            {
                _ = constraints == ConstraintProof.Unknown ? this.FailUnprovenConstraint(function, this.TakeUnknownFact(), unresolved: true)
                    : this.Fail(function, constraints == ConstraintProof.Error ? BindingFailure.InvalidConstraint : BindingFailure.UnsatisfiedConstraint);
                continue;
            }

            var valid = this.CompleteImplementationOrigins(function, definition, arguments, lengths, out var originMismatch);
            for (var p = 0; p < function.Parameters.Count; p++)
            {
                valid &= function.Parameters[p].ExternalName == definition.Parameters[p].ExternalName;
            }

            if (!valid)
            {
                if (originMismatch is { } contract)
                {
                    this.FailExplained(ref this.originContracts, function, BindingFailure.OriginContract, contract);
                }
                else
                {
                    this.Fail(function, BindingFailure.IncompatibleImplementation);
                }

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
                    this.FailDuplicate(siblings[i], function);
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

        // SPEC 8.13.1: every slot is supplied with its original kind, in declaration order.
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
    }

    private sealed class Specialization(BoundType?[] arguments, BoundLength?[] lengths)
    {
        internal BindingSymbol Original { get; set; } = null!;

        internal BoundType?[] Arguments { get; } = arguments;

        internal BoundLength?[] Lengths { get; } = lengths;
    }
}
