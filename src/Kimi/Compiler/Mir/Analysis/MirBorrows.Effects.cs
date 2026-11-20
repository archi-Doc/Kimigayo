// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Numerics;

namespace Kimi.Compiler.Mir;

// A5's effect rule (SPEC 15.6.4, 15.6.7, 16.3.3, 16.4 with 16.3.1, 22.4.3; DECISIONS R4-M92, Z7): a Call's or a Drop's effects on static
// storage, by A4's key row and classes (MirEffects.Keys), against the live Loans on the view's mutable static roots.
// Input: the location's one gather, its effect access listed by MirBorrows.Accesses (a Call's set the last operand's, every operand's
// value with no holder left out; a Drop's its value's), the reservations the location activates, the view's effect roots and A4's Field
// and initialization numbers (MirBorrows.Loans, MirEffects.Initialization).
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
// - the initialization proof (SPEC 15.6.4: a live Loan anchored to a Field proves that Field initialized; R4-M89 OQ-F5-6 (a)): Q, the
//   statics a candidate of the effect set is on, is what the literal reading proves and the clarified readings may not (liveness is a
//   may-property: e10f), so no conflict rests on it. A conflict of the row (every initialization composed) is decided by the row with
//   Q's initializations left out (MirEffects.Keys' excluded): a conflict there is the Language problem, its fact that row's (the first
//   effect outside the proof decides, R4-M92's R2-B1), unless a static of Q with several slots whose initialization the site may run has
//   that effect in its initializer's summary (an edge A4 cannot leave out until P42's key, R4-M103); that or none there is the located
//   Unsupported_Kd. An initializer with a validator finding is A4's Derived (R5-M183): its keys never decide.
// Invariants: a Derived class reports nothing by itself (R4-M10); the case view Keys selects is the run's again when the rule returns; a
// row is read from A5's copy, never from A4's scratch after another query.
// Must not decide whether an access meets a Loan (MirBorrows.Checks), record other than through Report and Limit, rank outcomes, or read keys
// outside a Call or a Drop. A static a Loan or a Drop roots has its initializer: the validator checks that its body ensures it and that
// each staticEnsure's static has one (G191).
internal sealed partial class MirBorrows
{
    private readonly List<int> undecided = []; // Q's statics with several slots whose initialization the view's row may run (Unproven).
    private int[] activated = []; // By Loan: the effect rule's run whose location activates it (W6).
    private int effecting; // The effect rule's runs, the stamp of activated, never reset.
    private ulong[] effectRow = []; // The effect rule's copy of A4's key row of the view it records,
    private ulong[] unprovenRow = []; // the row with Q's initializations left out (Unproven), its classes,
    private byte unprovenClasses;
    private ulong[] proving = []; // and Q's initialization bits, the excluded row of its query.
    private byte unprovenState; // Unproven's answer in the view: 0 not queried, 1 no Q, 2 the row.

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
            (this.Work, this.unprovenState) = (this.Work + row.Length, 0);
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

