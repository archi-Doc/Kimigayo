// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The Origin part of the Type relation (SPEC 8.1.2, 15.6.1): the common Type of two Types that differ only in Origins (CommonOriginType)
// and the one decision on the outer Origins of a fit (FitOuterOrigins).
public sealed partial class Binding
{
    // Only Origin restriction is inferred here. No Core conversion or common base search is
    // introduced; every invariant position stays identical and both inputs must fit the result.
    // SPEC 14.9.2, 15.6.1: a search that fails gives its failure, the only record of a failed common Type: Shape for a structural difference,
    // else the first position whose Origins have no common Origin (a presence difference at a pair layer relates its slots, an invariant
    // position or Origin argument its Origins), whose relation the fit's record judges.
    internal BoundType? CommonOriginType(BoundType left, BoundType right, out ComparisonFailure failure)
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

        // SPEC 8.1.2: a pair layer's slot is its OuterOrigin, the original's implicit `o` included (PLAN G758).
        var (leftSlot, rightSlot) = (this.OuterOrigin(left), this.OuterOrigin(right));
        if ((leftSlot is null) != (rightSlot is null) && !this.InactiveOuterOrigin(left, this, null, default))
        {
            failure = Unmet(rightSlot, leftSlot);
            return null;
        }

        var components = this.RentTypes(left.Components.Count);
        var origins = this.originScratch.Rent(left.OriginArguments.Count);
        try
        {
            for (var i = 0; i < left.Components.Count; i++)
            {
                var covariant = !IsInvariantLayer(left, this, null, default) &&
                    !(left.Kind == BoundTypeKind.Function && i == 0) &&
                    (left.Kind != BoundTypeKind.Constructed || left.Symbol?.Schema?.GenericSlots[i].OriginVariance == OriginVariance.Covariant);
                var (l, r) = (left.Components[i], right.Components[i]);
                var part = ReferenceEquals(l, r) ? l : null;
                if (covariant)
                {
                    part = this.CommonOriginType(l, r, out failure);
                }
                else if (part is null)
                {
                    failure = TyCtxt.StorageMatches(l, r) ? Unmet(null, null) : ComparisonFailure.Shape;
                }

                if (part is null)
                {
                    return null;
                }

                components[i] = part;
            }

            for (var i = 0; i < left.OriginArguments.Count; i++)
            {
                if (!ReferenceEquals(left.OriginArguments[i], right.OriginArguments[i]) && this.OriginArgumentVariance(left, i, this) != OriginVariance.Covariant)
                {
                    failure = Unmet(right.OriginArguments[i], left.OriginArguments[i]);
                    return null;
                }

                origins[i] = this.Meet(left.OriginArguments[i], right.OriginArguments[i]);
            }

            // SPEC 5.7 (Q-O6): differing raw Origins have the unstated common Type, never a meet.
            var origin = ReferenceEquals(left.RawOrigin, right.RawOrigin) ? left.RawOrigin : null;
            if (!this.InactiveOuterOrigin(left, this, null, default) && leftSlot is { } a)
            {
                origin = this.Meet(a, rightSlot!);
            }

            var result = this.types.Intern(left.Kind, left.Symbol, left.Semantics, components.AsSpan(0, left.Components.Count), left.Length, origin, origins.AsSpan(0, left.OriginArguments.Count), left.LengthExpression, left.InstanceContext, left.LengthArguments, left.Binder);
            if (this.IsSubtype(left, result) && this.IsSubtype(right, result))
            {
                return result;
            }

            failure = Unmet(right.Origin, result.Origin);
            return null;
        }
        finally
        {
            this.originScratch.Return(origins, clearArray: true);
            this.typeScratch.Return(components, clearArray: true);
        }

        static ComparisonFailure Unmet(BoundOrigin? longer, BoundOrigin? shorter) => new(ComparisonFailureKind.Position, longer, shorter, false, default);
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
    private bool InactiveOuterOrigin(BoundType type, Binding? binding, Koto? use, in CallableInstance comparison)
    {
        if (!TryAdaptationSelector(type, out var whole) || whole.Symbol is not { } selector)
        {
            return false;
        }

        binding ??= this;
        var scope = AdmittedScope(selector, binding.RelationScope(use, comparison));
        // An owner application preserves the target's borrow Origin, not the application's conditional slot.
        var semantics = type.Kind == BoundTypeKind.SemanticsAdaptation ? binding.ResultSemantics(type, scope)
            : binding.AdmittedSemantics(whole, scope);
        return (semantics & SemanticsMask.Borrow) == 0;
    }

    // SPEC 8.1.2, 15.6.1: the outer Origins of a fit's two Types of one shape, and the two ends of its relation (`longer` outlives
    // `shorter`; none unless Relation). Two written Origins relate. Two pair layers of one binder relate their slots, the implicit `o` of
    // an original `s/T` included, so the original `W` fits `s/T during a` when `o` outlives `a`. A pair layer that stores no slot although
    // a borrow case is admitted is unformed: its formation failure is recorded where it is declared, so the fit derives from that failure
    // instead of adding a mismatch. Any other difference in presence is structural.
    // It reads this Binding's premises, the binder's, whether or not the walk asking has a premises reader (PLAN G758).
    // RelationWalk, CheckTypeUse, CollectCallRelations and FailedOriginRelation share this one decision; each states the comparison
    // its relation belongs to (InactiveOuterOrigin), `default` outside any comparison. A placing fit's walk passes its raw-fit sink
    // (`fits`, Binding.ValueFits.cs' rawFits) and whether the position is invariant: at a raw layer whose Origin differs from the value's
    // that the value states, outside a Callable comparison, a candidate trial and a collected fit (FormsBorrows), the decision appends
    // the raw fit (G147 ruling 75); it is otherwise pure.
    private (OuterOriginFit Fit, BoundOrigin Longer, BoundOrigin Shorter) FitOuterOrigins(
        BoundType actual, BoundType expected, Koto? use, in CallableInstance comparison, List<RawFit>? fits = null, bool invariant = false)
    {
        if (actual.IsPointer)
        {
            // SPEC 5.7: a raw layer's Origins relate by their regions; a missing one is Unstated, the one producer of that end (S4). A raw
            // fit needs both ends stated: `null` states none (SPEC 5.1), and a layer stating none has no atom.
            var (value, target) = (actual.RawOrigin, expected.RawOrigin);
            if (ReferenceEquals(value, target))
            {
                return (OuterOriginFit.None, value ?? BoundOrigin.Unstated, target ?? BoundOrigin.Unstated);
            }

            if (fits is not null && (value, target) is ({ } source, { } holder) && comparison.Expected is null && this.FormsBorrows)
            {
                fits.Add(new(holder, source, invariant));
            }

            return (OuterOriginFit.Relation, value ?? BoundOrigin.Unstated, target ?? BoundOrigin.Unstated);
        }

        if (ReferenceEquals(actual.Origin, expected.Origin) || this.InactiveOuterOrigin(actual, this, use, comparison))
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
}
