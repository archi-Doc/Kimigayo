// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;

namespace Kimi.Compiler.Mir;

// A5's Call transfer (A3 5, Calls; E14, E16; DECISIONS R4-M154): a Call's plan (MirBorrows.CallRows) carries its inputs' values into its
// result by the result's record, and into each holder an argument's store reaches (W, MirBorrows.Reach) by the record of the storage it
// is: a local W reaches at its first step (the argument's direct Loans or addresses name it) the output's root (Own), E reached there its
// root and everything past it (Own and Reached.All), E reached past a holder Reached.All, and a local reached past a holder the Reached
// entry whose Type, in the view's case and Origin-erased, is the local's (ReachedEntry; Reached.All where none names it), joined over the
// ways the walk reached it. A Closure
// callee's call receiver is its input at position call.Count, the callee operand, read before the Moves, and a store into it takes W as
// any argument's (ER-closure-stores; SPEC 15.8.2, DECISIONS R4-M148). At a referent store (CallRetention.ReferentStores, a per-output
// membership: an operation of SPEC 4.6 to 4.9 writes through its exclusive receiver only what it refers to, 4.9.6) whose referent destroys
// shallowly (Destruction), the store reaches refs(S) alone (W's first step, Referents; R4-M143); where the referent may observe a
// destruction, which the record leaves to its callers, W stays, each holder past refs(S) also taking the root's record, until A5 composes
// it there (PLAN G264); where it observes only through an abstract part not proven Copy, W past refs(S) is undecided for a Loan or an
// address (R4-M147).
// Input: the state before the Call, its inspection Loans ended (R-a, MirBorrows.Endings), its terminator, and whether the checks replay
// runs; the Call's plan.
// Output: the Call's entries: its arguments' Moves, its inputs' values into the result and into the holders W reaches ({Φ_E} besides into
// an untied storage), and the result into the destination; Φ_E for an untied result, and for a result that may hold a raw pointer where it
// is untracked (MirBorrows.Addresses); in the checks replay, whether a store is undecided: its W reached a holder the guard leaves
// undecided (MirWrites.Limit, R4-M147), at a referent store whose referent observes only through an abstract part not proven Copy W past
// refs(S) would store a Loan or an address, which then stops at refs(S), or a record's pair whose Origins the view's Origin class does
// not class where its Value would store one (R4-M154); the location records it as a limit (MirBorrows.Locations). A record's pairs
// (StorageRetention.Pairs: Origins of the call's Type arguments) are judged by MirOrigins.Outlives under the view's class: one that holds
// makes its input Value (Solved, memoized per record, body and view for the program version).
// Must not choose a Call's records (MirBorrows.CallRows), compute W (MirBorrows.Reach), judge a Type (an entry's Type is Binding's, the
// local's case Type the view's) or an Origin relation (MirOrigins').
internal sealed partial class MirBorrows
{
    private readonly Dictionary<(OutputRetention Written, BoundType Type, long View), StorageRetention?> reachedEntries = new();
    private readonly Dictionary<(StorageRetention Record, MirBody Body, long View), (ulong Proven, ulong Undecided)> solved = new();
    private int[] argValues = []; // A Call's operand values by position (Input), the callee operand's at call.Count.
    private bool limit; // Whether the replay's Call at the location made an undecided store (R4-M147), read and reset by Check.
    private ulong reachedVersion; // The program version whose case Types the entry memo answers.

    // Whether a value of a case Type is a borrow, through which a read reaches what it refers to (A3 5's ret): a reference or a pair layer,
    // or a view handle, whose Index is a borrow Deref (E6).
    private static bool Borrows(BoundType? type) => MirTypes.IsReference(type) || MirTypes.View(type) is not null;

    // Two entries' join: their kinds and their pairs.
    private static StorageRetention Join(StorageRetention a, StorageRetention b)
    {
        var kinds = new RetentionKind[a.Kinds.Length];
        for (var i = 0; i < kinds.Length; i++)
        {
            kinds[i] = a.Kinds[i] | b.Kinds[i];
        }

        return new(kinds, [.. a.Pairs, .. b.Pairs], a.Untied || b.Untied);
    }

    // An input's kinds where its bit (Solved) is set: Value over Content, as the record's own Value is (inputs from 63 share the last bit,
    // which is never proven).
    private static RetentionKind Proven(RetentionKind kind, ulong proven, int input)
        => (proven & (1UL << Math.Min(input, 63))) != 0 ? (kind & ~RetentionKind.Content) | RetentionKind.Value : kind;