    // R1 and Limit on one effect root with candidates in the effect set.
    private void Met(MirLocation at, int taking, int root, ReadOnlySpan<ulong> row, byte classes)
    {
        if (this.First(root) == None)
        {
            return;
        }

        var field = this.fields[root - this.body.Locals.Length];
        var decided = Decided(row, classes, field);
        this.Work += 2;
        var scope = this.Source(at).Scope;
        if (decided is not null && this.Conflict(root, [], EffectRow(decided), taking, scope, -1) != None)
        {
            var fact = this.Unproved(at, taking, field, decided.Value);
            var c = fact is { } unproved ? this.Conflict(root, [], EffectRow(unproved), taking, scope, -1) : None;
            if (fact is not { } kind || c == None)
            {
                this.Limit(this.Subject(at), MirOutcome.Limit); // The conflict rests on Q (the proof's readings, or a key).
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
    // inline default copy it is its template's (D11: no Loan takes part).
    private void Destroying(MirLocation at, int taking, int destroyed, ReadOnlySpan<ulong> row, byte classes)
    {
        var field = this.fields[destroyed];
        this.Work += 2;
        if (Decided(row, classes, field) is not { } decided)
        {
            this.Limit(this.Subject(at), MirEffects.Outcome(classes)); // No key and no Unknown: A4's Limit or Deep, if any.
        }
        else if (this.Covered(this.Source(at).Scope, -1, -1))
        {
            return; // Its template's.
        }
        else if (this.Unproved(at, taking, field, decided) is { } kind)
        {
            this.Report(DiagnosticCode.ComparisonLoanConflict_Kd, this.Subject(at), None, at, -1, this.Effect(kind, this.Key(~destroyed)));
        }
        else
        {
            this.Limit(this.Subject(at), MirOutcome.Limit); // The conflict rests on Q (the proof's readings, or a key).
        }
    }

    // What decides a conflict of the full row on the Field A4 numbers f once Q's initializations are left out: the full row's effect where
    // Q is empty, that row's where Q leaves one (the first effect outside the proof, R4-M92's R2-B1) and no undecided initialization may
    // make it (Rests), null where the conflict rests on Q.
    private ProblemFactKind? Unproved(MirLocation at, int taking, int field, ProblemFactKind decided) => this.Unproven(at, taking) switch
    {
        1 => decided,
        _ => Decided(this.unprovenRow, this.unprovenClasses, field) is { } fact && !this.Rests(fact, field) ? fact : null,
    };

    // Whether an effect that decides a conflict on the Field A4 numbers f may lie in the initialization of a static of Q with several slots
    // that the site may run (undecided): its initializer's summary has the write or read key, or Unknown for an unknown effect (its classes
    // or an atom), which no row leaves out until P42's key (R4-M103).
    private bool Rests(ProblemFactKind fact, int field)
    {
        var bit = fact == ProblemFactKind.EffectWrite ? (2 * field) + 1 : 2 * field;
        foreach (var d in this.undecided)
        {
            var initializer = this.program.StaticInit(this.body, d)!;
            for (var view = 0L; view < MirCases.Count(initializer); view++)
            {
                var summary = effects.Summary(initializer, view, out var classes);
                this.Work++;
                if (fact == ProblemFactKind.EffectUnknown ? (classes & MirEffects.Unknown) != 0 : Bit(summary, bit))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Q's row in the view, queried once where a conflict needs it (unprovenState): 1 where the effect set holds no candidate on a static,
    // 2 with the row of the location whose initializations of the statics with a candidate are left out (unprovenRow, unprovenClasses),
    // and those with several slots whose initialization the view's row (Effects' copy) may run listed (undecided).
    private byte Unproven(MirLocation at, int taking)
    {
        if (this.unprovenState != 0)
        {
            return this.unprovenState;
        }

        var (words, any) = (0, false);
        this.undecided.Clear();
        this.Work += this.body.Statics.Length;
        for (var d = 0; d < this.body.Statics.Length; d++)
        {
            if (!this.Holds(this.Key(~d), taking))
            {
                continue;
            }

            any = true;
            var symbol = this.body.Statics[d].Symbol;
            if (effects.Initialization(symbol) is >= 0 and var id)
            {
                if ((id >> 6) + 1 > words)
                {
                    MirDataflow.Grow(ref this.proving, (id >> 6) + 1);
                    this.proving.AsSpan(words, (id >> 6) + 1 - words).Clear();
                    words = (id >> 6) + 1;
                }

                this.proving[id >> 6] |= 1UL << id;
            }
            else if (effects.Slot(symbol) is >= 0 and var slot && Bit(this.effectRow, slot))
            {
                this.undecided.Add(d);
            }
        }

        if (!any)
        {
            return this.unprovenState = 1;
        }

        var keys = effects.Keys(this.body, BitOperations.TrailingZeroCount(this.views), at.Block, this.proving.AsSpan(0, words), out this.unprovenClasses);
        MirDataflow.Grow(ref this.unprovenRow, keys.Length);
        keys.CopyTo(this.unprovenRow);
        this.Work += keys.Length;
        return this.unprovenState = 2;
    }

    // Whether the effect set holds a candidate on a root, in any mode.
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
