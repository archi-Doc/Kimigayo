// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402, CS1591 // Shared operation vocabulary.

public enum ArgumentAdaptation : byte
{
    Exact,
    Literal,
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
/// marks a known call signature whose fit only one of its own per-call Origins would satisfy, reported after selection (SPEC 15.3.6).</summary>
public readonly record struct BoundArgumentOperation(Koto? Source, BoundType? SourceType, BoundType? ParameterType, ArgumentOperationKind Kind, ArgumentAdaptation Adaptation, BoundMemberPath? BasePath = null, int ParameterIndex = -1, ConstraintProof ObjectCompatibility = ConstraintProof.Proven, BoundType? AdaptedType = null, bool MissingOrigin = false);

public sealed partial class Binding
{
    private readonly ScratchBuffers<BoundArgumentOperation> argumentOperationScratch = new();
    private readonly Dictionary<Koto, BoundArgumentOperation> receiverOperations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Koto, BoundAdaptation> adaptations = new(ReferenceEqualityComparer.Instance);

    /// <summary>Gets whether an expression of Type <c>ref/T</c> or <c>uniq/T</c> is read as its Copy referent where a <c>T</c> is expected (SPEC 3.5.3).</summary>
    /// <param name="node">The bound expression; its BoundType remains the reference Type.</param>
    /// <returns>Whether the expression's value is the copied referent.</returns>
    public bool ReadsReferent(Koto node) => this.adaptations.TryGetValue(node, out var adaptation) && adaptation.Kind == ExpectedAdaptationKind.ReferentRead;

    /// <summary>Gets the implicit adaptation selected for an expression at its fixed expected Type (SPEC 10.2).</summary>
    /// <param name="node">The expression.</param>
    /// <param name="adaptation">The operation and the Type it supplies.</param>
    /// <returns>Whether the expression is adapted rather than acquired as it is.</returns>
    public bool TryGetAdaptation(Koto node, out BoundAdaptation adaptation) => this.adaptations.TryGetValue(node, out adaptation);

    /// <summary>Gets a selected receiver/storage operation, including an unresolved projected-use proof obligation.</summary>
    /// <param name="use">The call or member access in the current binding pass.</param>
    /// <param name="operation">The selected operation; an Unknown proof is not permission to execute.</param>
    /// <returns>Whether this pass selected an operation for the use.</returns>
    public bool TryGetReceiverOperation(Koto use, out BoundArgumentOperation operation) => this.receiverOperations.TryGetValue(use, out operation);

    internal static Koto PlaceOriginSource(Koto source)
    {
        // Inline element projections share the owner's lifetime. Their Loans
        // retain separate place footprints for overlap checking.
        source = KotoHelper.UnwrapParentheses(source);
        while (source is MemberAccessKoto or IndexKoto)
        {
            if (IsGetterResult(source))
            {
                break; // Its lifetime is the result temporary, not the receiver's storage.
            }

            source = KotoHelper.UnwrapParentheses(((BinaryKoto)source).Left);
            if (source.BoundType?.Origin is not null)
            {
                break;
            }
        }

        return source;
    }

    internal static Koto PlaceOriginBinder(Koto source) => source.CodeContext.Compilation.Binding.PropertyCall(source, PropertyAccessorKind.Get) is { } getter ? getter
        : source.BoundSymbol is { Kind: BindingSymbolKind.PatternCandidate } candidate
        ? CandidateOriginBinder(candidate)
        : source.BoundSymbol is { Kind: BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Storage or BindingSymbolKind.Capture } symbol ? symbol.Declaration : source;

    // SPEC 7.7.2, 15.8.2: an environment binding is a Place of its closure, distinct from the closure's parameters, which share
    // the closure as binder; the i-th binding's Projection slot is -2 - i.
    internal static int EnvironmentSlot(int index) => -2 - index;

    // The Projection slot of a closure's call result, a display end only (SPEC 23.1).
    internal const int CallResultSlot = -1;

