// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(BoundConformancePath Conformance, BoundRequirement Requirement), BindingScope> witnessScopes = new();
    private Dictionary<Koto, BindingSymbol>? pendingExclusiveLimits;

    private static bool SameGenericShape(FunctionKoto requirement, FunctionKoto implementation)
    {
        if (requirement.GenericArguments.Count != implementation.GenericArguments.Count)
        {
            return false;
        }

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

    private static bool TypeAccessCovers(BoundType type, BindingSymbol a, BindingSymbol b)
    {
        if (type.Symbol is { Kind: BindingSymbolKind.Type } symbol && !AccessCovers(symbol, a, b))
        {
            return false;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (!TypeAccessCovers(type.Components[i], a, b))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AccessCovers(BindingSymbol member, BindingSymbol a, BindingSymbol b, ModifierKind? memberAccess = null)
    {
        for (var current = member; current is not null; current = EnclosingAccessDeclaration(current))
        {
            if (current.Declaration is DeclarationContainerKoto { IsRoot: true })
            {
                break;
            }

            var access = ReferenceEquals(current, member) && memberAccess is { } specific ? specific : DeclarationAccess(current);
            if (access == ModifierKind.Public)
            {
                continue;
            }

            var module = current.Declaration.CodeContext.Kotonoha;
            var internalDomain = (!ExternallyVisible(a) && ReferenceEquals(a.Declaration.CodeContext.Kotonoha, module)) || (!ExternallyVisible(b) && ReferenceEquals(b.Declaration.CodeContext.Kotonoha, module));
            var lexical = PrivateDomainWithin(a, current.Scope.Owner) || PrivateDomainWithin(b, current.Scope.Owner);
            var covered = access switch
            {
                ModifierKind.Internal or ModifierKind.ProtectedOrInternal => internalDomain || lexical,
                ModifierKind.ProtectedAndInternal => internalDomain && lexical,
                _ => lexical,
            };
            if (!covered)
            {
                return false;
            }
        }

        return true;
    }

    private static ModifierKind DeclarationAccess(BindingSymbol symbol)
        => (symbol.Declaration switch
        {
            DeclarationContainerKoto container => container.Modifier,
            FunctionKoto function => function.Modifier,
            PropertyAccessorKoto accessor => accessor.Modifier.ExtractAccessibilityModifiers() != ModifierKind.NoModifier ? accessor.Modifier : ((PropertyKoto)accessor.Parent!).Modifier,
            VariableKoto variable => variable.Modifier,
            _ => ModifierKind.Public,
        }).ExtractAccessibilityModifiers();

    private static bool PrivateDomainWithin(BindingSymbol symbol, Koto owner)
    {
        if (owner is DeclarationContainerKoto { IsRoot: true })
        {
            return !ExternallyVisible(symbol) && ReferenceEquals(owner.CodeContext.Kotonoha, symbol.Declaration.CodeContext.Kotonoha);
        }

        for (var current = symbol; current is not null; current = EnclosingAccessDeclaration(current))
        {
            if (current.Declaration is DeclarationContainerKoto { IsRoot: true })
            {
                break;
            }

            if (DeclarationAccess(current) is not (ModifierKind.Private or ModifierKind.NoModifier))
            {
                continue;
            }

            for (var scope = current.Scope; scope is not null; scope = scope.Parent)
            {
                if (ReferenceEquals(scope.Owner, owner))
                {
                    return true;
                }
            }
        }

        return false;
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

    // SPEC 12.4.4.1: a verification whose witnesses wait for OCC-X is not Proven; when nothing else is unproven, its only cause is that
    // wait, which the conformance and its uses report as a located limit rather than an unproven Constraint.
    private static ConstraintProof PendingExclusiveProof(BoundConformancePath path, ConstraintProof proof)
    {
        if (path.PendingExclusive is null)
        {
            return proof;
        }

        path.PendingExclusiveOnly = proof == ConstraintProof.Proven;
        return CombineProof(proof, ConstraintProof.Unknown, true);
    }

    private ConstraintProof VerifyConformance(BoundConformancePath conformance, ConstraintProof? inheritedProof = null)
    {
        if (this.collectingAssociated is not null && this.associatedInference.TryGetValue(conformance.Type, out var batch) && batch.State != 2)
        {
            return ConstraintProof.Unknown; // Type-value scheduling never certifies a collecting definition.
        }

        if (conformance.InheritedFrom is not null)
        {
            return this.VerifyInheritedConformance(conformance);
        }

        if (conformance.Identity.Invalid || conformance.Invalid || conformance.Declaration.BindingState == BindingState.Invalid || conformance.Scope.Parent?.Constraints?.Invalid == true || InvalidDeclarationContext(conformance.Contract.Declaration) || InvalidDeclarationContext(conformance.Type.Declaration))
        {
            conformance.IsVerified = false;
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

        if (conformance.Checking)
        {
            return ConstraintProof.Unknown;
        }

        conformance.Checking = true;
        conformance.WitnessStorage.Clear();
        conformance.WitnessMap.Clear();
        conformance.PropertyWitnessStorage.Clear();
        conformance.PropertyWitnessMap.Clear();
        conformance.PendingExclusive = null;
        conformance.PendingExclusiveOnly = false;
        try
        {
            var shape = conformance.Contract.Contract!;
            var scope = conformance.Scope;
            var self = this.ContractType(this.SelfType(conformance.Type), scope);
            var declarationProof = this.CheckClosedDeclarationConstraints((DeclarationContainerKoto)conformance.Type.Declaration);
            if (!this.ValidArithmeticConformance(conformance, self))
            {
                return ConstraintProof.Error;
            }

            if (conformance.Premises is { } premises)
            {
                for (var p = 0; p < premises.Operands.Length; p++)
                {
                    if (premises.Operands[p] is IsKoto { BoundConstraint: { } constraint } clause && !ConstraintAccessCovers(constraint, conformance.Type, conformance.Contract))
                    {
                        this.Fail(clause, BindingFailure.Access);
                        this.Fail(premises.Parent!, BindingFailure.Access);
                        return Invalid(BindingFailure.Access);
                    }
                }
            }

            if (conformance.Contract.Intrinsic is IntrinsicKind.Copy or IntrinsicKind.Owned or IntrinsicKind.Sealed)
            {
                var intrinsicProof = CombineProof(declarationProof, this.RequestCapability(self, conformance.Contract, scope, derivation: conformance.Contract.Intrinsic == IntrinsicKind.Copy), true);
                conformance.IsVerified = intrinsicProof == ConstraintProof.Proven;
                return intrinsicProof;
            }

            var proof = declarationProof;
            for (var i = 0; i < shape.AssociatedTypes.Count; i++)
            {
                if (!conformance.AssociatedStorage.TryGetValue(shape.AssociatedTypes[i], out var associated))
                {
                    var identity = shape.AssociatedTypes[i];
                    if (this.InferenceHole(conformance, identity))
                    {
                        var binding = this.associatedBindings[(conformance.RootPath, identity)];
                        if (binding.Problem is InferenceProblem.Missing or InferenceProblem.Ambiguous)
                        {
                            return Invalid(binding.Problem == InferenceProblem.Missing ? BindingFailure.MissingImplementation : BindingFailure.Ambiguous);
                        }

                        this.inferenceFailures.TryAdd(conformance.Use, (identity, binding));
                        return Invalid(BindingFailure.AssociatedInference);
                    }

                    return Invalid(BindingFailure.InvalidAssociatedType);
                }

                if (!TypeAccessCovers(associated, conformance.Type, conformance.Contract))
                {
                    return Invalid(BindingFailure.Access);
                }

                // Normalized identity/Core shape alone does not prove nested input constraints.
                var formationScope = this.AssociatedFormationScope(conformance, shape.AssociatedTypes[i], scope);
                proof = CombineProof(proof, this.CheckTypeConstraints(associated, formationScope), true);
                var inputs = this.associatedBindings[(conformance.RootPath, shape.AssociatedTypes[i])].Candidates;
                for (var input = 0; input < inputs.Count; input++)
                {
                    // Projection normalization may erase an invalid constructed qualifier.
                    proof = CombineProof(proof, this.CheckTypeConstraints(inputs[input], formationScope), true);
                }
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

                        ancestorProofs[i] = this.VerifyConformance(this.conformancePaths[(conformance.Type, ancestor, conformance.Declaration, conformance.RootContract)], prerequisites);
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

                var clauseScope = this.AssociatedFormationScope(conformance, this.AssociatedIdentity(shape.ClauseStorage[i].Left.BoundType, shape.Symbol), scope);
                proof = CombineProof(proof, this.ProveConstraint(this.ContractConstraint(constraint, clauseScope, self), clauseScope), true);
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
                            return Invalid(BindingFailure.InvalidAssociatedType);
                        }

                        if (clause.BoundConstraint is { } constraint && this.AssociatedIdentity(clause.Left.BoundType) is { } identity && shape.SeenRequirements.Contains(identity))
                        {
                            var clauseScope = this.AssociatedFormationScope(conformance, identity, scope);
                            proof = CombineProof(proof, this.ProveConstraint(this.ContractConstraint(constraint, clauseScope, self), clauseScope), true);
                        }
                    }
                }
            }

            for (var i = 0; i < container.Members.Count; i++)
            {
                if (container.Members[i] is IsKoto { IsAssociatedConstraint: true, BoundConstraint: { } constraint } clause && this.AssociatedIdentity(clause.Left.BoundType) is { } identity && shape.SeenRequirements.Contains(identity) &&
                    SpecifiesContract(clause, conformance.Contract))
                {
                    if (clause.BindingState == BindingState.Invalid)
                    {
                        return Invalid(BindingFailure.InvalidAssociatedType);
                    }

                    var clauseScope = this.AssociatedFormationScope(conformance, identity, scope);
                    proof = CombineProof(proof, this.ProveConstraint(this.ContractConstraint(constraint, clauseScope, self), clauseScope), true);
                }
            }

            for (var i = 0; i < shape.Requirements.Count; i++)
            {
                var identity = shape.Requirements[i];
                var requirement = identity.Symbol;
                if (!ReferenceEquals(identity.Contract, conformance.Contract))
                {
                    var ancestor = this.conformancePaths[(conformance.Type, identity.Contract, conformance.Declaration, conformance.RootContract)];
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
                        return Invalid(BindingFailure.IncompatibleImplementation);
                    }

                    proof = CombineProof(proof, propertyProof, true);
                    continue;
                }

                if (requirement.Declaration is not FunctionKoto function)
                {
                    return ConstraintProof.Unknown;
                }

                var match = this.IdentifyRequirement(conformance, identity, self);
                if (match.State == RequirementMatchState.Ambiguous)
                {
                    return Invalid(BindingFailure.Ambiguous);
                }

                if (match.State == RequirementMatchState.Pending)
                {
                    proof = CombineProof(proof, ConstraintProof.Unknown, true);
                    continue;
                }

                if (match.State == RequirementMatchState.Missing)
                {
                    return Invalid(BindingFailure.MissingImplementation);
                }

                var selected = match.Member!;
                var selection = match.Selection;
                var compatibility = this.CompatibleRequirement(conformance, function, (FunctionKoto)selected.Declaration, self, scope, selection);
                if (compatibility is ConstraintProof.Error or ConstraintProof.Refuted)
                {
                    return Invalid(BindingFailure.IncompatibleImplementation);
                }

                proof = CombineProof(proof, compatibility, true);
                var witness = new BoundWitness(identity, selected, this.FunctionWitness(conformance, identity));
                conformance.WitnessStorage.Add(witness);
                conformance.WitnessMap.Add(identity, witness);
            }

            proof = PendingExclusiveProof(conformance, proof);
            conformance.IsVerified = proof == ConstraintProof.Proven;
            return proof;
        }
        finally
        {
            conformance.Checking = false;
        }

        ConstraintProof Invalid(BindingFailure failure)
        {
            conformance.Invalid = true;
            this.Fail(conformance.Use, failure);
            return ConstraintProof.Error;
        }
    }

    private bool? MatchesRequirement(FunctionKoto requirement, FunctionKoto implementation, BoundType self, BindingScope scope, MemberSelection selection)
    {
        if (implementation.IsSpecialization || !SameGenericShape(requirement, implementation) || requirement.Parameters.Count != implementation.Parameters.Count || ResultModeOf(requirement.ReturnType) != ResultModeOf(implementation.ReturnType))
        {
            return false;
        }

        var pending = false;
        for (var i = 0; i < requirement.Parameters.Count; i++)
        {
            var a = requirement.Parameters[i];
            var b = implementation.Parameters[i];
            if ((a.InternalName == "self") != (b.InternalName == "self") || a.ExternalName != b.ExternalName)
            {
                return false;
            }

            if (a.Type.BoundType is not { } required || b.Type.BoundType is not { } actual)
            {
                pending = true;
                continue;
            }

            required = this.ContractType(required, scope, self);
            if (i == requirement.BoundSymbol!.ReceiverIndex && selection.Path is not null)
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

            if (!SignatureEquals(required, actual, requirement, implementation))
            {
                return false;
            }
        }

        return pending ? null : true;
    }

    private ConstraintProof CompatibleRequirement(BoundConformancePath conformance, FunctionKoto requirement, FunctionKoto implementation, BoundType self, BindingScope scope, MemberSelection selection)
    {
        if (requirement.BindingState == BindingState.Invalid || implementation.BindingState == BindingState.Invalid || ((implementation.Modifier & ModifierKind.Unsafe) != 0 && (requirement.Modifier & ModifierKind.Unsafe) == 0) || !this.ConformanceAccessible(conformance, implementation))
        {
            return ConstraintProof.Error;
        }

        var formation = CombineProof(this.CheckSignatureTypeConstraints(requirement), this.CheckSignatureTypeConstraints(implementation), true);
        if (formation != ConstraintProof.Proven)
        {
            return formation;
        }

        // SPEC 7.1.1, 8.4.5: the retained identification preserves the result category and mode.
        if (ResultModeOf(requirement.ReturnType) != ResultModeOf(implementation.ReturnType))
        {
            return ConstraintProof.Error;
        }

        var identity = new BoundRequirement(requirement.BoundSymbol!, conformance.Contract);
        var key = (conformance, identity);
        if (!this.witnessScopes.TryGetValue(key, out var premises))
        {
            this.witnessScopes.Add(key, premises = new(requirement));
        }

        premises.Reset();
        premises.Parent = scope;
        var environment = premises.Constraints ??= new();
        for (var i = 0; i < requirement.TypeConstraints.Count; i++)
        {
            if (((IsKoto)requirement.TypeConstraints[i]).BoundConstraint is not { } constraint)
            {
                return ConstraintProof.Unknown;
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

            var proof = this.ProveMemberConditions(implementation.BoundSymbol!, selection.DeclaringType, premises);
            for (var i = 0; i < implementation.TypeConstraints.Count; i++)
            {
                if (((IsKoto)implementation.TypeConstraints[i]).BoundConstraint is not { } constraint)
                {
                    return ConstraintProof.Unknown;
                }

                var substituted = this.SubstituteConstraint(constraint, implementation, arguments.AsSpan(0, requirement.GenericArguments.Count));
                if (selection.DeclaringType is { Symbol.Declaration: { } owner } declaring)
                {
                    substituted = this.SubstituteConstraint(substituted, owner, (BoundType[])declaring.Components);
                }

                proof = CombineProof(proof, this.ProveConstraint(this.ContractConstraint(substituted, premises, self), premises), true);
                var clause = (IsKoto)implementation.TypeConstraints[i];
                if (clause.EffectBounds.Count != 0 && substituted.Kind == ConstraintKind.Callable)
                {
                    var normalized = this.ContractConstraint(substituted, premises, self);
                    var receiver = normalized.Mask == SemanticsMask.Ref ? SemanticsKind.Ref : normalized.Mask == SemanticsMask.Uniq ? SemanticsKind.Uniq : SemanticsKind.Owner;
                    var available = this.AvailableCallableEffects(normalized.Subject!, normalized.RequiredType!, receiver, premises);
                    for (var e = 0; e < clause.EffectBounds.Count; e++)
                    {
                        var bound = clause.EffectBounds[e];
                        if (this.effectBoundRejections?.ContainsKey(bound) == true || IsRecovery(bound, out _))
                        {
                            continue; // An invalid item declares no obligation for the implementation.
                        }

                        if (!(bound.Bound == EffectBoundKind.Confined ? available.Confined : available.Preserves))
                        {
                            proof = CombineProof(proof, ConstraintProof.Refuted, true);
                            this.callablePremiseFailures.TryAdd(conformance.Use, (clause, bound, requirement));
                        }
                    }
                }
            }

            proof = CombineProof(proof, this.CompareCallableContracts(new(requirement), new(implementation), premises, self, selection.DeclaringType, arguments, origins, inputs, selection.Path), true);
            var witness = this.FunctionWitness(conformance, identity);
            witness.DeclaringType = selection.DeclaringType!;
            witness.BasePath = selection.Path;
            witness.RequirementReceiver = requirement.BoundSymbol!.ReceiverIndex is var receiverIndex && receiverIndex >= 0 ? this.ContractType(requirement.Parameters[receiverIndex].Type.BoundType!, premises, self) : null;
            witness.ImplementationReceiver = implementation.BoundSymbol!.ReceiverIndex is var implementationIndex && implementationIndex >= 0 ? this.CallType(implementation.Parameters[implementationIndex].Type.BoundType!, implementation, arguments, premises, null, origins, inputs, selection.DeclaringType) : null;
            witness.SetOrigins(origins.AsSpan(0, implementation.Origins.Count), inputs.AsSpan(0, InputOriginCount(implementation)));
            witness.ObjectCompatibility = selection.Path is not null && receiverIndex >= 0 ? ProjectedReceiverProof(implementation.BoundSymbol!) : ConstraintProof.Proven;
            if (witness.ObjectCompatibility == ConstraintProof.Unknown)
            {
                // SPEC 12.4.4.1: an exclusive receiver projected to its base waits for OCC-X; the verification adds it as its own cause.
                conformance.PendingExclusive ??= implementation.BoundSymbol;
                return proof;
            }

            return CombineProof(proof, witness.ObjectCompatibility, true);
        }
        finally
        {
            this.typeScratch.Return(arguments, clearArray: true);
            this.originScratch.Return(inputs, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
        }
    }

    private void MatchResultOrigins(BoundType pattern, BoundType expected, Koto binder, BoundOrigin[] origins, BoundOrigin[] inputs)
    {
        if (!pattern.CarriesOrigin || !expected.CarriesOrigin)
        {
            return;
        }

        if (pattern.Origin is { } p && expected.Origin is { } a)
        {
            Match(p, a);
        }

        for (var i = 0; i < Math.Min(pattern.OriginArguments.Count, expected.OriginArguments.Count); i++)
        {
            Match(pattern.OriginArguments[i], expected.OriginArguments[i]);
        }

        for (var i = 0; i < Math.Min(pattern.Components.Count, expected.Components.Count); i++)
        {
            this.MatchResultOrigins(pattern.Components[i], expected.Components[i], binder, origins, inputs);
        }

        void Match(BoundOrigin parameter, BoundOrigin value)
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
    private void MatchInputOrigins(BoundType pattern, BoundType actual, Koto binder, BoundOrigin[] origins, BoundOrigin[] inputs, Koto? known = null)
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

    private void MatchInputOrigin(BoundOrigin pattern, BoundOrigin actual, Koto binder, BoundOrigin[] origins, BoundOrigin[] inputs, Koto? known = null)
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
        => AccessCovers(implementation.BoundSymbol!, conformance.Type, conformance.Contract);
}
