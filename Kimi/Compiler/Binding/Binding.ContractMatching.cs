// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(BoundConformancePath Conformance, BindingSymbol Requirement), BindingScope> witnessScopes = new();

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
        for (var current = member; current is not null; current = current.Scope.Owner.BoundSymbol)
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

        for (var current = symbol; current is not null; current = current.Scope.Owner.BoundSymbol)
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
        for (var current = symbol; current is not null; current = current.Scope.Owner.BoundSymbol)
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

    private ConstraintProof VerifyConformance(BoundConformancePath conformance, ConstraintProof? inheritedProof = null)
    {
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
        try
        {
            var shape = conformance.Contract.Contract!;
            var scope = conformance.Scope;
            var self = this.ContractType(this.SelfType(conformance.Type), scope);
            var declarationProof = this.CheckClosedDeclarationConstraints((DeclarationContainerKoto)conformance.Type.Declaration);
            if (conformance.Premises is { } premises)
            {
                for (var p = 0; p < premises.Operands.Length; p++)
                {
                    if (premises.Operands[p] is IsKoto { BoundConstraint: { } constraint } clause && !ConstraintAccessCovers(constraint, conformance.Type, conformance.Contract))
                    {
                        Fail(clause, BindingFailure.Access);
                        Fail(premises.Parent!, BindingFailure.Access);
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
                    return Invalid(BindingFailure.InvalidAssociatedType);
                }

                if (!TypeAccessCovers(associated, conformance.Type, conformance.Contract))
                {
                    return Invalid(BindingFailure.Access);
                }

                // Normalized identity/Core shape alone does not prove nested input constraints.
                proof = CombineProof(proof, this.CheckTypeConstraints(associated, scope), true);
                var inputs = this.associatedBindings[(conformance.RootPath, shape.AssociatedTypes[i])].Candidates;
                for (var input = 0; input < inputs.Count; input++)
                {
                    // Projection normalization may erase an invalid constructed qualifier.
                    proof = CombineProof(proof, this.CheckTypeConstraints(inputs[input], scope), true);
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

                proof = CombineProof(proof, this.ProveConstraint(this.ContractConstraint(constraint, scope, self), scope), true);
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

                        if (clause.BoundConstraint is { } constraint && shape.AssociatedStorage.Contains(clause.BoundSymbol!))
                        {
                            proof = CombineProof(proof, this.ProveConstraint(this.ContractConstraint(constraint, scope, self), scope), true);
                        }
                    }
                }
            }

            for (var i = 0; i < container.Members.Count; i++)
            {
                if (container.Members[i] is IsKoto { IsAssociatedConstraint: true, BoundConstraint: { } constraint } clause && shape.AssociatedStorage.Contains(clause.BoundSymbol!))
                {
                    if (clause.BindingState == BindingState.Invalid)
                    {
                        return Invalid(BindingFailure.InvalidAssociatedType);
                    }

                    proof = CombineProof(proof, this.ProveConstraint(this.ContractConstraint(constraint, scope, self), scope), true);
                }
            }

            for (var i = 0; i < shape.Requirements.Count; i++)
            {
                var requirement = shape.Requirements[i];
                if (!ReferenceEquals(requirement.Scope.Owner, conformance.Contract.Declaration))
                {
                    var ancestor = this.conformancePaths[(conformance.Type, requirement.Scope.Owner.BoundSymbol!, conformance.Declaration, conformance.RootContract)];
                    if (ancestor.GetImplementation(requirement) is { } inherited)
                    {
                        var inheritedWitness = ancestor.WitnessMap[requirement];
                        conformance.WitnessStorage.Add(inheritedWitness);
                        conformance.WitnessMap.Add(requirement, inheritedWitness);
                        for (var w = 0; w < ancestor.PropertyWitnessStorage.Count; w++)
                        {
                            var operation = ancestor.PropertyWitnessStorage[w];
                            if (ReferenceEquals(operation.Requirement.Property.Symbol, requirement))
                            {
                                conformance.PropertyWitnessStorage.Add(operation);
                                conformance.PropertyWitnessMap.Add((requirement, operation.Requirement.Kind), operation);
                            }
                        }
                    }
                    else
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

                BindingSymbol? selected = null;
                var matches = 0;
                var pending = false;
                var selection = this.LookupTypeMember(self, requirement.Name, scope, self);
                if (selection.Ambiguous)
                {
                    return Invalid(BindingFailure.Ambiguous);
                }

                pending |= selection.Pending;
                for (var candidate = selection.Member; candidate is not null; candidate = candidate.Next)
                {
                    if (candidate.Declaration is not FunctionKoto implementation || !this.Accessible(candidate, scope, receiverType: self))
                    {
                        continue;
                    }

                    this.BindHeader(candidate);
                    var match = this.MatchesRequirement(function, implementation, self, scope, selection);
                    pending |= match is null;
                    if (match == true)
                    {
                        matches++;
                        selected = candidate;
                    }
                }

                if (matches > 1)
                {
                    return Invalid(BindingFailure.Ambiguous);
                }

                if (pending)
                {
                    proof = CombineProof(proof, ConstraintProof.Unknown, true);
                    continue;
                }

                if (selected is null)
                {
                    return Invalid(BindingFailure.MissingImplementation);
                }

                var compatibility = this.CompatibleRequirement(conformance, function, (FunctionKoto)selected.Declaration, self, scope, selection);
                if (compatibility is ConstraintProof.Error or ConstraintProof.Refuted)
                {
                    return Invalid(BindingFailure.IncompatibleImplementation);
                }

                proof = CombineProof(proof, compatibility, true);
                var witness = new BoundWitness(requirement, selected, this.FunctionWitness(conformance, requirement));
                conformance.WitnessStorage.Add(witness);
                conformance.WitnessMap.Add(requirement, witness);
            }

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
            Fail(conformance.Use, failure);
            return ConstraintProof.Error;
        }
    }

    private bool? MatchesRequirement(FunctionKoto requirement, FunctionKoto implementation, BoundType self, BindingScope scope, MemberSelection selection)
    {
        if (implementation.IsSpecialization || !SameGenericShape(requirement, implementation) || requirement.Parameters.Count != implementation.Parameters.Count)
        {
            return false;
        }

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
                return null;
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
                return null;
            }

            if (!SignatureEquals(required, this.ContractType(actual, scope), requirement, implementation))
            {
                return false;
            }
        }

        return true;
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

        var key = (conformance, requirement.BoundSymbol!);
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
            }

            proof = CombineProof(proof, this.CompareCallableContracts(new(requirement), new(implementation), premises, self, selection.DeclaringType, arguments, origins, inputs, selection.Path), true);
            var witness = this.FunctionWitness(conformance, requirement.BoundSymbol!);
            witness.DeclaringType = selection.DeclaringType!;
            witness.BasePath = selection.Path;
            witness.RequirementReceiver = requirement.BoundSymbol!.ReceiverIndex is var receiverIndex && receiverIndex >= 0 ? this.ContractType(requirement.Parameters[receiverIndex].Type.BoundType!, premises, self) : null;
            witness.ImplementationReceiver = implementation.BoundSymbol!.ReceiverIndex is var implementationIndex && implementationIndex >= 0 ? this.CallType(implementation.Parameters[implementationIndex].Type.BoundType!, implementation, arguments, premises, null, origins, inputs, selection.DeclaringType) : null;
            witness.SetOrigins(origins.AsSpan(0, implementation.Origins.Count), inputs.AsSpan(0, InputOriginCount(implementation)));
            witness.ObjectCompatibility = selection.Path is not null && receiverIndex >= 0 ? ProjectedReceiverProof(implementation.BoundSymbol!) : ConstraintProof.Proven;
            return CombineProof(proof, witness.ObjectCompatibility, true);
        }
        finally
        {
            this.typeScratch.Return(arguments, clearArray: true);
            this.originScratch.Return(inputs, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
        }
    }

    private void MatchInputOrigins(BoundType pattern, BoundType actual, Koto binder, BoundOrigin[] origins, BoundOrigin[] inputs)
    {
        if (!pattern.CarriesOrigin || !actual.CarriesOrigin)
        {
            return;
        }

        if (pattern.Origin is { } p && actual.Origin is { } a)
        {
            this.MatchInputOrigin(p, a, binder, origins, inputs);
        }

        for (var i = 0; i < Math.Min(pattern.OriginArguments.Count, actual.OriginArguments.Count); i++)
        {
            this.MatchInputOrigin(pattern.OriginArguments[i], actual.OriginArguments[i], binder, origins, inputs);
        }

        for (var i = 0; i < Math.Min(pattern.Components.Count, actual.Components.Count); i++)
        {
            this.MatchInputOrigins(pattern.Components[i], actual.Components[i], binder, origins, inputs);
        }
    }

    private void MatchInputOrigin(BoundOrigin pattern, BoundOrigin actual, Koto binder, BoundOrigin[] origins, BoundOrigin[] inputs)
    {
        if (pattern.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < pattern.Operands.Count; i++)
            {
                this.MatchInputOrigin(pattern.Operands[i], actual, binder, origins, inputs);
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
