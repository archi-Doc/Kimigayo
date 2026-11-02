// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// Origin records beyond the first (SPEC 15.6.1: every failed chain; SPEC 23.1.2: independent problems; contract of the concern, the
// judgment in Binding.CallRelations.cs).
// - Responsibility: the Origin relation records that one judgment collects (callRelationScratch: a selected call's JudgeCallPosition,
//   a value call's inputs, OriginClauses' declaration rows), each node failed with its first record (PublishCallRelations); and the
//   one channel of a node's records beyond its first, Origin relation and Origin contract records alike (furtherOriginRecords,
//   RecordFurtherOriginRecord, also for RecordOriginFitFailure's other chains), reported after the node's first record, whichever its kind,
//   each with its own condition (ReportOriginRecords, ReportFurtherOriginRecords); whether a node has a collected record (HasCallRelation).
// - Inputs: OriginRelationFact and OriginContractFact values with their nodes; the node's first record in originRelations or
//   originContracts.
// - Outputs: the OriginRelation failure of each collected node and every node's further records.
// - Invariants: callRelationScratch is empty after PublishCallRelations; one kind-agnostic list per node, which excludes the node's first
//   record and holds no duplicate; each record keeps its kind, subject and location, and the adapter publishes it by its kind; no record
//   of one kind is dropped, merged or reordered because of another kind's first record (SPEC 23.1.2); furtherOriginRecords is reset with the
//   pass's Origin records (ResetOriginRecords).
// - Prohibitions: no judgment of a relation or a comparison (CallRelations, OriginFits, OriginClauses and the Type relation judge); no
//   second list for one kind; no message text.
public sealed partial class Binding
{
    // The records of a node beyond the first, published after it with their own conditions (SPEC 15.6.1: every failed chain).
    private readonly List<(Koto Node, OriginRelationFact Fact)> callRelationScratch = new();
    private Dictionary<Koto, List<OriginRecord>>? furtherOriginRecords;

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
        if (this.furtherOriginRecords?.TryGetValue(node, out var more) != true)
        {
            return;
        }

        for (var i = 0; i < more!.Count; i++)
        {
            if (more[i].Relation is { } relation)
            {
                ReportOriginRelation(node, relation, requirement, OriginRelationCode(relation), this.OmittedResultNote(node), (ushort)(i + 1));
            }
            else
            {
                ReportOriginContract(node, more[i].Contract!.Value, requirement, DiagnosticCode.UnprovenOriginContract_Kd, (ushort)(i + 1));
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

    // Fails each node with its first record and keeps the others for publication with it.
    private void PublishCallRelations()
    {
        for (var i = 0; i < this.callRelationScratch.Count; i++)
        {
            var (node, fact) = this.callRelationScratch[i];
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

    // A record of `node` after its first, of either kind, published with it (ReportFurtherOriginRecords).
    private void RecordFurtherOriginRecord(Koto node, OriginRecord record)
    {
        var records = this.furtherOriginRecords ??= new(ReferenceEqualityComparer.Instance);
        if (!records.TryGetValue(node, out var more))
        {
            records.Add(node, more = new());
        }

        if (!more.Contains(record))
        {
            more.Add(record);
        }
    }
}
