// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Value fits (SPEC 3.8, 10.2, 15.6.1; DECISIONS BU-4): whether a value of Type `actual` fits a position of Type `expected`.
// - Responsibility: the entries a value position calls: FitsType and FitsTypeAt (a value at an expected Type, at a use whose premises and
//   Origins the fit reads), FitsValueAt (a value at a position that a local region may reach), FitsStructurallyAt (the structural part
//   of a fit, SPEC 15.6.1) and CheckTypeUse, the use-site fit whose differing Origin positions, once the structure holds, become
//   obligations that ownership judges and reports at the value (AddOriginFit). Never fitting (SPEC 3.8) is decided here, at the root:
//   a value of Type Never fits every position (NeverFits); below the root the Type relation decides.
// - Inputs: the two Types, the use, and the Type relation (RelationWalk, Binding.TypeRelations.Walk.cs), which relates Never only to itself.
// - Outputs: the verdict and, for FitsTypeAt and CheckTypeUse, the walk's first failure (ComparisonFailure), which a caller
//   that records the failure passes on (FailMismatch, RecordMismatch); CheckTypeUse records the obligations of a fit whose structure
//   holds.
// - Invariants: a fit adds no Constraint, reopens no inference and publishes nothing but CheckTypeUse's obligations; nothing below the
//   root fits by Never (a Type holding Never is an ordinary Type).
// - Prohibitions: a Type-to-Type comparison calls the relation's entries (IsSubtype, IsSubtypeAt, FitsStructuralPart), not these.
public sealed partial class Binding
{
    // CheckTypeUse's walk's parts (RelationWalk.FitPart), read in walk order and truncated back by the root that appended them (warm 0 B).
    private readonly List<FitPart> fitParts = new();

    /// <summary>Proves the implemented structural subtype rules without performing a value operation.</summary>
    /// <param name="actual">The supplied complete type.</param>
    /// <param name="expected">The required complete type.</param>
    /// <returns>Whether the implemented rules prove the relation.</returns>
    public static bool FitsType(BoundType actual, BoundType expected) => NeverFits(actual) || IsSubtype(actual, expected);

    internal bool FitsTypeAt(BoundType actual, BoundType expected, Koto use, out ComparisonFailure failure)
    {
        this.comparisonFacts.Clear();
        this.fitRested = false;
        var walk = new RelationWalk(this, use, this.comparisonFacts);
        var fits = NeverFits(actual) || walk.Fits(actual, expected);
        failure = walk.Failure;

        // SPEC 23.1.2 (PLAN G739): a fit that holds by a resting position leaves its positions as rows, as its failing path does. The
        // query-like callers (ArgumentOperations' try-then-read, ResultPropagation's late fits, an enum's and a control-flow join's) form
        // them too, on rejected programs only.
        if (fits && this.fitRested)
        {
            _ = this.CheckTypeUse(actual, expected, use, out _);
        }

        return fits;
    }

    // SPEC 15.6.1: the structural part of a fit at the use, with all Origin bindings treated as equal; only its failure is a Type
    // mismatch. The Origin part of a Function Type, a comparison of whole contracts, is still judged here (SPEC 10.7).
    internal bool FitsStructurallyAt(BoundType actual, BoundType expected, Koto use)
        => NeverFits(actual) || new RelationWalk(this, use, OriginPart.Structural).Fits(actual, expected);

    // SPEC 10.1, 10.3, 10.4 step 2, 15.6.1: the one entry of every pre-selection fit (a candidate's result, argument, receiver, its
    // acquisition and adaptation): the structural part alone, every Origin binding equal inside Function Types too, so no Origin relation
    // and no implementation well-formedness (FailedSignatureWellFormedness) drops a candidate for another to win; the selected candidate's
    // own fit judges them (DECISIONS R4-M155, landing review B1).
    internal bool FitsStructuralPartAt(BoundType actual, BoundType expected, Koto use)
        => NeverFits(actual) || new RelationWalk(this, use, OriginPart.Everywhere).Fits(actual, expected);

    // SPEC 5.2, 3.8, 15.6.1 (C-189): normalized Type identity with Origins treated as equal: the structural part alone, no Never
    // root rule, so it is symmetric. It reads no premise; with no use scope the walk reads `use` only in InactiveOuterOrigin, which then
    // reads an adaptation selector's own scope: a use scope only narrows what it admits, so its absence can only turn "same" into
    // "different" (review N2). A reference-equal pair is Discharged by the walk itself.
    internal bool SameStructure(BoundType a, BoundType b) => new RelationWalk(this, null, OriginPart.Everywhere).Fits(a, b);