    // The Projection slot that names a closure literal's own temporary value, the receiver of a call on that literal (SPEC 15.8.2).
    internal const int ClosureValueSlot = int.MinValue;

    // The Projection slot of the helper borrow, a Borrow of the whole destruction receiver (SPEC 16.3.1, Binding.HelperOrigin). Its binder
    // is always a destructor, never a value or a closure, so no reader of an environment or closure-value slot meets it.
    internal const int HelperSlot = int.MinValue + 1;

    // The slot of a constructor's or destructor's receiver `self` and so the Projection slot of its Place (SPEC 6.3.4, 16.3.1;
    // DECISIONS BU-23): no parameter index, so the receiver's Place is never a parameter's (Receiver). Its binder is always a
    // constructor or destructor, never a closure, so no reader of an environment slot (Slot <= EnvironmentSlot(0)) meets it.
    internal const int ReceiverSlot = int.MinValue + 2;

    // The Projection slot that names a binding's own Place.
    internal static int SymbolOriginSlot(BindingSymbol symbol) => symbol.Kind == BindingSymbolKind.Capture ? EnvironmentSlot(symbol.Slot) : symbol.Slot;

    // A guard candidate and the selected body binding share their source declaration,
    // but never their storage lifetime. Reuse the name node as the candidate's identity.
    internal static Koto CandidateOriginBinder(BindingSymbol candidate) => ((SyntaxFormKoto)candidate.Declaration).Operands[0];

    internal static int PlaceOriginSlot(Koto source) => source.BoundSymbol is { Kind: BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Storage or BindingSymbolKind.PatternCandidate or BindingSymbolKind.Capture } symbol
        ? SymbolOriginSlot(symbol) : 0;

    internal BoundType PreparedBorrowType(Koto source, BoundType parameter)
        => this.types.Intern(parameter.Kind, parameter.Symbol, parameter.Semantics, [parameter.Components[0]], origin: this.PreparedOrigin(source));

    // SPEC 4.7.1, 4.5: an exclusive borrow of a dynamic Array element lends the whole owned Array exclusively first.
    internal BoundType ExclusiveArrayHandle(Koto array)
        => this.types.Intern(BoundTypeKind.Semantics, null, SemanticsKind.Uniq, [array.BoundType!], origin: this.PlaceOrigin(array));

    // SPEC 3.4.3 (PLAN G59): the borrow, in a use's mode, of an Array element whose stored part the use borrows in place, with the
    // element Place's own Origin.
    internal BoundType ElementPlaceReference(SemanticsKind semantics, Koto element)
        => this.types.Intern(BoundTypeKind.Semantics, null, semantics, [element.BoundType!], origin: this.PlaceOrigin(element));

    /// <summary>
    /// SPEC 10.2: safe reference layers ending in <paramref name="referent"/> yield one shared reference to it. A <c>ref</c>
    /// layer is Copied with its own Origin, so it restarts the dependency; each <c>uniq</c> layer below it is shared-Reborrowed
    /// and meets its Origin into the result.
    /// </summary>
    /// <param name="actual">The complete Type of the value.</param>
    /// <param name="referent">The referent the expected <c>ref</c> Type names.</param>
    /// <param name="layers">The number of reference layers followed, or zero when the layers do not end in the referent.</param>
    /// <returns>The shared reference Type, or null.</returns>
    internal BoundType? SharedReferenceThroughLayers(BoundType actual, BoundType referent, out int layers)
    {
        BoundOrigin? origin = null;
        layers = 0;
        for (var type = actual; type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } && layers < 64; type = type.Components[0])
        {
            layers++;
            origin = type.Semantics == SemanticsKind.Ref || origin is null ? type.Origin ?? origin : type.Origin is null ? origin : this.Meet(origin, type.Origin);
            if (ReferenceEquals(type.Components[0], referent))
            {
                return this.types.Intern(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [referent], origin: origin);
            }
        }

