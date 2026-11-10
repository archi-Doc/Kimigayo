// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// InheritedConformances (PLAN G602; DeclQueryKind.InheritedConformances): a struct's inherited conformance paths (SPEC 8.6.2).
// - Responsibility: registering a struct's inherited paths, its base's under the base clause, once its base's are, with each path's
//   declared path and Self composed from its first source's (BoundConformancePath.DeclaredPath); the one by-type read of ImplTable
//   (ConformancesOf, ConformanceOf); verifying an inherited path and building its conformance scope.
// - Inputs: the struct's base clause read through DirectBase, the base's paths in ImplTable. Outputs: the paths in ImplTable.
// - Invariants: once clauses are bindable every by-type read demands the query first; a Done query has filled the table, and a read
//   after an abandoned demand sees it without the struct's inherited paths, which no final reading has given yet. Before, a read sees
//   what pass 0's sweep registered. Whether a reading of the clause is final is BaseLayers' answer (its not-ready count), never here.
// - Prohibitions: no record (ValidateBaseDeclaration and the verifications' callers own them); no read of a query state.
// - Removal: the accessors' clausesBindable condition, the pass-0 sweep and the not-ready path's read of BaseLayers' notReadyReads
//   go at R7-D2 d3 with R7-M9's temporaries.
public sealed partial class Binding
{
    private int inheritedSteps;
    private int inheritedPremiseSteps;

    /// <summary>Gets the clause readings the InheritedConformances query took in the latest pass, read by the scaling tests.</summary>
    internal int InheritedConformanceWork => this.inheritedSteps;

    /// <summary>Gets the steps that read an inherited path's declared premises (its scope's build and its conditions' proofs) in the latest
    /// pass, read by the scaling tests.</summary>
    internal int InheritedPremiseWork => this.inheritedPremiseSteps;

    // SPEC 8.6.2: every struct's inherited paths, demanded in node order. Pass 0's demand (BindConstraints) registers the paths through
    // bases named by a struct; this one, once clauses are bindable, the rest.
    private void RegisterInheritedConformances()
    {
        for (var i = 0; i < this.nodes.Count; i++)
        {
            if (this.nodes[i] is StructKoto { BoundSymbol: { } symbol })
            {
                _ = this.InheritedConformances(symbol);
            }
        }
    }

    // A Type's conformances in this pass: every by-type read of ImplTable. Once clauses are bindable a read demands the Type's inherited
    // paths first (DeclQueryKind.InheritedConformances); before, it reads what pass 0's demand registered.
    private List<BoundConformance>? ConformancesOf(BindingSymbol type)
    {
        if (this.clausesBindable)
        {
            _ = this.InheritedConformances(type);
        }

        return this.impls.OfType(type);
    }

    private BoundConformance? ConformanceOf(BindingSymbol type, BindingSymbol contract)
    {
        if (this.clausesBindable)
        {
            _ = this.InheritedConformances(type);
        }

        return this.impls.Find(type, contract);
    }

    // DeclQueryKind.InheritedConformances (SPEC 8.6.2): a struct's inherited paths, its base's paths under the base clause, registered
    // once its base's are. Not ready: a reading of the clause that moved BaseLayers' not-ready count, or a base whose own query is not
    // Done; abandoned. Re-entry (a base cycle): the base reads as not Done. Done with none: no base clause, a final reading that names no
    // struct, or a clause whose Type names none. Each reading of a clause counts in InheritedConformanceWork.
    private bool InheritedConformances(BindingSymbol type)
    {
        if (type.Declaration is not StructKoto declaration)
        {
            return true;
        }

        if (this.BeginQuery(type, DeclQueryKind.InheritedConformances) is not DeclState.NotStarted and var state)
        {
            return state == DeclState.Done;
        }

        if (declaration.BaseClause is not { } syntax)
        {
            this.CompleteQuery(type, DeclQueryKind.InheritedConformances);
            return true;
        }

        // The clause is read by DirectBase, the one reading (R7-M9). A read that moved its not-ready count (a clause in progress or not
        // ready) stores nothing and is read again at the next demand, so no heir registers paths through a clause in progress; a read
        // naming no struct without moving it is final.
        this.inheritedSteps++;
        var reads = this.notReadyReads;
        var direct = this.DirectBase(declaration);
        if (reads != this.notReadyReads)
        {
            this.AbandonQuery(type, DeclQueryKind.InheritedConformances);
            return false;
        }

        if (direct?.Declaration is not StructKoto)
        {
            this.CompleteQuery(type, DeclQueryKind.InheritedConformances);
            return true;
        }

        var written = syntax.BoundType ?? this.BindType(syntax, this.scopes[declaration]);
        if (written is null || this.StoredType(written, this.SelfType(type)) is not { Symbol: { } parent } baseType)
        {
            this.CompleteQuery(type, DeclQueryKind.InheritedConformances);
            return true;
        }

        if (!this.InheritedConformances(parent))
        {
            this.AbandonQuery(type, DeclQueryKind.InheritedConformances);
            return false;
        }

        if (this.impls.OfType(parent) is not { } identities)
        {
            this.CompleteQuery(type, DeclQueryKind.InheritedConformances);
            return true;
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
                var path = this.impls.Register(type, contract, Substitute(source.RootContract), source.Declaration, this.scopes[declaration], source.Premises);
                path.Use = syntax;
                if (path.InheritedFrom is null)
                {
                    // The declared path and Self from the first source's (BoundConformancePath.DeclaredPath).
                    path.InheritedFrom = source;
                    var declared = source.InheritedFrom is null;
                    path.DeclaredPath = declared ? source : source.DeclaredPath;
                    path.DeclaredSelf = declared ? baseType : source.DeclaredSelf is { } layer ? this.StoredType(layer, baseType) : null;
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

        this.CompleteQuery(type, DeclQueryKind.InheritedConformances);
        return true;

        BindingSymbol Substitute(BindingSymbol contract)
            => IsBoundContractReference(contract) ? this.BoundContractReference(this.StoredType(contract.Type!, baseType)!) : contract;
    }

    private ConstraintProof VerifyInheritedConformance(BoundConformancePath path)
    {
        if (path.Checking)
        {
            return ConstraintProof.Unknown;
        }

        if (path.Invalid || path.Identity.Invalid || this.InvalidDeclarationContext(path.Type.Declaration))
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

            if (this.RefusedConformance(path))
            {
                return ConstraintProof.Unknown; // No conformance scope: a refused declared Self (Binding.TypeHeight).
            }

            var self = this.ConformanceSelf(path);
            var scope = path.Scope; // D and the source's P, built once per pass (PrepareInheritedScopes).
            var shape = path.Contract.Contract!;
            foreach (var ancestor in shape.AncestorStorage)
            {
                var inheritedProof = ancestor.Intrinsic != IntrinsicKind.None
                    ? this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, self, contract: ancestor)), scope)
                    : this.VerifyConformance(this.impls.Path(path.Type, ancestor, path.Declaration, path.RootContract));
                proof = CombineProof(proof, inheritedProof, true);
            }

