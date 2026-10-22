// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Place Origins (SPEC 13.5.6, 15.6.3, 15.6.5, 15.8.2): the Origin of a Borrow of the Place an expression designates (PlaceOrigin), of a
// slot that stores a reference (SlotOrigin), of a borrow prepared from a source (PreparedOrigin) and of a Reborrow through exclusive
// references (PathReborrowOrigin, FollowedOrigin); the binder and Projection slot naming a Place's own atom (PlaceOriginSource,
// PlaceOriginBinder, PlaceOriginSlot, SymbolOriginSlot, CandidateOriginBinder) and the reserved Projection slots (EnvironmentSlot,
// CallResultSlot, ClosureValueSlot, HelperSlot, ReceiverSlot).
// - Inputs: the bound syntax, the recorded adaptations and pair-layer facts, the helper borrow (HelperOrigin), the indexer calls.
// - Outputs: Origins interned through OriginAtom and Meet, which adaptations, written Borrows and the Origin rows read.
// - Invariants: a Place reached through a shared layer depends only on that layer; a Reborrow stays within the borrows that reach
//   its exclusive references up to the first shared layer; a binding's own Place is the atom of its binder and slot.
// - Prohibitions: nothing here judges an Origin relation, selects an adaptation or reports a diagnostic.
public sealed partial class Binding
{
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
}
