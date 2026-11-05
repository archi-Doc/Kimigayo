// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly BindingSymbol?[] builtinArithmetic = new BindingSymbol[6];

    // The slot of a Contract ArithmeticContracts.Supports admits in builtinArithmetic: the right-hand Contracts, then Negatable.
    private static int ArithmeticSlot(KimiDeclarationId id) => id == KimiDeclarationId.Negatable ? 5 : id - KimiDeclarationId.Addable;

    // SPEC 8.7, 8.6.1, 8.2.3: a builtin `Self is C<Self>`; the required reference's argument is Self when the two are one Type in
    // `scope` (SameConformanceType), as a projection naming it is (`i32 is Addable<U.(HasItem).Item>` with SrcI for U).
    private bool BuiltinArithmeticConformance(BoundType self, BindingSymbol contract, BindingScope scope)
    {
        if (ArithmeticContracts.Identity(contract) is not { } id || ArithmeticContracts.IsLeft(id))
        {
            return false;
        }

        if (id != KimiDeclarationId.Negatable &&
            (contract.Type is not { Components: [var counterpart] } || !this.SameConformanceType(counterpart, self, scope)))
        {
            return false;
        }

        return ArithmeticContracts.Supports(self, id) || this.IsGenericWrapping(self, scope) || (id != KimiDeclarationId.Negatable && this.IsGenericInteger(self, scope));
    }

    private BindingSymbol ArithmeticReference(KimiDeclarationId id, BoundType counterpart)
    {
        var declaration = this.Library.GetSymbol(id)!;
        return id == KimiDeclarationId.Negatable ? declaration
            : this.BoundContractReference(this.types.Intern(BoundTypeKind.Constructed, declaration, SemanticsKind.Owner, [counterpart]));
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
}
