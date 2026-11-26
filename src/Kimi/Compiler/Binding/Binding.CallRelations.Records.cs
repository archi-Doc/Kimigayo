// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// Origin records beyond the first (SPEC 15.6.1: every failed chain; SPEC 23.1.2: independent problems). A body's relations are rows A5
// judges (FormBodyRelation, R7-O1: calls, fits, local clauses and pending relations); Binding judges in the declaration frame
// (JudgeDeclarationPosition, Binding.OriginClauses.cs) and keeps SPEC 5.7's Unstated and holder-place rules.
// - Responsibility: the Origin relation records the declaration judge collects (callRelationScratch: JudgeDeclarationPosition's, over
//   JudgeDeclarationRows' rows and chains), each node failed with its first record or published with its contract record
//   (FollowsContract; PublishCallRelations); a node's records beyond its first, Origin relation and Origin contract records alike, each
//   a problem of the node with its own condition, published after the node's first record (RecordFurtherOriginRecord;
//   ReportOriginRecords, ReportFurtherOriginRecords); and the relation problems that leave their node Resolved (SPEC 5.7's two rules,
//   R7-O1 B4), published by their subject whatever its failure (ReportResolvedOriginRecords).
// - Inputs: OriginRelationFact and OriginContractFact values with their nodes; the node's first record in originRelations or
//   originContracts.
// - Outputs: the OriginRelation failure of each collected node and every node's further records.
// - Invariants: callRelationScratch is empty after PublishCallRelations; a node's further records exclude its first record and hold no
//   duplicate; each record keeps its kind, subject and location, and the adapter publishes it by its kind; no record of one kind is
//   dropped, merged or reordered because of another kind's first record (SPEC 23.1.2).
// - Prohibitions: no judgment of a relation or a comparison (OriginClauses, OriginFits and the Type relation judge decide; CallRelations
//   forms rows); no message text.
public sealed partial class Binding
{
    private readonly List<(Koto Node, OriginRelationFact Fact)> callRelationScratch = new();

    // SPEC 15.6.1, 23.1.2: whether a node's Origin relations are all records beyond its first: its own record is a comparison's
    // contract (RecordComparisonFacts), and every relation judged at it is published with that record (PublishCallRelations).
    private static bool FollowsContract(Koto node) => node.BindingFailure == BindingFailure.OriginContract;

    // A node's first Origin relation or Origin contract record, then its records beyond the first; false when it has neither.
    private bool ReportOriginRecords(Koto node, DiagnosticCollection? store, DiagnosticCode code, DiagnosticRequirement requirement)
    {
        BindingProblem first = new(code, requirement, SourceRef.Written(node), 0);
        if (code is DiagnosticCode.UnsatisfiedOriginRelation_Kd or DiagnosticCode.UnprovenOriginRelation_Kd &&
            this.originRelations?.TryGetValue(node, out var relation) == true)
        {
            this.ReportOriginRelation(node, store, first, relation, this.OmittedResultNote(node));
        }
        else if (code == DiagnosticCode.UnprovenOriginContract_Kd && this.originContracts?.TryGetValue(node, out var contract) == true)
        {
            ReportOriginContract(node, store, first, contract);
        }
        else
        {
            return false;
        }

        this.ReportFurtherOriginRecords(node, store, requirement);
        return true;
    }

    // The records of one node beyond the first, each by its kind and with its own condition so that it stays an independent problem.
    private void ReportFurtherOriginRecords(Koto node, DiagnosticCollection? store, DiagnosticRequirement requirement)
    {
        for (var i = this.report.First(SourceRef.Written(node)); i >= 0; i = this.report.Next(i))
        {
            if (this.FactOf(i, ProblemFactKind.OriginRecord) is not { } fact)
            {
                continue;
            }

            var problem = this.report.Problem(i) with { Requirement = requirement };
            if (this.report.Origin(fact).Relation is { } relation)
            {
                this.ReportOriginRelation(node, store, problem, relation, this.OmittedResultNote(node));
            }
            else
            {
                ReportOriginContract(node, store, problem, this.report.Origin(fact).Contract!.Value);
            }
        }
    }

    // SPEC 23.1.2 (R7-O1 B4): the relation problems of nodes whose own record is no Origin record, a Resolved node's included, each by its
    // subject; a node failed with an Origin record publishes them with it (ReportOriginRecords). A problem a retried attempt unlinked is
    // not its subject's.
    private void ReportResolvedOriginRecords()
    {
        for (var i = 0; i < this.report.Count; i++)
        {
            var problem = this.report.Problem(i);
            if (this.FactOf(i, ProblemFactKind.OriginRecord) is not { } fact || !this.Linked(problem.Subject, i) ||
                this.compilation.SyntaxOf(problem.Subject) is not { } node || this.RecordedFailure(node) is BindingFailure.OriginRelation or BindingFailure.OriginContract)
            {
                continue;
            }

            var store = this.compilation.StoreOf(node);
            if (this.report.Origin(fact).Relation is { } relation)
            {
                this.ReportOriginRelation(node, store, problem, relation, this.OmittedResultNote(node));
            }
            else
            {
                ReportOriginContract(node, store, problem, this.report.Origin(fact).Contract!.Value);
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

    // Fails each node with its first record and keeps the others for publication with it; beside a contract, each is a further record.
    private void PublishCallRelations()
    {
        for (var i = 0; i < this.callRelationScratch.Count; i++)
        {
            var (node, fact) = this.callRelationScratch[i];
            if (FollowsContract(node))
            {
                this.RecordFurtherOriginRecord(node, new(fact, null));
                continue;
            }

            var first = true;
            for (var j = 0; j < i; j++)
            {
                first &= !ReferenceEquals(this.callRelationScratch[j].Node, node);
            }

            if (first)
            {
                this.FailExplained(ref this.originRelations, node, BindingFailure.OriginRelation, fact);
            }
            else if (this.originRelations?.TryGetValue(node, out var primary) == true && !primary.Equals(fact))
            {
                this.RecordFurtherOriginRecord(node, new(fact, null));
            }
        }

        this.callRelationScratch.Clear();
    }

    // A record of `node` after its first, of either kind: a problem beside the slot, whose first failure the check sets. A chain walk, as a
    // list's Contains was: a node's records are its failed chains.
    private void RecordFurtherOriginRecord(Koto node, in OriginRecord record)
    {
        var subject = SourceRef.Of(node);
        var (code, failure) = record.Relation is { } relation
            ? (OriginRelationCode(relation), BindingFailure.OriginRelation) : (DiagnosticCode.UnprovenOriginContract_Kd, BindingFailure.OriginContract);
        ProblemFact fact = new(ProblemFactKind.OriginRecord, subject, this.report.Keep(record));
        BindingProblem problem = new(code, DiagnosticRequirement.Binding(failure), subject, 1); // Condition 1 at least: the node's first record, the slot's, may be set after it (RecordOriginChains).
        this.report.Add(problem with { Condition = this.report.Condition(problem, [fact]) }, [fact]); // An equal record is the one kept.
    }
}