        layers = 0;
        return null;
    }

    private static ConstraintProof ProjectedReceiverProof(BindingSymbol implementation, BoundAccessor? accessor = null)
        // SPEC 12.7.1: a shared receiver grants only read/shared-borrow effects.
        // Exclusive receivers still require the receiver-preservation fixed point.
        => implementation.Declaration.BindingState == BindingState.Invalid || accessor?.Declaration?.BindingState == BindingState.Invalid ? ConstraintProof.Error
            : (accessor?.Receiver?.Semantics ?? ReceiverShape(implementation)) is SemanticsKind.Ref or SemanticsKind.ObjRef ? ConstraintProof.Proven : ConstraintProof.Unknown;

    private static bool IsTransfer(Koto source) => KotoHelper.UnwrapParentheses(source) is ConversionKoto { ConversionBinding: ConversionBinding.Transfer };

    // An Origin that a function's signature introduces, over its receiver or parameters, rather than a body-local borrow.
    private static bool SignatureOrigin(BoundOrigin origin)
    {
        if (origin.Kind is OriginKind.Input or OriginKind.Parameter)
        {
            return true;
        }

        for (var i = 0; i < origin.Operands.Count; i++)
        {
            if (SignatureOrigin(origin.Operands[i]))
            {
                return true;
            }
        }

        return false;
    }

    // The function whose result the expression is: the operand of a return, or the single-item body.
    private static FunctionKoto? ResultFunction(Koto node)
    {
        var parent = node.Parent;
        while (parent is ParenthesizedKoto)
        {
            parent = parent.Parent;
        }

        return parent switch
        {
            ReturnKoto jump => KotoHelper.ResolveTransferTarget(jump) as FunctionKoto,
            CodeBlockKoto { IsExpressionBody: true, Parent: FunctionKoto owner } => owner,
            FunctionKoto owner when ReferenceEquals(owner.ExpressionBody, node) => owner,
            _ => null,
        };
    }

    // Set while candidates are evaluated: the reason an otherwise fitting bare Place was not applicable,
    // so a call without applicable candidates names the required spelling (SPEC 15.1.5), the Place that needs it and,
    // for an exclusive borrow, whether the Place is an object handle (@objuniq) (SPEC 23.1.3).
    private AcquisitionFailure? acquisitionFailure;

