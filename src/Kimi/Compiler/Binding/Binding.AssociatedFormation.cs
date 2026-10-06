// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    /// <summary>Gets the formation Type of an associated-Type declaration, such as <c>ref/Self during source</c> (SPEC 8.4.3).</summary>
    /// <param name="declaration">The associated-Type declaration or specification.</param>
    /// <returns>The formation Type, or <see langword="null"/>.</returns>
    internal static BoundType? AssociatedFormationType(Koto declaration)
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

    // Whether `type` is the stored Type or one of the Type arguments stored in it, through nested value layers.
    private static bool StoresTypeArgument(BoundType stored, BoundType type)
    {
        // SPEC 8.1.1: a stored pair original `s/T` keeps every dependency of its whole Type, which includes those of its
        // target `T`.
        if (ReferenceEquals(stored, type) ||
            (type.Kind == BoundTypeKind.TargetProjection && type.Symbol?.WholeType is { } whole && ReferenceEquals(stored, whole)))
        {
            return true;
        }

        for (var i = 0; i < stored.Components.Count; i++)
        {
            if (StoresTypeArgument(stored.Components[i], type))
            {
                return true;
            }
        }

        return false;
    }

    private BindingScope AssociatedFormationScope(BoundConformancePath path, BindingSymbol? associated, BindingScope scope)
    {
        if (associated is not { Kind: BindingSymbolKind.AssociatedType } || this.AssociatedParameters(associated.Declaration).Length == 0 ||
            !this.associatedBindings.TryGetValue((path.RootPath, associated), out var binding))
        {
            return scope;
        }

        var result = binding.FormationScope ??= new(associated.Declaration);
        result.Parent = scope;
        return result;
    }

    private void BindAssociatedFormation(Koto declaration, BindingScope scope)
    {
        if (AssociatedFormationSyntax(declaration) is not { } syntax)
        {
            return;
        }

        if (!IsAssociatedRequirement(declaration) || (declaration is IsKoto clause && AssociatedIdentityType(clause.BoundConstraint) is not null))
        {
            this.Fail(declaration, BindingFailure.InvalidAssociatedType);
            return;
        }

        if (this.BindType(syntax, scope) is { } type && !HasSupportedAssociatedFormation(type))
        {
            this.Fail(declaration, BindingFailure.Unsupported);
        }
    }

    private bool CheckAssociatedFormation(BoundType type, Koto use)
    {
        if (this.CheckTypeOriginRelations(type, this.ConstraintScope(use)) != ConstraintProof.Proven)
        {
            return false;
        }

        if ((IsBorrow(type.Semantics) || type.Kind == BoundTypeKind.Slice) && type.Origin is { } outer)
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
        if (type.Kind is BoundTypeKind.Parameter or BoundTypeKind.AssociatedProjection or BoundTypeKind.TargetProjection)
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
                var owner = associated.Scope.Owner.BoundSymbol!;
                if (self.Contract is { } shape)
                {
                    for (var i = 0; i < shape.Ancestors.Count; i++)
                    {
                        if (ReferenceEquals(shape.Ancestors[i].Declaration, owner.Declaration))
                        {
                            formation = this.SubstituteContractReference(formation, shape.Ancestors[i]);
                            break;
                        }
                    }
                }
                else if (this.ConformanceByDeclaration(self, owner, out _) is { } conformance)
                {
                    formation = this.SubstituteContractReference(formation, conformance.Contract);
                }

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

            if (node is FunctionKoto or FunctionTypeKoto or PropertyAccessorKoto)
            {
                for (var i = 0; i < InputCount(node); i++)
                {
                    if (this.BoundInputType(node, i) is { } input && this.ProvesBorrowedTypeLifetime(input, type, outer, use))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    // SPEC 8.1.2, 15.3.1: a well-formed borrow `s/X during o` needs every dependency of X to outlive o, so it proves that X and
    // each Type argument stored in X outlive `outer` when o does (an input `uniq/W<I> during step` proves I during step).
    private bool ProvesBorrowedTypeLifetime(BoundType premise, BoundType type, BoundOrigin outer, Koto use)
    {
        if ((IsBorrow(premise.Semantics) || premise.Kind == BoundTypeKind.Slice) && premise.Origin is { } origin && premise.Components.Count != 0 &&
            StoresTypeArgument(premise.Components[0], type) && this.ProvesOriginOutlives(origin, outer, use))
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
