// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Numerics;
using System.Runtime.InteropServices;

namespace Kimi.Compiler.Mir;

// A5's effect rule (SPEC 15.6.4, 15.6.7, 16.3.3, 16.4 with 16.3.1, 22.4.3, 22.4.4; DECISIONS R4-M92, Z7, Z7b): a Call's or a Drop's effects
// on static storage, by A4's key row and classes (MirEffects.Keys), against the live Loans on the view's mutable static roots.
// Input: the location's one gather, its effect access listed by MirBorrows.Accesses (a Call's set the last operand's, every operand's
// value with no holder left out; a Drop's its value's), the reservations the location activates, the view's effect roots, the passed fact
// (MirBorrows.Passes) and A4's Field, initialization and slot numbers (MirBorrows.Loans, MirEffects.Initialization, Slot).
// Output, per view of the run (each record with the views whose keys give it, R4-M11), through MirBorrows.Reports:
// - R1: for each effect root with a candidate, a write key or Unknown as an EffectWrite, else a read key as an EffectRead of the whole
//   root (shallow, SPEC 15.6.2); its first conflict at the location its body owns (D11), coded by Code (row 1: a reservation the call does
//   not activate) with the effect fact on the met Field, what decided it (a write key, else Unknown, else the read key);
// - A4's Limit or Deep, where a candidate a write would meet is met by no key: the Join of a write key's decided answer and A4's
//   (MirEffects.Outcome), a limit record at the location where it is undecided (MirBorrows.Reports' Limit: a located Unsupported_Kd for
//   Limit, OwnershipDepthLimit_Kd for Deep);
// - R3: at a Drop of a mutable static, its own read or write key on that static, or Unknown, ComparisonLoanConflict_Kd anchored at the
//   Drop, no Loan issued and no witness (the destruction's hold, SPEC 16.4), its template's inside an inline default copy (D11); Limit or
//   Deep without them, that answer's limit record;
// - the initialization proof (SPEC 15.6.4, af6410d81: a slot a site may first-access is proved initialized there exactly when every path to
//   the site passes a Borrow of it; 22.4.4: per slot): P, the statics with one slot that MirBorrows.Passes finds passed, are proved, and
//   every decision on a root reads the row with P's initializations left out (MirEffects.Keys' excluded), from Spread's row where no
//   initializer of P may make what the decision reads (Row). Q, the statics with several slots that a live candidate is on and whose
//   initialization the site may run, is the temporary reading of R4-M92 (Z7) until P42's per-access key (R4-M103) makes every static one
//   slot per key, which removes Q, Holds and Q's Unsupported_Kd together: R4-M92's chain skips a kind Q's initializer may make (Rests), and
//   a conflict only such kinds make is the located Unsupported_Kd. For a static with one slot, liveness proves nothing. An initializer
//   with a validator finding is A4's Derived (R5-M183): its keys never decide, and its summary is not read.
// Invariants: a Derived class reports nothing by itself (R4-M10); the case view Keys selects is the run's again when the rule returns; a
// row is read from A5's copy, never from A4's scratch after another query.
// Must not decide whether an access meets a Loan (MirBorrows.Checks), record other than through Report and Limit, rank outcomes, or read keys
// outside a Call or a Drop. A static a Loan or a Drop roots has its initializer: the validator checks that its body ensures it and that
// each staticEnsure's static has one (G191).
internal sealed partial class MirBorrows
{
    private readonly List<int> proved = []; // P: the statics with one slot every path to the location passes a Borrow of (Proof),
    private readonly List<int> undecided = []; // Q: the statics with several slots a candidate is on whose initialization the row may run.
    private int[] activated = []; // By Loan: the effect rule's run whose location activates it (W6).
    private int effecting; // The effect rule's runs, the stamp of activated, never reset.
    private ulong[] effectRow = []; // The effect rule's copy of A4's key row of the view it records,
    private ulong[] provenRow = []; // the row with P's initializations left out (Row), its classes,
    private byte provenClasses;
    private ulong[] proving = []; // and P's initialization bits, the excluded row of its query.
    private int provingWords;
    private byte provenState; // In the view: 0 P and Q not formed, 1 formed (Proof), 2 the row without P queried (Row).

