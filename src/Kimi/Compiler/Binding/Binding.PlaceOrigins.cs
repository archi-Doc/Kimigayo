// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Place Origins (SPEC 13.5.6, 15.6.3, 15.6.5, 15.8.2; DECISIONS BU-27): the Origin of a Borrow of the Place an expression designates
// (PlaceOrigin), of a slot that stores a reference (SlotOrigin), of a borrow prepared from a source (PreparedOrigin) and of a Borrow of the
// referent of a stored reference (ReferentOrigin, PathReborrowOrigin), through the one containment walk over a path's steps
// (ContainedOrigin, MeetLayers) and a root's containment (EnvironmentContainment); the binder and Projection slot naming a Place's own
// atom (PlaceOriginSource, PlaceOriginBinder, PlaceOriginSlot, SymbolOriginSlot, CandidateOriginBinder) and the reserved Projection
// slots (EnvironmentSlot, CallResultSlot, ClosureValueSlot, HelperSlot, ReceiverSlot).
// - Inputs: the bound syntax and its path steps (NextPathStep), the recorded adaptations and pair-layer facts, the helper borrow
//   (HelperOrigin), the indexer calls.
// - Outputs: Origins interned through OriginAtom and Meet, which adaptations, written Borrows, Subjects, in-place reads and the Origin
//   rows read.
// - Invariants: a Borrow reached through safe borrow layers is named by the meet of their Origins, from its last dereference outward up
//   to and including the first shared layer, every one when none is shared, and then also the containment of the root they are stored
//   in, a Closure's receiver Origin ρ for an environment binding (SPEC 15.6.3, 15.8.2; EnvironmentContainment); a reference read in
//   place is its referent; a binding's own Place is the atom of its binder and slot; a raw dereference ends the walk (raw Origins are
//   Binding.RawOrigins').
// - Prohibitions: nothing here judges an Origin relation, selects an adaptation or reports a diagnostic; no other component meets the
//   layers of a Borrow's path or a root's containment (a nested bare entry's Type reads EnvironmentContainment, Binding.Captures), and
//   a Pattern position's layers (Binding.Patterns.Layers) meet through MeetLayer, this walk's step.
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

    // The Projection slot that names a closure literal's own temporary value, the receiver of a call on that literal, and in the
    // Closure's body its call receiver's Origin ρ (SPEC 15.8.2; EnvironmentContainment).
    internal const int ClosureValueSlot = int.MinValue;

    // The Projection slot of the helper borrow, a Borrow of the whole destruction receiver (SPEC 16.3.1, Binding.HelperOrigin). Its binder
    // is always a destructor, never a value or a closure, so no reader of an environment or closure-value slot meets it.
    internal const int HelperSlot = int.MinValue + 1;

    // The slot of a constructor's or destructor's receiver `self` and so the Projection slot of its Place (SPEC 6.3.4, 16.3.1;
    // DECISIONS BU-23): no parameter index, so the receiver's Place is never a parameter's (Receiver). Its binder is always a
    // constructor or destructor, never a closure, and every reader of a closure's slots reads their role (RoleOf).
    internal const int ReceiverSlot = int.MinValue + 2;

    // The Projection slot that names a binding's own Place.
    internal static int SymbolOriginSlot(BindingSymbol symbol) => symbol.Kind == BindingSymbolKind.Capture ? EnvironmentSlot(symbol.Slot) : symbol.Slot;

    // A guard candidate and the selected body binding share their source declaration,
    // but never their storage lifetime. Reuse the name node as the candidate's identity.
    internal static Koto CandidateOriginBinder(BindingSymbol candidate) => ((SyntaxFormKoto)candidate.Declaration).Operands[0];

    // A Closure literal's own temporary is its ClosureValueSlot (SPEC 15.8.2; DECISIONS R4-M152); any other temporary's slot is 0.
    internal static int PlaceOriginSlot(Koto source)
    {
        if (source is FunctionKoto { IsAnonymous: true })
        {
            return ClosureValueSlot;
        }

        return source.BoundSymbol is { Kind: BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Storage or BindingSymbolKind.PatternCandidate or BindingSymbolKind.Capture } symbol
            ? SymbolOriginSlot(symbol) : 0;
    }

    // The dependency of a borrow prepared from a source: through an implicitly followed pair layer it is the layer's own
    // (SPEC 13.5.5), as the adaptation recorded it; otherwise the source Place's.
    private BoundOrigin PreparedOrigin(Koto source)
        => KotoHelper.UnwrapParentheses(source) is var place && this.ImplicitPairAdmitted(place) is var admitted && admitted != SemanticsMask.None && place.BoundType is { } pair
            ? this.PairOrigin(place, pair, admitted) : this.PlaceOrigin(source);

    // SPEC 15.6.3: a Reborrow of the referent of an exclusive reference stored at `place`, whose Origin is `origin`, is contained in that
    // reference and in the layers `place` is reached through (ContainedOrigin).
    private BoundOrigin? PathReborrowOrigin(Koto place, BoundOrigin? origin)
        => origin is null ? null : this.ContainedOrigin(place, origin);

    // SPEC 15.8.2, 15.6.3 (DECISIONS R4-M126 P2, P3): an environment binding is a Field of its Closure's call receiver's referent, so a
    // Place reached through a layer stored in it is also contained in the receiver, whose Origin is ρ, the Closure's reserved atom at
    // ClosureValueSlot; any other root has no containment.
    private BoundOrigin? EnvironmentContainment(BindingSymbol? root)
        => root is { Kind: BindingSymbolKind.Capture, Declaration: FunctionKoto closure }
            ? this.OriginAtom(closure, OriginKind.Projection, ClosureValueSlot) : null;

    // SPEC 15.6.3, 13.5.5, 3.4.3: a Borrow of the referent that `layers` safe borrow layers of `reference`, stored at `holder`, reach (a
    // follow, a Subject's Reborrow, a read in place or through several layers): those layers first (MeetLayers), then, unless one of
    // them is shared, the layers `holder` is reached through. A Type that is no safe borrow layer keeps its own Origin.
    private BoundOrigin? ReferentOrigin(Koto holder, BoundType? reference, int layers = 1)
    {
        if (reference is not { Kind: BoundTypeKind.Semantics, Components.Count: 1 } || !IsBorrow(reference.Semantics))
        {
            return reference?.Origin;
        }

        BoundOrigin? origin = null;
        return this.MeetLayers(reference, layers, ref origin) ? origin : this.ContainedOrigin(holder, origin);
    }

    // SPEC 15.6.3, 4.7.1, 4.8.2, 14.8 (DECISIONS BU-27): `origin`, the meet of the layers dereferenced at `place` (null when none), met
    // with the Origins of the safe borrow layers `place`'s path dereferences, from its last step outward up to and including the first
    // shared one, over NextPathStep (MeetStep); null when no layer is dereferenced.
    private BoundOrigin? ContainedOrigin(Koto place, BoundOrigin? origin)
    {
        var path = KotoHelper.UnwrapParentheses(place);
        for (var depth = 0; depth < 64; depth++)
        {
            var step = NextPathStep(path, out var next);
            next = KotoHelper.UnwrapParentheses(next);
            if (this.MeetStep(step, path, next, ref origin))
            {
                break;
            }

            path = next;
        }

        return origin;
    }

    // One step of ContainedOrigin from `path` to `next`, met into `origin`; whether it ends the walk. A follow dereferences its operand's
    // outer layer, a member or a borrowed element every layer of its receiver's own Type (SPEC 3.4.3), a pair follow its slot by its
    // admitted cases (SPEC 13.5.5): on unless shared. A user index's element is the referent of its published result, an Array or
    // UniqSlice element of a reference to the receiver's Borrow, a Slice element of `ref/E during s.source` (SPEC 4.7.1), a guard candidate
    // `ref/T during g` (SPEC 14.8): each names its receiver or is shared, and ends it. An inline part goes on; a root, a special receiver
    // and a raw dereference end it, a root reached through a dereferenced layer met with its containment (EnvironmentContainment).
    private bool MeetStep(PathStep step, Koto path, Koto next, ref BoundOrigin? origin)
    {
        switch (step)
        {
            case PathStep.Follow:
                return this.MeetLayers(next.BoundType, 1, ref origin);
            case PathStep.Member or PathStep.BorrowedElement:
                return this.MeetLayers(next.BoundType, int.MaxValue, ref origin);
            case PathStep.InlinePart:
                return false;
            case PathStep.PairFollow:
                var admitted = this.PairAdmitted(path);
                origin = this.MeetWith(origin, (admitted & SemanticsMask.Borrow) != 0 && next.BoundType is { } pair ? this.OuterOrigin(pair) : null);
                return (admitted & (SemanticsMask.Owner | SemanticsMask.Uniq | SemanticsMask.ObjUniq)) == 0;
            case PathStep.UserIndex:
                origin = this.MeetWith(origin, ElementAccess.IndexerCall(path, false)?.BoundType?.Origin);
                return true;
            case PathStep.ArrayElement:
                origin = this.MeetWith(origin, this.PlaceOrigin(next));
                return true;
            case PathStep.SliceElement:
                origin = this.MeetWith(origin, (next.BoundType?.Kind == BoundTypeKind.Slice ? next.BoundType : next.BoundType?.Components[0])?.Origin);
                return true;
            case PathStep.Candidate:
                origin = this.MeetWith(origin, this.OriginAtom(PlaceOriginBinder(path), OriginKind.Projection, PlaceOriginSlot(path)));
                return true;
            case PathStep.Root when origin is not null:
                origin = this.MeetWith(origin, this.EnvironmentContainment(path.BoundSymbol));
                return true;
            case PathStep.Root or PathStep.Receiver or PathStep.RawFollow:
                return true;
            default:
                throw new UnreachableException();
        }
    }

    // SPEC 15.6.3, 3.4.3: the outer `count` safe borrow layers of a Type that one selection dereferences (a pair layer by its admitted
    // cases), met into `origin` from the innermost outward up to and including the first shared one; whether a shared layer ends the
    // walk. SharedReferenceThroughLayers names a read through several layers by it.
    private bool MeetLayers(BoundType? type, int count, ref BoundOrigin? origin)
    {
        BoundOrigin? met = null;
        var shared = false;
        for (var i = 0; i < count && type is not null; i++)
        {
            BoundOrigin? layer;
            bool ends;
            if (TryPairLayer(type, out _, out _))
            {
                var admitted = this.LayerAdmitted(type);
                layer = (admitted & SemanticsMask.Borrow) != 0 ? this.OuterOrigin(type) : null;
                ends = (admitted & (SemanticsMask.Owner | SemanticsMask.Uniq | SemanticsMask.ObjUniq)) == 0;
            }
            else if (type is { Kind: BoundTypeKind.Semantics, Components.Count: 1 } && IsBorrow(type.Semantics))
            {
                layer = type.Origin;
                ends = type.Semantics is SemanticsKind.Ref or SemanticsKind.ObjRef;
            }
            else
            {
                break;
            }

            met = this.MeetLayer(met, layer, ends);
            shared |= ends;
            type = BelowLayer(type);
        }

        origin = this.MeetWith(origin, met);
        return shared;
    }

    // SPEC 15.6.3: one layer's Origin met, from the outside in, into the Origin the layers outside it reached: a shared layer restarts it
    // at its own Origin (dropping the layers outside it), an exclusive one meets it. MeetLayers' step and a Pattern position's (SelectLayers).
    private BoundOrigin? MeetLayer(BoundOrigin? reached, BoundOrigin? layer, bool shared) => shared ? layer ?? reached : this.MeetWith(reached, layer);

    private BoundOrigin? MeetWith(BoundOrigin? a, BoundOrigin? b) => a is null ? b : b is null ? a : this.Meet(a, b);

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
            return this.ReferentOrigin(selected.Left, selected.Left.BoundType) ?? this.PlaceOrigin(selected.Left);
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
            return this.ReferentOrigin(reached.Left, reached.Left.BoundType) ?? this.PlaceOrigin(reached.Left);
        }

        // SPEC 3.4.3, 4.8.1, 15.6.3: a Place reached through an exclusive layer stored in place is contained in it and in the layers
        // reaching it (ContainedOrigin), a reference read in place is its referent, through an exclusive one also contained in its root's
        // containment (EnvironmentContainment), and a Slice handle that is itself the Place is a Place of its root.
        return layer is null || !(reachedThrough || layer.Kind == BoundTypeKind.Semantics)
            ? this.OriginAtom(PlaceOriginBinder(source), OriginKind.Projection, PlaceOriginSlot(source))
            : reachedThrough ? this.ContainedOrigin(place, null) ?? layer.Origin!
            : this.MeetWith(layer.Origin!, IsExclusive(layer.Semantics) ? this.EnvironmentContainment(source.BoundSymbol) : null)!;
    }
}
