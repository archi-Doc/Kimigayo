// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Place authority (SPEC 3.4, 3.4.3, 3.5, 3.6, 13.5.5, 15.1.5, 15.6.2): what an expression is as a Place and what its path grants.
// - Responsibility: whether an expression is a bare Place (IsBarePlace) or a Temporary Value a borrow may materialize
//   (BorrowableTemporary); the access a Place's path grants (PathAuthority: Owner, Uniq or Ref) and the one diagnostic for a
//   capability it denies (AccessFailure), and continued through the Place's own layers (ReferentAuthority); whether a Place offers
//   Take (OffersTake, and TakeJudgment for a repair candidate) and may be borrowed in a mode (BorrowablePlace, BorrowableHandle); and
//   the in-place read Binding records for a receiver selected below a Place (ReceiverThroughLayers, ReceiverElement, SharedElement,
//   SharedElementView; ElementRead through the layers an element stores) or for a compared operand (CompareInPlace).
// - Inputs: the bound syntax (Types, symbols, conversions), the selected index and getter calls, the pair-layer facts (PairAdmitted,
//   PairCapability) and a named root's mutability (Writable).
// - Outputs: the predicates above, and the recorded reads in `adaptations` (SharedBorrow or ReferenceRead), which
//   ElementAccess.AccessType and IsSynthesizedAccess publish to Binding, the HIR publisher and the old paths.
// - Invariants: one path computation decides writes, Moves and borrows; a shared layer anywhere on a path bounds it to Read; a node
//   keeps its own Type, and a recorded read only states how the node is read in place: one shared reference to the Type declaring
//   the member, never a layer (a path's layer below a Place is its own Type's, LayerAuthority).
// - Prohibitions: nothing here reports a diagnostic (callers record the failure AccessFailure names), selects a candidate or changes
//   a Type.
public sealed partial class Binding
{
    // SPEC 15.1.5, 23.1.3: whether a bare Place that needs @move offers Take and is a Movable Place, judged from its path alone
    // for a repair candidate: an owned path from a local, parameter or capture root is verified; a borrowed, published or shared
    // path, a Property's storage, a Pattern candidate and an element outside a static Move Path (SPEC 15) are refuted; a raw
    // referent takes with (*p)@move (SPEC 5.2.3); any other path is left to the check of the edited input.
    internal static AcquisitionJudgment TakeJudgment(Koto place)
    {
        place = KotoHelper.UnwrapParentheses(place);
        if (place is DereferenceKoto || (place is IndexKoto raw && ReferenceTypes.IsPointer(raw.Left.BoundType)))
        {
            return AcquisitionJudgment.Verified;
        }

        if (!OffersTake(place))
        {
            return AcquisitionJudgment.Refuted;
        }

        for (var depth = 0; depth < 64; depth++)
        {
            switch (place)
            {
                case IdentifierNameKoto { BoundSymbol.Kind: BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Capture }:
                    return AcquisitionJudgment.Verified;
                case IdentifierNameKoto:
                    return AcquisitionJudgment.Refuted;
                case IndexKoto index when index.Left.BoundType?.Kind == BoundTypeKind.FixedArray && KotoHelper.UnwrapParentheses(index.Right) is NumberLiteralKoto:
                    place = KotoHelper.UnwrapParentheses(index.Left);
                    break;
                case IndexKoto or InvocationKoto:
                    return AcquisitionJudgment.Refuted;
                case MemberAccessKoto { BoundSymbol.Property.Getter.IsStandard: false }:
                    return AcquisitionJudgment.Refuted;
                case MemberAccessKoto member:
                    place = KotoHelper.UnwrapParentheses(member.Left);
                    break;
                default:
                    return AcquisitionJudgment.Required;
            }
        }

        return AcquisitionJudgment.Required;
    }

