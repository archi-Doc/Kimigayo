// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Call retention, atoms (SPEC 15.6.4 steps 1-4; contract in TyCtxt.Retention.cs): each input's and the result's Origin atoms and
// carriers (RetentionInput, RetentionWalk), a Closure contract's call receiver input (ClosureReceiver) and every Closure part read as
// its body sees it (ClosureEnvironment; DECISIONS R4-M148), the content a raw layer's Origin lets the call write (RawContent), the
// borrow a Type refers through (BorrowReferent) and the Closure whose call receiver a borrow is (ReceiverClosure); Binding.Retention.cs
// reads them into premises, Binding.Retention.Rows.cs into rows.
public sealed partial class Binding
{
    // SPEC 15.6.4 (DECISIONS R4-M116, BU-40, BU-66): the raw sources of the contract and of each erased signature being walked, each input
    // once, in walk order: a raw argument (RetentionInput: null, its own value, and R′'s) and an input whose Type holds a raw layer stating
    // no Origin as it is read, by value without passing a shared layer (RawSource; a layer stating one is a stated layer only, G147 rulings
    // 39 and 44), with what the hold walk keeps of it there (DECISIONS BU-66, A2): Value where it holds the layer by value (a Tuple
    // element, a Field, a Type argument), Chain past an exclusive borrow (what a chain of reads through the input carries, never the
    // borrow's own Loans), the input's sources joined; reused.
    private readonly List<(int Input, RetentionKind? Kind)> retentionRawSources = new();

    // The Origins the raw layers of the inputs state (SPEC 5.7 Fits and claims; DECISIONS BU-66 as amended by C-352), each with the kind the
    // input holds its raw layers by, joined per input (RetentionInput; null for a raw argument): per raw layer that states an Origin as it
    // is read (ReadOrigin), that Origin, and for one past an exclusive borrow the borrow's outer Origin, whose storage it lies in (a layer stating
    // that Origin retains as that borrow would, its chain included); a layer stating none is no further entry, since it fits no layer
    // stating one but by a claim. The hold walk's stated arm reads them (Binding.Retention.Rows.cs, StatedFits); reused.
    private readonly List<(int Input, BoundOrigin Origin, RetentionKind? Kind)> retentionRawStated = new();

    // Where an atom lies below its input's layers (RetentionAtom.Place, RetentionWalk): past a shared layer or a raw layer whose step is
    // Shared (Shared); in a Closure's environment, whose bindings' Types its body's captures wrote, not the walked signature (Captured,
    // DECISIONS BU-108: no signature cap judges a layer there).
    [Flags]
    private enum RetentionPlace : byte
    {
        None = 0,
        Shared = 1,
        Captured = 2,
    }

    // SPEC 15.8.2, 7.7.3 (DECISIONS R4-M148, Z4-M): a Closure contract's input 0, its call receiver: `R/Env during ρ`, R the `receiver`
    // the callee's Type admits (its one reader, TyCtxt.Retains, passes TyCtxt.ClosureReceiverIn of it: a view's case Type its case's),
    // Env the callee's Closure Type, whose environment the walk reads as the body sees it (TyCtxt.ClosureEnvironment), and ρ its receiver
    // atom (ClosureValueSlot); Env by value under `owner/Self`. The body is checked once, against the strongest receiver
    // (BoundClosure.Receiver), which the MIR body's receiver local has over the definition's environment, without ρ (MirBuilder).
    internal BoundType ClosureReceiver(FunctionKoto closure, SemanticsKind receiver, BoundType callee)
        => receiver == SemanticsKind.Owner ? callee : this.types.Reference(receiver, callee, this.OriginAtom(closure, OriginKind.Projection, ClosureValueSlot));

    // A callable Type, which stores nothing (SPEC 15.2.3): no position below it is storage a call writes (RetentionParts, the hold walk).
    private static bool IsCallable(BoundType type) => type.Kind is BoundTypeKind.Function or BoundTypeKind.FunctionItem;

    // A shared borrow or shared handle (ref, objref, rc, arc, a Slice): past it a call reaches storage for reads only (SPEC 5.2.1).
    private static bool SharedLayer(BoundType type)
        => type.Kind == BoundTypeKind.Slice ||
            (type.Kind == BoundTypeKind.Semantics && type.Semantics is SemanticsKind.Ref or SemanticsKind.ObjRef or SemanticsKind.Rc or SemanticsKind.Arc);

