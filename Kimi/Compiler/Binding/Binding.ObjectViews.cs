// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private BoundType? BindObjectUpcast(ConversionKoto conversion, BindingScope scope, BoundType actual, BoundType target)
    {
        var core = actual.Components[0];
        var supported = false;
        for (var parent = core; parent is not null; parent = this.StoredBase(parent))
        {
            if (parent.Symbol?.Declaration.BindingState == BindingState.Invalid)
            {
                return Complete(conversion, null);
            }

            if (ReferenceEquals(parent, target.Components[0]))
            {
                supported = true;
                break;
            }
        }

        if (!supported)
        {
            return Fail(conversion, BindingFailure.TypeMismatch);
        }

        if (!ReferenceEquals(core, target.Components[0]))
        {
            var proof = this.ProveOwned(core, conversion);
            if (proof != ConstraintProof.Proven)
            {
                this.RequireConstraint(conversion, proof, this.capabilityMode);
                return null;
            }
        }

        BoundType result;
        if (ObjectTypes.IsOwner(target))
        {
            if (!ObjectTypes.IsOwner(actual) || KotoHelper.UnwrapParentheses(conversion.Left).BoundSymbol?.Kind == BindingSymbolKind.PatternCandidate)
            {
                return Fail(conversion, BindingFailure.InvalidAssignment);
            }

            result = target;
        }
        else
        {
            if (!this.AdaptObjectBorrow(conversion.Left, target, actual, scope, true, out var borrowed, out _, out _))
            {
                return Fail(conversion, BindingFailure.InvalidAssignment);
            }

            result = this.InternType(BoundTypeKind.Semantics, null, target.Semantics, [target.Components[0]], origin: borrowed.Origin);
            if (target.Origin is not null)
            {
                if (!this.CheckTypeUse(result, target, conversion))
                {
                    return Fail(conversion, BindingFailure.InvalidAssignment);
                }

                result = target;
            }
        }

        Complete(conversion.Right, result);
        conversion.ConversionBinding = ConversionBinding.ObjectUpcast;
        return Complete(conversion, result);
    }
}
