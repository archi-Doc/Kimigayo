// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The proof frame's failure channel (DECISIONS BU-22, N3-1; SPEC 8.3.3, 23.1.1, 23.1.2), a part of constraint validation.
// - Responsibility: the proof scopes (BeginProof, EndProof) and the causes they carry: the first limit (L) and Error cause a scope read,
//   kept by the scope whose answer rests on them (ProofCause, ReadVerdict); the one failed cause a settled proof leaves for the check
//   that failed on it (failedCause, TakeFailedCause); the reset of each pass (ClearProofScopes).
// - Inputs: a proof's answers and the causes its producers met (ICheckCause: a conformance on its specification or limited by OCC-X, a
//   component summary's site or stored Type); the capability engine's consumer, under which a scope stays combined.
// - Outputs: each scope's answer combined as P < U < L < R < E; where an answer is no longer combined, an L as Error resting on its site
//   and an Error resting on its cause, left in the channel; the Use a failing check takes.
// - Invariants: scopes nest (proofNesting) and a closing scope restores its enclosing scope's causes; a check clears the channel before
//   its proof and takes the cause after it; each pass starts with the channel clear. A fact the frame carries is stamped with the
//   outermost clause it was proven for, a consumer takes only a fact with its own frame's stamp, and an untaken fact is dropped at the
//   outermost EndProof (design-r2 Amendment 4; the U fact joins in G216 (a) U2.1 f1-1). Frames (design-r2 Amendment 5): a proof step
//   nested in a frame (BeginProof at depth > 0) opens no frame and pushes no epoch, and its facts carry the frame's epoch outward to the
//   frame's consumer; a frame is either the outermost BeginProof of a judgment or a candidate trial, even when the trial starts inside
//   an open frame, takes a fresh epoch from one monotonic counter, and its end pops its epoch and drops its untaken facts. A fact
//   carries the epoch of the innermost open frame; a consumer compares it with the innermost open frame at its read: an absent fact is
//   normal, a present foreign fact InternalInvariant_Kd.
// - Prohibitions: no decision reads a carried fact: no branch, no memo key and no fixpoint equality depends on it; the channel decides
//   no proof and publishes no record.
public sealed partial class Binding
{
    // The first cause a check's Error rests on (ICheckCause): a conformance resting on its specification, or one limited by OCC-X outside
    // conformance verification (ResolveConformance), or what a settled proof rested on (EndProof). A consumer clears it before its proof
    // and takes it after (TakeFailedCause); each pass starts with it clear.
    private ICheckCause? failedCause;

    // The proof in progress (DECISIONS BU-22, N3-1): the first limit (L, an Unknown resting on a located Unsupported_Kd) and the first
    // Error cause it read, of which a proof scope keeps the one its own answer rests on (BeginProof, EndProof).
    private ICheckCause? limitRead;
    private ICheckCause? errorRead;
    private int proofNesting;

    // The U fact (design-r2 Amendment 4): the first Callable comparison failure the Unknown answer of the proof scope in progress rests on
    // (unknownRead), and the one a settled Unknown leaves for its check (failedUnknown), each diagnostic payload only.
    private CallableUnknown? unknownRead;
    private CallableUnknown? failedUnknown;

    // The candidate trial's own U fact (SlotSolution.Prove), for the selection's explanation of a Pending candidate (CallableOriginFailure).
    private CallableUnknown? trialUnknown;

    // The clause fact (design-r2 G351a, R4-M155): the clause a clause list's combined verdict rests on, handed one step outward
    // (clauseRead, the scope's), left by a settled clause list for its check (failedClause) and kept by a candidate trial (trialClause),
    // each diagnostic payload only; no reader yet.
    private ClauseFact? clauseRead;
    private ClauseFact? failedClause;
    private ClauseFact? trialClause;

    // The innermost open frame's epoch (0: none) and the monotonic counter that issues them (design-r2 Amendment 5).
    private int frameEpoch;
    private int epochCounter;

    // The first clause at the highest rank of a clause list, or of the lists a scope holds (G351a).
    private static ClauseFact? FirstAtVerdict(ClauseFact? best, ClauseFact clause) => clause.Rank > (best?.Rank ?? 0) ? clause : best;