    // A bit of a key row: 2f a read and 2f + 1 a write of the Field A4 numbers f.
    private static bool Bit(ReadOnlySpan<ulong> row, int bit) => (row[bit >> 6] & (1UL << bit)) != 0;

    // What decides an effect on the Field A4 numbers f in a key row and classes: a write key, else Unknown, else a read key; null for none.
    private static ProblemFactKind? Decided(ReadOnlySpan<ulong> row, byte classes, int field)
    {
        if (Bit(row, (2 * field) + 1))
        {
            return ProblemFactKind.EffectWrite;
        }

        if ((classes & MirEffects.Unknown) != 0)
        {
            return ProblemFactKind.EffectUnknown;
        }

        return Bit(row, 2 * field) ? ProblemFactKind.EffectRead : null;
    }

    // Whether a key row and classes have what a decision on the Field A4 numbers f reads: its read or write key, Unknown, Limit or Deep.
    // Where Spread's row has none, the row with P's initializations left out, a subset of it, has none either (Row).
    private static bool Touches(ReadOnlySpan<ulong> row, byte classes, int field)
        => Bit(row, 2 * field) || Bit(row, (2 * field) + 1) || (classes & (MirEffects.Unknown | MirEffects.Limit | MirEffects.Deep)) != 0;

    // The conflicts relation's row of an effect decided so: a read key reads, a write key and Unknown write.
    private static AccessKind EffectRow(ProblemFactKind? fact) => fact == ProblemFactKind.EffectRead ? AccessKind.EffectRead : AccessKind.EffectWrite;

    // The effect rule at a location whose effect access is listed, on that access's check set: nothing unless an effect root has a
    // candidate or the location drops a mutable static; else the reservations it activates stamped (W6, Unique), then per view of the run
    // its keys, the run's views narrowed to that view while it records (Keys selects that case view, so the run's is selected again after).
    private void Effects(MirLocation at, int taking)
    {
        var destroyed = this.DroppedStatic(this.body.Blocks[at.Block].Terminator);
        var candidates = false;
        foreach (var root in this.effectRoots)
        {
            candidates |= this.First(root) != None;
        }

        this.Work += this.effectRoots.Count + this.reserving.Count;
        if (!candidates && destroyed < 0)
        {
            return;
        }

        this.effecting++;
        MirDataflow.Grow(ref this.activated, this.loanCount);
        foreach (var (_, loan, _) in this.reserving)
        {
            this.activated[loan] = this.effecting;
        }

        var all = this.views;
        for (var mask = all; mask != 0; mask &= mask - 1)
        {
            this.views = mask & ~(mask - 1);
            var keys = effects.Keys(this.body, BitOperations.TrailingZeroCount(mask), at.Block, default, out var classes);
            MirDataflow.Grow(ref this.effectRow, keys.Length);
            keys.CopyTo(this.effectRow);
            var row = this.effectRow.AsSpan(0, keys.Length);
            (this.Work, this.provenState) = (this.Work + row.Length, 0);
            foreach (var root in this.effectRoots)
            {
                this.Met(at, taking, root, row, classes);
            }

            if (destroyed >= 0)
            {
                this.Destroying(at, taking, destroyed, row, classes);
            }
        }

        this.views = all;
        cases.Select(this.body, this.view);
    }

