// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the body a callee runs in an item's instance (R5 package K 4.5; SPEC 8.13.3; DECISIONS R5-M30, R5-M36, R5-M70, R5-M74): a
// Closure's body under its context, a function item's function or a requirement Item's witness, the implementation a function's
// substitution selects, and for a function without a MIR body the requirement its published row runs or what its intrinsic's row names
// (and, where the row says so, its Type argument's layout verdict, K 4.8).
// Input: a closed Closure or function item Type, or a function with its composed substitution; the program's bodies; TyCtxt
// (SubstOfClosure, SubstOfItem, Dispatches, Witness(item), Implementation, Published, Signature); the intrinsic dispositions and rows.
// Output: the admitted item (Admit, the witness rows of InstanceCollector.Witnesses.cs or an intrinsic's row), or -1 for a virtual slot's
// Item (X5), a Function Type value (its erasure's adapter carries it), a callee without a body or row (C8*; a foreign function) or a
// refused edge.
// Invariants: the substitution admitted is the one the body runs under, so Admit keys and represents it.
// Must not: read a site or a template, or record anything itself (Admit, the layout verdict and the witness rows do).

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

        return this.types.Witness(callable) is { } target ? this.Requirement(program, item, subject, symbol, target) : -1;
    }

    // SPEC 8.13.3: the body of the implementation a function's substitution selects, admitted for a site of an item's body; without one,
    // what its call runs (Bodyless).
    private int Implementation(MirProgram program, int item, SourceRef subject, BindingSymbol function, Subst? subst)
    {
        var implementation = this.types.Implementation(function, subst, 0)!;
        return program.Callee(new(MirCalleeKind.Fn, implementation)) is { } body
            ? this.Admit(item, subject, body, subst) : this.Bodyless(program, item, subject, implementation, subst);
    }

    // K 4.5 rows 2, 6 and 8 (DECISIONS R5-M30, R5-M74): a callee without a MIR body whose published row runs a requirement on the Type below
    // a parameter (Utf8Format's format, for Text.toString, Text.tryFormat and Utf8Writer.write), that requirement's witness on that Type
    // under the composed substitution; the function of an intrinsic, reached by its Item (a call of it is an Intrinsic callee), the target
    // its row names under the Item's Type argument, which is a by-value use where the row says so, as at the intrinsic's call (K 4.8,
    // IntrinsicRows.LaysOutType; DECISIONS R5-M108); -1 for another (a legacy row's callees and needs, C8*; a foreign function).
    private int Bodyless(MirProgram program, int item, SourceRef subject, BindingSymbol function, Subst? subst)
    {
        if (this.types.Published(function) is { Runs: { } requirement, On: var on })
        {
            _ = TyCtxt.Layer(TyCtxt.Signature(function, subst, on, out _), out var below, out _);
            return this.Runs(program, item, subject, requirement, below);
        }

        if (IntrinsicDisposition.Of(function.CompilerFunction) is not { Kind: DispositionKind.Intrinsic, Id: var id })
        {
            return -1;
        }

        var row = IntrinsicRows.Of(id);
        if (row.LaysOutType)
        {
            _ = this.LaysOut(subject, subst!.Types[0]!);
        }

        return row.Target is not IntrinsicTarget.None ? this.Intrinsic(program, item, subject, row.Target, subst!.Types[0]) : -1;
    }
}
