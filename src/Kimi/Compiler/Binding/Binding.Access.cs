// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 9.4: enclosing declaration accessibility comes from functions and Containers. A Pattern scope's owner may
    // bind a local in that same scope; following its BoundSymbol would revisit that local forever. Body/guard scopes
    // are lexical scopes, not declarations with their own accessibility.
    private static BindingSymbol? EnclosingAccessDeclaration(BindingSymbol symbol)
    {
        for (var scope = symbol.Scope; scope is not null; scope = scope.Parent)
        {
            if (scope.Owner is DeclarationContainerKoto or FunctionKoto && scope.Owner.BoundSymbol is { } enclosing)
            {
                return enclosing;
            }
        }

        return null;
    }

    private static BoundType EffectiveCore(BoundType type)
        => type.Kind == BoundTypeKind.Semantics && type.Components.Count == 1 ? type.Components[0] : type;

    // SPEC 9.4: the written access of a declaration that may carry a protected form.
    private static ModifierKind WrittenAccess(Koto declaration) => (declaration switch
    {
        DeclarationContainerKoto container => container.Modifier,
        FunctionKoto function => function.Modifier,
        VariableKoto variable => variable.Modifier,
        _ => ModifierKind.NoModifier,
    }).ExtractAccessibilityModifiers();

    private static ModifierKind DeclarationAccess(BindingSymbol symbol)
        => (symbol.Declaration switch
        {
            DeclarationContainerKoto container => container.Modifier,
            FunctionKoto function => function.Modifier,
            PropertyAccessorKoto accessor => accessor.Modifier.ExtractAccessibilityModifiers() != ModifierKind.NoModifier ? accessor.Modifier : ((PropertyKoto)accessor.Parent!).Modifier,
            VariableKoto variable => variable.Modifier,
            _ => ModifierKind.Public,
        }).ExtractAccessibilityModifiers();

    // The Container or function body that declares a member; a conditional implementation block is part of its Container.
    private static Koto? DeclaringContainer(Koto declaration)
    {
        for (var node = declaration.Parent; node is not null; node = node.Parent)
        {
            if (node is DeclarationContainerKoto or FunctionKoto)
            {
                return node;
            }
        }

        return null;
    }

    // SPEC 9.4: protected forms apply only to struct members; the record names what declares this one instead.
    private static void ReportProtectedPlacement(Koto declaration, DiagnosticRequirement requirement)
    {
        var container = DeclaringContainer(declaration) switch
        {
            DeclarationContainerKoto { IsRoot: true } or FunctionKoto { IsGenerated: true } => "the source root",
            GroupKoto group => $"group {group.Name}",
            EnumKoto enumeration => $"enum {enumeration.Name}",
            FunctionKoto => "a function body",
            _ => "a Container other than a struct",
        };

        SourceSpan? span = declaration switch
        {
            FunctionKoto { SignatureSpan.Length: > 0 } function => function.SignatureSpan,
            VariableKoto variable => variable.NameKoto.Span,
            _ => null,
        };
        declaration.Report(requirement, DiagnosticCode.ProtectedPlacement_Kd, container, span: span);
    }

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

    // SPEC 9.4.1, 9.4.2: Access(a) ∩ Access(b) ⊆ Access(member), decided on atoms. Each level of member's chain is a union U (a
    // private protected level two: Kotonoha(K) and Body(C) ∪ Derived(C)); U holds when one level of a's or b's chain, itself a union,
    // has every atom included in U.
    private bool AccessCovers(BindingSymbol member, BindingSymbol a, BindingSymbol b, ModifierKind? memberAccess = null)
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

            if (access == ModifierKind.ProtectedAndInternal
                ? !this.UnionCovered(ModifierKind.Internal, current, a, b) || !this.UnionCovered(ModifierKind.Protected, current, a, b)
                : !this.UnionCovered(access, current, a, b))
            {
                return false;
            }
        }

        return true;
    }

    private bool UnionCovered(ModifierKind access, BindingSymbol level, BindingSymbol a, BindingSymbol b)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            for (var current = pass == 0 ? a : b; current is not null; current = EnclosingAccessDeclaration(current))
            {
                if (current.Declaration is DeclarationContainerKoto { IsRoot: true })
                {
                    break;
                }

                var own = DeclarationAccess(current);
                if (own == ModifierKind.ProtectedAndInternal
                    ? this.UnionIncluded(ModifierKind.Internal, current, access, level) || this.UnionIncluded(ModifierKind.Protected, current, access, level)
                    : own != ModifierKind.Public && this.UnionIncluded(own, current, access, level))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // SPEC 9.4.2: every atom of the union `own` gives at `current` is included in the union `access` gives at `level`. A level at the
    // root is Kotonoha(K); Derived(S) is empty unless S is an open struct.
    private bool UnionIncluded(ModifierKind own, BindingSymbol current, ModifierKind access, BindingSymbol level)
    {
        var owner = current.Scope.Owner;
        var target = level.Scope.Owner;
        var sameKotonoha = ReferenceEquals(current.Declaration.CodeContext.Kotonoha, level.Declaration.CodeContext.Kotonoha);
        var kotonoha = target is DeclarationContainerKoto { IsRoot: true } || access is ModifierKind.Internal or ModifierKind.ProtectedOrInternal;
        var body = target is not DeclarationContainerKoto { IsRoot: true } && access is not ModifierKind.Internal;
        var derived = body && access is ModifierKind.Protected or ModifierKind.ProtectedOrInternal && target is StructKoto { Modifier: var open } && (open & ModifierKind.Open) != 0;
        if (owner is DeclarationContainerKoto { IsRoot: true } || own is ModifierKind.Internal or ModifierKind.ProtectedOrInternal)
        {
            if (!(kotonoha && sameKotonoha))
            {
                return false;
            }
        }

        if (owner is not DeclarationContainerKoto { IsRoot: true } && own is not ModifierKind.Internal && !(kotonoha && sameKotonoha))
        {
            var included = false;
            for (var scope = current.Scope; scope is not null && !included; scope = scope.Parent)
            {
                included = (body && ReferenceEquals(scope.Owner, target)) || (derived && scope.Owner is StructKoto structure && this.DerivesFrom(structure.BoundSymbol, target.BoundSymbol!));
            }

            if (!included)
            {
                return false;
            }
        }

        // Derived(S) of an open S: included only in Derived(C) for S that is or derives from C.
        return owner is not DeclarationContainerKoto { IsRoot: true } && own is ModifierKind.Protected or ModifierKind.ProtectedOrInternal && owner is StructKoto { Modifier: var mine } && (mine & ModifierKind.Open) != 0
            ? derived && this.DerivesFrom(owner.BoundSymbol, target.BoundSymbol!)
            : true;
    }

    private bool ProjectionAccessCovers(Koto use, BoundType qualifier, BindingSymbol contract, BindingSymbol domain, BindingSymbol? intersection = null)
        => this.TypeAccessCovers(qualifier, domain, intersection ?? domain) && this.AccessCovers(contract, domain, intersection ?? domain) &&
            (use.BoundSymbol is not { Kind: BindingSymbolKind.AssociatedType } requirement || this.AccessCovers(requirement, domain, intersection ?? domain));

    // A library group's function receives what the group states (WitnessReceiver).
    private bool IsReceiverType(BoundType? type, BindingSymbol owner)
        => type is not null && type.Semantics is not (SemanticsKind.Raw or SemanticsKind.Parameter) &&
            (this.Library.MembersOf(owner.Declaration) is { } members
                ? WitnessReceiver(members, type) : SameType(EffectiveCore(type), this.DeclarationSelf(owner)));

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
                    if (ReferenceEquals(path.Premises, clause.Parent) &&
                        !this.ProjectionAccessCovers(use.Use, use.Type, use.Contract, path.Type, path.Contract))
                    {
                        path.Invalid = true;
                        path.IsVerified = false;
                        this.Fail(clause, BindingFailure.Access);
                        this.Fail(conditional, BindingFailure.Access);
                        this.Fail(path.Use, BindingFailure.Access);
                    }
                }

                if (TryConditionalBlock(conditional, out var block))
                {
                    for (var m = 0; m < block.Items.Count; m++)
                    {
                        var member = block.Items[m];
                        if (member is FunctionKoto or PropertyKoto && member.BoundSymbol is { } memberDomain &&
                            !this.ProjectionAccessCovers(use.Use, use.Type, use.Contract, memberDomain))
                        {
                            this.Fail(member, BindingFailure.Access);
                        }
                    }
                }

                continue;
            }

            if (clause.IsAssociatedConstraint && owner is not ContractKoto && this.AssociatedIdentity(clause.Left.BoundType) is { } associated)
            {
                var type = scope.ConformancePath?.Type ?? owner.BoundSymbol;
                for (var p = 0; p < this.activeConformancePaths.Count; p++)
                {
                    var path = this.activeConformancePaths[p];
                    if (!ReferenceEquals(path.Type, type) || !path.Contract.Contract!.SeenRequirements.Contains(associated) ||
                        (scope.ConformancePath is { } declaringPath && !ReferenceEquals(path.RootPath, declaringPath.RootPath)))
                    {
                        continue;
                    }

                    // A narrower child Contract cannot narrow an ancestor's domain.
                    // Conditional specifications belong only to their root path.
                    if (!this.ProjectionAccessCovers(use.Use, use.Type, use.Contract, path.Type, path.Contract))
                    {
                        path.Invalid = true;
                        path.IsVerified = false;
                        this.Fail(clause, BindingFailure.Access);
                        this.Fail(path.Use, BindingFailure.Access);
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
                    if (!this.ProjectionAccessCovers(use.Use, use.Type, use.Contract, domain))
                    {
                        this.Fail(clause, BindingFailure.Access);
                        this.Fail(owner, BindingFailure.Access);
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

            if (ReferenceEquals(node, structure.BaseClause))
            {
                if (mode is { } bindingMode)
                {
                    // The normalized base may no longer contain its projection qualifier.
                    this.RequireConstraint(node, this.CheckTypeConstraints(use.Type, this.scopes[structure]), bindingMode);
                }
                else if (!this.ProjectionAccessCovers(use.Use, use.Type, use.Contract, structure.BoundSymbol!))
                {
                    this.Fail(node, BindingFailure.Access);
                    this.Fail(structure, BindingFailure.Access);
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
                else if (!this.ProjectionAccessCovers(use.Use, use.Type, use.Contract, enumeration.BoundSymbol!))
                {
                    this.Fail(node, BindingFailure.Access);
                    this.Fail(enumeration, BindingFailure.Access);
                }
            }
        }
    }

    private void ValidateApiAccess(BindingMode mode)
    {
        for (var i = 0; i < this.nodes.Count; i++)
        {
            // SPEC 9.4: a protected form is invalid on root and group declarations, group members, enum members and local
            // declarations: every declaration whose declaring Container is not a struct. A Contract requirement takes no
            // modifier at all, which the parser reports (MisplacedSyntax_Kd).
            if (this.nodes[i].BoundSymbol is { } declared && ReferenceEquals(declared.Declaration, this.nodes[i]) &&
                WrittenAccess(this.nodes[i]) is ModifierKind.Protected or ModifierKind.ProtectedAndInternal or ModifierKind.ProtectedOrInternal &&
                DeclaringContainer(this.nodes[i]) is not (StructKoto or ContractKoto))
            {
                this.Fail(this.nodes[i], BindingFailure.ProtectedPlacement);
            }

            if (this.nodes[i] is FunctionKoto or PropertyKoto && this.nodes[i].BoundSymbol is { ConditionalDeclaration: { } conditional } member)
            {
                var premises = (SyntaxFormKoto)conditional.Operands[1];
                for (var p = 0; p < premises.Operands.Length; p++)
                {
                    if (premises.Operands[p] is IsKoto { BoundConstraint: { } premise } && !this.ConstraintAccessCovers(premise, member))
                    {
                        this.Fail(this.nodes[i], BindingFailure.Access);
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
                        clause.BoundConstraint is { } constraint &&
                        (!this.ConstraintAccessCovers(constraint, domain) ||
                            (clause.Left.BoundType is { } subject && !this.TypeAccessCovers(subject, domain, domain))))
                    {
                        this.Fail(clause, BindingFailure.Access);
                        this.Fail(container, BindingFailure.Access);
                    }
                }
            }

            if (this.nodes[i] is not FunctionKoto { IsGenerated: false, IsAnonymous: false, BoundSymbol: { Scope.Owner: DeclarationContainerKoto } symbol } function)
            {
                continue;
            }

            // Run after body constraints have been bound, even for unused APIs.
            // Named functions retain their declared result (or the Unit default).
            if (symbol.Type is { } result && !this.TypeAccessCovers(result, symbol, symbol))
            {
                this.Fail(function, BindingFailure.Access);
            }

            for (var p = 0; p < function.Parameters.Count; p++)
            {
                if (function.Parameters[p].Type.BoundType is { } parameter && !this.TypeAccessCovers(parameter, symbol, symbol))
                {
                    this.Fail(function, BindingFailure.Access);
                }
            }

            for (var c = 0; c < function.TypeConstraints.Count; c++)
            {
                if (function.TypeConstraints[c] is IsKoto { BoundConstraint: { } constraint } && !this.ConstraintAccessCovers(constraint, symbol))
                {
                    this.Fail(function, BindingFailure.Access);
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
            if (!this.ProjectionAccessCovers(use.Use, use.Type, use.Contract, domain))
            {
                this.Fail(function, BindingFailure.Access);
            }

            this.RequireConstraint(function, this.CheckTypeConstraints(use.Type, this.ConstraintScope(use.Use)), mode);
        }

        this.ValidateEnumProjections(mode);
    }

    private bool DerivesFrom(BindingSymbol? type, BindingSymbol target)
    {
        if (ReferenceEquals(type, target))
        {
            return true;
        }

        if (type?.Declaration is StructKoto structure)
        {
            foreach (var layer in this.BaseLayers(structure))
            {
                if (ReferenceEquals(layer, target))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool Accessible(BindingSymbol symbol, BindingScope use, ModifierKind? operationAccess = null, BoundType? receiverType = null, bool declarationOnly = false)
    {
        if (symbol.CompilerFunction == CompilerFunctionKind.TestTempDirectory && (!this.compilation.IsTestBuild || !TestDefinition.IsTestOnly(use.Owner)))
        {
            return false;
        }

        if ((symbol.Declaration is FunctionKoto test && TestDefinition.Marker(test) is not null) ||
            (TestDefinition.IsTestOnly(symbol.Declaration) && !TestDefinition.IsTestOnly(use.Owner)))
        {
            return false;
        }

        if (ReferenceEquals(symbol, this.Library.Module) || symbol.Intrinsic != IntrinsicKind.None || symbol.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.TypeParameter or BindingSymbolKind.LengthParameter or BindingSymbolKind.AssociatedType || symbol.Declaration is FunctionKoto { IsRequirement: true } or PropertyKoto { IsContractRequirement: true })
        {
            return true;
        }

        for (var current = symbol; current is not null; current = EnclosingAccessDeclaration(current))
        {
            // The fixed-array member group stands for the public built-in Type (PLAN G32): only a member's own modifier applies.
            if (current.Declaration is DeclarationContainerKoto { IsRoot: true } || (!ReferenceEquals(current, symbol) && this.Library.IsBuiltinMemberGroup(current.Declaration)))
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
                        protectedAccess |= declarationOnly || !instance || (receiverType is not null && this.DerivesFrom(EffectiveCore(receiverType).Symbol, derived.BoundSymbol!));
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
