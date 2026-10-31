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
//   outermost EndProof (design-r2 Amendment 4; the U fact joins in G216 (a) U2.1 f1-1).
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

    // Opens a proof scope: a connective's operand, a clause list, a disjunction of conformance paths, a capability request.
    private (ICheckCause? Limit, ICheckCause? Error) BeginProof()
    {
        var outer = (this.limitRead, this.errorRead);
        this.limitRead = null;
        this.errorRead = null;
        this.proofNesting++;
        return outer;
    }

    // SPEC 8.3.3, 23.1.1, 23.1.2 (DECISIONS BU-22): closes a proof scope with its answer. Inside another scope or the capability engine,
    // the cause the answer rests on joins the enclosing proof, so L combines as P < U < L < R < E; where the answer is no longer combined,
    // an L is Error resting on its site and an Error rests on its cause, in the channel; a check that fails on it takes it (TakeFailedCause),
    // which publishes a site.
    private ConstraintProof EndProof((ICheckCause? Limit, ICheckCause? Error) outer, ConstraintProof proof)
    {
        this.proofNesting--;
        var cause = this.ProofCause(proof);
        this.limitRead = outer.Limit;
        this.errorRead = outer.Error;
        if (this.proofNesting != 0 || this.capabilityConsumer is not null)
        {
            return this.ReadVerdict(proof, cause);
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

    // A read of an answer joins the proof in progress with what it rests on: an Unknown's limit, an Error's cause.
    private ConstraintProof ReadVerdict(ConstraintProof proof, ICheckCause? cause)
    {
        if (proof == ConstraintProof.Unknown)
        {
            this.limitRead ??= cause;
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
        this.proofNesting = 0;
    }

    // SPEC 23.1.2: the Use of the cause that the Error a check just consumed rests on; a limit a failing check needs is published by
    // Binding.Check.
    private Koto? TakeFailedCause()
    {
        if (this.failedCause is not { } cause)
        {
            return null;
        }

        this.failedCause = null;
        cause.Rest();
        return cause.Use;
    }
}