    private bool RequireAcquisition(Koto source, BoundType actual, BoundType expected, BindingFailure failure, bool @object, bool fixedExpectation, bool deferAcquisition)
    {
        if (fixedExpectation || (!deferAcquisition && !this.FitsStructurallyAt(actual, expected, source)))
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

        if (!this.AdaptInput(node, expected, actual, this.ConstraintScope(node), null, null, out var adapted, out _, out var operation, fixedExpectation: true, recordPair: recordPair) ||
            !this.FitsStructurallyAt(adapted, expected, node))
        {
            return null;
        }

        if (recordPair)
        {
            _ = this.FailStaticBorrow(node, operation, adapted); // The selected destination's Borrow; a probe adapts a call result.
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

    // The dependency of a borrow prepared from a source: through an implicitly followed pair layer it is the layer's own
    // (SPEC 13.5.5), as the adaptation recorded it; otherwise the source Place's.
    private BoundOrigin PreparedOrigin(Koto source)
        => KotoHelper.UnwrapParentheses(source) is var place && this.ImplicitPairAdmitted(place) is var admitted && admitted != SemanticsMask.None && place.BoundType is { } pair
            ? this.PairOrigin(place, pair, admitted) : this.PlaceOrigin(source);

    // SPEC 15.6.3: a Reborrow through an exclusive reference stored in a Place is contained in that reference's Origin and in the
    // Origin of each safe borrow through which the Place is reached, counting outward up to and including the first shared
    // layer, so a result reached through a borrowed receiver or parameter stays within that borrow. A shared reference is Copied
    // and keeps its own Origin. Body-local borrows on the path stay ancestors in ownership analysis (SPEC 15.6.2).
    private BoundOrigin? PathReborrowOrigin(Koto place, BoundOrigin? origin)
    {
        if (origin is null)
        {
            return null;
        }

        var path = KotoHelper.UnwrapParentheses(place);
        for (var depth = 0; depth < 64 && origin is not null; depth++)
        {
            var outer = path switch
            {
                MemberAccessKoto member => member.Left,
                IndexKoto index => index.Left,
                ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PairFollow } follow => follow.Left,
                _ => null,
            };
            if (outer is null)
            {
                break;
            }

            outer = KotoHelper.UnwrapParentheses(outer);
            if (path is ConversionKoto { ConversionBinding: ConversionBinding.PairFollow } pair)
            {
                // SPEC 13.5.5: an outer pair layer is a reference in its borrow cases, whose slot bounds the result there; the
                // walk goes on while an owner or exclusive case reaches further, and the meet over the cases keeps every bound.
                var admitted = this.PairAdmitted(pair);
                if ((admitted & SemanticsMask.Borrow) != 0 && outer.BoundType is { } pairLayer && this.OuterOrigin(pairLayer) is { } slot && SignatureOrigin(slot))
                {
                    origin = this.Meet(origin, slot);
                }

                if ((admitted & (SemanticsMask.Owner | SemanticsMask.Uniq | SemanticsMask.ObjUniq)) == 0)
                {
                    break;
                }
            }
            else if (outer.BoundType is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1, Origin: { } layer })
            {
                if (SignatureOrigin(layer))
                {
                    origin = this.Meet(origin, layer);
                }

                if (outer.BoundType.Semantics == SemanticsKind.Ref)
                {
                    break;
                }
            }

            path = outer;
        }

        return origin;
    }

