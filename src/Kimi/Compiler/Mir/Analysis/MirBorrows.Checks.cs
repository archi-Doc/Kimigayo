// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's conflicts relation (A3 5's hits and conflicts, with E1, E3, E11, R4-M68 and R4-M92): what an access does (AccessKind), a Call's or
// Drop's effect rows included; the Loan of its check set on the access's root it conflicts with, by mode (a reservation the call
// activates is unique at its effect rows, W6), by the order of clean and default-tainted Loans (R4-M70) and syntactically by Place
// prefix, with the route rule for a Loan in its reservation holder; and err(ended) (E7): at a guard candidate's EndBorrow, a value live
// after it that keeps its guard-local Origin.
// Must not list or classify a location's accesses (MirBorrows.Locations) or record a problem (MirBorrows.Reports).
internal sealed partial class MirBorrows
{
    /// <summary>What an access does (A3 5's access): read, borrow shared or exclusive, Move, write, destroy, or destroy observing what the
    /// Place holds (exclusive and deep, A3 5's obs; E3 leaves it no shared cut); a reservation's activation, deep as an exclusive borrow
    /// or, for a destroying one, shallow as a write (R4-M68), and that call's observing destruction of the old value past what the write
    /// hits (err(destroy), E11); a destruction past the destruction walk's bound is not classified (Unsupported_Kd, as A4's Limit); a Call's
    /// or Drop's effect on a mutable static root by A4's keys, a read or a write of the whole root, shallow (SPEC 15.6.4, 15.6.2;
    /// MirBorrows.Effects).</summary>
    private enum AccessKind : byte
    {
        Read,
        SharedBorrow,
        UniqueBorrow,
        Move,
        Write,
        Destroy,
        Observe,
        Activate,
        ActivateInPlace,
        ObservePast, // err(destroy): the Loans an observing destruction hits and the destroying activation's write does not, in every mode.
        Unclassified,
        EffectRead, // A read key: it meets a unique Loan, or one the call activates (W6).
        EffectWrite, // A write key or an unknown effect: it meets a Loan in any mode.
    }

    // Two steps after equal prefixes may select the same storage (A3 3): a runtime index meets every index; distinct Fields, constant
    // indices, bases and Downcasts are disjoint.
    private static bool Meets(in Step left, in Step right)
    {
        if (left.Kind == MirProjectionKind.Index || right.Kind == MirProjectionKind.Index)
        {
            return left.Kind is MirProjectionKind.Index or MirProjectionKind.ConstIndex &&
                right.Kind is MirProjectionKind.Index or MirProjectionKind.ConstIndex;
        }

        return left.Kind == right.Kind && (left.Kind == MirProjectionKind.Deref || left.Arg == right.Arg);
    }

    // The Loan on an access's root in its check set (Takes) it conflicts with (Conflicts), None for none. The first that sets without a
    // default taint hold is the access's Language problem; only where none is, the first other (R4-M70).
    private int Conflict(int root, ReadOnlySpan<Step> steps, AccessKind kind, int taking)
    {
        var tainted = None;
        for (var c = this.First(root); c != None; c = this.candidates[c].Next)
        {
            if (this.Conflicts(c, root, steps, kind, taking))
            {
                if (this.candidates[c].Clean.Takes(taking))
                {
                    return c;
                }

                tainted = tainted == None ? c : tainted;
            }
        }

        return tainted;
    }

    // Whether a candidate of an access's check set conflicts with it (A3 5's conflicts and hits): a write, Move, exclusive borrow,
    // destruction or write key with any mode (err(destroy) too), a read, shared borrow or read key with a unique one, an activation with
    // one shared in the holder found (R4-M68: an exclusive Loan that hits the reservation was met where the later Borrow was formed or
    // activated, SPEC 15.6.7).
    private bool Conflicts(int c, int root, ReadOnlySpan<Step> steps, AccessKind kind, int taking)
    {
        ref readonly var candidate = ref this.candidates[c];
        this.Work++;
        return candidate.Takes(taking) && (kind is AccessKind.Activate or AccessKind.ActivateInPlace ? !candidate.Unique
            : this.Unique(candidate, kind) || kind is not (AccessKind.Read or AccessKind.SharedBorrow or AccessKind.EffectRead)) &&
            this.Hits(root, steps, kind, candidate);
    }

    // hits(A, X, k) (A3 5, E1, E3): within(A, X), or within(X, A) where the access is deep, or no borrow or raw Deref of X lies below A, or
    // it writes, destroys or activates in place a prefix of a Loan that sits in its reservation holder across borrow Derefs (E1, SPEC
    // 15.6.7); err(destroy) hits what a deep access does and the write does not (E11).
    private bool Hits(int root, ReadOnlySpan<Step> steps, AccessKind kind, in Candidate candidate)
    {
        ref readonly var loan = ref this.loans[candidate.Loan];
        if (root != loan.Root)
        {
            return false;
        }

        for (var i = 0; i < Math.Min(steps.Length, loan.Count); i++)
        {
            if (!Meets(steps[i], this.loanSteps[loan.Start + i]))
            {
                return false;
            }
        }

        var past = kind == AccessKind.ObservePast; // The write's hits, negated.
        if (steps.Length >= loan.Count || kind is AccessKind.Move or AccessKind.SharedBorrow or AccessKind.UniqueBorrow or AccessKind.Activate or
            AccessKind.Observe)
        {
            return !past;
        }

        var route = kind is AccessKind.Write or AccessKind.Destroy or AccessKind.ActivateInPlace or AccessKind.ObservePast && this.Reserved(candidate, kind);
        for (var i = steps.Length; i < loan.Count; i++)
        {
            if (this.loanSteps[loan.Start + i].Class is var step && (step == StepClass.Raw || (!route && step is StepClass.Shared or StepClass.Uniq)))
            {
                return past;
            }
        }

        return !past;
    }

    // Whether a Loan of the check set sits in its reservation holder, where its two-phase mode is shared, as the access sees it (Unique).
    private bool Reserved(in Candidate candidate, AccessKind kind)
        => this.loans[candidate.Loan].Mode == MirRefKind.TwoPhaseUniq && !this.Unique(candidate, kind);

    // A candidate's mode as an access sees it (W6, SPEC 15.6.7, A3 E13 (1)): its mode in the holder it was found in, but unique at the
    // effect rows of the call that activates it, where every reservation is live at once with the call's effects; the call's other
    // accesses see a reservation shared in its holder.
    private bool Unique(in Candidate candidate, AccessKind kind)
    {
        if (candidate.Unique || kind is not (AccessKind.EffectRead or AccessKind.EffectWrite))
        {
            return candidate.Unique;
        }

        foreach (var (_, loan, _) in this.reserving)
        {
            if (loan == candidate.Loan)
            {
                return true;
            }
        }

        return false;
    }

    // err(ended) (A3 E7, SPEC 14.8.3): at a guard candidate's EndBorrow its guard-local Origin ends, so a value live after it that carries its
    // γ (CS(ℓ), transit included) is a borrow that escapes the guard: ComparisonLoanConflict_Kd at the cleanup (DIAGNOSTICS, escaped
    // borrows), anchored at the candidate.
    private void Ended(MirLocation at, int candidate)
    {
        var mark = Markers + candidate;
        if (this.markSeen[mark] == this.stamp)
        {
            var anchor = this.body.Subject(this.body.Locals[candidate].Source);
            this.Report(DiagnosticCode.ComparisonLoanConflict_Kd, anchor, this.markVia[mark], at, -1, null);
        }
    }
}
