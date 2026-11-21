// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// In-place reads (SPEC 3.4.3, 4.7.1, 4.8.2, 10.2, 13.4): the shared read Binding records for a receiver selected below a Place
// (ReceiverThroughLayers, ReceiverElement, SharedElementRead, SharedElementView; ElementRead through the layers an element stores) and
// for a compared operand and the receivers on its path (CompareInPlace, StringThroughLayers, InspectedRead).
// - Inputs: the bound syntax, the selected index calls, a Place's path authority (Binding.PlaceAuthority) and Origin (Binding.PlaceOrigins).
// - Outputs: the recorded reads in `adaptations` (SharedBorrow or ReferenceRead), which AccessType and IsSynthesizedAccess
//   (Binding.ElementAccess) publish to Binding's Place classification and the HIR publisher.
// - Invariants: a node keeps its own Type, and a recorded read only states how the node is read in place: one shared reference to the
//   Type declaring the member, never a layer. A compared operand's read is written once, by CompareInPlace (its layers' read over its
//   element read). A receiver's read has two writers: ReceiverElement at its selection (ReceiverThroughLayers hands it its reference,
//   G147 ruling 50), and CompareInPlace again (InspectedRead) where the receiver lies on a compared operand's path below a borrowed
//   receiver, is a runtime fixed element or is a user index with a Non-Copy element. They agree, writing one read from the same
//   inputs, where the receiver is an element Place that ReceiverElement reads by its element (a shared element, an Array element
//   storing a reference or handle, a user index whose element is no object case). Elsewhere the comparison's shared element read
//   replaces the selection's through-layers read, until each use decides a selection's read (PLAN open issue, G147 ruling 60).
// - Prohibitions: nothing here reports a diagnostic, selects a candidate or changes a Type.
public sealed partial class Binding
{
    // SPEC 3.4.3: a member or Tuple element selected through several safe value-reference layers (ref, uniq and qualifying pair
    // layers in any order, LayerBelow; PLAN G1122) is reached through one reference to the Type that declares it: exclusive when the
    // receiver can be exclusively acquired through them (ExclusivelyAcquirable: no shared layer or case, a pair layer's weakest case),
    // shared otherwise, with the Origins of 10.2. A receiver below one layer is its acquisition's (AdaptInput, TryPairReceiver). The
    // receiver keeps its own Type; the reference, which ownership loads and generation addresses, is handed to ReceiverElement, which
    // records it where no element read applies; null when no such reference is read.
    private BoundType? ReceiverThroughLayers(Koto left, BoundType? actual)
    {
        if (BelowLayer(BelowLayer(actual)) is null)
        {
            return null; // Fewer than two layers of any kind: no scope is needed.
        }

        var scope = this.ConstraintScope(left);
        if (this.LayerBelow(actual!, scope) is not { } below || (below is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.ObjRef } && this.LayerBelow(below, scope) is null))
        {
            return null;
        }

        var terminal = this.LayersTerminal(below, scope);

        // A stored shared object view is Copy: reading it preserves its own external Origin and header identity,
        // without borrowing its payload from the outer reference slot or granting exclusive authority.
        if (terminal.Semantics == SemanticsKind.ObjRef)
        {
            return terminal;
        }

        if ((terminal is not { Kind: BoundTypeKind.Tuple } && !AdtDef.IsStruct(terminal)) || this.SharedReferenceThroughLayers(actual!, terminal, scope, out var layers) is null)
        {
            return null;
        }