    // The Origin a followed reference's referent keeps: an exclusive reference's own Origin met with its reaching borrows.
    private BoundOrigin? FollowedOrigin(Koto reference)
        => reference.BoundType is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq, Origin: { } origin } ? this.PathReborrowOrigin(reference, origin) : reference.BoundType?.Origin;

    // SPEC 13.5.6, 15.6.5: a borrow of a slot that stores a reference depends on that slot. A slot reached through a reference
    // keeps that reference's dependencies, whereas the own slot of a local, a parameter, an environment binding or a temporary is
    // a Place of the body or of the closure's receiver (SPEC 15.8.2).
    private BoundOrigin SlotOrigin(Koto source)
        => this.HelperOrigin(source) ?? (KotoHelper.UnwrapParentheses(source) is var slot &&
            (slot is IdentifierNameKoto { BoundSymbol.Kind: BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Capture } ||
            slot is InvocationKoto)
            ? this.OriginAtom(PlaceOriginBinder(slot), OriginKind.Projection, PlaceOriginSlot(slot)) : this.PlaceOrigin(source));

    // SPEC 13.5.6: the Origin of a Borrow of the Place `source` designates; a caller typing the Places reached through a receiver
    // names one of them, never the receiver.
    private BoundOrigin PlaceOrigin(Koto source)
    {
        if (this.HelperOrigin(source) is { } helper)
        {
            return helper;
        }

        if (this.ReadsReferent(source))
        {
            return this.OriginAtom(source, OriginKind.Projection, 0);
        }

        if (KotoHelper.UnwrapParentheses(source) is ConversionKoto { ConversionBinding: ConversionBinding.PairFollow } pair)
        {
            // SPEC 13.5.5: an admitted owner selects the operand Place itself; with only borrows admitted, the selected
            // referent keeps the dependencies of the stored reference, like a Reborrow.
            return this.PairOrigin(pair.Left, pair.Left.BoundType, this.PairAdmitted(pair));
        }

        if (KotoHelper.UnwrapParentheses(source) is ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PayloadFollow } selected)
        {
            // SPEC 13.5.5, 15.6.3: a selected referent or payload keeps the dependencies of its reference or handle,
            // so a Reborrow depends on the referent and the parent Loan, not on the slot holding the parent; through an
            // exclusive reference, it also stays within the borrows that reach that reference.
            return this.FollowedOrigin(selected.Left) ?? this.PlaceOrigin(selected.Left);
        }

        if (ElementAccess.IndexerCall(source, false) is { BoundType.Origin: { } publishedElement })
        {
            return publishedElement; // SPEC 4.7.1: the element Place depends on the receiver under its contract.
        }

        // The walk stops at the value nearest the Place that carries its own Origin; a pair layer's Origin is that of the reference
        // it may hold, so the layer itself is its own dependency.
        var place = KotoHelper.UnwrapParentheses(source);
        source = PlaceOriginSource(source);
        var reachedThrough = !ReferenceEquals(source, place);
        var layer = ElementAccess.AccessType(source) is { Origin: not null } type && !TryPairLayer(type, out _, out _) ? type : null;
        if (reachedThrough && layer is { Semantics: not (SemanticsKind.Uniq or SemanticsKind.ObjUniq) })
        {
            // SPEC 15.6.3, 4.7.1, 4.8.1: a Place reached through a shared layer, a Slice handle or a shared reference, depends
            // only on that layer, its last shared dereference, also when the layer is a selected referent (`r@follow[0]`).
            return layer.Origin!;
        }

        if (source is ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PayloadFollow } reached)
        {
            // SPEC 13.5.5, 15.6.3: a part of any other selected referent (`p@follow.x`) keeps the dependencies of the reference,
            // as the referent itself does.
            return this.FollowedOrigin(reached.Left) ?? this.PlaceOrigin(reached.Left);
        }

        // SPEC 3.4.3, 4.8.1: a reference read in place is its referent, and a Place reached through an exclusive layer stored in
        // place depends on that layer; a Slice handle that is itself the Place is a Place of its root.
        return layer is not null && (reachedThrough || layer.Kind == BoundTypeKind.Semantics) ? layer.Origin!
            : this.OriginAtom(PlaceOriginBinder(source), OriginKind.Projection, PlaceOriginSlot(source));
    }

    // SPEC 3.5.3 value read: the safe value-reference layers of an operand are followed to their terminal Type, which is
    // read only when it is a read Type; any other referent, Copy or not, is never read implicitly. The node keeps its
    // reference Type.
    private BoundType? ReadReferent(Koto node, BoundType? type)
    {
        if (ScalarReferent(type) is not { } referent)
        {
            // A qualifying pair layer is one of the followed layers (SPEC 3.4.3), also below another one (s/(t/U)): the read is
            // recorded as the implicit follow of the outer layer, and each instance loads through the layers it has.
            var scope = this.ConstraintScope(node);
            if (this.ReadTypeReferent(type, scope) is { } read)
            {
                this.adaptations[node] = new(ExpectedAdaptationKind.ReferentRead, read); // SPEC 3.5.3, 8.7.4: a read Type.
                return read;
            }

            if (type is not null &&
                this.FollowablePair(type, scope, out var target) is var admitted && admitted != SemanticsMask.None &&
                this.PairTerminal(target, scope) is { } terminal &&
                (terminal.Kind == BoundTypeKind.Primitive && ScalarTypes.Supports(terminal) ? terminal : ScalarReferent(terminal)) is { } scalar)
            {
                this.implicitPairFollows[node] = admitted;
                this.adaptations[node] = new(ExpectedAdaptationKind.ReferentRead, scalar);
                return scalar;
            }

            return type;
        }

        this.adaptations[node] = new(ExpectedAdaptationKind.ReferentRead, referent);
        return referent;
    }

    // The type an argument presents to adaptation: a node already read where its parameter Type was
    // expected adapts from its own reference Type, so the plan records the Copy read once.
    private BoundType ArgumentType(Koto source, BoundType actual) => this.ReadsReferent(source) ? source.BoundType ?? actual : actual;

    /// <summary>
    /// Adapts an input to a parameter Type. <paramref name="receiver"/> marks a Receiver Expression, which SPEC 7.3
    /// acquires implicitly: a new exclusive borrow of an owned Place or temporary needs no spelling there, whereas
    /// every other position requires <c>@uniq</c>/<c>@objuniq</c> whatever the access path (SPEC 15.1.5).
    /// </summary>
    private bool AdaptInput(Koto source, BoundType pattern, BoundType actual, BindingScope scope, BoundMemberPath? path, BoundType? declaringType, out BoundType adapted, out ArgumentAdaptation quality, out ArgumentOperationKind kind, bool explicitBorrow = false, bool receiver = false, bool fixedExpectation = false, bool recordPair = true, bool deferAcquisition = false)
    {
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

        if (!receiver && IsTransfer(source) && actual is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.ObjRef or SemanticsKind.ObjUniq } &&
            ((pattern.Kind == BoundTypeKind.Semantics && !ReferenceTypes.StorageMatches(actual, pattern)) || pattern.Kind == BoundTypeKind.Primitive))
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

        if (pattern.Kind != BoundTypeKind.Semantics || pattern.Semantics is not (SemanticsKind.Ref or SemanticsKind.Uniq))
        {
            if (projected)
            {
                return false; // An owning receiver cannot acquire a sliced base.
            }

            // SPEC 10.2: where the reference does not fit but its Copy referent does, the referent is read.
            if (!this.FitsTypeAt(actual, pattern, source) && this.ReadTypeReferent(actual, scope) is { } read && this.FitsTypeAt(read, pattern, source))
            {
                adapted = read;
                quality = ArgumentAdaptation.CrossSemanticsBorrow;
                kind = ArgumentOperationKind.CopyRead;
            }
            else if (!fixedExpectation && actual.Semantics is SemanticsKind.Owner or SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc && IsBarePlace(source) &&
                this.BareAcquire(actual, source, out _) == HirAcquire.Move && !this.RequireAcquisition(source, actual, pattern, BindingFailure.TransferRequired, false, fixedExpectation, deferAcquisition))
            {
                // SPEC 3.5, 10.1: a bare Place never Moves, so a Place whose bare acquisition needs a Move is not acquired by value,
                // and the candidate that needs the Copy is inapplicable.
                return false;
            }

            return true;
        }

        // A comparison's Copy read is a temporary value, even though its syntax retains ref/T.
        // Borrow that snapshot; borrowing the original storage would change left-to-right semantics.
        if (!projected && pattern.Semantics == SemanticsKind.Ref && this.ReadsReferent(source) &&
            ScalarReferent(actual) is { } snapshot && this.FitsTypeAt(snapshot, pattern.Components[0], source))
        {
            adapted = this.PreparedBorrowType(source, pattern);
            quality = ArgumentAdaptation.CrossSemanticsBorrow;
            kind = ArgumentOperationKind.Borrow;
            return true;
        }

        // SPEC 7.3, 10.2: a receiver through a pair layer, or one shared reference to its target at a fixed ref/U; an
        // exclusive borrow of an owned argument keeps its explicit spelling (SPEC 15.1.5).
        if (!projected && (receiver || pattern.Semantics == SemanticsKind.Ref) && this.TryPairReceiver(source, pattern, actual, scope, out adapted, out kind, recordPair))
        {
            quality = ArgumentAdaptation.CrossSemanticsBorrow;
            return true;
        }

        // A shared receiver can inspect an open payload or its selected base without granting replacement authority.
        // The selected implementation still supplies the per-operation proof before the call is accepted.
        if (receiver && pattern.Semantics == SemanticsKind.Ref && declaringType is not null &&
            IsObjectSemantics(actual.Semantics) && actual.Components.Count == 1 && ReferenceEquals(pattern.Components[0], declaringType) &&
            (projected || ReferenceEquals(actual.Components[0], declaringType)) &&
            (actual.Semantics is SemanticsKind.ObjRef or SemanticsKind.ObjUniq || this.LendsPlace(source, scope, false, out _) ||
            BorrowableTemporary(source, false, true, false)))
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
            if (!this.BorrowablePlace(source, scope, target == SemanticsKind.Uniq) && !BorrowableTemporary(source, target == SemanticsKind.Uniq, true, fixedExpectation))
            {
                return false;
            }

            referent = actual;
            quality = ArgumentAdaptation.CrossSemanticsBorrow;
            kind = ArgumentOperationKind.Borrow;
            adapted = this.types.Intern(BoundTypeKind.Semantics, null, target, [referent], origin: this.SlotOrigin(source));
            return true;
        }

        if (actual.Kind == BoundTypeKind.Semantics && actual.Semantics is SemanticsKind.Ref or SemanticsKind.Uniq)
        {
            if (!explicitBorrow && pattern.Components[0] is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq } && ReferenceEquals(pattern.Components[0], actual))
            {
                return false; // SPEC 10.2: no implicit borrow of a reference slot.
            }

            if (!explicitBorrow && !projected && target == SemanticsKind.Ref &&
                this.SharedReferenceThroughLayers(actual, pattern.Components[0], out var layers) is { } shared && (layers > 1 || actual.Semantics == SemanticsKind.Uniq))
            {
                // SPEC 10.2: several reference layers yield one shared reference to the parameter's referent.
                adapted = actual.Semantics == SemanticsKind.Uniq && shared.Origin is { } origin
                    ? this.types.Reference(SemanticsKind.Ref, pattern.Components[0], this.PathReborrowOrigin(source, origin)) : shared;
                quality = ArgumentAdaptation.CrossSemanticsBorrow;
                kind = layers > 1 ? ArgumentOperationKind.ReferenceRead : ArgumentOperationKind.Reborrow;
                return true;
            }

            if (target == SemanticsKind.Uniq && (actual.Semantics != SemanticsKind.Uniq || ReachedThroughShared(source)))
            {
                return false;
            }

            referent = actual.Components[0];
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
        else if (actual.Semantics == SemanticsKind.Owner || (explicitBorrow && actual.Kind == BoundTypeKind.SemanticsApplication))
        {
            // SPEC 13.5.6: an explicit borrow of a slot storing a pair application borrows that slot like a stored value. A written
            // Borrow is lent as Binding lends it, an implicit one (a receiver's too) as SPEC does (BorrowablePlace).
            var exclusive = target == SemanticsKind.Uniq;
            if (explicitBorrow && !receiver ? this.BorrowablePlace(source, scope, exclusive) : this.LendsPlace(source, scope, exclusive, out _))
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
            else if (!BorrowableTemporary(source, exclusive, explicitBorrow || receiver, fixedExpectation) &&
                !(target == SemanticsKind.Ref && this.KindOf(source) == ArgumentKind.Literal))
            {
                return false;
            }

            // SPEC 10.2: an owner temporary, including a defaulted literal, may be shared-borrowed; its
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
        if (receiver && ReferenceTypes.IsStorage(actual) && actual.Components[0].HandleMode is { } stored)
        {
            if (exclusive && (actual.Semantics != SemanticsKind.Uniq || stored.PayloadAuthority != LoanRequirement.Uniq || ReachedThroughShared(source)))
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
            if (exclusive && (actual.Semantics != SemanticsKind.ObjUniq || ReachedThroughShared(source)))
            {
                return false;
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
            BorrowableTemporary(source, exclusive, explicitOwner || receiver, fixedExpectation)))
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

        adapted = this.types.Intern(BoundTypeKind.Semantics, null, pattern.Semantics, [actual.Components[0]], origin: this.PlaceOrigin(source));
        return true;
    }
}
