// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's Call transfer (A3 5, Calls; E14, E16): a Call's rows (MirBorrows.CallRows) carry its inputs' values into its result or into every
// holder an argument's store reaches (W, MirBorrows.Reach), one W per output; a Closure callee's call receiver is its input at position
// call.Count, the callee operand, read before the Moves, and a store into it takes W as any argument's (ER-closure-stores; SPEC 15.8.2,
// DECISIONS R4-M148). At a referent store (CallRetention.ReferentStores, a
// per-output membership: an operation of SPEC 4.6 to 4.9 writes through its exclusive receiver only what it refers to, 4.9.6) whose
// referent destroys shallowly (Destruction), the store reaches refs(S) alone (W's first step, Referents; R4-M143); where the referent may
// observe a destruction, which the row leaves to its callers, W stays, until A5 composes it there (PLAN G264); where it observes only
// through an abstract part not proven Copy (R4-M147), or its destruction passes the destruction walk's bound (R4-M153), W past refs(S) is
// undecided for a Loan or an address.
// Input: the state before the Call, its inspection Loans ended (R-a, MirBorrows.Endings), its terminator, and whether the checks replay
// runs; the Call's rows.
// Output: the Call's entries: its arguments' Moves, each row's value into the result or into the holders its W reaches ({Φ_E} for a row
// with no input, an untied store), and the result into the destination; Φ_E for an untied result, and for a result that may hold a raw
// pointer where it is untracked (MirBorrows.Addresses); in the checks replay, what leaves a store undecided (R4-M147, R4-M153): its W
// reached a holder the guard leaves undecided (MirWrites.Limit or Deep), or at a referent store whose referent observes only through an
// abstract part not proven Copy (MirBorrows.Destruction's unproven, Limit) or whose destruction passes the walk's bound (Deep), W past
// refs(S) would store a Loan or an address, which then stops at refs(S); the location records it as a limit (MirBorrows.Locations).
// Must not choose a Call's rows (MirBorrows.CallRows) or compute W (MirBorrows.Reach).
internal sealed partial class MirBorrows
{
    private int[] argValues = []; // A Call's operand values by position (Input), the callee operand's at call.Count.
    private MirWrites limit; // What the replay's Call at the location left a store undecided by (R4-M147, R4-M153), read and reset by Check.

    // Whether a value of a case Type is a borrow, through which a read reaches what it refers to (A3 5's ret): a reference or a pair layer,
    // or a view handle, whose Index is a borrow Deref (E6).
    private static bool Borrows(BoundType? type) => MirTypes.IsReference(type) || MirTypes.View(type) is not null;

    // A Call (A3 5, Calls; E14, E16): its arguments' Moves, then each row of its retention (Rows) into the result or into W of an argument's
    // value (at a referent store whose referent destroys shallowly, its refs(S)), {Φ_E} for a row with no input (an untied store, R-B2),
    // then the result into the destination; Φ_E for an untied result and, by the untracked rule, for a result whose Type may hold a raw
    // pointer (B′ §4.2). A Value callee's value is read with the arguments', the callee operand's position (Input): a Closure's call
    // receiver (R4-M148). In the checks replay, an undecided store sets the location's limit, the Limit outranking Deep.
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
        var (reach, undecided) = (0, MirWrites.None); // refs(S)'s count, and what leaves W past it undecided: an abstract part's destruction
        // (Limit) or one past the destruction walk's bound (Deep).
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
                AccessKind? destroys = stored && referent is not null ? this.Destruction(referent, out unproven) : null; // A referent store's.
                reach = this.Referents(this.argValues[row.Output]);
                var writes = destroys == AccessKind.Destroy ? MirWrites.None : this.Writes(state, this.argValues[row.Output]);
                this.limit = checks ? Undecided(this.limit, writes) : MirWrites.None;
                var past = unproven ? MirWrites.Limit : destroys == AccessKind.Unclassified ? MirWrites.Deep : MirWrites.None;
                (undecided, writesOf) = (this.targets.Count > reach ? past : MirWrites.None, row.Output);
            }

            // An undecided store of a Loan or an address (R4-M147, R4-M153; V16: one read, the sorted set's last element) stops at refs(S),
            // a limit at the call.
            var stops = undecided != MirWrites.None && this.sets.Elements(value) is [.., var last] && last >= this.loanBase;
            var count = stops ? reach : this.targets.Count;
            this.limit = checks && stops && count < this.targets.Count ? Undecided(this.limit, undecided) : this.limit;
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