    // Opens a proof scope: a connective's operand, a clause list, a disjunction of conformance paths, a capability request.
    private (ICheckCause? Limit, ICheckCause? Error, CallableUnknown? Unknown, ClauseFact? Clause, int Frame) BeginProof()
    {
        var outer = (this.limitRead, this.errorRead, this.unknownRead, this.clauseRead, this.proofNesting == 0 ? this.OpenFrame() : -1);
        this.limitRead = null;
        this.errorRead = null;
        this.unknownRead = null;
        this.clauseRead = null;
        this.proofNesting++;
        return outer;
    }

    // SPEC 8.3.3, 23.1.1, 23.1.2 (DECISIONS BU-22): closes a proof scope with its answer. Inside another scope or the capability engine,
    // the cause the answer rests on joins the enclosing proof, so L combines as P < U < L < R < E; where the answer is no longer combined,
    // an L is Error resting on its site and an Error rests on its cause, in the channel; a check that fails on it takes it (TakeFailedCause),
    // which publishes a site.
    private ConstraintProof EndProof(
        (ICheckCause? Limit, ICheckCause? Error, CallableUnknown? Unknown, ClauseFact? Clause, int Frame) outer, ConstraintProof proof)
    {
        this.proofNesting--;
        var cause = this.ProofCause(proof);
        var fact = this.ProofFact(proof);
        this.limitRead = outer.Limit;
        this.errorRead = outer.Error;
        this.unknownRead = outer.Unknown;
        this.clauseRead = outer.Clause;
        if (outer.Frame >= 0)
        {
            this.CloseFrame(outer.Frame);
        }

        if (this.proofNesting != 0 || this.capabilityConsumer is not null)
        {
            return this.ReadVerdict(proof, cause, fact);
        }

        // Where the answer is no longer combined, an untaken U fact is dropped and an Unknown without a limit leaves its own for the
        // frame's consumer, under the epoch of the frame it is read in; an untaken clause fact is dropped (a clause list leaves its own
        // after its end, LeaveClause).
        this.failedClause = null;
        this.failedUnknown = cause is null ? fact : null;
        if (this.failedUnknown is { } left)
        {
            left.Epoch = this.frameEpoch;
        }

        if (cause is null)
        {
            return proof;
        }

        this.failedCause ??= cause;
        return ConstraintProof.Error;
    }

    // The cause the answer of the proof scope in progress rests on: an Unknown's limit, an Error's cause (EndProof).
    private ICheckCause? ProofCause(ConstraintProof proof)
        => proof switch { ConstraintProof.Unknown => this.limitRead, ConstraintProof.Error => this.errorRead, _ => null };

    // A part of a composite proof whose parts settle as separate judgments (ReferenceArgumentProof) keeps its own U fact as its own
    // frame does (design-r2 Amendment 5): HoldUnknown takes the fact the part left, without the channel's Error cause, so the later
    // parts' ends cannot drop it; LeaveUnknown hands it to the composite's consumer under the innermost open frame when the composite
    // is Unknown, and drops it otherwise.
    private CallableUnknown? HoldUnknown()
    {
        var fact = this.failedUnknown;
        this.failedUnknown = null;
        return fact;
    }

    private void LeaveUnknown(CallableUnknown? fact, ConstraintProof proof)
    {
        if (proof == ConstraintProof.Unknown && fact is not null)
        {
            fact.Epoch = this.frameEpoch;
            this.failedUnknown = fact;
        }
    }

    // A clause list's clause fact (G351a): the first clause at the list's combined verdict, E > R > L > U (`Rank`), handed after the
    // list's end to its enclosing scope, or where no scope encloses it to the channel under the innermost open frame, and only when
    // the list's combined verdict is that clause's outcome (as LeaveUnknown requires Unknown). The enclosing scope keeps the first at
    // the highest rank of the lists it holds; it hands it on no further (EndProof restores the outer scope's), so a cached verdict and a
    // computed one leave the same facts.
    private void LeaveClause(ClauseFact? fact, ConstraintProof combined)
    {
        if (fact is not { } clause || clause.Outcome != combined)
        {
            return;
        }

        if (this.proofNesting != 0)
        {
            this.clauseRead = FirstAtVerdict(this.clauseRead, clause);
            return;
        }

        this.failedClause = clause with { Epoch = this.frameEpoch };
    }

