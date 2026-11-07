// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// Origin records beyond the first (SPEC 15.6.1: every failed chain; SPEC 23.1.2: independent problems; contract of the concern, the
// judgment in Binding.CallRelations.cs).
// - Responsibility: the Origin relation records that one judgment collects (callRelationScratch: a selected call's JudgeCallPosition,
//   a value call's inputs, OriginClauses' declaration rows), each node failed with its first record or published with its contract
//   record (FollowsContract; PublishCallRelations); and a node's records beyond its first, Origin relation and Origin contract records
//   alike, each a problem of the node with its own condition, published after the node's first record (RecordFurtherOriginRecord, also for
//   RecordOriginFitFailure's other chains; ReportOriginRecords, ReportFurtherOriginRecords); whether a node has a collected record
//   (HasCallRelation).
// - Inputs: OriginRelationFact and OriginContractFact values with their nodes; the node's first record in originRelations or
//   originContracts.
// - Outputs: the OriginRelation failure of each collected node and every node's further records.
// - Invariants: callRelationScratch is empty after PublishCallRelations; a node's further records exclude its first record and hold no
//   duplicate; each record keeps its kind, subject and location, and the adapter publishes it by its kind; no record of one kind is
//   dropped, merged or reordered because of another kind's first record (SPEC 23.1.2).
// - Prohibitions: no judgment of a relation or a comparison (CallRelations, OriginFits, OriginClauses and the Type relation judge); no
//   message text.
public sealed partial class Binding
{
    private readonly List<(Koto Node, OriginRelationFact Fact)> callRelationScratch = new();

    // SPEC 15.6.1, 23.1.2: whether a node's Origin relations are all records beyond its first: its own record is a comparison's
    // contract (RecordComparisonFacts), and every relation judged at it is published with that record (RecordOriginChains,
    // PublishCallRelations).
    private static bool FollowsContract(Koto node) => node.BindingFailure == BindingFailure.OriginContract;

    // A node's first Origin relation or Origin contract record, then its records beyond the first; false when it has neither.
    private bool ReportOriginRecords(Koto node, DiagnosticCode code, DiagnosticRequirement requirement)
    {
        if (code is DiagnosticCode.UnsatisfiedOriginRelation_Kd or DiagnosticCode.UnprovenOriginRelation_Kd &&
            this.originRelations?.TryGetValue(node, out var relation) == true)
        {
            ReportOriginRelation(node, relation, requirement, code, this.OmittedResultNote(node));
        }
        else if (code == DiagnosticCode.UnprovenOriginContract_Kd && this.originContracts?.TryGetValue(node, out var contract) == true)
        {
            ReportOriginContract(node, contract, requirement, code);
        }
        else
        {
            return false;
        }

        this.ReportFurtherOriginRecords(node, requirement);
        return true;
    }

    // The records of one node beyond the first, each by its kind and with its own condition so that it stays an independent problem.
    private void ReportFurtherOriginRecords(Koto node, DiagnosticRequirement requirement)
    {
        for (var i = this.report.First(SourceRef.Written(node)); i >= 0; i = this.report.Next(i))
        {
            if (this.FactOf(i, ProblemFactKind.OriginRecord) is not { } fact)
            {
                continue;
            }

            var problem = this.report.Problem(i);
            if (this.report.Origin(fact).Relation is { } relation)
            {
                ReportOriginRelation(node, relation, requirement, problem.Code, this.OmittedResultNote(node), problem.Condition);
            }
            else
            {
                ReportOriginContract(node, this.report.Origin(fact).Contract!.Value, requirement, problem.Code, problem.Condition);
            }
        }
    }

    private bool HasCallRelation(Koto node)
    {
        for (var i = 0; i < this.callRelationScratch.Count; i++)
        {
            if (ReferenceEquals(this.callRelationScratch[i].Node, node))
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
        if (this.report.OriginCondition(subject, record) is not 0 and var condition)
        {
            var (code, failure) = record.Relation is { } relation
                ? (OriginRelationCode(relation), BindingFailure.OriginRelation) : (DiagnosticCode.UnprovenOriginContract_Kd, BindingFailure.OriginContract);
            this.report.Add(new(code, DiagnosticRequirement.Binding(failure), subject, condition), [new(ProblemFactKind.OriginRecord, subject, this.report.Keep(record))]);
        }
    }
}
