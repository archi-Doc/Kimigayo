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
//   holds, among them the region conjuncts its comparisons that held left in the row sink (FormComparisonRows).
// - Invariants: a fit adds no Constraint, reopens no inference and publishes nothing but CheckTypeUse's obligations; nothing below the
//   root fits by Never (a Type holding Never is an ordinary Type). CheckTypeUse walks the relation once at its root and reads each
//   part's answer from that walk (RelationWalk.FitPart), and once more at each invariant part the relation meets reversed, in the fit's
//   own orientation; a reversed part's structure does not depend on the orientation (CheckTypeUseCore's precondition). A pair of
//   composite parts the walk meets again is decomposed once (FitPart.Same), so a fit is linear in its distinct pairs.
// - Prohibitions: a Type-to-Type comparison calls the relation's entries (IsSubtype, IsSubtypeAt, FitsStructuralPart), not these.
public sealed partial class Binding
{
    // CheckTypeUse's walk's parts (RelationWalk.FitPart), read in walk order and truncated back by the root that appended them (warm 0 B).
    private readonly List<FitPart> fitParts = new();

    // CheckTypeUse's place in its walk's parts and what it has read there: the walk's next part (an index of fitParts), the facts of the
    // last part read (a range of comparisonFacts; none for a part identity discharges, which has no position and no fact) and the
    // failure of the last part read that failed; once a part's components are read, the facts and failure of its first failed one.
    private record struct FitCursor(int Next, (int From, int To) Last, ComparisonFailure Failure);

    /// <summary>Proves the implemented structural subtype rules without performing a value operation.</summary>
    /// <param name="actual">The supplied complete type.</param>
    /// <param name="expected">The required complete type.</param>
    /// <returns>Whether the implemented rules prove the relation.</returns>
    public bool FitsType(BoundType actual, BoundType expected) => NeverFits(actual) || this.IsSubtype(actual, expected);