    // A Call (A3 5, Calls; E14, E16): its arguments' Moves, then its inputs' values into the result by its record and into the holders each
    // output's store reaches (Keep), then the result into the destination; Φ_E for an untied result and, by the untracked rule, for a
    // result whose Type may hold a raw pointer (B′ §4.2). A Value callee's value is read with the arguments', the callee operand's position
    // (Input): a Closure's call receiver (R4-M148). In the checks replay, an undecided store sets the location's limit.
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

        var untied = this.Plan(call);
        if (this.planned is { Result.Pairs.IsEmpty: false } contract)
        {
            var proven = this.Solved(state, call, contract.Result, checks);
            for (var slot = 0; slot < contract.Result.Kinds.Length; slot++)
            {
                this.resultKinds[this.slotPositions[slot]] = Proven(this.resultKinds[this.slotPositions[slot]], proven, slot);
            }
        }

        var result = 0;
        for (var p = 0; p <= call.Count; p++)
        {
            var kind = this.resultKinds[p];
            result = kind == RetentionKind.None ? result : this.sets.Union(result, this.Retained(state, this.Input(call, p), p, kind));
        }

        foreach (var written in this.planned is null ? default : this.planned.Writes)
        {
            this.Keep(state, call, written, checks);
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

    // The store through one output: W from its argument's value (at a referent store whose referent destroys shallowly, refs(S) alone),
    // each holder taking its storage's record (Kept); where the referent may observe a destruction, which the record leaves to its
    // callers, each holder past refs(S) also takes the root's record (R4-M143: W stays, with what the referent holds). An undecided store
    // of a Loan or an address (R4-M147; V16: one read, the sorted set's last element) stops at refs(S), a limit at the call.
    private void Keep(ulong[] state, in MirTerminator call, OutputRetention written, bool checks)
    {
        var position = this.slotPositions[written.Output];
        var stored = this.referentStores.AsSpan(0, this.referentCount).Contains(position);
        var input = this.TypeOf(this.Input(call, position));
        BoundType? referent = (this.wCount > 0 || stored) && input is { Kind: BoundTypeKind.Semantics, Components: [var to, ..] } ? to : null;
        var unproven = false;
        var shallow = stored && referent is not null && this.Destruction(referent, out unproven) == AccessKind.Destroy;
        var reach = this.Referents(this.argValues[position]);
        this.limit |= !shallow && this.Writes(state, this.argValues[position]) && checks;
        var undecided = unproven && this.targets.Count > reach;
        for (var t = 0; t < this.targets.Count; t++)
        {
            var target = this.targets[t];
            var value = this.Kept(state, call, written, target, checks);
            value = t >= reach && stored && !shallow ? this.sets.Union(value, this.RecordValue(state, call, written.Own, checks)) : value;
            if (t >= reach && undecided && this.sets.Elements(value) is [.., var last] && last >= this.loanBase)
            {
                this.limit |= checks;
                continue;
            }

            this.AddSlot(target, this.Arrive(target, referent, false, this.argValues[position], value), 0, Joined, issue: false);
        }
    }

    // The value a holder W reached receives through one output (DECISIONS R4-M154): each input's value by its kinds in the records of the
    // ways the walk reached the holder (Keep's), their pairs judged (Solved), {Φ_E} besides where the output is untied. A local past a
    // holder whose Type no Reached entry names, no abstract part hiding it (a raw layer's pointee read as another Type, SPEC 5.7), takes
    // Reached.All, which bounds what any storage past the root receives: the storage walk names each one.
    private int Kept(ulong[] state, in MirTerminator call, OutputRetention written, int target, bool checks)
    {
        var via = this.writingVia[target];
        var own = (via & ViaRoot) != 0 ? written.Own : null;
        var past = target == Escaped ? written.All : (via & ViaPast) != 0 ? this.ReachedEntry(written, this.writingLocal[target]) ?? written.All : null;
        var value = this.sets.Union(this.RecordValue(state, call, own, checks), this.RecordValue(state, call, past, checks));
        return (own ?? past)!.Untied ? this.sets.Union(value, this.escapedSet) : value;
    }

    // What one record stores: each input's value by its kinds, its pairs judged (Solved); none for no record.
    private int RecordValue(ulong[] state, in MirTerminator call, StorageRetention? record, bool checks)
    {
        var (value, proven) = (0, record is null ? 0 : this.Solved(state, call, record, checks));
        for (var i = 0; record is not null && i < record.Kinds.Length; i++)
        {
            var kind = Proven(record.Kinds[i], proven, i);
            var (operand, position) = (this.Input(call, this.slotPositions[i]), this.slotPositions[i]);
            value = kind == RetentionKind.None ? value : this.sets.Union(value, this.Retained(state, operand, position, kind));
        }

        return value;
    }

    // The inputs a record's pairs make Value at the call (DECISIONS R4-M154, Revision 2), a bit per input: those with a pair proven under
    // the view's Origin class (MirOrigins.Outlives at the call's site). A pair whose Origins the class does not class, or of an input past
    // the mask's 64, leaves its input undecided: a limit at the call where its Value would store a Loan or an address. Memoized per
    // record, body and view for the program version.
    private ulong Solved(ulong[] state, in MirTerminator call, StorageRetention record, bool checks)
    {
        if (record.Pairs.IsEmpty)
        {
            return 0;
        }

        this.Version();
        ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(this.solved, (record, this.body, this.view), out var known);
        if (!known)
        {
            var site = this.body.Subject(call.Source);
            foreach (var (input, longer, shorter) in record.Pairs)
            {
                var bit = 1UL << Math.Min(input, 63);
                bool? holds = input >= 63 ? null : (entry.Proven & bit) != 0 ? true : origins.Outlives(this.body, this.view, longer, shorter, site);
                (entry.Proven, entry.Undecided) = holds switch
                {
                    true => (entry.Proven | bit, entry.Undecided),
                    null => (entry.Proven, entry.Undecided | bit),
                    false => entry,
                };
            }
        }

        for (var i = 0; checks && i < record.Kinds.Length && (entry.Undecided & ~entry.Proven) >> Math.Min(i, 63) != 0; i++)
        {
            var (operand, position) = (this.Input(call, this.slotPositions[i]), this.slotPositions[i]);
            this.limit |= (entry.Undecided & ~entry.Proven & (1UL << Math.Min(i, 63))) != 0 && (record.Kinds[i] & RetentionKind.Value) == 0 &&
                this.sets.Elements(this.Retained(state, operand, position, RetentionKind.Value)) is [.., var last] && last >= this.loanBase;
        }

        return entry.Proven;
    }

    // Clears the entry and record memos at a new program version, whose case Types and records they answer.
    private void Version()
    {
        if (this.reachedVersion != this.program.Version)
        {
            this.reachedVersion = this.program.Version;
            this.reachedEntries.Clear();
            this.solved.Clear();
        }
    }

    // The Reached entry of a local W reached past a holder: the entries whose Types, in the view's case (MirCases.Type) and Origin-erased
    // (TyCtxt.Erase), are the local's case Type's, joined; the Abstract one where none has it and an abstract part hides storage; else
    // none (null; Kept takes All). Memoized per record, Type and view for the program version.
    private StorageRetention? ReachedEntry(OutputRetention written, int local)
    {
        this.Version();
        var type = types.Erase(cases.Type(this.body.Locals[local].Type));
        ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(this.reachedEntries, (written, type, this.view), out var known);
        if (!known)
        {
            entry = this.Matched(written, type);
        }

        return entry;
    }

    // ReachedEntry's answer, computed once.
    private StorageRetention? Matched(OutputRetention written, BoundType type)
    {
        StorageRetention? found = null;
        StorageRetention? hidden = null;
        foreach (var (key, retention) in written.Reached)
        {
            if (key is null)
            {
                hidden = retention;
            }
            else if (ReferenceEquals(types.Erase(cases.Type(key)), type))
            {
                found = found is null ? retention : Join(found, retention);
            }
        }

        return found ?? hidden;
    }

    // ret(i, κ) (A3 5): Value, the argument's value and, for a borrow (a reference or a view handle, Borrows), a read through it; Content,
    // a read through it only; Chain, what any chain of reads through it carries (SPEC 15.6.4; DECISIONS BU-40, R4-M138), its closure; the
    // union of each kind a record gives.
    private int Retained(ulong[] state, in MirOperand operand, int position, RetentionKind kind)
    {
        var value = this.argValues[position];
        var retained = (kind & RetentionKind.Chain) != 0 ? this.Chain(state, value) : 0;
        retained = (kind & RetentionKind.Content) != 0 || ((kind & RetentionKind.Value) != 0 && Borrows(this.TypeOf(operand)))
            ? this.sets.Union(retained, this.Content(state, value)) : retained;
        return (kind & RetentionKind.Value) != 0 ? this.sets.Union(retained, value) : retained;
    }

    // The operand at a position of a Call's contract, whose value argValues holds at that position: argument i at i, the callee operand at
    // call.Count.
    private MirOperand Input(in MirTerminator call, int position) => position < call.Count ? this.body.Operands[call.Start + position] : call.Operand;

    // An operand's case Type.
    private BoundType? TypeOf(int operand) => this.TypeOf(this.body.Operands[operand]);

    private BoundType? TypeOf(in MirOperand operand)
        => operand.Kind == MirOperandKind.Const ? this.body.Consts[operand.Arg].Type : cases.Type(this.body.TypeOf(operand.Place)!);
}