    // R1 and Limit on one effect root with candidates in the effect set, on the row with P's initializations left out (Row).
    private void Met(MirLocation at, int taking, int root, ReadOnlySpan<ulong> row, byte classes)
    {
        var field = this.fields[root - this.body.Locals.Length];
        if (this.First(root) == None || !Touches(row, classes, field))
        {
            return;
        }

        row = this.Row(at, taking, field, row, ref classes);
        var decided = Decided(row, classes, field);
        this.Work += 2;
        var scope = this.Source(at).Scope;
        if (decided is not null && this.Conflict(root, [], EffectRow(decided), taking, scope, -1) != None)
        {
            var fact = this.Unproved(field, row, classes);
            var c = fact is { } unproved ? this.Conflict(root, [], EffectRow(unproved), taking, scope, -1) : None;
            if (fact is not { } kind || c == None)
            {
                this.Limit(this.Subject(at), MirOutcome.Limit); // The conflict rests on Q (a key, P42).
            }
            else
            {
                this.Report(
                    this.Code(this.candidates[c], EffectRow(kind), DiagnosticCode.ComparisonLoanConflict_Kd), c, taking, at, -1, this.Effect(kind, root));
            }
        }

        var outcome = MirEffects.Outcome(classes).Join(decided == ProblemFactKind.EffectWrite ? MirOutcome.Decided : MirOutcome.None);
        if (outcome.IsDecided || outcome.IsNone)
        {
            return;
        }

        var read = decided == ProblemFactKind.EffectRead;
        for (var u = this.First(root); u != None; u = this.candidates[u].Next)
        {
            if (this.Conflicts(u, root, [], AccessKind.EffectWrite, taking) && !(read && this.Conflicts(u, root, [], AccessKind.EffectRead, taking)))
            {
                this.Limit(this.Subject(at), outcome); // A Loan a write would meet and no key meets: A4's answer decides.
                return;
            }
        }
    }

    // R3 (SPEC 16.4 with 16.3.1, 16.3.3): the destruction of a mutable static's old value holds an exclusive Loan on it until it completes, so
    // its own effects on that static conflict, a read key, a write key or Unknown; with Limit or Deep alone it is not classified. Inside an
    // inline default copy it is its template's (D11: no Loan takes part). It reads the row with P's initializations left out (Row).
    private void Destroying(MirLocation at, int taking, int destroyed, ReadOnlySpan<ulong> row, byte classes)
    {
        var field = this.fields[destroyed];
        this.Work += 2;
        if (!Touches(row, classes, field))
        {
            return;
        }

        row = this.Row(at, taking, field, row, ref classes);
        if (Decided(row, classes, field) is null)
        {
            this.Limit(this.Subject(at), MirEffects.Outcome(classes)); // No key and no Unknown: A4's Limit or Deep, if any.
        }
        else if (this.Covered(this.Source(at).Scope, -1, -1))
        {
            return; // Its template's.
        }
        else if (this.Unproved(field, row, classes) is { } kind)
        {
            this.Report(DiagnosticCode.ComparisonLoanConflict_Kd, this.Subject(at), None, at, -1, this.Effect(kind, this.Key(~destroyed)));
        }
        else
        {
            this.Limit(this.Subject(at), MirOutcome.Limit); // The conflict rests on Q (a key, P42).
        }
    }

    // What decides a conflict on the Field A4 numbers f in the row a decision reads (Row), Q's initializations set aside (R4-M92's R2-B1):
    // its write key, else Unknown, else its read key, each skipped where Q's initializer may make it (Rests); null where none is left, so the
    // conflict rests on Q. With Q empty it is the row's Decided.
    private ProblemFactKind? Unproved(int field, ReadOnlySpan<ulong> row, byte classes)
    {
        var q = CollectionsMarshal.AsSpan(this.undecided);
        if (Bit(row, (2 * field) + 1) && !this.Rests(q, field, 2, 0))
        {
            return ProblemFactKind.EffectWrite;
        }

        if ((classes & MirEffects.Unknown) != 0 && !this.Rests(q, field, 0, MirEffects.Unknown))
        {
            return ProblemFactKind.EffectUnknown;
        }

        return Bit(row, 2 * field) && !this.Rests(q, field, 1, 0) ? ProblemFactKind.EffectRead : null;
    }

    // The row a decision on the Field A4 numbers f reads at the location in the view (SPEC 15.6.4): the row with P's initializations left
    // out, P and Q formed first (Proof). It is Spread's row where no initializer of P may make, in a view, f's read or write key, Unknown,
    // Limit or Deep (Rests), since a kind no such summary has is reached from the site along a path that enters no initializer of P; else
    // A4's excluded query, once per location and view and copied (A4's scratch is the next query's). Q's initializations stay in it until
    // P42's key.
    private ReadOnlySpan<ulong> Row(MirLocation at, int taking, int field, ReadOnlySpan<ulong> row, scoped ref byte classes)
    {
        this.Proof(at, taking);
        if (!this.Rests(CollectionsMarshal.AsSpan(this.proved), field, 3, MirEffects.Unknown | MirEffects.Limit | MirEffects.Deep))
        {
            return row;
        }

        if (this.provenState == 1)
        {
            var keys = effects.Keys(this.body, BitOperations.TrailingZeroCount(this.views), at.Block, this.proving.AsSpan(0, this.provingWords), out this.provenClasses);
            MirDataflow.Grow(ref this.provenRow, keys.Length);
            keys.CopyTo(this.provenRow);
            (this.Work, this.provenState) = (this.Work + keys.Length, 2);
        }

        classes = this.provenClasses;
        return this.provenRow.AsSpan(0, row.Length);
    }

