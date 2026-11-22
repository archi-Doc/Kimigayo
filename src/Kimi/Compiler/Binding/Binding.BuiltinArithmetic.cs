// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly BindingSymbol?[] builtinArithmetic = new BindingSymbol[6];

    // The slot of a Contract TyCtxt.HasBuiltinArithmetic admits in builtinArithmetic: the right-hand Contracts, then Negatable.
    private static int ArithmeticSlot(KimiDeclarationId id) => id == KimiDeclarationId.Negatable ? 5 : id - KimiDeclarationId.Addable;

    private BindingSymbol ArithmeticReference(KimiDeclarationId id, BoundType counterpart)
    {
        var declaration = this.Library.GetSymbol(id)!;
        return id == KimiDeclarationId.Negatable ? declaration
            : this.BoundContractReference(this.types.Intern(BoundTypeKind.Constructed, declaration, SemanticsKind.Owner, [counterpart]));
    }

    // SPEC 13.3.3: an arithmetic built-in row's (Target or Entailed, BuiltinConformance) Output is Self. W5 (ContractSolver's
    // Completion): Complete at the static gate, Provisional past it (the conformance proves).
    private BoundType? BuiltinArithmeticOutput(BoundType projection, BindingScope scope, QuerySite site, out Completion completion)
    {
        completion = Completion.Complete;
        if (projection is not { Kind: BoundTypeKind.AssociatedProjection, Components: [var self, var reference], Symbol.Scope.Owner.BoundSymbol: { } declaration } ||
            !KimiLibraryCatalog.IsArithmeticContract(declaration.LibraryDeclaration))
        {
            return null;
        }

        completion = Completion.Provisional;
        return this.BuiltinConformance(self, this.BoundContractReference(reference), scope, site).Kind is ImplTable.BuiltinKind.Target or ImplTable.BuiltinKind.Entailed
            ? self : null;
    }
}
