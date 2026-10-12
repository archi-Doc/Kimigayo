// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The relations between Types that Binding owns and the MIR reads: the one structural walk of a value fit (SPEC 15.6.1) and the one
// variance rule of a position (SPEC 15.3.5).
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
    // Types that carry no Origin fit only when identical; an unknown Type (null) fits any. MIR's Type agreement (MirTypes.Agree).
    internal static bool Relate(BoundType? actual, BoundType? expected)
    {
        if (ReferenceEquals(actual, expected) || actual is null || expected is null)
        {
            return true;
        }

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

        if (!ReferenceEquals(actual.ClosureContext, expected.ClosureContext) || actual.Components.Count != expected.Components.Count ||
            !actual.LengthArguments.AsSpan().SequenceEqual(expected.LengthArguments))
        {
            return false;
        }

        for (var i = 0; i < actual.Components.Count; i++)
        {
            if (!Relate(actual.Components[i], expected.Components[i]))
            {
                return false;
            }
        }

        return true;
    }
}
