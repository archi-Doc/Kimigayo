// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Place authority (SPEC 3.4, 3.4.3, 3.5, 3.6, 13.5.5, 15.1.5, 15.6.2): what an expression is as a Place and what its path grants.
// - Responsibility: whether an expression is a bare Place (IsBarePlace), a static Field (IsStaticField), static storage outside its
//   accessor (StaticStorageAccess) or a Temporary Value a borrow may materialize (BorrowableTemporary); the steps of a Place's path
//   (NextPathStep, the one enumeration of their forms); the access a Place's path grants (PathAuthority: Owner, Uniq or Ref) under a
//   case assignment (SPEC 8.15.2: each assigned pair binder's layers take its case, every other layer its weakest case; the empty
//   assignment is every layer at its weakest case, which an operation valid in every case reads) and the
//   one diagnostic for a capability it denies (AccessFailure), and continued through the Place's own layers (ReferentAuthority);
//   whether a Place offers Write (Writable); whether a receiver can be exclusively acquired for indexUniq or an exclusive view
//   (ExclusivelyAcquirable, SPEC 15.1.5); whether a Place offers Take (OffersTake, and TakeJudgment for a repair candidate) and may be
//   borrowed in a mode (BorrowablePlace; as SPEC lends it: LendsPlace and, for a handle, LendsHandle, with the static root), with the
//   one diagnostic for a written Borrow it may not (BorrowFailure) and for a final implicit Borrow of a static root (FailStaticBorrow).
//   The in-place reads it decides the paths of are Binding.InPlaceReads'.
// - Inputs: the bound syntax (Types, symbols, conversions), the selected index and getter calls, the pair-layer facts (PairAdmitted,
//   PairCapability), a named root's reassignability (Reassignable) and a special receiver's authority (Receiver).
// - Outputs: the predicates above.
// - Invariants: one path computation decides writes, Moves and borrows; a shared layer anywhere on a path bounds it to Read; a path's
//   layer below a Place is its own Type's (LayerAuthority).
// - Prohibitions: nothing here reports a diagnostic but FailStaticBorrow, called where an implicit Borrow's operation is final (callers
//   record the failure AccessFailure or BorrowFailure names), selects a candidate or changes a Type.
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

    // SPEC 3.4, 13.5.5, 15.1.5: the access a Place's path grants under a case assignment (SPEC 8.15.2), computed once for writes,
    // Moves and borrows. Owner is a direct path (a local, parameter or static and their inline parts), Uniq a path through an
    // exclusive reference or objuniq view or rooted at the destruction receiver (Receiver), and Ref a path through a shared layer: a
    // ref reference, a Slice element, an objref, rc or arc view, a pair layer in a shared case or a guard candidate. A shared layer
    // anywhere on the path bounds it to shared access; the path never changes the Type. Below a base Place, the layers are those of
    // the base's own Type (SPEC 3.4.3); a recorded in-place read of it is representation.
    internal static SemanticsKind PathAuthority(Koto source, ReadOnlySpan<PairCase> assignment)
    {
        var path = PathAuthority(source, assignment, out var bindingDenies, out _);
        return bindingDenies ? SemanticsKind.Ref : path;
    }

    // SPEC 3.4.3, 15.6.2: the access a Place's path grants to what the Place's own safe reference layers reach: its path's, bounded
    // by a shared layer of the Place's Type and exclusive through an exclusive one.
    internal static SemanticsKind ReferentAuthority(Koto place)
        => (PathAuthority(place, []), LayerAuthority(place, [])) switch
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
    private static bool ReachedThroughShared(Koto source) => PathAuthority(source, []) == SemanticsKind.Ref;

    // SPEC 4.7.1, 15.1.5: whether a receiver can be exclusively acquired, which a selection through indexUniq or an exclusive view
    // needs: its path is not shared (ReferentAuthority) and its binding permits it (AcquirableByBinding).
    private static bool ExclusivelyAcquirable(Koto receiver) => ReferentAuthority(receiver) != SemanticsKind.Ref && AcquirableByBinding(receiver, []);

    // The path walk of PathAuthority: the access the path's layers grant, and bindingDenies, whether a selection on it (a user index
    // or an Array element, SPEC 4.7.1) needs its receiver exclusively acquired where the receiver's binding denies that (SPEC
    // 15.1.5), so that the selection shares the element for writes and exclusive borrows. A Take acquires nothing exclusively: it
    // reads the layers alone (OffersTake, AccessFailure), and layers, the cases of the layers of receivers' own Types the path passes
    // (LayerCases), past any of which no Take is offered (SPEC 15.1.5, 13.5.5); each pair layer read under the case assignment.
    private static SemanticsKind PathAuthority(Koto source, ReadOnlySpan<PairCase> assignment, out bool bindingDenies, out SemanticsMask layers)
    {
        var authority = SemanticsKind.Owner;
        var denied = false;
        bindingDenies = false;
        layers = SemanticsMask.None;
        source = KotoHelper.UnwrapParentheses(source);
        for (var depth = 0; depth < 64; depth++)
        {
            SemanticsKind? layer = null;
            switch (NextPathStep(source, out var next))
            {
                case PathStep.Candidate:
                    return SemanticsKind.Ref;
                case PathStep.Follow:
                    layer = next.BoundType?.Semantics;
                    layers |= layer?.ToMask() ?? SemanticsMask.None;
                    break;
                case PathStep.PairFollow:
                    var admitted = source.CodeContext.Compilation.Binding.PairAdmitted(source);
                    admitted = TryPairLayer(next.BoundType, out var followed, out _) ? CaseAdmitted(followed, admitted, assignment) : admitted;
                    layer = PairAuthority(admitted);
                    layers |= admitted;
                    break;
                case PathStep.UserIndex:
                    // SPEC 3.4.3, 4.7.1, 15.1.5: the element Place is reached through the receiver's reference layers; indexUniq
                    // publishes it exclusively when the receiver can be exclusively acquired, index alone shares it, and the
                    // element never offers Take.
                    if (!source.CodeContext.Compilation.Binding.HasExclusiveIndexer(source))
                    {
                        return SemanticsKind.Ref;
                    }

                    authority = SemanticsKind.Uniq;
                    denied |= !AcquirableByBinding(next, assignment);
                    layer = LayerAuthority(next, assignment);
                    break;
                case PathStep.SliceElement:
                    return SemanticsKind.Ref; // SPEC 4.8.2: a Slice element Place is shared.
                case PathStep.BorrowedElement:
                    layer = next.BoundType!.Semantics;
                    break;
                case PathStep.ArrayElement:
                    // SPEC 4.7.1: an Array element is selected like a user index's, an inline part of its owner's path when
                    // the receiver can be exclusively acquired.
                    denied |= !AcquirableByBinding(next, assignment);
                    break;
                case PathStep.Member:
                    layer = LayerAuthority(next, assignment); // SPEC 3.4.3: a member is reached through every layer of its receiver's own Type.
                    layers |= LayerCases(next, assignment);
                    break;
                case PathStep.InlinePart:
                    break; // An inline part shares its owner's path.
                case PathStep.Receiver:
                    bindingDenies = denied; // SPEC 6.3.4, 16.3.1: a special receiver's path grants its row's authority.
                    return Receiver(source)?.Parts == SemanticsKind.Uniq ? SemanticsKind.Uniq : authority;
                default:
                    // A root, or a raw dereference: no raw Origin denies an access (SPEC 5.7 Authority; condition 3 of SPEC 5.2.1).
                    bindingDenies = denied;
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

        bindingDenies = denied;
        return authority;
    }

    // SPEC 3.4.3, 4.7.1, 4.8.2, 5.2, 13.5.5: one step of a Place's path from the leaf toward its root, the one enumeration of a path's
    // forms that PathAuthority, PathOrigin and ContainedOrigin walk: what `source` selects, and next, the node it selects from (`source` itself at a
    // root, a guard candidate or a special receiver). Exception (DECISIONS BU-24): PlaceOriginSource keeps its own walk, which passes
    // forms this treats as roots (an Array range selection).
    private static PathStep NextPathStep(Koto source, out Koto next)
    {
        next = source;
        switch (source)
        {
            case IdentifierNameKoto { BoundSymbol.Kind: BindingSymbolKind.PatternCandidate }:
                return PathStep.Candidate;
            case ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PayloadFollow } selected:
                next = selected.Left;
                return PathStep.Follow;
            case ConversionKoto { ConversionBinding: ConversionBinding.PairFollow } pair:
                next = pair.Left;
                return PathStep.PairFollow;
            case DereferenceKoto dereference:
                next = dereference.Operand;
                return PathStep.RawFollow;
            case IndexKoto raw when ReferenceTypes.IsPointer(raw.Left.BoundType):
                next = raw.Left;
                return PathStep.RawFollow;
            case IndexKoto userIndex when ElementAccess.IsUserIndex(userIndex):
                next = userIndex.Left;
                return PathStep.UserIndex;
            case IndexKoto index when index.Left.BoundType?.Kind == BoundTypeKind.Slice ||
                index.Left.BoundType is { Kind: BoundTypeKind.Semantics, Components: [{ Kind: BoundTypeKind.Slice }] }:
                next = index.Left;
                return PathStep.SliceElement;
            case IndexKoto index when ReferenceTypes.IsArray(index.Left.BoundType) || ReferenceTypes.IsDynamicArray(index.Left.BoundType) || ReferenceTypes.IsDictionary(index.Left.BoundType):
                next = index.Left;
                return PathStep.BorrowedElement;
            case IndexKoto element when element.Left.BoundType?.Kind == BoundTypeKind.Array && !ElementAccess.IsSlicing(element):
                next = element.Left;
                return PathStep.ArrayElement;
            case MemberAccessKoto member when !IsGetterResult(member):
                next = member.Left;
                return PathStep.Member;
            case BinaryKoto part when ElementAccess.IsSyntax(part) && ElementAccess.TryType(part, out _, out _):
                next = part.Left;
                return PathStep.InlinePart;
            case IdentifierNameKoto root when Receiver(root) is not null:
                return PathStep.Receiver;
            default:
                return PathStep.Root;
        }
    }

    // SPEC 3.4, 5.2, 13.5.5, 15.6.2: whether a Place offers Write: a writable root or an inline part of one, an exclusive path, a raw
    // Place, a setter or an indexUniq selection; a shared layer anywhere on the path grants Read only.
    private static bool Writable(Koto node)
    {
        node = KotoHelper.UnwrapParentheses(node);
        if (node.BoundSymbol?.Property is { IsStored: false, Setter.IsPresent: true })
        {
            return true; // The accessor's Receiver Expression checks its own capability.
        }

        // SPEC 5.2, 5.7: binding mutability does not decide pointee write permission, nor does any raw Origin (condition 3 of SPEC 5.2.1
        // bounds the access at run time); a safe shared layer inside the pointee does.
        if (node is DereferenceKoto dereference)
        {
            return ReferenceTypes.IsPointer(dereference.Operand.BoundType) && !ReachedThroughShared(node);
        }

        if ((node is IndexKoto index && ReferenceTypes.IsPointer(index.Left.BoundType)) || ElementAccess.IsPointerPath(node))
        {
            return !ReachedThroughShared(node);
        }

        if (node.BoundSymbol?.Kind == BindingSymbolKind.PatternCandidate || ReachedThroughShared(node))
        {
            return false; // SPEC 3.4, 15.6.2: a shared layer anywhere on the path grants Read only.
        }

        if (node is ConversionKoto { ConversionBinding: ConversionBinding.Follow } selected)
        {
            // SPEC 13.5.5: the referent of uniq/T offers Write; a shared layer on the path bounds it.
            return selected.Left.BoundType?.Semantics == SemanticsKind.Uniq && !ReachedThroughShared(selected.Left);
        }

        if (node is ConversionKoto { ConversionBinding: ConversionBinding.PairFollow } pair)
        {
            return pair.CodeContext.Compilation.Binding.PairCapability(pair, null, true); // SPEC 13.5.5 pair layers
        }

        if (node is ConversionKoto { ConversionBinding: ConversionBinding.PayloadFollow } payload)
        {
            return payload.Left.BoundType?.Semantics switch
            {
                SemanticsKind.ObjUniq => !ReachedThroughShared(payload.Left),
                SemanticsKind.Obj => Writable(payload.Left),
                _ => false,
            };
        }

        if (node is IndexKoto userIndex && ElementAccess.IsUserIndex(userIndex))
        {
            return ElementAccess.IndexerCall(userIndex, true) is not null; // SPEC 4.7.1: an update selects indexUniq.
        }

        if (ElementAccess.IsExclusiveArrayElement(node))
        {
            return true;
        }

        if (node is MemberAccessKoto { Right: NumberLiteralKoto } nested && !ReferenceTypes.IsTuple(ElementAccess.AccessType(nested.Left)) &&
            ElementAccess.BorrowedPathRoot(nested) is { } root)
        {
            return ElementAccess.AccessType(root, true)!.Semantics == SemanticsKind.Uniq; // An inline Tuple level below a borrowed base.
        }

        if (node is MemberAccessKoto tupleElement && ReferenceTypes.IsTuple(ElementAccess.AccessType(tupleElement.Left)))
        {
            return ElementAccess.AccessType(tupleElement.Left, true)!.Semantics == SemanticsKind.Uniq && ElementAccess.TryBorrowedTupleElement(tupleElement, out _, out _);
        }

        if (node is MemberAccessKoto { BoundSymbol.Property.IsStored: true } throughLayers &&
            node.CodeContext.Compilation.Binding.TryGetAdaptation(throughLayers.Left, out var receiver) && receiver.Kind == ExpectedAdaptationKind.ReferenceRead)
        {
            return receiver.Type.Semantics == SemanticsKind.Uniq; // SPEC 3.4.3: a shared layer bounds the path to shared access.
        }

        // SPEC 13.5.5: the receiver's own Type adds no exclusive or shared layer, so the Field lies inline in its storage, past an
        // owning handle or in the owner case of a pair layer too (no member is selected through a raw pointer).
        if (node is MemberAccessKoto { BoundSymbol.Property.IsStored: true } field &&
            LayerAuthority(field.Left, []) is null && AdtDef.IsStruct(DeclaringType(field.Left.BoundType)) &&
            Receiver(field.Left) is null && !Writable(field.Left) && ElementAccess.WritableRoot(field.Left) is null)
        {
            return false; // A mutable field still requires a mutable owning root.
        }

        if (node.BoundSymbol is { Kind: BindingSymbolKind.Storage, Scope.Owner: PropertyAccessorKoto syntax })
        {
            var accessor = Accessor(syntax);
            if (accessor.Receiver is { Semantics: not SemanticsKind.Uniq })
            {
                return false;
            }
        }

        return node.BoundSymbol is { } symbol && Reassignable(symbol);
    }

    // SPEC 15.1.5: whether a receiver's own binding permits exclusive acquisition, which storage rooted in a let binding or an owned
    // parameter does not: an exclusive reference (also held in a let binding), a writable Place, an inline part of a writable root
    // or a Temporary Value. Its path's shared layers are the caller's (ExclusivelyAcquirable, PathAuthority).
    private static bool AcquirableByBinding(Koto receiver, ReadOnlySpan<PairCase> assignment)
        => LayerAuthority(receiver, assignment) == SemanticsKind.Uniq || Writable(receiver) || ElementAccess.WritableRoot(receiver) is not null ||
            !IsBarePlace(receiver);

    // SPEC 3.4.3, 13.5.5, 15.6.2: the layer a Place's own Type adds to a path below the Place: Ref when its reference layers pass a
    // shared one, Uniq when they pass an exclusive reference or objuniq view, none for an owned Type or an owning obj handle (whose
    // path keeps its root's capability). A pair layer adds the weakest of its cases under the case assignment (PairAuthority).
    private static SemanticsKind? LayerAuthority(Koto place, ReadOnlySpan<PairCase> assignment)
    {
        SemanticsKind? authority = null;
        for (var type = KotoHelper.UnwrapParentheses(place).BoundType; BelowLayer(type) is { } below; type = below)
        {
            var layer = TryPairLayer(type, out var whole, out _)
                ? PairAuthority(CaseAdmitted(whole, place.CodeContext.Compilation.Binding.LayerAdmitted(type!), assignment)) : type!.Semantics;
            if (layer is SemanticsKind.Ref or SemanticsKind.ObjRef or SemanticsKind.Rc or SemanticsKind.Arc)
            {
                return SemanticsKind.Ref;
            }

            authority = layer is SemanticsKind.Uniq or SemanticsKind.ObjUniq ? SemanticsKind.Uniq : authority;
        }

        return authority;
    }

    // SPEC 3.4.3, 13.5.5: the Type below one layer of a Type (a reference, handle or object view, raw pointer or pair layer); null
    // for a Type without one.
    private static BoundType? BelowLayer(BoundType? type) => type switch
    {
        _ when TryPairLayer(type, out _, out var target) => target,
        { Kind: BoundTypeKind.Semantics, Components: [var component] } => component,
        _ => null,
    };

    // SPEC 3.4.3, 13.5.5: the cases of the layers a Place's own Type adds to a path below the Place: a pair layer's cases under the case
    // assignment and any other layer's Semantics; None for a Type without a layer.
    private static SemanticsMask LayerCases(Koto place, ReadOnlySpan<PairCase> assignment)
    {
        var cases = SemanticsMask.None;
        for (var type = KotoHelper.UnwrapParentheses(place).BoundType; BelowLayer(type) is { } below; type = below)
        {
            cases |= TryPairLayer(type, out var whole, out _)
                ? CaseAdmitted(whole, place.CodeContext.Compilation.Binding.LayerAdmitted(type!), assignment) : type!.Semantics.ToMask();
        }

        return cases;
    }

    // SPEC 8.15.2: the cases of a pair layer, of the pair whose whole Type is given, under a case assignment: its binder's assigned
    // Semantics, else its admitted set.
    private static SemanticsMask CaseAdmitted(BoundType whole, SemanticsMask admitted, ReadOnlySpan<PairCase> assignment)
        => CaseOf(assignment, whole.Symbol!) is { } assigned ? assigned.ToMask() : admitted;

    // SPEC 13.5.5: a pair layer's weakest admitted case: shared when it admits a shared one, and otherwise exclusive unless it
    // admits an owner, an owning handle or a raw pointer, which keeps the operand's own path.
    private static SemanticsKind? PairAuthority(SemanticsMask admitted) => admitted switch
    {
        _ when (admitted & (SemanticsMask.Ref | SemanticsMask.Rc | SemanticsMask.Arc | SemanticsMask.ObjRef)) != 0 => SemanticsKind.Ref,
        _ when (admitted & (SemanticsMask.Owner | SemanticsMask.Obj | SemanticsMask.Raw)) == 0 => SemanticsKind.Uniq,
        _ => null,
    };

    // SPEC 3.4, 15.1.5: the one diagnostic for a capability that a Place's path denies. A shared layer grants Read only, an
    // exclusive reference grants Read and Write but not Take, as no case of a pair layer does (SPEC 13.5.5; only a pair layer adds the
    // owner case), a writable part of a special receiver that SPEC lends exclusively but its row does not yet is unsupported
    // (Receiver, SPEC 6.3.4), and otherwise the binding itself forbids the access, also through a selection whose receiver it cannot
    // exclusively acquire, or an object payload offers no Take.
    private static BindingFailure AccessFailure(Koto target, bool take = false)
        => PathAuthority(target, [], out _, out var layers) switch
        {
            SemanticsKind.Ref => BindingFailure.SharedPathAccess,
            SemanticsKind.Uniq when take => BindingFailure.ExclusivePathTake,
            _ when take && (layers & SemanticsMask.Owner) != 0 => BindingFailure.ExclusivePathTake,
            SemanticsKind.Owner when !take && ReceiverOfPart(target) is { LendsFields: false } && Writable(target) => BindingFailure.Unsupported,
            _ => BindingFailure.InvalidAssignment,
        };

    // SPEC 15.1.5, 13.5.5: only an owned root and its inline parts offer Take, whether the binding permits exclusive acquisition or
    // not: no path past a layer of a receiver's own Type (a reference, object handle or view, or pair layer) or a guard candidate does.
    private static bool OffersTake(Koto source)
    {
        var place = KotoHelper.UnwrapParentheses(source);
        if (ElementAccess.IsRawPlace(place))
        {
            return !ReachedThroughShared(place); // SPEC 5.2.3, 5.7: a raw Place offers Take unless a safe shared layer inside it denies.
        }

        return !IsBarePlace(place) || (PathAuthority(place, [], out _, out var layers) == SemanticsKind.Owner && layers == SemanticsMask.None);
    }

    // SPEC 3.5: a bare Place, as opposed to a Temporary Value or an explicit @ operation. Only a Place's
    // acquisition is restricted by the lending rule; a temporary transfers its ownership freely.
    private static bool IsBarePlace(Koto source)
    {
        source = KotoHelper.UnwrapParentheses(source);
        return IsStaticField(source) || source switch
        {
            IdentifierNameKoto => source.BoundSymbol?.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Storage or BindingSymbolKind.Capture or BindingSymbolKind.PatternCandidate,
            ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PayloadFollow or ConversionBinding.PairFollow } => true, // SPEC 13.5.5: a selected referent is a Place.
            MemberAccessKoto member => DeclaringType(ElementAccess.AccessType(member.Left)) is var declaring &&
                ((member.BoundSymbol?.Property is { Getter.IsStandard: true } && AdtDef.IsStruct(declaring)) || declaring?.Kind == BoundTypeKind.Tuple),
            // SPEC 4.7.1: a user index publishes a Place, whatever its key; a range selection of a sequence is a value (SPEC 4.8.2).
            IndexKoto index => ElementAccess.IsUserIndex(index) || (!ElementAccess.IsSlicing(index) &&
                (index.Left.BoundType?.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Slice or BoundTypeKind.Array or BoundTypeKind.Dictionary ||
                ReferenceTypes.IsArray(index.Left.BoundType) || ReferenceTypes.IsDynamicArray(index.Left.BoundType) ||
                ReferenceTypes.IsDictionary(index.Left.BoundType))),
            _ => false,
        };
    }

    // SPEC 3.4.3: the Type declaring a member selected through a receiver: below every layer of the receiver's Type or recorded
    // reference, the View Target of a handle or object view included.
    private static BoundType? DeclaringType(BoundType? type)
    {
        while (BelowLayer(type) is { } below)
        {
            type = below;
        }

        return type;
    }

    // SPEC 3.4, 3.6, 10.2: a Temporary Value, which a borrow materializes once in a Temporary Place with the ordinary temporary
    // lifetime: an operand whose root, through parentheses and inline parts (both keep a Place's classification, SPEC 3.4, 4.7.1), is
    // no Place (IsBarePlace), such as a call result, a Case construction, a callable value or a getter
    // result; a part of it is lent with it. A newly owned temporary has exclusive writable capability, lent exclusively only when
    // spelled or as a Receiver Expression (SPEC 7.3, 15.1.5) and not through an inline part here, and a getter result only shared
    // (SPEC 11.5).
    private static bool BorrowableTemporary(Koto source, bool exclusive, bool spelled, bool fixedExpectation)
    {
        var unwrapped = KotoHelper.UnwrapParentheses(source);
        var root = unwrapped;
        while (root is BinaryKoto part && !IsGetterResult(part) && ElementAccess.IsSyntax(part) && ElementAccess.TryType(part, out _, out _))
        {
            root = KotoHelper.UnwrapParentheses(part.Left);
        }

        // SPEC 15.6.2, 5.7: as for a Place, a shared layer on the path, a raw pointer's included, denies an exclusive borrow.
        var inline = unwrapped is BinaryKoto stored && ElementAccess.IsSyntax(stored);
        return !IsBarePlace(root) &&
            (!exclusive || (spelled && !IsGetterResult(root) && !inline && !ReachedThroughShared(unwrapped))) &&
            (fixedExpectation || (source.BoundType is { } temporary && !ReferenceEquals(temporary, BoundType.Never)));
    }

    // SPEC 15.6.2, 22.4.4: a static Field, the root Place of a path through the keys of its group's storage: a group's Property read
    // through its standard getter, named qualified or in its group. IsBarePlace includes it; lending and Take keep their own rules.
    private static bool IsStaticField(Koto node)
        => KotoHelper.UnwrapParentheses(node).BoundSymbol?.Property is { Getter.IsStandard: true, Symbol.Scope.Owner: GroupKoto };

    // SPEC 7.7.2, 11.8: whether `symbol`, named in `scope`, is a static Property's `storage` outside its accessor's own function (a
    // Closure's or nested function's): a static access, neither a capture nor the accessor's binding, so a Borrow of it has a static
    // root (LendsPlace; DECISIONS BU-25's exception binds only the accessor's own Borrow of its storage).
    private static bool StaticStorageAccess(BindingSymbol symbol, BindingScope scope)
        => symbol.Kind == BindingSymbolKind.Storage && IsStatic(symbol.Declaration.BoundSymbol) && scope.Function != symbol.Scope.Function;

    // SPEC 13.5.5, 13.5.6, 15.6.2: whether SPEC lends an owning handle's object or payload, from a Place it lends (LendsPlace, with the
    // static Field at the root of the handle Place's path) or from a Temporary Value materialized for the operation; the handle's own
    // authority (shared for rc/arc) is checked by the caller.
    private bool LendsHandle(Koto source, BindingScope scope, bool exclusive, out Koto? staticRoot)
        => this.LendsPlace(source, scope, exclusive, out staticRoot) || BorrowableTemporary(source, exclusive, true, false);

    // SPEC 15.6.2, PLAN P42: whether Binding lends a Place to a written Borrow: as SPEC does (LendsPlace), except a Place rooted at a
    // static Field, whose Loans no analysis models yet (BorrowFailure names the Borrow unsupported). An implicit Borrow belongs to an
    // operation that selection may still discard, so it is lent as SPEC lends it and reported once final (FailStaticBorrow).
    private bool BorrowablePlace(Koto source, BindingScope scope, bool exclusive)
        => this.LendsPlace(source, scope, exclusive, out var staticRoot) && staticRoot is null;

    // SPEC 15.6.2, 15.6.4: the one diagnostic for a written Borrow of a Place Binding does not lend (BorrowablePlace): one that SPEC
    // lends but whose root is a static Field is unsupported; otherwise an exclusive borrow names the capability its path denies
    // (AccessFailure) and a shared one InvalidAssignment.
    private BindingFailure BorrowFailure(Koto place, BindingScope scope, bool exclusive)
        => (this.LendsPlace(place, scope, exclusive, out var staticRoot) && staticRoot is not null, exclusive) switch
        {
            (true, _) => BindingFailure.Unsupported,
            (_, true) => AccessFailure(place),
            _ => BindingFailure.InvalidAssignment,
        };

    // SPEC 15.6, 15.6.2, PLAN P42: the one diagnostic for an implicit Borrow of a static root (a receiver's, a value call's callee, an
    // argument's, an expected Type's or a Case payload's adaptation), recorded where its operation is final and before that input's
    // Origin fit, whose relations then record nothing at the node, as a written Borrow's are never judged (BorrowFailure): a Borrow,
    // base borrow or payload projection in a mode of a Place whose path SPEC roots at a static Field fails its source as the located
    // Unsupported_Kd. True when it fails.
    private bool FailStaticBorrow(Koto source, ArgumentOperationKind kind, SemanticsKind mode)
    {
        if (kind is not (ArgumentOperationKind.Borrow or ArgumentOperationKind.BaseBorrow or ArgumentOperationKind.PayloadProjection) ||
            !this.LendsPlace(source, this.ConstraintScope(source), IsExclusive(mode), out var staticRoot) || staticRoot is null)
        {
            return false;
        }

        this.Fail(source, BindingFailure.Unsupported);
        return true;
    }

    // SPEC 3.4.3, 13.5.5, 15.1.5: whether SPEC lends a Place in a mode, and staticRoot, the static Field or static storage at the root
    // of the path that lends it (SPEC 15.6.2: through inline parts, elements and an owning handle's payload, an own step), if any; a
    // node, not a bool, as the structure limits leave this member one bool parameter, the mode. Below a pair layer the walk asks
    // BorrowablePlace (PairCapability): no static's Type has one.
    private bool LendsPlace(Koto source, BindingScope scope, bool exclusive, out Koto? staticRoot)
    {
        staticRoot = null;
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
            // SPEC 13.5.5, 15.6.2: an owning path inherits the root's mutability, the payload of an owning handle extending the handle
            // Place's path (lent with it, or with the Temporary Value holding it); rc/arc/objref paths are shared.
            var handle = payload.Left.BoundType!;
            return handle.Semantics switch
            {
                SemanticsKind.ObjRef => !exclusive,
                SemanticsKind.ObjUniq => !exclusive || !ReachedThroughShared(payload.Left),
                SemanticsKind.Obj => this.LendsHandle(payload.Left, scope, exclusive, out staticRoot),
                _ => !exclusive && this.LendsHandle(payload.Left, scope, false, out staticRoot),
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

            // SPEC 15.6.2: a static Field, qualified or in its group, is a root Place, lent as its accessors allow (above).
            if (IsStaticField(source))
            {
                staticRoot = source;
                return true;
            }

            if (source is MemberAccessKoto access)
            {
                // SPEC 15.1.5: a special receiver lends its own initialized Fields as its row says, although the receiver itself is
                // not an assignable binding (Receiver).
                if (Receiver(access.Left) is { } special)
                {
                    return !exclusive || special.LendsFields;
                }

                // SPEC 13.5.5, 15.6.2: a payload Field extends the handle Place's path, exclusively only through an exclusive payload.
                if (access.Left.BoundType?.HandleMode is { } handle)
                {
                    return (!exclusive || handle.PayloadAuthority == LoanRequirement.Uniq) && this.LendsHandle(access.Left, scope, exclusive, out staticRoot);
                }

                // SPEC 3.4.3, 13.5.5: past an exclusive layer of the receiver's own Type, a pair layer admitting only uniq included,
                // the Field is lent in either mode; past another reference, view or raw pointer it is shared; an owned receiver, also
                // past a pair layer admitting owner, lends it as the receiver Place does.
                return LayerAuthority(access.Left, []) == SemanticsKind.Uniq ||
                    (access.Left.BoundType?.Kind == BoundTypeKind.Semantics ? !exclusive : this.LendsPlace(access.Left, scope, exclusive, out staticRoot));
            }

            return true;
        }

        if (source is BinaryKoto part && ElementAccess.IsSyntax(part) && ElementAccess.TryType(part, out _, out _))
        {
            return this.LendsPlace(part.Left, scope, exclusive, out staticRoot);
        }

        // A closure environment binding is a Place of its own (SPEC 7.7.2), borrowable like a local; a static Property's storage outside
        // its accessor's function is a static root (StaticStorageAccess).
        if (source is not IdentifierNameKoto || !IsBarePlace(source) || (exclusive && !Writable(source)))
        {
            return false;
        }

        staticRoot = source.BoundSymbol is { } symbol && StaticStorageAccess(symbol, scope) ? source : null;
        return true;
    }

    // The forms of one path step (NextPathStep).
    private enum PathStep : byte
    {
        // The root: a local, parameter, capture or static, a Temporary Value, a getter result or any other node no step selects from.
        Root,

        // A guard candidate (SPEC 14.8.3).
        Candidate,

        // The receiver `self` of a constructor or destructor (Receiver).
        Receiver,

        // The referent of a ref/uniq reference or the payload of an object handle or view (@follow).
        Follow,

        // The pointee of a raw pointer, `*p` or `p[n]` (SPEC 5.2), which PathAuthority treats as a root.
        RawFollow,

        // The target of a pair layer (@follow, SPEC 13.5.5).
        PairFollow,

        // A user index's element Place (SPEC 4.7.1).
        UserIndex,

        // A Slice element, through the Slice or a reference to it (SPEC 4.8.2).
        SliceElement,

        // An element of a fixed array, Array or Dictionary reached through a ref or uniq reference to it.
        BorrowedElement,

        // An element of an owned Array, not a range selection (SPEC 4.7.1).
        ArrayElement,

        // A member reached through every layer of its receiver's own Type (SPEC 3.4.3).
        Member,

        // Any other inline part ElementAccess selects in place (a fixed-array or Tuple element), on its owner's path.
        InlinePart,
    }
}