    // SPEC 3.4, 13.5.5, 15.1.5: the access a Place's path grants, computed once for writes, Moves and borrows. Owner is a
    // direct path (a local, parameter or static and their inline parts), Uniq a path through an exclusive reference or
    // objuniq view, and Ref a path through a shared layer: a ref reference, a Slice element, an objref, rc or arc view or
    // a guard candidate. A shared layer anywhere on the path bounds it to shared access; the path never changes the Type. Below a
    // base Place, the layers are those of the base's own Type (SPEC 3.4.3); a recorded in-place read of it is representation.
    internal static SemanticsKind PathAuthority(Koto source)
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
                case ConversionKoto { ConversionBinding: ConversionBinding.PairFollow } pair:
                    // SPEC 13.5.5: the weakest admitted layer. An admitted owner keeps the operand's own path.
                    next = pair.Left;
                    var admitted = pair.CodeContext.Compilation.Binding.PairAdmitted(pair);
                    layer = (admitted & SemanticsMask.Ref) != 0 ? SemanticsKind.Ref : (admitted & SemanticsMask.Owner) == 0 ? SemanticsKind.Uniq : null;
                    break;
                case IndexKoto userIndex when ElementAccess.IsUserIndex(userIndex):
                    // SPEC 3.4.3, 4.7.1: the element Place is reached through the receiver's reference layers; indexUniq
                    // publishes it exclusively, index alone shares it, and the element never offers Take.
                    if (!userIndex.CodeContext.Compilation.Binding.HasExclusiveIndexer(userIndex) || HasSharedLayer(userIndex.Left.BoundType))
                    {
                        return SemanticsKind.Ref;
                    }

                    authority = SemanticsKind.Uniq;
                    next = userIndex.Left;
                    break;
                case IndexKoto index when index.Left.BoundType?.Kind == BoundTypeKind.Slice ||
                    index.Left.BoundType is { Kind: BoundTypeKind.Semantics, Components: [{ Kind: BoundTypeKind.Slice }] }:
                    return SemanticsKind.Ref; // SPEC 4.8.2: a Slice element Place is shared.
                case IndexKoto index when ReferenceTypes.IsArray(index.Left.BoundType) || ReferenceTypes.IsDynamicArray(index.Left.BoundType) || ReferenceTypes.IsDictionary(index.Left.BoundType):
                    next = index.Left;
                    layer = index.Left.BoundType!.Semantics;
                    break;
                case MemberAccessKoto member when ElementAccess.BorrowedPathRoot(member) is { } root:
                    next = root;
                    layer = LayerAuthority(KotoHelper.UnwrapParentheses(root).BoundType);
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

    // SPEC 3.4.3, 15.6.2: the access a Place's path grants to what the Place's own safe reference layers reach: its path's, bounded
    // by a shared layer of the Place's Type and exclusive through an exclusive one.
    internal static SemanticsKind ReferentAuthority(Koto place)
        => (PathAuthority(place), LayerAuthority(place.BoundType)) switch
        {
            (SemanticsKind.Ref, _) or (_, SemanticsKind.Ref) => SemanticsKind.Ref,
            (_, SemanticsKind.Uniq) => SemanticsKind.Uniq,
            var (path, _) => path,
        };

    // SPEC 3.4.3: whether selection through the Type's reference layers passes a shared layer.
    internal static bool HasSharedLayer(BoundType? type)
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

    // SPEC 15.6.2: access through a shared reference cannot grant exclusive
    // authority, even to an exclusive reference stored below it.
    private static bool ReachedThroughShared(Koto source) => PathAuthority(source) == SemanticsKind.Ref;

