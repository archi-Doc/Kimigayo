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

    /// <summary>Gets the runs of checks judged in combinations of split cases so far (RestsOnSplitCases), which a scaling check reads.</summary>
    internal long SplitCaseRuns => this.splitCaseRuns;

    /// <summary>Gets the formations of the open formed Type so far (OpenMarked: each declared component formed below its mark), which a
    /// scaling check reads.</summary>
    internal long OpenMarkedWork => this.openMarkedFormations;

    internal CallInferenceMetrics InferenceMetrics => new(this.inferenceMappings, this.inferenceComparisons, this.inferenceCandidateChecks, this.inferenceCorrelations, this.inferenceFixedChecks, this.inferenceFixedReuses, this.shapeContracts.Count, this.shapeContracts.Capacity, this.shapeDependencies.Capacity, this.slotWordScratch.RetainedCapacity);

    private void ResetInferenceMetrics()
    {
        this.inferenceMappings = this.inferenceComparisons = this.inferenceCandidateChecks = 0;
        this.inferenceCorrelations = this.inferenceFixedChecks = this.inferenceFixedReuses = 0;
        this.evidenceNameReads = 0;
    }
}
