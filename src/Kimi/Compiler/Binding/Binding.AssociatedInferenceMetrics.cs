// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal readonly record struct AssociatedInferenceMetrics(int Batches, int Matches, int Evidence, int PathCapacity, int EvidenceCapacity, int GraphCapacity);

public sealed partial class Binding
{
    internal AssociatedInferenceMetrics AssociatedMetrics
    {
        get
        {
            var evidence = 0;
            var paths = 0;
            var evidenceCapacity = 0;
            var graphCapacity = this.associatedInference.EnsureCapacity(0) + this.requirementMatches.EnsureCapacity(0) + this.pendingAssociatedTypes.EnsureCapacity(0) + this.completeTypeIdentities.EnsureCapacity(0) +
                this.inferenceFailures.EnsureCapacity(0) + this.inferenceConsumers.EnsureCapacity(0) + this.inferenceInvalidation.EnsureCapacity(0) + this.publishedInference.EnsureCapacity(0);
            foreach (var consumers in this.inferenceConsumers.Values)
            {
                graphCapacity += consumers.Capacity;
            }

            foreach (var binding in this.associatedBindings.Values)
            {
                evidenceCapacity += binding.EvidenceSources.Capacity;
            }

            foreach (var batch in this.associatedInference.Values)
            {
                paths += batch.Paths.Capacity;
                graphCapacity += batch.Evidence.EnsureCapacity(0) + batch.Normalized.EnsureCapacity(0) + batch.Holes.EnsureCapacity(0) + batch.Pairs.EnsureCapacity(0) + batch.CheckedTypes.EnsureCapacity(0) + batch.ProblemTypes.EnsureCapacity(0);
                foreach (var entries in batch.Evidence.Values)
                {
                    evidence += entries.Count;
                    evidenceCapacity += entries.Capacity;
                }
            }

            return new(this.associatedInference.Count, this.requirementMatches.Count, evidence, paths, evidenceCapacity, graphCapacity);
        }
    }
}