    // SPEC 3.4.3, 15.6.2: the layer a Place's own Type adds to a path below the Place: Ref when its reference layers pass a shared
    // one, Uniq for an exclusive reference or objuniq view, none for an owned Type.
    private static SemanticsKind? LayerAuthority(BoundType? type)
        => HasSharedLayer(type) ? SemanticsKind.Ref
            : type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq } ? SemanticsKind.Uniq : null;

    // SPEC 3.4, 15.1.5: the one diagnostic for a capability that a Place's path denies. A shared layer grants Read only, an
    // exclusive reference grants Read and Write but not Take, and otherwise the binding itself forbids the access.
    private static BindingFailure AccessFailure(Koto target, bool take = false)
        => PathAuthority(target) switch
        {
            SemanticsKind.Ref => BindingFailure.SharedPathAccess,
            SemanticsKind.Uniq when take => BindingFailure.ExclusivePathTake,
            _ => BindingFailure.InvalidAssignment,
        };

    // SPEC 15.1.5, 13.5.5: only an owned path offers Take; a guard candidate, a followed referent or payload and a followed
    // pair layer never do, even when the layer admits owner.
    private static bool OffersTake(Koto source)
    {
        var place = KotoHelper.UnwrapParentheses(source);
        return place.BoundSymbol?.Kind != BindingSymbolKind.PatternCandidate &&
            place is not ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PayloadFollow or ConversionBinding.PairFollow } &&
            (!IsBarePlace(place) || PathAuthority(place) == SemanticsKind.Owner);
    }

    // SPEC 3.5: a bare Place, as opposed to a Temporary Value or an explicit @ operation. Only a Place's
    // acquisition is restricted by the lending rule; a temporary transfers its ownership freely.
    private static bool IsBarePlace(Koto source)
    {
        source = KotoHelper.UnwrapParentheses(source);
        return source switch
        {
            IdentifierNameKoto => source.BoundSymbol?.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Storage or BindingSymbolKind.Capture or BindingSymbolKind.PatternCandidate,
            ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PayloadFollow or ConversionBinding.PairFollow } => true, // SPEC 13.5.5: a selected referent is a Place.
            MemberAccessKoto member => ElementAccess.AccessType(member.Left) is var receiver &&
                ((member.BoundSymbol?.Property is { Getter.IsStandard: true } && AdtDef.IsStruct(receiver?.Kind == BoundTypeKind.Semantics ? receiver.Components[0] : receiver)) ||
                ReferenceTypes.IsTuple(receiver) || receiver?.Kind == BoundTypeKind.Tuple), // SPEC 3.4.3: also through the receiver's recorded reference.
            IndexKoto index => !ElementAccess.IsSlicing(index) && (index.Left.BoundType?.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Slice or BoundTypeKind.Array or BoundTypeKind.Dictionary ||
                ReferenceTypes.IsArray(index.Left.BoundType) || ReferenceTypes.IsDynamicArray(index.Left.BoundType) || ReferenceTypes.IsDictionary(index.Left.BoundType) ||
                ElementAccess.IsUserIndex(index)), // SPEC 4.7.1, including a Place published by a user index; a range selection is a value
            _ => false,
        };
    }

    // SPEC 3.6, 10.2: a Temporary Value, which a borrow materializes once in a Temporary Place with the ordinary temporary
    // lifetime. A newly owned temporary has exclusive writable capability, lent exclusively only when spelled or as a Receiver
    // Expression (SPEC 7.3, 15.1.5), and a getter result only shared (SPEC 11.5). A selected referent or payload, a published
    // Place, a user index Place (SPEC 4.7.1), a named binding and a Tuple element are Places, never temporaries.
    private static bool BorrowableTemporary(Koto source, bool exclusive, bool spelled, bool fixedExpectation, bool callableValue = false)
    {
        var unwrapped = KotoHelper.UnwrapParentheses(source);
        if (unwrapped is ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PayloadFollow or ConversionBinding.PairFollow } ||
            (unwrapped is IndexKoto userIndex && ElementAccess.IsUserIndex(userIndex)))
        {
            return false;
        }

        return (source.BoundSymbol is null || callableValue || unwrapped is InvocationKoto || (!exclusive && IsGetterResult(source))) &&
            (!exclusive || (spelled && !(unwrapped is BinaryKoto stored && ElementAccess.IsSyntax(stored)))) &&
            !(unwrapped is MemberAccessKoto tupleElement && ReferenceTypes.IsTuple(tupleElement.Left.BoundType)) &&
            (unwrapped is not IdentifierNameKoto || callableValue) &&
            (fixedExpectation || (source.BoundType is { } temporary && !ReferenceEquals(temporary, BoundType.Never)));
    }

    // SPEC 3.4.3: a member or Tuple element selected through several reference layers is reached through one reference
    // to the Type that declares it: shared when any layer is shared, exclusive otherwise, with the Origins of 10.2. The
    // receiver keeps its own Type; the recorded reference is what ownership loads and generation addresses.
    private BoundType? ReceiverThroughLayers(Koto left, BoundType? actual)
    {
        if (actual is not
            {
                Kind: BoundTypeKind.Semantics,
                Semantics: SemanticsKind.Ref or SemanticsKind.Uniq,
                Components: [{ Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.ObjRef, Components: [_] }],
            })
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

        // A stored shared object view is Copy: reading it preserves its own external Origin and header identity,
        // without borrowing its payload from the outer reference slot or granting exclusive authority.
        if (terminal.Semantics == SemanticsKind.ObjRef)
        {
            this.adaptations[left] = new(ExpectedAdaptationKind.ReferenceRead, terminal);
            return terminal;
        }

        if ((terminal is not { Kind: BoundTypeKind.Tuple } && !AdtDef.IsStruct(terminal)) || this.SharedReferenceThroughLayers(actual, terminal, out _) is not { } shared)
        {
            return null;
        }

        var reference = exclusive ? this.types.Intern(BoundTypeKind.Semantics, null, SemanticsKind.Uniq, [terminal], origin: shared.Origin) : shared;
        this.adaptations[left] = new(ExpectedAdaptationKind.ReferenceRead, reference);
        return reference;
    }

    // SPEC 3.4.3, 4.7.1, 4.8.2: a member selected below a user index's element, or below a Non-Copy element of a Slice or borrowed
    // array, is reached through one shared read of that element Place, continued through the layers it stores (ElementRead). The
    // element keeps its stored Type; the read is the receiver's adaptation.
    private void ReceiverElement(Koto left, BoundType? element)
    {
        if (ElementAccess.IsUserIndex(left) && element is not null)
        {
            var origin = this.PlaceOrigin(left);
            var read = this.ElementRead(element, origin) ?? this.types.Reference(SemanticsKind.Ref, element, origin);
            this.adaptations[left] = new(ExpectedAdaptationKind.SharedBorrow, read);
            return;
        }

        // An owned Array element that stores a reference or handle is not an inline part either: its member is reached
        // through the stored view.
        // A Non-Copy element published by a user index (SPEC 4.7.1) is likewise read through one shared borrow of its Place.
        if (KotoHelper.UnwrapParentheses(left) is IndexKoto index &&
            (ElementAccess.IsSharedElement(index) || ElementAccess.IsUserIndex(index) ||
                (index.Right is not RangeKoto && index.Left.BoundType?.Kind == BoundTypeKind.Array && element?.Semantics is not (null or SemanticsKind.Owner))))
        {
            this.SharedElement(left, index.Left, element);
        }
    }

    // SPEC 13.4, 4.7.1: a string comparison reads each operand in place. An operand selected by a dynamic key, below an
    // element or through a reference has no owned static path, so it is shared-borrowed like a receiver element; each
    // receiver on its path that is such a selection is borrowed the same way.
    private void CompareInPlace(Koto operand)
    {
        if (ElementAccess.IsUserIndex(operand) && operand.BoundType is { } published && this.ProveCopy(published, operand) == ConstraintProof.Refuted &&
            SharedReadTypes.BorrowSemantics(published) is { } placeSemantics)
        {
            // SPEC 4.7.1, 13.4: a Non-Copy element of receiver[key] on a user Type is compared in place through the reference
            // its selected call returns.
            this.adaptations[operand] = new(ExpectedAdaptationKind.SharedBorrow, this.types.Intern(BoundTypeKind.Semantics, null, placeSemantics, [published], origin: this.PlaceOrigin(operand)));
            return;
        }

        if (KotoHelper.UnwrapParentheses(operand) is not BinaryKoto selection || selection is not (IndexKoto { Right: not RangeKoto } or MemberAccessKoto) ||
            IsGetterResult(selection) || ElementAccess.OwnedPathRoot(selection) is not null)
        {
            return;
        }

        // PLAN G59: an owned fixed-array element at a runtime index is compared in place like an element of a borrowed receiver.
        this.CompareInPlace(selection.Left);
        if (ElementAccess.IsBorrowedReceiver(ElementAccess.AccessType(selection.Left)) || ElementAccess.IsRuntimeFixedElement(selection))
        {
            this.SharedElement(operand, selection.Left, selection.BoundType);
        }
    }

    // SPEC 4.8.1, 4.8.2: a range selection of a fixed-array element reached through a Slice or a borrowed array borrows
    // the element slot in place, whatever the element's Copy capability, so the view retains the backing source.
    private void SharedElementView(Koto receiver)
    {
        if (KotoHelper.UnwrapParentheses(receiver) is IndexKoto element && element.BoundType is { Kind: BoundTypeKind.FixedArray, Semantics: SemanticsKind.Owner } array && ElementAccess.IsSharedElement(element))
        {
            this.adaptations[element] = new(ExpectedAdaptationKind.SharedBorrow, this.types.Intern(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [array], origin: this.PlaceOrigin(element.Left)));
        }
    }

    // Records the one shared borrow of a Non-Copy element Place that is read in place below its receiver.
    private void SharedElement(Koto node, Koto receiver, BoundType? element)
    {
        if (element is not null && this.ProveCopy(element, node) == ConstraintProof.Refuted && SharedReadTypes.BorrowSemantics(element) is { } semantics)
        {
            var origin = this.PlaceOrigin(receiver);
            var referent = element.Semantics == SemanticsKind.Owner ? element : element.Components[0];
            var dependency = element.Origin is { } stored ? this.Meet(origin, stored) : origin;
            var read = this.ElementRead(element, origin) ?? this.types.Intern(BoundTypeKind.Semantics, null, semantics, [referent], origin: dependency);
            this.adaptations[node] = new(ExpectedAdaptationKind.SharedBorrow, read);
        }
    }

    // SPEC 3.4.3, 10.2: the in-place read of an element storing safe reference layers, below which a member is selected: one shared
    // reference to the declaring struct or Tuple, the element's selection reference (with the selection's Origin) continued through
    // those layers; null for any other element. It is the selection's representation, never a layer (ElementAccess.IsSynthesizedAccess).
    private BoundType? ElementRead(BoundType element, BoundOrigin selection)
        => ComparisonReferent(element) is var terminal && !ReferenceEquals(terminal, element) &&
            (terminal.Kind == BoundTypeKind.Tuple || AdtDef.IsStruct(terminal))
            ? this.SharedReferenceThroughLayers(this.types.Reference(SemanticsKind.Ref, element, selection), terminal, out _) : null;

    // SPEC 13.5.5, 13.5.6: an owning handle lends its object or payload from a borrowable Place or from a Temporary Value
    // materialized for the operation; the handle's own authority (shared for rc/arc) is checked by the caller.
    private bool BorrowableHandle(Koto source, BindingScope scope, bool exclusive, bool spelled, bool fixedExpectation = false)
        => this.BorrowablePlace(source, scope, exclusive) || BorrowableTemporary(source, exclusive, spelled, fixedExpectation);

    private bool BorrowablePlace(Koto source, BindingScope scope, bool exclusive)
    {
        source = KotoHelper.UnwrapParentheses(source);
        if (exclusive && ReachedThroughShared(source))
        {
            return false; // SPEC 15.6.2: a shared layer anywhere on the path denies an exclusive borrow.
        }

        if (source is ConversionKoto { ConversionBinding: ConversionBinding.Follow } followed)
        {
            // SPEC 13.5.5: the referent of uniq/T offers Read and Write, that of ref/T Read only; a shared layer
            // anywhere on the path bounds the capability to shared access.
            return !exclusive || (followed.Left.BoundType?.Semantics == SemanticsKind.Uniq && !ReachedThroughShared(followed.Left));
        }

        if (source is ConversionKoto { ConversionBinding: ConversionBinding.PairFollow } pair)
        {
            return this.PairCapability(pair, scope, exclusive); // SPEC 13.5.5: the weakest admitted capability.
        }

        if (source is ConversionKoto { ConversionBinding: ConversionBinding.PayloadFollow } payload)
        {
            // SPEC 13.5.5: an owning path inherits the root's mutability; rc/arc/objref paths are shared.
            var handle = payload.Left.BoundType!;
            return handle.Semantics switch
            {
                SemanticsKind.ObjRef => !exclusive,
                SemanticsKind.ObjUniq => !exclusive || !ReachedThroughShared(payload.Left),
                SemanticsKind.Obj => this.BorrowableHandle(payload.Left, scope, exclusive, true),
                _ => !exclusive && this.BorrowableHandle(payload.Left, scope, false, true),
            };
        }

        if (source is IndexKoto userIndex && ElementAccess.IsUserIndex(userIndex))
        {
            return !exclusive || ElementAccess.IndexerCall(userIndex, true) is not null; // SPEC 4.7.1
        }

        if (source is IndexKoto { Left.BoundType.Kind: BoundTypeKind.Slice })
        {
            return !exclusive;
        }

        if (source is IndexKoto index && (ReferenceTypes.IsArray(index.Left.BoundType) || ReferenceTypes.IsDynamicArray(index.Left.BoundType) || ReferenceTypes.IsDictionary(index.Left.BoundType)))
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
                // SPEC 16.3.1: the destruction receiver has access equivalent to uniq/Self, so its initialized fields
                // may be borrowed exclusively although the receiver itself is not an assignable binding.
                if (IsSpecialField(access, out var special) && special.IsDestructor)
                {
                    return true;
                }

                if (access.Left.BoundType?.HandleMode is { } handle)
                {
                    return !exclusive || (handle.PayloadAuthority == LoanRequirement.Uniq && this.BorrowableHandle(access.Left, scope, true, true));
                }

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

        // A closure environment binding is a Place of its own (SPEC 7.7.2), borrowable like a local.
        return source is IdentifierNameKoto && source.BoundSymbol?.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Storage or BindingSymbolKind.Capture or BindingSymbolKind.PatternCandidate && (!exclusive || Writable(source));
    }
}
