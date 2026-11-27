// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private BindingSymbol? builtinEquals;
    private BindingSymbol? builtinCompare;

    // SPEC 22.1: the declaration a selected call executes. A linked Kimi constructor runs its validated internal implementation,
    // which has the same parameters and premise; null when the link is not validated. Otherwise the compiler requirement target.
    private BindingSymbol? ExecutedTarget(BindingSymbol selected, BoundType? self)
        => selected.LibraryDeclaration is { } id && KimiLibraryCatalog.ImplementationOf(id) is not null
            ? this.Library.ConstructorImplementation(selected)
            : this.CompilerRequirementTarget(selected, self);

    private BindingSymbol CompilerRequirementTarget(BindingSymbol selected, BoundType? self)
        => this.BuiltinTarget(ImplTable.Builtin(self, selected.Scope.Owner.BoundSymbol)) ?? selected;

    // F fact 11a: the built-in target a built-in row's requirement runs (ImplTable.Builtin's Target and Composed rows), its Contract's one
    // own requirement executed by the compiler; null for another row. A built-in target exists from the first pass's
    // PrepareBuiltinTargets on; no requirement is resolved before it.
    private BindingSymbol? BuiltinTarget(in ImplTable.BuiltinRow row)
        => row.Kind is not (ImplTable.BuiltinKind.Target or ImplTable.BuiltinKind.Composed) ? null
            : (row.Contract == KimiDeclarationId.Equatable ? this.builtinEquals
                : row.Contract == KimiDeclarationId.Comparable ? this.builtinCompare : this.builtinArithmetic[ArithmeticSlot(row.Contract)])
                ?? throw new InvalidOperationException("A built-in target is read before PrepareBuiltinTargets.");

    // F fact 11a: a built-in target is its Contract's one own requirement executed by the compiler, with that requirement's name,
    // declaration, result Type and receiver slot, taken once per pass after headers, so resolving a requirement writes nothing.
    private void PrepareBuiltinTargets()
    {
        Prepare(ref this.builtinEquals, KimiDeclarationId.Equatable);
        Prepare(ref this.builtinCompare, KimiDeclarationId.Comparable);
        for (var id = KimiDeclarationId.Addable; id <= KimiDeclarationId.Negatable; id++)
        {
            if (!KimiLibraryCatalog.IsLeftContract(id))
            {
                Prepare(ref this.builtinArithmetic[ArithmeticSlot(id)], id);
            }
        }

        void Prepare(ref BindingSymbol? target, KimiDeclarationId id)
        {
            if (this.Library.GetSymbol(id)!.Contract!.OwnRequirement() is { } requirement)
            {
                target ??= new(requirement.Name, requirement.Kind, requirement.Declaration, requirement.Scope) { Definition = new(DispositionKind.Shim) };
                target.Type = requirement.Type;
                target.ReceiverIndex = requirement.ReceiverIndex;
            }
        }
    }
}
