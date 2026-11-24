// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402, CS1591 // Shared operation vocabulary.

public enum ArgumentAdaptation : byte
{
    Exact,

    // SPEC 10.4 step 1, 10.2 (PLAN G924; the class Expected-Type fitting): a part checked against its expected Type by value, an
    // untyped literal, a literal-only expression, a literal form or a value join; at `ref/U` its one temporary is CrossSemanticsBorrow.
    ExpectedTypeFitting,
    SameSemanticsReborrow,
    CrossSemanticsBorrow,

    // SPEC 10.2: two erasures are equal; erasure and any other row are incomparable, not numerically ranked.
    Erasure,
}

public enum ArgumentOperationKind : byte
{
    Value,
    Borrow,
    Reborrow,
    BaseBorrow,
    StorageProjection,
    PayloadProjection,
    CopyRead,
    ReferenceRead,
}

/// <summary>An implicit operation of the common adaptation (SPEC 10.2) selected at a fixed expected Type.</summary>
public enum ExpectedAdaptationKind : byte
{
    /// <summary>The Scalar or Unit referent at the end of the reference layers is read (SPEC 3.5.3).</summary>
    ReferentRead,

    /// <summary>A readable owned Place is shared-borrowed.</summary>
    SharedBorrow,

    /// <summary>A single uniq layer is Reborrowed in the expected mode.</summary>
    Reborrow,

    /// <summary>Several reference layers yield one reference to their final referent: shared under SPEC 10.2, or exclusive when a
    /// receiver is selected through exclusive layers only (SPEC 3.4.3).</summary>
    ReferenceRead,
}

/// <summary>The one recorded adaptation of an expression and the Type it supplies; the node keeps its own Type.</summary>
public readonly record struct BoundAdaptation(ExpectedAdaptationKind Kind, BoundType Type);

/// <summary>A selected operation. Source retains the original storage/Loan anchor; substitution never rewrites it. AdaptedType is
/// the Type the adaptation supplies, whose Origin relations to ParameterType are judged after selection (SPEC 15.6.1). MissingOrigin
/// marks a known call signature whose fit only one of its own per-call Origins would satisfy, reported after selection (SPEC 15.3.6).
/// Positions: a Tuple or array literal argument's element positions' operations in the fit's order, recursively (SPEC 10.5.1; ranked
/// by their classes, 10.4 step 1), a range of the candidate evaluation's positionOperations; Adaptation is then the literal's own fit.
/// ValueAtParameter marks a SourceType and AdaptedType its fit derived from ParameterType (a composite checked against its expected
/// Type, ExpectedValueType: its value Type and, at `ref/U`, its owner temporary's borrow), which the call's completion derives again
/// from the completed ParameterType (SPEC 10.2 row 2, 15.6.4 step 1); every other SourceType is the source's own Type and is never
/// rewritten.</summary>
public readonly record struct BoundArgumentOperation(Koto? Source, BoundType? SourceType, BoundType? ParameterType, ArgumentOperationKind Kind, ArgumentAdaptation Adaptation, BoundMemberPath? BasePath = null, int ParameterIndex = -1, ConstraintProof ObjectCompatibility = ConstraintProof.Proven, BoundType? AdaptedType = null, bool MissingOrigin = false, Range Positions = default, bool ValueAtParameter = false);

public sealed partial class Binding
{
    private readonly ScratchBuffers<BoundArgumentOperation> argumentOperationScratch = new();
    private readonly ReceiverOperationRows receiverOperations;
    private readonly AdaptationRows adaptations;

    /// <summary>Gets whether an expression of Type <c>ref/T</c> or <c>uniq/T</c> is read as its Copy referent where a <c>T</c> is expected (SPEC 3.5.3).</summary>
    /// <param name="node">The bound expression; its BoundType remains the reference Type.</param>
    /// <returns>Whether the expression's value is the copied referent.</returns>
    public bool ReadsReferent(Koto node) => this.adaptations.TryGetValue(node, out var adaptation) && adaptation.Kind == ExpectedAdaptationKind.ReferentRead;

    /// <summary>Gets the implicit adaptation selected for an expression at its fixed expected Type (SPEC 10.2).</summary>
    /// <param name="node">The expression.</param>
    /// <param name="adaptation">The operation and the Type it supplies.</param>
    /// <returns>Whether the expression is adapted rather than acquired as it is.</returns>
    public bool TryGetAdaptation(Koto node, out BoundAdaptation adaptation) => this.adaptations.TryGetValue(node, out adaptation);

