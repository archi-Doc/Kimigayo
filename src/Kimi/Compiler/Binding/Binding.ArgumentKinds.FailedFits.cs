// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// FailedFit (DECISIONS BU-77; SPEC 23.1.2, 10.8): per candidate of a call evaluation, each unbound failed part (PartFailed, no
// BoundType) its evidence walk meets and the candidate's pattern at that part's position, open or closed.
// - Producer: the evidence walk alone (SlotSolution's InferParts and InferPosition's one-part default row, PublishFailedFit), in both
//   passes; it reads PartFailed and appends, so the evidence results are unchanged: a failed part is never evidence. An unbound Tuple,
//   array or Dictionary literal it meets whole publishes the failed parts it holds at that pattern. FitsPosition and FitArgument
//   publish nothing.
// - Storage: one Binding list in the evaluation's scratch. An evaluation marks its first entry (CallEvaluation.FailedFits) and releases
//   its entries when it ends, so a nested evaluation keeps its own; an entry is keyed by (candidate, part), and a later pass replaces it.
// - Readers: the selected candidate's unsolved slots (RestingFailedPart; SolveSelected) and the kept candidates of an ambiguity
//   (AmbiguityRestingPart; BindCallCore's Ambiguous outcome).
// - Not covered: a failed part that kept its Type (r2-2), whose answer the merge into U2.1 decides (R4's C19).
public sealed partial class Binding
{
    private readonly List<FailedFit> failedFits = new();

    // The owner's rule (2026-11-02, superseding C16): a mismatch is certain when it lies outside every own slot and no argument Type,
    // adapted or not (SPEC 10.2), fits both patterns. The walk stops at an own slot (OwnSlot; a length slot by its BoundLength
    // parameter) and at a Function Type, whose value's fit its own header decides against each pattern (SPEC 10.5.3); at a head
    // mismatch it asks the adaptation rules' owner (ExpectedAdaptation, both ways). The head is its own fields, not SameExceptOrigins,
    // which also compares Origin argument counts and raw Origins that never rank (10.4 step 2). Precondition (R4's C29): parameter shapes
    // forbid a layer difference at a position, so no own slot below a mismatch can make the adaptation instance-dependent (`ref/T`
    // against `i64` is ParameterShapeMismatch_Kd or MissingOriginBinding_Kd, and nothing adapts inside Option).
    private bool CertainMismatch(BoundType a, Koto ownerA, BoundType b, Koto ownerB, Koto part)
    {
        if (ReferenceEquals(a, b) || OwnSlot(a, ownerA) >= 0 || OwnSlot(b, ownerB) >= 0 || a.Kind == BoundTypeKind.Function ||
            b.Kind == BoundTypeKind.Function || IsOwnLength(a.LengthExpression, ownerA) || IsOwnLength(b.LengthExpression, ownerB))
        {
            return false;
        }

        if (a.Kind == b.Kind && a.Semantics == b.Semantics && ReferenceEquals(a.Symbol, b.Symbol) && a.Length == b.Length &&
            ReferenceEquals(a.LengthExpression, b.LengthExpression) && a.Components.Count == b.Components.Count &&
            (a.Components.Count > 0 || a.Symbol is not null))
        {
            for (var i = 0; i < a.Components.Count; i++)
            {
                if (this.CertainMismatch(a.Components[i], ownerA, b.Components[i], ownerB, part))
                {
                    return true;
                }
            }

            return false;
        }

        return this.ExpectedAdaptation(part, a, b, recordPair: false) is null && this.ExpectedAdaptation(part, b, a, recordPair: false) is null;
    }

    // SPEC 23.1.2, 10.4 (U1.6h B1; the owner's rule of 2026-11-02, superseding C16): the failed part an ambiguity rests on: one at which
    // two kept candidates' FailedFit patterns mismatch with certainty (CertainMismatch), so that a Type of the part separates them; null
    // otherwise, so that the ambiguity is reported, being independent or not certainly dependent.
    private Koto? AmbiguityRestingPart(in CallEvaluation evaluation)
    {
        var kept = evaluation.Evaluated.AsSpan(0, evaluation.Count);
        for (var a = 0; a < kept.Length; a++)
        {
            for (var b = a + 1; Kept(kept[a]) && b < kept.Length; b++)
            {
                if (Kept(kept[b]) && this.DifferingFailedPart(evaluation.FailedFits, a, b, kept) is { } part)
                {
                    return part;
                }
            }
        }

        return null;

        static bool Kept(in EvaluatedCandidate candidate) => candidate.State is CandidateApplicability.Applicable or CandidateApplicability.Waiting;
    }

    // The first failed part at which candidates `a` and `b`'s patterns mismatch with certainty, or null.
    private Koto? DifferingFailedPart(int start, int a, int b, ReadOnlySpan<EvaluatedCandidate> candidates)
    {
        var ownerA = CallSlotOwner((FunctionKoto)candidates[a].Symbol!.Declaration);
        var ownerB = CallSlotOwner((FunctionKoto)candidates[b].Symbol!.Declaration);
        for (var i = start; i < this.failedFits.Count; i++)
        {
            if (this.failedFits[i].Candidate == a && this.FailedFitOf(start, b, this.failedFits[i].Part) is { } other &&
                this.CertainMismatch(this.failedFits[i].Pattern, ownerA, other, ownerB, this.failedFits[i].Part))
            {
                return this.failedFits[i].Part;
            }
        }

        return null;
    }

    // Candidate `candidate`'s FailedFit pattern at the part, or null.
    private BoundType? FailedFitOf(int start, int candidate, Koto part)
    {
        for (var i = start; i < this.failedFits.Count; i++)
        {
            if (this.failedFits[i].Candidate == candidate && ReferenceEquals(this.failedFits[i].Part, part))
            {
                return this.failedFits[i].Pattern;
            }
        }

        return null;
    }

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
        // earlier pass wrote for the same part. An unbound Tuple, array or Dictionary literal it meets whole publishes the failed parts it
        // holds at that pattern (U1.6h-3', fix (b)). A solution that is no call candidate's publishes nothing.
        private readonly void PublishFailedFit(Koto part, BoundType pattern)
        {
            if (this.FailedFits is not { } scope || PartCore(part).BoundType is not null)
            {
                return;
            }

            if (!this.Binding.PartFailed(part))
            {
                this.PublishHeldFailures(PartCore(part), pattern);
                return;
            }

            var fits = this.Binding.failedFits;
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

        // The parts of an unbound aggregate the walk meets whole, each published at the aggregate's pattern.
        private readonly void PublishHeldFailures(Koto aggregate, BoundType pattern)
        {
            switch (aggregate)
            {
                case TupleLiteralKoto tuple:
                    foreach (var element in tuple.Elements)
                    {
                        this.PublishFailedFit(element, pattern);
                    }

                    break;
                case ArrayLiteralKoto array:
                    foreach (var element in array.Elements)
                    {
                        this.PublishFailedFit(element, pattern);
                    }

                    break;
                case DictionaryLiteralKoto dictionary:
                    foreach (var entry in dictionary.Entries)
                    {
                        this.PublishFailedFit(entry.Key, pattern);
                        this.PublishFailedFit(entry.Value, pattern);
                    }

                    break;
            }
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