    internal bool FitsTypeAt(BoundType actual, BoundType expected, Koto use, out ComparisonFailure failure)
    {
        this.comparisonFacts.Clear();
        this.fitRested = false;
        var walk = new RelationWalk(this, this, use, this.comparisonFacts); // No reader of region conjuncts: FitsValueAt's CheckTypeUse is.
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
        => NeverFits(actual) || new RelationWalk(this, this, use, OriginPart.Structural).Fits(actual, expected);

    // SPEC 10.1, 10.3, 10.4 step 2, 15.6.1: the one entry of every pre-selection fit (a candidate's result, argument, receiver, its
    // acquisition and adaptation): the structural part alone, every Origin binding equal inside Function Types too, so no Origin relation
    // and no implementation well-formedness (FailedSignatureWellFormedness) drops a candidate for another to win; the selected candidate's
    // own fit judges them (DECISIONS R4-M155, landing review B1).
    internal bool FitsStructuralPartAt(BoundType actual, BoundType expected, Koto use)
        => NeverFits(actual) || new RelationWalk(this, this, use, OriginPart.Everywhere).Fits(actual, expected);

    // SPEC 5.2, 3.8, 15.6.1 (C-189): normalized Type identity with Origins treated as equal: the structural part alone, no Never
    // root rule, so it is symmetric. It reads no premise; with no use scope the walk reads `use` only in InactiveOuterOrigin, which then
    // reads an adaptation selector's own scope: a use scope only narrows what it admits, so its absence can only turn "same" into
    // "different" (review N2). A reference-equal pair is Discharged by the walk itself.
    internal bool SameStructure(BoundType a, BoundType b) => new RelationWalk(this, this, null, OriginPart.Everywhere).Fits(a, b);

    // SPEC 3.8: an expression of Type Never fits any expected value Type, at the root of a value position only.
    private static bool NeverFits(BoundType actual) => ReferenceEquals(actual, BoundType.Never);

    // SPEC 10.2, 15.4.4, 15.6.1: a value at a value position: the fit at the use, or, where a local region of a body takes part, the
    // use-site fit at `value`, whose Origin positions ownership judges. `failure` is the fit's, which a caller that records it passes on.
    private bool FitsValueAt(BoundType actual, BoundType expected, Koto use, Koto value, out ComparisonFailure failure)
        => this.FitsTypeAt(actual, expected, use, out failure) || (this.RegionalAt(actual, expected, value) && this.CheckTypeUse(actual, expected, value, out _));

    // SPEC 15.6.1: a fit whose structure holds leaves each differing Origin position as an obligation that ownership judges and reports
    // with facts at the value. A position's pending relation is recorded with the fit's whole destination, as the fit's own record is
    // (RecordOriginFitFailure; DECISIONS R4-M133): this is the one entry to the positions' obligations (CheckTypeUseCore recurses only into
    // itself, RelationWalk forms none), so the entries appended before it returns are exactly this fit's.
    // `failure` is the failure of the walk at which the fit failed: the root's, or a component's that the obligations could not take.
    // The relation is walked once, at the root (PLAN G492-a (i)), and its parts (FitPart) answer each part as a walk rooted there in this
    // fit's orientation would; the facts left for a record (RecordComparisonFacts) are those of the part whose failure is the fit's.
    // The use-site fit is a reader of its comparisons' region conjuncts (a Function value's conversion at the value, a literal's element,
    // a selected call's argument): it forms as the body's rows those of its comparisons that held, also when another part failed (SPEC
    // 23.1.2). Its walks carry the row sink (comparisonRows), a reversed part's again too, whose repeated rows the obligations' set
    // takes once (AddObligation).
    private bool CheckTypeUse(BoundType actual, BoundType expected, Koto use, out ComparisonFailure failure)
    {
        var pending = this.pendingRelations.Count;
        var rows = this.comparisonRows.Count;
        failure = default;
        var fits = NeverFits(actual) || this.CheckTypeUseWalk(actual, expected, use, out failure);
        this.FormComparisonRows(rows);
        for (var i = pending; i < this.pendingRelations.Count; i++)
        {
            this.pendingRelations[i] = this.pendingRelations[i] with { Destination = expected };
        }

        return fits;
    }

    // The relation collects open initializer bounds even beside a local region; regional fits also publish their source edges. Until the
    // facts are trimmed to the last part's range, only this fit's own walks append to comparisonFacts (at its end), so a read range stays.
    private bool CheckTypeUseWalk(BoundType actual, BoundType expected, Koto use, out ComparisonFailure failure)
    {
        var facts = this.comparisonFacts;
        facts.Clear();
        this.fitRested = false;
        var cursor = default(FitCursor);
        var fits = this.CheckTypeUseParts(actual, expected, use, OriginVariance.Covariant, ref cursor);
        facts.RemoveRange(cursor.Last.To, facts.Count - cursor.Last.To);
        facts.RemoveRange(0, cursor.Last.From);
        failure = cursor.Failure;
        return fits;
    }

    // SPEC 15.3.5, 10.7: one walk of a part in this fit's orientation, decomposed: the root's, and an invariant part's that the relation
    // walks reversed (an invariant layer or slot under a contravariant one): `polarity` makes the part `==` without its orientation, and
    // a Function Type there is one oriented comparison (its per-call inputs and well-formedness are the implementation side's), so the
    // part is walked as the fit composes it, once for the part and everything in it. The walk's parts are read from its first; the
    // caller sets the cursor's next part again.
    private bool CheckTypeUseParts(BoundType actual, BoundType expected, Koto use, OriginVariance polarity, ref FitCursor cursor)
    {
        var parts = this.fitParts;
        var start = parts.Count;
        _ = new RelationWalk(this, this, use, this.comparisonFacts, parts) { Rows = this.comparisonRows }.Fits(actual, expected, polarity == OriginVariance.Invariant);
        cursor.Next = start;
        var fits = this.CheckTypeUseCore(actual, expected, use, polarity, ref cursor);
        parts.RemoveRange(start, parts.Count - start);
        return fits;
    }

    // `polarity` is the variance of the compared position within the fitted Type (SPEC 15.3.5), composed as RelationWalk composes it: a
    // contravariant position reverses the relation, and an invariant or unused one makes it `==`, also for every position nested in it,
    // such as the slots of a Type argument stored through `uniq/T` or read by `(T) -> i32`.
    // Contract: a pair identity discharges has no part; any other pair is the walk's next part (`cursor.Next`), in this fit's
    // orientation, or an invariant part the walk met reversed, which is walked once more (CheckTypeUseParts). Precondition: a reversed
    // part's structure does not depend on the orientation, so the root walk does not end inside such a part while the fit's own walk of
    // it holds; it would have entered no later part, and the decomposition would go on to components the walk never entered. A
    // structural failure ends the decomposition where it ends the walk (CheckTypeUsePart), so no part after the walk's end is read. A
    // pair that is not the walk's next part, or a read past the walk's parts, is a broken contract (InternalInvariant).
    private bool CheckTypeUseCore(BoundType actual, BoundType expected, Koto use, OriginVariance polarity, ref FitCursor cursor)
    {
        if (ReferenceEquals(actual, expected))
        {
            cursor.Last = (0, 0);
            return true;
        }

        // The walk's parts end at the list's end: a nested walk's parts are removed before the enclosing walk's next one is read.
        var part = cursor.Next < this.fitParts.Count ? this.fitParts[cursor.Next] : default;
        var (a, b) = polarity == OriginVariance.Contravariant ? (expected, actual) : (actual, expected);
        bool fits;
        if (polarity == OriginVariance.Invariant && ReferenceEquals(part.Actual, b) && ReferenceEquals(part.Expected, a))
        {
            fits = this.CheckTypeUseParts(actual, expected, use, polarity, ref cursor);
        }
        else if (ReferenceEquals(part.Actual, a) && ReferenceEquals(part.Expected, b))
        {
            cursor.Last = (part.FactStart, part.FactEnd);
            cursor.Next++;
            if (part.Same != 0)
            {
                // PLAN G492-a (ii): the walk entered this pair before (FitPart.Same), and the decomposition formed its obligations there,
                // which are this meeting's; it answers that part's decomposition (Fails), and a part whose walk failed leaves the cursor
                // as CheckTypeUsePart leaves it for a part whose walk failed. Where the earlier part's decomposition failed, its branch
                // failed first, so this part's failure and facts are never the fit's.
                if (!part.Holds)
                {
                    cursor.Failure = part.Failure;
                }

                fits = !this.fitParts[part.Same - 1].Fails;
            }
            else
            {
                var index = cursor.Next - 1;
                fits = this.CheckTypeUsePart(actual, expected, use, polarity, part, ref cursor);
                if (!fits)
                {
                    this.fitParts[index] = this.fitParts[index] with { Fails = true };
                }
            }
        }
        else
        {
            throw new InvalidOperationException($"CheckTypeUse's part `{actual}` into `{expected}` is not its walk's next part (InternalInvariant).");
        }

        cursor.Next = part.End;
        return fits;
    }

    // One part's obligations from its walk's answers: the verdict, resting and failure are the part's, and its components are the
    // walk's next parts.
    private bool CheckTypeUsePart(BoundType actual, BoundType expected, Koto use, OriginVariance polarity, in FitPart part, ref FitCursor cursor)
    {
        // SPEC 23.1.2 (PLAN G739): a walk that held by a resting position forms the rows its shape gives, and its answer stays the walk's
        // where the shape gives none (`rested`).
        var fits = part.Holds;
        var rested = fits && part.Rested;
        if (!fits)
        {
            cursor.Failure = part.Failure;
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

        // A value's fit is judged at its use, in no comparison (default).
        switch (this.FitOuterOrigins(actual, expected, this, use, out var longer, out var shorter, default))
        {
            case OuterOriginFit.Mismatch:
                return rested;
            case OuterOriginFit.Relation:
                this.AddOriginFit(longer, shorter, expected, use, polarity, SlotCondition(actual, this, use, default));
                break;
        }

        for (var i = 0; i < actual.OriginArguments.Count; i++)
        {
            var a = actual.OriginArguments[i];
            var b = expected.OriginArguments[i];
            if (!ReferenceEquals(a, b))
            {
                this.AddOriginFit(a, b, expected, use, ComposeVariance(polarity, this.OriginArgumentVariance(expected, i, this)));
            }
        }

        // SPEC 23.1.2: a part the obligations cannot take (a Function component's failed condition) is the fit's failure, the first such
        // part's (its failure and its facts); the parts after it still leave their positions as obligations, each an independent record
        // of the fit. SPEC 15.6.1: a structural failure is a Type mismatch, after which no Origin relation forms: it is the fit's failure
        // and ends the decomposition, as it ends the walk, which enters no part after it.
        var (failure, last, holds) = (cursor.Failure, cursor.Last, true);
        for (var i = 0; i < actual.Components.Count; i++)
        {
            var position = actual.Kind == BoundTypeKind.Constructed && actual.Symbol?.Schema is { } schema && i < schema.GenericSlots.Count
                ? schema.GenericSlots[i].OriginVariance : OriginVariance.Covariant;
            cursor.Failure = failure;
            if (this.CheckTypeUseCore(actual.Components[i], expected.Components[i], use, IsInvariantLayer(actual, this, use, default) ? OriginVariance.Invariant : ComposeVariance(polarity, position), ref cursor))
            {
                continue;
            }

            if (cursor.Failure.Kind == ComparisonFailureKind.Shape)
            {
                return false;
            }

            if (holds)
            {
                (failure, last, holds) = (cursor.Failure, cursor.Last, false);
            }
        }

        cursor.Failure = failure;
        cursor.Last = holds ? cursor.Last : last;
        return holds;
    }

    // One Origin position of a fit by its variance: `actual` outlives `expected` at a covariant position, the reverse at a contravariant
    // one, and `==` otherwise; `type` is the expected Type at that position, the record's destination, and `condition` the Semantics
    // case the position exists in.
    private void AddOriginFit(BoundOrigin actual, BoundOrigin expected, BoundType type, Koto use, OriginVariance variance, ulong condition = 0)
        => this.AddObligation(variance == OriginVariance.Contravariant
            ? new(BindingObligationKind.OriginOutlives, use, BindingDeadline.BodyOrigins, type, expected, actual, Condition: condition)
            : new(BindingObligationKind.OriginOutlives, use, BindingDeadline.BodyOrigins, type, actual, expected, Equality: variance != OriginVariance.Covariant, Condition: condition));
}
