// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal readonly record struct AssociatedInferenceMetrics(int Batches, int Matches, int Evidence, int PathCapacity, int EvidenceCapacity, int GraphCapacity);

public sealed partial class Binding
{
    internal AssociatedInferenceMetrics AssociatedMetrics
    {
        get
        {
            var matches = 0;
            var evidence = 0;
            var paths = 0;
            var evidenceCapacity = 0;
            var batches = 0;
            var graphCapacity = this.associatedInference.Capacity + this.pendingAssociatedTypes.EnsureCapacity(0) + this.completeTypeIdentities.EnsureCapacity(0) +
                this.inferenceFailures.EnsureCapacity(0) + this.inferenceSources.Capacity + this.inferenceInvalidation.EnsureCapacity(0);
            for (var i = 0; i < this.impls.Paths.Count; i++)
            {
                var path = this.impls.Paths[i];
                matches += path.RequirementMatches?.Count ?? 0;
                graphCapacity += (path.RequirementMatches?.Capacity ?? 0) + (path.InferenceConsumers?.Capacity ?? 0);
                if (path.AssociatedBindings is { } bindings)
                {
                    foreach (var (_, binding) in bindings)
                    {
                        evidenceCapacity += binding.EvidenceSources.Capacity;
                    }
                }
            }

            for (var rows = this.associatedInference.Values; rows.MoveNext(); batches++)
            {
                var batch = rows.Current!;
                paths += batch.Paths.Capacity;
                graphCapacity += batch.Evidence.EnsureCapacity(0) + batch.Normalized.EnsureCapacity(0) + batch.Holes.EnsureCapacity(0) + batch.Pairs.EnsureCapacity(0) + batch.CheckedTypes.EnsureCapacity(0) + batch.ProblemTypes.EnsureCapacity(0);
                foreach (var entries in batch.Evidence.Values)
                {
                    evidence += entries.Count;
                    evidenceCapacity += entries.Capacity;
                }
            }

            return new(batches, matches, evidence, paths, evidenceCapacity, graphCapacity);
        }
    }
}
