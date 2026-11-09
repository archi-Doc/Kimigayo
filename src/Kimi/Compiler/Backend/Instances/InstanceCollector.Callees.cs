// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the body a callee runs in an item's instance (R5 package K 4.5; SPEC 8.13.3; DECISIONS R5-M30, R5-M36, R5-M70, R5-M74, R5-M116): a
// Closure's body under its context, a function item's function or a requirement Item's witness, the implementation a function's
// substitution selects, and for a function without a MIR body the requirement its published row runs, the glue of what it destroys in
// place (each on the Type below a parameter, Below), or what its intrinsic's row names (and, where the row says so, its Type argument's
// layout verdict, K 4.8); and the body of a DictionaryStorage function the Dictionary runtime declares as its callee (Declared; DECISIONS
// R5-M124).
// Input: a closed Closure or function item Type, or a function with its composed substitution; the program's bodies; TyCtxt
// (SubstOfClosure, SubstOfItem, Dispatches, Witness(item), Implementation, Published, Signature, DictionaryStorage); the intrinsic
// dispositions and rows; the legacy rows' layout needs.
// Output: the admitted item (Admit, the witness rows of InstanceCollector.Witnesses.cs, a glue of InstanceCollector.Glue.cs or an
// intrinsic's row), or -1 for a virtual slot's Item (X5), a Function Type value (its erasure's adapter carries it), a callee without a
// body or row (a foreign function) or a refused edge.
// Invariants: the substitution admitted is the one the body runs under, so Admit keys and represents it; a Closure's body is the
// program's, read unchecked (the validator's program pass guarantees it, MirValidator.Program, G191).
// Must not: read a site or a template, or record anything itself (Admit, the layout verdict, the witness and glue rows do) but Below's
// verdict and a declared callee without a body, a broken contract.

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
            return this.Admit(item, subject, program[new(closure, MirBodyKind.Closure)]!, this.types.SubstOfClosure(callable));
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

    // K 4.5 rows 2 and 8 (DECISIONS R5-M30, R5-M74): a callee without a MIR body whose published row runs a requirement on the Type below
    // a parameter (Utf8Format's format, for Text.toString, Text.tryFormat and Utf8Writer.write), that requirement's witness on that Type
    // under the composed substitution; the function of an intrinsic, reached by its Item (a call of it is an Intrinsic callee), the target
    // its row names under the Item's Type argument, a part of the Item Type its site made (In), read only for a row that reads one
    // (IntrinsicRow.ReadsTypeArgument, as at an intrinsic call's site, Edge; DECISIONS R5-M136); -1 for another (a foreign function, an
    // intrinsic whose row reads no Type argument). An intrinsic row whose need is TypeArguments (IntrinsicRow.Need) makes each of the
    // call's Type arguments a use at the call first (K 4.8; DECISIONS R5-M108, R5-M149).
    private int Bodyless(MirProgram program, int item, SourceRef subject, BindingSymbol function, Subst? subst)
    {
        if (this.types.Published(function) is { Runs: { } requirement, On: var on })
        {
            return this.Below(subject, function, subst, on) is { } below ? this.Runs(program, item, subject, requirement, below) : -1;
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

    // K 4.5 rows 2 and 7 (DECISIONS BU-38): the Type below a bodyless callee's parameter at a slot in its call's instance (TyCtxt.Instance of
    // the declared Type; the declared Type without a substitution), its verdict read at the site (Made); null unless Prepared. A Prepared
    // slot without a Type below is a broken contract (a published row past the parameters).
    private BoundType? Below(SourceRef subject, BindingSymbol function, Subst? subst, int slot)
    {
        var declared = TyCtxt.Signature(function, null, slot, out _);
        var (type, storage) = declared is null || subst is null ? (declared, Binding.StorageOutcome.Prepared) : this.types.Instance(declared, subst);
        _ = TyCtxt.Layer(type, out var below, out _);
        if (storage == Binding.StorageOutcome.Prepared && below is null)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, subject);
        }

        return this.Made(storage, subject) ? below : null;
    }

    // X4 (DECISIONS R5-M124, R8-A1d, R8-K1c): the one owner of a target the compiler names: the body of a cataloged library function (a
    // Dictionary from none's initialize; a nonempty Array literal's growArray; a collection glue's destroyArray, destroyDictionary or
    // releaseDictionary), admitted for a site under the substitution given (a generic one's; null for one that does not instantiate); a
    // declared callee without a body is a broken contract.
    private int Declared(MirProgram program, int item, SourceRef site, BindingSymbol function, Subst? subst)
    {
        if (program[new(function, MirBodyKind.Fn)] is { } body)
        {
            return this.Admit(item, site, body, subst);
        }

        this.Report(DiagnosticCode.InternalInvariant_Kd, site);
        return -1;
    }
}
