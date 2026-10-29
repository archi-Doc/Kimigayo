// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly ScratchBuffers<ConstraintProof> conformanceProofScratch = new();
    private readonly Dictionary<BoundType, BoundType> associatedIdentityChecks = new(ReferenceEqualityComparer.Instance);
    private readonly List<(Koto Use, BoundType Type, BindingSymbol Contract)> projectionUses = new();
    private readonly List<(ForKoto Loop, BoundType Subject, BoundType Iterator, BindingSymbol Entry)> userIterations = new();
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
    {
        if (ReferenceEquals(child, parent) || SatisfiesDeclaration(child, parent))
        {
            return true;
        }

        if (child.Contract is not { } shape)
        {
            return false;
        }

        for (var i = 0; i < shape.AncestorStorage.Count; i++)
        {
            if (ReferenceEquals(shape.AncestorStorage[i], parent) || SatisfiesDeclaration(shape.AncestorStorage[i], parent))
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 8.4.3, 8.6.1: a Contract named without Type arguments, as an associated Type's owner is, is satisfied by any
    // bound reference of its declaration.
    private static bool SatisfiesDeclaration(BindingSymbol candidate, BindingSymbol parent)
        => IsBoundContractReference(candidate) && !IsBoundContractReference(parent) && ReferenceEquals(candidate.Declaration, parent.Declaration);

    private static bool IsBoundContractReference(BindingSymbol contract)
        => contract.Type is { } reference && !ReferenceEquals(reference.Symbol, contract);

    private static bool IsRefinementName(Koto syntax)
        => syntax switch
        {
            TypeSemanticsKoto { IsTransparentWrapper: true, Type: { } inner, OriginName: null, OriginExpression: null, OriginArguments: null } => IsRefinementName(inner),
            IdentifierNameKoto or TypeSemanticsKoto { Type: null, SemanticsKind: SemanticsKind.Owner, SemanticsParameter: null, OriginName: null, OriginExpression: null, OriginArguments: null } => true,
            MemberAccessKoto member => IsRefinementName(member.Left) && IsRefinementName(member.Right),
            SyntaxFormKoto { Akind: KotoKind.RootName, Operands.Length: 1 } root => IsRefinementName(root.Operands[0]),
            GenericsKoto { Identifier: { } identifier } => IsRefinementName(identifier), // SPEC 8.4.3: a parent with Type arguments.
            _ => false,
        };

    // SPEC 8.4.3: the Type-argument application of a refinement parent, below transparent wrappers and a root-name form.
    private static GenericsKoto? GenericParentApplication(Koto name)
    {
        if (name is SyntaxFormKoto { Akind: KotoKind.RootName, Operands: [{ } rooted] })
        {
            name = rooted;
            while (name is TypeSemanticsKoto { IsTransparentWrapper: true, Type: { } inner, OriginName: null, OriginExpression: null, OriginArguments: null })
            {
                name = inner;
            }
        }

        return name as GenericsKoto;
    }

    private bool ConstraintAccessCovers(BoundConstraint constraint, BindingSymbol contract, BindingSymbol? intersection = null)
    {
        if (constraint.Kind is ConstraintKind.And or ConstraintKind.Or or ConstraintKind.Not)
        {
            return this.ConstraintAccessCovers(constraint.Left!, contract, intersection) &&
                (constraint.Right is null || this.ConstraintAccessCovers(constraint.Right, contract, intersection));
        }

        return (constraint.Contract is null ||
            (AccessCovers(constraint.Contract, contract, intersection ?? contract) &&
             (constraint.Contract.Type is not { } reference || this.TypeAccessCovers(reference, contract, intersection ?? contract)))) &&
            (constraint.RequiredType is null || this.TypeAccessCovers(constraint.RequiredType, contract, intersection ?? contract));
    }

    /// <summary>
    /// SPEC 8.6, 8.6.1: the registered conformance of a Type declaration to a Contract, whether registered under the
    /// Contract declaration itself or under one bound reference of it. Several bound references of one declaration are
    /// distinct conformances, so a query by declaration alone is ambiguous there.
    /// </summary>
    private BoundConformance? ConformanceByDeclaration(BindingSymbol type, BindingSymbol contract, out bool ambiguous)
    {
        ambiguous = false;
        if (this.conformances.TryGetValue((type, contract), out var direct))
        {
            return direct;
        }

        if (IsBoundContractReference(contract) || !this.conformancesByType.TryGetValue(type, out var identities))
        {
            return null;
        }

        BoundConformance? found = null;
        for (var i = 0; i < identities.Count; i++)
        {
            var identity = identities[i];
            if (!ReferenceEquals(identity.Contract.Declaration, contract.Declaration) || identity.Paths.Count == 0)
            {
                continue;
            }

            if (found is not null)
            {
                ambiguous = true;
                return null;
            }

            found = identity;
        }

        return found;
    }

    private void ResetContracts()
    {
        foreach (var bound in this.boundContracts.Values)
        {
            bound.Contract!.State = 0;
        }

        this.ResetAssociatedInference();
        this.failedCause = null;
        this.contractHeadersReady = false;
        this.bindingConstraintTypes = false;
        this.activeConformancePaths.Clear();
        foreach (var list in this.conformancesByType.Values)
        {
            list.Clear();
        }

        this.projectionUses.Clear();
        this.userIterations.Clear();
        this.associatedApplications.Clear();
        this.associatedAmbiguities?.Clear();
        this.contractAgreementFailures?.Clear();
        this.ambiguousAssociatedMatches?.Clear();
        this.memberSelections.Clear();
        foreach (var group in this.requirementGroups.Values)
        {
            group.Active = false;
        }

        foreach (var binding in this.associatedBindings.Values)
        {
            binding.Candidates.Clear();
            binding.Result = null;
            binding.Inferred = null;
            binding.InferenceSource = null;
            binding.InferenceConflict = false;
            binding.Problem = InferenceProblem.None;
            binding.ProblemSource = null;
            binding.ProblemDetail = null;
            binding.EvidenceSources.Clear();
            binding.State = 0;
            binding.FormationScope?.Reset();
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
            conformance.RejectedSelfSignature = null;
            conformance.Invalid = false;
            conformance.WitnessStorage.Clear();
            conformance.WitnessMap.Clear();
            conformance.PropertyWitnessStorage.Clear();
            conformance.PropertyWitnessMap.Clear();
            conformance.Unsupported = false;
            conformance.Needed = false;
            conformance.AssociatedStorage.Clear();
            conformance.InferenceStorage.Clear();
            conformance.Scope.Reset();
        }
    }

    private void PrepareContracts()
    {
        this.Library.Copy.Contract ??= new(this.Library.Copy) { State = 2 };
        this.Library.Owned.Contract ??= new(this.Library.Owned) { State = 2 };
        this.Library.Sealed.Contract ??= new(this.Library.Sealed) { State = 2 };
        this.Library.ObjectPayload.Contract ??= new(this.Library.ObjectPayload) { State = 2 };
        for (var n = 0; n < this.nodes.Count; n++)
        {
            if (this.nodes[n] is not ContractKoto contract)
            {
                continue;
            }

            var shape = contract.BoundSymbol!.Contract ??= new(contract.BoundSymbol);
            shape.State = 0;
            shape.HasUnresolvedParents = false;
            shape.AncestorStorage.Clear();
            shape.RequirementStorage.Clear();
            shape.AssociatedStorage.Clear();
            shape.ClauseStorage.Clear();
            shape.EffectBoundStorage.Clear();
            shape.EffectState = 0;
            shape.Seen.Clear();
            shape.SeenRequirements.Clear();
            foreach (var members in shape.MembersByName.Values)
            {
                members.Clear();
            }

            for (var i = 0; i < contract.Members.Count; i++)
            {
                if (contract.Members[i] is SyntaxFormKoto { Akind: KotoKind.AssociatedType, Operands.Length: > 0 } declaration)
                {
                    this.DeclareAssociatedType(declaration, contract);
                }
            }

            for (var i = 0; i < contract.ConstraintNodes.Count; i++)
            {
                var clause = contract.ConstraintNodes[i];
                if (clause.IsAssociatedConstraint)
                {
                    this.DeclareAssociatedType(clause, contract);
                }
            }
        }

        this.contractShapes.Clear();
        for (var n = 0; n < this.nodes.Count; n++)
        {
            if (this.nodes[n] is ContractKoto contract)
            {
                this.BuildContract(contract.BoundSymbol!.Contract!);
                this.contractShapes.Add(contract.BoundSymbol!.Contract!);
            }
        }
    }

    private void DeclareAssociatedType(Koto declaration, ContractKoto contract)
    {
        var head = AssociatedHead(declaration);
        var name = head is OriginApplicationKoto application ? UnwrapAssociatedHead(application.Type) : head;
        if (name is not (IdentifierNameKoto or TypeSemanticsKoto { Type: null }) || TypeSpelling(name) is not { } spelling)
        {
            return; // A qualified specification refines an existing requirement.
        }

        var symbol = this.Declare(declaration, spelling, BindingSymbolKind.AssociatedType, declaration, this.scopes[contract]);
        symbol.Type = this.types.Intern(BoundTypeKind.AssociatedProjection, symbol, SemanticsKind.Owner, [this.ContractSelfType(contract.BoundSymbol!), this.SelfType(contract.BoundSymbol!)], originArguments: this.AssociatedParameters(declaration));
        name.BoundSymbol = symbol;
        Complete(name, symbol.Type);
        if (head is OriginApplicationKoto)
        {
            head.BoundSymbol = symbol;
            Complete(head, symbol.Type);
        }
    }

    private bool BuildContract(BoundContract shape)
    {
        var contract = (ContractKoto)shape.Symbol.Declaration;
        if (shape.State != 0)
        {
            if (shape.State == 1)
            {
                this.Fail(contract, BindingFailure.Cycle);
                return false;
            }

            return shape.State == 2;
        }

        shape.State = 1;
        var valid = contract.BindingState != BindingState.Invalid;
        for (var i = 0; i < contract.Bases.Count; i++)
        {
            var syntax = contract.Bases[i];
            if (!IsRefinementName(syntax))
            {
                this.Fail(syntax, BindingFailure.InvalidConstraint);
                valid = false;
                continue;
            }

            var name = syntax;
            while (name is TypeSemanticsKoto { IsTransparentWrapper: true, Type: { } inner, OriginName: null, OriginExpression: null, OriginArguments: null })
            {
                name = inner;
            }

            // SPEC 8.4.3: a parent with Type parameters takes exactly its arguments, possibly below a root-name form, and a
            // parent without takes none; an empty list is never an application, and a generic parent is never deferred.
            var generic = GenericParentApplication(name);
            var parent = this.TypeName(name, this.scopes[contract], false);
            if (parent is null && generic is null && this.capabilityMode == BindingMode.Provisional)
            {
                this.BindType(syntax, this.scopes[contract]);
                if (this.HasUnresolvedConstraintSyntax(syntax, this.scopes[contract]))
                {
                    shape.HasUnresolvedParents = true;
                    continue;
                }
            }

            if (parent?.Declaration is not ContractKoto || parent.Contract is not { } inherited ||
                (generic?.TypeArguments.Count ?? 0) != ((DeclarationContainerKoto)parent.Declaration).GenericParameterNodes.Count || generic is { TypeArguments.Count: 0 })
            {
                this.Fail(syntax, BindingFailure.InvalidConstraint);
                valid = false;
                continue;
            }

            syntax.BoundSymbol = parent;
            Complete(syntax, BoundType.Unit);
            name.BoundSymbol = parent;
            Complete(name, BoundType.Unit);
            if (!this.BuildContract(inherited))
            {
                valid = false;
                continue;
            }

            // The parent with Type arguments is refined as its bound reference, whose requirements carry the child's own
            // parameters (contract UniqIndexable<Key>: Indexable<Key>).
            if (generic is not null)
            {
                // The wrappers between a root-name form and its application resolve with the parent.
                for (var wrapper = name is SyntaxFormKoto { Akind: KotoKind.RootName, Operands: [{ } rooted] } ? rooted : name; !ReferenceEquals(wrapper, generic); wrapper = ((TypeSemanticsKoto)wrapper).Type!)
                {
                    wrapper.BoundSymbol = parent;
                    Complete(wrapper, BoundType.Unit);
                }

                generic.BoundSymbol = parent;
                Complete(generic, BoundType.Unit);
                generic.Identifier!.BoundSymbol = parent;
                Complete(generic.Identifier, BoundType.Unit);
                var arguments = this.BindTypeList(
                    generic.TypeArguments,
                    this.scopes[contract],
                    this.TypeContext(generic, this.scopes[contract]).Nested,
                    BoundTypeKind.Constructed,
                    parent);
                if (arguments is null)
                {
                    this.Fail(syntax, BindingFailure.InvalidConstraint);
                    valid = false;
                    continue;
                }

                parent = this.BoundContractReference(this.types.Intern(BoundTypeKind.Constructed, parent, SemanticsKind.Owner, (BoundType[])arguments.Components));
                inherited = parent.Contract!;
            }

            shape.HasUnresolvedParents |= inherited.HasUnresolvedParents;

            Add(parent, shape.AncestorStorage);
            for (var j = 0; j < inherited.Ancestors.Count; j++)
            {
                Add(inherited.Ancestors[j], shape.AncestorStorage);
            }

            for (var j = 0; j < inherited.Requirements.Count; j++)
            {
                AddRequirement(inherited.Requirements[j]);
            }

            for (var j = 0; j < inherited.AssociatedTypes.Count; j++)
            {
                AddAssociated(inherited.AssociatedTypes[j]);
            }
        }

        for (var i = 0; i < contract.Members.Count; i++)
        {
            var member = contract.Members[i];
            if (member is FunctionKoto { IsRequirement: true } or PropertyKoto { IsContractRequirement: true })
            {
                AddRequirement(new(member.BoundSymbol!, shape.Symbol));
            }
            else if (member is SyntaxFormKoto { Akind: KotoKind.AssociatedType } && member.BoundSymbol is { } associated)
            {
                AddAssociated(new(associated, shape.Symbol));
                Complete(member, BoundType.Unit);
            }
            else if (member is not EffectBoundKoto)
            {
                // An effect specification is checked with the bounds of the Contract (BindEffectBound).
                this.Fail(member, BindingFailure.InvalidConstraint);
                valid = false;
            }
        }

        for (var i = 0; i < contract.ConstraintNodes.Count; i++)
        {
            var clause = contract.ConstraintNodes[i];
            shape.ClauseStorage.Add(clause);
            if (clause.IsAssociatedConstraint && clause.BoundSymbol is { Kind: BindingSymbolKind.AssociatedType } associated)
            {
                AddAssociated(new(associated, shape.Symbol));
            }
        }

        // Every ancestor has fewer ancestors than any of its descendants. This gives
        // conformance verification one order in which inherited proofs are available.
        shape.AncestorStorage.Sort(static (left, right) => left.Contract!.Ancestors.Count.CompareTo(right.Contract!.Ancestors.Count));
        shape.State = valid ? (byte)2 : (byte)3;
        for (var i = 0; i < shape.Requirements.Count; i++)
        {
            var member = shape.Requirements[i];
            if (!shape.MembersByName.TryGetValue(member.Symbol.Name, out var list))
            {
                shape.MembersByName.Add(member.Symbol.Name, list = new());
            }

            list.Add(member);
        }

        if (!valid)
        {
            this.Fail(contract, BindingFailure.InvalidConstraint);
        }

        return valid;

        void AddRequirement(BoundRequirement requirement)
        {
            if (shape.SeenRequirements.Add(requirement))
            {
                shape.RequirementStorage.Add(requirement);
            }
        }

        void AddAssociated(BoundRequirement requirement)
        {
            if (shape.SeenRequirements.Add(requirement))
            {
                shape.AssociatedStorage.Add(requirement);
            }
        }

        void Add(BindingSymbol symbol, List<BindingSymbol> destination)
        {
            if (shape.Seen.Add(symbol))
            {
                destination.Add(symbol);
            }
        }
    }

    private ConstraintProof ProveConformance(BoundType type, BindingSymbol contract, BindingScope scope)
    {
        // Refinement assumptions are input evidence, not in-progress registrations.
        var premise = IsContractSelf(type) && type.Symbol!.SelfOf is { } own && this.AvailableContractPremise(own) && IsRefinement(own, contract);

        // SPEC 8.7.4: a proven PrimitiveInteger supplies the built-in comparison and formatting conformances of the twelve
        // Types and their conformance to the closed Contract Position; Wrapping<T> over it has the comparison and formatting
        // conformances of its argument but is never a Position (SPEC 3.1.1).
        premise |= contract.LibraryDeclaration is KimiDeclarationId.Equatable or KimiDeclarationId.Comparable or KimiDeclarationId.Utf8Format or KimiDeclarationId.Position && this.IsGenericInteger(type, scope);
        premise |= contract.LibraryDeclaration is KimiDeclarationId.Equatable or KimiDeclarationId.Comparable or KimiDeclarationId.Utf8Format && this.IsGenericWrapping(type, scope);
        for (var current = scope; current is not null && !premise; current = current.Parent)
        {
            if (current.Constraints is not { Invalid: false } environment)
            {
                continue;
            }

            foreach (var fact in environment.Facts)
            {
                if (fact.Kind == ConstraintKind.Contract && AssociatedIdentityMatches(fact.Subject, type) && this.AvailableConstraintFact(environment, fact) && this.AvailableContractPremise(fact.Contract!) && IsRefinement(this.AppliedAssociatedContract(fact, type), contract))
                {
                    premise = true;
                    break;
                }
            }
        }

        // Definition evidence is still resolved so that its errors are never hidden by an assumption.
        var proof = this.ResolveConformance(type, contract, scope, out _);
        return premise ? CombineProof(ConstraintProof.Proven, proof, false) : proof;
    }

    private BoundConstraint ContractConstraint(BoundConstraint constraint, BindingScope scope, BoundType? self, bool normalize = true, BindingSymbol? reference = null)
    {
        if (constraint.Kind == ConstraintKind.Not)
        {
            return this.NegateConstraint(this.ContractConstraint(constraint.Left!, scope, self, normalize, reference));
        }

        if (constraint.Kind is ConstraintKind.And or ConstraintKind.Or)
        {
            return this.InternConstraint(new(constraint.Kind, left: this.ContractConstraint(constraint.Left!, scope, self, normalize, reference), right: this.ContractConstraint(constraint.Right!, scope, self, normalize, reference)));
        }

        var contract = constraint.Contract;
        if (reference is not null && contract?.Type is { } contractType && !ReferenceEquals(contractType.Symbol, contract))
        {
            contract = this.BoundContractReference(this.SubstituteContractReference(contractType, reference));
        }

        return constraint.Subject is null ? constraint : this.InternConstraint(new(constraint.Kind, Instantiate(constraint.Subject), constraint.RequiredType is { } required ? Instantiate(required) : null, contract, constraint.Mask));

        BoundType Instantiate(BoundType type)
            => this.ContractType(reference is null ? type : this.SubstituteContractReference(type, reference), scope, self, normalize);
    }

    private bool IsLibraryDeclaration(Koto declaration) => ReferenceEquals(declaration.CodeContext.Kotonoha, this.Library.Kotonoha);

    private void ValidateConformances(BindingMode mode, bool final)
    {
        this.contractHeadersReady = true;
        if (final)
        {
            // Body/header validation may invalidate an earlier declaration-side witness of the sources. The Kimi library's
            // declarations are valid and fixed (a malformed library fails the pass), so a library conformance proven before the
            // bodies stays proven, and only an unproven one is judged again.
            foreach (var identity in this.conformances.Values)
            {
                if (!this.IsLibraryDeclaration(identity.Type.Declaration))
                {
                    identity.IsVerified = false;
                }
            }

            for (var i = 0; i < this.activeConformancePaths.Count; i++)
            {
                if (!this.IsLibraryDeclaration(this.activeConformancePaths[i].Type.Declaration))
                {
                    this.activeConformancePaths[i].IsVerified = false;
                }
            }
        }

        for (var i = 0; i < this.activeConformancePaths.Count; i++)
        {
            var conformance = this.activeConformancePaths[i];
            this.failedCause = null;
            var proof = this.VerifyConformance(conformance);
            if (final && conformance.InheritedFrom is null)
            {
                if (conformance.Unsupported && mode == BindingMode.Final)
                {
                    this.Fail(conformance.Use, BindingFailure.Unsupported); // SPEC 12.7, 23.1.1: the limit's one location
                    continue;
                }

                this.RequireConstraint(conformance.Use, proof, mode);
            }
        }

        if (final)
        {
            this.InvalidateAssociatedEvidence();
            foreach (var identity in this.conformances.Values)
            {
                if (identity.PathStorage.Count != 0)
                {
                    this.failedCause = null;
                    this.conformanceChecks++;
                    var agreement = this.VerifyConformanceAgreement(identity);
                    this.conformanceChecks--;
                    var explicitPath = false;
                    foreach (var path in identity.PathStorage)
                    {
                        explicitPath |= path.InheritedFrom is null;
                    }

                    if (explicitPath || agreement == ConstraintProof.Error)
                    {
                        this.RequireConstraint(identity.PathStorage[0].Use, agreement, mode);
                    }
                }
            }

            this.ValidateBoundConformanceAgreement();
            for (var i = 0; i < this.projectionUses.Count; i++)
            {
                var use = this.projectionUses[i];
                this.failedCause = null;
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
            this.ValidateAssociatedOverlapAgreement(scope);
            if (scope.Constraints is { } environment)
            {
                this.associatedIdentityChecks.Clear();
                foreach (var a in environment.Facts)
                {
                    if (a is not { Kind: ConstraintKind.TypeIdentity, Subject.Kind: BoundTypeKind.AssociatedProjection, RequiredType: { } ta } ||
                        this.DependentType(ta))
                    {
                        continue;
                    }

                    if (this.associatedIdentityChecks.TryGetValue(a.Subject, out var previous) && !this.SameCompleteType(ta, previous))
                    {
                        environment.Invalid = true;
                        this.Fail(scope.Owner, BindingFailure.InvalidAssociatedType);
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
                    this.Fail(contract, BindingFailure.Access);
                }
            }

            foreach (var members in shape.MembersByName.Values)
            {
                for (var i = 0; i < members.Count; i++)
                {
                    if (members[i].Symbol.Declaration is not FunctionKoto a)
                    {
                        continue;
                    }

                    var declaringContract = members[i].Contract;
                    if (a.BoundSymbol!.Type is { } result && !this.TypeAccessCovers(result, declaringContract, declaringContract))
                    {
                        this.Fail(a, BindingFailure.Access);
                    }

                    for (var p = 0; p < a.Parameters.Count; p++)
                    {
                        if (a.Parameters[p].Type.BoundType is { } parameter && !this.TypeAccessCovers(parameter, declaringContract, declaringContract))
                        {
                            this.Fail(a, BindingFailure.Access);
                        }
                    }

                    for (var p = 0; p < a.TypeConstraints.Count; p++)
                    {
                        if (((IsKoto)a.TypeConstraints[p]).BoundConstraint is { } constraint && !this.ConstraintAccessCovers(constraint, declaringContract))
                        {
                            this.Fail(a, BindingFailure.Access);
                        }
                    }

                    for (var j = 0; j < i; j++)
                    {
                        if (members[j].Symbol.Declaration is not FunctionKoto b || a.GenericArguments.Count != b.GenericArguments.Count || a.Parameters.Count != b.Parameters.Count)
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

                            sameSignature &= SignatureEquals(this.ContractType(this.SubstituteContractReference(ta, members[i].Contract), scope), this.ContractType(this.SubstituteContractReference(tb, members[j].Contract), scope), a, b);
                            differentLabels |= a.Parameters[p].ExternalName != b.Parameters[p].ExternalName;
                        }

                        if (sameSignature && (differentLabels || !SameGenericShape(a, b)))
                        {
                            this.Fail(contract, BindingFailure.InvalidConstraint);
                        }
                    }
                }
            }

            for (var i = 0; i < shape.ClauseStorage.Count; i++)
            {
                if (shape.ClauseStorage[i].BoundConstraint is { } constraint && (!this.ConstraintAccessCovers(constraint, shape.Symbol) ||
                    (shape.ClauseStorage[i].Left.BoundType is { } subject && !this.TypeAccessCovers(subject, shape.Symbol, shape.Symbol))))
                {
                    this.Fail(shape.ClauseStorage[i], BindingFailure.Access);
                    this.Fail(contract, BindingFailure.Access);
                }
            }
        }
    }
}
