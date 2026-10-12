// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The relations between Types and Origins that Binding owns and the MIR reads (SPEC 15.3.5, 15.6.1, 15.6.4): the one variance rule of a
// position, the one walk of a value fit, which is also MIR's structural agreement, and a callee's declared relations under a call's
// substitution. A relation is (longer, shorter, equal): the longer outlives the shorter, or the two are equal; a null end is a borrow layer
// whose Origin no Type writes (TyCtxt.Reference: the ownership analysis's to infer).
internal sealed partial class TyCtxt
{
    // SPEC 15.3.5: the variance of a Type's component within it, which Binding.ComposeVariance composes: a layer's target invariant where
    // the layer is (InvariantTarget), else covariant; a Function Type's inputs contravariant and its result covariant; a declared slot by
    // its inferred variance; any other component, owned contents, covariant.
    internal static OriginVariance ComponentVariance(BoundType type, int component, Binding? binding) => type.Kind switch
    {
        BoundTypeKind.Semantics or BoundTypeKind.SemanticsApplication when InvariantTarget(type, binding) => OriginVariance.Invariant,
        BoundTypeKind.Function when type.Components.Count == 2 && component == 0 => OriginVariance.Contravariant,
        BoundTypeKind.Constructed when type.Symbol?.Schema is { } schema && component < schema.GenericSlots.Count =>
            schema.GenericSlots[component].OriginVariance,
        _ => OriginVariance.Covariant,
    };

    // SPEC 15.3.5: whether a layer is invariant in its target: uniq, obj, objuniq and raw, and a pair layer whose binder admits one of
    // them (Binding.InvariantAdmitted), which only a Binding sees; a case Type's layers are concrete.
    internal static bool InvariantTarget(BoundType type, Binding? binding)
        => type.Semantics is SemanticsKind.Uniq or SemanticsKind.Obj or SemanticsKind.ObjUniq or SemanticsKind.Raw ||
            (binding is not null && binding.InvariantAdmitted(type));

    // SPEC 15.6.1: whether a value of one Type fits a destination of another by the structural part of the fit, the identity of SPEC 3.8
    // with every Origin binding treated as equal: equal but for the Origins and Origin arguments of either Type and its components. Interned
    // Types that carry no Origin fit only when identical; an unknown Type (null) fits any. MIR's Type agreement (MirTypes.Agree). Where it
    // fits, appends each Origin position at its variance within a position of `polarity` (Binding.ComposeVariance, ComponentVariance): a
    // covariant one as (actual, expected), a contravariant one as (expected, actual), an invariant or unused one as equal; a Function Type's
    // internals append nothing (a signature holds no Loan, SPEC 15.6.4). The position is invariant or covariant.
    internal static bool Relate(
        BoundType? actual,
        BoundType? expected,
        bool invariant,
        List<(BoundOrigin? Longer, BoundOrigin? Shorter, bool Equal)>? relations)
        => Fit(actual, expected, invariant ? OriginVariance.Invariant : OriginVariance.Covariant, relations);

    // SPEC 15.3.3, 15.6.4 step 3: the relations a function declares (an accessor's, its binder's), as a call's substitution substitutes them.
    internal void Clauses(BindingSymbol function, Subst? subst, List<(BoundOrigin? Longer, BoundOrigin? Shorter, bool Equal)> relations)
        => binding.Clauses(function, subst, relations);

    private static bool Fit(
        BoundType? actual,
        BoundType? expected,
        OriginVariance polarity,
        List<(BoundOrigin? Longer, BoundOrigin? Shorter, bool Equal)>? relations)
    {
        if (actual is null || expected is null || (ReferenceEquals(actual, expected) && relations is null))
        {
            return true;
        }

        if (!ReferenceEquals(actual, expected) && !Agrees(actual, expected))
        {
            return false;
        }

        if (relations is not null && (actual.Origin is not null || expected.Origin is not null || IsBorrowLayer(actual)))
        {
            Add(actual.Origin, expected.Origin, polarity, relations);
        }

        for (var i = 0; relations is not null && i < actual.OriginArguments.Count && i < expected.OriginArguments.Count; i++)
        {
            var slot = actual.Symbol?.Schema?.Origins[i].Variance ?? OriginVariance.Invariant;
            Add(actual.OriginArguments[i], expected.OriginArguments[i], Binding.ComposeVariance(polarity, slot), relations);
        }

        var inner = actual.Kind is BoundTypeKind.Function or BoundTypeKind.FunctionItem ? null : relations;
        for (var i = 0; i < actual.Components.Count; i++)
        {
            if (!Fit(actual.Components[i], expected.Components[i], Binding.ComposeVariance(polarity, ComponentVariance(actual, i, null)), inner))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsBorrowLayer(BoundType type)
        => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.ObjRef or SemanticsKind.ObjUniq };

    private static void Add(
        BoundOrigin? actual,
        BoundOrigin? expected,
        OriginVariance variance,
        List<(BoundOrigin? Longer, BoundOrigin? Shorter, bool Equal)> relations)
        => relations.Add(variance == OriginVariance.Contravariant ? (expected, actual, false) : (actual, expected, variance != OriginVariance.Covariant));

    // The node's own structure, modulo Origins.
    private static bool Agrees(BoundType actual, BoundType expected)
    {
        if ((!actual.CarriesOrigin && !expected.CarriesOrigin) || actual.Kind != expected.Kind || actual.Semantics != expected.Semantics ||
            !ReferenceEquals(actual.Symbol, expected.Symbol))
        {
            return false;
        }

        if (actual.Length != expected.Length || !ReferenceEquals(actual.LengthExpression, expected.LengthExpression) ||
            actual.ResultMode != expected.ResultMode)
        {
            return false;
        }

        return ReferenceEquals(actual.ClosureContext, expected.ClosureContext) && actual.Components.Count == expected.Components.Count &&
            actual.LengthArguments.AsSpan().SequenceEqual(expected.LengthArguments);
    }
}
