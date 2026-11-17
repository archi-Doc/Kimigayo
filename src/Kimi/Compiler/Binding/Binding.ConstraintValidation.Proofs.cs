// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The proof frame's failure channel (DECISIONS BU-22, N3-1; SPEC 8.3.3, 23.1.1, 23.1.2), a part of constraint validation.
// - Responsibility: the proof scopes (BeginProof, EndProof) and the causes they carry: the first limit (L) and Error cause a scope read,
//   kept by the scope whose answer rests on them (ProofCause, ReadVerdict); the one failed cause a settled proof leaves for the check
//   that failed on it (failedCause, TakeFailedCause) or for a later record (HoldCause); the reset of each pass (ClearProofScopes);
//   the judgment that left a candidate trial Pending before its proof, carried by the trial frame to its selection (trialPending, BU-86);
//   the clause fact a scope holds at its answer (ProofClause), which a clause list and a Type's per-use formation hand on (LeaveClause,
//   G354 (2)) and a candidate trial keeps for its evaluation (trialClause, TakeTrialClause).
// - Inputs: a proof's answers and the causes its producers met (ICheckCause: a conformance on its specification or limited by OCC-X, a
//   component summary's site or stored Type); the capability engine's consumer, under which a scope stays combined.
// - Outputs: each scope's answer combined as P < U < L < R < E; where an answer is no longer combined, an L as Error resting on its site
//   and an Error resting on its cause, left in the channel; the Use a failing check takes; a Pending trial's pre-proof judgment, which its
//   selection takes (TakeTrialPending).
// - Invariants: scopes nest (proofNesting), each reading into its own slot of the channel's stack (ProofReads), so a closing scope
//   leaves its enclosing scope's reads as they were and joins only what its answer rests on; a check clears the channel before
//   its proof and takes the cause after it; each pass starts with the channel clear. A fact the frame carries is stamped with the
//   outermost clause it was proven for, a consumer takes only a fact with its own frame's stamp, and an untaken fact is dropped when
//   the next judgment begins (design-r2 Amendment 4; the U fact joins in G216 (a) U2.1 f1-1; r2-6). Frames (design-r2 Amendment 5): a
//   proof step nested in a frame (BeginProof at depth > 0) opens no frame and pushes no epoch, and its facts carry the frame's epoch
//   outward to the frame's consumer; a frame is either the outermost BeginProof of a judgment or a candidate trial (TryCandidate's
//   OpenTrial, even inside an open frame), takes a fresh epoch from one monotonic counter, and its end pops its epoch and drops its
//   untaken facts; a trial's own facts pass to the enclosing frame, whose selection reads them before the next trial opens, and drop
//   with none (CloseTrial). A fact carries the epoch of the innermost open frame; a consumer compares it with the innermost open frame
//   at its read: an absent fact is normal, a present foreign fact InternalInvariant_Kd (OwnFrame).
// - Prohibitions: no proof decision reads a carried fact: no verdict, memo key or fixpoint equality depends on it; record selection
//   reads it. The channel decides no proof and publishes no record but a foreign fact's InternalInvariant_Kd at its consumer's use.
public sealed partial class Binding
{
    // The first cause a check's Error rests on (ICheckCause): a conformance resting on its specification, or one limited by OCC-X outside
    // conformance verification (ResolveConformance), or what a settled proof rested on (EndProof). A consumer clears it before its proof
    // and takes it after (TakeFailedCause); each pass starts with it clear.
    private ICheckCause? failedCause;

    // The proofs in progress (DECISIONS BU-22, N3-1): the channel's stack of scopes, one slot per depth (proofNesting; slot 0 is outside
    // every scope), each holding what its scope read (ProofReads), pushed by BeginProof and popped by EndProof in place.
    private ProofReads[] proofScopes = new ProofReads[16];
    private int proofNesting;

    // The U fact a settled Unknown leaves for its check (failedUnknown, design-r2 Amendment 4), diagnostic payload only.
    private CallableUnknown? failedUnknown;

    // The candidate trial's own U fact (SlotSolution.Prove), for the selection's explanation of a Pending candidate (CallableOriginFailure).
    private CallableUnknown? trialUnknown;

