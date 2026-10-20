// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// The computation behind TyCtxt.RecursiveBase, under its contract (TyCtxt.RecursiveContracts.cs): the pass's recursive Contract
// declarations, each found by expanding the premise supply of `T is C` in a pooled scope, and the proof of a projection's bases against
// them under a definition's effect premises.
public sealed partial class Binding
{
    // The pass's recursive Contract declarations, valid while recursiveVersion is the pass's version; the search's scope and the
    // (declaration, Type) pairs it expanded, reused.
    private readonly List<BindingSymbol> recursiveContracts = [];
    private readonly HashSet<(BindingSymbol Declaration, BoundType Subject)> suppliedExpansions = [];
    private BindingScope? supplyScope;
    private ulong recursiveVersion;

    // SPEC 15.6.4: whether one of a projection's bases is proven by a definition's effect premises to conform to a recursive Contract.
    internal bool RecursiveBase(BindingSymbol? definition, BoundType type)
    {
        if (type.Kind != BoundTypeKind.AssociatedProjection || this.RecursiveContracts().Count == 0)
        {
            return false;
        }

        var scope = this.EffectPremises(definition);
        for (var b = type.Components[0]; ; b = b.Components[0])
        {
            foreach (var contract in this.recursiveContracts)
            {
                if (this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, b, contract: contract)), scope) == ConstraintProof.Proven)
                {
                    return true;
                }
            }

            if (b.Kind != BoundTypeKind.AssociatedProjection)
            {
                return false;
            }
        }
    }

    // Whether a Type is the search's T or a projection of it, each base an associated projection down to T.
    private static bool OfSearchSelf(BoundType type, BoundType self)
    {
        while (type.Kind == BoundTypeKind.AssociatedProjection)
        {
            type = type.Components[0];
        }

        return ReferenceEquals(type, self);
    }

    // The pass's recursive Contract declarations, found on its first query (stamped with the pass's version, as its other memos are).
    private List<BindingSymbol> RecursiveContracts()
    {
        if (this.recursiveVersion != this.storageVersion)
        {
            this.recursiveContracts.Clear();
            foreach (var shape in this.contractShapes)
            {
                if (this.IsRecursive(shape))
                {
                    this.recursiveContracts.Add(shape.Symbol);
                }
            }

            this.recursiveVersion = this.storageVersion;
        }

        return this.recursiveContracts;
    }

    // SPEC 8.3.3: whether `T is C` supplies (SPEC 8.3.2), directly or through the Constraints of the Contracts so supplied, a conformance of
    // a projection of T to C's declaration or a refinement of it. The supply's facts are a worklist: each available conformance of T or a
    // projection of T is expanded in turn, unless its declaration was expanded at that Type or at one of its bases. An `or` Constraint is
    // one fact of kind Or and supplies nothing.
    private bool IsRecursive(BoundContract shape)
    {
        var scope = this.supplyScope ??= new(this.Library.Kotonoha.RootKoto) { Constraints = new() };
        var environment = scope.Constraints!;
        var self = this.ContractSelfType(shape.Symbol);
        environment.Reset();
        this.suppliedExpansions.Clear();
        this.suppliedExpansions.Add((shape.Symbol, self));
        this.AddContractPremises(shape, self, scope);
        for (var i = 0; i < environment.DerivedFacts.Count; i++)
        {
            var fact = environment.DerivedFacts[i].Fact;
            if (fact is not { Kind: ConstraintKind.Contract, Contract: { Contract: { } supplied } contract, Subject: { } subject } ||
                !OfSearchSelf(subject, self) || !this.AvailableConstraintFact(environment, fact))
            {
                continue;
            }

            if (!ReferenceEquals(subject, self) && IsRefinement(contract, shape.Symbol))
            {
                return true;
            }

            if (this.FirstExpansion(contract.Declaration.BoundSymbol!, subject))
            {
                this.AddContractPremises(supplied, subject, scope);
            }
        }

        return false;
    }

    // Records the expansion of a declaration at a Type of the search, unless it was expanded at that Type or at one of its bases (a chain
    // that repeats a declaration, which makes that declaration recursive itself).
    private bool FirstExpansion(BindingSymbol declaration, BoundType subject)
    {
        for (var b = subject; ; b = b.Components[0])
        {
            if (this.suppliedExpansions.Contains((declaration, b)))
            {
                return false;
            }

            if (b.Kind != BoundTypeKind.AssociatedProjection)
            {
                return this.suppliedExpansions.Add((declaration, subject));
            }
        }
    }
}
