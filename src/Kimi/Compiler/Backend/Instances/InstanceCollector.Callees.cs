// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the body a callee runs in an item's instance (R5 package K 4.5; SPEC 8.13.3; DECISIONS R5-M30, R5-M36, R5-M70, R5-M74, R5-M116): a
// Closure's body under its context, a function item's function or a requirement Item's witness, the implementation a function's
// substitution selects, and for a function without a MIR body the requirement its published row runs, the glue of what it destroys in
// place, its LegacyRow item when its legacy row writes its call in its instance (DECISIONS R5-M121), or what its intrinsic's row names (and,
// where the row says so, its Type argument's layout verdict, K 4.8); and the body of a DictionaryStorage function a legacy row or the
// Dictionary runtime declares as its callee (Declared; DECISIONS R5-M124).
// Input: a closed Closure or function item Type, or a function with its composed substitution; the program's bodies; TyCtxt
// (SubstOfClosure, SubstOfItem, Dispatches, Witness(item), Implementation, Published, Signature, DictionaryStorage); the intrinsic
// dispositions and rows; the legacy rows' declared callees.
// Output: the admitted item (Admit, the witness rows of InstanceCollector.Witnesses.cs, a glue of InstanceCollector.Glue.cs or an
// intrinsic's row), or -1 for a virtual slot's Item (X5), a Function Type value (its erasure's adapter carries it), a callee without a
// body or row (a foreign function) or a refused edge.
// Invariants: the substitution admitted is the one the body runs under, so Admit keys and represents it.
// Must not: read a site or a template, or record anything itself (Admit, Below, the layout verdict, the witness and glue rows do) but a
// declared callee without a body, a broken contract.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

internal sealed partial class InstanceCollector
{
    // The body a Closure or function item Type of an item's instance runs: a Closure's under its context, the substitution of the instance
    // that closed it (TyCtxt.SubstOfClosure, R5-M36); an ordinary item's function under the item's substitution (TyCtxt.SubstOfItem); a
    // requirement Item's witness there (TyCtxt.Witness(item), DECISIONS R5-M70), as a requirement call's. -1 for a virtual slot's Item, which
    // dispatches on its receiver as a Virtual call does, its receiver's VTable holding the entries (X5; TyCtxt.Witness(item) is null), a
    // Function Type value, whose erasure made its adapter (row 13), or a refused edge.
    private int Callee(MirProgram program, int item, SourceRef subject, BoundType callable)
    {
        if (callable is { Kind: BoundTypeKind.Closure, Symbol: { } closure })
        {
            return program[new(closure, MirBodyKind.Closure)] is { } body ? this.Admit(item, subject, body, this.types.SubstOfClosure(callable)) : -1;
        }

        if (callable is not { Kind: BoundTypeKind.FunctionItem, Symbol: { } symbol })
        {
            return -1;
        }

        if (!this.types.Dispatches(symbol))
        {
            return this.Implementation(program, item, subject, symbol, this.types.SubstOfItem(callable));
        }

        return this.types.Witness(callable) is { } target ? this.Called(program, item, subject, symbol, target) : -1;
    }

    // SPEC 8.13.3: the body of the implementation a function's substitution selects, admitted for a site of an item's body; without one,
    // what its call runs (Bodyless).
    private int Implementation(MirProgram program, int item, SourceRef subject, BindingSymbol function, Subst? subst)
        => this.Selected(program, function, subst, out var implementation) is { } body
            ? this.Admit(item, subject, body, subst) : this.Bodyless(program, item, subject, implementation, subst);

    // SPEC 8.13.3: the implementation a function's substitution selects, and its MIR body; null for a function without one.
    private MirBody? Selected(MirProgram program, BindingSymbol function, Subst? subst, out BindingSymbol implementation)
    {
        implementation = this.types.Implementation(function, subst, 0)!;
        return program.Callee(new(MirCalleeKind.Fn, implementation));
    }

    // K 4.5 rows 2, 6 and 8 (DECISIONS R5-M30, R5-M74, R5-M116): a callee without a MIR body whose published row runs a requirement on the
    // Type below a parameter (Utf8Format's format, for Text.toString, Text.tryFormat and Utf8Writer.write), that requirement's witness on
    // that Type under the composed substitution; one whose published row destroys the Type below a parameter in place (replace, SPEC 15.7:
    // Binding's one fact, which A3's borrows and the effect summary read too), that Type's glue when it may need a drop (GlueOf); one whose
    // legacy row writes its call in its instance (LegacyRow.InInstance: the Array operations and storage kinds, placeEntry), its LegacyRow
    // item (K 4.5 row 2; DECISIONS R5-M121, R5-M124, AdmitRow); the function of an intrinsic, reached by its Item (a call of it is an
    // Intrinsic callee), the target its row names under the Item's Type argument, a part of the Item Type its site made (In), read only for a
    // row that reads one (IntrinsicRow.ReadsTypeArgument, as at an intrinsic call's site, Edge; DECISIONS R5-M136); -1 for another (a
    // foreign function, an intrinsic whose row reads no Type argument). A legacy or intrinsic row whose need is TypeArguments (a whole-value
    // update, a Dictionary kernel, IntrinsicRow.Need) makes each of the call's Type arguments a use at the call first (K 4.8; DECISIONS
    // R5-M108, R5-M149).
    private int Bodyless(MirProgram program, int item, SourceRef subject, BindingSymbol function, Subst? subst)
    {
        if (subst is not null && LegacyRuntimeHelpers.Row(function.CompilerFunction) is { Need: LayoutNeed.TypeArguments })
        {
            this.LaysOutArguments(item, subject, subst);
        }

        if (this.types.Published(function) is { Runs: { } requirement, On: var on })
        {
            return this.Below(subject, function, subst, on) is { } below ? this.Runs(program, item, subject, requirement, below) : -1;
        }

        if (this.types.Published(function) is { InPlace: true, Destroys: >= 0 and var destroys })
        {
            return this.Below(subject, function, subst, destroys) is { } destroyed ? this.GlueOf(item, subject, destroyed) : -1;
        }

        if (LegacyRuntimeHelpers.Row(function.CompilerFunction) is { InInstance: true })
        {
            return this.AdmitRow(item, subject, function, subst);
        }

        if (IntrinsicDisposition.Of(function.CompilerFunction) is not { Kind: DispositionKind.Intrinsic, Id: var id } ||
            IntrinsicRows.Of(id) is not { ReadsTypeArgument: true } row)
        {
            return -1; // A foreign function, or an intrinsic whose row reads no Type argument (Test.tempDirectory's has none to read).
        }

        if (row.Need == LayoutNeed.TypeArguments)
        {
            this.LaysOutArguments(item, subject, subst!);
        }

        return this.Intrinsic(program, item, subject, row, subst!.Types[0]);
    }

    // X4, F2c (DECISIONS R5-M28, R5-M124): the body of the DictionaryStorage function a legacy row or the Dictionary runtime declares as its
    // callee (placeEntry's append, a Dictionary from none's initialize, a Dictionary glue's clearEntries), named by TyCtxt's symbol view and
    // admitted for a site under the substitution given (a generic one's over the entry Types; null for one that does not instantiate); a
    // declared callee without a body is a broken contract.
    private int Declared(MirProgram program, int item, SourceRef site, DictionaryCallee callee, Subst? subst)
    {
        if (program[new(this.types.DictionaryStorage(callee), MirBodyKind.Fn)] is { } body)
        {
            return this.Admit(item, site, body, subst);
        }

        this.Report(DiagnosticCode.InternalInvariant_Kd, site);
        return -1;
    }
}
