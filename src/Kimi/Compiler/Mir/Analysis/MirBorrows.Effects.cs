// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Numerics;

namespace Kimi.Compiler.Mir;

// A5's effect rule (SPEC 15.6.4, 15.6.7, 16.3.3, 16.4 with 16.3.1, 22.4.3; DECISIONS R4-M92): a Call's or a Drop's effects on static
// storage, by A4's key row and classes (MirEffects.Keys), against the live Loans on the view's mutable static roots.
// Input: the location's one gather, its effect access listed by MirBorrows.Accesses (a Call's set the last operand's, every operand's
// value with no holder left out; a Drop's its value's), the reservations the location activates, the view's effect roots and A4's Field
// numbers (MirBorrows.Loans), and A4's summaries of the static initializers (MirEffects.Summary).
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
// - the initialization proof: a conflict every effect that would decide it may be the first-access initialization of a static a live
//   Loan proves initialized (Proved) is the located Unsupported_Kd at the location instead (PD12 (ii): liveness is a may-property);
//   otherwise the first effect outside the proof decides the Language problem (Unproved).
// Invariants: a Derived class reports nothing by itself (R4-M10); the case view Keys selects is the run's again when the rule returns.
// Must not decide whether an access meets a Loan (MirBorrows.Checks), record other than through Report and Limit, rank outcomes, or read keys
// outside a Call or a Drop. A static a Loan or a Drop roots has its initializer: the validator checks that its body ensures it and that
// each staticEnsure's static has one (G191).
internal sealed partial class MirBorrows
{
    private int[] activated = []; // By Loan: the effect rule's run whose location activates it (W6).
    private int effecting; // The effect rule's runs, the stamp of activated, never reset.

    // A bit of a key row: 2f a read and 2f + 1 a write of the Field A4 numbers f.
    private static bool Bit(ReadOnlySpan<ulong> row, int bit) => (row[bit >> 6] & (1UL << bit)) != 0;

    // What decides an effect on the Field A4 numbers f in a key row and classes: a write key, else Unknown, else a read key; null for none.
    private static MirFactKind? Decided(ReadOnlySpan<ulong> row, byte classes, int field)
    {
        if (Bit(row, (2 * field) + 1))
        {
            return MirFactKind.EffectWrite;
        }

        if ((classes & MirEffects.Unknown) != 0)
        {
            return MirFactKind.EffectUnknown;
        }

        return Bit(row, 2 * field) ? MirFactKind.EffectRead : null;
    }

