// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(BindingScope Parent, BindingSymbol Left, BindingSymbol Right), BindingScope> contractOverlapScopes = new();
    private readonly List<BoundConstraint> associatedOverlapChecks = new();
    private Dictionary<Koto, ContractAgreementFailure>? contractAgreementFailures;

    private readonly record struct ContractAgreementFailure(BindingSymbol Left, BindingSymbol Right, BoundType? AssociatedLeft = null, BoundType? AssociatedRight = null, BindingSymbol? Associated = null);

    // SPEC 8.4.9.2: these equalities describe the intersection where two references coincide, not new declaration
    // premises. The ordinary bounded prover must establish consistency there; a residual term is never disjointness.
    private BindingScope? ContractOverlapScope(BindingScope parent, BindingSymbol left, BindingSymbol right)
    {
        if (ReferenceEquals(left, right) || !this.ContractBindingsMayCollide(left, right, parent))
        {
            return null;
        }

        var key = (parent, left, right);
        if (!this.contractOverlapScopes.TryGetValue(key, out var scope))
        {
            this.contractOverlapScopes.Add(key, scope = new(parent.Owner));
        }

        scope.Reset();
        scope.Parent = parent;
        var environment = scope.Constraints ??= new();
        foreach (var (variable, target) in this.collisionSubstitutions)
        {
            this.AddConstraintFact(environment, this.InternConstraint(new(ConstraintKind.TypeIdentity, variable.Type, target.Type)));
        }

        this.AddConstraintFact(environment, this.InternConstraint(new(ConstraintKind.TypeIdentity, left.Type, right.Type)));
        return scope;
    }

    private bool RefutedOverlapInputs(BindingScope original, BindingScope overlap)
    {
        for (var scope = original; scope is not null; scope = scope.Parent)
        {
            if (scope.Constraints is not { Invalid: false } environment)
            {
                continue;
            }

            foreach (var fact in environment.DirectFacts)
            {
                // Associated specifications are the obligations being compared, so their contradiction cannot excuse
                // the overlap. Only independently valid generic inputs can rule the intersection out.
                var subject = fact.Kind == ConstraintKind.Not ? fact.Left?.Subject : fact.Subject;
                if (subject is { Kind: BoundTypeKind.Parameter or BoundTypeKind.TargetProjection } && !IsContractSelf(subject) &&
                    this.ProveConstraint(this.ContractConstraint(fact, overlap, null), overlap) == ConstraintProof.Refuted)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void AddOverlapFacts(BindingScope target, BindingScope source)
    {
        var environment = target.Constraints ??= new();
        for (var scope = source; scope is not null; scope = scope.Parent)
        {
            if (scope.Constraints is not { Invalid: false } other)
            {
                continue;
            }

            foreach (var fact in other.DirectFacts)
            {
                this.AddConstraintFact(environment, fact);
            }

            for (var i = 0; i < other.DerivedFacts.Count; i++)
            {
                var derived = other.DerivedFacts[i];
                this.AddConstraintFact(environment, derived.Fact, derived.Source);
            }
        }
    }

    private void ValidateAssociatedOverlapAgreement(BindingScope scope)
    {
        if (scope.Constraints is not { Invalid: false } environment)
        {
            return;
        }

        this.associatedOverlapChecks.Clear();
        foreach (var fact in environment.Facts)
        {
            if (fact is { Kind: ConstraintKind.TypeIdentity, Subject.Kind: BoundTypeKind.AssociatedProjection, RequiredType: not null } && this.AvailableConstraintFact(environment, fact))
            {
                this.associatedOverlapChecks.Add(fact);
            }
        }

        for (var i = 0; i < this.associatedOverlapChecks.Count; i++)
        {
            var a = this.associatedOverlapChecks[i];
            var x = a.Subject!;
            for (var j = 0; j < i; j++)
            {
                var b = this.associatedOverlapChecks[j];
                var y = b.Subject!;
                if (!ReferenceEquals(x.Symbol, y.Symbol) || !ReferenceEquals(x.Components[0], y.Components[0]) ||
                    !((BoundOrigin[])x.OriginArguments).AsSpan().SequenceEqual((BoundOrigin[])y.OriginArguments) ||
                    this.AssociatedIdentity(x) is not { } left || this.AssociatedIdentity(y) is not { } right ||
                    this.ContractOverlapScope(scope, left.Contract, right.Contract) is not { } overlap || this.RefutedOverlapInputs(scope, overlap))
                {
                    continue;
                }

                if (!this.SameConformanceType(a.RequiredType, b.RequiredType, overlap))
                {
                    environment.Invalid = true;
                    this.FailExplained(ref this.contractAgreementFailures, scope.Owner, BindingFailure.InvalidAssociatedType, new(left.Contract, right.Contract, a.RequiredType, b.RequiredType, x.Symbol));
                }
            }
        }
    }

    private void ValidateBoundConformanceAgreement()
    {
        foreach (var identities in this.conformancesByType.Values)
        {
            for (var i = 0; i < identities.Count; i++)
            {
                var left = identities[i];
                for (var j = 0; j < i; j++)
                {
                    var right = identities[j];
                    if (!ReferenceEquals(left.Contract.Declaration, right.Contract.Declaration))
                    {
                        continue;
                    }

                    foreach (var a in left.PathStorage)
                    {
                        foreach (var b in right.PathStorage)
                        {
                            if (!a.IsVerified || !b.IsVerified || this.ContractOverlapScope(a.Scope, left.Contract, right.Contract) is not { } scope)
                            {
                                continue;
                            }

                            var self = this.SelfType(left.Type);
                            if (this.DisjointConformanceFacts(a.Scope, b.Scope) || this.RefutedOverlapInputs(a.Scope, scope) || this.RefutedOverlapInputs(b.Scope, scope) ||
                                this.ProveConformanceConditions(a, self, scope) == ConstraintProof.Refuted || this.ProveConformanceConditions(b, self, scope) == ConstraintProof.Refuted)
                            {
                                continue;
                            }

                            this.AddOverlapFacts(scope, b.Scope);
                            if (!this.SameNormalizedMappings(a, b, scope) || !this.SameNormalizedMappings(b, a, scope))
                            {
                                left.Invalid = right.Invalid = true;
                                left.IsVerified = right.IsVerified = false;
                                this.FailExplained(ref this.contractAgreementFailures, a.Use, BindingFailure.IncompatibleImplementation, new(left.Contract, right.Contract));
                            }
                        }
                    }
                }
            }
        }
    }

    private bool SameNormalizedRequirement(BoundRequirement a, BoundRequirement b, BindingScope scope)
        => ReferenceEquals(a.Symbol, b.Symbol) && (ReferenceEquals(a.Contract, b.Contract) || this.SameConformanceType(a.Contract.Type, b.Contract.Type, scope));

    private bool SameNormalizedMappings(BoundConformancePath a, BoundConformancePath b, BindingScope scope)
    {
        foreach (var x in a.WitnessStorage)
        {
            var found = false;
            foreach (var y in b.WitnessStorage)
            {
                if (!this.SameNormalizedRequirement(x.Identity, y.Identity, scope))
                {
                    continue;
                }

                if (!ReferenceEquals(x.Implementation, y.Implementation) || !this.SameFunctionWitness(x.Function, y.Function, scope))
                {
                    return false;
                }

                found = true;
            }

            if (!found)
            {
                return false;
            }
        }

        foreach (var x in a.AssociatedStorage)
        {
            var found = false;
            foreach (var y in b.AssociatedStorage)
            {
                if (!this.SameNormalizedRequirement(x.Key, y.Key, scope))
                {
                    continue;
                }

                if (!this.SameConformanceType(x.Value, y.Value, scope))
                {
                    return false;
                }

                found = true;
            }

            if (!found)
            {
                return false;
            }
        }

        foreach (var x in a.PropertyWitnessStorage)
        {
            var found = false;
            foreach (var y in b.PropertyWitnessStorage)
            {
                if (x.Requirement.Kind != y.Requirement.Kind || !this.SameNormalizedRequirement(x.Identity, y.Identity, scope))
                {
                    continue;
                }

                if (!this.SamePropertyWitness(x, y, scope))
                {
                    return false;
                }

                found = true;
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }

    private bool SamePropertyWitness(BoundPropertyWitness x, BoundPropertyWitness y, BindingScope scope)
    {
        if (!ReferenceEquals(x.Requirement, y.Requirement) || !ReferenceEquals(x.Implementation, y.Implementation) || x.Kind != y.Kind || !this.SameMemberPath(x.BasePath, y.BasePath, scope) || x.ObjectCompatibility != y.ObjectCompatibility ||
            !this.SameConformanceType(x.ReceiverType, y.ReceiverType, scope) || !this.SameConformanceType(x.InputType, y.InputType, scope) ||
            !this.SameConformanceType(x.ResultType, y.ResultType, scope) || !this.SameConformanceType(x.ImplementationType, y.ImplementationType, scope) || x.InputOrigins.Count != y.InputOrigins.Count)
        {
            return false;
        }

        for (var i = 0; i < x.InputOrigins.Count; i++)
        {
            if (!ReferenceEquals(x.InputOrigins[i], y.InputOrigins[i]))
            {
                return false;
            }
        }

        return true;
    }
}