    // SPEC 15.8.2 (DECISIONS R4-M148; PLAN G941): the Closure literal whose call receiver a borrow is, given the borrow's outer Origin
    // (BorrowReferent's) and the Type past it (its referent, or a key's Type past it): the outer Origin is the literal's receiver atom ρ
    // (ClosureReceiver's, at ClosureValueSlot) and the Type is that literal's Closure (its symbol declares the literal); null for any other.
    // The one test of a receiver: RetentionInput, RootWalk and OwnLayers read it.
    private static FunctionKoto? ReceiverClosure(BoundOrigin? outer, BoundType? closure)
        => outer is { Kind: OriginKind.Projection, Slot: ClosureValueSlot, Binder: FunctionKoto receiver } &&
            ReferenceEquals(closure?.Symbol?.Declaration, receiver) ? receiver : null;

    // BU-225's one predicate at a layer (PLAN G963): whether a walk that meets `type` through no shared layer and outside a callable meets
    // what lies past it so: past a pair layer only where its binder admits an exclusive Semantics (IsInvariantLayer, BU-3); past no
    // callable Type, shared layer (SharedLayer) or raw layer whose step at its position (`step`) is Shared. The atom walk narrows
    // RetentionAtom.HiddenWritable by it (RetentionWalk, RetentionParts), and a handed callable's write test descends by it
    // (WrittenThrough: Filled's Fill.Writes, Handed, Routes), so the storage a writer reaches is decided by one rule.
    private bool HiddenWritableBelow(BoundType type, HoldStep step)
        => TryPairLayer(type, out _, out _) ? IsInvariantLayer(type, this, null, default)
            : !IsCallable(type) && !SharedLayer(type) && step != HoldStep.Shared;

    // BU-1's fresh value at a layer (PLAN G993, G935): a value a call stores anew brings its whole Type, so what lies past one of its entries
    // is new too past a part held by value (ByValue), an own step (an owning handle's payload, rc and arc included: a fresh handle brings
    // a new payload; an object's; a collection's elements) and a raw layer's pointee where FreshPast passes the layer (a fresh pointer
    // stating no Origin, `self` or `static` may address storage the call allocates); never past a borrow, whose referent is no storage
    // the call makes (none is valid during the borrow's Origin), a raw layer stating a borrow's Origin, a callable, or an erased or
    // abstract part. WrittenThrough reads it for "writes a value of T"; HiddenWritableBelow is "writes what T hides".
    private bool FreshBelow(BoundType type, in Hold entry)
        => this.FreshPast(type, entry.Step) && (this.ByValue(type, entry) || entry.Step == HoldStep.Own || (entry.Flags & HoldFlags.Raw) != 0);