    // The conflicts relation's row of an effect decided so: a read key reads, a write key and Unknown write.
    private static AccessKind EffectRow(MirFactKind? fact) => fact == MirFactKind.EffectRead ? AccessKind.EffectRead : AccessKind.EffectWrite;

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
            var row = effects.Keys(this.body, BitOperations.TrailingZeroCount(mask), at.Block, out var classes);
            this.Work += row.Length;
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
            var fact = this.Unproved(at, taking, row, classes, field);
            var c = fact is { } unproved ? this.Conflict(root, [], EffectRow(unproved), taking, scope, -1) : None;
            if (fact is not { } kind || c == None)
            {
                this.Limit(this.Subject(at), MirOutcome.Limit); // Every effect that would decide it lies in a proved initializer.
            }
            else
            {
                this.Report(
                    this.Code(this.candidates[c], EffectRow(kind), DiagnosticCode.ComparisonLoanConflict_Kd), c, taking, at, -1, this.Effect(kind, root));
            }
        }

        var outcome = MirEffects.Outcome(classes).Join(decided == MirFactKind.EffectWrite ? MirOutcome.Decided : MirOutcome.None);
        if (outcome.IsDecided || outcome.IsNone)
        {
            return;
        }

        var read = decided == MirFactKind.EffectRead;
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
        if (Decided(row, classes, field) is null)
        {
            this.Limit(this.Subject(at), MirEffects.Outcome(classes)); // No key and no Unknown: A4's Limit or Deep, if any.
        }
        else if (this.Covered(this.Source(at).Scope, -1, -1))
        {
            return; // Its template's.
        }
        else if (this.Unproved(at, taking, row, classes, field) is not { } fact)
        {
            this.Limit(this.Subject(at), MirOutcome.Limit); // Every effect that would decide it lies in a proved initializer.
        }
        else
        {
            this.Report(DiagnosticCode.ComparisonLoanConflict_Kd, this.Subject(at), None, at, -1, this.Effect(fact, this.Key(~destroyed)));
        }
    }

    // The initialization proof (SPEC 15.6.4: a live Loan anchored to a Field proves that Field initialized, so its first access runs no
    // initializer; 22.4.3): whether an effect that would decide a conflict on a Field, a write or read key or Unknown, lies in the summary
    // of the initializer of a static d with a candidate in the effect set (MirEffects.Summary), where the location may first-access d: d is
    // immutable (its accesses carry no key), the location's row has a key of d, or the location is d's staticEnsure
    // (MirProgram.StaticInit), which carries no key of its own static (R4-M38). Liveness is a may-property, so the Loan proves nothing on a
    // path it is absent from (e10f): a conflict every effect that would decide it is so proved is the located Unsupported_Kd, never a
    // Language problem (PD12 (ii) as written); a key both a proved initializer and the site itself may produce counts as proved, since keys
    // do not record their source (P5).
    private bool Proved(MirLocation at, int taking, ReadOnlySpan<ulong> row, MirFactKind fact, int field)
    {
        var terminator = this.body.Blocks[at.Block].Terminator;
        MirBody? ensured = null;
        if (terminator is { Kind: MirTerminatorKind.Call, Callee: { Kind: MirCalleeKind.Intrinsic, Arg: (int)IntrinsicId.StaticEnsure } })
        {
            ensured = this.program.StaticInit(this.body, this.body.Operands[terminator.Start]);
        }

        var bit = fact == MirFactKind.EffectWrite ? (2 * field) + 1 : 2 * field;
        this.Work += this.body.Statics.Length;
        for (var d = 0; d < this.body.Statics.Length; d++)
        {
            if (!this.Holds(this.Key(~d), taking))
            {
                continue;
            }

            var initializer = this.program.StaticInit(this.body, d)!;
            var own = 2 * this.fields[d];
            if (this.body.Statics[d].Mutable && !Bit(row, own) && !Bit(row, own + 1) && !ReferenceEquals(initializer, ensured))
            {
                continue; // The location does not first-access d.
            }

            for (var view = 0L; view < MirCases.Count(initializer); view++)
            {
                var summary = effects.Summary(initializer, view, out var classes);
                if (fact == MirFactKind.EffectUnknown ? (classes & MirEffects.Unknown) != 0 : Bit(summary, bit))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // What decides an effect on the Field A4 numbers f once the initialization proof applies: the first of a write key, Unknown and a read
    // key that no initializer the proof covers may produce (Proved); null where none is left.
    private MirFactKind? Unproved(MirLocation at, int taking, ReadOnlySpan<ulong> row, byte classes, int field)
    {
        if (Bit(row, (2 * field) + 1) && !this.Proved(at, taking, row, MirFactKind.EffectWrite, field))
        {
            return MirFactKind.EffectWrite;
        }

        if ((classes & MirEffects.Unknown) != 0 && !this.Proved(at, taking, row, MirFactKind.EffectUnknown, field))
        {
            return MirFactKind.EffectUnknown;
        }

        if (Bit(row, 2 * field) && !this.Proved(at, taking, row, MirFactKind.EffectRead, field))
        {
            return MirFactKind.EffectRead;
        }

        return null;
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
    private MirFact Effect(MirFactKind kind, int root) => new(kind, this.program.StaticInit(this.body, root - this.body.Locals.Length)!.Source);
}
