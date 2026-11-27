// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// The Constraint records of a clause list's proof (census #25; DECISIONS R4-M155, G351), a part of RequireConstraint's shared path.
// - Responsibility: one record per node after its clause lists' proof, from the one clause fact (the first clause at the conjunction's
//   combined verdict, E > R > L > U; ClauseFact): a Function Item reference's (RequireClauses), a selection's pending candidate's and an
//   overloaded reference's (FailClauseRecord); its publication (ReportClauseConstraint), naming the declaration that states the clause
//   where the member does not (ForeignClauseOwner); the consumer's read of the fact a judgment left (TakeClauseFact); the publication of a
//   record holding only its caller's proposition (ReportProposition); a record's rest on the cause its Error proof left (RestOnCause,
//   for RequireClauses and FailOperator).
// - Inputs: the clause fact the proof frame left (TakeClauseFact) or a candidate's trial kept (ClauseFailure); the U fact of the same
//   clause.
// - Outputs: the node's UnsatisfiedConstraint_Kd or UnprovenConstraint_Kd naming its clause, its subject and the declaration; for a
//   Callable clause proven Unknown with its U fact and a failing condition, the Callable record with its member (FailUnprovenConstraint).
// - Invariants: one record channel per node; a clause record is made from the fact the proof left and only when the fact's outcome is
//   the proof's verdict; without a fact the node's record is RequireConstraint's.
// - Prohibitions: no proof or comparison runs to explain a record; no record is made during a proof.
public sealed partial class Binding
{
    // The declaration that states a clause fact's clause when neither the member nor a container enclosing it does (G354 (2)): a Type
    // formed for the use, such as a Type argument's, whose formation decided the fact; its record names it, not the member.
    private static string? ForeignClauseOwner(IsKoto clause, Koto? member)
    {
        var owner = clause.Parent;
        while (owner is not (null or DeclarationContainerKoto or FunctionKoto))
        {
            owner = owner.Parent;
        }

        for (var scope = member; scope is not null; scope = scope.Parent)
        {
            if (ReferenceEquals(scope, owner))
            {
                return null;
            }
        }

        return (owner as DeclarationContainerKoto)?.Name ?? (owner as FunctionKoto)?.Name;
    }

