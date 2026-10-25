// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Call retention, atoms (SPEC 15.6.4 steps 1-4; contract in TyCtxt.Retention.cs): each input's and the result's Origin atoms and
// carriers (RetentionInput, RetentionWalk), the content a raw layer's Origin lets the call write (RawContent) and the borrow a Type
// refers through (BorrowReferent); Binding.Retention.cs reads them into premises, Binding.Retention.Rows.cs into rows.
public sealed partial class Binding
{
    // SPEC 15.6.4 (DECISIONS R4-M116, BU-40): the raw inputs of the contract being computed with their Types, and the holds of its outputs,
    // each with its output, in walk order: the result's whole Type (Retention.Result), and each position of input j's writable storage
    // whose Type a raw input's own value or a raw layer may reach (a raw layer, an abstract part, or a struct or enum whose stored content
    // may hold one); reused.
    private readonly List<(int Input, BoundType Type)> retentionRawInputs = new();
    private readonly List<(int Output, BoundType Type)> retentionHolds = new();

    // Whether a position of the Type may hold a raw input's own value or a raw layer, at it or in its stored content (the hold walk,
    // Binding.Retention.Rows.cs): a raw layer, an abstract part (a pair layer among them), a struct or an enum.
    private static bool MayHold(BoundType type)
        => type.IsAbstract || type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw } || AdtDef.IsStruct(type) || AdtDef.IsEnum(type);

    // A callable Type, which stores nothing (SPEC 15.2.3): no position below it is storage a call writes (RetentionParts, the hold walk).
    private static bool IsCallable(BoundType type) => type.Kind is BoundTypeKind.Function or BoundTypeKind.FunctionItem;

    // A borrow of storage a value of the Type refers to, with its outer Origin and referent: a ref, uniq, objref or objuniq layer; a
    // pair layer whose binder admits only those, a borrow in each admitted case, its slot the outer Origin (SPEC 8.1.2) and exclusive when
    // an exclusive borrow is admitted (DECISIONS BU-3); a Slice, a shared borrow of its elements (SPEC 4.8.1); a UniqSlice, an exclusive
    // one, its `source` the outer Origin (SPEC 4.8.2).
    private BoundType? BorrowReferent(BoundType type, out BoundOrigin? outer, out bool exclusive)
    {
        (outer, exclusive) = (type.Origin, IsExclusive(type.Semantics));
        if (type is { Kind: BoundTypeKind.Semantics, Components: [var referent] } && IsBorrow(type.Semantics))
        {
            return referent;
        }

        if (this.LayerAdmitted(type) is not SemanticsMask.None and var admitted && (admitted & ~SemanticsMask.Borrow) == 0 &&
            TryPairLayer(type, out _, out var target))
        {
            (outer, exclusive) = (this.OuterOrigin(type), (admitted & (SemanticsMask.Uniq | SemanticsMask.ObjUniq)) != 0);
            return target;
        }

        if (type is { Kind: BoundTypeKind.Slice, Components: [var elements] })
        {
            return elements;
        }

        if (type is { Symbol.LibraryDeclaration: KimiDeclarationId.UniqSlice, Components: [var exclusiveElements], OriginArguments: [var source] })
        {
            (outer, exclusive) = (source, true);
            return exclusiveElements;
        }

        return null;
    }

    // An input's atoms: a borrowed input (BorrowReferent) gives Value through its outer Origin and Content through its referent (SPEC
    // 15.6.4 step 4), which an exclusive one lets the call write, its referent invariant there (SPEC 15.3.5), and a shared one's lies
    // past a shared layer (SPEC 5.7, 8.11.4 rule 1). A raw input is retained as a borrowed one, its pointee as the referent (SPEC 15.6.4,
    // DECISIONS R4-M116): its parts are Content, walked by its step (RetentionParts); its own layer's Origin is an atom of no kind, which
    // places it among the premises as a borrow's outer Origin is but gives no row (R-B3: RawContent's rows and its own value decide), and
    // its own value is retained by its Type (RetentionRows, over retentionRawInputs).
    private void RetentionInput(BoundType input, int index)
    {
        if (this.BorrowReferent(input, out var outer, out var exclusive) is { } referent)
        {
            this.AddRetentionAtom(new(index, RetentionKind.Value, Writable: false, outer, null, null), callable: false);
            var content = new RetentionAtom(index, RetentionKind.Content, exclusive, null, null, outer, Shared: !exclusive);
            this.RetentionWalk(referent, content, exclusive ? OriginVariance.Invariant : OriginVariance.Covariant, callable: false);
        }
        else if (input is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw })
        {
            this.retentionRawInputs.Add((index, input));
            this.AddRetentionAtom(new(index, Kind: null, Writable: false, OwnedOrigin(input), null, null), callable: false);
            this.RetentionParts(input, new(index, RetentionKind.Content, Writable: true, null, null, null), OriginVariance.Covariant, callable: false);
        }
        else
        {
            this.RetentionWalk(input, new(index, RetentionKind.Value, Writable: true, null, null, null), OriginVariance.Covariant, callable: false);
        }
    }

    // The atoms and carriers of a Type at a position of `variance` within its input (OwnedOrigins and the outer Origin, SPEC 15.2.3): a pair
    // layer's slot and target; a Type parameter or a pair's target as a carrier; the Origin, Origin arguments and components of any other,
    // so a projection or a dependent Semantics term is its parts and joins its base. An occurrence is writable storage of its input when
    // the walk has reached an invariant position through storage the call may write (`at.Writable`: an exclusive referent, the pointee
    // of a raw layer without an Origin or with `static`, or a value the call may write, whose nominal Type may hide an exclusive layer)
    // at or above it: a value stored there brings its whole Type, so every occurrence below that position is in it, past any layer
    // (SPEC 15.6.4, DECISIONS BU-1). Before such a position, the walk reaches no writable storage past a shared layer or a raw layer
    // the callee's own signature makes Shared (`at.Shared`; SignatureStep: only shared input borrows state its Origin there, so a pointer
    // read from it has read authority only, SPEC 5.2.1 condition 3, 8.11.4 rule 1; D10, superseding R4-M51's raw clause), nor past a
    // pair layer whose binder admits no exclusive Semantics. A raw layer with another Origin addresses what each borrow its Origin
    // belongs to admits (RawContent), and its atom is that Origin, as a borrow's; what a raw layer without one retains is the hold walk's
    // (Binding.Retention.Rows.cs). A callable Type stores nothing, and its per-call Origins belong to no value of it (SPEC 15.2.3). A
    // position of an input's writable storage whose Type is a raw layer, an abstract part, a struct or an enum is one of that input's
    // holds; the result's one hold is its whole Type (BeginRetention).
    private void RetentionWalk(BoundType type, RetentionAtom at, OriginVariance variance, bool callable)
    {
        var here = at with { Writable = at.Writable && variance == OriginVariance.Invariant };
        if (!callable && here.Writable && MayHold(type))
        {
            this.retentionHolds.Add((here.Input, type));
        }

        if (TryPairLayer(type, out _, out var target))
        {
            var exclusive = IsInvariantLayer(type, this);
            var slot = this.OuterOrigin(type);
            this.AddRetentionAtom(here with { Origin = slot }, callable);
            var inner = ComposeVariance(variance, exclusive ? OriginVariance.Invariant : OriginVariance.Covariant);
            this.RetentionWalk(target, at with { Writable = here.Writable || (at.Writable && exclusive), Layer = slot ?? at.Layer }, inner, callable);
            return;
        }

        if (type.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection)
        {
            this.retentionAtoms.Add(here with { Carrier = type });
        }
        else if (type.Kind == BoundTypeKind.Function)
        {
            this.retentionCallables.Add(type);
        }

        this.AddRetentionAtom(here with { Origin = OwnedOrigin(type) }, callable);
        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            var position = ComposeVariance(variance, type.Symbol?.Schema?.Origins[i].Variance ?? OriginVariance.Invariant);
            this.AddRetentionAtom(at with { Origin = type.OriginArguments[i], Writable = at.Writable && position == OriginVariance.Invariant }, callable);
        }

        this.RetentionParts(type, at, variance, callable);
    }

    // The walk below a Type's own layer at a position of `variance` (RetentionWalk): its components, writable, shared and past a raw
    // layer's step as RetentionWalk states, and the content a raw layer's Origin lets the call write (RawContent). A raw input's walk
    // starts here, past its own layer's atom (RetentionInput).
    private void RetentionParts(BoundType type, RetentionAtom at, OriginVariance variance, bool callable)
    {
        var writable = at.Writable && variance == OriginVariance.Invariant;
        var layer = this.BorrowReferent(type, out var outer, out _) is not null ? outer ?? at.Layer : at.Layer;
        var inside = callable || IsCallable(type);
        var shared = type.Kind == BoundTypeKind.Slice ||
            (type.Kind == BoundTypeKind.Semantics && type.Semantics is SemanticsKind.Ref or SemanticsKind.ObjRef or SemanticsKind.Rc or SemanticsKind.Arc);
        var raw = type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw };
        var rawOrigin = type.RawOrigin;
        var step = raw && !inside ? this.SignatureStep(rawOrigin, CollectionsMarshal.AsSpan(this.retentionScope)[..this.retentionInputs]) : RawStep.Unstated;
        var past = at.Shared || shared || step == RawStep.Shared;
        var through = raw ? !past && step is RawStep.Unstated or RawStep.Static : at.Writable && !shared;
        var below = at with { Layer = layer, Writable = !inside && (through || writable), Shared = past };
        if (raw && !inside && !past && step == RawStep.Exclusive && at.Input >= 0)
        {
            this.RawContent(rawOrigin!, at.Input);
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            this.RetentionWalk(type.Components[i], below, ComposeVariance(variance, this.ComponentVariance(type, i)), inside);
        }
    }

    // SPEC 5.7 Calls (D15; design-final §6.2): a raw parameter layer whose Origin steps Exclusive lets the call write what each borrow the
    // Origin belongs to admits (RawMembers: the referent of each input whose outer Origin it is, the keepers of each slot it binds in a
    // nominal Type of the contract, substituted by that Type), walked as input `index`'s writable content. Each (Origin, input) is walked
    // once per contract or erased signature, which loses nothing: its members and its content atom are the same each time. An incomplete
    // member walk makes the contract CallRetention.Limit.
    private void RawContent(BoundOrigin origin, int index)
    {
        if (!this.retentionRawVisits.Add((origin, index)))
        {
            return;
        }

        var content = new RetentionAtom(index, RetentionKind.Content, Writable: true, null, null, origin);
        var start = this.rawMembers.Count;
        this.retentionLimit |= !this.RawMembers(origin, CollectionsMarshal.AsSpan(this.retentionScope), this.rawMembers);
        var end = this.rawMembers.Count;
        for (var i = start; i < end && !this.retentionLimit; i++)
        {
            this.RetentionWalk(this.rawMembers[i].Referent, content, OriginVariance.Invariant, callable: false);
        }

        this.rawMembers.RemoveRange(start, end - start);
    }

    // An Origin occurrence; a Function Type's per-call Origin, bound by its own syntax, belongs to no value of it (SPEC 15.3.4).
    private void AddRetentionAtom(RetentionAtom atom, bool callable)
    {
        if (atom.Origin is { } origin && !(callable && origin is { Kind: OriginKind.Input, Binder: FunctionTypeKoto }))
        {
            this.retentionAtoms.Add(atom);
        }
    }

    // An Origin atom or a carrier of an input or of the result (Retention.Result): the kind it gives the input (none for a raw input's own
    // layer's Origin, RetentionInput), whether it is writable storage of the input (RetentionWalk), the Origin of the nearest borrow,
    // Slice or pair layer above it, which a carrier's hidden Origins outlive (SPEC 15.6.1), and whether a shared layer or a raw layer
    // whose step is Shared lies above it.
    private readonly record struct RetentionAtom(
        int Input, RetentionKind? Kind, bool Writable, BoundOrigin? Origin, BoundType? Carrier, BoundOrigin? Layer, bool Shared = false);
}
