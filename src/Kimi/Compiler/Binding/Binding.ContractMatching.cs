// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 8.4.2, 8.6.3: the bound Schemas' generic slot kinds, position by position, of two declarations whose generic counts
    // (FnSig.GenericCount) the caller found equal.
    private static bool SameGenericShape(FunctionKoto requirement, FunctionKoto implementation)
    {
        var a = requirement.BoundSymbol!.Schema!;
        var b = implementation.BoundSymbol!.Schema!;
        for (var i = 0; i < a.GenericSlots.Count; i++)
        {
            if (a.GenericSlots[i].Kind != b.GenericSlots[i].Kind)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ExternallyVisible(BindingSymbol symbol)
    {
        for (var current = symbol; current is not null; current = EnclosingAccessDeclaration(current))
        {
            if (current.Declaration is DeclarationContainerKoto { IsRoot: true })
            {
                break;
            }

            if (DeclarationAccess(current) is not (ModifierKind.Public or ModifierKind.Protected or ModifierKind.ProtectedOrInternal))
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 12.7, 23.1.1: a witness whose ObjectCallCompatible status is not computed leaves the verification Unknown; it is the
    // conformance's limit (Unsupported) only when nothing else is unproven.
    private static ConstraintProof UnsupportedProof(BoundConformancePath path, ConstraintProof proof)
    {
        path.Unsupported &= proof == ConstraintProof.Proven;
        return path.Unsupported ? ConstraintProof.Unknown : proof;
    }

    // A function requirement's premises scope on its path, kept with the path (BoundConformancePath.WitnessScopes).
    private static BindingScope WitnessScope(BoundConformancePath path, BoundRequirement identity, FunctionKoto requirement)
    {
        var scopes = path.WitnessScopes ??= new();
        for (var i = 0; i < scopes.Count; i++)
        {
            if (scopes[i].Requirement == identity)
            {
                return scopes[i].Premises;
            }
        }

        var premises = new BindingScope(requirement);
        scopes.Add((identity, premises));
        return premises;
    }

    // SPEC 8.8.2: a path's conforming Type in its conformance scope, normalized there as both sides of each requirement are (the scope's
    // identity premises included). Every reading of a path's Self (its verification, inherited or declared, its requirement
    // identification and its associated bindings) is this one, so what the path keeps by a requirement (its RequirementMatches) has one
    // Self.
    private BoundType ConformanceSelf(BoundConformancePath path) => this.ContractType(this.SelfType(path.Type), path.Scope);

    // R7-C3 (F's C2 rulings 273, 274, 289-290; SPEC 8.2.3, 8.8.2, 23.1.2): a conformance's verification reads the premises it is judged
    // under, each through the one consult (AvailableConstraintFact), whose read of a failed one is the verification's limit: its
    // Contract's (the Self requirements every conformer meets: ModuleBindingTest's late witness of `associate Item is Source.(Origin).Item`)
    // and its conformance scope's, the declaration D and a conditional's own premises P, by one walk of the chain (C3-1b).
    private PremiseFailure? ConsultsFailedPremise(BindingScope? scope)
    {
        PremiseFailure? failed = null;
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints is not { FailedPremises.Count: > 0 } environment)
            {
                continue;
            }

            foreach (var fact in environment.Facts)
            {
                if (environment.IsFailed(fact) && !this.AvailableConstraintFact(environment, fact, out _))
                {
                    failed ??= environment.FailedPremise;
                }
            }
        }

        return failed;
    }

    // A path keeps Proven (IsVerified) and Invalid only; Unknown and a non-Invalid Error are verified again at each read (its open
    // obligation answers Unknown): on-demand verification, staged to C3's scheduler (DECISIONS BU-59). The conforming declaration's own check is
    // its published source state.
    private ConstraintProof VerifyConformance(BoundConformancePath conformance, ConstraintProof? inheritedProof = null)
    {
        // A limit is the fact of a completed verification; an early or in-progress answer has none (ResolveConformance). Stored only on
        // a change, so a verified frozen path, never Unsupported (FreezeLibrary's UnsettledPath check), is never written (PLAN G949
        // Stage 2 U3).
        if (!this.Verifying(conformance) && conformance.Unsupported)
        {
            conformance.Unsupported = false;
        }

        if (this.collectingAssociated is not null && this.associatedInference.TryGetValue(conformance.Type, out var batch) && !batch.Published)
        {
            return ConstraintProof.Unknown; // Type-value scheduling never certifies a collecting definition.
        }

        if (conformance.InheritedFrom is not null)
        {
            return this.VerifyInheritedConformance(conformance);
        }

        if (conformance.Identity.Invalid || conformance.Invalid || conformance.Declaration.BindingState == BindingState.Invalid ||
            (this.scopes.TryGetValue(conformance.Contract.Declaration, out var contractScope) && this.ConsultsFailedPremise(contractScope) is not null) ||
            this.InvalidDeclarationContext(conformance.Contract.Declaration) || this.InvalidDeclarationContext(conformance.Type.Declaration) ||
            (!this.ChecksOwnSite(conformance.Use) && this.ConsultsFailedPremise(conformance.Scope.Parent) is not null))
        {
            conformance.IsVerified = false;
            if (conformance.Invalid && (this.partPrerequisites.ContainsKey(conformance.Use) || this.prerequisites.ContainsKey(conformance.Use)))
            {
                this.failedCause ??= conformance; // A use rests on a conformance that rests on its specification or its proof's evidence.
            }
            else if (conformance.RootPath.Use.BindingFailure != BindingFailure.None)
            {
                this.failedCause ??= conformance.RootPath; // The evidence: the failed root the proof read (RestsOnRecoveryOrLimit).
            }

            return ConstraintProof.Error;
        }

        if (UnresolvedTypeDeclarationContext(conformance.Type.Declaration) || UnresolvedTypeDeclarationContext(conformance.Contract.Declaration))
        {
            conformance.IsVerified = false;
            return ConstraintProof.Unknown;
        }

        if (conformance.IsVerified)
        {
            return ConstraintProof.Proven;
        }

        // SPEC 8.3.3 Recursion at the declaration's own obligation (any instance of the path), keyed by the path in the one table.
        if (!this.BeginVerification(conformance, out var held))
        {
            return ConstraintProof.Unknown;
        }

        this.conformanceChecks++;
        conformance.WitnessStorage.Clear();
        conformance.WitnessMap.Clear();
        conformance.PropertyWitnessStorage.Clear();
        conformance.PropertyWitnessMap.Clear();
        var completed = false;
        try
        {
            var shape = conformance.Contract.Contract!;
            var scope = conformance.Scope;
            var self = this.ConformanceSelf(conformance);
            var declarationProof = this.ReadSourceState(conformance.Type, out _);
            if (!this.ValidArithmeticConformance(conformance, self))
            {
                return ConstraintProof.Error;
            }

            if (conformance.Premises is { } premises)
            {
                for (var p = 0; p < premises.Operands.Length; p++)
                {
                    if (premises.Operands[p] is IsKoto { BoundConstraint: { } constraint } clause &&
                        this.InaccessibleConstraint(constraint, conformance.Type, conformance.Contract) is { } inaccessible)
                    {
                        // The premise's own record; the conditional and the conformance use keep their failures beside it, fact-less
                        // owners over the failed premise, until R7-E5 (ValidateConstraintProjectionAccess' y2).
                        this.RecordAccess(clause, inaccessible, null);
                        this.Fail(premises.Parent!, BindingFailure.Access);
                        return Invalid(BindingFailure.Access, []);
                    }
                }
            }

            if (IntrinsicContract.Of(conformance.Contract.Intrinsic).Judged)
            {
                // SPEC 8.7.1: the judgment of `Self is K`, as a Contract clause is proven; a declared Copy is validated where it opts in.
                var intrinsicProof = CombineProof(declarationProof, this.RequestCapability(self, conformance.Contract, scope), true);
                conformance.IsVerified = intrinsicProof == ConstraintProof.Proven;
                return intrinsicProof;
            }

            var proof = declarationProof;
            IsKoto? specification = null;
            Koto? failedRequirement = null;
            for (var i = 0; i < shape.AssociatedTypes.Count; i++)
            {
                if (!conformance.AssociatedStorage.TryGetValue(shape.AssociatedTypes[i], out var associated))
                {
                    var identity = shape.AssociatedTypes[i];
                    if (this.SpecificationPart(conformance, identity) is { } part)
                    {
                        // SPEC 23.1.2: an identity whose explicit specification did not bind is never inferred; its absence rests on
                        // that specification, while every other missing identity still reports.
                        specification ??= part;
                        continue;
                    }

                    if (this.HasFailureCauses(identity.Symbol.Declaration))
                    {
                        // SPEC 23.1.2: a requirement that failed in its Contract has no Type to bind; the absence rests on it (G686).
                        failedRequirement ??= identity.Symbol.Declaration;
                        continue;
                    }

                    if (this.InferenceHole(conformance, identity))
                    {
                        var binding = this.AssociatedBindingOf(conformance.RootPath, identity)!;
                        if (binding.Problem is InferenceProblem.Missing or InferenceProblem.Ambiguous)
                        {
                            return Invalid(binding.Problem == InferenceProblem.Missing ? BindingFailure.MissingImplementation : BindingFailure.Ambiguous, []);
                        }

                        return Invalid(BindingFailure.AssociatedInference, this.InferenceFacts(identity, binding));
                    }

                    return Invalid(BindingFailure.InvalidAssociatedType, []);
                }

                if (this.InaccessibleType(associated, conformance.Type, conformance.Contract) is { } inaccessible)
                {
                    // SPEC 9.4.4 (any exposed binding), 9.4.6: the use is located, the declaration related; the binding is the Container's
                    // specification or an inferred one, outside the use's syntax, so no part (RecordAccess' rule).
                    return Invalid(BindingFailure.Access, [this.AccessFact(inaccessible)]);
                }
            }

            if (specification is not null)
            {
                return RestOnSpecification(specification);
            }

            if (failedRequirement is not null)
            {
                this.partPrerequisites[conformance.Use] = failedRequirement;
                return Invalid(BindingFailure.InvalidAssociatedType, []);
            }

            if (inheritedProof is { } inheritedResult)
            {
                proof = CombineProof(proof, inheritedResult, true);
            }
            else if (shape.Ancestors.Count != 0)
            {
                var ancestorProofs = this.conformanceProofScratch.Rent(shape.Ancestors.Count);
                try
                {
                    for (var i = 0; i < shape.Ancestors.Count; i++)
                    {
                        var ancestor = shape.Ancestors[i];
                        var prerequisites = ConstraintProof.Proven;
                        // These candidates are Contract identities, so membership in Seen
                        // denotes an ancestor; member identities cannot match them.
                        for (var p = 0; p < i; p++)
                        {
                            if (ancestor.Contract!.Seen.Contains(shape.Ancestors[p]))
                            {
                                prerequisites = CombineProof(prerequisites, ancestorProofs[p], true);
                            }
                        }

                        ancestorProofs[i] = this.VerifyConformance(this.impls.Path(conformance.Type, ancestor, conformance.Declaration, conformance.RootContract), prerequisites);
                        proof = CombineProof(proof, ancestorProofs[i], true);
                    }
                }
                finally
                {
                    this.conformanceProofScratch.Return(ancestorProofs, clearArray: false);
                }
            }

            for (var i = 0; i < shape.ClauseStorage.Count; i++)
            {
                if (shape.ClauseStorage[i].BoundConstraint is not { } constraint)
                {
                    return ConstraintProof.Unknown;
                }

                var associated = this.AssociatedIdentity(shape.ClauseStorage[i].Left.BoundType, shape.Symbol);
                var clauseScope = this.AssociatedFormationScope(conformance, associated, scope);
                proof = CombineProof(proof, this.ProveRequirement(constraint, clauseScope, self), true);
            }

            var container = (DeclarationContainerKoto)conformance.Type.Declaration;
            if (conformance.Declaration.Parent is SyntaxFormKoto syntax && TryConditionalBlock(syntax, out var block))
            {
                for (var i = 0; i < block.Items.Count; i++)
                {
                    if (block.Items[i] is IsKoto { IsAssociatedConstraint: true } clause)
                    {
                        if (clause.BindingState == BindingState.Invalid)
                        {
                            return RestOnSpecification(clause);
                        }

                        if (clause.BoundConstraint is { } constraint && this.AssociatedIdentity(clause.Left.BoundType) is { } identity && shape.SeenRequirements.Contains(identity))
                        {
                            var clauseScope = this.AssociatedFormationScope(conformance, identity, scope);
                            proof = CombineProof(proof, this.ProveSpecification(constraint, clauseScope, self, this.SpecifiedBinding(conformance, identity)), true);
                        }
                    }
                }
            }

            for (var i = 0; i < container.Members.Count; i++)
            {
                if (container.Members[i] is IsKoto { IsAssociatedConstraint: true, BoundConstraint: { } constraint } clause && this.AssociatedIdentity(clause.Left.BoundType) is { } identity && shape.SeenRequirements.Contains(identity) &&
                    this.SpecifiesContract(clause, conformance.Contract))
                {
                    if (clause.BindingState == BindingState.Invalid)
                    {
                        return RestOnSpecification(clause);
                    }

                    var clauseScope = this.AssociatedFormationScope(conformance, identity, scope);
                    proof = CombineProof(proof, this.ProveSpecification(constraint, clauseScope, self, this.SpecifiedBinding(conformance, identity)), true);
                }
            }

            for (var i = 0; i < shape.Requirements.Count; i++)
            {
                var identity = shape.Requirements[i];
                var requirement = identity.Symbol;
                if (!ReferenceEquals(identity.Contract, conformance.Contract))
                {
                    var ancestor = this.impls.Path(conformance.Type, identity.Contract, conformance.Declaration, conformance.RootContract);
                    if (!CopyRequirementWitness(ancestor, conformance, identity))
                    {
                        proof = CombineProof(proof, ConstraintProof.Unknown, true);
                    }

                    continue;
                }

                if (requirement.Property is { } property)
                {
                    var propertyProof = this.VerifyPropertyRequirement(conformance, property, self, scope);
                    if (propertyProof is ConstraintProof.Error or ConstraintProof.Refuted)
                    {
                        return Invalid(BindingFailure.IncompatibleImplementation, []);
                    }

                    proof = CombineProof(proof, propertyProof, true);
                    continue;
                }

                if (requirement.Declaration is not FunctionKoto function)
                {
                    return ConstraintProof.Unknown;
                }

                var match = this.IdentifyRequirement(conformance, identity);
                if (match.State == RequirementMatchState.Ambiguous)
                {
                    return Invalid(BindingFailure.Ambiguous, []);
                }

                if (match.State == RequirementMatchState.Pending)
                {
                    proof = CombineProof(proof, ConstraintProof.Unknown, true);
                    continue;
                }

                if (match.State == RequirementMatchState.Missing)
                {
                    return Invalid(BindingFailure.MissingImplementation, []);
                }

                var selected = match.Member!;
                var selection = match.Selection;
                var (compatibility, bound) = this.CompatibleRequirement(conformance, function, (FunctionKoto)selected.Declaration, self, scope, selection);
                if (compatibility is ConstraintProof.Error or ConstraintProof.Refuted)
                {
                    // SPEC 8.11.4: an implementation's bounded Callable clause the requirement's premises do not declare (Related, role 0)
                    // and the requirement (role 1); any other incompatibility is fact-less.
                    return bound is null
                        ? Invalid(BindingFailure.IncompatibleImplementation, [])
                        : Invalid(BindingFailure.IncompatibleImplementation, [new(ProblemFactKind.Related, this.FactSite(bound), 0), new(ProblemFactKind.Related, this.FactSite(function), 1)]);
                }

                proof = CombineProof(proof, compatibility, true);
                var witness = new BoundWitness(identity, selected, this.FunctionWitness(conformance, identity));
                conformance.WitnessStorage.Add(witness);
                conformance.WitnessMap.Add(identity, witness);
            }

            proof = UnsupportedProof(conformance, proof);
            // R7-C3 C3-1b (F's C2 rulings 289-290, R1; SPEC 8.2.3, 23.1.2): in the conformance's own check every check ran and its own
            // Refuted, Error or Unknown verdict keeps its record; a verdict otherwise Proven rests on the conforming scope's Failed
            // premise: the premise leaves it open, and the consult's read is its limit, so the proof scope ends resting on that premise's
            // record (EndProof): unverified, and the conformance clause keeps dev's record, a kept dependent (F's C2 ruling 294, DECISIONS
            // Q13). Every other reader rested at the first branch.
            if (proof == ConstraintProof.Proven)
            {
                this.BeginProof();
                proof = this.EndProof(this.ConsultsFailedPremise(conformance.Scope.Parent) is null ? ConstraintProof.Proven : ConstraintProof.Unknown);
            }

            conformance.IsVerified = proof == ConstraintProof.Proven;
            completed = true;
            return proof;
        }
        finally
        {
            this.EndVerification(conformance, held);
            conformance.Unsupported &= completed; // An interrupted verification records no limit.
            this.conformanceChecks--;
        }

        // The conformance's problem at its use (SPEC 8.6, 23.1.2): its own record beside any other of the use, an inference hole's and an
        // undeclared Callable bound's with their facts, every other one fact-less. Numbered (RecordNumbered): a refinement's ancestor
        // path checks its own requirements at the same use before the child checks its own, so two failed requirements of one code
        // (two IncompatibleImplementation problems, a premise's and another's) are two problems; an equal one is its first's.
        ConstraintProof Invalid(BindingFailure failure, ReadOnlySpan<ProblemFact> facts)
        {
            conformance.Invalid = true;
            this.RecordNumbered(conformance.Use, failure, facts);
            return ConstraintProof.Error;
        }

        // SPEC 23.1.2: the conformance rests on a specification that did not bind or failed. It may fail only later, as its formation
        // at the Definition deadline, so the link is kept whatever its state is now and settled at publication; the failure is derived
        // only when the specification has failed (IsDerived).
        ConstraintProof RestOnSpecification(IsKoto clause)
        {
            this.partPrerequisites[conformance.Use] = clause;
            (this.specificationLinks ??= new()).Add(conformance.Use);
            return Invalid(BindingFailure.InvalidAssociatedType, []);
        }
    }

    // SPEC 8.6.3, 8.3.3 Finality (R7-C2e2 e2-3b Revision 4): a requirement's evidence, that the specified Types meet a Contract clause (or a
    // specification's obligation): one answer holding pending the obligations open when it began (BeginEvidence), so a part of the
    // specified Types whose route depends on one stays the projection there (HeldPending) and only a judgment reading that part reads it
    // pending; Unknown when the answer depends on one (it supplies none). The clause is instantiated for the conforming Type inside it.
    private ConstraintProof ProveRequirement(BoundConstraint written, BindingScope scope, BoundType self)
    {
        var mark = this.BeginEvidence();
        var verdict = ConstraintProof.Unknown;
        try
        {
            verdict = this.ProveConstraint(this.ContractConstraint(written, scope, self), scope);
        }
        finally
        {
            verdict = this.EndEvidence(mark, verdict);
        }

        return verdict;
    }

    // SPEC 8.5.1, 8.6.3: a conforming Type's specification clause, by conjunction elimination (8.3.2). The identity atom the binding is
    // formed from (`binding`, its first candidate) is the binding itself, a formation read (the Types it names are formed where written,
    // after the conformance holds); every other conjunct is an obligation of the conformance, a requirement's evidence: a further identity
    // atom (its consequence, an identity between Types, is checked and never assumed) or any other conjunct. One proof, as the clause's.
    private ConstraintProof ProveSpecification(BoundConstraint written, BindingScope scope, BoundType self, BoundType? binding)
    {
        this.BeginProof();
        return this.EndProof(Conjunct(written));

        ConstraintProof Conjunct(BoundConstraint conjunct)
            => conjunct.Kind switch
            {
                ConstraintKind.And => CombineProof(Conjunct(conjunct.Left!), Conjunct(conjunct.Right!), true),
                ConstraintKind.TypeIdentity when ReferenceEquals(conjunct.RequiredType, binding) => this.ProveConstraint(this.ContractConstraint(conjunct, scope, self), scope),
                _ => this.ProveRequirement(conjunct, scope, self),
            };
    }

    private bool? MatchesRequirement(FunctionKoto requirement, FunctionKoto implementation, BoundType self, BindingScope scope, MemberSelection selection)
    {
        var demanded = new FnSig(requirement);
        var declared = new FnSig(implementation);
        if (implementation.IsSpecialization || demanded.GenericCount != declared.GenericCount || !SameGenericShape(requirement, implementation) || demanded.InputCount != declared.InputCount)
        {
            return false;
        }

        var pending = false;
        for (var i = 0; i < demanded.InputCount; i++)
        {
            var a = requirement.Parameters[i];
            var b = implementation.Parameters[i];
            if ((a.InternalName == "self") != (b.InternalName == "self") || a.ExternalName != b.ExternalName)
            {
                return false;
            }

            if (demanded.Input(i) is not { } required || declared.Input(i) is not { } actual)
            {
                pending = true;
                continue;
            }

            required = this.ContractType(required, scope, self);
            if (i == demanded.ReceiverIndex && selection.Path is not null)
            {
                required = this.ProjectRequirementReceiver(required, self, selection.DeclaringType)!;
                if (required is null)
                {
                    return false;
                }
            }

            actual = this.MemberType(actual, selection.DeclaringType)!;
            if (actual is null)
            {
                pending = true;
                continue;
            }

            actual = this.ContractType(actual, scope);
            if (this.PendingAssociatedType(required) || this.PendingAssociatedType(actual))
            {
                pending = true;
                continue;
            }

            if (!SignatureEquals(required, actual, demanded.Binder, declared.Binder))
            {
                return false;
            }
        }

        return pending ? null : true;
    }

    // SPEC 8.3.1: a requirement or an implementation whose declaration failed supplies no evidence, a function's or an accessor's alike.
    private bool SuppliesNoEvidence(FnSig requirement, FnSig implementation)
        => this.InvalidDeclarationContext(requirement.Binder) || this.InvalidDeclarationContext(implementation.Binder);

    // An implementation's compatibility with a requirement, and with a Refuted verdict the first of the implementation's bounded Callable
    // clauses its requirement's premises do not declare (SPEC 8.11.4), a value the verdict carries: a return without a verdict carries
    // none.
    private (ConstraintProof Proof, IsKoto? Bound) CompatibleRequirement(BoundConformancePath conformance, FunctionKoto requirement, FunctionKoto implementation, BoundType self, BindingScope scope, MemberSelection selection)
    {
        if (this.SuppliesNoEvidence(new(requirement), new(implementation)) || ((implementation.Modifier & ModifierKind.Unsafe) != 0 && (requirement.Modifier & ModifierKind.Unsafe) == 0) || !this.ConformanceAccessible(conformance, implementation))
        {
            return (ConstraintProof.Error, null);
        }

        var identity = new BoundRequirement(requirement.BoundSymbol!, conformance.Contract);
        var premises = WitnessScope(conformance, identity, requirement);
        premises.Reset();
        premises.Parent = scope;
        var environment = premises.Constraints ??= new();
        for (var i = 0; i < requirement.TypeConstraints.Count; i++)
        {
            if (((IsKoto)requirement.TypeConstraints[i]).BoundConstraint is not { } constraint)
            {
                return (ConstraintProof.Unknown, null);
            }

            this.AddConstraintFact(environment, this.ContractConstraint(constraint, premises, self));
        }

        this.ExpandScopeContractPremises(premises);

        var arguments = this.typeScratch.Rent(requirement.GenericArguments.Count);
        var origins = this.originScratch.Rent(implementation.Origins.Count);
        var inputs = this.originScratch.Rent(InputOriginCount(implementation));
        Array.Clear(origins, 0, implementation.Origins.Count);
        Array.Clear(inputs, 0, InputOriginCount(implementation));
        try
        {
            for (var i = 0; i < requirement.GenericArguments.Count; i++)
            {
                arguments[i] = requirement.BoundSymbol!.Schema!.GenericSlots[i].Symbol.WholeType;
            }

            // SPEC 15.3.7: the implementation's Origins are solved first (CompareCallableContracts), and its clauses are read under the
            // one substitution its signature is (PLAN G700, G707).
            var proof = CombineProof(
                this.ProveMemberConditions(implementation.BoundSymbol!, selection.DeclaringType, premises),
                this.CompareCallableContracts(new(requirement), new(implementation), premises, self, selection.DeclaringType, arguments, origins, inputs, selection.Path),
                true);
            var substitution = SubstitutionOf(implementation, selection.DeclaringType, arguments.AsSpan(0, requirement.GenericArguments.Count), default, origins, inputs);
            IsKoto? bound = null;
            for (var i = 0; i < implementation.TypeConstraints.Count; i++)
            {
                if (((IsKoto)implementation.TypeConstraints[i]).BoundConstraint is not { } constraint)
                {
                    return (ConstraintProof.Unknown, null);
                }

                var substituted = this.SubstituteConstraint(constraint, substitution);

                proof = CombineProof(proof, this.ProveConstraint(this.ContractConstraint(substituted, premises, self), premises), true);
                var clause = (IsKoto)implementation.TypeConstraints[i];
                if (clause.EffectBounds.Count != 0 && substituted.Kind == ConstraintKind.Callable)
                {
                    var normalized = this.ContractConstraint(substituted, premises, self);
                    var receiver = normalized.Mask == SemanticsMask.Ref ? SemanticsKind.Ref : normalized.Mask == SemanticsMask.Uniq ? SemanticsKind.Uniq : SemanticsKind.Owner;
                    var available = this.AvailableCallableEffects(normalized.Subject!, normalized.RequiredType!, receiver, premises);
                    for (var e = 0; e < clause.EffectBounds.Count; e++)
                    {
                        if (!this.AppliesBound(clause.EffectBounds[e]))
                        {
                            continue; // An invalid item declares no obligation for the implementation.
                        }

                        if (!available)
                        {
                            proof = CombineProof(proof, ConstraintProof.Refuted, true);
                            bound ??= clause;
                        }
                    }
                }
            }

            var witness = this.FunctionWitness(conformance, identity);
            witness.DeclaringType = selection.DeclaringType!;
            witness.BasePath = selection.Path;
            witness.RequirementReceiver = requirement.BoundSymbol!.ReceiverIndex is var receiverIndex && receiverIndex >= 0 ? this.ContractType(requirement.Parameters[receiverIndex].Type.BoundType!, premises, self) : null;
            witness.ImplementationReceiver = implementation.BoundSymbol!.ReceiverIndex is var implementationIndex && implementationIndex >= 0 ? this.CallType(implementation.Parameters[implementationIndex].Type.BoundType!, implementation, arguments, premises, null, origins, inputs, selection.DeclaringType) : null;
            witness.SetOrigins(origins.AsSpan(0, implementation.Origins.Count), inputs.AsSpan(0, InputOriginCount(implementation)));
            witness.ObjectCompatibility = selection.Path is not null && receiverIndex >= 0 ? this.ProjectedReceiverProof(implementation.BoundSymbol!) : ConstraintProof.Proven;
            if (witness.ObjectCompatibility == ConstraintProof.Unknown)
            {
                conformance.Unsupported = true; // SPEC 12.7: OCC-X (UnsupportedProof).
                return (proof, bound);
            }

            return (CombineProof(proof, witness.ObjectCompatibility, true), bound);
        }
        finally
        {
            this.typeScratch.Return(arguments, clearArray: true);
            this.originScratch.Return(inputs, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
        }
    }

    private void MatchResultOrigins(BoundType pattern, BoundType expected, Koto binder, Span<BoundOrigin> origins, Span<BoundOrigin> inputs)
    {
        if (!pattern.CarriesOrigin || !expected.CarriesOrigin)
        {
            return;
        }

        if (pattern.Origin is { } p && expected.Origin is { } a)
        {
            Match(p, a, origins, inputs);
        }

        for (var i = 0; i < Math.Min(pattern.OriginArguments.Count, expected.OriginArguments.Count); i++)
        {
            Match(pattern.OriginArguments[i], expected.OriginArguments[i], origins, inputs);
        }

        for (var i = 0; i < Math.Min(pattern.Components.Count, expected.Components.Count); i++)
        {
            this.MatchResultOrigins(pattern.Components[i], expected.Components[i], binder, origins, inputs);
        }

        // The spans are parameters: a local function cannot capture a span.
        void Match(BoundOrigin parameter, BoundOrigin value, Span<BoundOrigin> origins, Span<BoundOrigin> inputs)
        {
            if (ReferenceEquals(parameter.Binder, binder) && parameter.Kind is OriginKind.Parameter or OriginKind.Input)
            {
                var target = parameter.Kind == OriginKind.Parameter ? origins : inputs;
                if ((uint)parameter.Slot < (uint)target.Length && target[parameter.Slot] is null)
                {
                    target[parameter.Slot] = value;
                }
            }
        }
    }

    // `known` is the binder of an argument's known call signature (SPEC 10.8): the Origins it quantifies, such as per-call inputs,
    // lie beyond the call and are no evidence for the callee's own Origins.
    private void MatchInputOrigins(BoundType pattern, BoundType actual, Koto binder, Span<BoundOrigin> origins, Span<BoundOrigin> inputs, Koto? known = null)
    {
        if (!pattern.CarriesOrigin || !actual.CarriesOrigin)
        {
            return;
        }

        if (pattern.Kind == BoundTypeKind.SemanticsApplication)
        {
            // SPEC 8.1.2: the outer slot of s/U binds only when s is a borrow, so s/U is matched only once s is inferred and
            // the application is formed (FormedApplication); an unformed application binds nothing.
            return;
        }

        if (pattern.Origin is { } p && actual.Origin is { } a)
        {
            this.MatchInputOrigin(p, a, binder, origins, inputs, known);
        }

        for (var i = 0; i < Math.Min(pattern.OriginArguments.Count, actual.OriginArguments.Count); i++)
        {
            this.MatchInputOrigin(pattern.OriginArguments[i], actual.OriginArguments[i], binder, origins, inputs, known);
        }

        for (var i = 0; i < Math.Min(pattern.Components.Count, actual.Components.Count); i++)
        {
            this.MatchInputOrigins(pattern.Components[i], actual.Components[i], binder, origins, inputs, known);
        }
    }

    private void MatchInputOrigin(BoundOrigin pattern, BoundOrigin actual, Koto binder, Span<BoundOrigin> origins, Span<BoundOrigin> inputs, Koto? known = null)
    {
        if (known is not null && QuantifiedBy(actual, known))
        {
            return;
        }

        if (pattern.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < pattern.Operands.Count; i++)
            {
                this.MatchInputOrigin(pattern.Operands[i], actual, binder, origins, inputs, known);
            }
        }
        else if (ReferenceEquals(pattern.Binder, binder) && pattern.Kind is OriginKind.Parameter or OriginKind.Input)
        {
            // Anonymous aggregate input slots are recorded while parameter Types bind, so a
            // pattern can outrun the width the caller reserved. Skip what cannot be carried.
            var target = pattern.Kind == OriginKind.Parameter ? origins : inputs;
            if ((uint)pattern.Slot < (uint)target.Length)
            {
                target[pattern.Slot] = target[pattern.Slot] is { } previous ? this.Meet(previous, actual) : actual;
            }
        }
        else if (pattern.Kind == OriginKind.Parameter && binder is DeclarationContainerKoto && binder.BoundSymbol?.Schema is { } schema)
        {
            for (var i = 0; i < schema.Origins.Count && i < origins.Length; i++)
            {
                if (ReferenceEquals(schema.Origins[i].Origin, pattern))
                {
                    origins[i] = origins[i] is { } previous ? this.Meet(previous, actual) : actual;
                    break;
                }
            }
        }
    }

    private bool ConformanceAccessible(BoundConformancePath conformance, FunctionKoto implementation)
        => this.AccessCovers(implementation.BoundSymbol!, conformance.Type, conformance.Contract);
}
