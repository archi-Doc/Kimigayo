// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private static BoundType? AssociatedFormationType(Koto declaration)
        => AssociatedFormationSyntax(declaration)?.BoundType ?? (declaration is IsKoto clause ? AssociatedIdentityType(clause.BoundConstraint) : null);

    private static Koto? AssociatedFormationSyntax(Koto declaration)
        => declaration switch
        {
            IsKoto clause => clause.FormationType,
            SyntaxFormKoto { Akind: KotoKind.AssociatedType, Operands.Length: 2 } syntax => syntax.Operands[1],
            _ => null,
        };

    private static BoundType? AssociatedIdentityType(BoundConstraint? constraint)
        => constraint?.Kind switch
        {
            ConstraintKind.TypeIdentity => constraint.RequiredType,
            ConstraintKind.And => AssociatedIdentityType(constraint.Left) ?? AssociatedIdentityType(constraint.Right),
            _ => null,
        };

    private void BindAssociatedFormation(Koto declaration, BindingScope scope)
    {
        if (AssociatedFormationSyntax(declaration) is not { } syntax)
        {
            return;
        }

        if (!IsAssociatedRequirement(declaration) || (declaration is IsKoto clause && AssociatedIdentityType(clause.BoundConstraint) is not null))
        {
            Fail(declaration, BindingFailure.InvalidAssociatedType);
            return;
        }

        if (this.BindType(syntax, scope) is { } type && !HasSupportedAssociatedFormation(type))
        {
            Fail(declaration, BindingFailure.Unsupported);
        }
    }

    private bool CheckAssociatedFormation(BoundType type, Koto use)
    {
        if (this.CheckTypeOriginRelations(type, this.ConstraintScope(use)) != ConstraintProof.Proven)
        {
            return false;
        }

        if (IsBorrow(type.Semantics) && type.Origin is { } outer)
        {
            if ((outer.Kind == OriginKind.Static && IsExclusive(type.Semantics)) ||
                (type.Components.Count != 0 && !this.AssociatedOriginsOutlive(type.Components[0], outer, use)))
            {
                return false;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (!this.CheckAssociatedFormation(type.Components[i], use))
            {
                return false;
            }
        }

        return true;
    }

    private bool AssociatedOriginsOutlive(BoundType type, BoundOrigin outer, Koto use)
    {
        if (type.Kind is BoundTypeKind.Parameter or BoundTypeKind.AssociatedProjection or BoundTypeKind.TargetProjection || type.Symbol?.Declaration is ContractKoto)
        {
            return this.ProveOwned(type, use) == ConstraintProof.Proven || this.ProvesAssociatedTypeLifetime(type, outer, use);
        }

        if (type.Origin is { } origin && !this.ProvesOriginOutlives(origin, outer, use))
        {
            return false;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (!this.ProvesOriginOutlives(type.OriginArguments[i], outer, use))
            {
                return false;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (!this.AssociatedOriginsOutlive(type.Components[i], outer, use))
            {
                return false;
            }
        }

        return true;
    }

    private BoundType? InheritedAssociatedFormation(Koto node)
    {
        if (AssociatedHead(node) is not OriginApplicationKoto || node.BoundSymbol is not { Kind: BindingSymbolKind.AssociatedType } associated ||
            ReferenceEquals(associated.Declaration, node) || AssociatedFormationType(associated.Declaration) is not { } formation)
        {
            return null;
        }

        formation = this.SubstituteStoredOrigins(formation, associated.Declaration, this.AssociatedParameters(node));
        var scope = this.ConstraintScope(node);
        for (var enclosing = scope; enclosing is not null; enclosing = enclosing.Parent)
        {
            if (enclosing.Owner is StructKoto or EnumKoto or ContractKoto && enclosing.Owner.BoundSymbol is { } self)
            {
                return this.ContractType(formation, scope, this.SelfType(self));
            }
        }

        return formation;
    }

    private bool ProvesAssociatedTypeLifetime(BoundType type, BoundOrigin outer, Koto use)
    {
        for (var node = use; node is not null; node = node.Parent)
        {
            var formation = IsAssociatedRequirement(node) ? AssociatedFormationType(node) : this.InheritedAssociatedFormation(node);
            if (formation is not null && this.ProvesBorrowedTypeLifetime(formation, type, outer, use))
            {
                return true;
            }

            if (node is FunctionKoto or PropertyAccessorKoto)
            {
                for (var i = 0; i < InputCount(node); i++)
                {
                    if (BoundInputType(node, i) is { } input && this.ProvesBorrowedTypeLifetime(input, type, outer, use))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private bool ProvesBorrowedTypeLifetime(BoundType premise, BoundType type, BoundOrigin outer, Koto use)
    {
        if (IsBorrow(premise.Semantics) && premise.Origin is { } origin && premise.Components.Count != 0 &&
            ReferenceEquals(premise.Components[0], type) && this.ProvesOriginOutlives(origin, outer, use))
        {
            return true;
        }

        for (var i = 0; i < premise.Components.Count; i++)
        {
            if (this.ProvesBorrowedTypeLifetime(premise.Components[i], type, outer, use))
            {
                return true;
            }
        }

        return false;
    }
}
