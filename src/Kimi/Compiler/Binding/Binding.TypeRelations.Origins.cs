// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The Origin part of the Type relation (SPEC 8.1.2, 15.6.1): the common Type of a place's parts (PlaceCommonType, StatedPlaceType), the
// common supertype of two Types that differ only in Origins (CommonOriginType) and the one decision on the outer Origins of a fit
// (FitOuterOrigins).
// Contract (PLAN G1379): the place's common Type is decided here once; its readers (the array, Dictionary and element hole places, a
// waiting part's default) record its failure at the part and fit each part to it, and none decides an Origin of it. Only Origins are
// met: no Core conversion or common base search is introduced, and the walk forms no relation and no record.
public sealed partial class Binding
{
    // SPEC 4.3.1 Places, 14.9.2 step 2, 10.8, 15.3.6: the common Type of two parts whose structural parts agree. Its Origins are the place's
    // variables, each taking its candidate: the meet of the parts' Origins at a covariant position and at an invariant one (a bound a
    // part's fit gives is no proven equality, so the invariant variable takes the covariant candidate); differing raw Origins stay
    // unstated (5.7). The polarity is the canonical table's (ComposeVariance, ComponentVariance, OriginArgumentVariance), entered
    // covariant. It forms no relation: each part's use-site fit to the result does, which its reader runs (FitsValueAt, RequireAtPosition).
    // A failure is the only record of a failed place: Shape for a structural difference (a presence difference FitOuterOrigins finds
    // included), Limit for a contravariant position whose Origins differ, whose candidate (a lower bound proven to outlive the others) this
    // search does not form: the located Unsupported_Kd until the place forms it (PLAN's contravariant-candidate row, from G1379).
    internal BoundType? PlaceCommonType(BoundType left, BoundType right, QuerySite site, out ComparisonFailure failure)
        => this.PlaceCommonType(left, right, OriginVariance.Covariant, site, out failure);

    // SPEC 15.6.1: a common supertype of two Types that differ only in Origins, for a reader that needs one both fit (an equality's
    // operands, a function reference's bindings, a join): the place's common Type, which both must fit, judged once at its root, so an
    // invariant difference fails unless the premises prove its Origins equal.
    internal BoundType? CommonOriginType(BoundType left, BoundType right, QuerySite site, out ComparisonFailure failure)
    {
        var result = this.PlaceCommonType(left, right, site, out failure);
        if (result is null || (this.IsSubtype(left, result) && this.IsSubtype(right, result)))
        {
            return result;
        }

        failure = new(ComparisonFailureKind.Position, right.Origin, result.Origin, false, default);
        return null;
    }

    // How a fit treats the outer Origins of two Types of one shape (FitOuterOrigins).
    private enum OuterOriginFit : byte
    {
        // Nothing to relate: equal Origins, a slot that no admitted case stores, or an unformed pair layer.
        None,

        // `longer` outlives `shorter` in the pair slot's Semantics cases.
        Relation,

        // A difference in presence that no relation explains.
        Mismatch,
    }

    // SPEC 15.6.5: a pair layer's slot exists only in its binder's borrow cases, whose premises the relation may use; every caller
    // states the comparison the relation belongs to (PairSlotCondition), `default` outside any comparison.
    private static ulong SlotCondition(BoundType actual, Binding? binding, Koto? use, in CallableInstance comparison)
    {
        if (binding is null || use is null || !actual.ContainsPairLayer)
        {
            return 0;
        }

        var at = new UseScope(use);
        return RequiredCondition(binding.PairSlotCondition(actual, ref at, comparison));
    }

    // SPEC 8.1.2: a pair's conditional outer Origin participates only in admitted borrow cases. Internal Origins remain checked.
    private bool InactiveOuterOrigin(BoundType type, Binding? binding, Koto? use, QuerySite site, in CallableInstance comparison)
    {
        if (!TryAdaptationSelector(type, out var whole) || whole.Symbol is not { } selector)
        {
            return false;
        }

        binding ??= this;
        var scope = AdmittedScope(selector, binding.RelationScope(use, comparison));
        // An owner application preserves the target's borrow Origin, not the application's conditional slot.
        var semantics = type.Kind == BoundTypeKind.SemanticsAdaptation ? binding.ResultSemantics(type, scope, site)
            : binding.AdmittedSemantics(whole, scope);
        return (semantics & SemanticsMask.Borrow) == 0;
    }

