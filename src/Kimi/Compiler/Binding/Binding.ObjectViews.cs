// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private Dictionary<Koto, (BoundType Source, BoundType Target)>? objectErasureFailures;

    internal readonly record struct ObjectErasureEvidence(BoundType Source, BoundType Target, FunctionKoto? Entry);

    private Dictionary<Koto, ObjectErasureEvidence>? objectErasures;

    // Evidence belongs to this operation and source snapshot, never to every value of Source.
    internal bool TryGetObjectErasure(Koto use, out ObjectErasureEvidence evidence)
    {
        evidence = default;
        return use.HasCurrentBinding && this.objectErasures is not null && this.objectErasures.TryGetValue(use, out evidence);
    }

    internal BoundType PayloadReadReference(ConversionKoto source, BoundType payload)
        => this.SharedReference(payload, this.PlaceOrigin(source));

    // SPEC 3.4.1: a field access lends the handle's payload in the access mode already checked by Binding.
    internal BoundType ObjectView(Koto source, BoundType handle, bool exclusive = false)
        => this.Reference(exclusive ? SemanticsKind.ObjUniq : SemanticsKind.ObjRef, handle.Components[0], this.PlaceOrigin(source));

    private bool RequireObjectErasure(Koto use, Koto operand, BoundType source, BoundType target)
    {
        if (ReferenceEquals(source, target))
        {
            return true;
        }

        var inherited = this.InheritsObjectErasure(operand, source, out var entry);
        if (entry is { BindingFailure: not BindingFailure.None })
        {
            this.CompleteDependent(use, entry);
            return false;
        }

        var proof = inherited ? ConstraintProof.Proven : this.ProveOwned(source, use);
        if (proof == ConstraintProof.Proven)
        {
            (this.objectErasures ??= new(ReferenceEqualityComparer.Instance))[use] = new(source, target, entry);
            return true;
        }

        (this.objectErasureFailures ??= new(ReferenceEqualityComparer.Instance))[use] = (source, target);
        this.RequireConstraint(use, proof, this.capabilityMode);
        return false;
    }

    // SPEC 6.2.4: an override's self arrived through the original slot's erased View.
    // Its immutable receiver binding and explicit captures preserve that evidence; another
    // parameter of the same Type does not. Original virtual entries have no such premise.
    private bool InheritsObjectErasure(Koto operand, BoundType source, out FunctionKoto? entry)
    {
        entry = null;
        operand = KotoHelper.UnwrapParentheses(operand);
        while (ObjectTypes.IsBorrow(operand.BoundType))
        {
            if (this.TryGetObjectErasure(operand, out var inherited) && ReferenceEquals(source, inherited.Target))
            {
                entry = inherited.Entry;
                return true;
            }

            if (operand is not ConversionKoto { ConversionBinding: ConversionBinding.Identity or ConversionBinding.Transfer or ConversionBinding.Borrow or ConversionBinding.ObjectUpcast } conversion ||
                !ObjectTypes.IsBorrow(conversion.Left.BoundType))
            {
                break;
            }

            operand = KotoHelper.UnwrapParentheses(conversion.Left);
        }

        if (operand is not IdentifierNameKoto { BoundSymbol: { Kind: BindingSymbolKind.Parameter or BindingSymbolKind.Capture } receiver } ||
            receiver.MutableCapture || this.ConstraintScope(operand).Function is not { } function)
        {
            return false;
        }

        var lexical = function;
        while (lexical.IsAnonymous && this.scopes[lexical].Parent?.Function is { } outer)
        {
            lexical = outer;
        }

        if (lexical.IsOverride &&
            this.virtualOverrides.TryGetValue(lexical, out var implementation) && ReferenceEquals(source, implementation.ImplementingType) &&
            ReferenceEquals(receiver, this.BaseReceiver(function, lexical)))
        {
            entry = lexical;
            return true;
        }

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

        if (!this.RequireObjectErasure(conversion, conversion.Left, core, target.Components[0]))
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
