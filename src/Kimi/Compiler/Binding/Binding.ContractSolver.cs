// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// ContractSolver (PlanB §4.1; DECISIONS BU-29): premise derivation and evidence availability (SPEC 8.3): what the premises supply for a
// proposition and its negation (Supply) under the request's view (ViewOf, the one derivation point); until C3 the eager derivation too
// (ExpandContractPremises, ExpandScopeContractPremises, AddContractPremises) and the availability of a fact (AvailableConstraintFact).
// - Inputs: the view's scope chain (ConstraintEnvironments), derived facts' source Contracts.
// - Outputs: Evidence (Positive, Negative; Invalid, temporary until C3: the chain holds an Invalid environment).
// - Invariants: innermost scope first, each environment in its order; an Invalid environment answers Invalid to Supply; availability
//   is asked per fact, lazily, in the caller's order.
// - Prohibitions: no judgment, lookup, normalization or inference rule here; no mode or bool parameter; no answer kept across
//   requests; no new syntax-triggered supply; no read sets.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly HashSet<BindingSymbol> contractPremiseQueries = new(ReferenceEqualityComparer.Instance);
    private BoundConstraint[] contractFactScratch = [];

    // The premises a request reads: the scope its consumer holds. C3 adds the source states, the definition whose collection is
    // active, the inadmissible bindings and the check under way here, in ViewOf (BU-29).
    private readonly record struct EvidenceView(BindingScope Premises);

    // Supply's answer: whether the premises supply the proposition (Positive) and its negation (Negative). Invalid (temporary, BU-29;
    // removed at C3 when B 3.10's declaration validation answers a request reaching a premise error): the chain holds an Invalid
    // environment, which ProveProposition answers Error.
    private readonly struct Evidence
    {
        internal bool Positive { get; init; }

        internal bool Negative { get; init; }

        internal bool Invalid { get; init; }
    }

    private EvidenceView ViewOf(BindingScope scope) => new(scope);

    // `proposition` is normalized by its caller (NormalizeProofConstraint); a fact is normalized only under the presence gate.
    private Evidence Supply(BoundConstraint proposition, EvidenceView view)
    {
        var positive = false;
        var negative = false;
        var parameterIdentity = HasParameterIdentity(view.Premises);
        var negation = this.NegateConstraint(proposition);
        for (var current = view.Premises; current is not null; current = current.Parent)
        {
            if (current.Constraints is not { } environment)
            {
                continue;
            }

            if (environment.Invalid)
            {
                return new() { Invalid = true };
            }

            positive |= this.AvailableConstraintFact(environment, proposition);
            negative |= this.AvailableConstraintFact(environment, negation);
            if (environment.HasAssociatedProjection || parameterIdentity || proposition.Kind == ConstraintKind.Callable || proposition is { Kind: ConstraintKind.Not, Left.Kind: ConstraintKind.Callable })
            {
                foreach (var fact in environment.Facts)
                {
                    if (!this.AvailableConstraintFact(environment, fact))
                    {
                        continue;
                    }

                    var identity = this.NormalizeProofConstraint(fact, view.Premises);
                    positive |= SameProofPremise(identity, proposition);
                    negative |= SameProofPremise(identity, negation);
                }
            }
        }

        return new() { Positive = positive, Negative = negative };
    }

    private bool AvailableContractPremise(BindingSymbol contract)
    {
        // Ancestors is already the deduplicated transitive closure. Recurse only through
        // actual closed obligations, not through that flattened refinement list again.
        var ancestors = contract.Contract?.Ancestors;
        for (var i = -1; i < (ancestors?.Count ?? 0); i++)
        {
            var source = i < 0 ? contract : ancestors![i];
            if (InvalidDeclarationContext(source.Declaration) || UnresolvedTypeDeclarationContext(source.Declaration) || !this.contractPremiseQueries.Add(source))
            {
                return false;
            }

            try
            {
                if (this.CheckClosedDeclarationConstraints((DeclarationContainerKoto)source.Declaration) != ConstraintProof.Proven)
                {
                    return false;
                }
            }
            finally
            {
                this.contractPremiseQueries.Remove(source);
            }
        }

        return true;
    }

    private bool AvailableConstraintFact(ConstraintEnvironment environment, BoundConstraint fact)
    {
        if (!environment.Facts.Contains(fact))
        {
            return false;
        }

        if (environment.DirectFacts.Contains(fact))
        {
            return true;
        }

        for (var i = 0; i < environment.DerivedFacts.Count; i++)
        {
            var derived = environment.DerivedFacts[i];
            if (ReferenceEquals(derived.Fact, fact) && this.AvailableContractPremise(derived.Source))
            {
                return true;
            }
        }

        return false;
    }

    private void ExpandContractPremises()
    {
        for (var n = 0; n < this.nodes.Count; n++)
        {
            if (this.nodes[n] is not (FunctionKoto or DeclarationContainerKoto or SyntaxFormKoto { Akind: KotoKind.ConditionalConformance, Parent: DeclarationContainerKoto }))
            {
                continue;
            }

            var scope = this.scopes[this.nodes[n]];
            if (scope.Owner is ContractKoto contract)
            {
                // SPEC 8.7.3: inside a Contract, an inherited Self clause holds for its Self, as one declared directly does.
                this.AddContractPremises(contract.BoundSymbol!.Contract!, this.ContractSelfType(contract.BoundSymbol), scope);
            }

            this.ExpandScopeContractPremises(scope);
        }
    }

    private void ExpandScopeContractPremises(BindingScope scope)
    {
        if (scope.Constraints is not { } environment || environment.Facts.Count == 0)
        {
            return;
        }

        var count = environment.Facts.Count;
        if (this.contractFactScratch.Length < count)
        {
            this.contractFactScratch = new BoundConstraint[Math.Max(count, Math.Max(16, this.contractFactScratch.Length * 2))];
        }

        // Expansion performs no proof or nested scope expansion, so one owned buffer suffices.
        var facts = this.contractFactScratch;
        environment.Facts.CopyTo(facts);
        try
        {
            for (var i = 0; i < count; i++)
            {
                if (facts[i] is { Kind: ConstraintKind.Contract, Contract.Contract: { } shape, Subject: { } subject })
                {
                    this.AddContractPremises(shape, subject, scope);
                }
            }
        }
        finally
        {
            Array.Clear(facts, 0, count);
        }
    }

    private void AddContractPremises(BoundContract shape, BoundType self, BindingScope scope)
    {
        var environment = scope.Constraints ??= new();
        AddClauses(shape);
        for (var i = 0; i < shape.Ancestors.Count; i++)
        {
            this.AddConstraintFact(environment, this.InternConstraint(new(ConstraintKind.Contract, self, contract: shape.Ancestors[i])), shape.Symbol);
            AddClauses(shape.Ancestors[i].Contract!);
        }

        void AddClauses(BoundContract declaration)
        {
            this.AddArithmeticPremises(declaration, self, environment, shape.Symbol);
            for (var i = 0; i < declaration.ClauseStorage.Count; i++)
            {
                if (declaration.ClauseStorage[i].BoundConstraint is { } constraint && DependentConstraint(constraint, contractSelf: true))
                {
                    this.AddConstraintFact(environment, this.ContractConstraint(constraint, scope, self, false, declaration.Symbol), shape.Symbol);
                }
            }
        }
    }
}
