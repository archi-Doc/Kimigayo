// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private static BoundType EffectiveCore(BoundType type)
        => type.Kind == BoundTypeKind.Semantics && type.Components.Count == 1 ? type.Components[0] : type;

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
            type = syntax.BoundType?.Symbol ?? this.TypeName(syntax is GenericsKoto generic ? generic.Identifier! : syntax, this.scopes[structure], false);
        }

        return false;
    }

    private bool Accessible(BindingSymbol symbol, BindingScope use, ModifierKind? operationAccess = null, BoundType? receiverType = null)
    {
        if (ReferenceEquals(symbol, this.Core.Module) || symbol.Intrinsic != IntrinsicKind.None || symbol.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.TypeParameter or BindingSymbolKind.LengthParameter or BindingSymbolKind.AssociatedType || symbol.Declaration is FunctionKoto { IsRequirement: true })
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
