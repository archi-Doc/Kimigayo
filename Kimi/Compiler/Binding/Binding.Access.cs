// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private static BoundType EffectiveCore(BoundType type)
        => type.Kind == BoundTypeKind.Semantics && type.Components.Count == 1 ? type.Components[0] : type;

    private static bool ProjectionAccessCovers(Koto use, BoundType qualifier, BindingSymbol contract, BindingSymbol domain, BindingSymbol? intersection = null)
        => TypeAccessCovers(qualifier, domain, intersection ?? domain) && AccessCovers(contract, domain, intersection ?? domain) &&
            (use.BoundSymbol is not { Kind: BindingSymbolKind.AssociatedType } requirement || AccessCovers(requirement, domain, intersection ?? domain));

    private static FunctionKoto? FunctionSignatureOwner(Koto use)
    {
        for (var node = use; node.Parent is { } parent; node = parent)
        {
            if (parent is not FunctionKoto function)
            {
                continue;
            }

            if (function.IsGenerated || function.IsAnonymous || function.BoundSymbol?.Scope.Owner is not DeclarationContainerKoto)
            {
                return null;
            }

            if (ReferenceEquals(node, function.ReturnType))
            {
                return function;
            }

            for (var p = 0; p < function.Parameters.Count; p++)
            {
                if (ReferenceEquals(node, function.Parameters[p].Type))
                {
                    return function;
                }
            }

            // Body and default expressions use definition-site access, not API access.
            return null;
        }

        return null;
    }

    private bool IsReceiverType(BoundType? type, BindingSymbol owner)
        => type is not null && SameType(EffectiveCore(type), this.SelfType(owner)) && type.Semantics is not (SemanticsKind.Unsafe or SemanticsKind.Parameter);

    private void ValidateConstraintProjectionAccess()
    {
        for (var i = 0; i < this.projectionUses.Count; i++)
        {
            var use = this.projectionUses[i];
            var node = use.Use;
            while (node is not (IsKoto or FunctionKoto or DeclarationContainerKoto) && node.Parent is { } parent)
            {
                node = parent;
            }

            if (node is not IsKoto clause)
            {
                continue;
            }

            var scope = this.ConstraintScope(clause);
            var owner = scope.Owner;
            if (owner is SyntaxFormKoto { Akind: KotoKind.ConditionalConformance } conditional && ReferenceEquals(clause.Parent, conditional.Operands[1]))
            {
                for (var p = 0; p < this.activeConformancePaths.Count; p++)
                {
                    var path = this.activeConformancePaths[p];
                    if (ReferenceEquals(path.Premises, clause.Parent) && !ProjectionAccessCovers(use.Use, use.Type, use.Contract, path.Type, path.Contract))
                    {
                        path.Invalid = true;
                        path.IsVerified = false;
                        Fail(clause, BindingFailure.Access);
                        Fail(conditional, BindingFailure.Access);
                        Fail(path.Use, BindingFailure.Access);
                    }
                }

                if (TryConditionalBlock(conditional, out var block))
                {
                    for (var m = 0; m < block.Items.Count; m++)
                    {
                        var member = block.Items[m];
                        if (member is FunctionKoto or PropertyKoto && member.BoundSymbol is { } memberDomain && !ProjectionAccessCovers(use.Use, use.Type, use.Contract, memberDomain))
                        {
                            Fail(member, BindingFailure.Access);
                        }
                    }
                }

                continue;
            }

            if (clause.IsAssociatedConstraint && owner is not ContractKoto && clause.BoundSymbol is { } associated)
            {
                var type = scope.ConformancePath?.Type ?? owner.BoundSymbol;
                for (var p = 0; p < this.activeConformancePaths.Count; p++)
                {
                    var path = this.activeConformancePaths[p];
                    if (!ReferenceEquals(path.Type, type) || !path.Contract.Contract!.AssociatedStorage.Contains(associated) ||
                        (scope.ConformancePath is { } declaringPath && !ReferenceEquals(path.RootPath, declaringPath.RootPath)))
                    {
                        continue;
                    }

                    // A narrower child Contract cannot narrow an ancestor's domain.
                    // Conditional specifications belong only to their root path.
                    if (!ProjectionAccessCovers(use.Use, use.Type, use.Contract, path.Type, path.Contract))
                    {
                        path.Invalid = true;
                        path.IsVerified = false;
                        Fail(clause, BindingFailure.Access);
                        Fail(path.Use, BindingFailure.Access);
                    }
                }

                continue;
            }

            IReadOnlyList<Koto> clauses;
            BindingSymbol domain;
            switch (owner)
            {
                case FunctionKoto { IsGenerated: false, IsAnonymous: false, BoundSymbol: { Scope.Owner: DeclarationContainerKoto } symbol } function:
                    clauses = function.TypeConstraints;
                    domain = function.IsRequirement ? symbol.Scope.Owner.BoundSymbol! : symbol;
                    break;
                case DeclarationContainerKoto container when container is StructKoto or EnumKoto or ContractKoto:
                    // Implementation specifications and Self conformances have their
                    // separate Type/Contract intersection domain, not the Type API domain.
                    if (container is not ContractKoto && (clause.IsAssociatedConstraint || IsSelfConstraint(clause)))
                    {
                        continue;
                    }

                    clauses = container.ConstraintNodes;
                    domain = container.BoundSymbol!;
                    break;
                default:
                    continue;
            }

            for (var c = 0; c < clauses.Count; c++)
            {
                if (ReferenceEquals(clauses[c], clause))
                {
                    if (!ProjectionAccessCovers(use.Use, use.Type, use.Contract, domain))
                    {
                        Fail(clause, BindingFailure.Access);
                        Fail(owner, BindingFailure.Access);
                    }

                    break;
                }
            }
        }
    }

    private void ValidateBaseProjections(BindingMode? mode)
    {
        for (var i = 0; i < this.projectionUses.Count; i++)
        {
            var use = this.projectionUses[i];
            var node = use.Use;
            while (node.Parent is { } parent && parent is not DeclarationContainerKoto && node is not (FunctionKoto or PropertyKoto))
            {
                node = parent;
            }

            if (node.Parent is not StructKoto structure)
            {
                continue;
            }

            for (var b = 0; b < structure.Bases.Count; b++)
            {
                if (ReferenceEquals(node, structure.Bases[b]))
                {
                    if (mode is { } bindingMode)
                    {
                        // The normalized base may no longer contain its projection qualifier.
                        this.RequireConstraint(node, this.CheckTypeConstraints(use.Type, this.scopes[structure]), bindingMode);
                    }
                    else if (!ProjectionAccessCovers(use.Use, use.Type, use.Contract, structure.BoundSymbol!))
                    {
                        Fail(node, BindingFailure.Access);
                        Fail(structure, BindingFailure.Access);
                    }

                    break;
                }
            }
        }
    }

    private void ValidateEnumProjections(BindingMode? mode = null)
    {
        for (var i = 0; i < this.projectionUses.Count; i++)
        {
            var use = this.projectionUses[i];
            var node = use.Use;
            while (node.Parent is { } parent && parent is not DeclarationContainerKoto && node is not (FunctionKoto or PropertyKoto))
            {
                node = parent;
            }

            if (node is SyntaxFormKoto { Akind: KotoKind.EnumCase, Parent: EnumKoto enumeration })
            {
                if (mode is { } bindingMode)
                {
                    var proof = this.CheckTypeConstraints(use.Type, this.scopes[enumeration]);
                    this.RequireConstraint(node, proof, bindingMode);
                    this.RequireConstraint(enumeration, proof, bindingMode);
                }
                else if (!ProjectionAccessCovers(use.Use, use.Type, use.Contract, enumeration.BoundSymbol!))
                {
                    Fail(node, BindingFailure.Access);
                    Fail(enumeration, BindingFailure.Access);
                }
            }
        }
    }

    private void ValidateApiAccess(BindingMode mode)
    {
        for (var i = 0; i < this.nodes.Count; i++)
        {
            if (this.nodes[i].BoundSymbol is { Scope.Owner: GroupKoto } groupMember &&
                ReferenceEquals(groupMember.Declaration, this.nodes[i]) &&
                DeclarationAccess(groupMember) is ModifierKind.Protected or ModifierKind.ProtectedAndInternal or ModifierKind.ProtectedOrInternal)
            {
                Fail(this.nodes[i], BindingFailure.Access);
            }

            if (this.nodes[i] is FunctionKoto or PropertyKoto && this.nodes[i].BoundSymbol is { ConditionalDeclaration: { } conditional } member)
            {
                var premises = (SyntaxFormKoto)conditional.Operands[1];
                for (var p = 0; p < premises.Operands.Length; p++)
                {
                    if (premises.Operands[p] is IsKoto { BoundConstraint: { } premise } && !ConstraintAccessCovers(premise, member))
                    {
                        Fail(this.nodes[i], BindingFailure.Access);
                    }
                }
            }

            if (this.nodes[i] is DeclarationContainerKoto { BoundSymbol: { } domain } container && container is StructKoto or EnumKoto)
            {
                for (var c = 0; c < container.ConstraintNodes.Count; c++)
                {
                    var clause = container.ConstraintNodes[c];
                    // Self conformances have the intersection of the Type and
                    // Contract domains; their separate witness checks own this case.
                    if (!IsSelfConstraint(clause) && !clause.IsAssociatedConstraint &&
                        clause.BoundConstraint is { } constraint && (!ConstraintAccessCovers(constraint, domain) || (clause.Left.BoundType is { } subject && !TypeAccessCovers(subject, domain, domain))))
                    {
                        Fail(clause, BindingFailure.Access);
                        Fail(container, BindingFailure.Access);
                    }
                }
            }

            if (this.nodes[i] is not FunctionKoto { IsGenerated: false, IsAnonymous: false, BoundSymbol: { Scope.Owner: DeclarationContainerKoto } symbol } function)
            {
                continue;
            }

            // Run after body constraints have been bound, even for unused APIs.
            // Named functions retain their declared result (or the Unit default).
            if (symbol.Type is { } result && !TypeAccessCovers(result, symbol, symbol))
            {
                Fail(function, BindingFailure.Access);
            }

            for (var p = 0; p < function.Parameters.Count; p++)
            {
                if (function.Parameters[p].Type.BoundType is { } parameter && !TypeAccessCovers(parameter, symbol, symbol))
                {
                    Fail(function, BindingFailure.Access);
                }
            }

            for (var c = 0; c < function.TypeConstraints.Count; c++)
            {
                if (function.TypeConstraints[c] is IsKoto { BoundConstraint: { } constraint } && !ConstraintAccessCovers(constraint, symbol))
                {
                    Fail(function, BindingFailure.Access);
                }
            }
        }

        // Normalization can replace S.C.Element with its concrete binding. The
        // retained projection use still owns the qualifier and requirement domains.
        for (var i = 0; i < this.projectionUses.Count; i++)
        {
            var use = this.projectionUses[i];
            if (FunctionSignatureOwner(use.Use) is not { BoundSymbol: { } symbol } function)
            {
                continue;
            }

            var domain = function.IsRequirement ? symbol.Scope.Owner.BoundSymbol! : symbol;
            if (!ProjectionAccessCovers(use.Use, use.Type, use.Contract, domain))
            {
                Fail(function, BindingFailure.Access);
            }

            this.RequireConstraint(function, this.CheckTypeConstraints(use.Type, this.ConstraintScope(use.Use)), mode);
        }

        this.ValidateEnumProjections(mode);
    }

    private bool DerivesFrom(BindingSymbol? type, BindingSymbol target)
    {
        // The current language has one inline base. The bound limits invalid cycles too.
        for (var depth = 0; type is not null && depth <= this.nodes.Count; depth++)
        {
            if (ReferenceEquals(type, target))
            {
                return true;
            }

            if (type.Declaration is not StructKoto { Bases.Count: > 0 } structure)
            {
                break;
            }

            var syntax = structure.Bases[0];
            type = syntax.BoundType?.Symbol ?? this.TypeName(syntax, this.scopes[structure], false);
        }

        return false;
    }

    private bool Accessible(BindingSymbol symbol, BindingScope use, ModifierKind? operationAccess = null, BoundType? receiverType = null)
    {
        if (ReferenceEquals(symbol, this.Library.Module) || symbol.Intrinsic != IntrinsicKind.None || symbol.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.TypeParameter or BindingSymbolKind.LengthParameter or BindingSymbolKind.AssociatedType || symbol.Declaration is FunctionKoto { IsRequirement: true })
        {
            return true;
        }

        for (var current = symbol; current is not null; current = current.Scope.Owner.BoundSymbol)
        {
            if (current.Declaration is DeclarationContainerKoto { IsRoot: true })
            {
                break;
            }

            var access = ReferenceEquals(current, symbol) && operationAccess is { } specific ? specific : DeclarationAccess(current);
            if (access == ModifierKind.Public)
            {
                continue;
            }

            var sameModule = ReferenceEquals(current.Declaration.CodeContext.Kotonoha, use.Owner.CodeContext.Kotonoha);
            var lexical = false;
            for (var scope = use; scope is not null && !lexical; scope = scope.Parent)
            {
                lexical = ReferenceEquals(scope, current.Scope);
            }

            if (lexical || (current.Scope.Owner is DeclarationContainerKoto { IsRoot: true } && sameModule))
            {
                continue;
            }

            var protectedAccess = false;
            if (access is ModifierKind.Protected or ModifierKind.ProtectedAndInternal or ModifierKind.ProtectedOrInternal && current.Scope.Owner is StructKoto owner)
            {
                for (var scope = use; scope is not null; scope = scope.Parent)
                {
                    if (scope.Owner is StructKoto derived && this.DerivesFrom(derived.BoundSymbol, owner.BoundSymbol!))
                    {
                        var instance = ReferenceEquals(current, symbol) && (symbol.ReceiverIndex >= 0 || symbol.Kind == BindingSymbolKind.Property);
                        protectedAccess |= !instance || (receiverType is not null && this.DerivesFrom(EffectiveCore(receiverType).Symbol, derived.BoundSymbol!));
                        if (protectedAccess)
                        {
                            break;
                        }
                    }
                }
            }

            var allowed = access switch
            {
                ModifierKind.Internal => sameModule,
                ModifierKind.Protected => protectedAccess,
                ModifierKind.ProtectedAndInternal => sameModule && protectedAccess,
                ModifierKind.ProtectedOrInternal => sameModule || protectedAccess,
                _ => false,
            };
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }
}
