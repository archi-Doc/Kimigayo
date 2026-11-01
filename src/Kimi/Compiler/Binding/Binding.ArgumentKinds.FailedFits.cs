// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// FailedFit (DECISIONS BU-77; SPEC 23.1.2, 10.8): per candidate of a call evaluation, each unbound failed part (PartFailed, no
// BoundType) its evidence walk meets and the candidate's pattern at that part's position, open or closed.
// - Producer: the evidence walk alone (SlotSolution's InferParts and InferPosition's one-part default row, PublishFailedFit), in both
//   passes; it reads PartFailed and appends, so the evidence results are unchanged: a failed part is never evidence. FitsPosition and
//   FitArgument publish nothing.
// - Storage: one Binding list in the evaluation's scratch. An evaluation marks its first entry (CallEvaluation.FailedFits) and releases
//   its entries when it ends, so a nested evaluation keeps its own; an entry is keyed by (candidate, part), and a later pass replaces it.
// - Readers: the selected candidate's unsolved slots (RestingFailedPart; SolveSelected); the Ambiguous outcome reads it in U1.6h-2.
// - Not covered: a failed part that kept its Type (r2-2), whose answer the merge into U2.1 decides (R4's C19).
public sealed partial class Binding
{
    private readonly List<FailedFit> failedFits = new();

    private readonly record struct FailedFit(int Candidate, Koto Part, BoundType Pattern);

    // A call candidate's FailedFit entries: its evaluation's first entry and the candidate's index in that evaluation.
    private readonly record struct FailedFitScope(int Start, int Candidate);

    private ref partial struct SlotSolution
    {
        // SPEC 23.1.2, 10.8 (U1.6h B2, R4's C15): the failed part the candidate's unsolved slots rest on, when every unsolved slot occurs
        // in the pattern of one of its FailedFit entries (the first such part); null when one occurs at none, or none is unsolved.
        internal readonly Koto? RestingFailedPart(int[] mapping)
        {
            if (this.FailedFits is not { } scope)
            {
                return null;
            }

            var binding = this.Binding;
            var unsolved = binding.RentSlotSet(CallSlotCount(this.Function));
            var slot = binding.RentSlotSet(CallSlotCount(this.Function));
            try
            {
                binding.UnsolvedSlots(this.Call, this.Function, this.Types, this.Lengths, mapping, ref unsolved, out _);
                Koto? cause = null;
                for (var s = 0; s < CallSlotCount(this.Function); s++)
                {
                    if (!unsolved.Contains(s))
                    {
                        continue;
                    }

                    slot.Clear();
                    slot.Add(s);
                    var part = this.FailedPartAt(scope, slot);
                    if (part is null)
                    {
                        return null;
                    }

                    cause ??= part;
                }

                return cause;
            }
            finally
            {
                binding.ReturnSlotSet(slot);
                binding.ReturnSlotSet(unsolved);
            }
        }

        // U1.6h: where the evidence walk meets an unbound failed part, the candidate's pattern at that position, replacing the entry an
        // earlier pass wrote for the same part. A solution that is no call candidate's publishes nothing.
        private readonly void PublishFailedFit(Koto part, BoundType pattern)
        {
            var fits = this.Binding.failedFits;
            if (this.FailedFits is not { } scope || PartCore(part).BoundType is not null || !this.Binding.PartFailed(part))
            {
                return;
            }

            for (var i = scope.Start; i < fits.Count; i++)
            {
                if (fits[i].Candidate == scope.Candidate && ReferenceEquals(fits[i].Part, part))
                {
                    fits[i] = fits[i] with { Pattern = pattern };
                    return;
                }
            }

            fits.Add(new(scope.Candidate, part, pattern));
        }

        // The first failed part of the candidate whose pattern mentions the slot.
        private readonly Koto? FailedPartAt(FailedFitScope scope, SlotSet slot)
        {
            var fits = this.Binding.failedFits;
            for (var i = scope.Start; i < fits.Count; i++)
            {
                if (fits[i].Candidate == scope.Candidate && MentionsSlots(fits[i].Pattern, this.SlotOwner, slot))
                {
                    return fits[i].Part;
                }
            }

            return null;
        }
    }
}
