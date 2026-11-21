// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// Queries: the parts checked against an expected Type that started a query (SPEC 10.5.1; a memo hit is no query), which a query's own
// counters do not hold (CloseQuery restores them).
internal readonly record struct CallInferenceMetrics(long Mappings, long Comparisons, long CandidateChecks, long Correlations, long FixedChecks, long FixedReuses, int Contracts, int ContractCapacity, int DependencyCapacity, int SlotWordCapacity, long Queries);

public sealed partial class Binding
{
    private long inferenceMappings;
    private long inferenceComparisons;
    private long inferenceCandidateChecks;
    private long inferenceCorrelations;
    private long inferenceFixedChecks;
    private long inferenceFixedReuses;
    private long evidenceNameReads;
    private long inferenceQueries;

    internal bool MeasureCallInference { get; set; }

    /// <summary>Gets the rows premise discovery's evidence names read in the latest pass (EvidenceNames), which a scaling check reads.</summary>
    internal long EvidenceNameWork => this.evidenceNameReads;

    internal CallInferenceMetrics InferenceMetrics => new(this.inferenceMappings, this.inferenceComparisons, this.inferenceCandidateChecks, this.inferenceCorrelations, this.inferenceFixedChecks, this.inferenceFixedReuses, this.shapeContracts.Count, this.shapeContracts.Capacity, this.shapeDependencies.Capacity, this.slotWordScratch.RetainedCapacity, this.inferenceQueries);

    private void ResetInferenceMetrics()
    {
        this.inferenceMappings = this.inferenceComparisons = this.inferenceCandidateChecks = 0;
        this.inferenceCorrelations = this.inferenceFixedChecks = this.inferenceFixedReuses = 0;
        this.evidenceNameReads = 0;
        this.inferenceQueries = 0;
    }
}
