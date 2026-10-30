// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The Origin part of the Type relation (SPEC 8.1.2, 15.6.1): the common Type of two Types that differ only in Origins (CommonOriginType)
// and the one decision on the outer Origins of a fit (FitOuterOrigins).
public sealed partial class Binding
{
    // Only Origin restriction is inferred here. No Core conversion or common base search is
    // introduced; every invariant position stays identical and both inputs must fit the result.
    internal BoundType? CommonOriginType(BoundType left, BoundType right)
    {
        if (ReferenceEquals(left, right))
        {
            return left;
        }

        if (left.Kind == BoundTypeKind.Primitive || right.Kind == BoundTypeKind.Primitive || left.Kind != right.Kind || left.Symbol != right.Symbol || left.Semantics != right.Semantics || left.Length != right.Length ||
            !ReferenceEquals(left.LengthExpression, right.LengthExpression) || !ReferenceEquals(left.ClosureContext, right.ClosureContext) || !left.LengthArguments.AsSpan().SequenceEqual(right.LengthArguments) || left.Components.Count != right.Components.Count ||
            left.OriginArguments.Count != right.OriginArguments.Count || ((left.Origin is null) != (right.Origin is null) && !InactiveOuterOrigin(left, this, null)))
        {
            return null;
        }

        var components = this.RentTypes(left.Components.Count);
        var origins = this.originScratch.Rent(left.OriginArguments.Count);
        try
        {
            for (var i = 0; i < left.Components.Count; i++)
            {
                var covariant = !IsInvariantLayer(left, this) &&
                    !(left.Kind == BoundTypeKind.Function && i == 0) &&
                    (left.Kind != BoundTypeKind.Constructed || left.Symbol?.Schema?.GenericSlots[i].OriginVariance == OriginVariance.Covariant);
                if ((covariant ? this.CommonOriginType(left.Components[i], right.Components[i]) : ReferenceEquals(left.Components[i], right.Components[i]) ? left.Components[i] : null) is not { } part)
                {
                    return null;
                }

                components[i] = part;
            }

            for (var i = 0; i < left.OriginArguments.Count; i++)
            {
                if (!ReferenceEquals(left.OriginArguments[i], right.OriginArguments[i]) && left.Symbol?.Schema?.Origins[i].Variance != OriginVariance.Covariant)
                {
                    return null;
                }

                origins[i] = this.Meet(left.OriginArguments[i], right.OriginArguments[i]);
            }

            // SPEC 5.7 (Q-O6): differing raw Origins have the unstated common Type, never a meet.
            var origin = ReferenceEquals(left.RawOrigin, right.RawOrigin) ? left.RawOrigin : null;
            if (!InactiveOuterOrigin(left, this, null) && left.Origin is { } a)
            {
                origin = this.Meet(a, right.Origin!);
            }

            var result = this.types.Intern(left.Kind, left.Symbol, left.Semantics, components.AsSpan(0, left.Components.Count), left.Length, origin, origins.AsSpan(0, left.OriginArguments.Count), left.LengthExpression, left.ClosureContext, left.LengthArguments);
            return IsSubtype(left, result) && IsSubtype(right, result) ? result : null;
        }
        finally
        {
            this.originScratch.Return(origins, clearArray: true);
            this.typeScratch.Return(components, clearArray: true);
        }
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

    // SPEC 8.1.2: a pair's conditional outer Origin participates only in admitted borrow cases. Internal Origins remain checked.
    private static bool InactiveOuterOrigin(BoundType type, Binding? binding, Koto? use)
    {
        if (!TryAdaptationSelector(type, out var whole) || whole.Symbol is not { } selector)
        {
            return false;
        }

        binding ??= selector.Declaration.CodeContext.Compilation.Binding;
        var scope = use is null ? selector.Scope : binding.ConstraintScope(use);
        // An owner application preserves the target's borrow Origin, not the application's conditional slot.
        var semantics = type.Kind == BoundTypeKind.SemanticsAdaptation ? binding.ResultSemantics(type, scope)
            : binding.AdmittedSemantics(whole, scope);
        return (semantics & SemanticsMask.Borrow) == 0;
    }

    // SPEC 8.1.2, 15.6.1: the outer Origins of a fit's two Types of one shape. Two written Origins relate. Two pair layers of one binder
    // relate their slots, the implicit `o` of an original `s/T` included, so the original `W` fits `s/T during a` when `o` outlives `a`. A
    // pair layer that stores no slot although a borrow case is admitted is unformed: its formation failure is recorded where it is
    // declared, so the fit derives from that failure instead of adding a mismatch. Any other difference in presence is structural.
    // Without a Binding no failure is visible, so only written Origins relate and every other difference stays a mismatch.
    // RelationWalk, CheckTypeUse, CollectCallRelations and FailedOriginRelation share this one decision.
    private static OuterOriginFit FitOuterOrigins(BoundType actual, BoundType expected, Binding? binding, Koto? use, out BoundOrigin longer, out BoundOrigin shorter)
    {
        longer = shorter = null!;
        if (actual.Semantics == SemanticsKind.Raw && actual.Kind == BoundTypeKind.Semantics)
        {
            // SPEC 5.7: a raw layer's Origins relate by their regions; a missing one is Unstated, the one producer of that end (S4).
            longer = actual.RawOrigin ?? BoundOrigin.Unstated;
            shorter = expected.RawOrigin ?? BoundOrigin.Unstated;
            return ReferenceEquals(actual.RawOrigin, expected.RawOrigin) ? OuterOriginFit.None : OuterOriginFit.Relation;
        }

        if (ReferenceEquals(actual.Origin, expected.Origin) || InactiveOuterOrigin(actual, binding, use))
        {
            return OuterOriginFit.None;
        }

        if (actual.Origin is { } a && expected.Origin is { } b)
        {
            longer = a;
            shorter = b;
            return OuterOriginFit.Relation;
        }

        if (binding is null || !TryPairLayer(actual, out _, out _))
        {
            return OuterOriginFit.Mismatch;
        }

        if (binding.OuterOrigin(actual) is not { } slot || binding.OuterOrigin(expected) is not { } other)
        {
            return OuterOriginFit.None;
        }

        longer = slot;
        shorter = other;
        return OuterOriginFit.Relation;
    }

    // SPEC 15.6.5: a pair layer's slot exists only in its binder's borrow cases, whose premises the relation may use.
    private static ulong SlotCondition(BoundType actual, Binding? binding, Koto? use)
        => binding is not null && use is not null && actual.ContainsPairLayer ? RequiredCondition(binding.PairSlotCondition(actual, use)) : 0;
}
