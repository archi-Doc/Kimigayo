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
    /// receiver is selected through exclusive layers only (SPEC 3.4.1).</summary>
    ReferenceRead,

    /// <summary>A bare Place is exclusively borrowed as the operand of a place uniq/T result (SPEC 7.1.1).</summary>
    ExclusiveBorrow,
}

/// <summary>The one recorded adaptation of an expression and the Type it supplies; the node keeps its own Type.</summary>
public readonly record struct BoundAdaptation(ExpectedAdaptationKind Kind, BoundType Type);

/// <summary>A selected operation. Source retains the original storage/Loan anchor; substitution never rewrites it.</summary>
public readonly record struct BoundArgumentOperation(Koto? Source, BoundType? SourceType, BoundType? ParameterType, ArgumentOperationKind Kind, ArgumentAdaptation Adaptation, BoundMemberPath? BasePath = null, int ParameterIndex = -1, ConstraintProof ObjectCompatibility = ConstraintProof.Proven);

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
        : source.BoundSymbol is { Kind: BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Storage } symbol ? symbol.Declaration : source;

    // A guard candidate and the selected body binding share their source declaration,
    // but never their storage lifetime. Reuse the name node as the candidate's identity.
    internal static Koto CandidateOriginBinder(BindingSymbol candidate) => ((SyntaxFormKoto)candidate.Declaration).Operands[0];

    internal static int PlaceOriginSlot(Koto source) => source.BoundSymbol is { Kind: BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Storage or BindingSymbolKind.PatternCandidate } symbol ? symbol.Slot : 0;

    internal BoundType PreparedBorrowType(Koto source, BoundType parameter)
        => this.InternType(parameter.Kind, parameter.Symbol, parameter.Semantics, [parameter.Components[0]], origin: this.PlaceOrigin(source));

    // SPEC 4.6.9, 4.5: an exclusive borrow of a dynamic Array element lends the whole owned Array exclusively first.
    internal BoundType ExclusiveArrayHandle(Koto array)
        => this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Uniq, [array.BoundType!], origin: this.PlaceOrigin(array));

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
                return this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [referent], origin: origin);
            }
        }

        layers = 0;
        return null;
    }

    private static ConstraintProof ProjectedReceiverProof(BindingSymbol implementation)
        // Until Access Effect verification supplies callee/returned-Loan summaries, no body or signature is evidence.
        => implementation.Declaration.BindingState == BindingState.Invalid ? ConstraintProof.Error : ConstraintProof.Unknown;

    // SPEC 15.6.2: access through a shared reference cannot grant exclusive
    // authority, even to an exclusive reference stored below it.
    private static bool ReachedThroughShared(Koto source) => PathAuthority(source) == SemanticsKind.Ref;

    // SPEC 3.4.1: whether selection through the Type's reference layers passes a shared layer.
    private static bool HasSharedLayer(BoundType? type)
    {
        for (; type is { Kind: BoundTypeKind.Semantics, Components.Count: 1 }; type = type.Components[0])
        {
            if (type.Semantics is SemanticsKind.Ref or SemanticsKind.ObjRef or SemanticsKind.Rc or SemanticsKind.Arc)
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 3.4, 13.5.5.1, 15.1.5: the access a Place's path grants, computed once for writes, Moves and borrows. Owner is a
    // direct path (a local, parameter or static and their inline parts), Uniq a path through an exclusive reference or
    // objuniq view, and Ref a path through a shared layer: a ref reference, a Slice element, an objref, rc or arc view or
    // a guard candidate. A shared layer anywhere on the path bounds it to shared access; the path never changes the Type.
    private static SemanticsKind PathAuthority(Koto source)
    {
        var authority = SemanticsKind.Owner;
        source = KotoHelper.UnwrapParentheses(source);
        for (var depth = 0; depth < 64; depth++)
        {
            Koto next;
            SemanticsKind? layer = null;
            switch (source)
            {
                case IdentifierNameKoto { BoundSymbol.Kind: BindingSymbolKind.PatternCandidate }:
                    return SemanticsKind.Ref;
                case ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PayloadFollow } selected:
                    next = selected.Left;
                    layer = selected.Left.BoundType?.Semantics;
                    break;
                case InvocationKoto call when ElementAccess.PlaceCallReference(call) is { } published:
                    // SPEC 7.1.1: a published Place has the capability of the returned reference and never Take.
                    return published.Semantics == SemanticsKind.Ref ? SemanticsKind.Ref : SemanticsKind.Uniq;
                case IndexKoto userIndex when ElementAccess.IsUserIndex(userIndex):
                    // SPEC 3.4.1, 4.6.9: the element Place is reached through the receiver's reference layers; indexUniq
                    // publishes it exclusively, index alone shares it, and a published Place never offers Take.
                    if (!userIndex.CodeContext.Compilation.Binding.HasExclusiveIndexer(userIndex) || HasSharedLayer(userIndex.Left.BoundType))
                    {
                        return SemanticsKind.Ref;
                    }

                    authority = SemanticsKind.Uniq;
                    next = userIndex.Left;
                    break;
                case IndexKoto index when index.Left.BoundType?.Kind == BoundTypeKind.Slice ||
                    index.Left.BoundType is { Kind: BoundTypeKind.Semantics, Components: [{ Kind: BoundTypeKind.Slice }] }:
                    return SemanticsKind.Ref; // SPEC 4.6.6: a Slice element Place is shared.
                case IndexKoto index when ReferenceTypes.IsArray(index.Left.BoundType) || ReferenceTypes.IsDynamicArray(index.Left.BoundType):
                    next = index.Left;
                    layer = index.Left.BoundType!.Semantics;
                    break;
                case MemberAccessKoto member when ElementAccess.BorrowedPathRoot(member) is { } root:
                    next = root;
                    layer = ElementAccess.AccessType(KotoHelper.UnwrapParentheses(root))?.Semantics;
                    break;
                case BinaryKoto part when ElementAccess.IsSyntax(part) && ElementAccess.TryType(part, out _, out _):
                    next = part.Left; // An inline part shares its owner's path.
                    break;
                default:
                    return authority;
            }

            switch (layer)
            {
                case SemanticsKind.Ref or SemanticsKind.ObjRef or SemanticsKind.Rc or SemanticsKind.Arc:
                    return SemanticsKind.Ref;
                case SemanticsKind.Uniq or SemanticsKind.ObjUniq:
                    authority = SemanticsKind.Uniq;
                    break;
            }

            source = KotoHelper.UnwrapParentheses(next);
        }

        return authority;
    }

    // SPEC 3.4, 15.1.5: the one diagnostic for a capability that a Place's path denies. A shared layer grants Read only, an
    // exclusive reference grants Read and Write but not Take, and otherwise the binding itself forbids the access.
    private static BindingFailure AccessFailure(Koto target, bool take = false)
        => PathAuthority(target) switch
        {
            SemanticsKind.Ref => BindingFailure.SharedPathAccess,
            SemanticsKind.Uniq when take => BindingFailure.ExclusivePathTake,
            _ => BindingFailure.InvalidAssignment,
        };

    private static bool IsTransfer(Koto source) => KotoHelper.UnwrapParentheses(source) is ConversionKoto { ConversionBinding: ConversionBinding.Transfer };

    // SPEC 3.5: a bare Place, as opposed to a Temporary Value or an explicit @ operation. Only a Place's
    // acquisition is restricted by the lending rule; a temporary transfers its ownership freely.
    private static bool IsBarePlace(Koto source)
    {
        source = KotoHelper.UnwrapParentheses(source);
        return source switch
        {
            IdentifierNameKoto => source.BoundSymbol?.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Storage or BindingSymbolKind.Capture or BindingSymbolKind.PatternCandidate,
            ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PayloadFollow } => true, // SPEC 13.5.5.1: a selected referent is a Place.
            MemberAccessKoto member => ElementAccess.AccessType(member.Left) is var receiver &&
                ((member.BoundSymbol?.Property is { Getter.IsStandard: true } && StructStorage.IsStruct(receiver?.Kind == BoundTypeKind.Semantics ? receiver.Components[0] : receiver)) ||
                ReferenceTypes.IsTuple(receiver) || receiver?.Kind == BoundTypeKind.Tuple), // SPEC 3.4.1: also through the receiver's recorded reference.
            IndexKoto index => index.Left.BoundType?.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Slice or BoundTypeKind.Array or BoundTypeKind.Dictionary ||
                ReferenceTypes.IsArray(index.Left.BoundType) || ReferenceTypes.IsDynamicArray(index.Left.BoundType) || ReferenceTypes.IsDictionary(index.Left.BoundType) ||
                ElementAccess.IsUserIndex(index), // SPEC 4.6.9, including a Place published by a user index
            InvocationKoto call => ElementAccess.IsPlaceCall(call), // SPEC 7.1.1: a published Place.
            _ => false,
        };
    }

    // SPEC 7.1.1: whether the expression is the operand of a return, or the single-item body, of a function with a
    // place uniq/T result; only there does an owned Place adapt to an exclusive expectation without @uniq.
    private static bool IsExclusivePlaceResultSource(Koto node)
    {
        var parent = node.Parent;
        while (parent is ParenthesizedKoto)
        {
            parent = parent.Parent;
        }

        var function = parent switch
        {
            ReturnKoto jump => KotoHelper.ResolveTransferTarget(jump) as FunctionKoto,
            CodeBlockKoto { IsExpressionBody: true, Parent: FunctionKoto owner } => owner,
            FunctionKoto owner when ReferenceEquals(owner.ExpressionBody, node) => owner,
            _ => null,
        };
        return function is { ReturnType: PlaceResultKoto { IsExclusive: true } };
    }

    // Set while candidates are evaluated: the reason an otherwise fitting bare Place was not applicable,
    // so a call without applicable candidates names the required spelling (SPEC 15.1.5).
    private bool transferRequired;
    private bool lendingRequired;

    // SPEC 3.4.1: a member or Tuple element selected through several reference layers is reached through one reference
    // to the Type that declares it: shared when any layer is shared, exclusive otherwise, with the Origins of 10.2. The
    // receiver keeps its own Type; the recorded reference is what ownership loads and generation addresses.
    private BoundType? ReceiverThroughLayers(Koto left, BoundType? actual)
    {
        if (actual is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } ||
            actual.Components[0] is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 })
        {
            return null;
        }

        var terminal = actual;
        var exclusive = !ReachedThroughShared(left);
        while (terminal is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 })
        {
            exclusive &= terminal.Semantics == SemanticsKind.Uniq;
            terminal = terminal.Components[0];
        }

        if ((terminal is not { Kind: BoundTypeKind.Tuple } && !StructStorage.IsStruct(terminal)) || this.SharedReferenceThroughLayers(actual, terminal, out _) is not { } shared)
        {
            return null;
        }

        var reference = exclusive ? this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Uniq, [terminal], origin: shared.Origin) : shared;
        this.adaptations[left] = new(ExpectedAdaptationKind.ReferenceRead, reference);
        return reference;
    }

    // SPEC 3.4.1, 4.6.6: a member selected below a Non-Copy element of a Slice or borrowed array is reached through one
    // shared borrow of that element Place. The element keeps its stored Type; the borrow is the receiver's adaptation.
    private void ReceiverElement(Koto left, BoundType? element)
    {
        // An owned Array element that stores a reference or handle is not an inline part either: its member is reached
        // through the stored view.
        if (KotoHelper.UnwrapParentheses(left) is IndexKoto index &&
            (ElementAccess.IsSharedElement(index) || (index.Right is not RangeKoto && index.Left.BoundType?.Kind == BoundTypeKind.Array && element?.Semantics is not (null or SemanticsKind.Owner))))
        {
            this.SharedElement(left, index.Left, element);
        }
    }

    // SPEC 13.4, 4.6.9: a string comparison reads each operand in place. An operand selected by a dynamic key, below an
    // element or through a reference has no owned static path, so it is shared-borrowed like a receiver element; each
    // receiver on its path that is such a selection is borrowed the same way.
    private void CompareInPlace(Koto operand)
    {
        if (KotoHelper.UnwrapParentheses(operand) is not BinaryKoto selection || selection is not (IndexKoto { Right: not RangeKoto } or MemberAccessKoto) ||
            IsGetterResult(selection) || ElementAccess.OwnedPathRoot(selection) is not null)
        {
            return;
        }

        this.CompareInPlace(selection.Left);
        if (ElementAccess.IsBorrowedReceiver(ElementAccess.AccessType(selection.Left)))
        {
            this.SharedElement(operand, selection.Left, selection.BoundType);
        }
    }

    // Records the one shared borrow of a Non-Copy element Place that is read in place below its receiver.
    private void SharedElement(Koto node, Koto receiver, BoundType? element)
    {
        if (element is not null && this.ProveCopy(element, node) == ConstraintProof.Refuted && SharedReadTypes.BorrowSemantics(element) is { } semantics)
        {
            var origin = this.PlaceOrigin(receiver);
            var referent = element.Semantics == SemanticsKind.Owner ? element : element.Components[0];
            this.adaptations[node] = new(ExpectedAdaptationKind.SharedBorrow, this.InternType(BoundTypeKind.Semantics, null, semantics, [referent], origin: element.Origin is { } dependency ? this.Meet(origin, dependency) : origin));
        }
    }

    // SPEC 10.2: the implicit rows of the common adaptation for a value at a fixed expected Type. Exactly one operation
    // is selected, and it is recorded once for control flow, ownership and generation. Arguments select the same rows
    // through AdaptInput; explicit borrows, projections and receivers are separate operations.
    private BoundAdaptation? ExpectedAdaptation(Koto node, BoundType actual, BoundType expected)
    {
        if (IsTransfer(node) && actual is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.ObjRef or SemanticsKind.ObjUniq })
        {
            return null; // SPEC 10.2: a transferred reference is not corrected by a later adaptation.
        }

        if (ScalarReferent(actual) is { } referent && Compatible(referent, expected))
        {
            return new(ExpectedAdaptationKind.ReferentRead, referent);
        }

        if (expected is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 })
        {
            return null;
        }

        var target = expected.Components[0];
        if (actual is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 })
        {
            if (expected.Semantics == SemanticsKind.Uniq)
            {
                // An exclusive Reborrow keeps one exclusive layer; a shared layer on the path bounds it.
                return actual.Semantics == SemanticsKind.Uniq && ReferenceEquals(actual.Components[0], target) && !ReachedThroughShared(node)
                    ? new(ExpectedAdaptationKind.Reborrow, this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Uniq, [target], origin: actual.Origin)) : null;
            }

            if (this.SharedReferenceThroughLayers(actual, target, out var layers) is not { } shared)
            {
                return null;
            }

            // A single ref layer is the reference itself: ordinary fitting Copies it.
            return layers > 1 ? new(ExpectedAdaptationKind.ReferenceRead, shared)
                : actual.Semantics == SemanticsKind.Uniq ? new(ExpectedAdaptationKind.Reborrow, shared) : null;
        }

        if (expected.Semantics == SemanticsKind.Uniq && actual.Semantics == SemanticsKind.Owner && Compatible(actual, target) && IsBarePlace(node) &&
            IsExclusivePlaceResultSource(node) && PathAuthority(node) != SemanticsKind.Ref)
        {
            // SPEC 7.1.1: the operand of a place uniq/T result designates a Place that is borrowed exclusively.
            return new(ExpectedAdaptationKind.ExclusiveBorrow, this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Uniq, [actual], origin: this.PlaceOrigin(node)));
        }

        return expected.Semantics == SemanticsKind.Ref && actual.Semantics == SemanticsKind.Owner && Compatible(actual, target) && IsBarePlace(node)
            ? new(ExpectedAdaptationKind.SharedBorrow, this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [actual], origin: this.PlaceOrigin(node))) : null;
    }

    private BoundOrigin PlaceOrigin(Koto source)
    {
        if (this.ReadsReferent(source))
        {
            return this.OriginAtom(source, OriginKind.Projection, 0);
        }

        if (KotoHelper.UnwrapParentheses(source) is ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PayloadFollow } selected)
        {
            // SPEC 13.5.5: a selected referent or payload keeps the dependencies of its reference or handle,
            // so a Reborrow depends on the referent and the parent Loan, not on the slot holding the parent.
            return selected.Left.BoundType?.Origin ?? this.PlaceOrigin(selected.Left);
        }

        if (ElementAccess.PlaceCallReference(source) is { Origin: { } published })
        {
            return published; // SPEC 7.1.1: a published Place keeps the dependencies of the returned reference.
        }

        if (ElementAccess.IndexerCall(source, false) is { } indexer && ElementAccess.PlaceCallReference(indexer) is { Origin: { } publishedElement })
        {
            return publishedElement; // SPEC 4.6.9: the element Place depends on the receiver under its contract.
        }

        source = PlaceOriginSource(source);
        return source.BoundType?.Origin ?? this.OriginAtom(PlaceOriginBinder(source), OriginKind.Projection, PlaceOriginSlot(source));
    }

    // SPEC 3.5.3 Scalar read: the safe value-reference layers of an operand are followed to their terminal Type, which
    // is read only when it is a Scalar; a non-Scalar referent, Copy or not, is never read implicitly. The node keeps its
    // reference Type.
    private BoundType? ReadReferent(Koto node, BoundType? type)
    {
        if (ScalarReferent(type) is not { } referent)
        {
            return type;
        }

        this.adaptations[node] = new(ExpectedAdaptationKind.ReferentRead, referent);
        return referent;
    }

    // The type an argument presents to adaptation: a node already read where its parameter Type was
    // expected adapts from its own reference Type, so the plan records the Copy read once.
    private BoundType ArgumentType(Koto source, BoundType actual) => this.ReadsReferent(source) ? source.BoundType ?? actual : actual;

    private bool BorrowablePlace(Koto source, BindingScope scope, bool exclusive)
    {
        source = KotoHelper.UnwrapParentheses(source);
        if (exclusive && ReachedThroughShared(source))
        {
            return false; // SPEC 15.6.2: a shared layer anywhere on the path denies an exclusive borrow.
        }

        if (source is ConversionKoto { ConversionBinding: ConversionBinding.Follow } followed)
        {
            // SPEC 13.5.5.1: the referent of uniq/T offers Read and Write, that of ref/T Read only; a shared layer
            // anywhere on the path bounds the capability to shared access.
            return !exclusive || (followed.Left.BoundType?.Semantics == SemanticsKind.Uniq && !ReachedThroughShared(followed.Left));
        }

        if (source is ConversionKoto { ConversionBinding: ConversionBinding.PayloadFollow } payload)
        {
            // SPEC 13.5.5.1: an owning path inherits the root's mutability; rc/arc/objref paths are shared.
            var handle = payload.Left.BoundType!;
            return handle.Semantics switch
            {
                SemanticsKind.ObjRef => !exclusive,
                SemanticsKind.ObjUniq => !exclusive || !ReachedThroughShared(payload.Left),
                SemanticsKind.Obj => this.BorrowablePlace(payload.Left, scope, exclusive),
                _ => !exclusive && this.BorrowablePlace(payload.Left, scope, false),
            };
        }

        if (source is InvocationKoto placeCall && ElementAccess.PlaceCallReference(placeCall) is { } publishedReference)
        {
            return !exclusive || publishedReference.Semantics == SemanticsKind.Uniq; // SPEC 7.1.1
        }

        if (source is IndexKoto userIndex && ElementAccess.IsUserIndex(userIndex))
        {
            return !exclusive || ElementAccess.IndexerCall(userIndex, true) is not null; // SPEC 4.6.9
        }

        if (source is IndexKoto { Left.BoundType.Kind: BoundTypeKind.Slice })
        {
            return !exclusive;
        }

        if (source is IndexKoto index && (ReferenceTypes.IsArray(index.Left.BoundType) || ReferenceTypes.IsDynamicArray(index.Left.BoundType)))
        {
            return !exclusive || index.Left.BoundType!.Semantics == SemanticsKind.Uniq;
        }

        if (source is MemberAccessKoto element && ReferenceTypes.IsTuple(element.Left.BoundType))
        {
            return ElementAccess.TryBorrowedTupleElement(element, out _, out _) &&
                (!exclusive || element.Left.BoundType!.Semantics == SemanticsKind.Uniq);
        }

        if (source.BoundSymbol?.Property is { } property)
        {
            if (!property.Getter.IsStandard || !this.Accessible(property.Symbol, scope, property.Getter.Access, (source as MemberAccessKoto)?.Left.BoundType))
            {
                return false;
            }

            if (exclusive && (!property.Setter.IsStandard || !this.Accessible(property.Symbol, scope, property.Setter.Access, (source as MemberAccessKoto)?.Left.BoundType)))
            {
                return false;
            }

            if (source is MemberAccessKoto access)
            {
                return access.Left.BoundType is { Kind: BoundTypeKind.Semantics } receiver
                    ? !exclusive || receiver.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq
                    : this.BorrowablePlace(access.Left, scope, exclusive);
            }

            return true;
        }

        if (source is BinaryKoto part && ElementAccess.IsSyntax(part) && ElementAccess.TryType(part, out _, out _))
        {
            return this.BorrowablePlace(part.Left, scope, exclusive);
        }

        // A closure environment binding is a Place of its own (SPEC 7.6.2), borrowable like a local.
        return source is IdentifierNameKoto && source.BoundSymbol?.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Storage or BindingSymbolKind.Capture or BindingSymbolKind.PatternCandidate && (!exclusive || Writable(source));
    }

    /// <summary>
    /// Adapts an input to a parameter Type. <paramref name="receiver"/> marks a Receiver Expression, which SPEC 7.3
    /// acquires implicitly: a new exclusive borrow of an owned Place or temporary needs no spelling there, whereas
    /// every other position requires <c>@uniq</c>/<c>@objuniq</c> whatever the access path (SPEC 15.1.5).
    /// </summary>
    private bool AdaptInput(Koto source, BoundType pattern, BoundType actual, BindingScope scope, BoundMemberPath? path, BoundType? declaringType, out BoundType adapted, out ArgumentAdaptation quality, out ArgumentOperationKind kind, bool explicitBorrow = false, bool receiver = false)
    {
        actual = this.ContractType(actual, scope);
        adapted = actual;
        quality = ArgumentAdaptation.Exact;
        kind = ArgumentOperationKind.Value;
        if (ReferenceEquals(actual, BoundType.Never))
        {
            return true; // A noncompleting argument forms no borrow or reference value.
        }

        if (!receiver && IsTransfer(source) && actual is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.ObjRef or SemanticsKind.ObjUniq } &&
            ((pattern.Kind == BoundTypeKind.Semantics && !ReferenceTypes.StorageMatches(actual, pattern)) || pattern.Kind == BoundTypeKind.Primitive))
        {
            // SPEC 10.2: an explicit @move runs first and is not corrected by a later adaptation, so a transferred
            // reference is passed only to an expectation of its own Type.
            return false;
        }

        var projected = path is not null;
        if (ObjectTypes.IsBorrow(pattern))
        {
            return !projected && this.AdaptObjectBorrow(source, pattern, actual, scope, explicitBorrow, out adapted, out quality, out kind, receiver);
        }

        if (pattern.Kind != BoundTypeKind.Semantics || pattern.Semantics is not (SemanticsKind.Ref or SemanticsKind.Uniq))
        {
            if (projected)
            {
                return false; // An owning receiver cannot acquire a sliced base.
            }

            // SPEC 10.2: where the reference does not fit but its Copy referent does, the referent is read.
            if (!this.FitsTypeAt(actual, pattern, source) && ScalarReferent(actual) is { } read && this.FitsTypeAt(read, pattern, source))
            {
                adapted = read;
                quality = ArgumentAdaptation.CrossSemanticsBorrow;
                kind = ArgumentOperationKind.CopyRead;
            }
            else if (actual.Semantics is SemanticsKind.Owner or SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc && IsBarePlace(source) && this.ProveCopy(actual, source) != ConstraintProof.Proven)
            {
                // SPEC 3.5, 10.2: a bare Place never Moves, so a Non-Copy or Copy-unproven Place is not
                // applicable by value; overload selection never transfers a bare Place.
                this.transferRequired = true;
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
            // SPEC 13.5.5.2: an explicit @ref/@uniq on a slot storing a reference or handle borrows that slot and
            // adds one layer; a temporary reference value is materialized first (SPEC 3.6.2).
            var slotUnwrapped = KotoHelper.UnwrapParentheses(source);
            if (!this.BorrowablePlace(source, scope, target == SemanticsKind.Uniq) &&
                (slotUnwrapped is IdentifierNameKoto || slotUnwrapped is ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PayloadFollow } || (target == SemanticsKind.Uniq && !(slotUnwrapped is InvocationKoto || IsGetterResult(source)))))
            {
                return false;
            }

            referent = actual;
            quality = ArgumentAdaptation.CrossSemanticsBorrow;
            kind = ArgumentOperationKind.Borrow;
            adapted = this.InternType(BoundTypeKind.Semantics, null, target, [referent], origin: this.PlaceOrigin(source));
            return true;
        }

        if (actual.Kind == BoundTypeKind.Semantics && actual.Semantics is SemanticsKind.Ref or SemanticsKind.Uniq)
        {
            if (!explicitBorrow && pattern.Components[0] is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq } && ReferenceEquals(pattern.Components[0], actual))
            {
                return false; // SPEC 10.2: no implicit borrow of a reference slot.
            }

            if (!explicitBorrow && !projected && target == SemanticsKind.Ref &&
                this.SharedReferenceThroughLayers(actual, pattern.Components[0], out var layers) is { } shared && layers > 1)
            {
                // SPEC 10.2: several reference layers yield one shared reference to the parameter's referent.
                adapted = shared;
                quality = ArgumentAdaptation.CrossSemanticsBorrow;
                kind = ArgumentOperationKind.ReferenceRead;
                return true;
            }

            if (target == SemanticsKind.Uniq && (actual.Semantics != SemanticsKind.Uniq || ReachedThroughShared(source)))
            {
                return false;
            }

            referent = actual.Components[0];
            if (actual.Semantics == SemanticsKind.Ref && !projected)
            {
                // An explicit shared string borrow (text@ref) is prepared at the call like the implicit one (SPEC 22.4).
                if (ReferenceEquals(referent, BoundType.String) && KotoHelper.UnwrapParentheses(source) is ConversionKoto { ConversionBinding: ConversionBinding.Borrow })
                {
                    kind = ArgumentOperationKind.Borrow;
                }

                return true;
            }

            quality = actual.Semantics == target ? ArgumentAdaptation.SameSemanticsReborrow : ArgumentAdaptation.CrossSemanticsBorrow;
            kind = ArgumentOperationKind.Reborrow;
        }
        else if (actual.Semantics == SemanticsKind.Owner)
        {
            var exclusive = target == SemanticsKind.Uniq;
            var unwrapped = KotoHelper.UnwrapParentheses(source);
            if (this.BorrowablePlace(source, scope, exclusive))
            {
                // SPEC 15.1.5 lending rule: an owned Place is lent exclusively by @uniq at every position other
                // than a Receiver Expression, whatever its access path; a receiver is acquired implicitly (SPEC 7.3).
                if (exclusive && !explicitBorrow && !receiver)
                {
                    this.lendingRequired = true;
                    return false;
                }
            }
            else if (unwrapped is ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PayloadFollow } || ElementAccess.IsPlaceCall(unwrapped) ||
                (unwrapped is IndexKoto userIndex && ElementAccess.IsUserIndex(userIndex)) || // SPEC 4.6.9: a published element Place is never a temporary.
                (!((source.BoundSymbol is null || unwrapped is InvocationKoto || (!exclusive && IsGetterResult(source))) &&
                (!exclusive || ((explicitBorrow || receiver) && !(unwrapped is BinaryKoto stored && ElementAccess.IsSyntax(stored)))) &&
                !(unwrapped is MemberAccessKoto tupleElement && ReferenceTypes.IsTuple(tupleElement.Left.BoundType)) &&
                unwrapped is not IdentifierNameKoto && source.BoundType is { } temporary && !ReferenceEquals(temporary, BoundType.Never)) &&
                !(target == SemanticsKind.Ref && IsUnfittedLiteral(source))))
            {
                return false;
            }

            // SPEC 10.2: an owner temporary, including a defaulted literal, may be shared-borrowed; its
            // exclusive borrow is explicit or implicit for a receiver (SPEC 3.6.2, 7.3). A getter result
            // is a MemberAccessKoto with a Symbol and is never exclusively acquired (SPEC 11.2.3).
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

        adapted = this.InternType(BoundTypeKind.Semantics, null, target, [referent], origin: this.PlaceOrigin(source));
        return true;
    }

    private bool AdaptObjectBorrow(Koto source, BoundType pattern, BoundType actual, BindingScope scope, bool explicitOwner, out BoundType adapted, out ArgumentAdaptation quality, out ArgumentOperationKind kind, bool receiver = false)
    {
        adapted = actual;
        quality = ArgumentAdaptation.Exact;
        kind = ArgumentOperationKind.Value;
        var exclusive = pattern.Semantics == SemanticsKind.ObjUniq;
        if (ObjectTypes.IsBorrow(actual))
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
        else if ((actual.Semantics == SemanticsKind.Obj || (explicitOwner && !exclusive && actual.Semantics is SemanticsKind.Rc or SemanticsKind.Arc)) &&
            this.BorrowablePlace(source, scope, exclusive))
        {
            if (exclusive && !explicitOwner && !receiver)
            {
                // SPEC 15.1.5 lending rule: an owned handle is lent exclusively by @objuniq except as a receiver (SPEC 7.3).
                this.lendingRequired = true;
                return false;
            }

            kind = ArgumentOperationKind.Borrow;
            quality = ArgumentAdaptation.CrossSemanticsBorrow;
        }
        else
        {
            return false;
        }

        adapted = this.InternType(BoundTypeKind.Semantics, null, pattern.Semantics, [actual.Components[0]], origin: this.PlaceOrigin(source));
        return true;
    }
}
