// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// Origin records (SPEC 15.6.1: every failed chain; SPEC 23.1.2: independent problems). A body's relations are rows A5 judges
// (FormBodyRelation, DECISIONS R7-M66: calls, fits, local clauses and relations naming a pending atom); Binding judges in the
// declaration frame (JudgeDeclarationPosition, Binding.OriginClauses.cs) and keeps SPEC 5.7's Unstated and holder-place rules.
// - Responsibility: every Origin relation or Origin contract record, a node's first included, as a problem of its node with one
//   OriginRecord fact, numbered from 0 under its own requirement (DECISIONS R7-M95 E4d): one that fails its node (RecordOrigin, its first
//   failure only) or one that leaves it Resolved (RecordResolvedOrigin; SPEC 5.7's two rules, R7-O1 B4); the relations the declaration
//   judge collects (callRelationScratch: JudgeDeclarationPosition's, over JudgeDeclarationRows' rows and chains), each recorded at its node
//   (PublishCallRelations); each record's publication by its own key (ReportOriginRecord, ReportProblem's Origin row), the records outside
//   Binding.Issues included (ReportOutsideIssues).
// - Inputs: OriginRelationFact and OriginContractFact values with their nodes.
// - Outputs: the OriginRelation or OriginContract failure of a recorded node, as its first failure only, and the problems.
// - Invariants: callRelationScratch is empty after PublishCallRelations; a record's code is its own (a relation's Refuted or Unknown, a
//   contract's), so its requirement selects its row; an equal record at a node is the one kept (BindingReport.Condition); each record keeps
//   its kind, subject and location and is published once; no record is dropped, merged or reordered because of another (SPEC 23.1.2).
// - Prohibitions: no judgment of a relation or a comparison (OriginClauses, OriginProof and the Type relation judge decide;
//   FormBodyRelation forms rows); no message text.
public sealed partial class Binding
{
    private readonly List<(Koto Node, OriginRelationFact Fact)> callRelationScratch = new();

    // SPEC 15.6.1, 23.1.2: an Origin record that fails `node`: the record's requirement is the node's failure when it has none (Fail keeps
    // the first), and the record is a problem of its own beside any other.
    private BoundType? RecordOrigin(Koto node, in OriginRecord record)
    {
        this.Fail(node, record.Relation is null ? BindingFailure.OriginContract : BindingFailure.OriginRelation);
        this.AddOriginRecord(node, record);
        return null;
    }

    // R7-O1 B4 (SPEC 5.7's Unstated and holder-place rules; 15.6.1: a failed relation adds no constraint, so the use's Loans reach MIR): an
    // Origin relation record that leaves `at` Resolved, as a located limit does (RecordLocatedLimit).
    private void RecordResolvedOrigin(Koto at, in OriginRelationFact relation) => this.AddOriginRecord(at, new(relation, null));

    // The two recorders' problem: the record's own code and requirement, its kept record as the one fact, numbered from 0.
    private void AddOriginRecord(Koto node, in OriginRecord record)
    {
        var subject = SourceRef.Of(node);
        var (code, failure) = record.Relation is { } relation
            ? (OriginRelationCode(relation), BindingFailure.OriginRelation) : (DiagnosticCode.UnprovenOriginContract_Kd, BindingFailure.OriginContract);
        ProblemFact fact = new(ProblemFactKind.OriginRecord, subject, this.report.Keep(record));
        BindingProblem problem = new(code, DiagnosticRequirement.Binding(failure), subject, 0);
        this.report.Add(problem with { Condition = this.report.Condition(problem, [fact]) }, [fact]); // An equal record is the one kept.
    }

    // Records each collected relation at its node; the first fails the node, and the node's slot keeps an earlier failure.
    private void PublishCallRelations()
    {
        foreach (var (node, fact) in this.callRelationScratch)
        {
            this.RecordOrigin(node, new(fact, null));
        }

        this.callRelationScratch.Clear();
    }

    // SPEC 15.6.1, 23.1 (B-4): one Origin record by its key, a relation's with the SPEC 15.4.3 elision Note; its one fact is the record.
    private void ReportOriginRecord(Koto node, DiagnosticCollection? store, int index, in BindingProblem problem)
    {
        var record = this.report.Origin(this.report.Facts(index)[0]);
        if (record.Relation is { } relation)
        {
            this.ReportOriginRelation(node, store, problem, relation, this.OmittedResultNote(node));
        }
        else
        {
            ReportOriginContract(node, store, problem, record.Contract!.Value);
        }
    }

    // SPEC 23.1.2 (R7-O1 B4; DECISIONS BU-126): each Origin record outside Binding.Issues (OutsideIssues), a Resolved node's included, by
    // its own key: derived where its node's slot is an Origin record and its own requirement rests on the node's recorded inputs
    // (IsDerived, rule W), else direct. A problem a retried attempt unlinked is not its subject's.
    private void ReportOutsideIssues()
    {
        for (var i = 0; i < this.report.Count; i++)
        {
            var problem = this.report.Problem(i);
            if (this.compilation.SyntaxOf(problem.Subject) is not { } node || !this.OutsideIssues(node, i) || !this.Linked(problem.Subject, i))
            {
                continue;
            }

            var failure = (BindingFailure)problem.Requirement.Kind;
            BindingIssue issue = new(node, problem.Code) { Failure = failure, Problem = i };
            if (this.RecordedFailure(node) is BindingFailure.OriginRelation or BindingFailure.OriginContract && this.IsDerived(node, failure, problem.Code))
            {
                this.ReportDerived(issue);
            }
            else
            {
                this.ReportIssue(issue);
            }
        }
    }

    // Whether a problem is in its subject's chain.
    private bool Linked(SourceRef subject, int problem)
    {
        for (var i = this.report.First(subject); i >= 0; i = this.report.Next(i))
        {
            if (i == problem)
            {
                return true;
            }
        }

        return false;
    }
}