    /// <summary>Gets a selected receiver/storage operation, including an unresolved projected-use proof obligation. Its reader,
    /// InheritedReceiverBindingTest, observes the operation selected for an inherited member (its base path, object compatibility,
    /// kind, adaptation, source and Origins): the HIR has only its adaptation (HirAdjust), and for a member access or a call that
    /// selects no candidate no other table holds it (a resolved call's is CallPlan.ReceiverOperation).</summary>
    /// <param name="use">The call or member access in the current binding pass.</param>
    /// <param name="operation">The selected operation; an Unknown proof is not permission to execute.</param>
    /// <returns>Whether this pass selected an operation for the use; a node of the frozen library is refused (EnsurePassNode: the
    /// freeze released the library pass's rows, so the refusal is the answer).</returns>
    public bool TryGetReceiverOperation(Koto use, out BoundArgumentOperation operation)
    {
        EnsurePassNode(use, nameof(this.TryGetReceiverOperation));
        return this.receiverOperations.TryGetValue(use, out operation);
    }

    internal BoundType PreparedBorrowType(Koto source, BoundType parameter)
        => this.types.Rebuild(parameter, [parameter.Components[0]], this.PreparedOrigin(source), []);

    /// <summary>
    /// SPEC 10.2: safe value-reference layers ending in <paramref name="referent"/> yield one shared reference to it, the layers
    /// walked as <see cref="LayersTerminal"/> walks them (ref, uniq and qualifying pair layers, SPEC 3.4.3). A <c>ref</c> layer is
    /// Copied with its own Origin, so it restarts the dependency; each <c>uniq</c> layer below it is shared-Reborrowed and meets its
    /// Origin into the result; a pair layer does either by its admitted cases (MeetLayers, SPEC 13.5.5). Its layer count also serves the
    /// exclusive mode of the same row (AdaptInput's through-layers arm, ReceiverThroughLayers; SPEC 7.3.3), which forms its own
    /// <c>uniq</c> reference from it.
    /// </summary>
    /// <param name="actual">The complete Type of the value.</param>
    /// <param name="referent">The referent the expected <c>ref</c> Type names.</param>
    /// <param name="scope">The scope whose premises give a pair layer's admitted set.</param>
    /// <param name="layers">The number of reference layers followed, or zero when the layers do not end in the referent.</param>
    /// <returns>The shared reference Type, or null.</returns>
    internal BoundType? SharedReferenceThroughLayers(BoundType actual, BoundType referent, BindingScope scope, out int layers)
    {
        layers = 0;
        for (var type = actual; this.LayerBelow(type, scope) is { } below; type = below)
        {
            layers++;
            this.termWalks++;
            if (ReferenceEquals(below, referent))
            {
                var walk = default(ContainmentWalk);
                this.MeetLayers(actual, layers, ref walk); // SPEC 15.6.3: the layers' Origins, up to the innermost shared one.
                return this.types.Intern(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [referent], origin: this.WalkOrigin(walk));
            }
        }

        layers = 0;
        return null;
    }

    private static bool IsTransfer(Koto source) => KotoHelper.UnwrapParentheses(source) is ConversionKoto { ConversionBinding: ConversionBinding.Transfer };

    private ConstraintProof ProjectedReceiverProof(BindingSymbol implementation, BoundAccessor? accessor = null)
        // SPEC 12.7.1: a shared receiver grants only read/shared-borrow effects.
        // Exclusive receivers still require the receiver-preservation fixed point.
        => this.DeclarationFailed(implementation.Declaration) || (accessor?.Declaration is { } declaration && this.DeclarationFailed(declaration)) ? ConstraintProof.Error
            : (accessor?.Receiver?.Semantics ?? ReceiverShape(implementation)) is SemanticsKind.Ref or SemanticsKind.ObjRef ? ConstraintProof.Proven : ConstraintProof.Unknown;

    // Set while an input is adapted (AdaptInput's out-channel): the reason an otherwise fitting bare Place was not applicable, which a
    // candidate trial takes into its row (EvaluatedCandidate.Acquisition), so a call without applicable candidates names the required
    // spelling (SPEC 15.1.5), the Place that needs it and, for an exclusive borrow, whether the Place is an object handle (@objuniq)
    // (SPEC 23.1.3).
    private AcquisitionFailure? acquisitionFailure;

