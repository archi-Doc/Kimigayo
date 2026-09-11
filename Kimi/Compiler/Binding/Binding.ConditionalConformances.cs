// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly HashSet<(BoundType Type, BindingSymbol Contract, BindingScope Scope)> conformanceQueries = new();
    private readonly Dictionary<(BoundConformancePath Left, BoundConformancePath Right), BindingScope> conformanceOverlapScopes = new();

    private static bool DisjointConformanceFacts(BindingScope a, BindingScope b)
    {
        for (var left = a; left is not null; left = left.Parent)
        {
            if (left.Constraints is not { Invalid: false } x)
            {
                continue;
            }

            for (var right = b; right is not null; right = right.Parent)
            {
                if (right.Constraints is not { Invalid: false } y)
                {
                    continue;
                }

                foreach (var p in x.Facts)
                {
                    foreach (var q in y.Facts)
                    {
                        if (p.Subject is null || !ReferenceEquals(p.Subject, q.Subject))
                        {
                            continue;
                        }

                        // Substitute explicit identities, then compare determined concrete atoms.
                        // No case splitting, contrapositive, or general satisfiability solver.
                        if (p.Kind == ConstraintKind.TypeIdentity && q.Kind == ConstraintKind.TypeIdentity && p.RequiredType is { } pt && q.RequiredType is { } qt && !DependentType(pt) && !DependentType(qt) && !ReferenceEquals(pt, qt))
                        {
                            return true;
                        }

                        if (p.Kind == ConstraintKind.Semantics && q.Kind == ConstraintKind.Semantics && (p.Mask & q.Mask) == 0)
                        {
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private ConstraintProof VerifyConformanceAgreement(BoundConformance identity)
    {
        if (identity.Invalid)
        {
            return ConstraintProof.Error;
        }

        var proof = ConstraintProof.Proven;
        for (var i = 0; i < identity.PathStorage.Count; i++)
        {
            var a = identity.PathStorage[i];
            proof = CombineProof(proof, this.VerifyConformance(a), true);
            for (var j = 0; j < i; j++)
            {
                var b = identity.PathStorage[j];
                if (!a.IsVerified || !b.IsVerified)
                {
                    proof = CombineProof(proof, ConstraintProof.Unknown, true);
                    continue;
                }

                // Only refutation in an independently valid environment establishes exclusion.
                // Arbitrary errors (invalid names, declarations, cycles) are never disjointness.
                var self = this.SelfType(a.Type);
                var ab = this.ProveConformanceConditions(a, self, b.Scope);
                var ba = this.ProveConformanceConditions(b, self, a.Scope);
                var disjoint = DisjointConformanceFacts(a.Scope, b.Scope);
                if (ab == ConstraintProof.Error || ba == ConstraintProof.Error)
                {
                    // Substitution can expose a directly inconsistent intersection of two
                    // individually valid paths. Only the concrete fact check may exclude it.
                    if (!disjoint)
                    {
                        proof = ConstraintProof.Error;
                        continue;
                    }
                }

                if (ab == ConstraintProof.Refuted || ba == ConstraintProof.Refuted || disjoint)
                {
                    continue;
                }

                if (!this.conformanceOverlapScopes.TryGetValue((a, b), out var scope))
                {
                    this.conformanceOverlapScopes.Add((a, b), scope = new(a.Declaration));
                }

                scope.Reset();
                scope.Parent = a.Scope;
                var environment = scope.Constraints ??= new();
                if (b.Scope.Parent?.Constraints is { } other)
                {
                    foreach (var fact in other.Facts)
                    {
                        this.AddConstraintFact(environment, fact);
                    }
                }

                var same = a.WitnessStorage.Count == b.WitnessStorage.Count && a.AssociatedStorage.Count == b.AssociatedStorage.Count && a.PropertyWitnessStorage.Count == b.PropertyWitnessStorage.Count;
                for (var w = 0; w < a.WitnessStorage.Count; w++)
                {
                    var witness = a.WitnessStorage[w];
                    same &= b.WitnessMap.TryGetValue(witness.Requirement, out var implementation) && ReferenceEquals(witness.Implementation, implementation);
                }

                foreach (var binding in a.AssociatedStorage)
                {
                    same &= b.AssociatedStorage.TryGetValue(binding.Key, out var otherType) && this.SameConformanceType(binding.Value, otherType, scope);
                }

                for (var w = 0; w < a.PropertyWitnessStorage.Count; w++)
                {
                    var x = a.PropertyWitnessStorage[w];
                    if (!b.PropertyWitnessMap.TryGetValue((x.Requirement.Property.Symbol, x.Requirement.Kind), out var y))
                    {
                        same = false;
                        continue;
                    }

                    same &= ReferenceEquals(x.Requirement, y.Requirement) && ReferenceEquals(x.Implementation, y.Implementation) && x.Kind == y.Kind && ReferenceEquals(x.BasePath, y.BasePath)
                        && this.SameConformanceType(x.ReceiverType, y.ReceiverType, scope) && this.SameConformanceType(x.InputType, y.InputType, scope)
                        && this.SameConformanceType(x.ResultType, y.ResultType, scope) && this.SameConformanceType(x.ImplementationType, y.ImplementationType, scope)
                        && x.InputOrigins.Count == y.InputOrigins.Count;
                    for (var o = 0; o < Math.Min(x.InputOrigins.Count, y.InputOrigins.Count); o++)
                    {
                        same &= ReferenceEquals(x.InputOrigins[o], y.InputOrigins[o]);
                    }
                }

                if (!same)
                {
                    identity.Invalid = true;
                    Fail(a.Declaration, BindingFailure.IncompatibleImplementation);
                    Fail(b.Declaration, BindingFailure.IncompatibleImplementation);
                    proof = ConstraintProof.Error;
                }
            }
        }

        identity.IsVerified = proof == ConstraintProof.Proven;
        return proof;
    }

    private bool SameConformanceType(BoundType? a, BoundType? b, BindingScope scope)
        => ReferenceEquals(a, b) || (a is not null && b is not null && this.ProveConstraint(this.InternConstraint(new(ConstraintKind.TypeIdentity, this.ContractType(a, scope), this.ContractType(b, scope))), scope) == ConstraintProof.Proven);

    private void BindConditionalDeclarations(bool conditions)
    {
        for (var n = 0; n < this.nodes.Count; n++)
        {
            if (this.nodes[n] is not SyntaxFormKoto { Akind: KotoKind.ConditionalConformance, Parent: DeclarationContainerKoto container } syntax)
            {
                continue;
            }

            if (container is not (StructKoto or EnumKoto) || syntax.Operands.Length is not (2 or 3) || syntax.Operands[0] is not IsKoto target || syntax.Operands[1] is not SyntaxFormKoto premises)
            {
                Fail(syntax, BindingFailure.InvalidConstraint);
                continue;
            }

            var outer = this.scopes[container];
            var scope = this.GetScope(syntax, outer);
            if (!conditions)
            {
                this.ValidateConditionalBlock(syntax, container);
                this.BindConstraint(target, outer);
                if (container.GenericParameterNodes.Count == 0 || target.Left is not IdentifierNameKoto { IdentifierName: "Self" } || target.BoundConstraint is not { Kind: ConstraintKind.Contract, Contract: { Contract: not null } contract } || premises.Operands.Length == 0)
                {
                    Fail(syntax, BindingFailure.InvalidConstraint);
                    continue;
                }

                if (contract.Intrinsic is IntrinsicKind.None or IntrinsicKind.Copy or IntrinsicKind.Owned)
                {
                    this.RegisterConformanceDeclaration(container.BoundSymbol!, contract, target, scope, premises);
                    if (IsRefinement(contract, this.Core.Copy))
                    {
                        this.RegisterCopy(target, container, scope, premises);
                    }
                }
                else
                {
                    Fail(syntax, BindingFailure.InvalidConstraint);
                }

                continue;
            }

            var valid = syntax.BindingState != BindingState.Invalid;
            // Bind ordinary parameter facts before projections, independent of clause order.
            for (var pass = 0; pass < 2; pass++)
            {
                for (var i = 0; i < premises.Operands.Length; i++)
                {
                    if (premises.Operands[i] is not IsKoto condition)
                    {
                        valid = false;
                        continue;
                    }

                    if (this.DeferredConstraint(condition, scope) != (pass != 0))
                    {
                        continue;
                    }

                    this.BindConstraint(condition, scope);
                    var subject = condition.BoundConstraint?.Subject ?? condition.Left.BoundType;
                    while (subject?.Kind == BoundTypeKind.AssociatedProjection)
                    {
                        subject = subject.Components[0];
                    }

                    valid &= condition.BoundConstraint is { } requirement && PositiveRequirement(requirement) && ReferenceEquals(subject?.Symbol?.Scope, outer);
                }

                this.ExpandScopeContractPremises(scope);
            }

            Complete(premises, BoundType.Boolean);
            if (valid)
            {
                Complete(syntax, BoundType.Unit);
            }
            else
            {
                Fail(syntax, BindingFailure.InvalidConstraint);
                (scope.Constraints ??= new()).Invalid = true;
            }
        }
    }

    private ConstraintProof ResolveConformance(BoundType type, BindingSymbol contract, BindingScope scope, out BoundConformancePath? evidence)
    {
        evidence = null;
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints?.Invalid == true)
            {
                return ConstraintProof.Error;
            }
        }

        if (!this.contractHeadersReady || type.Kind is not (BoundTypeKind.Nominal or BoundTypeKind.Constructed) || type.Symbol is not { } symbol)
        {
            return ConstraintProof.Unknown;
        }

        if (InvalidConstraintType(type) || contract.Declaration.BindingState == BindingState.Invalid)
        {
            return ConstraintProof.Error;
        }

        if (!this.conformances.TryGetValue((symbol, contract), out var identity) || identity.PathStorage.Count == 0)
        {
            return !DependentType(type) && this.capabilityMode == BindingMode.Final ? ConstraintProof.Refuted : ConstraintProof.Unknown;
        }

        if (identity.Invalid)
        {
            return ConstraintProof.Error;
        }

        var key = (type, contract, scope);
        if (!this.conformanceQueries.Add(key))
        {
            return ConstraintProof.Unknown;
        }

        try
        {
            var result = ConstraintProof.Refuted;
            for (var i = 0; i < identity.PathStorage.Count; i++)
            {
                var path = identity.PathStorage[i];
                var definition = this.VerifyConformance(path);
                var condition = this.ProveConformanceConditions(path, type, scope);
                var available = definition == ConstraintProof.Proven ? condition : condition == ConstraintProof.Error ? ConstraintProof.Error : definition;
                result = CombineProof(result, available, false);
                if (available == ConstraintProof.Proven)
                {
                    evidence ??= path;
                }
            }

            if (result == ConstraintProof.Proven && symbol.Declaration is DeclarationContainerKoto container && container.GenericParameterNodes.Count != 0)
            {
                result = CombineProof(result, type.Components.Count == container.GenericParameterNodes.Count ? this.CheckConstraints(container.ConstraintNodes, container, (BoundType[])type.Components, scope) : ConstraintProof.Unknown, true);
            }

            if (result != ConstraintProof.Proven)
            {
                evidence = null;
            }

            return result;
        }
        finally
        {
            this.conformanceQueries.Remove(key);
        }
    }

    private ConstraintProof ProveConformanceConditions(BoundConformancePath path, BoundType type, BindingScope scope)
    {
        if (path.Scope.Parent?.Constraints?.Invalid == true)
        {
            return ConstraintProof.Error;
        }

        return this.ProveConditionalPremises(path.Premises, path.Type.Declaration, type, scope);
    }

    private ConstraintProof ProveConditionalPremises(SyntaxFormKoto? premises, Koto binder, BoundType type, BindingScope scope)
    {
        if (premises is null)
        {
            return ConstraintProof.Proven;
        }

        if (premises.Parent?.BindingState == BindingState.Invalid)
        {
            return ConstraintProof.Error;
        }

        var result = ConstraintProof.Proven;
        for (var i = 0; i < premises.Operands.Length; i++)
        {
            var condition = (IsKoto)premises.Operands[i];
            result = CombineProof(result, condition.BoundConstraint is { } bound ? this.ProveConstraint(this.ContractConstraint(this.SubstituteConstraint(bound, binder, (BoundType[])type.Components), scope, type), scope) : ConstraintProof.Unknown, true);
        }

        return result;
    }
}
