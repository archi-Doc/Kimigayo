// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<BindingSymbol, byte> inheritedConformanceStates = new(ReferenceEqualityComparer.Instance);

    // Nominal bases supply early associated names; projected bases wait for associated specifications.
    // Each definition path is registered once, never rediscovered at a call site.
    private void RegisterInheritedConformances(bool complete = false)
    {
        if (!complete)
        {
            this.inheritedConformanceStates.Clear();
        }

        for (var i = 0; i < this.nodes.Count; i++)
        {
            if (this.nodes[i] is StructKoto { BoundSymbol: { } symbol })
            {
                Register(symbol);
            }
        }

        void Register(BindingSymbol type)
        {
            if (this.inheritedConformanceStates.GetValueOrDefault(type) != 0)
            {
                return;
            }

            this.inheritedConformanceStates[type] = 1;

            if (type.Declaration is not StructKoto { Bases.Count: 1 } declaration)
            {
                this.inheritedConformanceStates[type] = 2;
                return;
            }

            var syntax = declaration.Bases[0];
            if (!complete && this.TypeName(syntax, this.scopes[declaration], false)?.Declaration is not StructKoto)
            {
                this.inheritedConformanceStates[type] = 0;
                return;
            }

            var written = syntax.BoundType ?? this.BindType(syntax, this.scopes[declaration]);
            if (written is null || this.StoredType(written, this.SelfType(type)) is not { Symbol: { } parent } baseType)
            {
                return;
            }

            Register(parent);
            if (this.inheritedConformanceStates.GetValueOrDefault(parent) != 2)
            {
                this.inheritedConformanceStates[type] = 0;
                return;
            }

            if (!this.conformancesByType.TryGetValue(parent, out var identities))
            {
                this.inheritedConformanceStates[type] = 2;
                return;
            }

            for (var i = 0; i < identities.Count; i++)
            {
                var identity = identities[i];
                if (identity.Contract.Intrinsic != IntrinsicKind.None)
                {
                    continue; // Copy, Owned and other intrinsic capabilities retain their own derivation rules.
                }

                var contract = Substitute(identity.Contract);
                for (var j = 0; j < identity.Paths.Count; j++)
                {
                    var source = identity.Paths[j];
                    var path = this.RegisterConformance(type, contract, Substitute(source.RootContract), source.Declaration, this.scopes[declaration], source.Premises);
                    path.Use = syntax;
                    if (path.InheritedFrom is null)
                    {
                        path.InheritedFrom = source;
                    }
                    else if (!ReferenceEquals(path.InheritedFrom, source))
                    {
                        var sources = path.AdditionalInheritedSources ??= new();
                        if (!sources.Contains(source))
                        {
                            sources.Add(source);
                        }
                    }

                    path.InheritedBase = baseType;
                }
            }

            this.inheritedConformanceStates[type] = 2;

            BindingSymbol Substitute(BindingSymbol contract)
                => IsBoundContractReference(contract) ? this.BoundContractReference(this.StoredType(contract.Type!, baseType)!) : contract;
        }
    }

    private ConstraintProof VerifyInheritedConformance(BoundConformancePath path)
    {
        if (path.Checking)
        {
            return ConstraintProof.Unknown;
        }

        if (path.Invalid || path.Identity.Invalid || InvalidDeclarationContext(path.Type.Declaration))
        {
            return ConstraintProof.Error;
        }

        if (path.IsVerified)
        {
            return ConstraintProof.Proven;
        }

        path.Checking = true;
        this.conformanceChecks++;
        path.RejectedSelfSignature = null;
        path.WitnessStorage.Clear();
        path.WitnessMap.Clear();
        path.PropertyWitnessStorage.Clear();
        path.PropertyWitnessMap.Clear();
        var completed = false;
        try
        {
            var proof = ConstraintProof.Proven;
            for (var i = 0; i < path.InheritedSourceCount; i++)
            {
                proof = CombineProof(proof, this.VerifyConformance(path.InheritedSource(i)), true);
            }

            if (proof != ConstraintProof.Proven)
            {
                return proof;
            }

            var self = this.SelfType(path.Type);
            var scope = path.Scope;
            scope.Reset();
            this.AddInheritedPremises(path, self, scope);
            var shape = path.Contract.Contract!;
            foreach (var ancestor in shape.AncestorStorage)
            {
                var inheritedProof = ancestor.Intrinsic != IntrinsicKind.None
                    ? this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, self, contract: ancestor)), scope)
                    : this.VerifyConformance(this.conformancePaths[(path.Type, ancestor, path.Declaration, path.RootContract)]);
                proof = CombineProof(proof, inheritedProof, true);
            }

            foreach (var associated in shape.AssociatedStorage)
            {
                if (this.ResolveAssociated(path, associated) is not { } binding)
                {
                    return ConstraintProof.Unknown;
                }

                path.AssociatedStorage[associated] = binding;
                if (!TypeAccessCovers(binding, path.Type, path.Contract))
                {
                    return ConstraintProof.Refuted;
                }
            }

            foreach (var clause in shape.ClauseStorage)
            {
                if (clause.BoundConstraint is not { } constraint)
                {
                    return ConstraintProof.Unknown;
                }

                proof = CombineProof(proof, this.ProveConstraint(this.ContractConstraint(constraint, scope, self), scope), true);
            }

            for (var s = 0; s < path.InheritedSourceCount; s++)
            {
                foreach (var witness in path.InheritedSource(s).WitnessStorage)
                {
                    var identity = new BoundRequirement(witness.Requirement, this.SubstituteRequirementContract(witness.Identity.Contract, path.InheritedBase!));
                    if (path.WitnessMap.TryGetValue(identity, out var previous))
                    {
                        // The same exact requirement and implementation on this one inline base path have the same
                        // substituted signature, receiver correspondence and Origin map. Every source was verified above.
                        if (!ReferenceEquals(previous.Implementation, witness.Implementation))
                        {
                            return ConstraintProof.Refuted;
                        }

                        continue;
                    }

                    if (!ReferenceEquals(identity.Contract, path.Contract))
                    {
                        var ancestor = this.conformancePaths[(path.Type, identity.Contract, path.Declaration, path.RootContract)];
                        if (!CopyRequirementWitness(ancestor, path, identity))
                        {
                            proof = CombineProof(proof, ConstraintProof.Unknown, true);
                        }
                        else if (!ReferenceEquals(path.WitnessMap[identity].Implementation, witness.Implementation))
                        {
                            return ConstraintProof.Refuted;
                        }

                        continue;
                    }

                    var selection = this.InheritedWitnessSelection(self, witness.Implementation);
                    if (selection.Pending)
                    {
                        return ConstraintProof.Unknown;
                    }

                    if (witness.Requirement.Property is { } property)
                    {
                        proof = CombineProof(proof, this.VerifyPropertyRequirement(path, property, self, scope, selection), true);
                        continue;
                    }

                    if (witness.Requirement.Declaration is not FunctionKoto requirement || witness.Implementation.Declaration is not FunctionKoto implementation)
                    {
                        return ConstraintProof.Unknown;
                    }

                    var matches = this.MatchesRequirement(requirement, implementation, self, scope, selection);
                    if (matches != true)
                    {
                        // Object receiver projection is a separate implementation limit, not evidence of a Self mismatch.
                        var receiver = requirement.BoundSymbol!.ReceiverIndex;
                        path.RejectedSelfSignature = matches == false && (receiver < 0 || requirement.Parameters[receiver].Type.BoundType?.Semantics is not (SemanticsKind.ObjRef or SemanticsKind.ObjUniq)) ? witness.Implementation : null;
                        return matches is null ? ConstraintProof.Unknown : ConstraintProof.Refuted;
                    }

                    var compatible = this.CompatibleRequirement(path, requirement, implementation, self, scope, selection);
                    // Incompatibility of the retained mapping rules out this path, not the derived declaration.
                    proof = CombineProof(proof, compatible == ConstraintProof.Error ? ConstraintProof.Refuted : compatible, true);
                    var inherited = new BoundWitness(identity, witness.Implementation, this.FunctionWitness(path, identity));
                    path.WitnessStorage.Add(inherited);
                    path.WitnessMap.Add(identity, inherited);
                }
            }

            proof = UnsupportedProof(path, proof);
            path.IsVerified = proof == ConstraintProof.Proven;
            completed = true;
            return proof;
        }
        finally
        {
            path.Checking = false;
            path.Unsupported &= completed; // An interrupted verification records no limit.
            this.conformanceChecks--;
        }
    }

    private void AddInheritedPremises(BoundConformancePath path, BoundType self, BindingScope scope)
    {
        if (path.InheritedFrom is { } source)
        {
            // Registration merges only paths with the same original declaration, root Contract and inline base.
            // Additional sources therefore carry the same declaration premises; their mapping obligations are
            // verified separately in VerifyInheritedConformance and ProveConformanceConditions.
            this.AddInheritedPremises(source, this.StoredType(path.InheritedBase!, self)!, scope);
        }
        else if (path.Premises is { } premises)
        {
            var environment = scope.Constraints ??= new();
            foreach (var clause in premises.Operands)
            {
                if (((IsKoto)clause).BoundConstraint is { } constraint)
                {
                    this.AddConstraintFact(environment, this.SubstituteConstraint(constraint, path.Type.Declaration, (BoundType[])self.Components));
                }
            }

            this.ExpandScopeContractPremises(scope);
        }
    }

    private MemberSelection InheritedWitnessSelection(BoundType self, BindingSymbol implementation)
    {
        BoundMemberPath? path = null;
        for (var type = self; type.Symbol is { } symbol;)
        {
            if (ReferenceEquals(symbol.Declaration, implementation.Scope.Owner))
            {
                return new(implementation, type, path);
            }

            if (symbol.Declaration is not StructKoto { Bases.Count: 1 } declaration || this.StoredBase(type) is not { } parent)
            {
                break;
            }

            path = this.MemberPath(path, declaration.Bases[0], parent);
            type = parent;
        }

        return new(null, null, null, Pending: true);
    }

    private string? InheritedSelfMismatchNote(BoundConstraint constraint)
    {
        // SPEC 8.4.4: a refuted and/or keeps the cause of a refuted operand; a negated operand's failure is no such cause.
        if (constraint.Kind is ConstraintKind.And or ConstraintKind.Or)
        {
            return this.InheritedSelfMismatchNote(constraint.Left!) ?? this.InheritedSelfMismatchNote(constraint.Right!);
        }

        if (constraint is not { Kind: ConstraintKind.Contract, Subject: { Symbol: { } owner } self, Contract: { } contract } || !this.conformancesByType.TryGetValue(owner, out var identities))
        {
            return null;
        }

        foreach (var identity in identities)
        {
            if (!ReferenceEquals(identity.Contract, contract) && !(IsBoundContractReference(contract) && identity.Contract.Type is { } reference && ReferenceEquals(this.StoredType(reference, self), contract.Type)))
            {
                continue;
            }

            foreach (var path in identity.PathStorage)
            {
                if (path.InheritedFrom is not null && path.RejectedSelfSignature is { } implementation)
                {
                    var declaring = implementation.Scope.Owner.BoundSymbol!.Name;
                    return $"The retained implementation {declaring}.{implementation.Name} keeps {declaring} as Self, so its input signature does not match Self = {DiagnosticTypeName(self)}. Only a borrowed receiver may project to a base subobject; other inputs and owning receivers do not convert";
                }
            }
        }

        return null;
    }
}