        // SPEC 10.2, 15.6.3, 13.5.5: the Origin of the shared-reference row (AdaptInput): the layers, then the path reaching the receiver
        // unless a layer is shared (ReferentOrigin); where nothing on the path is dereferenced, the receiver Place's own.
        var origin = this.ReferentOrigin(left, actual, layers) ?? this.PlaceOrigin(left);
        return this.types.Intern(BoundTypeKind.Semantics, null, this.ExclusivelyAcquirable(left) ? SemanticsKind.Uniq : SemanticsKind.Ref, [terminal], origin: origin);
    }

    // SPEC 3.4.3, 4.7.1, 4.8.2: a member selected below a user index's element, or below a Non-Copy element of a Slice or borrowed
    // array, is reached through one shared read of that element Place, continued through the layers it stores (ElementRead); any other
    // receiver read through several layers is read through `throughLayers` (ReceiverThroughLayers' reference), if any. The element keeps
    // its stored Type; the read is the receiver's adaptation at its selection, written once, here (G147 ruling 50).
    private void ReceiverElement(Koto left, BoundType? element, BoundType? throughLayers)
    {
        if (this.IsUserIndex(left) && element is not null)
        {
            var origin = this.PlaceOrigin(left);
            var read = this.ElementRead(left, element, origin) ?? this.types.Reference(SemanticsKind.Ref, element, origin);
            this.adaptations[left] = new(ExpectedAdaptationKind.SharedBorrow, read);
            return;
        }

        // An owned Array element that stores a reference or handle is not an inline part either: its member is reached
        // through the stored view.
        // A Non-Copy element published by a user index (SPEC 4.7.1) is likewise read through one shared borrow of its Place.
        if (KotoHelper.UnwrapParentheses(left) is IndexKoto index &&
            (this.IsSharedElement(index) || this.IsUserIndex(index) ||
                (index.Right is not RangeKoto && index.Left.BoundType?.Kind == BoundTypeKind.Array && element?.Semantics is not (null or SemanticsKind.Owner))) &&
            this.SharedElementRead(left, element) is { } shared)
        {
            this.adaptations[left] = new(ExpectedAdaptationKind.SharedBorrow, shared);
            return;
        }

        if (throughLayers is not null)
        {
            this.adaptations[left] = new(ExpectedAdaptationKind.ReferenceRead, throughLayers);
        }
    }

    // SPEC 13.4, 10.2: a string comparison reads each operand in place, and its read is decided here once (G147 ruling 60): an operand
    // behind two or more reference layers is read as one shared reference to the string, `layers` (StringThroughLayers: following
    // acquires nothing); otherwise its element read (InspectedRead), if any. A single ref or uniq layer is inspected as it is.
    private void CompareInPlace(Koto operand, BoundType? layers)
    {
        var read = this.InspectedRead(operand);
        if (layers is not null)
        {
            this.adaptations[operand] = new(ExpectedAdaptationKind.ReferenceRead, layers);
        }
        else if (read is not null)
        {
            this.adaptations[operand] = new(ExpectedAdaptationKind.SharedBorrow, read);
        }
    }

    // SPEC 13.4, 10.2: the shared reference to the string a compared operand's two or more safe value-reference layers reach; null for
    // a single ref or uniq layer, which is inspected as it is.
    private BoundType? StringThroughLayers(Koto operand, BoundType type)
        => type.Components.Count == 1 && ReferenceEquals(ComparisonReferent(type), BoundType.String) && !ReferenceEquals(type.SafeReferent, BoundType.String)
            ? this.SharedReferenceThroughLayers(type, BoundType.String, this.ConstraintScope(operand), out _) : null;

    // SPEC 13.4, 4.7.1: the element read of a Place on a compared operand's path, or null. A Place selected by a dynamic key, below an
    // element or through a reference has no owned static path, so it is shared-borrowed like a receiver element; each receiver on its
    // path that is such a selection is recorded here, before the Place's own read is decided.
    private BoundType? InspectedRead(Koto node)
    {
        if (this.IsUserIndex(node) && node.BoundType is { } published && this.ProveCopy(published, node) == ConstraintProof.Refuted &&
            this.SharedBorrowSemantics(published, node) is { } placeSemantics)
        {
            // SPEC 4.7.1, 13.4: a Non-Copy element of receiver[key] on a user Type is compared in place through the reference
            // its selected call returns, continued through the layers it stores as any in-place read of it (ElementRead).
            var origin = this.PlaceOrigin(node);
            return this.ElementRead(node, published, origin) ?? this.types.Intern(BoundTypeKind.Semantics, null, placeSemantics, [published], origin: origin);
        }

        if (KotoHelper.UnwrapParentheses(node) is not BinaryKoto selection || selection is not (IndexKoto { Right: not RangeKoto } or MemberAccessKoto) ||
            IsGetterResult(selection) || this.OwnedPathRoot(selection) is not null)
        {
            return null;
        }

        if (this.InspectedRead(selection.Left) is { } receiver)
        {
            this.adaptations[selection.Left] = new(ExpectedAdaptationKind.SharedBorrow, receiver);
        }

        // PLAN G59: an owned fixed-array element at a runtime index is compared in place like an element of a borrowed receiver.
        return this.IsBorrowedReceiver(selection.Left) || this.IsRuntimeFixedElement(selection) ? this.SharedElementRead(node, selection.BoundType) : null;
    }

    // SPEC 4.8.1, 4.8.2: a range selection of a fixed-array element reached through a Slice or a borrowed array borrows
    // the element slot in place, whatever the element's Copy capability, so the view retains the backing source.
    private void SharedElementView(Koto receiver)
    {
        if (KotoHelper.UnwrapParentheses(receiver) is IndexKoto element && element.BoundType is { Kind: BoundTypeKind.FixedArray, Semantics: SemanticsKind.Owner } array && this.IsSharedElement(element))
        {
            this.adaptations[element] = new(ExpectedAdaptationKind.SharedBorrow, this.types.Intern(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [array], origin: this.PlaceOrigin(element)));
        }
    }

    // The one shared borrow of a Non-Copy element Place that is read in place below its receiver; null for any other element.
    private BoundType? SharedElementRead(Koto node, BoundType? element)
    {
        if (element is null || this.ProveCopy(element, node) != ConstraintProof.Refuted || this.SharedBorrowSemantics(element, node) is not { } semantics)
        {
            return null;
        }

        var origin = this.PlaceOrigin(node);
        var referent = element.Semantics == SemanticsKind.Owner ? element : element.Components[0];
        var dependency = element.Semantics == SemanticsKind.Owner ? origin : this.ReferentOrigin(node, element) ?? origin;
        return this.ElementRead(node, element, origin) ?? this.types.Intern(BoundTypeKind.Semantics, null, semantics, [referent], origin: dependency);
    }

    // SPEC 3.4.3, 10.2: the in-place read of an element storing safe reference layers, below which a member is selected: one shared
    // reference to the declaring struct or Tuple, the element's selection reference (with the selection's Origin) continued through
    // those layers; null for any other element. It is the selection's representation, never a layer (IsSynthesizedAccess).
    private BoundType? ElementRead(Koto at, BoundType element, BoundOrigin selection)
        => ComparisonReferent(element) is var terminal && !ReferenceEquals(terminal, element) &&
            (terminal.Kind == BoundTypeKind.Tuple || AdtDef.IsStruct(terminal))
            ? this.SharedReferenceThroughLayers(this.types.Reference(SemanticsKind.Ref, element, selection), terminal, this.ConstraintScope(at), out _) : null;
}