    // A Function Item reference's Constraint record after its proof (ReferenceArgumentProof): its clause fact's record, or the Callable
    // record when that clause's U fact names a failing condition. An Error, a fact resting on a limit (L) and a verdict without a fact at
    // it are RequireConstraint's record (an Error and a limit are derived from their cause, SPEC 23.1.2); the provisional pass only marks
    // an Unknown unresolved.
    private void RequireClauses(Koto use, ConstraintProof proof, BindingMode mode, BindingSymbol? declaration = null)
    {
        // A selected call's record names its declaration (`declaration`); a Function Item reference's names none.
        if (this.TakeClauseFact(use) is not { Limit: false } held || ClauseFailure(held, declaration) is not { } clause || clause.Proof != proof ||
            proof is not (ConstraintProof.Refuted or ConstraintProof.Unknown) || (proof == ConstraintProof.Unknown && mode != BindingMode.Final))
        {
            // R7-C3 (B1; SPEC 8.2.3 "never again at uses", 23.1.2): the use's requirement check reads a failed context through the reported
            // failure's producer channel: an Error resting on another failed node (its declaration, an Invalid environment's use) makes
            // the use's record derived.
            var cause = proof == ConstraintProof.Error ? this.TakeFailedCause().Use : null;
            this.RestOnCause(use, cause);
            this.RequireConstraint(use, proof, mode, cause);
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

    // SPEC 23.1.2 rule W (R7-C3 B1; PLAN G1356, B's ruling 7): a record resting on a reported failure is derived. The node's record rests
    // on the cause its Error proof left (TakeFailedCause) when that cause is a failed node other than the node itself; the one copy of the
    // rule, for RequireClauses and FailOperator.
    private void RestOnCause(Koto node, Koto? cause)
    {
        if (cause is { BindingState: BindingState.Invalid } && !ReferenceEquals(cause, node))
        {
            this.AddPrerequisite(node, cause);
        }
    }

    // The clause fact a judgment left for its consumer (G352-N1): read at depth 0 in the frame it was left in (design-r2 Amendment 5); a
    // read inside a proof or of another frame's fact is a broken contract (InternalInvariant_Kd), never a silent absence.
    private ClauseFact? TakeClauseFact(Koto use)
    {
        var fact = this.HoldClause(null);
        if (this.proofNesting == 0)
        {
            return this.OwnFrame(use, fact?.Epoch) ? fact : null;
        }

        this.Fail(use, BindingFailure.Internal);
        return null;
    }

    // R7-C3 (B1'; SPEC 8.2.3): a declaration's premise clauses whose fact, or an And part of it, failed (FailPremise): its premise
    // error's related locations, the premises that linked it, which the adapter's InvalidConstraint row relates (ReportConstraint,
    // F's C2 ruling 306). A failure path only.
    private List<(string Role, Koto At, string? Label)>? FailedPremiseClauses(Koto declaration)
    {
        if (!this.scopes.TryGetValue(declaration, out var scope) || scope.Constraints is not { FailedPremises: { Count: > 0 } premises })
        {
            return null;
        }

        List<(string Role, Koto At, string? Label)>? related = null;
        IReadOnlyList<Koto> clauses = declaration switch
        {
            FunctionKoto function => function.TypeConstraints,
            DeclarationContainerKoto container => container.ConstraintNodes,
            _ => [],
        };
        for (var i = 0; i < clauses.Count; i++)
        {
            if (clauses[i] is IsKoto { BoundConstraint: { } fact } clause && Failed(fact))
            {
                (related ??= new()).Add(("premise", clause, "the premise that linked the error"));
            }
        }

        return related;

        bool Failed(BoundConstraint fact)
            => premises.Contains(fact) || (fact.Kind == ConstraintKind.And && (Failed(fact.Left!) || Failed(fact.Right!)));
    }

    // The record of a clause fact through the one mapping (ConstraintFailureOf: Refuted is UnsatisfiedConstraint_Kd, Unknown
    // UnprovenConstraint_Kd), naming the clause (Related, role 0) and its kept proposition (Constraint), whose subject the record shows,
    // and a selected call's declaration (role 1), which states the clause or encloses the foreign Type that does (ForeignClauseOwner).
    // Recorded always, as landed (E2-5b), outside RecordConstraint's read: its callers RequireClauses (whose own test of the clause fact
    // stays), FailPendingConstraint and FunctionReferences' single inapplicable candidate (B1 none by reading: each earlier failure returns).
    private BoundType? FailClauseRecord(Koto use, ReferenceConstraintFailure clause, bool unresolved)
    {
        var failure = ConstraintFailureOf(clause.Proof);
        var site = this.FactSite(clause.Clause);
        Span<ProblemFact> facts = [new(ProblemFactKind.Related, site, 0), new(ProblemFactKind.Constraint, site, this.report.Keep(clause.Constraint)), default];
        var count = 2;
        if (clause.Declaration?.Declaration is { } declaration)
        {
            facts[count++] = new(ProblemFactKind.Related, this.FactSite(declaration), 1);
        }

        return this.Record(use, failure, facts[..count], unresolved: unresolved);
    }

    // A clause record from its facts: its clause related, labelled by the declaration that states it, and, for a selected call, that
    // declaration, whose name is read from it as a related declaration's is (any declaration a call selects). The Note stays (B's O-f,
    // (c): a CLI and LSP row pins it), rendered from the facts: the outcome by the code, the member and owner by the declarations, the
    // subject by the kept proposition.
    private void ReportClauseConstraint(Koto use, DiagnosticCollection? store, int index, in BindingProblem problem)
    {
        var facts = this.report.Facts(index);
        var clause = (IsKoto)this.compilation.SyntaxOf(facts[0].Site);
        var declaration = facts.Length > 2 ? this.compilation.SyntaxOf(facts[2].Site) : null;
        var member = declaration is not null ? declaration.BoundSymbol!.Name : use.BoundSymbol?.Name ?? "the referenced function";
        var owner = ForeignClauseOwner(clause, declaration ?? use.BoundSymbol?.Declaration);
        var subject = ClauseSubject(this.report.Constraint(facts[1])) is { } type ? DiagnosticTypeName(type) : null;
        var text = clause.ToString();
        var outcome = (BindingFailure)problem.Requirement.Kind == BindingFailure.UnsatisfiedConstraint ? "is refuted" : "cannot be proven";
        var role = owner is not null ? $"required by {owner}" : declaration is not null ? "required by the called declaration" : "required by the referenced declaration";
        var note = $"{(declaration is not null ? "Call" : "Function Item")} {member}{(owner is null ? null : ": " + owner)} requires {text}; this condition {outcome}" +
            (subject is null ? " under the supplied bindings" : $" for {subject}");
        (string Role, Koto At, string? Label)[] related = declaration is null
            ? [("constraint", clause, role)]
            : [("constraint", clause, role), ("declaration", declaration, member)];
        use.ReportIn(
            store,
            problem.Requirement,
            problem.Code,
            evidence: subject is null ? null : [subject, owner ?? member, text],
            note: note,
            related: related,
            condition: problem.Condition);
    }

    // A caller's proposition record (RequireConstraint's `proposition`: the erasure's `S is Owned`, SPEC 15.2.3), from its one Constraint
    // fact: the Note names the proposition and its outcome, as a clause record's does (ReportClauseConstraint), and an Unknown one shows
    // its subject and Contract as evidence (R7-E3 E3b-2: the erasure's own Note went, M3; B's ruling 12).
    private void ReportProposition(Koto use, DiagnosticCollection? store, int index, in BindingProblem problem)
    {
        var proposition = this.report.Constraint(this.report.Facts(index)[0]);
        var unknown = (BindingFailure)problem.Requirement.Kind == BindingFailure.UnprovenConstraint;
        var (subject, contract) = (DiagnosticTypeName(proposition.Subject!), proposition.Contract!.Name);
        use.ReportIn(
            store,
            problem.Requirement,
            problem.Code,
            evidence: unknown ? [subject, contract] : null,
            note: $"The use requires {subject} is {contract}; this condition {(unknown ? "cannot be proven" : "is refuted")}",
            condition: problem.Condition);
    }
}
