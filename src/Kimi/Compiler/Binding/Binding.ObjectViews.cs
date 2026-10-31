// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly List<VariableKoto> objectErasureAliases = new();
    private Dictionary<Koto, (BoundType Source, BoundType Target)>? objectErasureFailures;

    internal readonly record struct ObjectErasureEvidence(BoundType Source, BoundType Target, FunctionKoto? Entry);

    private Dictionary<Koto, ObjectErasureEvidence>? objectErasures;

    // Evidence belongs to this operation and source snapshot, never to every value of Source.
    internal bool TryGetObjectErasure(Koto use, out ObjectErasureEvidence evidence)
    {
        evidence = default;
        return HirTables.HasCurrentBinding(use) && this.objectErasures is not null && this.objectErasures.TryGetValue(use, out evidence);
    }

    internal BoundType PayloadReadReference(ConversionKoto source, BoundType payload)
        => this.types.Reference(SemanticsKind.Ref, payload, this.PlaceOrigin(source));

    // SPEC 3.4.3: a field access lends the handle's payload in the access mode already checked by Binding.
    internal BoundType ObjectView(Koto source, BoundType handle, bool exclusive = false)
        => this.types.Reference(exclusive ? SemanticsKind.ObjUniq : SemanticsKind.ObjRef, handle.Components[0], this.PlaceOrigin(source));

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

    // SPEC 6.4: an override's self arrived through the original slot's erased View.
    // Its immutable receiver binding, local aliases and immutable captures preserve that evidence; another
    // parameter of the same Type does not. Original virtual entries have no such premise.
    private bool InheritsObjectErasure(Koto operand, BoundType source, out FunctionKoto? entry)
    {
        var mark = this.objectErasureAliases.Count;
        try
        {
            if (!this.FindObjectErasure(operand, source, out entry))
            {
                return false;
            }

            // Each proven immutable alias is a reusable fact for this binding pass, not an Owned Type premise.
            for (var i = mark; i < this.objectErasureAliases.Count; i++)
            {
                (this.objectErasures ??= new(ReferenceEqualityComparer.Instance))[this.objectErasureAliases[i]] = new(source, source, entry);
            }

            return true;
        }
        finally
        {
            this.objectErasureAliases.RemoveRange(mark, this.objectErasureAliases.Count - mark);
        }
    }

    private bool FindObjectErasure(Koto operand, BoundType source, out FunctionKoto? entry)
    {
        entry = null;
        operand = KotoHelper.UnwrapParentheses(operand);
        for (var depth = 0; operand.BoundType is { IsObjectBorrow: true } && depth <= this.nodes.Count; depth++)
        {
            if (this.TryGetObjectErasure(operand, out var inherited) && ReferenceEquals(source, inherited.Target))
            {
                entry = inherited.Entry;
                return true;
            }

            if (operand is IdentifierNameKoto { BoundSymbol: { } value } && this.ImmutableObjectAlias(value) is { InitializerKoto: { } initializer } alias &&
                initializer.BoundType is { IsObjectBorrow: true })
            {
                if (this.TryGetObjectErasure(alias, out var cached) && ReferenceEquals(source, cached.Target))
                {
                    entry = cached.Entry;
                    return true;
                }

                this.objectErasureAliases.Add(alias);
                operand = KotoHelper.UnwrapParentheses(initializer);
            }
            else if (operand is ConversionKoto { ConversionBinding: ConversionBinding.Identity or ConversionBinding.Transfer or ConversionBinding.Borrow or ConversionBinding.ObjectUpcast } conversion &&
                conversion.Left.BoundType is { IsObjectBorrow: true })
            {
                operand = KotoHelper.UnwrapParentheses(conversion.Left);
            }
            else
            {
                break;
            }
        }

        if (operand is not IdentifierNameKoto { BoundSymbol: { Kind: BindingSymbolKind.Parameter or BindingSymbolKind.Capture } receiver } ||
            receiver.MutableCapture)
        {
            return false;
        }

        // SPEC 6.4.5: lexical self as a direct base call takes it (TyCtxt.EnclosingFunction, DECISIONS BU-53); an accessor is never an override.
        var scope = this.ConstraintScope(operand);
        if (this.types.EnclosingFunction(scope) is { Declaration: FunctionKoto { IsOverride: true } lexical } &&
            this.virtualOverrides.TryGetValue(lexical, out var implementation) && ReferenceEquals(source, implementation.ImplementingType) &&
            ReferenceEquals(receiver, this.BaseReceiver(scope.Function, lexical)))
        {
            entry = lexical;
            return true;
        }

        return false;
    }

    private VariableKoto? ImmutableObjectAlias(BindingSymbol value)
    {
        for (var depth = 0; depth <= this.nodes.Count && !value.MutableCapture; depth++)
        {
            if (value is { Kind: BindingSymbolKind.Local, Declaration: VariableKoto { VariableKind: VariableKind.Let, InitializerKoto: not null } alias })
            {
                return alias;
            }

            if (value is not { Kind: BindingSymbolKind.Capture, Declaration: FunctionKoto { ClosureStorage: { } closure } })
            {
                break;
            }

            BindingSymbol? source = null;
            for (var i = 0; i < closure.Captures.Count; i++)
            {
                if (ReferenceEquals(closure.Captures[i].Environment, value))
                {
                    source = closure.Captures[i].Source;
                    break;
                }
            }

            if (source is null)
            {
                break;
            }

            value = source;
        }

        return null;
    }

    // SPEC 13.6.2: an object upcast: its Types (ObjectUpcastFits), then the handle it moves or the object it borrows (ObjectUpcastResult).
    private BoundType? BindObjectUpcast(ConversionKoto conversion, BindingScope scope, BoundType actual, BoundType target)
        => this.ObjectUpcastFits(conversion, actual, target) ? this.ObjectUpcastResult(conversion, scope, actual, target) : null;

    // Whether the target's View Target is a base of the operand's object Type whose erasure holds; false once the failure is recorded or
    // depends on an invalid declaration.
    private bool ObjectUpcastFits(ConversionKoto conversion, BoundType actual, BoundType target)
    {
        var core = actual.Components[0];
        for (var parent = core; parent is not null; parent = AdtDef.Base(parent))
        {
            if (parent.Symbol?.Declaration.BindingState == BindingState.Invalid)
            {
                Complete(conversion, null);
                return false;
            }

            if (ReferenceEquals(parent, target.Components[0]))
            {
                return this.RequireObjectErasure(conversion, conversion.Left, core, target.Components[0]);
            }
        }

        this.Fail(conversion, BindingFailure.TypeMismatch);
        return false;
    }

    // An upcast whose Types fit: the handle a Place gives up, or the object it borrows, named by BorrowFailure when the Place does not lend.
    private BoundType? ObjectUpcastResult(ConversionKoto conversion, BindingScope scope, BoundType actual, BoundType target)
    {
        BoundType result;
        if (target.HandleMode is { } targetMode)
        {
            if (actual.HandleMode is not { } actualMode || actualMode != targetMode)
            {
                return this.FailMismatch(conversion, conversion, actual, target, ComparisonFailure.Shape);
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
                return this.Fail(conversion, this.BorrowFailure(conversion.Left, scope, target.Semantics == SemanticsKind.ObjUniq));
            }

            result = this.types.Intern(BoundTypeKind.Semantics, null, target.Semantics, [target.Components[0]], origin: borrowed.Origin);
            if (target.Origin is not null)
            {
                if (!this.CheckTypeUse(result, target, conversion, out _))
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
