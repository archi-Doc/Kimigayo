// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's conflicts relation (A3 5's hits and conflicts, with E1, E3, E11, R4-M68 and R4-M92): what an access does (AccessKind), a Call's or
// Drop's effect rows included; the first Loan of its check set on the access's root it conflicts with, by mode (a reservation the call
// activates is unique at its effect rows, W6) and syntactically by Place prefix, with the route rule for a Loan in its reservation holder,
// never a raw Place borrow's Loan, its fresh anchor's (SPEC 5.2.2, 15.6.2; DECISIONS R4-M156), a Loan its template owns skipped
// (MirBorrows.Defaults' Covered, D11, R4-M97); and err(ended) (E7): at a guard candidate's EndBorrow, a value live after it that keeps its
// guard-local Origin, but where the EndBorrow and the candidate lie in one inline default copy (D12). A candidate or marker only the drop
// tier found (MirBorrows.CheckSets) is the answer only where no real one is, as the depth limit (R4-M153).
// Must not list or classify a location's accesses (MirBorrows.Accesses) or record a problem (MirBorrows.Reports).
internal sealed partial class MirBorrows
{
    /// <summary>What an access does (A3 5's access): read, borrow shared or exclusive, Move, write, destroy, or destroy observing what the
    /// Place holds (exclusive and deep, A3 5's obs; E3 leaves it no shared cut); a reservation's activation, deep as an exclusive borrow
    /// or, for a destroying one, shallow as a write (R4-M68), and that call's observing destruction of the old value past what the write
    /// hits (err(destroy), E11); a destruction past the destruction walk's bound is not classified (OwnershipDepthLimit_Kd, as A4's Deep); a Call's
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
    private static bool Meets(in MirStep left, in MirStep right)
    {
        if (left.Kind == MirProjectionKind.Index || right.Kind == MirProjectionKind.Index)
        {
            return left.Kind is MirProjectionKind.Index or MirProjectionKind.ConstIndex &&
                right.Kind is MirProjectionKind.Index or MirProjectionKind.ConstIndex;
        }

        return left.Kind == right.Kind && (left.Kind == MirProjectionKind.Deref || left.Arg == right.Arg);
    }

    // The first Loan on an access's root in its check set (Takes) it conflicts with (Conflicts), None for none: one whose failure its template
    // owns, by the operation's scope and reservation (-1: none), is skipped and the scan goes on (D11), so it hides no later one. In a run
    // with rule W's elements the scan goes on past the first: the access is derived exactly when every conflicting candidate is
    // (MirBorrows.Causes' Derived, R4-M96), its causes then left for its record, else none. A candidate only the drop tier takes is passed
    // over, so it hides no real one, and is the answer only where no real one conflicts (Report records it as the depth limit, R4-M153).
    private int Conflict(int root, ReadOnlySpan<MirStep> steps, AccessKind kind, int taking, int scope, int reservation)
    {
        var (first, dropped) = (None, None);
        this.causes.Clear();
        for (var c = this.First(root); c != None; c = this.candidates[c].Next)
        {
            if (this.Conflicts(c, root, steps, kind, taking) && !this.Covered(scope, this.candidates[c].Loan, reservation))
            {
                if (!this.candidates[c].Tags.Takes(taking))
                {
                    dropped = dropped == None ? c : dropped;
                    continue;
                }

                first = first == None ? c : first;
                if (this.wCount == 0 || !this.Derived(c))
                {
                    this.causes.Clear();
                    return first;
                }
            }
        }

        return first == None ? dropped : first;
    }

    // Whether a candidate of an access's check set conflicts with it (A3 5's conflicts and hits): a write, Move, exclusive borrow,
    // destruction or write key with any mode (err(destroy) too), a read, shared borrow or read key with a unique one, an activation with
    // one shared in the holder found (R4-M68: an exclusive Loan that hits the reservation was met where the later Borrow was formed or
    // activated, SPEC 15.6.7).
    private bool Conflicts(int c, int root, ReadOnlySpan<MirStep> steps, AccessKind kind, int taking)
    {
        ref readonly var candidate = ref this.candidates[c];
        this.Work++;
        return candidate.Takes(taking) && (kind is AccessKind.Activate or AccessKind.ActivateInPlace ? !candidate.Unique
            : this.Unique(candidate, kind) || kind is not (AccessKind.Read or AccessKind.SharedBorrow or AccessKind.EffectRead)) &&
            this.Hits(root, steps, kind, candidate);
    }

    // hits(A, X, k) (A3 5, E1, E3): never where X is a raw Place borrow's Loan, its fresh anchor's, compared with no Place not derived from
    // it (SPEC 5.2.2, 15.6.2; DECISIONS R4-M156), whatever k; else within(A, X), or within(X, A) where the access is deep, or no borrow Deref
    // of X lies below A, or it writes, destroys or activates in place a prefix of a Loan that sits in its reservation holder across borrow
    // Derefs (E1, SPEC 15.6.7); err(destroy) hits what a deep access does and the write does not (E11).
    private bool Hits(int root, ReadOnlySpan<MirStep> steps, AccessKind kind, in Candidate candidate)
    {
        ref readonly var loan = ref this.loans[candidate.Loan];
        if (root != loan.Root || loan.Anchored)
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
            if (!route && this.loanSteps[loan.Start + i].Class is MirStepClass.Shared or MirStepClass.Uniq)
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
    // effect rows of the call that activates it (stamped by MirBorrows.Effects), where every reservation is live at once with the call's
    // effects; the call's other accesses see a reservation shared in its holder.
    private bool Unique(in Candidate candidate, AccessKind kind)
        => candidate.Unique || (kind is AccessKind.EffectRead or AccessKind.EffectWrite && this.activated[candidate.Loan] == this.effecting);

    // err(ended) (A3 E7, SPEC 14.8.3): at a guard candidate's EndBorrow its guard-local Origin ends, so a value live after it that carries its
    // γ (CS(ℓ), transit included) is a borrow that escapes the guard: ComparisonLoanConflict_Kd at the cleanup (DIAGNOSTICS, escaped
    // borrows), anchored at the candidate; a copy's template reports it where the EndBorrow and the candidate lie in one copy (D12). A γ only
    // the drop tier saw is the depth limit at its holder's declaration (R4-M153).
    private void Ended(MirLocation at, int candidate)
    {
        var mark = Markers + candidate;
        var seen = this.markSeen[mark] == this.stamp;
        if ((seen || (this.drops.Count > 0 && this.markDropped[mark].Stamp == this.stamp)) &&
            !this.Covered(this.body.Common(this.Source(at).Scope, this.body.Locals[candidate].Source.Scope), -1, -1))
        {
            if (!seen)
            {
                this.Limit(this.Declaration(this.markDropped[mark].Via), MirOutcome.Deep);
                return;
            }

            var anchor = this.body.Subject(this.body.Locals[candidate].Source);
            this.EndedCauses(candidate, mark);
            this.Report(DiagnosticCode.ComparisonLoanConflict_Kd, anchor, this.markVia[mark], at, -1, null);
        }
    }
}
