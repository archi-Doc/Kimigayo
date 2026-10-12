// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// The relations between Types that Binding owns and the MIR reads: the one structural walk of a value fit (SPEC 15.6.1).
internal sealed partial class TyCtxt
{
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
