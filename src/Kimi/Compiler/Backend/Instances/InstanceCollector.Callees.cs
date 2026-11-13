// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the body a callee runs in an item's instance (R5 package K 4.5; SPEC 8.13.3; DECISIONS R5-M30, R5-M36, R5-M70, R5-M74, R5-M116): a
// Closure's body under its context, a function item's function or a requirement Item's witness, the implementation a function's
// substitution selects, and for a function without a MIR body what its intrinsic's row names (and, where the row says so, its Type
// argument's layout verdict, K 4.8), or for a #LibraryImport function its Import item (Imported; DECISIONS R8-M19); and the body of a
// DictionaryStorage function the Dictionary runtime declares as its callee (Declared; DECISIONS R5-M124).
// Input: a closed Closure or function item Type, or a function with its composed substitution; the program's bodies; TyCtxt
// (SubstOfClosure, SubstOfItem, Dispatches, Witness(item), Implementation, DictionaryStorage, Bodyless); the intrinsic dispositions and
// rows.
// Output: the admitted item (Admit, the witness rows of InstanceCollector.Witnesses.cs, a glue of InstanceCollector.Glue.cs, an
// intrinsic's row or an Import item), or -1 for a virtual slot's Item (X5), a Function Type value (its erasure's adapter carries it), a
// compiler function without a row or a refused edge.
// Invariants: the substitution admitted is the one the body runs under, so Admit keys and represents it; a Closure's body is the
// program's, read unchecked (the validator's program pass guarantees it, MirValidator.Program, G191).
// Must not: read a site or a template, or record anything itself (Admit, the layout verdict, the witness and glue rows do) but a
// declared callee without a body and a bodyless function that is neither a compiler function nor an import, broken contracts.

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
            var subst = this.types.SubstOfClosure(callable);
            if (program[new(closure, MirBodyKind.Closure)] is { } body)
            {
                return this.Admit(item, subject, body, subst);
            }

            // The program has no body of a Closure whose literal Binding did not publish, one in a declaration it rejected (a static whose
            // Type is not Owned): the validator names a Closure's body only where a read body creates it (MirValidator.Program), and
            // this call reads its value from a failed access. Its request is checked as any other (Requested) and then rests on
            // Binding's record, as a target with a finding does (DECISIONS R5-M183); with Binding complete no declaration was rejected,
            // so the missing body is a broken contract.
            if (this.Requested(subject, closure, subst, out _) && this.types.BindingComplete)
            {
                this.Report(DiagnosticCode.InternalInvariant_Kd, subject);
            }

            return -1;
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

    // K 4.5 row 8 (DECISIONS R5-M30, R5-M74): a callee without a MIR body: the function of an intrinsic, reached by its Item (a call of
    // it is an Intrinsic callee), the target its row names under the Item's Type argument, a part of the Item Type its site made (In), read
    // only for a row that reads one (IntrinsicRow.ReadsTypeArgument, as at an intrinsic call's site, Edge; DECISIONS R5-M136); -1 for
    // another (an intrinsic whose row reads no Type argument); a function that is no compiler function, its import's item (Imported). An
    // intrinsic row whose need is TypeArguments (IntrinsicRow.Need) makes each of the call's Type arguments a use at the call first (K 4.8;
    // DECISIONS R5-M108, R5-M149).
    private int Bodyless(MirProgram program, int item, SourceRef subject, BindingSymbol function, Subst? subst)
    {
        if (function.CompilerFunction == CompilerFunctionKind.None)
        {
            return this.Imported(item, subject, function);
        }

        if (IntrinsicDisposition.Of(function.CompilerFunction) is not { Kind: DispositionKind.Intrinsic, Id: var id } ||
            IntrinsicRows.Of(id) is not { ReadsTypeArgument: true } row)
        {
            return -1; // An intrinsic whose row reads no Type argument (Test.tempDirectory's has none to read).
        }

        if (row.Need == LayoutNeed.TypeArguments)
        {
            this.LaysOutArguments(item, subject, subst!);
        }

        return this.Intrinsic(program, item, subject, row, subst!.Types[0]);
    }

    // SPEC 22.6 (DECISIONS R8-M19): what a call of a function without a MIR body that is no compiler function runs, its direct call or a
    // requirement's or requirement Item's whose witness it is (Called; Binding rejects its own Item): a #LibraryImport function's item
    // (TyCtxt.Bodyless: a foreign function), keyed by the function alone (an import is never generic), with no target; code generation
    // calls it by its C signature (FunctionCodegen.Calls' Witness). None for a definition Binding rejected, that error's dependent (no
    // record); any other, a compiler function taken as a witness included, is a broken contract.
    private int Imported(int item, SourceRef subject, BindingSymbol function)
    {
        switch (TyCtxt.Bodyless(function))
        {
            case CompilerFunctionEffect.Dependent:
                return -1;
            case CompilerFunctionEffect.Environment when function.CompilerFunction == CompilerFunctionKind.None:
                var key = new MonoKey(MonoKind.Import, default, null, function, 0, null);
                if (this.Set.Find(key) is >= 0 and var found)
                {
                    return found;
                }

                this.Work++;
                return this.Set.Add(new() { Key = key, BodyOrdinal = -1, Parent = item, Growth = new(0, 0, this.Above(item)), Site = subject });
            default:
                this.Report(DiagnosticCode.InternalInvariant_Kd, subject);
                return -1;
        }
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
