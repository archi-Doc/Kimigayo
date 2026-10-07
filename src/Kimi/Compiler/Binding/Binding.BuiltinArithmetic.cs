// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly BindingSymbol?[] builtinArithmetic = new BindingSymbol[6];

    private bool BuiltinArithmeticConformance(BoundType self, BindingSymbol contract, BindingScope scope)
    {
        if (ArithmeticContracts.Identity(contract) is not { } id || ArithmeticContracts.IsLeft(id) ||
            (id != KimiDeclarationId.Negatable && (contract.Type is not { Components: [var counterpart] } || !ReferenceEquals(counterpart, self))))
        {
            return false;
        }

        return ArithmeticContracts.Supports(self, id) || this.IsGenericWrapping(self, scope) || (id != KimiDeclarationId.Negatable && this.IsGenericInteger(self, scope));
    }

    private BindingSymbol ArithmeticReference(KimiDeclarationId id, BoundType counterpart)
    {
        var declaration = this.Library.GetSymbol(id)!;
        return id == KimiDeclarationId.Negatable ? declaration
            : this.BoundContractReference(this.InternType(BoundTypeKind.Constructed, declaration, SemanticsKind.Owner, [counterpart]));
    }

    private BoundType? BuiltinArithmeticOutput(BoundType projection, BindingScope scope)
    {
        if (projection is not { Kind: BoundTypeKind.AssociatedProjection, Components: [var self, var reference], Symbol.Scope.Owner.BoundSymbol: { } declaration } ||
            !ArithmeticContracts.IsArithmetic(declaration.LibraryDeclaration))
        {
            return null;
        }

        return this.BuiltinArithmeticConformance(self, this.BoundContractReference(reference), scope) ? self : null;
    }

    private BindingSymbol NumericRequirementTarget(BindingSymbol selected, KimiDeclarationId id)
    {
        var index = id == KimiDeclarationId.Negatable ? 5 : id - KimiDeclarationId.Addable;
        var target = this.builtinArithmetic[index] ??= new(selected.Name, selected.Kind, selected.Declaration, selected.Scope)
        {
            CompilerFunction = CompilerFunctionKind.BuiltinArithmetic,
        };
        target.Type = selected.Type;
        target.ReceiverIndex = selected.ReceiverIndex;
        return target;
    }
}
