// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's Call transfer (A3 5, Calls; E14, E16): a Call's rows (MirBorrows.CallRows) carry its inputs' values into its result or into every
// holder an argument's store reaches (W, MirBorrows.Reach), one W per output.
// Input: the state before the Call, its inspection Loans ended (R-a, MirBorrows.Endings), its terminator, and whether the checks replay
// runs; the Call's rows.
// Output: the Call's entries: its arguments' Moves, each row's value into the result or into the holders its W reaches ({Φ_E} for a row
// with no input, an untied store), and the result into the destination; Φ_E for an untied result, and for a result that may hold a raw
// pointer where it is untracked (MirBorrows.Addresses).
// In the forward solve, a store of a non-empty value past a holder that passes the guard only by argument holds the view (P13's interim,
// R4-M73).
// Invariants: the forward solve holds the view, never the replay, which reports with the converged fact known.
// Must not choose a Call's rows (MirBorrows.CallRows) or compute W (MirBorrows.Reach).
internal sealed partial class MirBorrows
{
    private int[] argValues = [];
    private bool held; // Whether the view is held (R4-M73): a Call's W stored a non-empty value past a by-argument holder in the forward solve.

    // Whether a value of a case Type is a borrow, through which a read reaches what it refers to (A3 5's ret): a reference or a pair layer,
    // or a view handle, whose Index is a borrow Deref (E6).
    private static bool Borrows(BoundType? type) => MirTypes.IsReference(type) || MirTypes.View(type) is not null;

    // A Call (A3 5, Calls; E14, E16): its arguments' Moves, then each row of its retention (Rows) into the result or into W of an argument's
    // value, {Φ_E} for a row with no input (an untied store, R-B2), then the result into the destination; Φ_E for an untied result and, by
    // the untracked rule, for a result whose Type may hold a raw pointer (B′ §4.2). A Value callee's environment, its value or what a
    // reference to it refers to, reaches the result too (its captures; temporary until F7 reads TyCtxt.Closure, unit 2c). In the forward
    // solve, never in the checks replay, a non-empty value that W stores past a by-argument holder holds the view (R4-M73): the solve
    // applies every chain at its converged state, so the fact includes the fixpoint's, and the replay reports with it known.
    private void Call(ulong[] state, in MirTerminator call, bool checks)
    {
        MirDataflow.Grow(ref this.argValues, call.Count);
        for (var i = 0; i < call.Count; i++)
        {
            this.argValues[i] = this.Operand(state, call.Start + i);
        }

        var callee = call.Callee;
        var result = callee.Kind == MirCalleeKind.Value && call.Operand.Kind != MirOperandKind.Const ? this.Read(state, call.Operand.Place) : 0;
        result = result != 0 && MirTypes.IsReference(this.TypeOf(call.Operand)) ? this.Content(state, result) : result;
        result = this.Indirect(result); // Rule W: a Value callee's environment is no Copy of an argument.
        this.Moves(call.Start, call.Count);
        if (callee.Kind == MirCalleeKind.Value && call.Operand is { Kind: MirOperandKind.Move, Place: { Projection: 0 } moved })
        {
            this.Add(moved.Root, 0, 0, Cleared, issue: false);
        }

        var retained = this.Rows(call, out var untied);
        var writesOf = Retention.Result; // The output whose writable holders the targets are.
        var clean = 0; // The first target W reaches only past a by-argument holder.
        BoundType? referent = null; // Rule W: the Type the written argument refers to, every part of which may receive the value.
        foreach (var row in retained)
        {
            var value = row.Input == NoInput ? this.escapedSet : this.Retained(state, call.Start + row.Input, row.Input, row.Kind);
            if (row.Output == Retention.Result)
            {
                result = this.sets.Union(result, value);
                continue;
            }

            if (row.Output != writesOf)
            {
                clean = this.Writes(state, this.argValues[row.Output]);
                writesOf = row.Output;
                referent = this.wCount > 0 && this.TypeOf(call.Start + row.Output) is { Kind: BoundTypeKind.Semantics, Components: [var to, ..] } ? to : null;
            }

            this.held |= !checks && value != 0 && clean < this.targets.Count;
            foreach (var target in this.targets)
            {
                this.AddSlot(target, this.Arrive(target, referent, false, this.argValues[row.Output], value), 0, Joined, issue: false);
            }
        }

        result = untied ? this.sets.Union(result, this.escapedSet) : result;
        if (call.Target < 0)
        {
            return;
        }

        if (this.Untracked(result))
        {
            this.Work++; // The Type walk (A3 6.2).
            result = MirTypes.HoldsRaw(cases.Type(this.body.TypeOf(call.Place)!), this.seen) ? this.sets.Union(result, this.escapedSet) : result;
        }

        this.Store(state, call.Place, result, 0, issue: false);
    }

    // ret(i, κ) (A3 5): Value, the argument's value and, for a borrow (a reference or a view handle, Borrows), a read through it; Content,
    // a read through it only; Chain, what any chain of reads through it carries (SPEC 15.6.4; DECISIONS BU-40, R4-M138), its closure.
    private int Retained(ulong[] state, int operand, int position, RetentionKind kind)
    {
        var value = this.argValues[position];
        if (kind == RetentionKind.Chain)
        {
            return this.Chain(state, value);
        }

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
