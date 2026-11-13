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
        => type.Symbol is { } symbol && this.ConformanceOf(symbol, contract) is { } result && result.UnconditionalPath is not null ? result : null;

    /// <summary>Gets definition metadata, without asserting availability for any substitution.</summary>
    /// <param name="type">The nominal or constructed Type identifying the declaration.</param>
    /// <param name="contract">The Contract declaration identity.</param>
    /// <returns>The registered definition and paths, or null if absent.</returns>
    public BoundConformance? GetConformanceDefinition(BoundType type, BindingSymbol contract)
        => type.Symbol is { } symbol && this.ConformanceOf(symbol, contract) is { } result && result.Paths.Count != 0 ? result : null;

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

    // SPEC 8.4.3, 8.6.1: a query by Contract declaration, which the compiler asks (an associated Type's owner, a library Contract), is
    // satisfied by any bound reference of that declaration. A written Contract is never such a query: with Type parameters it names a
    // bound reference (QualifierReference, SPEC 8.5.1, 9.7).
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

    private bool ConstraintAccessCovers(BoundConstraint constraint, BindingSymbol contract, BindingSymbol? intersection = null)
    {
        if (constraint.Kind is ConstraintKind.And or ConstraintKind.Or or ConstraintKind.Not)
        {
            return this.ConstraintAccessCovers(constraint.Left!, contract, intersection) &&
                (constraint.Right is null || this.ConstraintAccessCovers(constraint.Right, contract, intersection));
        }

        return (constraint.Contract is null ||
            (this.AccessCovers(constraint.Contract, contract, intersection ?? contract) &&
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
        if (this.ConformanceOf(type, contract) is { } direct)
        {
            return direct;
        }

        if (IsBoundContractReference(contract) || this.ConformancesOf(type) is not { } identities)
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
        this.impls.Reset();
        this.conformancesRegistered = false;

        this.projectionUses.Clear();
        this.userIterations.Clear();
        this.associatedApplications.Clear();
        this.associatedAmbiguities?.Clear();
        this.contractAgreementFailures?.Clear();
        this.ambiguousAssociatedMatches?.Clear();
        this.memberSelections.Clear();
        this.memberSides.Clear();
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
    }

    private void PrepareContracts()
    {
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
        var name = head is OriginApplicationKoto application ? UnwrapTransparentType(application.Type)! : head!;

        // Only a declaration reaches here, and its head is a Name: the parser places every specification among the members.
        var spelling = TypeSpelling(name) ?? throw new InvalidOperationException($"`{declaration}` declares no Name (InternalInvariant; G683).");
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

            var generic = ContractApplication(name);
            var parentPath = this.NamePath(name, this.scopes[contract]);
            var parent = parentPath.Symbol;
            if (parent is null && this.PendingReference(syntax, null, this.scopes[contract]))
            {
                // A missing parent, generic or not, is pending (BU-151, O-216 (a)): a later source may declare it.
                shape.HasUnresolvedParents = true;
                continue;
            }

            if (parent is not { IsContract: true } || parent.Contract is not { } inherited)
            {
                // A failed parent forms no reference; its Type arguments keep their own records (SPEC 23.1.2), as BindConstructedType's do.
                this.BindUnappliedArguments(generic, this.scopes[contract], this.TypeContext(syntax, this.scopes[contract]).Nested);
                this.Fail(syntax, BindingFailure.InvalidConstraint);
                valid = false;
                continue;
            }

            if (!this.BuildContract(inherited))
            {
                syntax.BoundSymbol = parent;
                Complete(syntax, BoundType.Unit);
                name.BoundSymbol = parent;
                Complete(name, BoundType.Unit);
                valid = false;
                continue;
            }

            // SPEC 8.4.3, 9.7.1: a parent is a written Contract reference, formed as every one is (BindContractReference: its arity, its
            // own Type arguments and its outer ones), so its requirements carry the child's own parameters
            // (contract UniqIndexable<Key>: Indexable<Key>).
            var selection = parent;
            parent = this.BindContractReference(name, selection, this.scopes[contract]);
            if (parent is null && this.PendingReference(syntax, selection, this.scopes[contract]))
            {
                shape.HasUnresolvedParents = true; // A Type argument is missing (SPEC 9.7.1): pending as a missing parent is.
                continue;
            }

            if (parent is null)
            {
                this.Fail(syntax, BindingFailure.InvalidConstraint);
                valid = false;
                continue;
            }

            CompleteQualifier(syntax, selection, parent);
            inherited = parent.Contract!;

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
            else if (member is IsKoto { IsAssociatedConstraint: true } specification)
            {
                // SPEC 8.5.3: a specification constrains an inherited associated Type; its bound requirement (BindAssociatedSpecification)
                // is one of the Contract's clauses, inherited and checked as they are.
                shape.ClauseStorage.Add(specification);
            }
            else if (member is not EffectBoundKoto && member is not FunctionKoto { Placement: not null })
            {
                // An effect specification is checked with the bounds of the Contract (BindEffectBound); a misplaced specialization
                // (SPEC F.6.2) is no member: it stands for its placement Error, so the Contract's verdict does not rest on it.
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
        // SPEC 8.7.4: a proven PrimitiveInteger supplies PrimitiveIntegerSupplies; Wrapping<T> over it has the comparison
        // conformances of its argument but is never a Position (SPEC 3.1.1). Its Utf8Format is the witness table's WrappingFormat
        // row, for an abstract T too (Binding.WitnessGroup, R8-S2d1), which ResolveConformance below proves.
        var premise = contract.LibraryDeclaration is { } supplied && Array.IndexOf(PrimitiveIntegerSupplies, supplied) >= 0 && this.IsGenericInteger(type, scope);
        premise |= contract.LibraryDeclaration is KimiDeclarationId.Equatable or KimiDeclarationId.Comparable && this.IsGenericWrapping(type, scope);

        // Refinement assumptions are input evidence, not in-progress registrations (PremiseRefines, the refinement row's one predicate).
        premise = premise || this.PremiseRefines(type, contract, scope);

        // Definition evidence is still resolved so that its errors are never hidden by an assumption.
        var proof = this.ResolveConformance(type, contract, scope, out _);

        // N9 (SPEC 8.2.3, 8.3.2): a subject identical with a complete constructed Type takes that Type's closed-world refutation, and a
        // premise against it is contradictory evidence (Error), as for the constructed Type itself (c2-1's rule). The member's Proven is
        // not taken here: a conformance it proves reaches projections' completion, which is c2-4's (N8).
        if (proof == ConstraintProof.Unknown && type.IsAbstract && HasTypeIdentity(scope) && this.ClassesOf(scope, out _)?.CompleteMember(type) is { } member &&
            this.ResolveConformance(member, contract, scope, out _) == ConstraintProof.Refuted)
        {
            if (premise)
            {
                return ConstraintProof.Error;
            }

            proof = ConstraintProof.Refuted;
        }

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

        var contract = constraint.Contract is { } written ? this.InstantiatedContract(written, scope, self, normalize, reference) : null;
        return constraint.Subject is null ? constraint : this.InternConstraint(new(constraint.Kind, Instantiate(constraint.Subject), constraint.RequiredType is { } required ? Instantiate(required) : null, contract, constraint.Mask));

        BoundType Instantiate(BoundType type)
            => this.ContractType(reference is null ? type : this.SubstituteContractReference(type, reference, out _), scope, self, normalize);
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
            foreach (var identity in this.impls.Identities)
            {
                if (!this.IsLibraryDeclaration(identity.Type.Declaration))
                {
                    identity.IsVerified = false;
                }
            }

            for (var i = 0; i < this.impls.Paths.Count; i++)
            {
                if (!this.IsLibraryDeclaration(this.impls.Paths[i].Type.Declaration))
                {
                    this.impls.Paths[i].IsVerified = false;
                }
            }
        }

        this.PublishSourceStates();
        for (var i = 0; i < this.impls.Paths.Count; i++)
        {
            var conformance = this.impls.Paths[i];
            this.failedCause = null;
            var frame = this.BeginConsultation(conformance.Use); // The proof's reads of other nodes (RequireConstraintInFrame).
            var proof = this.VerifyConformance(conformance);
            if (final && conformance.InheritedFrom is null && !(conformance.Unsupported && mode == BindingMode.Final))
            {
                this.RequireConstraintInFrame(conformance.Use, proof, mode, frame);
                continue;
            }

            this.DropConsultation(frame);
            if (final && conformance.InheritedFrom is null)
            {
                this.Fail(conformance.Use, BindingFailure.Unsupported); // SPEC 12.7, 23.1.1: the limit's one location
            }
        }

        if (final)
        {
            this.InvalidateAssociatedEvidence();
            foreach (var identity in this.impls.Identities)
            {
                if (identity.PathStorage.Count != 0)
                {
                    this.failedCause = null;
                    this.conformanceChecks++;
                    var frame = this.BeginConsultation(identity.PathStorage[0].Use); // The agreement's judgment is at its first path's use.
                    var agreement = this.VerifyConformanceAgreement(identity);
                    this.conformanceChecks--;
                    var explicitPath = false;
                    foreach (var path in identity.PathStorage)
                    {
                        explicitPath |= path.InheritedFrom is null;
                    }

                    if (explicitPath || agreement == ConstraintProof.Error)
                    {
                        this.RequireConstraintInFrame(identity.PathStorage[0].Use, agreement, mode, frame);
                    }
                    else
                    {
                        this.DropConsultation(frame);
                    }
                }
            }

            this.ValidateBoundConformanceAgreement();
            for (var i = 0; i < this.projectionUses.Count; i++)
            {
                var use = this.projectionUses[i];
                this.failedCause = null;
                var frame = this.BeginConsultation(use.Use); // The proof's reads of other nodes (RequireConstraintInFrame).
                this.RequireConstraintInFrame(use.Use, this.ProveConformance(use.Type, use.Contract, this.ConstraintScope(use.Use)), mode, frame);
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
                        environment.Fail(scope.Owner);
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
                if (!this.AccessCovers(shape.Ancestors[i], shape.Symbol, shape.Symbol))
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

                            sameSignature &= SignatureEquals(this.ContractType(this.SubstituteContractReference(ta, members[i].Contract, out _), scope), this.ContractType(this.SubstituteContractReference(tb, members[j].Contract, out _), scope), a, b);
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
