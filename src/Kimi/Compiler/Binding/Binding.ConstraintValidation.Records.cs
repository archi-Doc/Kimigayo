// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// The Constraint records of a clause list's proof (census #25; DECISIONS R4-M155, G351), a part of RequireConstraint's shared path.
// - Responsibility: one record per node after its clause lists' proof, from the one clause fact (the first clause at the conjunction's
//   combined verdict, E > R > L > U; ClauseFact): a Function Item reference's (RequireClauses), a selection's pending candidate's and an
//   overloaded reference's (FailClauseRecord); its publication (ReportClauseConstraint); the consumer's read of the fact a judgment
//   left (TakeClauseFact).
// - Inputs: the clause fact the proof frame left (TakeClauseFact) or a candidate's trial kept (ClauseFailure); the U fact of the same
//   clause.
// - Outputs: the node's UnsatisfiedConstraint_Kd or UnprovenConstraint_Kd naming its clause, its subject and the declaration; for a
//   Callable clause proven Unknown with its U fact and a failing condition, the Callable record with its member (FailUnprovenConstraint).
// - Invariants: one record channel per node; a clause record is made from the fact the proof left and only when the fact's outcome is
//   the proof's verdict; without a fact the node's record is RequireConstraint's.
// - Prohibitions: no proof or comparison runs to explain a record; no record is made during a proof.
public sealed partial class Binding
{
    // A node's clause record, published by ReportClauseConstraint.
    private Dictionary<Koto, ReferenceConstraintFailure>? clauseConstraints;

    // A Function Item reference's Constraint record after its proof (ReferenceArgumentProof): its clause fact's record, or the Callable
    // record when that clause's U fact names a failing condition. An Error, a fact resting on a limit (L) and a verdict without a fact at
    // it are RequireConstraint's record (an Error and a limit are derived from their cause, SPEC 23.1.2); the provisional pass only marks
    // an Unknown unresolved.
    private void RequireClauses(Koto use, ConstraintProof proof, BindingMode mode)
    {
        if (this.TakeClauseFact(use) is not { Limit: false } held || ClauseFailure(held, null) is not { } clause || clause.Proof != proof ||
            proof is not (ConstraintProof.Refuted or ConstraintProof.Unknown) || (proof == ConstraintProof.Unknown && mode != BindingMode.Final))
        {
            this.RequireConstraint(use, proof, mode);
            return;
        }

        if (proof == ConstraintProof.Unknown && this.failedUnknown is { Failure.IsCondition: true } fact &&
            ReferenceEquals(fact.Clause, clause.Clause))
        {
            this.FailUnprovenConstraint(use, this.TakeUnknownFact(), unresolved: false);
            return;
        }

        this.FailClauseRecord(use, clause, unresolved: false);
    }

    // The clause fact a judgment left for its consumer (G352-N1): read at depth 0 in the frame it was left in (design-r2 Amendment 5); a
    // read inside a proof or of another frame's fact is a broken contract (InternalInvariant_Kd), never a silent absence.
    private ClauseFact? TakeClauseFact(Koto use)
    {
        var fact = this.HoldClause(null);
        if (this.proofNesting == 0 && (fact is not { } clause || clause.Epoch == this.frameEpoch))
        {
            return fact;
        }

        this.Fail(use, BindingFailure.Internal);
        return null;
    }

    // The record of a clause fact: Refuted is UnsatisfiedConstraint_Kd, Unknown UnprovenConstraint_Kd, each naming the clause.
    private BoundType? FailClauseRecord(Koto use, ReferenceConstraintFailure clause, bool unresolved)
    {
        var failure = clause.Proof == ConstraintProof.Refuted ? BindingFailure.UnsatisfiedConstraint : BindingFailure.UnprovenConstraint;
        return this.FailExplained(ref this.clauseConstraints, use, failure, clause, unresolved);
    }

    private void ReportClauseConstraint(Koto use, ReferenceConstraintFailure fact, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var call = fact.Declaration is not null;
        var member = fact.Declaration?.Name ?? use.BoundSymbol?.Name ?? "the referenced function";
        var subject = ClauseSubject(fact.Constraint) is { } type ? DiagnosticTypeName(type) : null;
        var clause = fact.Clause.ToString();
        var outcome = fact.Proof == ConstraintProof.Refuted ? "is refuted" : "cannot be proven";
        var note = $"{(call ? "Call" : "Function Item")} {member} requires {clause}; this condition {outcome}" +
            (subject is null ? " under the supplied bindings" : $" for {subject}");
        use.Report(
            requirement,
            code,
            evidence: subject is null ? null : [subject, member, clause],
            note: note,
            related: [("constraint", fact.Clause, call ? "required by the called declaration" : "required by the referenced declaration")]);
    }
}