    private bool RequireAcquisition(Koto source, BoundType actual, BoundType expected, BindingFailure failure, bool @object, bool fixedExpectation, bool deferAcquisition)
    {
        if (fixedExpectation || (!deferAcquisition && !this.FitsStructuralPartAt(actual, expected, source)))
        {
            return false;
        }

        this.acquisitionFailure = new(source, failure, @object);
        return deferAcquisition;
    }

    private readonly record struct AcquisitionFailure(Koto Place, BindingFailure Kind, bool Object);

    // SPEC 10.2: fixed destinations and candidate result filtering use the argument adaptation rules. A probe
    // records no pair-follow plan; the selected destination records the one operation consumed by later phases.
    private BoundAdaptation? ExpectedAdaptation(Koto node, BoundType actual, BoundType expected, bool recordPair = true)
    {
        if (IsTransfer(node) && actual is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.ObjRef or SemanticsKind.ObjUniq })
        {
            return null; // SPEC 10.2: a transferred reference is not corrected by a later adaptation.
        }

        if (!this.AdaptInput(node, FixedPattern.Open(expected), actual, this.ConstraintScope(node), null, null, out var adapted, out _, out var operation, fixedExpectation: true, recordPair: recordPair) ||
            !this.FitsStructuralPartAt(adapted, expected, node))
        {
            return null;
        }

        if (recordPair)
        {
            _ = this.FailStaticBorrow(node, operation, adapted.Semantics); // The selected destination's Borrow; a probe adapts a call result.
        }

