// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;

namespace Kimi.Compiler.Mir;

// A5's Call transfer (A3 5, Calls; E14, E16; DECISIONS R4-M154): a Call's plan (MirBorrows.CallRows) carries its inputs' values into its
// result by the result's record, and into each holder an argument's store reaches (W, MirBorrows.Reach) by the record of the storage it
// is: a local W reaches at its first step (the argument's direct Loans or addresses name it) the output's root (Own), E reached there its
// root and everything past it (Own and Reached.All), E reached past a holder Reached.All (through one layer's record alone, a punned
// reading's layers or the layer at which the call may write its own pointer, that layer's pointee's Reached entry: MirBorrows.Reach's
// Punned, Unowned, OwnPointer), and a local reached past a holder the Reached entry whose Type, in the view's case and Origin-erased, is
// that of the storage by which W reached it (the reading layer's pointee or referent where W read the holder before it at a reading key,
// MirBorrows.Reach's ReadPart; else the Place the reaching Loan names, Revision 8) (PastEntry, ReachedEntry; Reached.All where none
// names it, never the Abstract entry), joined
// over the ways the walk reached it. A Closure
// callee's call receiver is its input at position call.Count, the callee operand, read before the Moves, and a store into it takes W as
// any argument's (ER-closure-stores; SPEC 15.8.2, DECISIONS R4-M148). At a referent store (CallRetention.ReferentStores, a per-output
// membership: an operation of SPEC 4.6 to 4.9 writes through its exclusive receiver only what it refers to, 4.9.6) the store reaches
// W's first step alone (Referents; R4-M143), and the destruction the operation's published row lists is composed at the call
// after its rows (SPEC 4.9.6, 15.6.4; MirBorrows.Destructions' Destroying, PLAN G264). A store through a stated raw layer at a written
// input's own address (its own, or one it holds by value at any holder: an element, a Field, a payload) also writes the storage its
// Origin covers (Cover, PLAN G338, G598, G583), and with an argument A5 proves null only that (Keep, PLAN G598).
// Input: the state before the Call, its inspection Loans ended (R-a, MirBorrows.Endings), its terminator, and whether the checks replay
// runs; the Call's plan.
// Output: the Call's entries: its arguments' Moves, its inputs' values into the result and into the holders W reaches ({Φ_E} besides into
// an untied storage), the values of the inputs an output sources into the write slot of each holder its store reaches (Keep, Sourcing;
// MirBorrows.Holders' WriteSlots), and the result into the destination, which replaces the destination's write slot (Rewrite); Φ_E for an
// untied result; in the checks replay, whether a store is undecided,
// two facts: the limit, where its W reached a
// holder the guard leaves undecided (MirWrites.Limit, R4-M147), or a record's pair whose Origins the view's Origin class does not class
// where its Value would store one (R4-M154); and the depth fact (R4-M153), where the step from a holder W met whose guard is Deep would
// change what W writes; the location records their join (MirBorrows.Locations). A record's pairs
// (StorageRetention.Pairs: Origins of the call's Type arguments) are judged by MirOrigins.Outlives under the view's class: one that holds
// makes its input Value (Solved, memoized per record, body, view and site for the program version).
// Must not choose a Call's records (MirBorrows.CallRows), compute W (MirBorrows.Reach), judge a Type (an entry's Type is Binding's, the
// Place's case Type the view's) or an Origin relation (MirOrigins').
internal sealed partial class MirBorrows
{
    private const int Unread = -2; // argReferents: no Value-less row has read the input at this Call or Drop yet.

