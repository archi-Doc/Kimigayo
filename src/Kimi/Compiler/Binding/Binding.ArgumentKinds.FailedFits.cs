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
// - Readers: the selected candidate's unsolved slots (RestingFailedPart; SolveSelected) and the kept candidates of an ambiguity
//   (AmbiguityRestingPart; BindCallCore's Ambiguous outcome).
// - Not covered: a failed part that kept its Type (r2-2), whose answer the merge into U2.1 decides (R4's C19).
public sealed partial class Binding
{
    private readonly List<FailedFit> failedFits = new();

    // The slots of two candidates paired by AgreeModuloSlots, A's to B's, one-to-one; scratch of one comparison.
    private readonly List<(BindingSymbol A, BindingSymbol B)> pairedSlots = new();

    // SPEC 10.4, 10.8 (U1.6h B1, R4's C16): whether two candidates' patterns agree structurally, each candidate's own slots (Type and
    // length) paired one-to-one in order of first occurrence (`pairs`); Origin bindings never rank (10.4 step 2), so they are not read.
    private static bool AgreeModuloSlots(BoundType a, Koto ownerA, BoundType b, Koto ownerB, List<(BindingSymbol A, BindingSymbol B)> pairs)
    {
        var slotA = a.Kind == BoundTypeKind.Parameter && ContainerSlot(ownerA, a.Symbol!) >= 0;
        if (slotA || (b.Kind == BoundTypeKind.Parameter && ContainerSlot(ownerB, b.Symbol!) >= 0))
        {
            return slotA && b.Kind == BoundTypeKind.Parameter && a.Semantics == b.Semantics && Pair(a.Symbol!, b.Symbol!, ownerB, pairs);
        }

        if (ReferenceEquals(a, b))
        {
            return true;
        }

        // Interned Types: two that differ but in their parts' slots have the same form, and a Primitive has no parts.
        if (a.Kind == BoundTypeKind.Primitive || !SameForm(a, b) || !LengthsAgree(a.LengthExpression, ownerA, b.LengthExpression, ownerB, pairs))
        {
            return false;
        }

        for (var i = 0; i < a.Components.Count; i++)
        {
            if (!AgreeModuloSlots(a.Components[i], ownerA, b.Components[i], ownerB, pairs))
            {
                return false;
            }
        }

        return true;
    }

    // The fields FitsTypeCore compares besides the parts and the Origins.
    private static bool SameForm(BoundType a, BoundType b)
        => SameHead(a, b) && a.Length == b.Length && a.LengthArguments.AsSpan().SequenceEqual(b.LengthArguments) && a.Components.Count == b.Components.Count;

    private static bool SameHead(BoundType a, BoundType b)
        => a.Kind == b.Kind && a.Semantics == b.Semantics && ReferenceEquals(a.Symbol, b.Symbol) && ReferenceEquals(a.ClosureContext, b.ClosureContext);

    // A fixed array's length: the same expression, or two own length slots paired as Type slots are.
    private static bool LengthsAgree(BoundLength? a, Koto ownerA, BoundLength? b, Koto ownerB, List<(BindingSymbol A, BindingSymbol B)> pairs)
    {
        if (a?.Parameter is { } parameterA && ContainerSlot(ownerA, parameterA) >= 0)
        {
            return b?.Parameter is { } parameterB && Pair(parameterA, parameterB, ownerB, pairs);
        }

        return ReferenceEquals(a, b);
    }

    // Pairs slot `a` with `b`, an own slot of `ownerB`, unless either is paired with another.
    private static bool Pair(BindingSymbol a, BindingSymbol b, Koto ownerB, List<(BindingSymbol A, BindingSymbol B)> pairs)
    {
        if (ContainerSlot(ownerB, b) < 0)
        {
            return false;
        }

        foreach (var (x, y) in pairs)
        {
            if (ReferenceEquals(x, a) || ReferenceEquals(y, b))
            {
                return ReferenceEquals(x, a) && ReferenceEquals(y, b);
            }
        }

        pairs.Add((a, b));
        return true;
    }

    // R4's C16: the paired slots' Constraints are equal: the clauses of each candidate that name a paired slot, in order, agree pairwise.
    private static bool ConstraintsAgree(FunctionKoto a, FunctionKoto b, List<(BindingSymbol A, BindingSymbol B)> pairs)
    {
        var i = 0;
        var j = 0;
        while (true)
        {
            var x = NextPairedClause(a, ref i, pairs, first: true);
            var y = NextPairedClause(b, ref j, pairs, first: false);
            if (x is null || y is null)
            {
                return x is null && y is null;
            }

            if (!ClausesAgree(x, CallSlotOwner(a), y, CallSlotOwner(b), pairs))
            {
                return false;
            }
        }
    }

