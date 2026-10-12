// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The effect facts Binding decided, in MIR terms (DECISIONS R4-M22, R4-M23): who owes `confined`, the bounds a call may use under a
// definition's premises, what a function without a MIR body does, the bodies a call may run, and the Copy and Sealed facts its summaries
// read, all under one premise scope per definition (Binding.EffectPremises).
internal sealed partial class TyCtxt
{
    // SPEC 8.4.10.2: what a call of a function without a MIR body does: a compiler function's class (IntrinsicDisposition.Effect), a
    // definition Binding rejected a dependent of that error (R4-M18: its parts are not published), a foreign function (SPEC 22.3.1) an
    // environment effect; any other is unclassified.
    internal static CompilerFunctionEffect Bodyless(BindingSymbol function) => function switch
    {
        { CompilerFunction: not CompilerFunctionKind.None } => IntrinsicDisposition.Effect(function.CompilerFunction),
        { Declaration.BindingState: not BindingState.Resolved } => CompilerFunctionEffect.Dependent,
        { Declaration: FunctionKoto declaration } when Binding.IsLibraryImport(declaration) => CompilerFunctionEffect.Environment,
        _ => CompilerFunctionEffect.Unclassified,
    };

    // SPEC 4.7.5, 12.3.4: an operation's published row in MIR terms (KimiLibraryCatalog.Row): the requirement it runs on the Type below
    // parameter On and the parameter below which it destroys, -1 for none; null for an operation without a row.
    internal (BindingSymbol? Runs, int On, int Destroys)? Published(BindingSymbol operation)
    {
        if (KimiLibraryCatalog.Row(operation) is not { } row)
        {
            return null;
        }

        return (row.Runs is { } runs ? library.GetSymbol(runs)!.Contract!.Requirements[0].Symbol : null, row.On, row.Destroys); // Its one requirement.
    }

    // SPEC 8.4.10.4: what a requirement runs on a Self that is not abstract (Binding.Witnesses), appended to a caller-owned list with each
    // target's declaring instance (none for a compiler function); false where none resolves.
    internal bool Witnesses(BindingSymbol requirement, BoundType self, BindingSymbol? definition, List<(BindingSymbol Target, BoundType? Declaring)> into)
        => binding.Witnesses(requirement, self, definition, into);

    // R4-M22: the confined obligations of the latest pass (Binding.EffectObligations), appended to a caller-owned list.
    internal void EffectObligations(List<EffectObligation> rows) => binding.EffectObligations(rows);

    // SPEC 8.4.10.4, 8.4.7: the bounds a requirement or virtual call may use under the calling definition's own premises, null for a callee
    // that does not dispatch (Binding.DispatchBounds).
    internal (bool Confined, bool Preserves)? DispatchBounds(BindingSymbol? definition, BindingSymbol callee, BoundType? self, BindingSymbol? contract)
        => binding.DispatchBounds(definition, callee, self, contract);

    // SPEC 8.4.10.7: the bounds a call through an abstract Callable input may use, null where it has several signatures (Binding.CallableBounds).
    internal (bool Confined, bool Preserves)? CallableBounds(BindingSymbol? definition, BoundType input, SemanticsKind receiver)
        => binding.CallableBounds(definition, input, receiver);

    // SPEC 8.4.10.4: a requirement function item's dispatching Contract reference (DispatchBounds' contract); null for another item.
    internal BindingSymbol? ItemContract(BoundType item) => binding.FunctionItemContext(item, requireClosed: false)?.RequirementContract;

    // SPEC 8.4.7.1, 16.3.3: whether a Type is proven Copy, whose destruction does nothing, or Sealed, whose handle's destruction runs only its
    // own destructor, under a definition's premises.
    internal bool Copy(BindingSymbol? definition, BoundType type) => binding.ProveCopy(type, binding.EffectPremises(definition)) == ConstraintProof.Proven;

    internal bool Sealed(BindingSymbol? definition, BoundType type) => binding.ProveSealed(type, binding.EffectPremises(definition)) == ConstraintProof.Proven;

    // SPEC 8.8.3: a body a call of a function with a substitution may run, by index (Binding.Implementation); null past the last.
    internal BindingSymbol? Implementation(BindingSymbol function, Subst? subst, int index) => binding.Implementation(function, subst, index);
}