        return operation switch
        {
            ArgumentOperationKind.CopyRead => new(ExpectedAdaptationKind.ReferentRead, adapted),
            ArgumentOperationKind.Borrow => new(ExpectedAdaptationKind.SharedBorrow, adapted),
            ArgumentOperationKind.Reborrow => new(ExpectedAdaptationKind.Reborrow, adapted),
            ArgumentOperationKind.ReferenceRead => new(ExpectedAdaptationKind.ReferenceRead, adapted),
            _ => null,
        };
    }

    // SPEC 3.5.3 value read: the safe value-reference layers of an operand are followed to their terminal Type, which is
    // read only when it is a read Type; any other referent, Copy or not, is never read implicitly. The node keeps its
    // reference Type.
    private BoundType? ReadReferent(Koto node, BoundType? type)
    {
        if (this.ReadTypeReferent(type, node) is not { } read)
        {
            return type;
        }

        // A qualifying pair layer is one of the followed layers (SPEC 3.4.3), also below another one (s/(t/U)): the read is
        // recorded as the implicit follow of the outer layer, and each instance loads through the layers it has.
        if (TryPairLayer(type, out _, out _))
        {
            this.implicitPairFollows[node] = this.FollowablePair(type!, this.ConstraintScope(node), out _) ?? SemanticsMask.None; // LayersTerminal followed it.
        }

        this.adaptations[node] = new(ExpectedAdaptationKind.ReferentRead, read);
        return read;
    }

    // The type an argument presents to adaptation: a node already read where its parameter Type was
    // expected adapts from its own reference Type, so the plan records the Copy read once; an erased source presents its own Type,
    // which its acquisition is judged on (rule E, DECISIONS BU-83).
    private BoundType ArgumentType(Koto source, BoundType actual)
        => this.ReadsReferent(source) || source.ErasedFunctionType is not null ? source.BoundType ?? actual : actual;

    // SPEC 3.5, 10.1: whether a bare input is acquired by value. A bare Place never Moves, so a Place whose bare acquisition of `judged`,
    // the Type it stores, needs a Move is not, unless its acquisition is required on `arriving`, the Type that arrives at the parameter.
    private bool AcquiresBare(Koto source, BoundType judged, BoundType arriving, BoundType pattern, bool deferAcquisition)
        => judged.Semantics is not (SemanticsKind.Owner or SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc) || !this.IsBarePlace(source) ||
            this.BareAcquire(judged, source, out _) != HirAcquire.Move ||
            this.RequireAcquisition(source, arriving, pattern, BindingFailure.TransferRequired, @object: false, fixedExpectation: false, deferAcquisition);

    /// <summary>
    /// Adapts an input to a parameter Type. <paramref name="receiver"/> marks a Receiver Expression, which SPEC 7.3
    /// acquires implicitly: a new exclusive borrow of an owned Place or temporary needs no spelling there, whereas
    /// every other position requires <c>@uniq</c>/<c>@objuniq</c> whatever the access path (SPEC 15.1.5).
    /// </summary>
    private bool AdaptInput(Koto source, FixedPattern fixedPattern, BoundType actual, BindingScope scope, BoundMemberPath? path, BoundType? declaringType, out BoundType adapted, out ArgumentAdaptation quality, out ArgumentOperationKind kind, bool explicitBorrow = false, bool receiver = false, bool fixedExpectation = false, bool recordPair = true, bool deferAcquisition = false)
    {
        var pattern = fixedPattern.Type;

        // Rule W (DECISIONS BU-79): a Receiver Expression whose whole use its special receiver's row refuses carries BindCall's record,
        // so its lending and bare acquisition are not judged again; its Types are formed as for a lent Place (SPEC 7.3.2).
        var judged = !(receiver && RefusedReceiver(source));
        if (receiver && this.adaptations.TryGetValue(source, out var preparedReceiver) && preparedReceiver.Kind == ExpectedAdaptationKind.ReferenceRead)
        {
            actual = preparedReceiver.Type; // Member selection already prepared the reference reached through the stored layers.
        }

        actual = this.ContractType(actual, scope);
        adapted = actual;
        quality = ArgumentAdaptation.Exact;
        kind = ArgumentOperationKind.Value;
        if (ReferenceEquals(actual, BoundType.Never) && !explicitBorrow)
        {
            return true; // A noncompleting argument forms no borrow or reference value; an explicit borrow names a Place (IsNeverValue).
        }

        if (source.ErasedFunctionType is { } erased)
        {
            // SPEC 7.7.4, 3.5, 10.2 (rule E, DECISIONS BU-83): an erasure is its node's one implicit adaptation. Binding erased the source at
            // its position, so the input is the erased Function Type. Erasure acquires the source ordinarily: its bare acquisition is judged
            // once, here, on its own Type (published as its Adjust, ChildAdjust and BareOf); the erased value is a fresh one.
            adapted = erased;
            quality = ArgumentAdaptation.Erasure;
            return fixedExpectation || this.AcquiresBare(source, source.BoundType ?? erased, erased, pattern, deferAcquisition);
        }

        if (pattern.IsObjectBorrow && TryPairLayer(actual, out var whole, out _) && this.ObjectCases(actual, scope, out _) is var layerCases && layerCases != SemanticsMask.None)
        {
            // SPEC 10.2, 8.14, 8.15.2: an object pair layer is adapted to a fixed object borrow by this rule in each of its cases, on the
            // case's complete Type: a case that does not adapt fails the layer, and a case that needs a spelling records it
            // (RequireAcquisition), as its concrete twin does. Where every case adapts, the adaptation differs by case (objref Copies,
            // objuniq Reborrows, a handle lends), which no plan forms yet (PLAN G762): the located limit, the call's one obstacle as an
            // acquisition's; a case's spelling, then an earlier input's record, take precedence. Its Type is a case's, for the fit.
            var before = this.acquisitionFailure;
            this.acquisitionFailure = null;
            foreach (var mode in SemanticsOrder)
            {
                if (layerCases.Contains(mode))
                {
                    ReadOnlySpan<PairCase> selected = [new(whole.Symbol!, mode)];
                    if (!this.AdaptInput(source, fixedPattern, this.CaseType(actual, selected), scope, path, declaringType, out adapted, out _, out _, explicitBorrow, receiver, fixedExpectation, false, deferAcquisition) ||
                        !this.FitsStructuralPartAt(adapted, pattern, source))
                    {
                        this.acquisitionFailure ??= before;
                        return false;
                    }
                }
            }

            if (fixedExpectation)
            {
                this.acquisitionFailure ??= before;
                return false;
            }

            this.acquisitionFailure ??= before ?? new(source, BindingFailure.Unsupported, true);
            return deferAcquisition;
        }

        if (!receiver && IsTransfer(source) && actual is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.ObjRef or SemanticsKind.ObjUniq } &&
            ((pattern.Kind == BoundTypeKind.Semantics && !TyCtxt.StorageMatches(actual, pattern)) || pattern.Kind == BoundTypeKind.Primitive))
        {
            // SPEC 10.2: an explicit @move runs first and is not corrected by a later adaptation, so a transferred
            // reference is passed only to an expectation of its own Type.
            return false;
        }

        var projected = path is not null;
        if (pattern.IsObjectBorrow)
        {
            if (!this.AdaptObjectBorrow(source, pattern, actual, scope, explicitBorrow, out adapted, out quality, out kind, receiver, fixedExpectation, deferAcquisition))
            {
                return false;
            }

            if (projected)
            {
                // Selection fixes the View Target; Owned erasure is an obligation after selection, never a ranking rule.
                adapted = this.types.Reference(pattern.Semantics, path!.Type, adapted.Origin);
                quality = ArgumentAdaptation.CrossSemanticsBorrow;
                kind = ArgumentOperationKind.BaseBorrow;
            }

            return true;
        }

        if (pattern.SafeReferent is null)
        {
            if (projected)
            {
                return false; // An owning receiver cannot acquire a sliced base.
            }

            // SPEC 10.2: where the reference does not fit but its Copy referent does, the referent is read.
            if (!this.FitsTypeAt(actual, pattern, source, out _, null) && this.ReadTypeReferent(actual, scope, source) is { } read &&
                this.FitsTypeAt(read, pattern, source, out _, null))
            {
                adapted = read;
                quality = ArgumentAdaptation.CrossSemanticsBorrow;
                kind = ArgumentOperationKind.CopyRead;
            }
            else if (!receiver && this.StoresExclusiveReference(source, actual) && this.BareReborrowLayer(actual, source) is { } layer)
            {
                // SPEC 3.5.1, 8.14, 10.1 step 1 (DECISIONS BU-27; PLAN G757): a bare Place whose plan Reborrows, a stored `uniq/T` or
                // `objuniq/T` or a pair layer in an exclusive case, is Reborrowed in its own Semantics before inference, so a parameter that
                // is no reference binds the Reborrow's Type; the path is walked only then (ReachOf). A receiver is taken by its call's
                // receiver operation (TryPairReceiver, SPEC 7.3), never by bare acquisition.
                adapted = this.ReborrowedType(actual, layer, this.ReachOf(source));
                quality = ArgumentAdaptation.SameSemanticsReborrow;
                kind = ArgumentOperationKind.Reborrow;
            }
            else if (judged && !fixedExpectation && !this.AcquiresBare(source, actual, actual, pattern, deferAcquisition))
            {
                return false; // The candidate that needs the Copy is inapplicable.
            }

            return true;
        }

        // A comparison's Copy read is a temporary value, even though its syntax retains ref/T.
        // Borrow that snapshot; borrowing the original storage would change left-to-right semantics.
        if (!projected && pattern.Semantics == SemanticsKind.Ref && this.ReadsReferent(source) &&
            ScalarReferent(actual) is { } snapshot && this.FitsTypeAt(snapshot, pattern.Components[0], source, out _, null))
        {
            adapted = this.PreparedBorrowType(source, pattern);
            quality = ArgumentAdaptation.CrossSemanticsBorrow;
            kind = ArgumentOperationKind.Borrow;
            return true;
        }

        // SPEC 7.3, 9.6.1, 10.2: a receiver through a pair layer, projected to the base declaring its member as every receiver is, or one
        // shared reference to its target at a fixed ref/U; an exclusive borrow of an owned argument keeps its explicit spelling (SPEC 15.1.5).
        if ((receiver || pattern.Semantics == SemanticsKind.Ref) && this.TryPairReceiver(source, pattern, actual, scope, projected ? declaringType : null, out adapted, out kind, recordPair))
        {
            quality = ArgumentAdaptation.CrossSemanticsBorrow;
            return true;
        }

        // A shared receiver can inspect an open payload or its selected base without granting replacement authority.
        // The selected implementation still supplies the per-operation proof before the call is accepted.
        if (receiver && pattern.Semantics == SemanticsKind.Ref && declaringType is not null &&
            this.ObjectCases(actual, scope, out var view) is var objects && objects != SemanticsMask.None && ReferenceEquals(pattern.Components[0], declaringType) &&
            (projected || ReferenceEquals(view, declaringType)) &&
            ((objects & SemanticsMask.Object) == 0 || this.LendsHandle(source, this.LendFold(source, scope, false), out _)))
        {
            adapted = this.types.Reference(SemanticsKind.Ref, declaringType, this.PlaceOrigin(source));
            quality = ArgumentAdaptation.CrossSemanticsBorrow;
            kind = projected ? ArgumentOperationKind.BaseBorrow : ArgumentOperationKind.PayloadProjection;
            return true;
        }

        if (!projected && declaringType is not null && this.TryPayloadProjection(source, pattern, actual, scope, out adapted))
        {
            quality = ArgumentAdaptation.CrossSemanticsBorrow;
            kind = ArgumentOperationKind.PayloadProjection;
            return true;
        }

        var target = pattern.Semantics;
        BoundType referent;
        if (explicitBorrow && actual.Kind == BoundTypeKind.Semantics && actual.Semantics != SemanticsKind.Owner && ReferenceEquals(pattern.Components[0], actual))
        {
            // SPEC 13.5.6: an explicit @ref/@uniq on a slot storing a reference or handle borrows that slot and
            // adds one layer; a temporary reference value is materialized first (SPEC 3.6).
            // A getter result is lent only shared (SPEC 11.5), as every Temporary Value is admitted (BorrowableTemporary).
            if (!this.BorrowablePlace(source, scope, target == SemanticsKind.Uniq) && !this.BorrowableTemporary(source, target == SemanticsKind.Uniq, true, fixedExpectation))
            {
                return false;
            }

            referent = actual;
            quality = ArgumentAdaptation.CrossSemanticsBorrow;
            kind = ArgumentOperationKind.Borrow;
            adapted = this.types.Intern(BoundTypeKind.Semantics, null, target, [referent], origin: this.SlotOrigin(source));
            return true;
        }

        if (actual.SafeReferent is { } actualReferent)
        {
            if (!explicitBorrow && ReferenceEquals(pattern.Components[0], actual))
            {
                return false; // SPEC 10.2: no implicit borrow of a reference slot.
            }

            if (!explicitBorrow && !projected && (target == SemanticsKind.Ref || (receiver && target == SemanticsKind.Uniq && BelowLayer(actualReferent) is not null)) &&
                this.SharedReferenceThroughLayers(actual, pattern.Components[0], scope, out var layers) is { } shared &&
                (target == SemanticsKind.Uniq ? layers > 1 && this.ExclusivelyAcquirable(source) : layers > 1 || actual.Semantics == SemanticsKind.Uniq))
            {
                // SPEC 10.2, 7.3.3, 15.6.3: safe value-reference layers ending in the parameter's referent yield one reference to it,
                // contained in the layers and, unless one is shared, in those that reach the source: shared at a fixed ref/U (10.2's row);
                // exclusive for a Receiver Expression below several layers whose path and layers grant it (7.3.3's p@follow@uniq on the
                // path of 3.4.3; ExclusivelyAcquirable, the mode ReceiverThroughLayers' recorded read takes). One stored uniq layer at
                // uniq/U is the Reborrow below (PLAN G147 (2) (c)). The walk's Origin is the shared mode's fallback alone: the exclusive
                // mode dereferences a concrete uniq top layer, so ReferentOrigin answers there.
                adapted = this.types.Reference(target, pattern.Components[0], this.ReferentOrigin(source, actual, layers) ?? shared.Origin);
                quality = ArgumentAdaptation.CrossSemanticsBorrow;
                kind = layers > 1 ? ArgumentOperationKind.ReferenceRead : ArgumentOperationKind.Reborrow;
                return true;
            }

            if (target == SemanticsKind.Uniq && (actual.Semantics != SemanticsKind.Uniq || this.ReachedThroughShared(source)))
            {
                // SPEC 3.5.1: a stored uniq reached through a shared path has no Reborrow, so its bare acquisition needs the Move a bare
                // Place never makes (TransferRequired, DECISIONS BU-45), deferred as every bare acquisition's; a written one is its own.
                return actual.Semantics == SemanticsKind.Uniq && !explicitBorrow && !receiver &&
                    this.RequireAcquisition(source, actual, pattern, BindingFailure.TransferRequired, @object: false, fixedExpectation, deferAcquisition);
            }

            referent = actualReferent;
            if (actual.Semantics == SemanticsKind.Ref && !projected)
            {
                // An explicit shared string borrow (text@ref) is prepared at the call like the implicit one (SPEC 22.1).
                if (ReferenceEquals(referent, BoundType.String) && KotoHelper.UnwrapParentheses(source) is ConversionKoto { ConversionBinding: ConversionBinding.Borrow })
                {
                    kind = ArgumentOperationKind.Borrow;
                }

                return true;
            }

            quality = actual.Semantics == target ? ArgumentAdaptation.SameSemanticsReborrow : ArgumentAdaptation.CrossSemanticsBorrow;
            kind = ArgumentOperationKind.Reborrow;
        }
        else if (actual.Semantics is SemanticsKind.Owner or SemanticsKind.Raw || (explicitBorrow && TryAdaptationSelector(actual, out _)))
        {
            // SPEC 13.5.6: an explicit borrow of a slot storing a pair layer, an application or an adaptation family (SPEC 13.5.5,
            // TryAdaptationSelector), borrows that slot like a stored value, whatever each admitted case stores. A written
            // Borrow is lent as Binding lends it, an implicit one (a receiver's too) as SPEC does (BorrowablePlace).
            var exclusive = target == SemanticsKind.Uniq;
            if (explicitBorrow && !receiver ? this.BorrowablePlace(source, scope, exclusive) : !judged || this.LendsPlace(source, scope, exclusive, out _))
            {
                // SPEC 15.1.5 lending rule: an owned Place is lent exclusively by @uniq at every position other
                // than a Receiver Expression, whatever its access path; a receiver is acquired implicitly (SPEC 7.3).
                if (exclusive && !explicitBorrow && !receiver)
                {
                    if (!this.RequireAcquisition(source, actual, pattern.Components[0], BindingFailure.ExclusiveBorrowRequired, false, fixedExpectation, deferAcquisition))
                    {
                        return false;
                    }
                }
            }
            else if (!this.BorrowableTemporary(source, exclusive, explicitBorrow || receiver, fixedExpectation) &&
                !(target == SemanticsKind.Ref && this.KindOf(source) == PartKind.Literal))
            {
                return false;
            }

            // SPEC 10.2: an owner temporary, including a defaulted literal and a raw/U value (raw is no reference or handle slot; PLAN
            // G585, K's K4: `null` at ref/(raw/U)), may be shared-borrowed; its
            // exclusive borrow is explicit or implicit for a receiver (SPEC 3.6, 7.3). A getter result
            // is a MemberAccessKoto with a Symbol and is never exclusively acquired (SPEC 11.5).
            referent = actual;
            quality = ArgumentAdaptation.CrossSemanticsBorrow;
            kind = ArgumentOperationKind.Borrow;
        }
        else
        {
            return false;
        }

        if (projected)
        {
            if (declaringType is null)
            {
                return false;
            }

            referent = declaringType;
            kind = ArgumentOperationKind.BaseBorrow;
        }

        var dependency = !projected && kind == ArgumentOperationKind.Reborrow ? this.PathReborrowOrigin(source, actual.Origin) : this.PlaceOrigin(source);
        adapted = this.types.Reference(target, referent, dependency);
        return true;
    }

    private bool AdaptObjectBorrow(Koto source, BoundType pattern, BoundType actual, BindingScope scope, bool explicitOwner, out BoundType adapted, out ArgumentAdaptation quality, out ArgumentOperationKind kind, bool receiver = false, bool fixedExpectation = false, bool deferAcquisition = false)
    {
        adapted = actual;
        quality = ArgumentAdaptation.Exact;
        kind = ArgumentOperationKind.Value;
        var exclusive = pattern.Semantics == SemanticsKind.ObjUniq;
        if (receiver && actual.SafeReferent?.HandleMode is { } stored)
        {
            if (exclusive && (actual.Semantics != SemanticsKind.Uniq || stored.PayloadAuthority != LoanRequirement.Uniq || this.ReachedThroughShared(source)))
            {
                return false;
            }

            // Inspect the protected handle slot, retaining its Loan; acquiring the owner is unnecessary.
            adapted = this.types.Reference(pattern.Semantics, actual.Components[0].Components[0], this.PlaceOrigin(source));
            kind = ArgumentOperationKind.Borrow;
            quality = ArgumentAdaptation.CrossSemanticsBorrow;
            return true;
        }

        if (actual.IsObjectBorrow)
        {
            if (!this.LendsView(source, actual, exclusive))
            {
                // SPEC 3.5.1: as a stored uniq's (AdaptInput), an objuniq view's bare acquisition through a shared path needs a Move.
                return actual.Semantics == SemanticsKind.ObjUniq && !explicitOwner && !receiver &&
                    this.RequireAcquisition(source, actual, pattern, BindingFailure.TransferRequired, @object: true, fixedExpectation, deferAcquisition);
            }

            if (actual.Semantics == SemanticsKind.ObjRef)
            {
                return true;
            }

            kind = ArgumentOperationKind.Reborrow;
            quality = exclusive ? ArgumentAdaptation.SameSemanticsReborrow : ArgumentAdaptation.CrossSemanticsBorrow;
        }
        else if (actual.HandleMode is { } mode && (!exclusive || mode.PayloadAuthority == LoanRequirement.Uniq) &&
            ((explicitOwner && !receiver ? this.BorrowablePlace(source, scope, exclusive) : this.LendsPlace(source, scope, exclusive, out _)) ||
            this.BorrowableTemporary(source, exclusive, explicitOwner || receiver, fixedExpectation)))
        {
            // SPEC 13.5.5, 13.5.6: a handle lends its object from a Place, as Binding lends it to a written Borrow and as SPEC does to an
            // implicit one (BorrowablePlace), or from a Temporary Value.
            if (exclusive && !explicitOwner && !receiver)
            {
                // SPEC 15.1.5 lending rule: an owned handle is lent exclusively by @objuniq except as a receiver (SPEC 7.3).
                if (!this.RequireAcquisition(source, actual.Components[0], pattern.Components[0], BindingFailure.ExclusiveBorrowRequired, true, fixedExpectation, deferAcquisition))
                {
                    return false;
                }
            }

            kind = ArgumentOperationKind.Borrow;
            quality = ArgumentAdaptation.CrossSemanticsBorrow;
        }
        else
        {
            return false;
        }

        // SPEC 15.6.3: a Reborrow of an object view is contained in the view and the layers reaching it; a handle lends its object from its Place.
        var origin = actual.IsObjectBorrow ? this.ReferentOrigin(source, actual)! : this.PlaceOrigin(source);
        adapted = this.types.Intern(BoundTypeKind.Semantics, null, pattern.Semantics, [actual.Components[0]], origin: origin);
        return true;
    }

    /// <summary>The `adaptations` family's one accessor (DECISIONS BU-220): a pass family, the implicit adaptation selected for an
    /// expression (SPEC 10.2), kept in its node's module's NodeColumns at its SyntaxId (an adaptation always has its Type, so the column's
    /// default is none); absent after a pass until the column's first write. Its members keep the names its readers use.</summary>
    private readonly struct AdaptationRows(Binding binding)
    {
        internal BoundAdaptation this[Koto node]
        {
            set
            {
                HirTables.Of(node).Families.Adaptations.Ref(node.SyntaxId, binding) = value;
                if (binding.FormsBorrows)
                {
                    binding.AdaptationBorrow(value, binding.borrows); // An adaptation is a borrow fact of its node (Binding.RawOrigins.Borrows).
                }
            }
        }

        internal bool ContainsKey(Koto node) => this.TryGetValue(node, out _);

        internal void Remove(Koto node)
        {
            if (this.TryGetValue(node, out _))
            {
                this[node] = default;
            }
        }

        internal bool TryGetValue(Koto node, out BoundAdaptation adaptation)
            => (adaptation = node.SyntaxOwner is { } table ? table.Hir.Families.Adaptations.Get(node.SyntaxId, binding) : default) != default;
    }

    /// <summary>The `receiverOperations` family's one accessor (DECISIONS BU-220): a working-state family, the receiver or storage
    /// operation selected for a call or member access, kept in its node's module's NodeColumns at its SyntaxId (a selected operation always
    /// has its Source, so the column's default is none). The HIR publisher reads it at its pass's publication and TryGetReceiverOperation
    /// refuses a frozen node, so no reader reads a library row after the library pass, and the freeze releases them (NodeColumns.Freeze).
    /// Its members keep the names its readers use.</summary>
    private readonly struct ReceiverOperationRows(Binding binding)
    {
        internal BoundArgumentOperation this[Koto use]
        {
            set => HirTables.Of(use).Families.ReceiverOperations.Ref(use.SyntaxId, binding) = value;
        }

        internal bool TryGetValue(Koto use, out BoundArgumentOperation operation)
            => (operation = use.SyntaxOwner is { } table ? table.Hir.Families.ReceiverOperations.Get(use.SyntaxId, binding) : default).Source is not null;
    }
}
