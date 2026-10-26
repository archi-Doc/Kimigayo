// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's Call transfer (A3 5, Calls; E14, E16): a Call's rows (MirBorrows.CallRows) carry its inputs' values into its result or into every
// holder an argument's store reaches (W, MirBorrows.Reach), one W per output; a Closure callee's call receiver is its input at position
// call.Count, the callee operand, read before the Moves, and a store into it takes W as any argument's (ER-closure-stores; SPEC 15.8.2,
// DECISIONS R4-M148). At a referent store (CallRetention.ReferentStores, a
// per-output membership: an operation of SPEC 4.6 to 4.9 writes through its exclusive receiver only what it refers to, 4.9.6) whose
// referent destroys shallowly (Destruction), the store reaches refs(S) alone (W's first step, Referents; R4-M143); where the referent may
// observe a destruction, which the row leaves to its callers, W stays, until A5 composes it there (PLAN G264); where it observes only
// through an abstract part not proven Copy, W past refs(S) is undecided for a Loan or an address (R4-M147).
// Input: the state before the Call, its inspection Loans ended (R-a, MirBorrows.Endings), its terminator, and whether the checks replay
// runs; the Call's rows.
// Output: the Call's entries: its arguments' Moves, each row's value into the result or into the holders its W reaches ({Φ_E} for a row
// with no input, an untied store), and the result into the destination; Φ_E for an untied result, and for a result that may hold a raw
// pointer where it is untracked (MirBorrows.Addresses); in the checks replay, whether a store is undecided (R4-M147): its W reached a
// holder the guard leaves undecided (MirWrites.Limit), or at a referent store whose referent observes only through an abstract part not
// proven Copy (MirBorrows.Destruction's unproven), W past refs(S) would store a Loan or an address, which then stops at refs(S); the
// location records it as a limit (MirBorrows.Locations).
// Must not choose a Call's rows (MirBorrows.CallRows) or compute W (MirBorrows.Reach).
internal sealed partial class MirBorrows
{
    private int[] argValues = []; // A Call's operand values by position (Input), the callee operand's at call.Count.
    private bool limit; // Whether the replay's Call at the location made an undecided store (R4-M147), read and reset by Check.

    // Whether a value of a case Type is a borrow, through which a read reaches what it refers to (A3 5's ret): a reference or a pair layer,
    // or a view handle, whose Index is a borrow Deref (E6).
    private static bool Borrows(BoundType? type) => MirTypes.IsReference(type) || MirTypes.View(type) is not null;

    // A Call (A3 5, Calls; E14, E16): its arguments' Moves, then each row of its retention (Rows) into the result or into W of an argument's
    // value (at a referent store whose referent destroys shallowly, its refs(S)), {Φ_E} for a row with no input (an untied store, R-B2),
    // then the result into the destination; Φ_E for an untied result and, by the untracked rule, for a result whose Type may hold a raw
    // pointer (B′ §4.2). A Value callee's value is read with the arguments', the callee operand's position (Input): a Closure's call
    // receiver (R4-M148). In the checks replay, an undecided store sets the location's limit.
    private void Call(ulong[] state, in MirTerminator call, bool checks)
    {
        MirDataflow.Grow(ref this.argValues, call.Count + 1);
        for (var i = 0; i < call.Count; i++)
        {
            this.argValues[i] = this.Operand(state, call.Start + i);
        }

        var callee = call.Callee;
        this.argValues[call.Count] = callee.Kind == MirCalleeKind.Value && call.Operand.Kind != MirOperandKind.Const ? this.Read(state, call.Operand.Place) : 0;
        this.Moves(call.Start, call.Count);
        if (callee.Kind == MirCalleeKind.Value && call.Operand is { Kind: MirOperandKind.Move, Place: { Projection: 0 } moved })
        {
            this.Add(moved.Root, 0, 0, Cleared, issue: false);
        }

        var retained = this.Rows(call, out var untied);
        var result = 0;
        var writesOf = Retention.Result; // The output whose writable holders the targets are.
        BoundType? referent = null; // Rule W: the Type the written argument refers to, every part of which may receive the value.
        var (reach, undecided) = (0, false); // refs(S)'s count, and whether W past it rests on an abstract part's destruction.
        foreach (var row in retained)
        {
            var value = row.Input == NoInput ? this.escapedSet : this.Retained(state, this.Input(call, row.Input), row.Input, row.Kind);
            if (row.Output == Retention.Result)
            {
                result = this.sets.Union(result, value);
                continue;
            }

            if (row.Output != writesOf)
            {
                var stored = this.referentStores.AsSpan(0, this.referentCount).Contains(row.Output);
                var written = this.Input(call, row.Output);
                referent = (this.wCount > 0 || stored) && this.TypeOf(written) is { Kind: BoundTypeKind.Semantics, Components: [var to, ..] } ? to : null;
                var unproven = false;
                var shallow = stored && referent is not null && this.Destruction(referent, out unproven) == AccessKind.Destroy;
                reach = this.Referents(this.argValues[row.Output]);
                this.limit |= !shallow && this.Writes(state, this.argValues[row.Output]) && checks;
                (undecided, writesOf) = (unproven && this.targets.Count > reach, row.Output);
            }

            // An undecided store of a Loan or an address (R4-M147; V16: one read, the sorted set's last element) stops at refs(S), a limit at
            // the call.
            var count = undecided && this.sets.Elements(value) is [.., var last] && last >= this.loanBase ? reach : this.targets.Count;
            this.limit |= count < this.targets.Count && checks;
            for (var t = 0; t < count; t++)
            {
                var target = this.targets[t];
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
    private int Retained(ulong[] state, in MirOperand operand, int position, RetentionKind kind)
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

    // The operand at a position of a Call's rows, whose value argValues holds at that position: argument i at i, the callee operand at
    // call.Count.
    private MirOperand Input(in MirTerminator call, int position) => position < call.Count ? this.body.Operands[call.Start + position] : call.Operand;

    // An operand's case Type.
    private BoundType? TypeOf(int operand) => this.TypeOf(this.body.Operands[operand]);

    private BoundType? TypeOf(in MirOperand operand)
        => operand.Kind == MirOperandKind.Const ? this.body.Consts[operand.Arg].Type : cases.Type(this.body.TypeOf(operand.Place)!);
}
