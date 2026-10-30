// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Value fits (SPEC 3.8, 10.2, 15.6.1; DECISIONS BU-4): whether a value of Type `actual` fits a position of Type `expected`.
// - Responsibility: the entries a value position calls: FitsType and FitsTypeAt (a value at an expected Type, at a use whose premises and
//   Origins the fit reads), FitsStructurally(At) (the structural part of a fit, SPEC 15.6.1), FitsVerifiedTypeAt (lowering's recheck of
//   a verified contract) and CheckTypeUse, the use-site fit whose differing Origin positions, once the structure holds, become
//   obligations that ownership judges and reports at the value (AddOriginFit). Never fitting (SPEC 3.8) is decided here, at the root:
//   a value of Type Never fits every position (NeverFits); below the root the Type relation decides.
// - Inputs: the two Types, the use, and the Type relation (RelationWalk, Binding.TypeRelations.cs), which relates Never only to itself.
// - Outputs: the verdict; CheckTypeUse records the obligations of a fit whose structure holds.
// - Invariants: a fit adds no Constraint, reopens no inference and publishes nothing but CheckTypeUse's obligations; nothing below the
//   root fits by Never (a Type holding Never is an ordinary Type).
// - Prohibitions: a Type-to-Type comparison calls the relation's entries (IsSubtype, IsStructuralSubtype, IsSubtypeAt), not these.
public sealed partial class Binding
{
    /// <summary>Proves the implemented structural subtype rules without performing a value operation.</summary>
    /// <param name="actual">The supplied complete type.</param>
    /// <param name="expected">The required complete type.</param>
    /// <returns>Whether the implemented rules prove the relation.</returns>
    public static bool FitsType(BoundType actual, BoundType expected)
        => NeverFits(actual) || new RelationWalk(null, null, OriginPart.Judged).Fits(actual, expected);

    // SPEC 15.6.1: the structural part of a fit, with all Origin bindings treated as equal; only its failure is a Type mismatch. The
    // Origin part of a Function Type, a comparison of whole contracts, is still judged here (SPEC 10.7).
    internal static bool FitsStructurally(BoundType actual, BoundType expected)
        => NeverFits(actual) || new RelationWalk(null, null, OriginPart.Structural).Fits(actual, expected);

    internal bool FitsTypeAt(BoundType actual, BoundType expected, Koto use)
        => NeverFits(actual) || new RelationWalk(this, use, OriginPart.Judged).Fits(actual, expected);

    // Lowering rechecks the already verified contract without adding constraints or reopening inference.
    internal bool FitsVerifiedTypeAt(BoundType actual, BoundType expected, Koto use)
        => this.FitsTypeAt(actual, expected, use) || ((actual.CarriesAnyOrigin || expected.CarriesAnyOrigin) &&
            this.FitsStructurallyAt(actual, expected, use) &&
            new OriginFitSearch(this, use, this.formingBody, OriginFitMode.Verified).FailedOriginRelation(actual, expected, OriginVariance.Covariant) is null);

    internal bool FitsStructurallyAt(BoundType actual, BoundType expected, Koto use)
        => NeverFits(actual) || new RelationWalk(this, use, OriginPart.Structural).Fits(actual, expected);

    // SPEC 3.8: an expression of Type Never fits any expected value Type, at the root of a value position only.
    private static bool NeverFits(BoundType actual) => ReferenceEquals(actual, BoundType.Never);

    // SPEC 15.6.1: a fit whose structure holds leaves each differing Origin position as an obligation that ownership judges and reports
    // with facts at the value. A position's pending relation is recorded with the fit's whole destination, as the fit's own record is
    // (RecordOriginFitFailure; DECISIONS R4-M133): this is the one entry to the positions' obligations (CheckTypeUseCore recurses only into
    // itself, RelationWalk forms none), so the entries appended before it returns are exactly this fit's.
    private bool CheckTypeUse(BoundType actual, BoundType expected, Koto use)
    {
        var pending = this.pendingRelations.Count;
        var fits = NeverFits(actual) || this.CheckTypeUseCore(actual, expected, use, OriginVariance.Covariant);
        for (var i = pending; i < this.pendingRelations.Count; i++)
        {
            this.pendingRelations[i] = this.pendingRelations[i] with { Destination = expected };
        }

        return fits;
    }

    // `polarity` is the variance of the compared position within the fitted Type (SPEC 15.3.5), composed as RelationWalk composes it: a
    // contravariant position reverses the relation, and an invariant or unused one makes it `==`, also for every position nested in it,
    // such as the slots of a Type argument stored through `uniq/T` or read by `(T) -> i32`.
    private bool CheckTypeUseCore(BoundType actual, BoundType expected, Koto use, OriginVariance polarity)
    {
        // The relation collects open initializer bounds even beside a local region; regional fits also publish their source edges.
        if ((polarity == OriginVariance.Contravariant ?
                new RelationWalk(this, use, OriginPart.Judged).Fits(expected, actual) :
                new RelationWalk(this, use, OriginPart.Judged).Fits(actual, expected, polarity == OriginVariance.Invariant)) &&
            !HasLocalRegion(actual) && !HasLocalRegion(expected))
        {
            return true;
        }

        if (actual.Kind != expected.Kind || actual.Semantics != expected.Semantics || actual.Symbol != expected.Symbol || actual.Length != expected.Length || !ReferenceEquals(actual.LengthExpression, expected.LengthExpression) || !actual.LengthArguments.AsSpan().SequenceEqual(expected.LengthArguments) || actual.Components.Count != expected.Components.Count || actual.OriginArguments.Count != expected.OriginArguments.Count || actual.Kind == BoundTypeKind.Primitive)
        {
            return false;
        }

        if (actual.Kind == BoundTypeKind.Function)
        {
            return false; // SPEC 10.7, 15.6.1: a conversion compares whole contracts, so its failure is one record (RecordMismatch).
        }

        switch (FitOuterOrigins(actual, expected, this, use, out var longer, out var shorter))
        {
            case OuterOriginFit.Mismatch:
                return false;
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
                this.AddOriginFit(a, b, expected, use, ComposeVariance(polarity, expected.Symbol?.Schema?.Origins[i].Variance ?? OriginVariance.Invariant));
            }
        }

        for (var i = 0; i < actual.Components.Count; i++)
        {
            if (IsInvariantLayer(actual, this))
            {
                if (!this.CheckTypeUseCore(actual.Components[i], expected.Components[i], use, OriginVariance.Invariant))
                {
                    return false;
                }

                continue;
            }

            var position = actual.Kind == BoundTypeKind.Constructed && actual.Symbol?.Schema is { } schema && i < schema.GenericSlots.Count
                ? schema.GenericSlots[i].OriginVariance : OriginVariance.Covariant;
            if (!this.CheckTypeUseCore(actual.Components[i], expected.Components[i], use, ComposeVariance(polarity, position)))
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
