// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(BoundConformance Conformance, BindingSymbol Requirement), BindingScope> witnessScopes = new();

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
        => (ModifierKind)((byte)(symbol.Declaration switch
        {
            DeclarationContainerKoto container => container.Modifier,
            FunctionKoto function => function.Modifier,
            PropertyAccessorKoto accessor => ((byte)accessor.Modifier & 7) != 0 ? accessor.Modifier : ((PropertyKoto)accessor.Parent!).Modifier,
            VariableKoto variable => variable.Modifier,
            _ => ModifierKind.Public,
        }) & 7);

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

    private ConstraintProof VerifyConformance(BoundConformance conformance)
    {
        if (conformance.Invalid || conformance.Contract.Declaration.BindingState == BindingState.Invalid || conformance.Type.Declaration.BindingState == BindingState.Invalid)
        {
            conformance.IsVerified = false;
            return ConstraintProof.Error;
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
            var scope = this.scopes[conformance.Type.Declaration];
            var self = this.SelfType(conformance.Type);
            if (conformance.Contract.Intrinsic is IntrinsicKind.Copy or IntrinsicKind.Owned)
            {
                var intrinsicProof = this.RequestCapability(self, conformance.Contract, scope, derivation: conformance.Contract.Intrinsic == IntrinsicKind.Copy);
                conformance.IsVerified = intrinsicProof == ConstraintProof.Proven;
                return intrinsicProof;
            }

            var proof = ConstraintProof.Proven;
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
            }

            for (var i = 0; i < shape.Ancestors.Count; i++)
            {
                proof = CombineProof(proof, this.VerifyConformance(this.conformances[(conformance.Type, shape.Ancestors[i])]), true);
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
            for (var i = 0; i < container.Members.Count; i++)
            {
                if (container.Members[i] is IsKoto { IsAssociatedConstraint: true, BoundConstraint: { } constraint } clause && shape.AssociatedStorage.Contains(clause.BoundSymbol!))
                {
                    proof = CombineProof(proof, this.ProveConstraint(this.ContractConstraint(constraint, scope, self), scope), true);
                }
            }

            for (var i = 0; i < shape.Requirements.Count; i++)
            {
                var requirement = shape.Requirements[i];
                if (!ReferenceEquals(requirement.Scope.Owner.BoundSymbol, conformance.Contract))
                {
                    var ancestor = this.conformances[(conformance.Type, requirement.Scope.Owner.BoundSymbol!)];
                    if (ancestor.GetImplementation(requirement) is { } inherited)
                    {
                        conformance.WitnessStorage.Add(new(requirement, inherited));
                        conformance.WitnessMap.Add(requirement, inherited);
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
                for (var candidate = scope.Values.GetValueOrDefault(requirement.Name); candidate is not null; candidate = candidate.Next)
                {
                    if (candidate.Declaration is not FunctionKoto implementation)
                    {
                        continue;
                    }

                    var match = this.MatchesRequirement(function, implementation, self, scope);
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

                var compatibility = this.CompatibleRequirement(conformance, function, (FunctionKoto)selected.Declaration, self, scope);
                if (compatibility is ConstraintProof.Error or ConstraintProof.Refuted)
                {
                    return Invalid(BindingFailure.IncompatibleImplementation);
                }

                proof = CombineProof(proof, compatibility, true);
                conformance.WitnessStorage.Add(new(requirement, selected));
                conformance.WitnessMap.Add(requirement, selected);
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

    private bool? MatchesRequirement(FunctionKoto requirement, FunctionKoto implementation, BoundType self, BindingScope scope)
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

            if (!SignatureEquals(this.ContractType(required, scope, self), this.ContractType(actual, scope), requirement, implementation))
            {
                return false;
            }
        }

        return true;
    }

    private ConstraintProof CompatibleRequirement(BoundConformance conformance, FunctionKoto requirement, FunctionKoto implementation, BoundType self, BindingScope scope)
    {
        if (requirement.BindingState == BindingState.Invalid || implementation.BindingState == BindingState.Invalid || ((implementation.Modifier & ModifierKind.Unsafe) != 0 && (requirement.Modifier & ModifierKind.Unsafe) == 0) || !this.ConformanceAccessible(conformance, implementation))
        {
            return ConstraintProof.Error;
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
        var inputs = this.originScratch.Rent(implementation.Parameters.Count);
        Array.Clear(origins, 0, implementation.Origins.Count);
        Array.Clear(inputs, 0, implementation.Parameters.Count);
        try
        {
            for (var i = 0; i < requirement.GenericArguments.Count; i++)
            {
                arguments[i] = requirement.BoundSymbol!.Schema!.GenericSlots[i].Symbol.WholeType;
            }

            var proof = ConstraintProof.Proven;
            for (var i = 0; i < implementation.TypeConstraints.Count; i++)
            {
                if (((IsKoto)implementation.TypeConstraints[i]).BoundConstraint is not { } constraint)
                {
                    return ConstraintProof.Unknown;
                }

                var substituted = this.SubstituteConstraint(constraint, implementation, arguments.AsSpan(0, requirement.GenericArguments.Count));
                proof = CombineProof(proof, this.ProveConstraint(this.ContractConstraint(substituted, premises, self), premises), true);
            }

            return CombineProof(proof, this.CompareCallableContracts(new(requirement), new(implementation), premises, self, null, arguments, origins, inputs), true);
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
        if (pattern.Origin is { } p && actual.Origin is { } a)
        {
            Match(p, a);
        }

        for (var i = 0; i < Math.Min(pattern.OriginArguments.Count, actual.OriginArguments.Count); i++)
        {
            Match(pattern.OriginArguments[i], actual.OriginArguments[i]);
        }

        for (var i = 0; i < Math.Min(pattern.Components.Count, actual.Components.Count); i++)
        {
            this.MatchInputOrigins(pattern.Components[i], actual.Components[i], binder, origins, inputs);
        }

        void Match(BoundOrigin pattern, BoundOrigin actual)
        {
            if (pattern.Kind == OriginKind.Intersection)
            {
                for (var i = 0; i < pattern.Operands.Count; i++)
                {
                    Match(pattern.Operands[i], actual);
                }
            }
            else if (ReferenceEquals(pattern.Binder, binder) && pattern.Kind is OriginKind.Parameter or OriginKind.Input)
            {
                var target = pattern.Kind == OriginKind.Parameter ? origins : inputs;
                target[pattern.Slot] = target[pattern.Slot] is { } previous ? this.Meet(previous, actual) : actual;
            }
        }
    }

    private bool ConformanceAccessible(BoundConformance conformance, FunctionKoto implementation)
        => AccessCovers(implementation.BoundSymbol!, conformance.Type, conformance.Contract);
}