    // The layer half of FreshBelow, which the atom walk reads too (RetentionWalk, RetentionParts: an occurrence below a written invariant
    // position is writable past a layer only where a fresh value passes it, BU-1 narrowed by PLAN G993): no callable, no borrow, and no
    // raw layer but at the steps Own (`self`), Address (no Origin) and Static (`step`, the layer's at its position): a layer stating a
    // borrow's Origin addresses only storage that Origin covers (SPEC 5.7), which is no part of a value the call stores anew.
    private bool FreshPast(BoundType type, HoldStep step)
        => !IsCallable(type) && this.BorrowReferent(type, out _, out _) is null &&
            (!type.IsPointer || step is HoldStep.Own or HoldStep.Address or HoldStep.Static);

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
    // past a shared layer (SPEC 5.7, 8.11.4 rule 1); a Closure's borrowed call receiver, the input whose outer Origin is its receiver atom
    // ρ, has its environment bindings as Fields of the referent, each written as SPEC 7.7.2 lets the call (SPEC 15.8.2, DECISIONS R4-M148,
    // D1): a `var` binding where the receiver is exclusive, what an exclusive layer in any binding refers to, a `let` binding itself never
    // ("no Closure body can replace its environment"; a by-value receiver writes no binding, as any by-value input). A raw input is
    // retained as a borrowed one, its pointee as the referent (SPEC 15.6.4, DECISIONS R4-M116): its parts are Content, walked by its step
    // (RetentionParts); its own layer's Origin is an atom of no kind, which places it among the premises as a borrow's outer Origin is but
    // gives no row (R-B3: RawContent's rows and its own value decide), and its own value is retained by its Type (RetentionRows, over
    // retentionRawSources, as the value of any input whose walk meets a raw layer it holds by value).
    private void RetentionInput(BoundType input, int index, BoundType? declared)
    {
        var (sources, stated) = (this.retentionRawSources.Count, this.retentionRawStated.Count);
        if (this.BorrowReferent(input, out var outer, out var exclusive) is { } referent)
        {
            this.AddRetentionAtom(new(index, RetentionKind.Value, Writable: false, outer, null, null), callable: false);
            var place = exclusive ? RetentionPlace.None : RetentionPlace.Shared;
            var written = Below(declared, declared is null ? null : this.BorrowReferent(declared, out _, out _));
            var content = new RetentionAtom(index, RetentionKind.Content, exclusive, null, null, outer, place, written, HiddenWritable: exclusive);
            var captured = content with { Place = place | RetentionPlace.Captured, Signature = Below(written, null) };
            var variance = exclusive ? OriginVariance.Invariant : OriginVariance.Covariant;
            if (ReceiverClosure(outer, referent) is { } receiver)
            {
                var (captures, environment) = (receiver.ClosureDefinition!.Captures, this.ClosureEnvironment(referent));
                for (var i = 0; i < environment.Components.Count; i++)
                {
                    var field = captures[i].Environment.MutableCapture ? variance : OriginVariance.Covariant;
                    this.RetentionWalk(environment.Components[i], captured, field, callable: false);
                }
            }
            else
            {
                this.RetentionWalk(referent, content, variance, callable: false);
            }
        }
        else if (input is { IsPointer: true })
        {
            this.retentionRawSources.Add((index, null));
            this.RecordStatedOrigin(OwnedOrigin(input), index, null);
            this.AddRetentionAtom(new(index, Kind: null, Writable: false, OwnedOrigin(input), null, null), callable: false);
            var content = new RetentionAtom(index, RetentionKind.Content, Writable: true, null, null, null, Signature: declared, HiddenWritable: true);
            this.RetentionParts(input, content, OriginVariance.Covariant, callable: false);
        }
        else
        {
            var value = new RetentionAtom(index, RetentionKind.Value, Writable: true, null, null, null, Signature: declared, HiddenWritable: true);
            this.RetentionWalk(input, value, OriginVariance.Covariant, callable: false);
        }

        var list = this.retentionRawSources;
        for (var i = sources + 1; i < list.Count; i++)
        {
            list[sources] = (index, list[sources].Kind | list[i].Kind); // The input once; its own layer (null) stays alone.
        }

        if (list.Count > sources + 1)
        {
            list.RemoveRange(sources + 1, list.Count - sources - 1);
        }

        // StatedFits keeps the input by the kind it holds its raw layers by, joined over all of them (a raw argument's own layer, null,
        // stays alone), as it did when every raw layer was a raw source.
        var (states, kind) = (this.retentionRawStated, list.Count > sources ? list[sources].Kind : RetentionKind.None);
        for (var i = stated; i < states.Count; i++)
        {
            kind |= states[i].Kind;
        }

        for (var i = stated; i < states.Count; i++)
        {
            states[i] = (index, states[i].Origin, kind);
        }
    }

