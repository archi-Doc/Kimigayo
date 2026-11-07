// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The effect facts Binding decided, in MIR terms (DECISIONS R4-M22, R4-M23): who owes `confined`, the bounds a call may use under a
// definition's premises, what a function without a MIR body does, the bodies a call may run, and the Copy and Sealed facts its summaries
// read, all under one premise scope per definition (Binding.EffectPremises).
internal sealed partial class TyCtxt
{
    private readonly Dictionary<Koto, CopyFacts> copies = new(ReferenceEqualityComparer.Instance); // By premises node (a view, ResetViews).
    private bool callsRowsChecked; // Whether CheckCallsRows has held: the catalog's Calls rows are checked once, not at each read.

    // SPEC 8.11.2: what a call of a function without a MIR body does: a compiler function's class (IntrinsicDisposition.Effect), a
    // definition Binding rejected a dependent of that error (R4-M18: its parts are not published), a foreign function (SPEC 22.6) an
    // environment effect; any other is unclassified.
    internal static CompilerFunctionEffect Bodyless(BindingSymbol function) => function switch
    {
        { CompilerFunction: not CompilerFunctionKind.None } => IntrinsicDisposition.Effect(function.CompilerFunction),
        { Declaration.BindingState: not BindingState.Resolved } => CompilerFunctionEffect.Dependent,
        { Declaration: FunctionKoto declaration } when declaration.FindAttribute(AttributeKind.LibraryImport) is not null => CompilerFunctionEffect.Environment,
        _ => CompilerFunctionEffect.Unclassified,
    };

    // SPEC 4.9.6, 12.4.3: an operation's published row in MIR terms (KimiLibraryCatalog.Row): the requirement it runs on the Type below
    // parameter On, the parameter below which it destroys, -1 for none, whether it destroys that target in place (`replace`), and the
    // by-value Callable parameter it calls through a shared borrow and then destroys, -1 for none (`removeAll`); null for an operation
    // without a row.
    internal (BindingSymbol? Runs, int On, int Destroys, bool InPlace, int Calls)? Published(BindingSymbol operation)
    {
        if (KimiLibraryCatalog.Row(operation) is not { } row)
        {
            return null;
        }

        if (row.Calls >= 0 && !this.callsRowsChecked)
        {
            this.CheckCallsRows();
        }

        var requirement = row.Runs is { } runs ? this.OwnRequirement(runs) : null; // The one its Contract declares, not an inherited one.
        return (requirement, row.On, row.Destroys, row.InPlace, row.Calls);
    }

    // SPEC 8.11.3: what a requirement runs on a Self that is not abstract under the Contract reference its call recorded (MirCallee.Contract)
    // and that reference in the call's substitution (Subst.Declaring), as Witness reads them (Binding.Witnesses), appended to a caller-owned
    // list with each target's declaring instance (none for a compiler function); false where none resolves.
    internal bool Witnesses(BindingSymbol requirement, BoundType self, BindingSymbol? contract, BoundType? reference, BindingSymbol? definition, List<(BindingSymbol Target, BoundType? Declaring)> into)
        => binding.Witnesses(requirement, self, contract, reference, definition, into);

    // R4-M22: the confined obligations of the latest pass (Binding.EffectObligations), appended to a caller-owned list.
    internal void EffectObligations(List<EffectObligation> rows) => binding.EffectObligations(rows);

    // SPEC 8.11.3, 8.7: whether confined is available to a requirement or virtual call under the calling definition's own premises, null
    // for a callee that does not dispatch (Binding.DispatchBounds).
    internal bool? DispatchBounds(BindingSymbol? definition, BindingSymbol callee, BoundType? self, BindingSymbol? contract)
        => binding.DispatchBounds(definition, callee, self, contract);

    // SPEC 8.11.4: whether confined is available to a call through an abstract Callable input, null where it has several signatures
    // (Binding.CallableBounds).
    internal bool? CallableBounds(BindingSymbol? definition, BoundType input, SemanticsKind receiver)
        => binding.CallableBounds(definition, input, receiver);

    // SPEC 8.11.3: a requirement function item's dispatching Contract reference (DispatchBounds' contract); null for another item.
    internal BindingSymbol? ItemContract(BoundType item) => binding.FunctionItemContext(item)?.RequirementContract;

    // SPEC 8.7.2, 16.3.3: the Copy facts of a definition's premises (CopyFacts), one per node its premises are read at (Binding.DefinitionUse),
    // a view memo (ResetViews; DECISIONS BU-88).
    internal CopyFacts Copies(BindingSymbol? definition)
    {
        var use = binding.DefinitionUse(definition);
        if (!this.copies.TryGetValue(use, out var facts))
        {
            facts = new(binding, binding.EffectPremises(definition));
            this.copies.Add(use, facts);
        }

        return facts;
    }

    // SPEC 8.7.2, 16.3.3: whether a Type is proven Sealed, whose handle's destruction runs only its own destructor, under a definition's
    // premises.
    internal bool Sealed(BindingSymbol? definition, BoundType type) => binding.ProveSealed(type, binding.EffectPremises(definition)) == ConstraintProof.Proven;

    // SPEC 8.13.3: a body a call of a function with a substitution may run, by index (Binding.Implementation); null past the last.
    internal BindingSymbol? Implementation(BindingSymbol function, Subst? subst, int index) => binding.Implementation(function, subst, index);

    // The catalog's contract (DECISIONS BU-70), checked for every row once, before the first Calls row is formed: a row's Callable input is
    // called through a shared borrow, which its premise declares; a violation is a defect of the compiler build.
    private void CheckCallsRows()
    {
        foreach (var entry in KimiLibraryCatalog.Entries)
        {
            if (entry.Row is { Calls: >= 0 and var calls } && library.GetSymbol(entry.Id) is { } operation &&
                binding.CallableReceiver(operation, Signature(operation, null, calls, out _)!) != SemanticsKind.Ref)
            {
                throw new InvalidOperationException($"{operation.Name}'s published Callable input is not called through `ref` (InternalInvariant).");
            }
        }

        this.callsRowsChecked = true;
    }

    /// <summary>Whether each Type is proven Copy, whose destruction does nothing (SPEC 8.7.2, 16.3.3), under one definition's premises,
    /// resolved once (Binding.EffectPremises): each Type is proven at its first question (DECISIONS BU-88).</summary>
    /// <param name="binding">The Binding that proves.</param>
    /// <param name="premises">The definition's premises.</param>
    internal sealed class CopyFacts(Binding binding, BindingScope premises)
    {
        private readonly Dictionary<BoundType, bool> proven = new();

        internal bool Copy(BoundType type)
        {
            if (!this.proven.TryGetValue(type, out var copy))
            {
                copy = binding.ProveCopy(type, premises) == ConstraintProof.Proven;
                this.proven.Add(type, copy);
            }

            return copy;
        }
    }
}