    // SPEC 3.8: an expression of Type Never fits any expected value Type, at the root of a value position only.
    private static bool NeverFits(BoundType actual) => ReferenceEquals(actual, BoundType.Never);

    // SPEC 10.2, 15.4.4, 15.6.1: a value at a value position: the fit at the use, or, where a local region of a body takes part, the
    // use-site fit at `value`, whose Origin positions ownership judges. `failure` is the fit's, which a caller that records it passes on.
    private bool FitsValueAt(BoundType actual, BoundType expected, Koto use, Koto value, out ComparisonFailure failure)
        => this.FitsTypeAt(actual, expected, use, out failure) ||
            ((this.HasLocalRegion(actual) || this.HasLocalRegion(expected)) && this.CheckTypeUse(actual, expected, value, out _));

    // SPEC 15.6.1: a fit whose structure holds leaves each differing Origin position as an obligation that ownership judges and reports
    // with facts at the value. A position's pending relation is recorded with the fit's whole destination, as the fit's own record is
    // (RecordOriginFitFailure; DECISIONS R4-M133): this is the one entry to the positions' obligations (CheckTypeUseCore recurses only into
    // itself, RelationWalk forms none), so the entries appended before it returns are exactly this fit's.
    // `failure` is the failure of the walk at which the fit failed: the root's, or a component's that the obligations could not take.
    // The relation is walked once, at the root (G492-a (i)), and its parts (FitPart) answer each part as a walk rooted there in this
    // fit's orientation would; the facts left for a record (RecordComparisonFacts) are the last part's, as that part's own walk leaves them.
    private bool CheckTypeUse(BoundType actual, BoundType expected, Koto use, out ComparisonFailure failure)
    {
        var pending = this.pendingRelations.Count;
        failure = default;
        var fits = NeverFits(actual) || this.CheckTypeUseWalk(actual, expected, use, ref failure);
        for (var i = pending; i < this.pendingRelations.Count; i++)
        {
            this.pendingRelations[i] = this.pendingRelations[i] with { Destination = expected };
        }

        return fits;
    }

    // The relation collects open initializer bounds even beside a local region; regional fits also publish their source edges. Until the
    // facts are trimmed to the last part's range, only this fit's own walks append to comparisonFacts (at its end), so a read range stays.
    private bool CheckTypeUseWalk(BoundType actual, BoundType expected, Koto use, ref ComparisonFailure failure)
    {
        var facts = this.comparisonFacts;
        facts.Clear();
        this.fitRested = false;
        var last = (From: 0, To: 0);
        var fits = this.CheckTypeUseParts(actual, expected, use, OriginVariance.Covariant, ref last, ref failure);
        facts.RemoveRange(last.To, facts.Count - last.To);
        facts.RemoveRange(0, last.From);
        return fits;
    }

    // SPEC 15.3.5, 10.7: one walk of a part in this fit's orientation, decomposed: the root's, and an invariant part's that the relation
    // walks reversed (an invariant layer or slot under a contravariant one): `polarity` makes the part `==` without its orientation, and
    // a Function Type there is one oriented comparison (its per-call inputs and well-formedness are the implementation side's), so the
    // part is walked as the fit composes it, once for the part and everything in it.
    private bool CheckTypeUseParts(BoundType actual, BoundType expected, Koto use, OriginVariance polarity, ref (int From, int To) last, ref ComparisonFailure failure)
    {
        var parts = this.fitParts;
        var start = parts.Count;
        _ = new RelationWalk(this, use, this.comparisonFacts, parts).Fits(actual, expected, polarity == OriginVariance.Invariant);
        var next = start;
        var fits = this.CheckTypeUseCore(actual, expected, use, polarity, ref next, ref last, ref failure);
        parts.RemoveRange(start, parts.Count - start);
        return fits;
    }