    // SPEC 8.1.2, 15.6.1: the outer Origins of a fit's two Types of one shape, and the two ends of its relation (`longer` outlives
    // `shorter`; none unless Relation). Two written Origins relate. Two pair layers of one binder relate their slots, the implicit `o` of
    // an original `s/T` included, so the original `W` fits `s/T during a` when `o` outlives `a`. A pair layer that stores no slot although
    // a borrow case is admitted is unformed: its formation failure is recorded where it is declared, so the fit derives from that failure
    // instead of adding a mismatch. Any other difference in presence is structural.
    // It reads this Binding's premises, the binder's, whether or not the walk asking has a premises reader (PLAN G758).
    // RelationWalk, CheckTypeUse, CollectCallRelations, FailedOriginRelation and a place (PlaceOuterOrigin) share this one decision; each states the comparison
    // its relation belongs to (InactiveOuterOrigin), `default` outside any comparison. A placing fit's walk passes its raw-fit sink
    // (`fits`, Binding.ValueFits.cs' rawFits; null where the fit places no value) and whether the position is invariant: at a raw layer,
    // outside a Callable comparison, the decision appends the raw fit by its one entry rule (AddRawFit; G147 rulings 75, 76); it is
    // otherwise pure.
    private (OuterOriginFit Fit, BoundOrigin Longer, BoundOrigin Shorter) FitOuterOrigins(
        BoundType actual, BoundType expected, Koto? use, QuerySite site, in CallableInstance comparison, List<RawFit>? fits, bool invariant)
    {
        if (actual.IsPointer)
        {
            // SPEC 5.7: a raw layer's Origins relate by their regions; a missing one is Unstated, the one producer of that end (S4).
            var (value, target) = (actual.RawOrigin, expected.RawOrigin);
            if (ReferenceEquals(value, target))
            {
                return (OuterOriginFit.None, value ?? BoundOrigin.Unstated, target ?? BoundOrigin.Unstated);
            }

            if (fits is not null && comparison.Expected is null)
            {
                this.AddRawFit(fits, target, value, invariant);
            }

            return (OuterOriginFit.Relation, value ?? BoundOrigin.Unstated, target ?? BoundOrigin.Unstated);
        }

        if (ReferenceEquals(actual.Origin, expected.Origin) || this.InactiveOuterOrigin(actual, this, use, site, comparison))
        {
            return (OuterOriginFit.None, null!, null!);
        }

        if (actual.Origin is { } a && expected.Origin is { } b)
        {
            return (OuterOriginFit.Relation, a, b);
        }

        if (!TryPairLayer(actual, out var whole, out _) || whole.Symbol is null)
        {
            return (OuterOriginFit.Mismatch, null!, null!);
        }

        // As InactiveOuterOrigin: `o` is the binder's (PairOuterOrigin), interned when its binder was bound, so a walk after publication
        // reads the atom and adds none.
        return this.OuterOrigin(actual) is { } slot && this.OuterOrigin(expected) is { } other ? (OuterOriginFit.Relation, slot, other) : (OuterOriginFit.None, null!, null!);
    }

    // SPEC 4.3.1, 15.3.6 (PLAN G1379, B's ruling 1): the place's Type at an expected element Type: the parts' common Type `common`, whose
    // Origin at each position `expected` states is that stated Origin, fixed (only a variable takes a candidate); an omitted one, a
    // variable, keeps the meet. A node `expected` does not share (another kind, arity or binder, as an adapted part's) states none.
    private BoundType StatedPlaceType(BoundType common, BoundType expected)
    {
        if (!expected.CarriesOrigin || common.Kind != expected.Kind || common.Symbol != expected.Symbol || !ReferenceEquals(common.Binder, expected.Binder) ||
            common.Components.Count != expected.Components.Count || common.OriginArguments.Count != expected.OriginArguments.Count)
        {
            return common;
        }

        var components = this.RentTypes(common.Components.Count);
        var origins = this.originScratch.Rent(common.OriginArguments.Count);
        try
        {
            for (var i = 0; i < common.Components.Count; i++)
            {
                components[i] = this.StatedPlaceType(common.Components[i], expected.Components[i]);
            }

            for (var i = 0; i < common.OriginArguments.Count; i++)
            {
                origins[i] = Stated(expected.OriginArguments[i]) ?? common.OriginArguments[i];
            }

            return this.types.Rebuild(common, components.AsSpan(0, common.Components.Count), Stated(expected.Origin) ?? common.Origin, origins.AsSpan(0, common.OriginArguments.Count));
        }
        finally
        {
            this.originScratch.Return(origins, clearArray: true);
            this.typeScratch.Return(components, clearArray: true);
        }

        // SPEC 15.4.2, 15.4.4: an omitted Origin is an inference variable (a local region is a written one).
        static BoundOrigin? Stated(BoundOrigin? origin) => origin is { Kind: OriginKind.Inference } && !IsLocalRegion(origin) ? null : origin;
    }

