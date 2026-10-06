// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class KimiLibrary
{
    private static bool StrongPair(Koto? syntax)
    {
        while (true)
        {
            if (syntax is ParenthesizedTypeKoto grouped)
            {
                syntax = grouped.Type;
            }
            else if (syntax is TypeSemanticsKoto { IsTransparentWrapper: true, HasOrigin: false, AttributeChain: null } wrapper)
            {
                syntax = wrapper.Type;
            }
            else
            {
                return syntax is TypeSemanticsKoto { SemanticsParameter: "s", OriginName: null, OriginExpression: null, OriginArguments: null, AttributeChain: null, Type: { } target } && BareName(target, "T");
            }
        }
    }

    private static bool ValidBoundStrongClone(BindingSymbol symbol, KimiDeclarationId id)
        => id != KimiDeclarationId.Clone ||
            (symbol.Declaration is FunctionKoto { GenericArguments.Count: 1, Parameters.Count: 1 } f &&
                f.GenericArguments[0].BoundSymbol is { Kind: BindingSymbolKind.SemanticsTarget, WholeType: { } whole } &&
                ReferenceEquals(symbol.Type, whole) && BoundInputBorrow(f.Parameters[0].Type.BoundType, whole, SemanticsKind.Ref, f, 0));

    private bool ValidStrongClone(BindingSymbol symbol)
        => symbol.CompilerFunction == CompilerFunctionKind.Clone && ReferenceEquals(symbol.Scope, this.IntrinsicsScope) &&
            ReferenceEquals(symbol.Declaration.Parent, this.Intrinsics) &&
            symbol.Declaration is FunctionKoto { Name: "clone", NameBoundaryIndex: -1, Modifier: ModifierKind.Public, AttributeChain: null, GenericArguments.Count: 1, Parameters.Count: 1, Origins.Count: 0, TypeConstraints.Count: 1, Body: null, ExpressionBody: null, IsRequirement: false, IsGenerated: false, IsSpecialization: false } f &&
            f.GenericArguments[0] is GenericParameterKoto { Identifier: "T", SemanticsParameter: "s", AttributeChain: null } &&
            f.TypeConstraints[0] is IsKoto { IsNegated: false, IsAssociatedConstraint: false, FormationType: null, AttributeChain: null, Left: { } constrained, Right: OrKoto modes } &&
            BareName(constrained, "s") && BareName(modes.Left, "rc") && BareName(modes.Right, "arc") &&
            f.Parameters[0] is { InternalName: "value", ExternalName: "value", DefaultValue: null, AttributeChain: null, Type: TypeSemanticsKoto { SemanticsKind: SemanticsKind.Ref, SemanticsParameter: null, OriginName: null, OriginExpression: null, OriginArguments: null, Type: { } input } } &&
            StrongPair(input) && StrongPair(f.ReturnType);
}
