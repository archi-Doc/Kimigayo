// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// In-place reads (SPEC 3.4.3, 4.7.1, 4.8.2, 10.2, 13.4): the read Binding records for a receiver selected below a Place
// (ReceiverElement, from ReceiverThroughLayers, SharedElementRead and InPlaceSelection; SharedElementView; ElementRead through the
// layers an element stores) and for a compared operand (CompareInPlace, StringThroughLayers, InspectedRead).
// - Inputs: the bound syntax, the selected index calls, a Place's path authority (Binding.PlaceAuthority) and Origin (Binding.PlaceOrigins).
// - Outputs: the recorded reads in `adaptations` (SharedBorrow or ReferenceRead), which AccessType and IsSynthesizedAccess
//   (Binding.ElementAccess) publish to Binding's Place classification and the HIR publisher.
// - Invariants: a node keeps its own Type, and a recorded read only states how the node is read in place: one reference to the Type
//   declaring the member, never a layer. Each read has one writer. A receiver's read is the path's, written by its selection:
//   ReceiverElement, in priority a user index's returned reference, the reference through the receiver's own layers with the path's
//   authority (ReceiverThroughLayers), the shared element read of an InPlaceSelection; SharedElementView for a range selection's
//   fixed-array element. A compared operand's read is written by CompareInPlace (its layers' read over its element read), which
//   reads its receiver's recorded read and never writes it. The use decides the capability: a ReferenceRead states no mode, and a
//   call's, a borrow's or a write's own capability overrides a synthesized shared read (DECISIONS BU-243).
// - Prohibitions: nothing here reports a diagnostic, selects a candidate or changes a Type; no use writes a receiver's read.
public sealed partial class Binding
{
    private long inPlaceSelections;

    /// <summary>Gets the selections InPlaceSelection judged (one per receiver or compared operand it reads), which a scaling check reads.</summary>
    internal long InPlaceSelectionWork => this.inPlaceSelections;

    // SPEC 3.4.3: a member or Tuple element selected through several safe value-reference layers (ref, uniq and qualifying pair
    // layers in any order, LayerBelow; PLAN G1122) is reached through one reference to the Type that declares it: exclusive when the
    // receiver can be exclusively acquired through them (ExclusivelyAcquirable: no shared layer or case, a pair layer's weakest case),
    // shared otherwise, with the Origins of 10.2. A receiver below one layer is its acquisition's (AdaptInput, TryPairReceiver). The
    // receiver keeps its own Type; the reference, which ownership loads and generation addresses, is handed to ReceiverElement, which
    // records it unless the receiver is a user index; null when no such reference is read.
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

    // SPEC 3.4.3, 4.7.1, 4.8.2: a receiver's in-place read is the path's, written once, here, at its selection (G147 ruling 50), in
    // priority: a user index's returned reference (4.7.1), continued through the layers its element stores (ElementRead); else the
    // reference through the receiver's own layers with the path's authority, `throughLayers` (ReceiverThroughLayers, 3.4.3); else the
    // shared read of a Non-Copy element of an InPlaceSelection (SharedElementRead). The element keeps its stored Type, and each use
    // takes its capability from itself (a call's receiver adaptation, a borrow's Type, a write's plan); no use writes this read.
    private void ReceiverElement(Koto left, BoundType? element, BoundType? throughLayers)
    {
        if (this.IsUserIndex(left) && element is not null)
        {
            var origin = this.PlaceOrigin(left);
            var read = this.ElementRead(left, element, origin) ?? this.types.Reference(SemanticsKind.Ref, element, origin);
            this.adaptations[left] = new(ExpectedAdaptationKind.SharedBorrow, read);
        }
        else if (throughLayers is not null)
        {
            this.adaptations[left] = new(ExpectedAdaptationKind.ReferenceRead, throughLayers);
        }
        else if (KotoHelper.UnwrapParentheses(left) is BinaryKoto selection && this.InPlaceSelection(selection) && this.SharedElementRead(left, element) is { } shared)
        {
            this.adaptations[left] = new(ExpectedAdaptationKind.SharedBorrow, shared);
        }
    }

    // SPEC 13.4, 10.2: a string comparison reads each operand in place, and its read is decided here once (G147 ruling 60): an operand
    // behind two or more reference layers is read as one shared reference to the string, `layers` (StringThroughLayers: following
    // acquires nothing); otherwise its element read (InspectedRead), if any. A single ref or uniq layer is inspected as it is. It writes
    // only the operand.
    private void CompareInPlace(Koto operand, BoundType? layers)
    {
        if (layers is not null)
        {
            this.adaptations[operand] = new(ExpectedAdaptationKind.ReferenceRead, layers);
        }
        else if (this.InspectedRead(operand) is { } read)
        {
            this.adaptations[operand] = new(ExpectedAdaptationKind.SharedBorrow, read);
        }
    }

    // SPEC 13.4, 10.2: the shared reference to the string a compared operand's two or more safe value-reference layers reach; null for
    // a single ref or uniq layer, which is inspected as it is.
    private BoundType? StringThroughLayers(Koto operand, BoundType type)
        => type.Components.Count == 1 && ReferenceEquals(ComparisonReferent(type), BoundType.String) && !ReferenceEquals(type.SafeReferent, BoundType.String)
            ? this.SharedReferenceThroughLayers(type, BoundType.String, this.ConstraintScope(operand), out _) : null;

    // SPEC 13.4, 4.7.1: the element read of a compared operand, or null. A Place selected by a dynamic key, below an element or through
    // a reference has no owned static path, so it is shared-borrowed like a receiver element (InPlaceSelection); its receiver keeps the
    // read its selection recorded (ReceiverElement), which IsBorrowedReceiver reads, and nothing here writes it.
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

        return KotoHelper.UnwrapParentheses(node) is BinaryKoto selection && this.InPlaceSelection(selection) ? this.SharedElementRead(node, selection.BoundType) : null;
    }

    // SPEC 13.4, 4.7.1, 4.8.2: a selection whose Place no owned root holds, so that a Non-Copy element of it is read in place through
    // one shared borrow: no range or getter, below a borrowed receiver or at a runtime fixed index (PLAN G59: an owned fixed-array
    // element at a runtime index is read like an element of a borrowed receiver), with no owned static path. Read by ReceiverElement's
    // element arm and InspectedRead; the receiver tests come before OwnedPathRoot's walk of the chain.
    private bool InPlaceSelection(BinaryKoto selection)
    {
        this.inPlaceSelections++;
        return selection is IndexKoto { Right: not RangeKoto } or MemberAccessKoto && !IsGetterResult(selection) &&
            (this.IsBorrowedReceiver(selection.Left) || this.IsRuntimeFixedElement(selection)) && this.OwnedPathRoot(selection) is null;
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
