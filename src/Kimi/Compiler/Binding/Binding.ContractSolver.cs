// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// ContractSolver (PlanB §4.1; DECISIONS BU-29): premise derivation and evidence availability (SPEC 8.3): what the premises supply for a
// proposition and its negation (Supply) under the request's view (ViewOf, the one derivation point); until C3 the eager derivation too
// (ExpandContractPremises, ExpandScopeContractPremises, AddContractPremises over a Contract premise's supply, ContractSupply, which
// retention's handed signatures read too) and the availability of a fact (AvailableConstraintFact).
// - Inputs: the view's scope chain (ConstraintEnvironments), derived facts' source Contracts.
// - Outputs: Evidence (Positive, Negative; Invalid, temporary until C3: the chain holds an Invalid environment).
// - Invariants: innermost scope first, each environment in its order; an Invalid environment answers Invalid to Supply; availability
//   is asked per fact, lazily, in the caller's order.
// - Prohibitions: no judgment, lookup, normalization or inference rule here; no mode or bool parameter; no answer kept across
//   requests; no second scheduler; no read sets. From C3 (C1 §5), broken until then by the eager derivation moved here: no Binding
//   writes, syntax-triggered supply, DerivedFacts or fallback scan.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private BoundConstraint[] contractFactScratch = [];

    // The premises a request reads: the scope its consumer holds. C3 adds the definition whose collection is active and the
    // inadmissible bindings here, in ViewOf (BU-29); a source's state and its check under way stay on the source (SourceStates, BU-59).
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

    // A Contract premise is available when the Contract and each ancestor (the deduplicated transitive closure) are valid sources: its
    // declaration context and its published source state (Binding.SourceStates).
    private bool AvailableContractPremise(BindingSymbol contract)
    {
        var ancestors = contract.Contract?.Ancestors;
        for (var i = -1; i < (ancestors?.Count ?? 0); i++)
        {
            var source = i < 0 ? contract : ancestors![i];
            if (this.InvalidDeclarationContext(source.Declaration) || UnresolvedTypeDeclarationContext(source.Declaration) ||
                this.ReadSourceState(source) != ConstraintProof.Proven)
            {
                return false;
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
        foreach (var (ancestor, declaration, premise) in new ContractSupply(this, shape, self, scope))
        {
            if (premise is not null)
            {
                this.AddConstraintFact(environment, premise, shape.Symbol);
                continue;
            }

            if (ancestor >= 0)
            {
                var inherited = this.InternConstraint(new(ConstraintKind.Contract, self, contract: shape.Ancestors[ancestor]));
                this.AddConstraintFact(environment, inherited, shape.Symbol);
            }

            this.AddArithmeticPremises(declaration, self, environment, shape.Symbol);
        }
    }

    // SPEC 8.3.2: what a Contract premise `self is shape` supplies from its declarations, the one enumeration of it, in the order
    // AddContractPremises adds it: the Contract, then each ancestor (Ancestor: -1, then the ancestor's index), each first as itself
    // (Premise null: its own facts, an ancestor's `self is A` and the arithmetic premises, AddContractPremises'), then each of its clause
    // constraints that depends on the Contract's Self (DependentConstraint) as `self` and `scope` make it (ContractConstraint, not
    // normalized), evaluated when reached. Read by AddContractPremises and by retention's handed signatures (HandedContract).
    private ref struct ContractSupply(Binding binding, BoundContract shape, BoundType self, BindingScope scope)
    {
        private int ancestor = -1;
        private int next; // The declaration's next item: 0 the declaration itself, i + 1 its clause i.

        public (int Ancestor, BoundContract Declaration, BoundConstraint? Premise) Current { get; private set; }

        public readonly ContractSupply GetEnumerator() => this;

        public bool MoveNext()
        {
            for (; this.ancestor < shape.Ancestors.Count; (this.ancestor, this.next) = (this.ancestor + 1, 0))
            {
                var declaration = this.ancestor < 0 ? shape : shape.Ancestors[this.ancestor].Contract!;
                if (this.next == 0)
                {
                    (this.next, this.Current) = (1, (this.ancestor, declaration, null));
                    return true;
                }

                for (; this.next <= declaration.ClauseStorage.Count; this.next++)
                {
                    if (declaration.ClauseStorage[this.next - 1].BoundConstraint is { } constraint &&
                        binding.DependentConstraint(constraint, contractSelf: true))
                    {
                        this.Current = (this.ancestor, declaration, binding.ContractConstraint(constraint, scope, self, false, declaration.Symbol));
                        this.next++;
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