    // The place's common Type at a position of variance `polarity` within the place's Type.
    private BoundType? PlaceCommonType(BoundType left, BoundType right, OriginVariance polarity, QuerySite site, out ComparisonFailure failure)
    {
        failure = default;
        if (ReferenceEquals(left, right))
        {
            return left;
        }

        if (left.Kind == BoundTypeKind.Primitive || right.Kind == BoundTypeKind.Primitive || left.Kind != right.Kind || left.Symbol != right.Symbol || left.Semantics != right.Semantics || left.Length != right.Length ||
            !ReferenceEquals(left.LengthExpression, right.LengthExpression) || !ReferenceEquals(left.InstanceContext, right.InstanceContext) || !left.LengthArguments.AsSpan().SequenceEqual(right.LengthArguments) || left.Components.Count != right.Components.Count ||
            left.OriginArguments.Count != right.OriginArguments.Count)
        {
            failure = ComparisonFailure.Shape;
            return null;
        }

        // SPEC 5.7 (Q-O6): differing raw Origins have the unstated common Type, never a meet.
        var origin = left.IsPointer ? (ReferenceEquals(left.RawOrigin, right.RawOrigin) ? left.RawOrigin : null) : this.PlaceOuterOrigin(left, right, polarity, site, ref failure);
        if (failure.Kind != ComparisonFailureKind.None)
        {
            return null;
        }

        var components = this.RentTypes(left.Components.Count);
        var origins = this.originScratch.Rent(left.OriginArguments.Count);
        try
        {
            for (var i = 0; i < left.Components.Count; i++)
            {
                if (this.PlaceCommonType(left.Components[i], right.Components[i], ComposeVariance(polarity, ComponentVariance(left, i, this)), site, out failure) is not { } part)
                {
                    return null;
                }

                components[i] = part;
            }

            for (var i = 0; i < left.OriginArguments.Count; i++)
            {
                if (this.PlaceCandidate(left.OriginArguments[i], right.OriginArguments[i], ComposeVariance(polarity, this.OriginArgumentVariance(left, i, this)), ref failure) is not { } met)
                {
                    return null;
                }

                origins[i] = met;
            }

            return this.types.Intern(left.Kind, left.Symbol, left.Semantics, components.AsSpan(0, left.Components.Count), left.Length, origin, origins.AsSpan(0, left.OriginArguments.Count), left.LengthExpression, left.InstanceContext, left.LengthArguments, left.Binder);
        }
        finally
        {
            this.originScratch.Return(origins, clearArray: true);
            this.typeScratch.Return(components, clearArray: true);
        }
    }

    // SPEC 8.1.2, 15.6.1: a place's outer Origin by the fit's one decision (FitOuterOrigins; a pair layer's slot, the original's implicit `o`
    // included, PLAN G758): two that relate take their candidate; nothing to relate (equal Origins, an inactive slot, or an unformed pair
    // layer, whose formation record explains it) keeps only an equal Origin; a difference in presence no relation explains is Shape.
    private BoundOrigin? PlaceOuterOrigin(BoundType left, BoundType right, OriginVariance polarity, QuerySite site, ref ComparisonFailure failure)
    {
        var (fit, longer, shorter) = this.FitOuterOrigins(left, right, null, site, default, null, false);
        if (fit == OuterOriginFit.Relation)
        {
            return this.PlaceCandidate(longer, shorter, polarity, ref failure);
        }

        failure = fit == OuterOriginFit.Mismatch ? ComparisonFailure.Shape : failure;
        return ReferenceEquals(left.Origin, right.Origin) ? left.Origin : null;
    }

    // SPEC 15.3.6: a place variable's candidate from two parts' Origins: their meet at a covariant or invariant position; at a contravariant
    // one whose Origins differ, the Limit (null).
    private BoundOrigin? PlaceCandidate(BoundOrigin left, BoundOrigin right, OriginVariance polarity, ref ComparisonFailure failure)
    {
        if (polarity == OriginVariance.Contravariant && !ReferenceEquals(left, right))
        {
            failure = new(ComparisonFailureKind.Limit, left, right, false, default);
            return null;
        }

        return this.Meet(left, right);
    }
}
