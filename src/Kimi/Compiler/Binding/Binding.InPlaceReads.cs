// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// In-place reads (SPEC 3.4.3, 4.7.1, 4.8.2, 10.2, 13.4): the shared read Binding records for a receiver selected below a Place
// (ReceiverThroughLayers, ReceiverElement, SharedElement, SharedElementView; ElementRead through the layers an element stores) and for a
// compared operand (CompareInPlace).
// - Inputs: the bound syntax, the selected index calls, a Place's path authority (Binding.PlaceAuthority) and Origin (Binding.PlaceOrigins).
// - Outputs: the recorded reads in `adaptations` (SharedBorrow or ReferenceRead), which ElementAccess.AccessType and IsSynthesizedAccess
//   publish to Binding, the HIR publisher and the old paths.
// - Invariants: a node keeps its own Type, and a recorded read only states how the node is read in place: one shared reference to the
//   Type declaring the member, never a layer.
// - Prohibitions: nothing here reports a diagnostic, selects a candidate or changes a Type.
public sealed partial class Binding
{
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
            this.SharedElement(left, element);
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
            // its selected call returns, continued through the layers it stores as any in-place read of it (ElementRead).
            var origin = this.PlaceOrigin(operand);
            var read = this.ElementRead(published, origin) ?? this.types.Intern(BoundTypeKind.Semantics, null, placeSemantics, [published], origin: origin);
            this.adaptations[operand] = new(ExpectedAdaptationKind.SharedBorrow, read);
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
            this.SharedElement(operand, selection.BoundType);
        }
    }

    // SPEC 4.8.1, 4.8.2: a range selection of a fixed-array element reached through a Slice or a borrowed array borrows
    // the element slot in place, whatever the element's Copy capability, so the view retains the backing source.
    private void SharedElementView(Koto receiver)
    {
        if (KotoHelper.UnwrapParentheses(receiver) is IndexKoto element && element.BoundType is { Kind: BoundTypeKind.FixedArray, Semantics: SemanticsKind.Owner } array && ElementAccess.IsSharedElement(element))
        {
            this.adaptations[element] = new(ExpectedAdaptationKind.SharedBorrow, this.types.Intern(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [array], origin: this.PlaceOrigin(element)));
        }
    }

    // Records the one shared borrow of a Non-Copy element Place that is read in place below its receiver.
    private void SharedElement(Koto node, BoundType? element)
    {
        if (element is not null && this.ProveCopy(element, node) == ConstraintProof.Refuted && SharedReadTypes.BorrowSemantics(element) is { } semantics)
        {
            var origin = this.PlaceOrigin(node);
            var referent = element.Semantics == SemanticsKind.Owner ? element : element.Components[0];
            var dependency = element.Semantics == SemanticsKind.Owner ? origin : this.ReferentOrigin(node, element) ?? origin;
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
}
