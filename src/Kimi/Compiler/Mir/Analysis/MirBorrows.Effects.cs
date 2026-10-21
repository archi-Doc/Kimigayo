// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Numerics;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

// A5's effect rule (SPEC 15.6.4, 15.6.7, 16.3.3, 16.4 with 16.3.1, 22.4.3; DECISIONS R4-M92): a Call's or a Drop's effects on static
// storage, by A4's key row and classes (MirEffects.Keys), against the live Loans on the view's mutable static roots.
// Input: the location's one gather, its effect access listed by Effecting (a Call's set the last operand's, every operand's value with no
// holder left out; a Drop's its value's), the reservations the location activates, the view's effect roots and A4's Field numbers
// (MirBorrows.Loans).
// Output, per view of the run (each record with the views whose keys give it, R4-M11), through MirBorrows.Reports:
// - R1: for each effect root with a candidate, a write key or Unknown as an EffectWrite, else a read key as an EffectRead of the whole
//   root (shallow, SPEC 15.6.2); its first conflict at the location, coded by Code (row 1: a reservation the call does not activate) with
//   the effect fact on the met Field, what decided it (a write key, else Unknown, else the read key);
// - A4's Limit without Unknown, where a candidate a write would meet is met by no key: a located Unsupported_Kd at the location;
// - at a staticEnsure whose effect set holds a candidate on the ensured static (the proof, a may-set: PD12 (ii) as written), every R1
//   conflict a located Unsupported_Kd at the ensure instead;
// - R3: at a Drop of a mutable static, its own read or write key on that static, or Unknown, ComparisonLoanConflict_Kd anchored at the
//   Drop, no Loan issued and no witness (the destruction's hold, SPEC 16.4); Limit without them, a located Unsupported_Kd.
// Invariants: a Derived class reports nothing by itself (R4-M10); the case view Keys selects is the run's again when the rule returns.
// Must not decide whether an access meets a Loan (MirBorrows.Checks), record other than through Report and Unsupported, or read keys
// outside a Call or a Drop.
internal sealed partial class MirBorrows
{
    // A bit of a key row: 2f a read and 2f + 1 a write of the Field A4 numbers f.
    private static bool Bit(ReadOnlySpan<ulong> row, int bit) => (row[bit >> 6] & (1UL << bit)) != 0;

    // A location's effect access, listed after its own accesses so that the one gather takes its values: at a Call or a Drop of a view with
    // an effect root, or at a Drop of a mutable static (R3), a Call's set its last operand's (TakesStores without one: its result carries
    // no Loan of its own), a Drop's its value's (TakesValue). It is listed as the widest row; the rule gives each root its own.
    private void Effecting(int block, int index)
    {
        ref readonly var data = ref this.body.Blocks[block];
        var terminator = data.Terminator;
        if (index < data.Count || terminator.Kind is not (MirTerminatorKind.Call or MirTerminatorKind.Drop) ||
            (this.effectRoots.Count == 0 && this.DroppedStatic(terminator) < 0))
        {
            return;
        }

        var operands = terminator.Kind == MirTerminatorKind.Drop ? 0 : terminator.Count + (terminator.Callee.Kind == MirCalleeKind.Value ? 1 : 0);
        var taking = terminator.Kind == MirTerminatorKind.Drop ? TakesValue : operands > 0 ? operands - 1 : TakesStores;
        MirDataflow.Grow(ref this.accesses, this.accessCount + 1);
        this.accesses[this.accessCount++] = new(terminator.Place, AccessKind.EffectWrite, false, taking);
    }

