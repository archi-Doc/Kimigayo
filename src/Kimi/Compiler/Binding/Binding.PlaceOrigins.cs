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
        // retain separate place footprints for overlap checking. A raw Place's root `p[n]` is no part of `p` (SPEC 5.3).
        source = KotoHelper.UnwrapParentheses(source);
        while (source is MemberAccessKoto or IndexKoto && !ElementAccess.IsRawRoot(source))
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
        => origin is null ? null : this.ContainedOrigin(place, new() { Met = origin, Dereferenced = true });

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

        var walk = default(ContainmentWalk);
        return this.MeetLayers(reference, layers, ref walk) ? this.WalkOrigin(walk) : this.ContainedOrigin(holder, walk);
    }

    // SPEC 15.6.3, 4.7.1, 4.8.2, 14.8 (DECISIONS BU-27): `walk`, the layers dereferenced at `place` (none for a Place's own walk), met
    // with the Origins of the safe borrow layers `place`'s path dereferences, from its last step outward up to and including the first
    // shared one, over NextPathStep (MeetStep); null when no layer is dereferenced. One walk decides every fact of the layers it meets
    // (ContainmentWalk).
    private BoundOrigin? ContainedOrigin(Koto place, ContainmentWalk walk)
    {
        var path = KotoHelper.UnwrapParentheses(place);
        for (var depth = 0; depth < 64; depth++)
        {
            var step = NextPathStep(path, out var next);
            next = KotoHelper.UnwrapParentheses(next);
            if (this.MeetStep(step, path, next, ref walk))
            {
                break;
            }

            path = next;
        }

        return this.WalkOrigin(walk);
    }

    private BoundOrigin? WalkOrigin(in ContainmentWalk walk) => this.MeetWith(walk.Met, walk.Unbounded);

    // One step of ContainedOrigin from `path` to `next`, met into `walk`; whether it ends the walk. A follow dereferences its operand's
    // outer layer, a member or a borrowed element every layer of its receiver's own Type (SPEC 3.4.3), a pair follow its slot by its
    // admitted cases (SPEC 13.5.5): on unless shared. A user index's element is the referent of its published result, an Array or
    // UniqSlice element of a reference to the receiver's Borrow, a Slice element of `ref/E during s.source` (SPEC 4.7.1), a guard candidate
    // `ref/T during g` (SPEC 14.8): each names its receiver or is shared, and ends it. An inline part goes on; a root, a special receiver
    // and a raw dereference end it, a root reached through a met layer met with its containment (EnvironmentContainment), or with its
    // own atom while an admitted case has dereferenced nothing (that case's Place is the root's).
    private bool MeetStep(PathStep step, Koto path, Koto next, ref ContainmentWalk walk)
    {
        switch (step)
        {
            case PathStep.Follow:
                return this.MeetLayers(next.BoundType, 1, ref walk);
            case PathStep.Member or PathStep.BorrowedElement:
                return this.MeetLayers(next.BoundType, int.MaxValue, ref walk);
            case PathStep.InlinePart:
                return false;
            case PathStep.PairFollow:
                return this.MeetPair(ref walk, next.BoundType, this.PairAdmitted(path));
            case PathStep.UserIndex:
                walk.Met = this.MeetWith(walk.Met, ElementAccess.IndexerCall(path, false)?.BoundType?.Origin);
                return true;
            case PathStep.ArrayElement:
                walk.Met = this.MeetWith(walk.Met, this.PlaceOrigin(next));
                return true;
            case PathStep.SliceElement:
                walk.Met = this.MeetWith(walk.Met, (next.BoundType?.Kind == BoundTypeKind.Slice ? next.BoundType : next.BoundType?.Components[0])?.Origin);
                return true;
            case PathStep.Candidate:
                walk.Met = this.MeetWith(walk.Met, this.OriginAtom(PlaceOriginBinder(path), OriginKind.Projection, PlaceOriginSlot(path)));
                return true;
            case PathStep.Root when walk.Met is not null || walk.Unbounded is not null:
                walk.Met = this.MeetWith(walk.Met, walk.Dereferenced ? this.EnvironmentContainment(path.BoundSymbol) : this.OriginAtom(PlaceOriginBinder(path), OriginKind.Projection, PlaceOriginSlot(path)));
                return true;
            case PathStep.Root or PathStep.Receiver or PathStep.RawFollow:
                return true;
            default:
                throw new UnreachableException();
        }
    }

    // SPEC 15.6.3, 3.4.3: the outer `count` safe borrow layers of a Type that one selection dereferences (a pair layer by its admitted
    // cases), met into `walk` from the innermost outward up to and including the first shared one (MeetOuter); whether a shared layer
    // ends the walk. SharedReferenceThroughLayers names a read through several layers by it. It recurses once per layer, at most the
    // Type's height H (DECISIONS BU-134) deep on the compile stack (BU-135): G144's depth rule for a Type walk.
    private bool MeetLayers(BoundType? type, int count, ref ContainmentWalk walk)
    {
        var pair = TryPairLayer(type, out _, out _);
        if (count == 0 || (!pair && !(type is { Kind: BoundTypeKind.Semantics, Components.Count: 1 } && IsBorrow(type.Semantics))))
        {
            return false;
        }

        if (this.MeetLayers(BelowLayer(type), count - 1, ref walk))
        {
            return true; // A shared layer below this one ended the walk.
        }

        if (pair)
        {
            return this.MeetPair(ref walk, type, this.LayerAdmitted(type!));
        }

        walk.Dereferenced = true;
        this.MeetOuter(ref walk, type!.Origin, null);
        return type.Semantics is SemanticsKind.Ref or SemanticsKind.ObjRef;
    }

    // SPEC 13.5.5, 15.6.1: a followed pair layer met into the walk (MeetLayers' pair layers, MeetStep's pair follow); whether it is shared.
    private bool MeetPair(ref ContainmentWalk walk, BoundType? pair, SemanticsMask admitted)
    {
        var slot = this.FollowedSlot(pair, admitted, out var shared, out var inPlace);
        walk.Dereferenced |= !inPlace;
        this.MeetOuter(ref walk, shared ? slot : null, shared ? null : slot);
        return shared;
    }

    // SPEC 15.6.3, 15.6.1: one layer met into the walk, outside every layer met before it: `met`, an Origin it meets (a borrow layer's, a
    // restarting slot's), or `unbounded`, a slot that does not restart. Either bounds the slot the walk kept so far, which is dropped.
    private void MeetOuter(ref ContainmentWalk walk, BoundOrigin? met, BoundOrigin? unbounded)
    {
        walk.Unbounded = met is null && unbounded is null ? walk.Unbounded : unbounded;
        walk.Met = this.MeetWith(walk.Met, met);
    }

    // SPEC 13.5.5, 15.6.1: a followed pair layer's one case split, which the walk (MeetPair) and a Place's own follow (PairSlot) read:
    // its slot, present in the borrow cases only; whether it restarts the dependency (`shared`: no owner or exclusive case); and whether
    // a case selects the operand Place itself (`inPlace`: owner admitted).
    private BoundOrigin? FollowedSlot(BoundType? pair, SemanticsMask admitted, out bool shared, out bool inPlace)
    {
        shared = (admitted & (SemanticsMask.Owner | SemanticsMask.Uniq | SemanticsMask.ObjUniq)) == 0;
        inPlace = (admitted & SemanticsMask.Owner) != 0;
        return (admitted & SemanticsMask.Borrow) != 0 && pair is not null ? this.OuterOrigin(pair) : null;
    }

    // SPEC 15.6.3: one layer's Origin met, from the outside in, into the Origin the layers outside it reached: a shared layer restarts it
    // at its own Origin (dropping the layers outside it), an exclusive one meets it. A Pattern position's step (SelectLayers).
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

        // SPEC 3.4.3, 13.5.5, 15.6.3, 4.7.1, 4.8.1 (DECISIONS BU-27): a Place reached through safe borrow layers (a selected referent, a
        // Slice handle or a pair layer by its admitted cases among them) is named by the one walk over its path's steps (ContainedOrigin).
        var place = KotoHelper.UnwrapParentheses(source);
        source = PlaceOriginSource(source);
        var reachedThrough = !ReferenceEquals(source, place);
        if (reachedThrough && this.ContainedOrigin(place, default) is { } contained)
        {
            return contained;
        }

        if (source is ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PayloadFollow } reached)
        {
            // SPEC 13.5.5, 15.6.3: a part of a selected payload that no borrow layer reaches keeps the dependencies of its handle.
            return this.ReferentOrigin(reached.Left, reached.Left.BoundType) ?? this.PlaceOrigin(reached.Left);
        }

        // Otherwise the walk dereferenced nothing: the Place is its root's, or (SPEC 5.2.2) one rooted at a raw Place through owned
        // parts, borrowed as a fresh anchor (RawAnchor); a reference read in place is its referent, through an exclusive one also
        // contained in its root's containment (EnvironmentContainment), and a Slice handle that is itself the Place is a Place of its root.
        // A pair layer's Origin is that of the reference it may hold, so the layer itself is its own dependency.
        var layer = ElementAccess.AccessType(source) is { Origin: not null } type && !TryPairLayer(type, out _, out _) ? type : null;
        return layer is null || !(reachedThrough || layer.Kind == BoundTypeKind.Semantics)
            ? (ElementAccess.IsRawRoot(source) ? this.RawAnchor(place, place) : this.OriginAtom(PlaceOriginBinder(source), OriginKind.Projection, PlaceOriginSlot(source)))
            : reachedThrough ? layer.Origin!
            : this.MeetWith(layer.Origin!, IsExclusive(layer.Semantics) ? this.EnvironmentContainment(source.BoundSymbol) : null)!;
    }

    // The facts of one containment walk (ContainedOrigin) from a Place's last step outward, decided by the walk alone (SPEC 15.6.3, 13.5.5,
    // 15.6.1): Met, the Origins met; Unbounded, the slot of a followed pair layer that does not restart the dependency, kept while no layer
    // outside it is met (in its borrow cases it outlives every layer outside it, a premise of those cases alone, so a met layer outside
    // makes it redundant); Dereferenced, whether every admitted case has dereferenced a layer (an owner case selects its operand Place).
    private struct ContainmentWalk
    {
        internal BoundOrigin? Met { get; set; }

        internal BoundOrigin? Unbounded { get; set; }

        internal bool Dereferenced { get; set; }
    }
}
