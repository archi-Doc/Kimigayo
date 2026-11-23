// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly List<VariableKoto> objectErasureAliases = new();

    internal readonly record struct ObjectErasureEvidence(BoundType Source, BoundType Target, FunctionKoto? Entry);

    private readonly ObjectErasureRows objectErasures;

    // Evidence belongs to this operation and source snapshot, never to every value of Source.
    internal bool TryGetObjectErasure(Koto use, out ObjectErasureEvidence evidence)
    {
        evidence = default;
        return this.IsCurrent(use) && this.objectErasures.TryGetValue(use, out evidence);
    }

    // SPEC 3.4.3: a field access lends the handle's payload in the access mode already checked by Binding.
    internal BoundType ObjectView(Koto source, BoundType handle, bool exclusive = false)
        => this.types.Reference(exclusive ? SemanticsKind.ObjUniq : SemanticsKind.ObjRef, handle.Components[0], this.PlaceOrigin(source));

    // SPEC 15.8.1: erasing a payload behind a view needs its complete data Type Owned. An Owned proof that read a region with no answer yet
    // (ValueIn's deferral fact, while a body is bound) is the erasure's obligation (BindingObligationKind.Owned), judged once the body is
    // bound (JudgeErasedOwned, the one deferral path; the owner's O-481 (b), R7-O2 A1+A2's D2): the view's evidence stands until then.
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

        var held = this.readsUnboundedRegion;
        this.readsUnboundedRegion = false;
        var proof = inherited ? ConstraintProof.Proven : this.ProveOwned(source, use);
        var deferred = proof == ConstraintProof.Unknown && this.readsUnboundedRegion && this.formingBody.IsBody;
        this.readsUnboundedRegion = held;
        if (proof == ConstraintProof.Proven || deferred)
        {
            this.objectErasures[use] = new(source, target, entry);
            if (deferred)
            {
                this.AddObligation(new(BindingObligationKind.Owned, use, BindingDeadline.BodyOrigins, source));
            }

            return true;
        }

        // SPEC 15.2.3: the record keeps the proposition `source is Owned` (its Constraint fact), whose subject and Contract it shows.
        this.RequireConstraint(use, proof, this.capabilityMode, proposition: this.InternConstraint(new(ConstraintKind.Contract, source, contract: this.Library.Owned)));
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
                this.objectErasures[this.objectErasureAliases[i]] = new(source, source, entry);
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

        // SPEC 6.4.5: lexical self as a direct base call takes it (TyCtxt.EnclosingDeclaration, DECISIONS BU-53); an accessor is never an override.
        var scope = this.ConstraintScope(operand);
        if (this.types.EnclosingDeclaration(scope) is { Declaration: FunctionKoto { IsOverride: true } lexical } &&
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
        for (var parent = core; parent is not null; parent = AdtDef.Base(this.types, parent))
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

        this.RecordTypeMismatch(conversion, conversion, actual, target);
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
                return this.Record(conversion, BindingFailure.InvalidAssignment, []);
            }

            if (this.IsBarePlace(conversion.Left) && this.ProveCopy(actual, this.ConstraintScope(conversion), conversion) != ConstraintProof.Proven)
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
                if (!this.CheckTypeUse(result, target, conversion, out var failure))
                {
                    return failure.IsCondition ? this.RecordMismatch(conversion, conversion, result, target, failure)
                        : this.Record(conversion, BindingFailure.InvalidAssignment, []);
                }

                result = target;
            }
        }

        Complete(conversion.Right, result);
        conversion.ConversionBinding = ConversionBinding.ObjectUpcast;
        return Complete(conversion, result);
    }

    /// <summary>The `objectErasures` family's one accessor (DECISIONS BU-220): a pass family, an operation's or an immutable alias's
    /// erased-view evidence kept in its node's module's NodeColumns at its SyntaxId; absent after a pass until the column's first write.
    /// Its members keep the names its readers use.</summary>
    private readonly struct ObjectErasureRows(Binding binding)
    {
        internal ObjectErasureEvidence this[Koto use]
        {
            set => HirTables.Of(use).Families.ObjectErasures.Ref(use.SyntaxId, binding) = value;
        }

        // A present row's Source is a Type, so the column's default, an absent row, has none.
        internal bool TryGetValue(Koto use, out ObjectErasureEvidence evidence)
            => (evidence = use.SyntaxOwner is { } table ? table.Hir.Families.ObjectErasures.Get(use.SyntaxId, binding) : default).Source is not null;
    }
}
