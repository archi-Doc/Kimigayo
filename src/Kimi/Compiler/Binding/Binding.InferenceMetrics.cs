// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal readonly record struct CallInferenceMetrics(long Mappings, long Comparisons, long CandidateChecks, long Correlations, long FixedChecks, long FixedReuses, int Contracts, int ContractCapacity, int DependencyCapacity, int SlotWordCapacity);

public sealed partial class Binding
{
    private long inferenceMappings;
    private long inferenceComparisons;
    private long inferenceCandidateChecks;
    private long inferenceCorrelations;
    private long inferenceFixedChecks;
    private long inferenceFixedReuses;
    private long evidenceNameReads;

    internal bool MeasureCallInference { get; set; }

    /// <summary>Gets the rows premise discovery's evidence names read in the latest pass (EvidenceNames), which a scaling check reads.</summary>
    internal long EvidenceNameWork => this.evidenceNameReads;

    internal CallInferenceMetrics InferenceMetrics => new(this.inferenceMappings, this.inferenceComparisons, this.inferenceCandidateChecks, this.inferenceCorrelations, this.inferenceFixedChecks, this.inferenceFixedReuses, this.shapeContracts.Count, this.shapeContracts.Capacity, this.shapeDependencies.Capacity, this.slotWordScratch.RetainedCapacity);

    private void ResetInferenceMetrics()
    {
        this.inferenceMappings = this.inferenceComparisons = this.inferenceCandidateChecks = 0;
        this.inferenceCorrelations = this.inferenceFixedChecks = this.inferenceFixedReuses = 0;
        this.evidenceNameReads = 0;
    }
}