    private readonly Dictionary<(OutputRetention Written, BoundType Type, long View), StorageRetention?> reachedEntries = new();
    private readonly Dictionary<(StorageRetention Record, MirBody Body, long View, SourceRef Site), (ulong Proven, ulong Undecided)> solved = new();
    private int[] argValues = []; // A Call's operand values by position (Input), the callee operand's at call.Count.
    private int[] argReferents = []; // Beside argValues: each input's referent (MirBorrows.Values' Referent), formed at its first Value-less read, or Unread.
    private bool limit; // Whether the replay's Call at the location made an undecided store (R4-M147, or a pun, MirBorrows.Puns), read and reset by Check.
    private bool deep; // Whether that store is undecided past a walk's bound (R4-M153), read and reset by Check.
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
    // output's store reaches (Keep), then the result into the destination; Φ_E for an untied result. A Value callee's value is read with
    // the arguments', the callee operand's position (Input): a Closure's call receiver (R4-M148). In the checks replay, an undecided store sets the location's limit.
    private void Call(ulong[] state, in MirTerminator call, bool checks)
    {
        MirDataflow.Grow(ref this.argValues, call.Count + 1);
        MirDataflow.Grow(ref this.argReferents, call.Count + 1);
        Array.Fill(this.argReferents, Unread, 0, call.Count + 1);
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

        if (this.writeCount > 0 && this.planned is { } writing && !writing.Writes.IsEmpty)
        {
            this.SourceSlots(call, writing.Result.Kinds.Length, this.Receiver(call)); // Once per call, for Keep's Sourcing.
        }

        foreach (var written in this.planned is null ? default : this.planned.Writes)
        {
            this.Cover(call, written.Output, this.planned!.Result.Kinds.Length);
            this.Keep(state, call, this.planned, written, checks);
        }

        this.covering.Clear();
        for (var p = 0; p <= call.Count; p++)
        {
            if (this.argValues[p] != 0)
            {
                this.PunCall(state, call, p, cases.TypeOf(this.Input(call, p)), this.argValues[p]); // The pun rule (MirBorrows.Puns).
            }
        }

        this.Destroying(state, call, checks);

        result = untied ? this.sets.Union(result, this.escapedSet) : result;
        result = this.sets.Union(result, this.unformedSet); // Never certainly null (PLAN G598): a call without a row may return any pointer.
        if (call.Target < 0)
        {
            return;
        }

        this.Store(state, call.Place, result, 0, issue: false);
        if (this.writeCount > 0 && call.Place is { Projection: 0, Root: >= 0 and var into })
        {
            this.Rewrite(state, into, -1); // A new call's result replaces its holder's write slot (MirBorrows.Holders' WriteSlots).
        }
    }

    // The store through one output: W from its argument's value (at a referent store, its first step alone; R4-M143), each holder taking its
    // storage's record (Received). With covering inputs (Cover), W from theirs too; an argument whose value is the empty set then has no
    // first step of its own (PLAN G338): it is null on every path (PLAN G598, A5's must-null fact), since every pointer A5 sees formed
    // names a Loan, an address or E, and Φ_N marks the bytes of an exposed holder and every call result, the only values a pointer A5
    // does not see formed can reach (MirBorrows.Flow). A Drop's drops store through the same path (MirBorrows.Destructions). `plan` is
    // the retention `written` belongs to, a Call's or a drop row's: the walk composes as it does (walkComposes).
    private void Keep(ulong[] state, in MirTerminator call, CallRetention plan, OutputRetention written, bool checks)
    {
        this.walkComposes = plan.Composes;
        var position = this.slotPositions[written.Output];
        var stored = this.referentStores.AsSpan(0, this.referentCount).Contains(position);
        // The input's layer and its pointee or referent, the Type the store writes through it at: W's first step reads the Places the
        // argument names at it (Writes; SPEC 5.7 Calls, 15.6.4), and each holder receives at it (Arrive).
        var input = cases.TypeOf(this.Input(call, position));
        BoundType? referent = input is { Kind: BoundTypeKind.Semantics, Components: [var to, ..] } ? to : null;
        // The Φ_E rule's site 1: where the walk's input key lies wholly in the callee's kept binding (KeptKey), the first step is route 1,
        // the named step (Referents).
        var root = this.covering.Count == 0 && this.KeptKey(this.punKeys[this.Key(this.InputKey(call, position, input), this.Fold(written))], written)
            ? MirWrites.Named : MirWrites.Through;
        if (this.covering.Count > 0 && this.argValues[position] == 0)
        {
            this.BeginWalk();
            this.CoveredSteps();
        }
        else
        {
            this.Referents(this.argValues[position], root);
        }

        var (limited, deepHolder) = stored ? default : this.Writes(state, call, position, written, root);
        this.limit |= limited && checks;
        this.deep |= deepHolder && checks;
        var sourced = this.writeCount > 0 ? this.Sourcing(written) : 0;
        for (var t = 0; t < this.targets.Count; t++)
        {
            var target = this.targets[t];
            var value = this.Arrive(target, referent, false, this.argValues[position], this.Received(state, call, written, target, checks));
            value = target == Escaped ? value : this.sets.Union(value, this.unformedSet); // W writes exposed holders and E only (Written).
            value = target == Escaped && this.walkResting ? this.sets.Union(value, this.undecidedSet) : value; // MirBorrows.Puns' Φ_U.
            this.AddSlot(target, value, 0, JoinedThrough, issue: false);
            if (sourced != 0 && this.slotWrites[target] >= 0)
            {
                this.AddSlot(this.slotWrites[target], sourced, 0, Joined, issue: false); // Its write slot (MirBorrows.Holders' WriteSlots).
            }
        }
    }