    // The atoms and carriers of a Type at a position of `variance` within its input (OwnedOrigins and the outer Origin, SPEC 15.2.3): a pair
    // layer's slot and target; a Type parameter or a pair's target as a carrier; the Origin, Origin arguments and components of any other,
    // so a projection or a dependent Semantics term is its parts and joins its base. An occurrence is writable storage of its input when
    // the walk has reached an invariant position through storage the call may write (`at.Writable`: an exclusive referent, the pointee
    // of a raw layer without an Origin or with `static`, or a value the call may write, whose nominal Type may hide an exclusive layer)
    // at or above it: a value stored there brings its whole Type, so every occurrence below that position is in it, past any layer a
    // fresh value passes (FreshPast: no callable, no borrow, no raw layer stating a borrow's Origin; SPEC 15.6.4, 5.7, DECISIONS BU-1 as
    // PLAN G993 narrows it). Before such a position, the walk reaches no writable storage past a shared layer or a raw layer
    // the step makes Shared (`at.Shared`; RawStepAt: the signature's or the binder's inputs state its Origin only by shared borrows, so a
    // pointer read from it has read authority only, SPEC 5.2.1 condition 3, 8.11.4 rule 1; D10, superseding R4-M51's raw clause), nor past a
    // pair layer whose binder admits no exclusive Semantics (an exclusive borrow layer, uniq, objuniq, or a pair layer admitting one, is
    // walked by value in every case, DECISIONS BU-3). Which of an input's occurrences retain its own value, and where, is the storage
    // walk's and the sources' (Binding.Retention.Storage.cs, .Sources.cs; DECISIONS R4-M154). A raw layer with another Origin
    // addresses what each borrow its Origin
    // belongs to admits (RawContent), and its atom is that Origin, as a borrow's; what a raw layer without one retains is the hold walk's
    // (Binding.Retention.Rows.cs), which starts at the positions of the storage walk (DECISIONS BU-52), not here. A callable Type stores
    // nothing, and its per-call Origins belong to no value of it (SPEC 15.2.3). What a position adds depends on its Type, `at`, `variance`
    // and `callable` alone (the scope and raw inputs are fixed for the contract or erased signature), and a second visit adds the same atoms
    // again, so each position is walked once (retentionPositions): the walk is linear in the distinct positions, not in the paths to them
    // through the interned Types (V16; DECISIONS BU-55).
    private void RetentionWalk(BoundType type, RetentionAtom at, OriginVariance variance, bool callable)
    {
        if (!this.retentionPositions.Add((type, at, variance, callable)))
        {
            return;
        }

        var here = at with { Writable = at.Writable && variance == OriginVariance.Invariant };
        if (TryPairLayer(type, out _, out var target))
        {
            var exclusive = IsInvariantLayer(type, this, null, default);
            var slot = this.OuterOrigin(type);
            this.AddRetentionAtom(here with { Origin = slot }, callable);
            var inner = ComposeVariance(variance, exclusive ? OriginVariance.Invariant : OriginVariance.Covariant);
            var written = this.SignaturePast(at.Signature, type, default);
            var below = at with
            {
                Writable = (here.Writable && this.FreshPast(type, default)) || (at.Writable && exclusive), Layer = slot ?? at.Layer, Signature = written,
                HiddenWritable = at.HiddenWritable && this.HiddenWritableBelow(type, default),
            };
            this.RetentionWalk(target, below, inner, callable);
            return;
        }

        if (type.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection)
        {
            this.retentionAtoms.Add(here with { Carrier = type, Signature = null });
        }

        this.AddRetentionAtom(here with { Origin = OwnedOrigin(type) }, callable);
        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            var position = ComposeVariance(variance, this.OriginArgumentVariance(type, i, this));
            this.AddRetentionAtom(at with { Origin = type.OriginArguments[i], Writable = at.Writable && position == OriginVariance.Invariant }, callable);
        }

