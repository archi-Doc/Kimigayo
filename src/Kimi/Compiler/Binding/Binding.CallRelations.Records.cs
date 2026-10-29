// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// Call relations, records beyond the first (SPEC 15.6.1: every failed chain; SPEC 23.1.2: independent problems; contract of the
// concern, the judgment in Binding.CallRelations.cs).
// - Responsibility: the Origin relation records that one judgment collects (callRelationScratch: a selected call's JudgeCallPosition,
//   a value call's inputs, OriginClauses' declaration rows), each node failed with its first record (PublishCallRelations) and its
//   other records kept (RecordMoreRelation, also for RecordOriginFitFailure's other chains), then reported with the node, each with
//   its own condition (ReportMoreCallRelations); whether a node has a collected record (HasCallRelation); the payload related to a
//   node's records (RelatePayload, D-1).
// - Inputs: OriginRelationFact values with their nodes; the node's first record in originRelations.
// - Outputs: the OriginRelation failure of each node and its further records.
// - Invariants: callRelationScratch is empty after PublishCallRelations; a node's further records exclude its first and hold no
//   duplicate; callRelations is reset with the pass's Origin relation records (ResetOriginRecords).
// - Prohibitions: no judgment of a relation (CallRelations, OriginFits and OriginClauses judge); no message text.
public sealed partial class Binding
{
    // The records of a node beyond the first, published after it with their own conditions (SPEC 15.6.1: every failed chain).
    private readonly List<(Koto Node, OriginRelationFact Fact)> callRelationScratch = new();
    private Dictionary<Koto, List<OriginRelationFact>>? callRelations;

    // The records of one call node beyond the first, each with its own condition so that it stays an independent problem.
    private void ReportMoreCallRelations(Koto node, DiagnosticRequirement requirement)
    {
        if (this.callRelations?.TryGetValue(node, out var more) != true)
        {
            return;
        }

        for (var i = 0; i < more!.Count; i++)
        {
            ReportOriginRelation(node, more[i], requirement, OriginRelationCode(more[i]), this.OmittedResultNote(node), (ushort)(i + 1));
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
                this.RecordMoreRelation(node, fact);
            }
        }

        this.callRelationScratch.Clear();
    }

    // D-1: relates `payload`, the contextual payload whose fit a call judged at its argument `node`, to the node's records that fit just
    // recorded, those relating no payload yet (role `payload`): the fact the construction completing the payload owns.
    private void RelatePayload(Koto node, Koto payload)
    {
        if (this.originRelations?.TryGetValue(node, out var first) == true && first.Payload is null)
        {
            this.originRelations[node] = first with { Payload = payload };
        }

        if (this.callRelations?.TryGetValue(node, out var more) == true)
        {
            for (var i = 0; i < more.Count; i++)
            {
                more[i] = more[i].Payload is null ? more[i] with { Payload = payload } : more[i];
            }
        }
    }

    // A record of `node` after its first, published with it (ReportMoreCallRelations).
    private void RecordMoreRelation(Koto node, OriginRelationFact fact)
    {
        var callRelations = this.callRelations ??= new(ReferenceEqualityComparer.Instance);
        if (!callRelations.TryGetValue(node, out var more))
        {
            callRelations.Add(node, more = new());
        }

        if (!more.Contains(fact))
        {
            more.Add(fact);
        }
    }
}