    // A composite proof whose parts settle as separate judgments (ReferenceArgumentProof) holds each part's clause fact, as HoldUnknown
    // holds its U fact, and leaves the first at the composite's verdict (LeaveClause).
    private ClauseFact? HoldClause(ClauseFact? held)
    {
        var fact = this.failedClause;
        this.failedClause = null;
        return fact is { } clause ? FirstAtVerdict(held, clause) : held;
    }

    // Opens a frame with a fresh epoch (design-r2 Amendment 5) and returns the enclosing frame's.
    private int OpenFrame()
    {
        var enclosing = this.frameEpoch;
        this.frameEpoch = ++this.epochCounter;
        return enclosing;
    }

    // Closes the innermost frame: its untaken facts are dropped and the enclosing frame is innermost again.
    private void CloseFrame(int enclosing)
    {
        this.failedUnknown = this.failedUnknown?.Epoch == this.frameEpoch ? null : this.failedUnknown;
        this.trialUnknown = this.trialUnknown?.Epoch == this.frameEpoch ? null : this.trialUnknown;
        this.failedClause = this.failedClause?.Epoch == this.frameEpoch ? null : this.failedClause;
        this.trialClause = this.trialClause?.Epoch == this.frameEpoch ? null : this.trialClause;
        this.frameEpoch = enclosing;
    }

    // The U fact the answer of the proof scope in progress rests on (EndProof, a cache entry).
    private CallableUnknown? ProofFact(ConstraintProof proof) => proof == ConstraintProof.Unknown ? this.unknownRead : null;

    // A read of an answer joins the proof in progress with what it rests on: an Unknown's limit and U fact, an Error's cause.
    private ConstraintProof ReadVerdict(ConstraintProof proof, ICheckCause? cause, CallableUnknown? fact)
    {
        if (proof == ConstraintProof.Unknown)
        {
            this.limitRead ??= cause;
            this.unknownRead ??= fact;
        }
        else if (proof == ConstraintProof.Error)
        {
            this.errorRead ??= cause;
        }

        return proof;
    }

    private void ClearProofScopes()
    {
        this.failedCause = null;
        this.limitRead = null;
        this.errorRead = null;
        this.unknownRead = null;
        this.failedUnknown = null;
        this.trialUnknown = null;
        this.clauseRead = null;
        this.failedClause = null;
        this.trialClause = null;
        this.frameEpoch = 0;
        this.proofNesting = 0;
    }

    // SPEC 23.1.2: the Use of the cause that the Error a check just consumed rests on, and the U fact a settled Unknown left (no fact is a
    // normal state); a limit a failing check needs is published by Binding.Check.
    private (Koto? Use, CallableUnknown? Fact) TakeFailedCause()
    {
        var fact = this.failedUnknown;
        this.failedUnknown = null;
        if (this.failedCause is not { } cause)
        {
            return (null, fact);
        }

        this.failedCause = null;
        cause.Rest();
        return (cause.Use, fact);
    }

    // SPEC 15.6.1 (design-r2 Amendment 4): a Callable proof that is Unknown only in the Origin part of its whole-contract comparison, with
    // that comparison's failure, stamped with the outermost CheckConstraints clause it was proven for. Diagnostic payload only.
    internal sealed class CallableUnknown(BoundType subject, BoundType required, ComparisonFailure failure)
    {
        internal BoundType Subject { get; } = subject;

        internal BoundType Required { get; } = required;

        internal ComparisonFailure Failure { get; } = failure;

        internal IsKoto? Clause { get; set; }

        // The epoch of the innermost open frame it is read in (design-r2 Amendment 5).
        internal int Epoch { get; set; }
    }

    // A clause of a clause list with its outcome: Refuted, Unknown (on a limit when `Limit`), or Error; the Constraint as proven, none
    // for a clause that did not bind. Diagnostic payload only (G351a).
    internal readonly record struct ClauseFact(IsKoto Clause, BoundConstraint? Constraint, ConstraintProof Outcome, bool Limit)
    {
        // The epoch of the innermost open frame it is read in (design-r2 Amendment 5).
        internal int Epoch { get; init; }

        // E > R > L > U, the conjunction's order (DECISIONS BU-22); a Proven clause has none.
        internal int Rank => this.Outcome switch
        {
            ConstraintProof.Error => 4,
            ConstraintProof.Refuted => 3,
            ConstraintProof.Unknown => this.Limit ? 2 : 1,
            _ => 0,
        };
    }
}
