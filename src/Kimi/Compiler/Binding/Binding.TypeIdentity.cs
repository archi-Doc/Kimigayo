// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// SPEC 8.3, 8.4.8.1: an available Type-identity premise `X is U` makes X and U one Type in its scope. Expressions bound
// there substitute U for the Type parameter X, so fitting, member lookup and overload resolution see one Type. This is
// substitution, not an equation solver: only premises whose subject is a Type parameter are applied.
public sealed partial class Binding
{
    private readonly HashSet<BoundType> identityExpansions = new(ReferenceEqualityComparer.Instance);

    /// <summary>Fits <paramref name="actual"/> to <paramref name="expected"/> after substituting the Type-identity premises
    /// of the scope that contains <paramref name="use"/>; flow analysis compares written Types this way (SPEC 8.3).</summary>
    /// <param name="actual">The supplied Type.</param>
    /// <param name="expected">The required Type.</param>
    /// <param name="use">A node in the premise's scope.</param>
    /// <returns>Whether the substituted Types fit.</returns>
    internal bool FitsUnderIdentity(BoundType actual, BoundType expected, Koto use)
    {
        if (!actual.ContainsParameter && !expected.ContainsParameter)
        {
            return false;
        }

        var scope = this.ConstraintScope(use);
        return HasParameterIdentity(scope) && FitsType(this.SubstituteIdentityPremises(actual, scope), this.SubstituteIdentityPremises(expected, scope));
    }

    private static bool HasParameterIdentity(BindingScope scope)
    {
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints is { Invalid: false, HasParameterIdentity: true })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Substitutes the Type-identity premises available in <paramref name="scope"/> into <paramref name="type"/>.</summary>
    /// <param name="type">A bound expression Type.</param>
    /// <param name="scope">The scope whose Constraint environments supply the premises.</param>
    /// <returns><paramref name="type"/> itself when no premise applies; otherwise the substituted interned Type.</returns>
    private BoundType SubstituteIdentityPremises(BoundType type, BindingScope scope)
    {
        if (!type.ContainsParameter || !HasParameterIdentity(scope))
        {
            return type;
        }

        return this.SubstituteIdentity(type, scope);
    }

    private BoundType SubstituteIdentity(BoundType type, BindingScope scope, bool parametersOnly = false)
    {
        if (!type.ContainsParameter)
        {
            return type;
        }

        if (type.Kind == BoundTypeKind.Parameter)
        {
            if (!this.identityExpansions.Add(type))
            {
                return type; // A recursive structural premise must not expand without bound.
            }

            try
            {
                var required = this.IdentityPremise(type, scope, parametersOnly);
                return required.Kind == BoundTypeKind.Parameter ? required : this.SubstituteIdentity(required, scope, parametersOnly);
            }
            finally
            {
                this.identityExpansions.Remove(type);
            }
        }

        var count = type.Components.Count;
        var components = this.RentTypes(count);
        try
        {
            var changed = false;
            for (var i = 0; i < count; i++)
            {
                components[i] = this.SubstituteIdentity(type.Components[i], scope, parametersOnly);
                changed |= !ReferenceEquals(components[i], type.Components[i]);
            }

            if (!changed)
            {
                return type;
            }

            // A substituted struct or enum is another Type: its stored fields and cases follow the substitution, as for the
            // declared Types whose storage is prepared, so a field of it keeps the substituted Type (SPEC 8.3).
            var substituted = this.InternType(type.Kind, type.Symbol, type.Semantics, components.AsSpan(0, count), type.Length, type.Origin, (BoundOrigin[])type.OriginArguments, type.LengthExpression, type.ClosureContext);
            this.PrepareInstantiatedStorage(substituted, 0);
            return substituted;
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
        }
    }

    // Parameter identities form equivalence classes, not directed rewrite chains. Close only over the finite
    // parameter premises in scope, then choose the same representative for every member. No structural unification
    // or inference of missing premises is performed. Scratch storage is reused across queries and rebinding.
    private BoundType IdentityPremise(BoundType parameter, BindingScope scope, bool parametersOnly)
    {
        var capacity = 1;
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints is { Invalid: false, HasParameterIdentity: true } environment)
            {
                capacity += environment.Facts.Count * 2;
            }
        }

        var members = this.RentTypes(capacity);
        var count = 1;
        members[0] = parameter;
        try
        {
            var changed = true;
            while (changed)
            {
                changed = false;
                for (var current = scope; current is not null; current = current.Parent)
                {
                    if (current.Constraints is not { Invalid: false, HasParameterIdentity: true } environment)
                    {
                        continue;
                    }

                    foreach (var fact in environment.Facts)
                    {
                        if (fact is not { Kind: ConstraintKind.TypeIdentity, Subject.Kind: BoundTypeKind.Parameter, RequiredType.Kind: BoundTypeKind.Parameter } ||
                            !this.AvailableConstraintFact(environment, fact))
                        {
                            continue;
                        }

                        var hasSubject = members.AsSpan(0, count).Contains(fact.Subject);
                        var hasRequired = members.AsSpan(0, count).Contains(fact.RequiredType);
                        if (hasSubject != hasRequired)
                        {
                            members[count++] = hasSubject ? fact.RequiredType : fact.Subject;
                            changed = true;
                        }
                    }
                }
            }

            BoundType? representative = null;
            for (var current = scope; current is not null; current = current.Parent)
            {
                if (current.Constraints is not { Invalid: false, HasParameterIdentity: true } environment)
                {
                    continue;
                }

                foreach (var fact in environment.Facts)
                {
                    if (fact is { Kind: ConstraintKind.TypeIdentity, Subject.Kind: BoundTypeKind.Parameter, RequiredType: { } required } &&
                        members.AsSpan(0, count).Contains(fact.Subject) && this.AvailableConstraintFact(environment, fact))
                    {
                        if (required.Kind != BoundTypeKind.Parameter)
                        {
                            if (!parametersOnly)
                            {
                                return required;
                            }

                            continue;
                        }

                        representative ??= required;
                    }
                }
            }

            return representative ?? parameter;
        }
        finally
        {
            this.typeScratch.Return(members, clearArray: true);
        }
    }
}