            foreach (var associated in shape.AssociatedStorage)
            {
                if (this.ResolveAssociated(path, associated, out _) is not { } binding)
                {
                    return ConstraintProof.Unknown;
                }

                path.AssociatedStorage[associated] = binding;
                if (!this.TypeAccessCovers(binding, path.Type, path.Contract))
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
                        var ancestor = this.impls.Path(path.Type, identity.Contract, path.Declaration, path.RootContract);
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
                        // Object receiver projection is a separate implementation limit of the conformance, as OCC-X is
                        // (UnsupportedProof), not evidence of a Self mismatch.
                        var receiver = requirement.BoundSymbol!.ReceiverIndex;
                        if (matches == false && receiver >= 0 && requirement.Parameters[receiver].Type.BoundType?.Semantics is SemanticsKind.ObjRef or SemanticsKind.ObjUniq)
                        {
                            path.Unsupported = true;
                            continue;
                        }

                        path.RejectedSelfSignature = matches == false ? witness.Implementation : null;
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

    // SPEC 8.8.2: each inherited path's conformance scope, the heir D and its premises P (Register copies the declared path's) at its
    // declared Self (BoundConformancePath.DeclaredPath), one step per path; none where that Self was refused. Built once per pass after
    // the table's reset (ImplTable.Reset empties it) and before its readers, the inline layouts' heir nodes and
    // VerifyInheritedConformance, which read it and never build it.
    private void PrepareInheritedScopes()
    {
        foreach (var path in this.impls.Paths)
        {
            if (path.InheritedFrom is null)
            {
                continue;
            }

            this.inheritedPremiseSteps++;
            if (path.Premises is not { } premises || path.DeclaredSelf is not { } self)
            {
                continue;
            }

            var environment = path.Scope.Constraints ??= new();
            foreach (var clause in premises.Operands)
            {
                if (((IsKoto)clause).BoundConstraint is { } constraint)
                {
                    this.AddConstraintFact(environment, this.StoredConstraint(constraint, self));
                }
            }

            this.ExpandScopeContractPremises(path.Scope);
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

            if (symbol.Declaration is not StructKoto { BaseClause: { } clause } || AdtDef.Base(type) is not { } parent)
            {
                break;
            }

            path = this.MemberPath(path, clause, parent);
            type = parent;
        }

        return new(null, null, null, Pending: true);
    }

    private string? InheritedSelfMismatchNote(BoundConstraint constraint)
    {
        // SPEC 8.6: a refuted and/or keeps the cause of a refuted operand; a negated operand's failure is no such cause.
        if (constraint.Kind is ConstraintKind.And or ConstraintKind.Or)
        {
            return this.InheritedSelfMismatchNote(constraint.Left!) ?? this.InheritedSelfMismatchNote(constraint.Right!);
        }

        if (constraint is not { Kind: ConstraintKind.Contract, Subject: { Symbol: { } owner } self, Contract: { } contract } || this.ConformancesOf(owner) is not { } identities)
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
