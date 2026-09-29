// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// SPEC 8.3, 8.4.8.1: an available Type-identity premise `X is U` makes X and U one Type in its scope. Expressions bound
// there substitute U for the Type parameter X, so fitting, member lookup and overload resolution see one Type. This is
// substitution, not an equation solver: only premises whose subject is a Type parameter are applied.
public sealed partial class Binding
{
    // A chain of premises (A is B, B is C) is followed at most this far; a longer chain or a cycle stops substituting.
    private const int IdentityChainLimit = 16;

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

        return this.SubstituteIdentity(type, scope, 0);
    }

    private BoundType SubstituteIdentity(BoundType type, BindingScope scope, int depth)
    {
        if (!type.ContainsParameter || depth > IdentityChainLimit)
        {
            return type;
        }

        if (type.Kind == BoundTypeKind.Parameter)
        {
            return this.IdentityPremise(type, scope) is { } required ? this.SubstituteIdentity(required, scope, depth + 1) : type;
        }

        var count = type.Components.Count;
        var components = this.RentTypes(count);
        try
        {
            var changed = false;
            for (var i = 0; i < count; i++)
            {
                components[i] = this.SubstituteIdentity(type.Components[i], scope, depth);
                changed |= !ReferenceEquals(components[i], type.Components[i]);
            }

            return changed
                ? this.InternType(type.Kind, type.Symbol, type.Semantics, components.AsSpan(0, count), type.Length, type.Origin, (BoundOrigin[])type.OriginArguments, type.LengthExpression)
                : type;
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
        }
    }

    // The Type an available premise identifies with the Type parameter `parameter`, or null.
    private BoundType? IdentityPremise(BoundType parameter, BindingScope scope)
    {
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints is not { Invalid: false, HasParameterIdentity: true } environment)
            {
                continue;
            }

            foreach (var fact in environment.Facts)
            {
                if (fact.Kind == ConstraintKind.TypeIdentity && ReferenceEquals(fact.Subject, parameter) && fact.RequiredType is { } required &&
                    !ReferenceEquals(required, parameter) && this.AvailableConstraintFact(environment, fact))
                {
                    return required;
                }
            }
        }

        return null;
    }
}