    // What an output's store may put in a Loan-free Case payload: the values of the inputs at the call's source slots (MirBorrows.Holders'
    // SourceSlots, found once per call by Call or a drop's Compose; no Mixing runs between, as a view with write slots ran it before
    // numbering) that its record holds (Sourced).
    private int Sourcing(OutputRetention written)
    {
        var value = 0;
        foreach (var slot in this.sourceSlots)
        {
            value = Sourced(written, slot) ? this.sets.Union(value, this.argValues[this.slotPositions[slot]]) : value;
        }

        return value;
    }

    // The value a holder W reached receives through one output (DECISIONS R4-M154): each input's value by its kinds in the records of the
    // ways the walk reached the holder (Keep's), their pairs judged (Solved), {Φ_E} besides where the output is untied. Past another
    // holder, the record is the Reached entry of the storage by which W reached it (PastEntry: a reading layer's pointee or referent, or
    // the reaching Loan's Place, Revision 8); E's is All, but one layer's pointee's Reached entry where the walk reached E only through
    // layers of that pointee (a punned reading's, the call's own pointer's: Punned, Unowned, OwnPointer; Written).
    private int Received(ulong[] state, in MirTerminator call, OutputRetention written, int target, bool checks)
    {
        var via = this.writingVia[target];
        var own = (via & ViaRoot) != 0 ? written.Own : null;
        var past = target == Escaped || (via & ViaPast) != 0 ? this.PastEntry(written, target) : null;
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

    // The inputs a record's pairs make Value at the call (DECISIONS R4-M154, Revisions 2 and 10), a bit per input: those with a conjunction
    // whose every pair is proven under the view's Origin class (MirOrigins.Outlives at the call's site, Holds). A conjunction with a pair
    // the class leaves Undecided and none refuted, or of an input past the mask's 64, leaves its input undecided: a limit at the call where
    // its Value would store a Loan or an address; one resting on a recorded failure (MirRelation.Rests) is neither proven nor undecided,
    // so it stores as an undecided one does and the call records nothing of its own. Memoized per record, body, view and site for the
    // program version:
    // Outlives resolves the pair's Origins at the site (OriginAtUse), so two calls sharing a record are judged apart.
    private ulong Solved(ulong[] state, in MirTerminator call, StorageRetention record, bool checks)
    {
        if (record.Pairs.IsEmpty)
        {
            return 0;
        }

        this.Version();
        var site = this.body.Subject(call.Source);
        ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(this.solved, (record, this.body, this.view, site), out var known);
        if (!known)
        {
            var pairs = record.Pairs;
            for (var start = 0; start < pairs.Length;)
            {
                var (input, end) = (pairs[start].Input, start + 1);
                while (end < pairs.Length && pairs[end].Joined)
                {
                    end++;
                }

                var bit = 1UL << Math.Min(input, 63);
                var holds = input >= 63 ? MirRelation.Undecided : this.Holds(pairs[start..end], site);
                entry.Proven |= holds == MirRelation.Holds ? bit : 0;
                entry.Undecided |= holds == MirRelation.Undecided ? bit : 0; // A resting or failing input is neither (no record of its own).
                start = end;
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

    // Whether one conjunction of a record's pairs holds at the call's site (MirOrigins.Outlives): its pairs' greatest answer (Fails over
    // Undecided over Rests over Holds).
    private MirRelation Holds(ReadOnlySpan<(int Input, BoundOrigin Longer, BoundOrigin Shorter, bool Joined)> conjunction, SourceRef site)
    {
        var holds = MirRelation.Holds;
        foreach (var (_, longer, shorter, _) in conjunction)
        {
            holds = (MirRelation)Math.Max((byte)holds, (byte)origins.Outlives(this.body, this.view, longer, shorter, site));
        }

        return holds;
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

    // The record a holder W reached past another holder receives (DECISIONS R4-M154, Revision 8): the storage by which W reached it, the
    // reading layer's pointee or referent where W read the holder before it at a reading key (MirBorrows.Reach's ReadPart), else the
    // Place a Loan reaching the holder names, the holder its root; the holder takes the Reached entry of that storage's Type
    // (ReachedEntry); All where none names it, or where W reached it by storages of several Types (All joins every entry, so it bounds
    // their join). E takes All (Written counts each of its reaches with no record as several) but through one layer's pointee alone (a
    // punned reading's layers, the call's own pointer's layer: Punned, Unowned, OwnPointer).
    private StorageRetention PastEntry(OutputRetention written, int target)
        => this.writingPlace[target] is (BoundType place, false) ? this.ReachedEntry(written, place) ?? written.All : written.All;

    // The Reached entry of a Place's erased case Type (PastEntry): the entries whose Types, in the view's case (MirCases.Type) and
    // Origin-erased (TyCtxt.Erase), are that Type, joined; none (null) where no entry names it, never the Abstract one, which only All
    // joins (c1: Binding's entry is the record, G964). Memoized per record, Type and view for the program version.
    private StorageRetention? ReachedEntry(OutputRetention written, BoundType place)
    {
        this.Version();
        ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(this.reachedEntries, (written, place, this.view), out var known);
        if (!known)
        {
            entry = this.Matched(written, place);
        }

        return entry;
    }

    // ReachedEntry's answer, computed once.
    private StorageRetention? Matched(OutputRetention written, BoundType type)
    {
        StorageRetention? found = null;
        foreach (var (key, retention) in written.Reached)
        {
            if (key is not null && ReferenceEquals(types.Erase(cases.Type(key)), type))
            {
                found = found is null ? retention : Join(found, retention);
            }
        }

        return found;
    }

    // ret(i, κ) (A3 5): Value, the argument's value and, for a borrow (a reference or a view handle, Borrows), a read through it; Content,
    // a read through it only; Chain, what any chain of reads through it carries (SPEC 15.6.4; DECISIONS BU-40, R4-M138), its closure; the
    // union of each kind a record gives. Only Value keeps the input's own value, its Loans and addresses (SPEC 15.6.4: retained only by
    // storage that can hold it; rule A's "but not" and the transfer rule's "never"); past a Deref the value carries the parent Loans
    // (15.6.3), and a read through one names the intermediate reference's holder, whose state gives them back. So a kind without Value
    // keeps what its reads carry less the own elements that the read at the input's referent (MirBorrows.Values' Referent, its last
    // Deref) does not carry, which stay as a read; with no referent nothing goes; markers are no part of the own value (D-935).
    private int Retained(ulong[] state, in MirOperand operand, int position, RetentionKind kind)
    {
        var value = this.argValues[position];
        var retained = (kind & RetentionKind.Chain) != 0 ? this.Chain(state, value) : 0;
        retained = (kind & RetentionKind.Content) != 0 || ((kind & RetentionKind.Value) != 0 && Borrows(cases.TypeOf(operand)))
            ? this.sets.Union(retained, this.Content(state, value)) : retained;
        if ((kind & RetentionKind.Value) != 0)
        {
            return this.sets.Union(retained, value);
        }

        ref var referent = ref this.argReferents[position];
        referent = referent == Unread ? this.Referent(state, operand) : referent;
        if (referent < 0)
        {
            return retained;
        }

        var kept = (kind & RetentionKind.Chain) != 0 ? this.Chain(state, referent) : 0;
        kept = (kind & RetentionKind.Content) != 0 ? this.sets.Union(kept, this.Content(state, referent)) : kept;
        return this.sets.Remove(retained, this.sets.Remove(this.sets.Remove(value, this.sets.Range(0, this.loanBase)), kept));
    }

    // The operand at a position of a Call's contract, whose value argValues holds at that position: argument i at i, the callee operand at
    // call.Count.
    private MirOperand Input(in MirTerminator call, int position) => position < call.Count ? this.body.Operands[call.Start + position] : call.Operand;
}
