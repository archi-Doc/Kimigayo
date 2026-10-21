// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's Call transfer (A3 5, Calls; E14, E16): a Call's retention rows (Rows), its callee's contract by operand position or a default
// template's P9 rows (R4-M93), carry its inputs' values into its result or into every holder an argument's store reaches (W,
// MirBorrows.Values), one W per output; a store of a non-empty value past a holder that passes the guard only by argument holds the view
// (P13's interim, R4-M73); a template call whose slot holds raw authority beside a Loan is a located Unsupported_Kd (r3, R4-M93).
internal sealed partial class MirBorrows
{
    private int[] argValues = [];
    private bool held; // Whether the view is held (R4-M73): a Call's W stored a non-empty value past a by-argument holder in the forward solve.
    private Retention[] rows = []; // A Call's retention rows by operand position (Rows).

    // Whether a value of a case Type is a borrow, through which a read reaches what it refers to (A3 5's ret): a reference or a pair layer,
    // or a view handle, whose Index is a borrow Deref (E6).
    private static bool Borrows(BoundType? type) => MirTypes.IsReference(type) || MirTypes.View(type) is not null;

    // A Call at a location (A3 5, Calls; E14, E16): its arguments' Moves, then each row of its retention (Rows) into the result or into
    // W of an argument's value, then the result into the destination (Placed: an inline default copy's store into its slot carries the
    // slot's default taint); Φ_E for an untied result. A Value callee's environment, its value or what a reference to it refers to, reaches
    // the result too (its captures; temporary until F7 reads TyCtxt.Closure, unit 2c). In the forward solve, never in the checks replay, a
    // non-empty value that W stores past a by-argument holder holds the view (R4-M73): the solve applies every chain at its converged state,
    // so the fact includes the fixpoint's, and the replay reports with it known. In the replay, a default template's call whose slot holds
    // raw authority beside a Loan is a located Unsupported_Kd at the call (r3, R4-M93).
    private void Call(ulong[] state, MirLocation at, in MirTerminator call, bool checks)
    {
        MirDataflow.Grow(ref this.argValues, call.Count);
        for (var i = 0; i < call.Count; i++)
        {
            this.argValues[i] = this.Operand(state, call.Start + i);
        }

        var callee = call.Callee;
        var result = callee.Kind == MirCalleeKind.Value && call.Operand.Kind != MirOperandKind.Const ? this.Read(state, call.Operand.Place) : 0;
        result = result != 0 && MirTypes.IsReference(this.TypeOf(call.Operand)) ? this.Content(state, result) : result;
        this.Moves(call.Start, call.Count);
        if (callee.Kind == MirCalleeKind.Value && call.Operand is { Kind: MirOperandKind.Move, Place: { Projection: 0 } moved })
        {
            this.Add(moved.Root, 0, 0, Cleared, issue: false);
        }

        var retained = this.Rows(call, out var untied);
        var writesOf = Retention.Result; // The output whose writable holders the targets are.
        var clean = 0; // The first target W reaches only past a by-argument holder.
        foreach (var row in retained)
        {
            var value = this.Retained(state, call.Start + row.Input, row.Input, row.Kind);
            if (row.Output == Retention.Result)
            {
                result = this.sets.Union(result, value);
                continue;
            }

            if (row.Output != writesOf)
            {
                clean = this.Writes(state, this.argValues[row.Output]);
                writesOf = row.Output;
            }

            this.held |= !checks && value != 0 && clean < this.targets.Count;
            foreach (var target in this.targets)
            {
                this.AddSlot(target, value, 0, Joined, issue: false);
            }
        }

        if (checks && callee is { Kind: MirCalleeKind.Fn, Arg: > 0 } && this.RawBesideLoan(call, result))
        {
            this.Unsupported(at); // r3 (R4-M93): the default may write by raw authority where no row follows (PLAN G160).
        }

        result = untied ? this.sets.Union(result, this.escapedSet) : result;
        if (call.Target >= 0)
        {
            this.Store(state, call.Place, this.Placed(result, call.Place, at), 0, issue: false);
        }
    }

    // A Call's retention rows by operand position, each output's together, into the scratch (A3 E14): its callee's contract (Retaining),
    // slots to positions, with an intrinsic's row for the operand the builder adds beyond its declaration (MirDescriptors); a default
    // template's call P9's (SPEC 7.2.3, R4-M93): its inputs are the prologue's shared borrows of the slots, and each input's Content, its
    // slot's value, reaches the untied result; no store row (a slot that holds raw authority is r3's).
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
        untied = retention?.Untied == true;
        MirDataflow.Grow(ref this.rows, declared.Length + 1);
        foreach (var row in declared)
        {
            var output = row.Output == Retention.Result ? Retention.Result : MirCallee.Position(callee.Symbol, row.Output);
            this.rows[count++] = new(MirCallee.Position(callee.Symbol, row.Input), row.Kind, output);
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

    // ret(i, κ) (A3 5): Value, the argument's value and, for a borrow (a reference or a view handle, Borrows), a read through it; Content,
    // a read through it only.
    private int Retained(ulong[] state, int operand, int position, RetentionKind kind)
    {
        var value = this.argValues[position];
        if (kind == RetentionKind.Content)
        {
            return this.Content(state, value);
        }

        return Borrows(this.TypeOf(operand)) ? this.sets.Union(value, this.Content(state, value)) : value;
    }

    // r3 (R4-M93; registered for X-2, PD28, until G160's representation): whether a default template's call, whose inputs' Contents hold a
    // Loan, has a slot whose case Type holds raw authority (MirTypes.HoldsRaw: a raw pointer, an rc handle or a Weak of one), through which
    // the default may write by raw authority (SPEC 5.2.1) where no row follows; an input's case Type is a shared reference to its slot,
    // whose referent the walk reaches.
    private bool RawBesideLoan(in MirTerminator call, int contents)
    {
        if (this.sets.Elements(contents) is not [.., var last] || last < this.loanBase)
        {
            return false;
        }

        this.Work += call.Count; // The Type walks (A3 6.2).
        for (var i = 0; i < call.Count; i++)
        {
            if (MirTypes.HoldsRaw(this.TypeOf(call.Start + i), this.seen))
            {
                return true;
            }
        }

        return false;
    }

    // An operand's case Type.
    private BoundType? TypeOf(int operand) => this.TypeOf(this.body.Operands[operand]);

    private BoundType? TypeOf(in MirOperand operand)
        => operand.Kind == MirOperandKind.Const ? this.body.Consts[operand.Arg].Type : cases.Type(this.body.TypeOf(operand.Place)!);
}
