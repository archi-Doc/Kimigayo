// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(BindingSymbol Type, BindingSymbol Contract), BoundConformance> conformances = new();
    private readonly Dictionary<(BindingSymbol Type, BindingSymbol Contract, IsKoto Declaration, BindingSymbol Root), BoundConformancePath> conformancePaths = new();
    private readonly List<BoundConformancePath> activeConformancePaths = new();
    private readonly Dictionary<BindingSymbol, List<BoundConformance>> conformancesByType = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BoundType, BoundType> associatedIdentityChecks = new(ReferenceEqualityComparer.Instance);
    private readonly List<(Koto Use, BoundType Type, BindingSymbol Contract)> projectionUses = new();
    private BoundConstraint[] contractFactScratch = [];
    private bool contractHeadersReady;
    private bool bindingConstraintTypes;

    /// <summary>Gets legacy unconditional definition metadata. Use ResolveConformance for use-site evidence.</summary>
    /// <param name="type">The nominal or constructed conforming Type.</param>
    /// <param name="contract">The required Contract identity.</param>
    /// <returns>The verified unconditional definition, or null. Type-formation prerequisites are not discharged.</returns>
    public BoundConformance? GetConformance(BoundType type, BindingSymbol contract)
        => type.Symbol is { } symbol && this.conformances.TryGetValue((symbol, contract), out var result) && result.UnconditionalPath is not null ? result : null;

    /// <summary>Gets definition metadata, without asserting availability for any substitution.</summary>
    /// <param name="type">The nominal or constructed Type identifying the declaration.</param>
    /// <param name="contract">The Contract declaration identity.</param>
    /// <returns>The registered definition and paths, or null if absent.</returns>
    public BoundConformance? GetConformanceDefinition(BoundType type, BindingSymbol contract)
        => type.Symbol is { } symbol && this.conformances.TryGetValue((symbol, contract), out var result) && result.Paths.Count != 0 ? result : null;

    /// <summary>Proves use-site availability and returns a definition-verified evidence path.</summary>
    /// <param name="type">The actual Type, including its generic arguments.</param>
    /// <param name="contract">The required Contract identity.</param>
    /// <param name="context">The use site supplying lexical proof assumptions.</param>
    /// <param name="path">A verified definition path only when the judgment is Proven; its Types retain declaration slots.</param>
    /// <returns>The four-valued use-site proof result.</returns>
    public ConstraintProof ResolveConformance(BoundType type, BindingSymbol contract, Koto context, out BoundConformancePath? path)
        => this.ResolveConformance(type, contract, this.ConstraintScope(context), out path);

    private static bool IsRefinement(BindingSymbol child, BindingSymbol parent)
        => ReferenceEquals(child, parent) || (child.Contract is { } shape && shape.AncestorStorage.Contains(parent));

    private static bool ConstraintAccessCovers(BoundConstraint constraint, BindingSymbol contract)
    {
        if (constraint.Kind is ConstraintKind.And or ConstraintKind.Or or ConstraintKind.Not)
        {
            return ConstraintAccessCovers(constraint.Left!, contract) && (constraint.Right is null || ConstraintAccessCovers(constraint.Right, contract));
        }

        return (constraint.Contract is null || AccessCovers(constraint.Contract, contract, contract)) && (constraint.RequiredType is null || TypeAccessCovers(constraint.RequiredType, contract, contract));
    }

    private void ResetContracts()
    {
        this.contractHeadersReady = false;
        this.bindingConstraintTypes = false;
        this.activeConformancePaths.Clear();
        foreach (var list in this.conformancesByType.Values)
        {
            list.Clear();
        }

        this.projectionUses.Clear();
        this.memberSelections.Clear();
        foreach (var group in this.requirementGroups.Values)
        {
            group.Active = false;
        }

        foreach (var binding in this.associatedBindings.Values)
        {
            binding.Candidates.Clear();
            binding.Result = null;
            binding.State = 0;
        }

        foreach (var conformance in this.conformances.Values)
        {
            conformance.IsVerified = false;
            conformance.Invalid = false;
            conformance.DirectClause = null;
            conformance.PathStorage.Clear();
        }

        foreach (var conformance in this.conformancePaths.Values)
        {
            conformance.Active = false;
            conformance.IsVerified = false;
            conformance.Checking = false;
            conformance.Invalid = false;
            conformance.WitnessStorage.Clear();
            conformance.WitnessMap.Clear();
            conformance.PropertyWitnessStorage.Clear();
            conformance.PropertyWitnessMap.Clear();
            conformance.AssociatedStorage.Clear();
            conformance.Scope.Reset();
        }
    }

    private void PrepareContracts()
    {
        this.Core.Copy.Contract ??= new(this.Core.Copy) { State = 2 };
        this.Core.Owned.Contract ??= new(this.Core.Owned) { State = 2 };
        for (var n = 0; n < this.nodes.Count; n++)
        {
            if (this.nodes[n] is not ContractKoto contract)
            {
                continue;
            }

            var shape = contract.BoundSymbol!.Contract ??= new(contract.BoundSymbol);
            shape.State = 0;
            shape.AncestorStorage.Clear();
            shape.RequirementStorage.Clear();
            shape.AssociatedStorage.Clear();
            shape.ClauseStorage.Clear();
            shape.Seen.Clear();
            foreach (var members in shape.MembersByName.Values)
            {
                members.Clear();
            }

            for (var i = 0; i < contract.Members.Count; i++)
            {
                if (contract.Members[i] is SyntaxFormKoto { Akind: KotoKind.AssociatedType, Operands.Length: 1 } declaration && declaration.Operands[0] is IdentifierNameKoto name)
                {
                    this.DeclareAssociatedType(declaration, name, contract);
                }
            }

            for (var i = 0; i < contract.ConstraintNodes.Count; i++)
            {
                var clause = contract.ConstraintNodes[i];
                if (clause.IsAssociatedConstraint && clause.Left is IdentifierNameKoto name)
                {
                    this.DeclareAssociatedType(clause, name, contract);
                }
            }
        }

        for (var n = 0; n < this.nodes.Count; n++)
        {
            if (this.nodes[n] is ContractKoto contract)
            {
                this.BuildContract(contract.BoundSymbol!.Contract!);
            }
        }
    }

    private void DeclareAssociatedType(Koto declaration, IdentifierNameKoto name, ContractKoto contract)
    {
        var symbol = this.Declare(declaration, name.IdentifierName, BindingSymbolKind.AssociatedType, declaration, this.scopes[contract]);
        symbol.Type = this.InternType(BoundTypeKind.AssociatedProjection, symbol, SemanticsKind.Owner, [contract.BoundSymbol!.Type!]);
        name.BoundSymbol = symbol;
        Complete(name, symbol.Type);
    }

    private bool BuildContract(BoundContract shape)
    {
        var contract = (ContractKoto)shape.Symbol.Declaration;
        if (shape.State != 0)
        {
            if (shape.State == 1)
            {
                Fail(contract, BindingFailure.Cycle);
                return false;
            }

            return shape.State == 2;
        }

        shape.State = 1;
        var valid = contract.BindingState != BindingState.Invalid;
        for (var i = 0; i < contract.Bases.Count; i++)
        {
            var syntax = contract.Bases[i];
            var parent = this.TypeName(syntax, this.scopes[contract], false);
            if (parent?.Declaration is not ContractKoto || syntax is GenericsKoto || parent.Contract is not { } inherited)
            {
                Fail(syntax, BindingFailure.InvalidConstraint);
                valid = false;
                continue;
            }

            syntax.BoundSymbol = parent;
            Complete(syntax, BoundType.Unit);
            if (!this.BuildContract(inherited))
            {
                valid = false;
                continue;
            }

            Add(parent, shape.AncestorStorage);
            for (var j = 0; j < inherited.Ancestors.Count; j++)
            {
                Add(inherited.Ancestors[j], shape.AncestorStorage);
            }

            for (var j = 0; j < inherited.Requirements.Count; j++)
            {
                Add(inherited.Requirements[j], shape.RequirementStorage);
            }

            for (var j = 0; j < inherited.AssociatedTypes.Count; j++)
            {
                Add(inherited.AssociatedTypes[j], shape.AssociatedStorage);
            }
        }

        for (var i = 0; i < contract.Members.Count; i++)
        {
            var member = contract.Members[i];
            if (member is FunctionKoto { IsRequirement: true } or PropertyKoto { IsContractRequirement: true })
            {
                Add(member.BoundSymbol!, shape.RequirementStorage);
            }
            else if (member is SyntaxFormKoto { Akind: KotoKind.AssociatedType } && member.BoundSymbol is { } associated)
            {
                Add(associated, shape.AssociatedStorage);
                Complete(member, BoundType.Unit);
            }
            else
            {
                Fail(member, BindingFailure.InvalidConstraint);
                valid = false;
            }
        }

        for (var i = 0; i < contract.ConstraintNodes.Count; i++)
        {
            var clause = contract.ConstraintNodes[i];
            shape.ClauseStorage.Add(clause);
            if (clause.IsAssociatedConstraint && clause.BoundSymbol is { Kind: BindingSymbolKind.AssociatedType } associated)
            {
                Add(associated, shape.AssociatedStorage);
            }
        }

        shape.State = valid ? (byte)2 : (byte)3;
        for (var i = 0; i < shape.Requirements.Count; i++)
        {
            var member = shape.Requirements[i];
            if (!shape.MembersByName.TryGetValue(member.Name, out var list))
            {
                shape.MembersByName.Add(member.Name, list = new());
            }

            list.Add(member);
        }

        if (!valid)
        {
            Fail(contract, BindingFailure.InvalidConstraint);
        }

        return valid;

        void Add(BindingSymbol symbol, List<BindingSymbol> destination)
        {
            if (shape.Seen.Add(symbol))
            {
                destination.Add(symbol);
            }
        }
    }

    private void RegisterConformances()
    {
        for (var n = 0; n < this.nodes.Count; n++)
        {
            if (this.nodes[n] is not DeclarationContainerKoto container || container is not (StructKoto or EnumKoto))
            {
                continue;
            }

            for (var i = 0; i < container.ConstraintNodes.Count; i++)
            {
                var clause = container.ConstraintNodes[i];
                if (clause.Left is IdentifierNameKoto { IdentifierName: "Self" } && clause.BoundConstraint is { } constraint)
                {
                    Register(constraint, container.BoundSymbol!, clause);
                }
            }
        }

        void Register(BoundConstraint constraint, BindingSymbol type, IsKoto use)
        {
            if (constraint.Kind == ConstraintKind.And)
            {
                Register(constraint.Left!, type, use);
                Register(constraint.Right!, type, use);
            }
            else if (constraint is { Kind: ConstraintKind.Contract, Contract: { Intrinsic: IntrinsicKind.None or IntrinsicKind.Copy or IntrinsicKind.Owned, Contract: not null } contract })
            {
                this.RegisterConformanceDeclaration(type, contract, use, this.scopes[type.Declaration], null);
            }
        }
    }

    private void RegisterConformanceDeclaration(BindingSymbol type, BindingSymbol contract, IsKoto use, BindingScope scope, SyntaxFormKoto? premises)
    {
        var direct = this.RegisterConformance(type, contract, contract, use, scope, premises);
        if (direct.Identity.DirectClause is { } previous)
        {
            direct.Identity.Invalid = true;
            Fail(use, BindingFailure.Duplicate);
            Fail(previous, BindingFailure.Duplicate);
        }

        direct.Identity.DirectClause = use;
        var shape = contract.Contract!;
        for (var i = 0; i < shape.Ancestors.Count; i++)
        {
            this.RegisterConformance(type, shape.Ancestors[i], contract, use, scope, premises);
        }
    }

    private BoundConformancePath RegisterConformance(BindingSymbol type, BindingSymbol contract, BindingSymbol root, IsKoto use, BindingScope scope, SyntaxFormKoto? premises)
    {
        if (!this.conformances.TryGetValue((type, contract), out var identity))
        {
            this.conformances.Add((type, contract), identity = new(type, contract));
        }

        if (!this.conformancePaths.TryGetValue((type, contract, use, root), out var result))
        {
            this.conformancePaths.Add((type, contract, use, root), result = new(type, contract) { Identity = identity });
        }

        if (!result.Active)
        {
            result.Active = true;
            result.Use = use;
            result.Declaration = use;
            result.RootContract = root;
            result.RootPath = ReferenceEquals(contract, root) ? result : this.conformancePaths[(type, root, use, root)];
            result.Premises = premises;
            result.Scope.Parent = scope;
            this.activeConformancePaths.Add(result);
            if (identity.PathStorage.Count == 0)
            {
                if (!this.conformancesByType.TryGetValue(type, out var list))
                {
                    this.conformancesByType.Add(type, list = new());
                }

                list.Add(identity);
            }

            identity.PathStorage.Add(result);
        }

        return result;
    }

    private ConstraintProof ProveConformance(BoundType type, BindingSymbol contract, BindingScope scope)
    {
        // Refinement assumptions are input evidence, not in-progress registrations.
        var premise = ConstraintProof.Unknown;
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints is not { Invalid: false } environment)
            {
                continue;
            }

            foreach (var fact in environment.Facts)
            {
                if (fact.Kind == ConstraintKind.Contract && ReferenceEquals(fact.Subject, type) && IsRefinement(fact.Contract!, contract))
                {
                    premise = ConstraintProof.Proven;
                }
            }
        }

        if (type.Symbol?.Declaration is ContractKoto own && IsRefinement(own.BoundSymbol!, contract))
        {
            premise = ConstraintProof.Proven;
        }

        var proof = this.ResolveConformance(type, contract, scope, out _);
        return premise == ConstraintProof.Proven ? CombineProof(premise, proof, false) : proof;
    }

    private BoundConstraint ContractConstraint(BoundConstraint constraint, BindingScope scope, BoundType self, bool normalize = true)
    {
        if (constraint.Kind == ConstraintKind.Not)
        {
            return this.NegateConstraint(this.ContractConstraint(constraint.Left!, scope, self, normalize));
        }

        if (constraint.Kind is ConstraintKind.And or ConstraintKind.Or)
        {
            return this.InternConstraint(new(constraint.Kind, left: this.ContractConstraint(constraint.Left!, scope, self, normalize), right: this.ContractConstraint(constraint.Right!, scope, self, normalize)));
        }

        return constraint.Subject is null ? constraint : this.InternConstraint(new(constraint.Kind, this.ContractType(constraint.Subject, scope, self, normalize), constraint.RequiredType is { } required ? this.ContractType(required, scope, self, normalize) : null, constraint.Contract, constraint.Mask));
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
                this.AddContractPremises(contract.BoundSymbol!.Contract!, contract.BoundSymbol.Type!, scope);
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
            this.AddConstraintFact(environment, this.InternConstraint(new(ConstraintKind.Contract, self, contract: shape.Ancestors[i])));
            AddClauses(shape.Ancestors[i].Contract!);
        }

        void AddClauses(BoundContract declaration)
        {
            for (var i = 0; i < declaration.ClauseStorage.Count; i++)
            {
                if (declaration.ClauseStorage[i].BoundConstraint is { } constraint)
                {
                    this.AddConstraintFact(environment, this.ContractConstraint(constraint, scope, self, false));
                }
            }
        }
    }

    private void ValidateConformances(BindingMode mode, bool final)
    {
        this.contractHeadersReady = true;
        if (final)
        {
            foreach (var identity in this.conformances.Values)
            {
                identity.IsVerified = false;
            }

            // Body/header validation may invalidate an earlier declaration-side witness.
            for (var i = 0; i < this.activeConformancePaths.Count; i++)
            {
                this.activeConformancePaths[i].IsVerified = false;
            }
        }

        for (var i = 0; i < this.activeConformancePaths.Count; i++)
        {
            var conformance = this.activeConformancePaths[i];
            var proof = this.VerifyConformance(conformance);
            if (final)
            {
                this.RequireConstraint(conformance.Use, proof, mode);
            }
        }

        if (final)
        {
            foreach (var identity in this.conformances.Values)
            {
                if (identity.PathStorage.Count != 0)
                {
                    this.RequireConstraint(identity.PathStorage[0].Use, this.VerifyConformanceAgreement(identity), mode);
                }
            }

            for (var i = 0; i < this.projectionUses.Count; i++)
            {
                var use = this.projectionUses[i];
                this.RequireConstraint(use.Use, this.ProveConformance(use.Type, use.Contract, this.ConstraintScope(use.Use)), mode);
            }
        }
    }

    private void ValidateContractDeclarations()
    {
        for (var n = 0; n < this.nodes.Count; n++)
        {
            if (this.nodes[n] is not (FunctionKoto or DeclarationContainerKoto))
            {
                continue;
            }

            var scope = this.scopes[this.nodes[n]];
            if (scope.Constraints is { } environment)
            {
                this.associatedIdentityChecks.Clear();
                foreach (var a in environment.Facts)
                {
                    if (a is not { Kind: ConstraintKind.TypeIdentity, Subject.Kind: BoundTypeKind.AssociatedProjection, RequiredType: { } ta } || DependentType(ta))
                    {
                        continue;
                    }

                    if (this.associatedIdentityChecks.TryGetValue(a.Subject, out var previous) && !ReferenceEquals(ta, previous))
                    {
                        environment.Invalid = true;
                        Fail(scope.Owner, BindingFailure.InvalidAssociatedType);
                    }

                    this.associatedIdentityChecks[a.Subject] = ta;
                }
            }

            if (scope.Owner is not ContractKoto contract || contract.BoundSymbol!.Contract is not { } shape)
            {
                continue;
            }

            for (var i = 0; i < shape.Ancestors.Count; i++)
            {
                if (!AccessCovers(shape.Ancestors[i], shape.Symbol, shape.Symbol))
                {
                    Fail(contract, BindingFailure.Access);
                }
            }

            foreach (var members in shape.MembersByName.Values)
            {
                for (var i = 0; i < members.Count; i++)
                {
                    if (members[i].Declaration is not FunctionKoto a)
                    {
                        continue;
                    }

                    var declaringContract = members[i].Scope.Owner.BoundSymbol!;
                    if (a.BoundSymbol!.Type is { } result && !TypeAccessCovers(result, declaringContract, declaringContract))
                    {
                        Fail(a, BindingFailure.Access);
                    }

                    for (var p = 0; p < a.Parameters.Count; p++)
                    {
                        if (a.Parameters[p].Type.BoundType is { } parameter && !TypeAccessCovers(parameter, declaringContract, declaringContract))
                        {
                            Fail(a, BindingFailure.Access);
                        }
                    }

                    for (var p = 0; p < a.TypeConstraints.Count; p++)
                    {
                        if (((IsKoto)a.TypeConstraints[p]).BoundConstraint is { } constraint && !ConstraintAccessCovers(constraint, declaringContract))
                        {
                            Fail(a, BindingFailure.Access);
                        }
                    }

                    for (var j = 0; j < i; j++)
                    {
                        if (members[j].Declaration is not FunctionKoto b || a.GenericArguments.Count != b.GenericArguments.Count || a.Parameters.Count != b.Parameters.Count)
                        {
                            continue;
                        }

                        var sameSignature = true;
                        var differentLabels = false;
                        for (var p = 0; p < a.Parameters.Count; p++)
                        {
                            if (a.Parameters[p].Type.BoundType is not { } ta || b.Parameters[p].Type.BoundType is not { } tb)
                            {
                                sameSignature = false;
                                break;
                            }

                            sameSignature &= SignatureEquals(this.ContractType(ta, scope), this.ContractType(tb, scope), a, b);
                            differentLabels |= a.Parameters[p].ExternalName != b.Parameters[p].ExternalName;
                        }

                        if (sameSignature && (differentLabels || !SameGenericShape(a, b)))
                        {
                            Fail(contract, BindingFailure.InvalidConstraint);
                        }
                    }
                }
            }

            for (var i = 0; i < shape.ClauseStorage.Count; i++)
            {
                if (shape.ClauseStorage[i].BoundConstraint is { } constraint && !ConstraintAccessCovers(constraint, shape.Symbol))
                {
                    Fail(shape.ClauseStorage[i], BindingFailure.Access);
                    Fail(contract, BindingFailure.Access);
                }
            }
        }
    }
}