    // The effect rule at a location whose effect access is listed, on that access's check set: nothing unless an effect root has a
    // candidate or the location drops a mutable static; else per view of the run its keys, the run's views narrowed to that view while it
    // records (Keys selects that case view, so the run's is selected again after).
    private void Effects(MirLocation at, int taking)
    {
        var terminator = this.body.Blocks[at.Block].Terminator;
        var (destroyed, candidates) = (this.DroppedStatic(terminator), false);
        foreach (var root in this.effectRoots)
        {
            candidates |= this.First(root) != None;
        }

        this.Work += this.effectRoots.Count;
        if (!candidates && destroyed < 0)
        {
            return;
        }

        var proof = false; // A staticEnsure's effect set holds a candidate on the ensured static (SPEC 15.6.4, 22.4.3).
        var ensured = terminator is { Kind: MirTerminatorKind.Call, Callee: { Kind: MirCalleeKind.Intrinsic, Arg: (int)IntrinsicId.StaticEnsure } }
            ? this.Key(~(int)this.body.Consts[this.body.Operands[terminator.Start].Arg].Bits) : -1; // The slot MirProgram.StaticInit decodes.
        for (var c = ensured < 0 ? None : this.First(ensured); c != None && !proof; c = this.candidates[c].Next)
        {
            proof = this.candidates[c].Takes(taking);
        }

        var all = this.views;
        for (var mask = all; mask != 0; mask &= mask - 1)
        {
            this.views = mask & ~(mask - 1);
            var row = effects.Keys(this.body, BitOperations.TrailingZeroCount(mask), at.Block, out var classes);
            this.Work += row.Length;
            foreach (var root in this.effectRoots)
            {
                this.Met(at, taking, root, row, classes, proof);
            }

            if (destroyed >= 0)
            {
                this.Destroying(at, destroyed, row, classes);
            }
        }

        this.views = all;
        cases.Select(this.body, this.view);
    }

    // R1 and Limit on one effect root with candidates in the effect set (R4 at an ensure with the proof).
    private void Met(MirLocation at, int taking, int root, ReadOnlySpan<ulong> row, byte classes, bool proof)
    {
        if (this.First(root) == None)
        {
            return;
        }

        var field = 2 * this.fields[root - this.body.Locals.Length];
        var (read, write, unknown) = (Bit(row, field), Bit(row, field + 1), (classes & MirEffects.Unknown) != 0);
        var kind = write || unknown ? AccessKind.EffectWrite : AccessKind.EffectRead;
        this.Work += 2;
        if ((write || unknown || read) && this.Conflict(root, [], kind, taking) is var c and not None)
        {
            if (proof)
            {
                this.Unsupported(at); // The proof would decide (PD12 (ii)): the effect set is a may-set (e10f).
            }
            else
            {
                var fact = this.Effect(write ? MirFactKind.EffectWrite : unknown ? MirFactKind.EffectUnknown : MirFactKind.EffectRead, root);
                this.Report(this.Code(this.candidates[c], kind, DiagnosticCode.ComparisonLoanConflict_Kd), c, taking, at, -1, fact);
            }
        }

        if ((classes & MirEffects.Limit) == 0 || unknown || write)
        {
            return;
        }

        for (var u = this.First(root); u != None; u = this.candidates[u].Next)
        {
            if (this.Conflicts(u, root, [], AccessKind.EffectWrite, taking) && !(read && this.Conflicts(u, root, [], AccessKind.EffectRead, taking)))
            {
                this.Unsupported(at); // A Loan a write would meet and no key meets: A4's Limit decides (AGENTS Diagnostics).
                return;
            }
        }
    }

    // R3 (SPEC 16.4 with 16.3.1, 16.3.3): the destruction of a mutable static's old value holds an exclusive Loan on it until it completes, so
    // its own effects on that static conflict, a read key, a write key or Unknown; with Limit alone it is not classified.
    private void Destroying(MirLocation at, int destroyed, ReadOnlySpan<ulong> row, byte classes)
    {
        var field = 2 * this.fields[destroyed];
        var (read, write, unknown) = (Bit(row, field), Bit(row, field + 1), (classes & MirEffects.Unknown) != 0);
        this.Work += 2;
        if (write || unknown || read)
        {
            var root = this.Key(~destroyed);
            var fact = this.Effect(write ? MirFactKind.EffectWrite : unknown ? MirFactKind.EffectUnknown : MirFactKind.EffectRead, root);
            this.Report(DiagnosticCode.ComparisonLoanConflict_Kd, this.Subject(at), None, at, -1, fact);
        }
        else if ((classes & MirEffects.Limit) != 0)
        {
            this.Unsupported(at);
        }
    }

    // The static a Drop of a mutable static destroys, whole or in part, or -1.
    private int DroppedStatic(in MirTerminator terminator)
        => terminator is { Kind: MirTerminatorKind.Drop, Place: { IsStatic: true } place } && this.body.Statics[~place.Root].Mutable ? ~place.Root : -1;

    // An effect fact on a static root: what decided it, at its Field's declaration (its StaticInit body's source; every static has one).
    private MirFact Effect(MirFactKind kind, int root)
        => new(kind, this.program[new(this.body.Statics[root - this.body.Locals.Length].Symbol, MirBodyKind.StaticInit)]!.Source);
}