    // `polarity` is the variance of the compared position within the fitted Type (SPEC 15.3.5), composed as RelationWalk composes it: a
    // contravariant position reverses the relation, and an invariant or unused one makes it `==`, also for every position nested in it,
    // such as the slots of a Type argument stored through `uniq/T` or read by `(T) -> i32`. `next` is the walk's next part and `last`
    // the facts of the last part read (none for a part identity discharges, which has no position and no fact).
    private bool CheckTypeUseCore(BoundType actual, BoundType expected, Koto use, OriginVariance polarity, ref int next, ref (int From, int To) last, ref ComparisonFailure failure)
    {
        if (ReferenceEquals(actual, expected))
        {
            last = (0, 0);
            return true;
        }

        var part = this.fitParts[next];
        var (a, b) = polarity == OriginVariance.Contravariant ? (expected, actual) : (actual, expected);
        if (polarity == OriginVariance.Invariant && ReferenceEquals(part.Actual, b) && ReferenceEquals(part.Expected, a))
        {
            next = part.End;
            return this.CheckTypeUseParts(actual, expected, use, polarity, ref last, ref failure);
        }

        if (!ReferenceEquals(part.Actual, a) || !ReferenceEquals(part.Expected, b))
        {
            throw new InvalidOperationException($"CheckTypeUse's part `{actual}` into `{expected}` is not its walk's next part (G492-a (i)).");
        }

        last = (part.FactStart, part.FactEnd);
        next++;
        var fits = this.CheckTypeUsePart(actual, expected, use, polarity, part, ref next, ref last, ref failure);
        next = part.End;
        return fits;
    }

    private bool CheckTypeUsePart(BoundType actual, BoundType expected, Koto use, OriginVariance polarity, in FitPart part, ref int next, ref (int From, int To) last, ref ComparisonFailure failure)
    {
        // SPEC 23.1.2 (PLAN G739): a walk that held by a resting position forms the rows its shape gives, and its answer stays the walk's
        // where the shape gives none (`rested`).
        var fits = part.Holds;
        var rested = fits && part.Rested;
        if (!fits)
        {
            failure = part.Failure;
        }
        else if (!rested && !this.HasLocalRegion(actual) && !this.HasLocalRegion(expected))
        {
            return true;
        }

        if (!part.Shaped || actual.Kind != expected.Kind || actual.Semantics != expected.Semantics || actual.Symbol != expected.Symbol || actual.Length != expected.Length || !ReferenceEquals(actual.LengthExpression, expected.LengthExpression) || !actual.LengthArguments.AsSpan().SequenceEqual(expected.LengthArguments) || actual.Components.Count != expected.Components.Count || actual.OriginArguments.Count != expected.OriginArguments.Count || actual.Kind == BoundTypeKind.Primitive)
        {
            return rested;
        }

        if (actual.Kind == BoundTypeKind.Function)
        {
            return rested; // SPEC 10.7, 15.6.1: a conversion compares whole contracts, so its failure is one record (RecordMismatch).
        }

        switch (FitOuterOrigins(actual, expected, this, use, out var longer, out var shorter))
        {
            case OuterOriginFit.Mismatch:
                return rested;
            case OuterOriginFit.Relation:
                this.AddOriginFit(longer, shorter, expected, use, polarity, SlotCondition(actual, this, use));
                break;
        }

        for (var i = 0; i < actual.OriginArguments.Count; i++)
        {
            var a = actual.OriginArguments[i];
            var b = expected.OriginArguments[i];
            if (!ReferenceEquals(a, b))
            {
                this.AddOriginFit(a, b, expected, use, ComposeVariance(polarity, OriginArgumentVariance(expected, i, this)));
            }
        }

        for (var i = 0; i < actual.Components.Count; i++)
        {
            if (IsInvariantLayer(actual, this))
            {
                if (!this.CheckTypeUseCore(actual.Components[i], expected.Components[i], use, OriginVariance.Invariant, ref next, ref last, ref failure))
                {
                    return false;
                }

                continue;
            }

            var position = actual.Kind == BoundTypeKind.Constructed && actual.Symbol?.Schema is { } schema && i < schema.GenericSlots.Count
                ? schema.GenericSlots[i].OriginVariance : OriginVariance.Covariant;
            if (!this.CheckTypeUseCore(actual.Components[i], expected.Components[i], use, ComposeVariance(polarity, position), ref next, ref last, ref failure))
            {
                return false;
            }
        }

        return true;
    }

    // One Origin position of a fit by its variance: `actual` outlives `expected` at a covariant position, the reverse at a contravariant
    // one, and `==` otherwise; `type` is the expected Type at that position, the record's destination, and `condition` the Semantics
    // case the position exists in.
    private void AddOriginFit(BoundOrigin actual, BoundOrigin expected, BoundType type, Koto use, OriginVariance variance, ulong condition = 0)
        => this.AddObligation(variance == OriginVariance.Contravariant
            ? new(BindingObligationKind.OriginOutlives, use, BindingDeadline.BodyOrigins, type, expected, actual, Condition: condition)
            : new(BindingObligationKind.OriginOutlives, use, BindingDeadline.BodyOrigins, type, actual, expected, Equality: variance != OriginVariance.Covariant, Condition: condition));
}