    // The clause fact (design-r2 G351a, R4-M155): left by a settled clause list for its check (failedClause) and kept by a candidate trial
    // (trialClause), each diagnostic payload only; a failed candidate's explanation reads its trial's (ClauseFailure, G352).
    private ClauseFact? failedClause;
    private ClauseFact? trialClause;

    // The judgment that left a candidate trial Pending before its proof (DECISIONS BU-86), diagnostic payload only; its selection reads
    // it (EvaluateCallCandidate).
    private PendingJudgment? trialPending;

    // The innermost open frame's epoch (0: none) and the monotonic counter that issues them, never reused (design-r2 Amendment 5), and
    // the frame that encloses the outermost scope's own (BeginProof, EndProof).
    private long frameEpoch;
    private long epochCounter;
    private long judgmentEnclosing;

    // What the proof scope in progress read: the slot of the channel's stack at its depth.
    private ref ProofReads ScopeReads => ref this.proofScopes[this.proofNesting];

    // The first clause at the highest rank of a clause list, or of the lists a scope holds (G351a).
    private static ClauseFact? FirstAtVerdict(ClauseFact? best, ClauseFact clause) => clause.Rank > (best?.Rank ?? 0) ? clause : best;

    // Opens a proof scope: a connective's operand, a clause list, a disjunction of conformance paths, a capability request. It pushes a
    // clear slot; the enclosing scopes' reads stay in theirs. A judgment (a scope whose answer EndProof no longer combines) opens a frame
    // and first drops the facts the last one left untaken (r2-6).
    private void BeginProof()
    {
        if (this.proofNesting == 0)
        {
            if (this.capabilityConsumer is null)
            {
                this.failedUnknown = null;
                this.failedClause = null;
            }

            this.judgmentEnclosing = this.OpenFrame();
        }

        if (++this.proofNesting == this.proofScopes.Length)
        {
            Array.Resize(ref this.proofScopes, this.proofNesting * 2);
        }

        this.proofScopes[this.proofNesting] = default;
    }

