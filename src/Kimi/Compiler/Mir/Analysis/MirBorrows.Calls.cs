// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's Call transfer (A3 5, Calls; E14, E-store): a Call's retention rows (Rows), its callee's contract by operand position, carry its
// inputs' values into its result or into every holder an argument's store reaches (W, MirBorrows.Values), one W per output; a value stored
// only past a holder that passes the guard by argument carries P13's taint (R4-M73).
internal sealed partial class MirBorrows
{
    private int[] argValues = [];
    private Retention[] rows = []; // A Call's retention rows by operand position (Rows).

    // Whether a value of a case Type is a borrow, through which a read reaches what it refers to (A3 5's ret): a reference or a pair layer,
    // or a view handle, whose Index is a borrow Deref (E6).
    private static bool Borrows(BoundType? type) => MirTypes.IsReference(type) || MirTypes.View(type) is not null;

    // A Call at a location (A3 5, Calls; E14, E-store): its arguments' Moves, then each row of its retention (Rows) into the result or into
    // W of an argument's value, then the result into the destination (Placed: a default template's result carries its slot's default taint);
    // Φ_E for an untied result. A Value callee's environment, its value or what a reference to it refers to, reaches the result too (its
    // captures; temporary until F7 reads TyCtxt.Closure, unit 2c).
    private void Call(ulong[] state, MirLocation at, in MirTerminator call)
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
        var (writesOf, clean) = (Retention.Result, 0); // The output whose writable holders the targets are, and the first P13 one.
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

            var tainted = clean < this.targets.Count ? this.sets.Add(value, Markers + this.body.Locals.Length) : value; // P13's marker.
            for (var k = 0; k < this.targets.Count; k++)
            {
                this.AddSlot(this.targets[k], k < clean ? value : tainted, 0, Joined, issue: false);
            }
        }

        result = untied ? this.sets.Union(result, this.escapedSet) : result;
        if (call.Target >= 0)
        {
            this.Store(state, call.Place, this.Placed(result, call.Place, at), 0, issue: false);
        }
    }

    // A Call's retention rows by operand position, each output's together, into the scratch (A3 E14): its callee's contract (Retaining),
    // slots to positions, with an intrinsic's row for the operand the builder adds beyond its declaration (MirDescriptors); a default
    // template's call is an unknown callee until F9 (unit 2e): every input reaches the result and each exclusive input's referents, and its
    // result is untied.
    private ReadOnlySpan<Retention> Rows(in MirTerminator call, out bool untied)
    {
        var (callee, count) = (call.Callee, 0);
        if (callee is { Kind: MirCalleeKind.Fn, Arg: > 0 })
        {
            MirDataflow.Grow(ref this.rows, call.Count * (call.Count + 1));
            for (var output = Retention.Result; output < call.Count; output++)
            {
                if (output >= 0 && !MirTypes.IsExclusive(this.TypeOf(call.Start + output)))
                {
                    continue;
                }

                for (var i = 0; i < call.Count; i++)
                {
                    this.rows[count++] = new(i, RetentionKind.Value, output);
                }
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

    // An operand's case Type.
    private BoundType? TypeOf(int operand) => this.TypeOf(this.body.Operands[operand]);

    private BoundType? TypeOf(in MirOperand operand)
        => operand.Kind == MirOperandKind.Const ? this.body.Consts[operand.Arg].Type : cases.Type(this.body.TypeOf(operand.Place)!);
}