    // The next clause of the function, from `index`, that names a slot of `pairs` on its side (`first`: A's).
    private static BoundConstraint? NextPairedClause(FunctionKoto function, ref int index, List<(BindingSymbol A, BindingSymbol B)> pairs, bool first)
    {
        while (index < function.TypeConstraints.Count)
        {
            if (function.TypeConstraints[index++] is IsKoto { BoundConstraint: { } clause } && NamesPaired(clause, pairs, first))
            {
                return clause;
            }
        }

        return null;
    }

    private static bool NamesPaired(BoundConstraint? clause, List<(BindingSymbol A, BindingSymbol B)> pairs, bool first)
        => clause is not null && (NamesPaired(clause.Subject, pairs, first) || NamesPaired(clause.RequiredType, pairs, first) ||
            NamesPaired(clause.Left, pairs, first) || NamesPaired(clause.Right, pairs, first));

    private static bool NamesPaired(BoundType? type, List<(BindingSymbol A, BindingSymbol B)> pairs, bool first)
    {
        if (type is null)
        {
            return false;
        }

        foreach (var (x, y) in pairs)
        {
            if (ReferenceEquals(type.Symbol, first ? x : y) || ReferenceEquals(type.LengthExpression?.Parameter, first ? x : y))
            {
                return true;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (NamesPaired(type.Components[i], pairs, first))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ClausesAgree(BoundConstraint? a, Koto ownerA, BoundConstraint? b, Koto ownerB, List<(BindingSymbol A, BindingSymbol B)> pairs)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        return a.Kind == b.Kind && a.Mask == b.Mask && ReferenceEquals(a.Contract, b.Contract) && OperandsAgree(a, ownerA, b, ownerB, pairs);
    }

    private static bool OperandsAgree(BoundConstraint a, Koto ownerA, BoundConstraint b, Koto ownerB, List<(BindingSymbol A, BindingSymbol B)> pairs)
        => OptionalAgree(a.Subject, ownerA, b.Subject, ownerB, pairs) && OptionalAgree(a.RequiredType, ownerA, b.RequiredType, ownerB, pairs) &&
            ClausesAgree(a.Left, ownerA, b.Left, ownerB, pairs) && ClausesAgree(a.Right, ownerA, b.Right, ownerB, pairs);

    private static bool OptionalAgree(BoundType? a, Koto ownerA, BoundType? b, Koto ownerB, List<(BindingSymbol A, BindingSymbol B)> pairs)
        => a is null || b is null ? a is null && b is null : AgreeModuloSlots(a, ownerA, b, ownerB, pairs);

    // SPEC 23.1.2, 10.4 (U1.6h B1, R4's C16): the failed part an ambiguity rests on: one at which two kept candidates' FailedFit patterns
    // differ, structurally and modulo their own slots, paired across the failed parts of the pair with equal Constraints, so that a Type
    // of the part could have separated them; null when every pair of kept candidates agrees, so that no Type of it could (the ambiguity
    // is then independent and recorded).
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

    // The first failed part at which candidates `a` and `b` disagree, or, when their patterns agree at every failed part both met, the
    // first of those parts if their paired slots' Constraints differ; null when they agree.
    private Koto? DifferingFailedPart(int start, int a, int b, ReadOnlySpan<EvaluatedCandidate> candidates)
    {
        var functionA = (FunctionKoto)candidates[a].Symbol!.Declaration;
        var functionB = (FunctionKoto)candidates[b].Symbol!.Declaration;
        var pairs = this.pairedSlots;
        pairs.Clear();
        Koto? first = null;
        for (var i = start; i < this.failedFits.Count; i++)
        {
            if (this.failedFits[i].Candidate != a || this.FailedFitOf(start, b, this.failedFits[i].Part) is not { } other)
            {
                continue;
            }

            first ??= this.failedFits[i].Part;
            if (!AgreeModuloSlots(this.failedFits[i].Pattern, CallSlotOwner(functionA), other, CallSlotOwner(functionB), pairs))
            {
                return this.failedFits[i].Part;
            }
        }

        return first is not null && !ConstraintsAgree(functionA, functionB, pairs) ? first : null;
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
