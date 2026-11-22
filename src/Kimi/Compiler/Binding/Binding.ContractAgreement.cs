// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(BindingScope Parent, BindingSymbol Left, BindingSymbol Right), BindingScope> contractOverlapScopes = new();
    private readonly Dictionary<BindingScope, BindingScope> collisionScopes = new(ReferenceEqualityComparer.Instance); // CollisionScope's, per overlap scope
    private readonly List<(BoundConstraint A, BoundConstraint B)> associatedOverlapPairs = new();

    // SPEC 8.9.2: these equalities describe the intersection where two references coincide, not new declaration
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

    // R7-C3 (B2'; SPEC 8.9.1 step 1, 8.9.2): the collision conditions are the collision substitution together with all the Type's inputs,
    // never the specifications (CollisionScope). The inputs rule the intersection out when the judge finds that view contradictory: an
    // identity class in Conflict, or an input the view's closed world refutes (Refuted, or Error: a premise against its closed world),
    // alike for every input kind.
    private bool RefutedOverlapInputs(BindingScope original, BindingScope overlap, QuerySite site)
    {
        var collision = this.CollisionScope(original, overlap);
        if (this.ClassesOf(collision, out _) is { Worst: IdentityClasses.Conflict })
        {
            return true;
        }

        for (var scope = original; scope is not null; scope = scope.Parent)
        {
            if (scope.Constraints is not { } environment)
            {
                continue;
            }

            foreach (var fact in environment.DirectFacts)
            {
                // Associated specifications are the obligations being compared, so their contradiction cannot excuse
                // the overlap. Only independently valid generic inputs can rule the intersection out.
                var subject = fact.Kind == ConstraintKind.Not ? fact.Left?.Subject : fact.Subject;
                if (subject is { Kind: BoundTypeKind.Parameter or BoundTypeKind.TargetProjection } && !IsContractSelf(subject) &&
                    this.ProveConstraint(this.ContractConstraint(fact, collision, null), collision, site) is ConstraintProof.Refuted or ConstraintProof.Error)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // The collision conditions in a view of their own, no parent: the overlap's own identities (the collision substitution and the two
    // references made one) and the Type's inputs, the facts RefutedOverlapInputs reads (a generic input's, never a specification).
    private BindingScope CollisionScope(BindingScope original, BindingScope overlap)
    {
        if (!this.collisionScopes.TryGetValue(overlap, out var scope))
        {
            this.collisionScopes.Add(overlap, scope = new(overlap.Owner));
        }

        scope.Reset();
        scope.Parent = null;
        var environment = scope.Constraints ??= new();
        foreach (var fact in overlap.Constraints!.DirectFacts)
        {
            this.AddConstraintFact(environment, fact);
        }

        for (var current = original; current is not null; current = current.Parent)
        {
            if (current.Constraints is not { } inputs)
            {
                continue;
            }

            foreach (var fact in inputs.DirectFacts)
            {
                if ((fact.Kind == ConstraintKind.Not ? fact.Left?.Subject : fact.Subject) is { Kind: BoundTypeKind.Parameter or BoundTypeKind.TargetProjection } subject &&
                    !IsContractSelf(subject))
                {
                    this.AddConstraintFact(environment, fact);
                }
            }
        }

        return scope;
    }

    private void AddOverlapFacts(BindingScope target, BindingScope source)
    {
        var environment = target.Constraints ??= new();
        for (var scope = source; scope is not null; scope = scope.Parent)
        {
            if (scope.Constraints is not { } other)
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
                this.AddConstraintFact(environment, derived.Fact, derived.Source, derived.Origin);
            }
        }
    }

    private void ValidateAssociatedOverlapAgreement(BindingScope scope)
    {
        if (scope.Constraints is not { } environment)
        {
            return;
        }

        // SPEC 8.9.2: the pairs are the scope's available identities of one projection, each with those before it: a bucket of
        // Closure(view)'s TypeIdentity row of this scope (its key is the projection's declaration and receiver). The judgment below
        // proves, and nothing proves while an entry is read (ContractSolver's Readers), so the pairs are taken first.
        this.associatedOverlapPairs.Clear();
        ViewIndex? index = null;
        try
        {
            foreach (var a in environment.Facts)
            {
                if (a is not { Kind: ConstraintKind.TypeIdentity, Subject: { Kind: BoundTypeKind.AssociatedProjection } x, RequiredType: not null } ||
                    !this.AvailableConstraintFact(environment, a, out _) ||
                    (index ??= this.BeginClosureRead(scope)).Buckets.GetValueOrDefault((scope, ConstraintKind.TypeIdentity, SubjectKey(x).Symbol, SubjectKey(x).Type)) is not { } bucket)
                {
                    continue;
                }

                for (var j = 0; j < bucket.Count && !ReferenceEquals(bucket[j].Raw, a); j++)
                {
                    if (bucket[j].Raw is { RequiredType: not null } b && this.AvailableConstraintFact(environment, b, out _))
                    {
                        this.associatedOverlapPairs.Add((a, b));
                    }
                }
            }
        }
        finally
        {
            if (index is not null)
            {
                this.EndClosureRead();
            }
        }

        foreach (var (a, b) in this.associatedOverlapPairs)
        {
            var (x, y) = (a.Subject!, b.Subject!);
            if (!((BoundOrigin[])x.OriginArguments).AsSpan().SequenceEqual((BoundOrigin[])y.OriginArguments) ||
                this.AssociatedIdentity(x) is not { } left || this.AssociatedIdentity(y) is not { } right ||
                this.ContractOverlapScope(scope, left.Contract, right.Contract) is not { } overlap || this.RefutedOverlapInputs(scope, overlap, scope.Owner))
            {
                continue;
            }

            if (!this.SameConformanceType(a.RequiredType, b.RequiredType, overlap, scope.Owner))
            {
                this.FailPremise(environment, a, scope.Owner); // R7-C3 gate 2: the disagreeing specifications fail (b where it is this one's)
                this.FailPremise(environment, b, scope.Owner);
                var (l, r, item) = (this.FactSite(left.Contract.Declaration), this.FactSite(right.Contract.Declaration), this.FactSite(left.Symbol.Declaration));
                this.RecordNumbered(
                    scope.Owner,
                    BindingFailure.InvalidAssociatedType,
                    [
                        new(ProblemFactKind.Related, l, 0),
                        new(ProblemFactKind.Instance, l, this.report.Keep(left.Contract.Type!)),
                        new(ProblemFactKind.Instance, item, this.report.Keep(a.RequiredType!)),
                        new(ProblemFactKind.Related, r, 0),
                        new(ProblemFactKind.Instance, r, this.report.Keep(right.Contract.Type!)),
                        new(ProblemFactKind.Instance, item, this.report.Keep(b.RequiredType!)),
                    ]);
            }
        }
    }

    private void ValidateBoundConformanceAgreement()
    {
        foreach (var identities in this.impls.InPass.ByType)
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

                    foreach (var a in this.impls.PathsOf(left))
                    {
                        foreach (var b in this.impls.PathsOf(right))
                        {
                            if (!a.IsVerified || !b.IsVerified || this.ContractOverlapScope(a.Scope, left.Contract, right.Contract) is not { } scope)
                            {
                                continue;
                            }

                            var self = this.SelfType(left.Type);
                            if (this.DisjointConformanceFacts(a.Scope, b.Scope) || this.RefutedOverlapInputs(a.Scope, scope, a.Use) || this.RefutedOverlapInputs(b.Scope, scope, a.Use) ||
                                this.ProveConformanceConditions(a, self, scope) == ConstraintProof.Refuted || this.ProveConformanceConditions(b, self, scope) == ConstraintProof.Refuted)
                            {
                                continue;
                            }

                            this.AddOverlapFacts(scope, b.Scope);
                            if (!this.SameNormalizedMappings(a, b, scope, a.Use) || !this.SameNormalizedMappings(b, a, scope, a.Use))
                            {
                                left.Invalid = right.Invalid = true;
                                left.IsVerified = right.IsVerified = false;
                                var (l, r) = (this.FactSite(left.Contract.Declaration), this.FactSite(right.Contract.Declaration));
                                this.RecordNumbered(
                                    a.Use,
                                    BindingFailure.IncompatibleImplementation,
                                    [
                                        new(ProblemFactKind.Related, l, 0),
                                        new(ProblemFactKind.Instance, l, this.report.Keep(left.Contract.Type!)),
                                        new(ProblemFactKind.Related, r, 0),
                                        new(ProblemFactKind.Instance, r, this.report.Keep(right.Contract.Type!)),
                                    ]);
                            }
                        }
                    }
                }
            }
        }
    }

    // SPEC 8.9.2: two coinciding instances of one Contract that disagree, one problem per disagreeing pair (RecordNumbered): each instance's
    // Contract declaration (Related, role 0, shared by both) with the instance (Instance) and, for an associated Type they bind to Types
    // not proven equal, the Type each binds it to at its declaration (a second Instance); otherwise their effective implementation
    // mappings disagree.
    private void ReportDisagreement(Koto node, DiagnosticCollection? store, int index, in BindingProblem problem)
    {
        var facts = this.report.Facts(index);
        var associated = (BindingFailure)problem.Requirement.Kind == BindingFailure.InvalidAssociatedType;
        var right = associated ? 3 : 2;
        var first = DiagnosticTypeName(this.report.Value(facts[1]));
        var second = DiagnosticTypeName(this.report.Value(facts[right + 1]));
        object[] evidence = associated
            ? [first, second, this.AssociatedName(this.compilation.SyntaxOf(facts[2].Site)), DiagnosticTypeName(this.report.Value(facts[2])), DiagnosticTypeName(this.report.Value(facts[5]))]
            : [$"At {first} = {second}, effective implementation mappings do not agree"];
        node.ReportIn(
            store,
            problem.Requirement,
            problem.Code,
            evidence: evidence,
            related: [("contract", this.compilation.SyntaxOf(facts[0].Site), first), ("contract", this.compilation.SyntaxOf(facts[right].Site), second)],
            condition: problem.Condition);
    }

    private bool SameNormalizedRequirement(BoundRequirement a, BoundRequirement b, BindingScope scope, QuerySite site)
        => ReferenceEquals(a.Symbol, b.Symbol) && (ReferenceEquals(a.Contract, b.Contract) || this.SameConformanceType(a.Contract.Type, b.Contract.Type, scope, site));

    private bool SameNormalizedMappings(BoundConformancePath a, BoundConformancePath b, BindingScope scope, QuerySite site)
    {
        foreach (var x in a.WitnessStorage)
        {
            var found = false;
            foreach (var y in b.WitnessStorage)
            {
                if (!this.SameNormalizedRequirement(x.Identity, y.Identity, scope, site))
                {
                    continue;
                }

                if (!ReferenceEquals(x.Implementation, y.Implementation) || !this.SameFunctionWitness(x.Function, y.Function, scope, site))
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
                if (!this.SameNormalizedRequirement(x.Key, y.Key, scope, site))
                {
                    continue;
                }

                if (!this.SameConformanceType(x.Value, y.Value, scope, site))
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
                if (x.Requirement.Kind != y.Requirement.Kind || !this.SameNormalizedRequirement(x.Identity, y.Identity, scope, site))
                {
                    continue;
                }

                if (!this.SamePropertyWitness(x, y, scope, site))
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

    private bool SamePropertyWitness(BoundPropertyWitness x, BoundPropertyWitness y, BindingScope scope, QuerySite site)
    {
        if (!ReferenceEquals(x.Requirement, y.Requirement) || !ReferenceEquals(x.Implementation, y.Implementation) || x.Kind != y.Kind || !this.SameMemberPath(x.BasePath, y.BasePath, scope, site) || x.ObjectCompatibility != y.ObjectCompatibility ||
            !this.SameConformanceType(x.ReceiverType, y.ReceiverType, scope, site) || !this.SameConformanceType(x.InputType, y.InputType, scope, site) ||
            !this.SameConformanceType(x.ResultType, y.ResultType, scope, site) || !this.SameConformanceType(x.ImplementationType, y.ImplementationType, scope, site) || x.InputOrigins.Count != y.InputOrigins.Count)
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