    // Whether the initializer of a static among `statics` may make, in a view, a key of the Field A4 numbers f that `keys` names (1 its
    // read, 2 its write) or a class among `classes`: its summary has it, an atom as Unknown. An initializer with a validator finding is
    // skipped: it has no node, and what rests on it is A4's Derived (R5-M183), which decides nothing.
    private bool Rests(ReadOnlySpan<int> statics, int field, int keys, byte classes)
    {
        var mask = (ulong)keys << ((2 * field) & 63);
        foreach (var d in statics)
        {
            var initializer = this.program.StaticInit(this.body, d)!;
            for (var view = 0L; !initializer.HasFinding && view < MirCases.Count(initializer); view++)
            {
                var summary = effects.Summary(initializer, view, out var made);
                this.Work++;
                if ((made & classes) != 0 || (summary[(2 * field) >> 6] & mask) != 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // P and Q at the location in the view, formed once (provenState): P, the statics with one slot whose bit the passed fact leaves clear
    // before the block's terminator, with their initialization bits (proving); Q, the statics with several slots whose initialization the
    // view's row may run (their slot bit in Effects' copy) that a candidate of the effect set is on (Holds: Z7's reading, a temporary
    // structure that P42's per-access key removes, R4-M103, since every static then has one slot per key and P decides it).
    private void Proof(MirLocation at, int taking)
    {
        if (this.provenState != 0)
        {
            return;
        }

        (this.provenState, this.provingWords) = (1, 0);
        this.proved.Clear();
        this.undecided.Clear();
        var passed = this.Passed(at.Block);
        for (var bit = 0; bit < this.passCount; bit++)
        {
            if ((passed[bit >> 6] & (1UL << bit)) != 0)
            {
                continue;
            }

            var d = this.passStatics[bit];
            var id = effects.Initialization(this.body.Statics[d].Symbol);
            if ((id >> 6) + 1 > this.provingWords)
            {
                MirDataflow.Grow(ref this.proving, (id >> 6) + 1);
                this.proving.AsSpan(this.provingWords, (id >> 6) + 1 - this.provingWords).Clear();
                this.provingWords = (id >> 6) + 1;
            }

            this.proving[id >> 6] |= 1UL << id;
            this.proved.Add(d);
        }

        for (var d = 0; d < this.body.Statics.Length; d++)
        {
            if (effects.Slot(this.body.Statics[d].Symbol) is >= 0 and var slot && Bit(this.effectRow, slot) && this.Holds(this.Key(~d), taking))
            {
                this.undecided.Add(d);
            }
        }

        this.Work += this.passCount + this.body.Statics.Length;
    }

    // Whether the effect set holds a candidate on a root, in any mode: Q's reading (Proof) until P42's per-access key (R4-M103).
    private bool Holds(int root, int taking)
    {
        for (var c = this.First(root); c != None; c = this.candidates[c].Next)
        {
            if (this.candidates[c].Takes(taking))
            {
                return true;
            }
        }

        return false;
    }

    // The static a Drop of a mutable static destroys, whole or in part, or -1.
    private int DroppedStatic(in MirTerminator terminator)
        => terminator is { Kind: MirTerminatorKind.Drop, Place: { IsStatic: true } place } && this.body.Statics[~place.Root].Mutable ? ~place.Root : -1;

    // An effect fact on a static root: what decided it, at its Field's declaration (its initializer's source).
    private ProblemFact Effect(ProblemFactKind kind, int root) => new(kind, this.program.StaticInit(this.body, root - this.body.Locals.Length)!.Source);
}