        this.RetentionParts(type, at, variance, callable);
    }

    // The walk below a Type's own layer at a position of `variance` (RetentionWalk): its components, writable, shared and past a raw
    // layer's step as RetentionWalk states, and the content a raw layer's Origin lets the call write (RawContent); a Closure's components
    // are its environment as its body sees it (TyCtxt.ClosureEnvironment, DECISIONS R4-M148; one that does not substitute is the
    // contract's Limit). A raw input's walk starts here, past its own layer's atom (RetentionInput). A raw layer of an input not past a
    // shared layer is classified by RawSource (a raw source, retentionRawSources, or stated, retentionRawStated; SPEC 15.6.4 at the raw source, DECISIONS
    // BU-66; a call writes through none past one, 5.2.1), and so is one a struct or enum there holds as a part of its value (StoredRawSources).
    private void RetentionParts(BoundType type, RetentionAtom at, OriginVariance variance, bool callable)
    {
        at = type.Kind == BoundTypeKind.Closure ? at with { Place = at.Place | RetentionPlace.Captured, Signature = Below(at.Signature, null) } : at;
        type = type.Kind == BoundTypeKind.Closure ? this.ClosureEnvironment(type) : type;
        var writable = at.Writable && variance == OriginVariance.Invariant;
        var layer = this.BorrowReferent(type, out var outer, out _) is not null ? outer ?? at.Layer : at.Layer;
        var inside = callable || IsCallable(type);
        var shared = SharedLayer(type);
        var raw = type is { IsPointer: true };
        if (raw && !inside && !at.Shared && at.Input >= 0)
        {
            this.RawSource(type, at);
        }

        var step = raw && !inside ? this.RawStepAt(type, null, null, at.Signature) : HoldStep.Address;
        var past = at.Shared || shared || step == HoldStep.Shared;
        var through = raw ? !past && step is HoldStep.Address or HoldStep.Static : at.Writable && !shared;
        var below = at with
        {
            Layer = layer, Writable = !inside && (through || (writable && this.FreshPast(type, step))), Place = past ? at.Place | RetentionPlace.Shared : at.Place,
            HiddenWritable = at.HiddenWritable && !callable && !at.Shared && this.HiddenWritableBelow(type, step),
        };
        if (raw && !inside && !past && step == HoldStep.Exclusive && at.Input >= 0)
        {
            this.RawContent(OwnedOrigin(type)!, at.Input, at.Signature);
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            var component = below with { Signature = Below(at.Signature, DeclaredComponent(at.Signature, type, i)) };
            this.RetentionWalk(type.Components[i], component, ComposeVariance(variance, ComponentVariance(type, i, this)), inside);
        }

        if (!inside && !past && at.Input >= 0 && (AdtDef.IsStruct(type) || AdtDef.IsEnum(type)))
        {
            this.StoredRawSources(type, null, null, below);
        }
    }

    // A raw layer input `at.Input` holds at a position past no shared layer, the one classification every reader of the raw sources reads
    // (G147 rulings 39 and 44): held by its own value where the input holds the position by value, else by its chain. A layer stating an
    // Origin as it is read there (ReadOrigin) retains as the borrow that Origin belongs to would (SPEC 15.6.4, 5.7), which the walk's
    // atoms name: it is a stated layer only (retentionRawStated), never a raw source. A layer stating none is one of the input's raw
    // sources (SPEC 15.6.4 at the raw source, DECISIONS BU-66). Past a borrow, either is stated by that borrow's Origin too.
    private void RawSource(BoundType layer, in RetentionAtom at)
    {
        var kind = at.Kind == RetentionKind.Value ? RetentionKind.Value : RetentionKind.Chain;
        if (!this.RecordStatedOrigin(this.ReadOrigin(layer, at), at.Input, kind))
        {
            this.retentionRawSources.Add((at.Input, kind));
        }

        if (at.Kind != RetentionKind.Value && at.Layer is { } borrow)
        {
            this.retentionRawStated.Add((at.Input, borrow, kind)); // Past a borrow, in its referent: a layer stating that Origin addresses it.
        }
    }

    // SPEC 15.6.4 ("A raw layer that an argument holds by value through no shared layer, a Tuple element, an Option payload, a Field or a
    // Type argument included, is retained as a raw pointer argument is"; D-933): the raw layers a struct or enum holds as parts of its
    // value, read by the one by-value criterion the storage walk reads for the same phrase (ByValue: its Fields, Case payloads and base,
    // the Tuples and fixed arrays within them, an owning handle's payload, a collection's elements; never past an rc or arc payload, a
    // shared layer, SPEC 15.6, as the atom walk stops at SharedLayer), are classified as its components' are (RawSource),
    // each Type once per position (retentionStored, which also ends a recursive Type's walk, e.g. `Tree { kids: Array<Tree> }`, so the walk
    // is linear in the distinct parts). Their Origins are the instance's Origin arguments, which the walk's atoms name already, so it adds
    // no atom; at Binding's Limit the contract is the Limit, as the hold walk's.
    private void StoredRawSources(BoundType type, BoundType? written, BoundType? holder, in RetentionAtom at)
    {
        if (!this.retentionStored.Add((type, at)))
        {
            return;
        }

        var run = this.HeldRun(type, written, holder, out var complete);
        this.retentionLimit |= !complete;
        for (var i = run.Start; i < run.End; i++)
        {
            if (this.retentionHeld[i] is { Part: { } part } entry && this.ByValue(type, entry))
            {
                if (part is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw })
                {
                    this.RawSource(part, at);
                }
                else
                {
                    this.StoredRawSources(part, entry.Written, entry.Holder, at);
                }
            }
        }

        this.retentionHeld.RemoveRange(run.Start, run.End - run.Start);
    }

    // SPEC 5.7 ("Reading a Field"): the Origin a raw layer of an input states as it is read at its position: a `during self` layer
    // (HoldsHolder: for a raw layer, the layer itself) the holder's, which past a dereference is the Origin of the nearest borrow above it
    // (`at.Layer`) and by value none; any other layer the Origin it states (OwnedOrigin; Unstated for one its declaration writes without
    // `during`).
    private BoundOrigin? ReadOrigin(BoundType layer, in RetentionAtom at)
        => this.HoldsHolder(layer) ? (at.Kind == RetentionKind.Value ? null : at.Layer) : OwnedOrigin(layer);

    // A raw layer of input `index`, held with `kind`, as it is read (`origin`): recorded in retentionRawStated where it states an Origin;
    // whether it does.
    private bool RecordStatedOrigin(BoundOrigin? origin, int index, RetentionKind? kind)
    {
        if (origin is { Kind: not OriginKind.Unstated })
        {
            this.retentionRawStated.Add((index, origin, kind));
            return true;
        }

        return false;
    }

    // A Closure part of a contract input as its body sees it (TyCtxt.ClosureEnvironment, DECISIONS R4-M148): its environment, read at every
    // Closure part, the call receiver's (RetentionInput) and a nested one's (RetentionParts); one that does not substitute is the
    // contract's Limit.
    private BoundType ClosureEnvironment(BoundType closure)
    {
        var environment = this.types.ClosureEnvironment(closure);
        this.retentionLimit |= environment is null;
        return environment ?? closure;
    }

    // SPEC 5.7 Calls (D15; design-final §6.2): a raw parameter layer whose Origin steps Exclusive lets the call write what each borrow the
    // Origin belongs to admits at the layer's position (ContractMembers: the referent of each input whose outer Origin it is and the
    // keepers of each slot it binds in a nominal Type of the contract, substituted by that Type; or the Origin's own where it is bound,
    // `signature` the declared counterpart that tells them apart), walked as input `index`'s writable
    // content. Each (Origin, input) is walked once per contract or erased signature, which loses nothing: its members and its content
    // atom are the same each time. Unanswered members make the contract CallRetention.Limit.
    private void RawContent(BoundOrigin origin, int index, BoundType? signature)
    {
        if (!this.retentionRawVisits.Add((origin, index)))
        {
            return;
        }

        var content = new RetentionAtom(index, RetentionKind.Content, Writable: true, null, null, origin, HiddenWritable: true);
        var start = this.rawMembers.Count;
        this.ContractMembers(origin, null, signature, CollectionsMarshal.AsSpan(this.retentionScope));
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
            this.retentionAtoms.Add(atom with { Signature = null }); // A position's declared counterpart is no part of its atom.
        }
    }

    // An Origin atom or a carrier of an input or of the result (CallRetention.ResultOutput): the kind it gives the input (none for a raw input's own
    // layer's Origin, RetentionInput), whether it is writable storage of the input (RetentionWalk), the Origin of the nearest borrow,
    // Slice or pair layer above it, which a carrier's hidden Origins outlive (SPEC 15.6.1), and where it lies (RetentionPlace); while the
    // walk carries it, the walked signature's declared Type at its position (Signature, as StorageAt's; DECISIONS BU-113; no atom keeps it),
    // and whether the walk met it through no shared layer and outside a callable (HiddenWritable, PLAN G963, BU-225: narrowed at each
    // layer by HiddenWritableBelow, false past a shared borrow or handle, a raw layer stepping Shared or a pair layer admitting no
    // exclusive case, and inside a callable Type; the condition on which the storage walk enters a state, StorageAt.HiddenWritable), which
    // says for a carrier that what its instances hide is written.
    private readonly record struct RetentionAtom(
        int Input, RetentionKind? Kind, bool Writable, BoundOrigin? Origin, BoundType? Carrier, BoundOrigin? Layer, RetentionPlace Place = RetentionPlace.None,
        BoundType? Signature = null, bool HiddenWritable = false)
    {
        public bool Shared => (this.Place & RetentionPlace.Shared) != 0;

        public bool Captured => (this.Place & RetentionPlace.Captured) != 0;
    }
}
