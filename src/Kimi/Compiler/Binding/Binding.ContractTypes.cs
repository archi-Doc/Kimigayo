// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Owns a Type's Contract environment rewrite (ContractType): a Contract's Self replaced by the conforming Type, a bound Contract
// reference's arguments substituted, and completed associated identities normalized in a scope, without member selection, each distinct
// part once per call (contractParts). Reads the conformance tables and the scope's facts; publishes nothing.
public sealed partial class Binding
{
    // The images of ContractType's queries (DECISIONS BU-64): one outermost call is a query, its scope, Self, normalize flag,
    // activeRequirementContract and normalizingAssociated's set fixed, so each distinct part is rewritten once per query. The set changes
    // only by a push after a part's components are rewritten, popped before that part returns, and what runs under the push enters
    // ContractType afresh: a nested query, which can only evict this query's entries, recomputed then (TypeMemo's contract).
    private readonly TypeMemo<BoundType> contractParts = new();

    /// <summary>Substitutes Contract Self and normalizes completed associated identities without performing member selection.</summary>
    /// <remarks>A Type with neither an abstract part nor an Origin is its own image: the environment substitutes slots and Origins, a
    /// Contract's Self is a Parameter and normalization rewrites only abstract kinds, so a shared closed part is never walked
    /// (DECISIONS BU-19). Any other part is rewritten once per call (contractParts, DECISIONS BU-64).</remarks>
    private BoundType ContractType(BoundType type, BindingScope scope, BoundType? self = null, bool normalize = true)
    {
        if (!type.HasAbstractPart && !type.CarriesOrigin)
        {
            return type;
        }

        var query = this.contractParts.Begin(this.storageVersion);
        return this.collectingAssociated is null ? this.ContractTypeCore(type, scope, self, normalize, query) : this.BatchedContractType(type, scope, self, normalize, query);
    }

    // A component of a query: the associated collection's batch answers it where one is open (unchanged); otherwise a component with
    // components is recorded, a leaf being cheaper to rewrite again than to record and a query's root met once (DECISIONS BU-55).
    private BoundType ContractPart(BoundType type, BindingScope scope, BoundType? self, bool normalize, uint query)
    {
        if (!type.HasAbstractPart && !type.CarriesOrigin)
        {
            return type;
        }

        if (this.collectingAssociated is not null)
        {
            return this.BatchedContractType(type, scope, self, normalize, query);
        }

        if (type.Components.Count == 0)
        {
            return this.ContractTypeCore(type, scope, self, normalize, query);
        }

        if (!this.contractParts.TryGet(type, query, out var image))
        {
            image = this.ContractTypeCore(type, scope, self, normalize, query);
            this.contractParts.Set(type, query, image);
        }

        return image;
    }

    // The associated collection's answer, by every input including the active requirement (Binding.AssociatedInference's batch).
    private BoundType BatchedContractType(BoundType type, BindingScope scope, BoundType? self, bool normalize, uint query)
    {
        var batch = this.collectingAssociated!;
        var key = (type, scope, self, normalize, this.activeRequirementContract);
        if (batch.Normalized.TryGetValue(key, out var known))
        {
            return known;
        }

        var result = this.ContractTypeCore(type, scope, self, normalize, query);
        batch.Normalized[key] = result;
        return result;
    }

    private BoundType ContractTypeCore(BoundType type, BindingScope scope, BoundType? self, bool normalize, uint query)
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
                components![i] = this.ContractPart(type.Components[i], scope, self, normalize, query);
                changed |= !ReferenceEquals(components[i], type.Components[i]);
            }

            var result = changed ? this.types.Rebuild(type, components.AsSpan(0, count), type.Origin, (BoundOrigin[])type.OriginArguments) : type;
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