    // SPEC 8.3.3, 23.1.1, 23.1.2 (DECISIONS BU-22): closes a proof scope with its answer and pops its slot. Inside another scope or the
    // capability engine, the cause the answer rests on joins the enclosing proof, so L combines as P < U < L < R < E; where the answer is
    // no longer combined, an L is Error resting on its site and an Error rests on its cause, in the channel; a check that fails on it takes
    // it (TakeFailedCause), which publishes a site.
    private ConstraintProof EndProof(ConstraintProof proof)
    {
        var cause = this.ProofCause(proof);
        var fact = this.ProofFact(proof);
        var origin = proof == ConstraintProof.Proven ? null : this.ScopeReads.Origin;
        if (--this.proofNesting == 0)
        {
            this.CloseFrame(this.judgmentEnclosing);
        }

        if (this.proofNesting != 0 || this.capabilityConsumer is not null)
        {
            if (origin is not null)
            {
                this.ScopeReads.Origin ??= origin;
            }

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
        => proof switch { ConstraintProof.Unknown => this.ScopeReads.Limit, ConstraintProof.Error => this.ScopeReads.Error, _ => null };

    // The U fact a settled Unknown left (no fact is a normal state), taken alone: an Unknown's consumer rests on no Error cause
    // (FailUnprovenConstraint, r2-6). A part of a composite proof whose parts settle as separate judgments (ReferenceArgumentProof) keeps
    // its own as its own frame does (design-r2 Amendment 5), so the later parts' judgments cannot drop it; LeaveUnknown hands it to the
    // composite's consumer under the innermost open frame when the composite is Unknown, and drops it otherwise.
    private CallableUnknown? TakeUnknownFact()
    {
        var fact = this.failedUnknown;
        this.failedUnknown = null;
        return fact;
    }

    // The Error cause a settled proof left, held without resting on it (an erasure's Owned proof, r2-1a): an erasure publishes no limit
    // (it may be judged where nothing records); its record rests on it (FailOwnedConversion).
    private ICheckCause? HoldCause()
    {
        var cause = this.failedCause;
        this.failedCause = null;
        return cause;
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
    // the highest rank of the lists it holds; it hands it on no further (EndProof restores the outer scope's) unless it is a Type's
    // per-use formation, one judgment that hands on its lists' fact in the same way (CheckTypeConstraints, FormedPart; G354 (2)), so a
    // cached verdict and a computed one leave the same facts.
    private void LeaveClause(ClauseFact? fact, ConstraintProof combined)
    {
        if (fact is not { } clause || clause.Outcome != combined)
        {
            return;
        }

        if (this.proofNesting != 0)
        {
            ref var reads = ref this.ScopeReads;
            reads.Clause = FirstAtVerdict(reads.Clause, clause);
            return;
        }

        this.failedClause = clause with { Epoch = this.frameEpoch };
    }

    // A composite proof whose parts settle as separate judgments (ReferenceArgumentProof) holds each part's clause fact, as it holds its
    // U fact (TakeUnknownFact), and leaves the first at the composite's verdict (LeaveClause).
    private ClauseFact? HoldClause(ClauseFact? held)
    {
        var fact = this.failedClause;
        this.failedClause = null;
        return fact is { } clause ? FirstAtVerdict(held, clause) : held;
    }

    // Opens a frame with a fresh epoch (design-r2 Amendment 5) and returns the enclosing frame's.
    private long OpenFrame()
    {
        var enclosing = this.frameEpoch;
        this.frameEpoch = ++this.epochCounter;
        return enclosing;
    }

    // Closes the innermost frame: its untaken facts are dropped and the enclosing frame is innermost again; a frame that holds no fact
    // writes nothing else.
    private void CloseFrame(long enclosing)
    {
        var epoch = this.frameEpoch;
        if (this.failedUnknown is { } failed && failed.Epoch == epoch)
        {
            this.failedUnknown = null;
        }

        if (this.trialUnknown is { } trial && trial.Epoch == epoch)
        {
            this.trialUnknown = null;
        }

        if (this.failedClause.HasValue && this.failedClause.GetValueOrDefault().Epoch == epoch)
        {
            this.failedClause = null;
        }

        if (this.trialClause.HasValue && this.trialClause.GetValueOrDefault().Epoch == epoch)
        {
            this.trialClause = null;
        }

        if (this.trialPending.HasValue && this.trialPending.GetValueOrDefault().Epoch == epoch)
        {
            this.trialPending = null;
        }

        this.frameEpoch = enclosing;
    }

    // Opens a candidate trial's frame (TryCandidate, r2-6): a new trial owns no earlier trial's fact.
    private long OpenTrial()
    {
        this.trialUnknown = null;
        this.trialClause = null;
        this.trialPending = null;
        return this.OpenFrame();
    }

    // Closes a candidate trial's frame: its own facts pass to the enclosing frame, whose selection reads them before the next trial
    // opens (CallableOriginFailure); with no enclosing frame they drop, never handed to epoch 0 (r2-6, BU's P2).
    private void CloseTrial(long enclosing)
    {
        var unknown = enclosing != 0 ? this.trialUnknown : null;
        var clause = enclosing != 0 ? this.trialClause : null;
        var pending = enclosing != 0 ? this.trialPending : null;
        this.CloseFrame(enclosing);
        if (unknown is not null)
        {
            unknown.Epoch = enclosing;
        }

        this.trialUnknown = unknown;
        this.trialClause = clause is { } kept ? kept with { Epoch = enclosing } : null;
        this.trialPending = pending is { } judged ? judged with { Epoch = enclosing } : null;
    }

    // The selection's take of its candidate trial's pending judgment (EvaluateCallCandidate, DECISIONS BU-86), under the consumer rule.
    private PendingJudgment? TakeTrialPending(Koto use)
    {
        var fact = this.trialPending;
        this.trialPending = null;
        return this.OwnFrame(use, fact?.Epoch) ? fact : null;
    }

    // The candidate evaluation's take of its trial's clause fact (ClauseFailure, G352; G354 (2)), under the consumer rule.
    private ClauseFact? TakeTrialClause(Koto use)
    {
        var fact = this.trialClause;
        this.trialClause = null;
        return this.OwnFrame(use, fact?.Epoch) ? fact : null;
    }

    // The consumer rule of a carried fact (design-r2 Amendment 5, r2-6): true when no fact is present (a normal state) or its stamp is the
    // innermost open frame's; a present foreign fact is a broken contract, failing its use (InternalInvariant_Kd).
    private bool OwnFrame(Koto use, long? epoch)
    {
        if (epoch is null || epoch == this.frameEpoch)
        {
            return true;
        }

        this.Fail(use, BindingFailure.Internal);
        return false;
    }

    // The U fact the answer of the proof scope in progress rests on (EndProof, a cache entry).
    private CallableUnknown? ProofFact(ConstraintProof proof) => proof == ConstraintProof.Unknown ? this.ScopeReads.Unknown : null;

    // The clause fact the proof scope in progress holds at its answer (G351a), read before its end by the judgment that keeps it: a
    // candidate trial (SlotSolution.Prove), a Type's formation and its memo (FormedPart; G354 (2)).
    private ClauseFact? ProofClause(ConstraintProof proof) => this.ScopeReads.Clause is { } kept && kept.Outcome == proof ? kept : null;

    // A read of an answer joins the proof in progress with what it rests on: an Unknown's limit and U fact, an Error's cause.
    private ConstraintProof ReadVerdict(ConstraintProof proof, ICheckCause? cause, CallableUnknown? fact)
    {
        if (proof == ConstraintProof.Unknown)
        {
            ref var reads = ref this.ScopeReads;
            reads.Limit ??= cause;
            reads.Unknown ??= fact;
        }
        else if (proof == ConstraintProof.Error)
        {
            this.ScopeReads.Error ??= cause;
        }

        return proof;
    }

    // The answer of a proof that read an Invalid environment (the one proof read of it): Error resting on the environment, whose Use is
    // the node that made it Invalid; inside a combined proof it joins the proof in progress (ReadVerdict), else the channel.
    private ConstraintProof InvalidEnvironment(ConstraintEnvironment environment)
    {
        if (this.proofNesting != 0 || this.capabilityConsumer is not null)
        {
            return this.ReadVerdict(ConstraintProof.Error, environment, null);
        }

        this.failedCause ??= environment;
        return ConstraintProof.Error;
    }

    private void ClearProofScopes()
    {
        this.failedCause = null;
        Array.Clear(this.proofScopes);
        this.failedUnknown = null;
        this.trialUnknown = null;
        this.failedClause = null;
        this.trialClause = null;
        this.trialPending = null;
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
        internal long Epoch { get; set; }
    }

    // A clause of a clause list with its outcome: Refuted, Unknown (on a limit when `Limit`), or Error; the Constraint as proven, none
    // for a clause that did not bind. Diagnostic payload only (G351a).
    internal readonly record struct ClauseFact(IsKoto Clause, BoundConstraint? Constraint, ConstraintProof Outcome, bool Limit)
    {
        // The epoch of the innermost open frame it is read in (design-r2 Amendment 5).
        internal long Epoch { get; init; }

        // E > R > L > U, the conjunction's order (DECISIONS BU-22); a Proven clause has none.
        internal int Rank => this.Outcome switch
        {
            ConstraintProof.Error => 4,
            ConstraintProof.Refuted => 3,
            ConstraintProof.Unknown => this.Limit ? 2 : 1,
            _ => 0,
        };
    }

    // What a proof scope read, of which it keeps what its own answer rests on (EndProof): the first limit (L, an Unknown resting on a
    // located Unsupported_Kd) and the first Error cause (DECISIONS BU-22, N3-1); the U fact, the first Callable comparison failure an
    // Unknown rests on (design-r2 Amendment 4); the clause fact, the clause a clause list's combined verdict rests on, handed one step
    // outward (G351a); and the first Origin not proven `static` an Owned answer rests on, kept only when the answer is not Proven
    // (R4-M161, r2-1b; CapabilityWork.Origin). All but the causes are diagnostic payload only.
    private struct ProofReads
    {
        internal ICheckCause? Limit;
        internal ICheckCause? Error;
        internal CallableUnknown? Unknown;
        internal ClauseFact? Clause;
        internal BoundOrigin? Origin;
    }
}
