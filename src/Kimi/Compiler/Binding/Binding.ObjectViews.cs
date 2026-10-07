// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private Dictionary<Koto, (BoundType Source, BoundType Target)>? objectErasureFailures;

    internal BoundType PayloadReadReference(ConversionKoto source, BoundType payload)
        => this.SharedReference(payload, this.PlaceOrigin(source));

    // SPEC 3.4.1: a field access lends the handle's payload in the access mode already checked by Binding.
    internal BoundType ObjectView(Koto source, BoundType handle, bool exclusive = false)
        => this.Reference(exclusive ? SemanticsKind.ObjUniq : SemanticsKind.ObjRef, handle.Components[0], this.PlaceOrigin(source));

    private bool RequireObjectErasure(Koto use, BoundType source, BoundType target)
    {
        if (ReferenceEquals(source, target))
        {
            return true;
        }

        var proof = this.ProveOwned(source, use);
        if (proof == ConstraintProof.Proven)
        {
            return true;
        }

        (this.objectErasureFailures ??= new(ReferenceEqualityComparer.Instance))[use] = (source, target);
        this.RequireConstraint(use, proof, this.capabilityMode);
        return false;
    }

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
            return this.Fail(conversion, BindingFailure.TypeMismatch);
        }

        if (!this.RequireObjectErasure(conversion, core, target.Components[0]))
        {
            return null;
        }

        BoundType result;
        if (ObjectTypes.HandleMode(target) is { } targetMode)
        {
            if (ObjectTypes.HandleMode(actual) is not { } actualMode || actualMode != targetMode)
            {
                return this.FailMismatch(conversion, conversion, actual, target);
            }

            if (KotoHelper.UnwrapParentheses(conversion.Left).BoundSymbol?.Kind == BindingSymbolKind.PatternCandidate)
            {
                return this.Fail(conversion, BindingFailure.InvalidAssignment);
            }

            if (IsBarePlace(conversion.Left) && this.ProveCopy(actual, conversion) != ConstraintProof.Proven)
            {
                return this.FailAcquisition(conversion, BindingFailure.TransferRequired, conversion.Left);
            }

            result = target;
        }
        else
        {
            if (!this.AdaptObjectBorrow(conversion.Left, target, actual, scope, true, out var borrowed, out _, out _))
            {
                return this.Fail(conversion, BindingFailure.InvalidAssignment);
            }

            result = this.InternType(BoundTypeKind.Semantics, null, target.Semantics, [target.Components[0]], origin: borrowed.Origin);
            if (target.Origin is not null)
            {
                if (!this.CheckTypeUse(result, target, conversion))
                {
                    return this.Fail(conversion, BindingFailure.InvalidAssignment);
                }

                result = target;
            }
        }

        Complete(conversion.Right, result);
        conversion.ConversionBinding = ConversionBinding.ObjectUpcast;
        return Complete(conversion, result);
    }
}
