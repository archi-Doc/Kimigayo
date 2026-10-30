// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Owns a Type's Contract environment rewrite (ContractType): a Contract's Self replaced by the conforming Type, a bound Contract
// reference's arguments substituted, and completed associated identities normalized in a scope, without member selection. Reads the
// conformance tables and the scope's facts; publishes nothing.
public sealed partial class Binding
{
    /// <summary>Substitutes Contract Self and normalizes completed associated identities without performing member selection.</summary>
    /// <remarks>A Type with neither an abstract part nor an Origin is its own image: the environment substitutes slots and Origins, a
    /// Contract's Self is a Parameter and normalization rewrites only abstract kinds, so a shared closed part is never walked
    /// (DECISIONS BU-19).</remarks>
    private BoundType ContractType(BoundType type, BindingScope scope, BoundType? self = null, bool normalize = true)
    {
        if (!type.HasAbstractPart && !type.CarriesOrigin)
        {
            return type;
        }

        if (this.collectingAssociated is not { } batch)
        {
            return this.ContractTypeCore(type, scope, self, normalize);
        }

        var key = (type, scope, self, normalize, this.activeRequirementContract);
        if (batch.Normalized.TryGetValue(key, out var known))
        {
            return known;
        }

        var result = this.ContractTypeCore(type, scope, self, normalize);
        batch.Normalized[key] = result;
        return result;
    }

    private BoundType ContractTypeCore(BoundType type, BindingScope scope, BoundType? self, bool normalize)
    {
        type = this.ApplyContractEnvironment(type, scope);
        if (this.activeRequirementContract is { } bound)
        {
            type = this.SubstituteContractReference(type, bound); // SPEC 8.4.1, 8.6.4: the requirement of a bound reference.
        }

        // SPEC 8.4: Self of any Contract, including an ancestor whose requirement is read here, is the conforming Type. A
        // reference to a Contract, such as a projection's declaring Contract, only has its Type arguments substituted.
        if (IsContractSelf(type))
        {
            return self ?? this.EnclosingContractSelf(scope) ?? type;
        }

        var count = type.Components.Count;
        var components = count == 0 ? null : this.RentTypes(count);
        try
        {
            var changed = false;
            for (var i = 0; i < count; i++)
            {
                components![i] = this.ContractType(type.Components[i], scope, self, normalize);
                changed |= !ReferenceEquals(components[i], type.Components[i]);
            }

            var result = changed ? this.types.Rebuild(type, components.AsSpan(0, count), type.Origin, (BoundOrigin[])type.OriginArguments) : type;
            if (TyCtxt.HeldType(type) is { } held)
            {
                // DECISIONS BU-60: the Type instance a closure's context holds is normalized with the Closure Type.
                var image = this.ContractType(held, scope, self, normalize);
                result = ReferenceEquals(image, held) ? result : this.WithContext(result, this.HeldContext(type.Symbol!, image));
            }

            if (!normalize || result.Kind is not (BoundTypeKind.AssociatedProjection or BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication) || !this.normalizingAssociated.Add((result, scope)))
            {
                return result;
            }

            try
            {
                if (result.Kind == BoundTypeKind.AssociatedProjection && this.ProjectionSpecification(result, scope) is { Type: { } specified } outcome)
                {
                    // A container substitution can expose another associated projection (Wrapper<S>.Element -> S.Element). Normalize
                    // that identity too; the active query above still guards recursive specifications.
                    if (outcome.Subject is not { } subject)
                    {
                        return specified;
                    }

                    return this.StoredType(specified, subject) is { } stored ? this.ContractType(stored, scope, self) : result;
                }

                for (var current = scope; current is not null; current = current.Parent)
                {
                    if (current.Constraints is not { Invalid: false } environment)
                    {
                        continue;
                    }

                    foreach (var fact in environment.Facts)
                    {
                        if (fact.Kind == ConstraintKind.TypeIdentity && this.FactStates(fact, result, out var required) && this.AvailableConstraintFact(environment, fact) && required is not null)
                        {
                            return this.ContractType(required, scope, self);
                        }
                    }
                }

                return result;
            }
            finally
            {
                this.normalizingAssociated.Remove((result, scope));
            }
        }
        finally
        {
            if (components is not null)
            {
                this.typeScratch.Return(components, clearArray: true);
            }
        }
    }
}
