// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's Call contract rows (A3 5, Calls; E14; SPEC 7.2.3; DECISIONS R4-M51, R4-M63, R4-M93): the retention rows a Call applies, by callee
// kind, apart from their transfer (MirBorrows.Calls).
// Input: a Call terminator; for a callee's contract Binding's retention (TyCtxt.Retains over the callee's declaration and Subst, an
// intrinsic's library declaration included, or a Value callee's declared signature) and the intrinsic table's retained operand beyond a
// declaration (MirDescriptors); a default template's call (MirCalleeKind.Fn, Arg > 0) needs neither.
// Output: Rows, a Call's rows (input position, kind, output: the result or an argument whose store W reaches), each output's together, in
// a reused scratch, with whether the result is untied; Retaining, the CallRetention of a callee's contract.
// - A callee's contract: its rows, slots to positions, a row with no input (NoInput, Value) for each input whose writable storage the
//   call may write a pointer tied to no input into (UntiedStores, R4-M116 R-B2), and an intrinsic's row for the operand the builder adds
//   beyond its declaration.
// - P9 (SPEC 7.2.3, R4-M93): a default template's call has one row (input i, Content, Result) per input and is untied: its inputs are the
//   prologue's shared borrows of the slots, and each input's Content, its slot's value, reaches the result; no store row (the omitting
//   call's own rows apply the default's raw stores, SPEC 7.2.3, 5.2.1 Retained dependencies; R4-M116 PQ-5).
// Invariants: a CallRetention.Limit, a contract Binding does not instantiate, retains nothing the stratum follows; another terminator, an
// intrinsic without a declaration and a default template's call have no CallRetention.
// Must not read a state or apply a row (MirBorrows.Calls), derive retention (TyCtxt.Retains) or record a problem: the checks record the
// Limit as a located Unsupported_Kd at the call (R4-M51, MirBorrows.Locations).
internal sealed partial class MirBorrows
{
    private const int NoInput = -1; // A row's input: none; its value is {Φ_E}, a pointer tied to no input (CallRetention.UntiedStores).

    private Retention[] rows = []; // A Call's retention rows by operand position (Rows).

    // A Call's retention rows by operand position, each output's together, into the scratch (A3 E14): its callee's contract (Retaining),
    // slots to positions, each untied store's row with no input before its output's other rows, with an intrinsic's row for the operand the
    // builder adds beyond its declaration (MirDescriptors); a default template's call P9's (SPEC 7.2.3, R4-M93): its inputs are the
    // prologue's shared borrows of the slots, and each input's Content, its slot's value, reaches the untied result; no store row.
    private ReadOnlySpan<Retention> Rows(in MirTerminator call, out bool untied)
    {
        var (callee, count) = (call.Callee, 0);
        if (callee is { Kind: MirCalleeKind.Fn, Arg: > 0 })
        {
            MirDataflow.Grow(ref this.rows, call.Count);
            for (var i = 0; i < call.Count; i++)
            {
                this.rows[count++] = new(i, RetentionKind.Content, Retention.Result);
            }

            untied = true;
            return this.rows.AsSpan(0, count);
        }

        var retention = this.Retaining(call) is { IsLimit: false } contract ? contract : null;
        var declared = retention is null ? default : retention.Rows;
        var stores = retention is null ? default : retention.UntiedStores;
        untied = retention?.Untied == true;
        MirDataflow.Grow(ref this.rows, declared.Length + stores.Length + 1);
        var s = 0;
        foreach (var row in declared)
        {
            for (; s < stores.Length && stores[s] <= row.Output; s++)
            {
                this.rows[count++] = new(NoInput, RetentionKind.Value, MirCallee.Position(callee.Symbol, stores[s]));
            }

            var output = row.Output == Retention.Result ? Retention.Result : MirCallee.Position(callee.Symbol, row.Output);
            this.rows[count++] = new(MirCallee.Position(callee.Symbol, row.Input), row.Kind, output);
        }

        for (; s < stores.Length; s++)
        {
            this.rows[count++] = new(NoInput, RetentionKind.Value, MirCallee.Position(callee.Symbol, stores[s]));
        }

        if (callee.Kind == MirCalleeKind.Intrinsic && MirDescriptors.Of((IntrinsicId)callee.Arg).Retained is >= 0 and var added)
        {
            this.rows[count++] = new(added, RetentionKind.Value, Retention.Result);
        }

        return this.rows.AsSpan(0, count);
    }

    // The retention a Call's contract gives: TyCtxt.Retains over the callee's declaration (an intrinsic's library declaration, E14) and
    // Subst, or a Value callee's declared signature; none for another terminator, an intrinsic without a declaration or a default template's
    // call. Its Limit, a contract Binding does not instantiate, retains nothing the stratum follows: the checks record a located
    // Unsupported_Kd at the call (R4-M51).
    private CallRetention? Retaining(in MirTerminator call)
    {
        var callee = call.Callee;
        if (call.Kind != MirTerminatorKind.Call || callee is { Kind: MirCalleeKind.Intrinsic, Symbol: null } or { Kind: MirCalleeKind.Fn, Arg: > 0 })
        {
            return null;
        }

        return callee.Kind == MirCalleeKind.Value
            ? types.Retains(this.TypeOf(call.Operand)!, this.body.Consts[callee.Arg].Type, this.body.Key.Decl)
            : types.Retains(callee.Symbol!, callee.Subst, this.body.Key.Decl);
    }
}
