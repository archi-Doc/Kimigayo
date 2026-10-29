// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Call retention, per storage (SPEC 15.6.4 read as (iii) with (β′); DECISIONS R4-M154; contract in TyCtxt.Retention.cs).
// Responsibility: the one walk of what a value holds by value and reaches through its layers (StorageWalk; DECISIONS R4-M154, r2.3),
// which the output fold reads into the storages a call may write through an input and their positions (Position, which also start the
// hold walk, DECISIONS BU-52) and the input fold into an input's sources (Binding.Retention.Sources.cs, Source), and the parts a value
// holds at its own address (HeldParts), which the fits read too; Binding.Retention.Rows.cs reads the storages into StorageRetentions.
// Inputs: the contract's instantiated inputs and result, its premise environment's nodes (Binding.Retention.cs), and the stored parts
// Binding's instances keep (Instance), with each stored raw layer's step (DeclaredStep, SignatureStep).
// Outputs: per walk, the storages (the root, Own; the Abstract one, what an abstract part hides, at each carrier's ReachedNode; one per
// contract Type past an exclusive layer, keyed for its Origin-erased Type), each position's node and the Types its fit reads (Value
// tests, Reads: a raw layer stating an Origin as the borrows that Origin belongs to; no Type: any value), every Origin occurrence's
// node (Content targets), and for an input's walk each position with the Type its declaration writes there and the instance holding
// it (storageHolds: where rule A's hold walk starts, so a by-value input's Fields, payloads and elements reach it as a borrowed
// input's referent does).
// Invariants: a position is storage the call may write: a part a value of the storage holds by value (Tuple and fixed-array elements, a
// struct's Fields and base, an enum's payloads, a Closure's environment bindings, a raw layer's pointee by its own step and what a
// `during self` layer admits, Admitted), the layer of a borrow, pair or raw layer, an abstract part (whose hidden storage is the
// Abstract one) and a callable; past an exclusive borrow, pair layer admitting one, UniqSlice or raw layer the call may write lies
// another storage; past a shared one, what a stored value brings (Content targets) but no position. Each (storage, Type) is walked once,
// so the walk names every storage past the root; a Type whose storage is past Binding's limits (a growing recursive Type) is the Limit.
// Must not: judge an Origin relation (the premise closure's), decide which source a position can hold (the fits'), or read the call's
// arguments or solved Origins.
public sealed partial class Binding
{
    // The storage walk's root storage, the Abstract one (what an abstract part hides), the result's (every position of its Type, one
    // storage), and the depth bound of the source and part walks (MirTypes.Holding's).
    private const int OwnStorage = 0;
    private const int AbstractStorage = 1;
    private const int ResultStorage = 2;
    private const int StorageBound = 32;
    private const int NoStorage = -2; // An input fold's walk (RetentionSources), which names no position.

    // The storages of the walk being made (by index: Own, Abstract, the result's, then one per contract Type past an exclusive layer)
    // with each one's contract Type, its index by that Type, the (storage, Type) pairs walked and targeted, the positions (Value tests: a
    // position's node and Type, null for any value), the Content targets (every Origin occurrence's node) and an input's holds; reused.
    private readonly List<BoundType?> storageTypes = new();
    private readonly Dictionary<BoundType, int> storageOf = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<(int Storage, bool Writable, SourceAt Source, BoundType Type)> storageSeen = new();
    private readonly HashSet<(int Storage, BoundType Type)> storageTargeted = new();
    private readonly List<(int Storage, int Node, BoundType? Type)> storagePositions = new();
    private readonly List<(int Storage, int Node)> storageTargets = new();
    private readonly List<(BoundType Type, BoundType? Written, BoundType? Holder)> storageHolds = new();
    private readonly List<int> retentionOwnNodes = new();
    private readonly List<int> retentionOccurrences = new();
    private readonly HashSet<(BoundType Type, bool Callable)> retentionOccurred = new();
    private readonly List<HeldPart> retentionHeld = new(); // HeldParts' runs, a stack: each caller reads its run and removes it.
    private readonly List<BoundType> retentionReads = new(); // Reads' runs, a stack.

    // An abstract part a carrier stands for: a Type parameter that is no pair layer, or a pair's target (RetentionWalk's carriers).
    private static bool IsCarrier(BoundType type)
        => type.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection && !TryPairLayer(type, out _, out _);

    // An abstract part, a carrier or an associated projection, whose instance's storage the body does not see (SPEC 8.15.1): a position it
    // is holds what reaches it, and a referent it is holds any part by value.
    private static bool Abstract(BoundType type) => type.IsAbstract && !TryPairLayer(type, out _, out _);

    // Begins a storage walk: no storage but the three fixed ones.
    private void BeginStorages()
    {
        this.storageTypes.Clear();
        this.storageTypes.AddRange([null, null, null]);
        this.storageOf.Clear();
        this.storageSeen.Clear();
        this.storageTargeted.Clear();
        this.storagePositions.Clear();
        this.storageTargets.Clear();
        this.storageHolds.Clear();
    }

    // The storage walk from input j's value (DECISIONS R4-M154): the value itself is the callee's, not storage; what its first exclusive
    // layers refer to is its root (Own), a borrowed input's referent among them. A Closure's call receiver's referent holds its environment
    // bindings as Fields where the receiver is exclusive (past a shared one the call writes nothing, SPEC 5.2.1): a `var` binding is a
    // position, a `let` binding never, and what an exclusive layer in either refers to lies past it (SPEC 15.8.2, DECISIONS R4-M148).
    private void RootWalk(OriginPremiseEnvironment environment, BoundType input)
    {
        if (this.BorrowReferent(input, out var outer, out var exclusive) is { } referent &&
            outer is { Kind: OriginKind.Projection, Slot: ClosureValueSlot, Binder: FunctionKoto receiver })
        {
            var (captures, bindings) = (receiver.ClosureDefinition!.Captures, this.ClosureEnvironment(referent));
            for (var i = 0; exclusive && i < bindings.Components.Count; i++)
            {
                this.StorageWalk(environment, bindings.Components[i], StorageAt.Output(OwnStorage, captures[i].Environment.MutableCapture));
            }

            return;
        }

        this.StorageWalk(environment, input, StorageAt.Output(-1, writable: false));
    }

    // The one walk (DECISIONS R4-M154, r2.3): a Type the walk visits, once per state it is in (StorageAt, the path's reduced prefix
    // included). Each visit emits to the fold its state names: the output fold its position where the call writes it (Position; an input's
    // also a hold, storageHolds), the input fold its sources (Source). Then what the Type holds and reaches: what a pair, borrow, shared
    // handle or raw layer refers to (Past; a raw layer by its step, `during self` content as Admitted), its parts by value (Parts); an
    // abstract part or a callable, nothing.
    private void StorageWalk(OriginPremiseEnvironment environment, BoundType? type, StorageAt at)
    {
        var source = at.Source.Input >= 0;
        var visit = (at.Storage, at.Writable, at.Source with { Above = -1 }, type!); // The layer above is no part of the state's key.
        if (type is null || (source && at.Depth > StorageBound) || !this.storageSeen.Add(visit))
        {
            return;
        }

        var targets = at.Writable && at.Storage >= 0 ? at.Storage : -1;
        if (targets >= 0)
        {
            this.Position(environment, type, at.Storage);
            if (at.Storage != ResultStorage)
            {
                this.storageHolds.Add((type, at.Written, at.Holder));
            }
        }

        var layer = source ? this.Source(environment, type, at) : -1;
        var below = at with { Written = null, Holder = null, Depth = at.Depth + 1 };
        if (TryPairLayer(type, out _, out var target))
        {
            this.Past(environment, target, targets, IsInvariantLayer(type, this), layer, below);
            if (source && this.BorrowTarget(type) is null)
            {
                this.Parts(environment, type, at); // A pair layer admitting no borrow holds its target by value in its owning cases.
            }
        }
        else if (Abstract(type) || IsCallable(type))
        {
            return;
        }
        else if (this.BorrowReferent(type, out _, out var exclusive) is { } referent)
        {
            this.Past(environment, referent, targets, exclusive, layer, below);
        }
        else if (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Rc or SemanticsKind.Arc, Components: [var payload] })
        {
            this.Past(environment, payload, targets, written: false, layer: -1, below);
        }
        else if (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw, Components: [var pointee] })
        {
            if (at.Storage == NoStorage)
            {
                this.Through(environment, pointee, layer, below); // An input fold's walk names no write: any step's layer is gone through.
                return;
            }

            var step = at.Holder is not null ? this.DeclaredStep(type, at.Written, at.Holder)
                : this.SignatureStep(OwnedOrigin(type), CollectionsMarshal.AsSpan(this.retentionScope)[..this.retentionInputs]);
            if (step == RawStep.Own)
            {
                this.Admitted(environment, pointee, at.Holder, below);
                return;
            }

            this.Past(environment, pointee, targets, step != RawStep.Shared, layer, below);
            if (step == RawStep.Exclusive && at.Holder is null)
            {
                this.Members(environment, OwnedOrigin(type)!, below);
            }
        }
        else
        {
            this.Parts(environment, type, at);
        }
    }

    // What a `during self` layer of a stored part admits (SelfAdmitted, SPEC 5.7 Calls), for the output fold: its pointee and the holding
    // instance's Type arguments by their actual Types in the same storage; where the instance has an Origin argument, the write fact: any
    // value at each argument's node (a position of no Type), in the same storage and in the Abstract one, what such a value may refer to.
    // The input fold reads that content among the holder's parts by value (HeldParts) and goes through the layer to its pointee.
    private void Admitted(OriginPremiseEnvironment environment, BoundType pointee, BoundType? holder, StorageAt at)
    {
        this.StorageWalk(environment, pointee, at);
        var any = SelfAdmitted(holder, out var arguments);
        foreach (var argument in arguments)
        {
            this.StorageWalk(environment, argument, at);
        }

        foreach (var argument in any && at.Writable && at.Storage >= 0 && at.Storage != ResultStorage ? holder!.OriginArguments : [])
        {
            var node = this.PremiseNode(environment, argument);
            this.storagePositions.AddRange([(at.Storage, node, null), (AbstractStorage, node, null)]);
            this.storageTargets.AddRange([(at.Storage, node), (AbstractStorage, node)]);
        }
    }

    // What a raw layer of the contract whose Origin steps Exclusive lets the call write (RawContent's members: the referent of each input
    // whose outer Origin it is, the keepers of each slot it binds), each past the layer, for the output fold; an incomplete member walk is
    // the Limit.
    private void Members(OriginPremiseEnvironment environment, BoundOrigin origin, StorageAt at)
    {
        if (at.Storage == NoStorage)
        {
            return;
        }

        var start = this.rawMembers.Count;
        this.retentionLimit |= !this.RawMembers(origin, CollectionsMarshal.AsSpan(this.retentionScope), this.rawMembers);
        var end = this.rawMembers.Count;
        for (var i = start; i < end; i++)
        {
            this.Past(environment, this.rawMembers[i].Referent, targets: -1, written: true, layer: -1, at);
        }

        this.rawMembers.RemoveRange(start, end - start);
    }

    // What a layer refers to. For the output fold: the storage past it where the call may write through it (the root from before the
    // root), and the result's own past any layer (a result carries its whole Type); else, below a position (`targets`, its storage; -1
    // below none), only what a value stored there brings. For the input fold: what a source goes through the layer to (Through, `layer`
    // its node, -1 for none).
    private void Past(OriginPremiseEnvironment environment, BoundType referent, int targets, bool written, int layer, StorageAt at)
    {
        if (at.Storage != NoStorage && (written || at.Storage == ResultStorage))
        {
            var past = at.Storage switch
            {
                ResultStorage => ResultStorage,
                < 0 => OwnStorage,
                _ => this.StorageOf(referent),
            };
            this.StorageWalk(environment, referent, at with { Storage = past, Writable = true });
        }
        else if (targets >= 0)
        {
            this.Targets(environment, referent, targets);
        }

        this.Through(environment, referent, layer, at);
    }

    // A value's parts by value (StorageWalk; HeldParts), walked in the same state with the Type each part's declaration writes there and
    // the instance holding it: each with an address, but `during self` content (Admitted, which the output fold reaches through its layer's
    // step); a nominal Type's Type arguments, which have none, bring the output fold only targets. A walk naming positions is the Limit
    // where a part's storage is past Binding's limits.
    private void Parts(OriginPremiseEnvironment environment, BoundType type, StorageAt at)
    {
        this.retentionLimit |= !this.HeldParts(type, at.Written, at.Holder, carried: false, out var run) && at.Storage != NoStorage;
        for (var i = run.Start; i < run.End; i++)
        {
            var part = this.retentionHeld[i];
            if (part.Addressed && (part.Place != PartPlace.Admitted || at.Storage == NoStorage))
            {
                this.StorageWalk(environment, part.Type, at with { Written = part.Written, Holder = part.Holder, Depth = at.Depth + 1 });
            }
            else if (!part.Addressed)
            {
                this.Targets(environment, at.Writable && at.Storage >= 0 ? part.Type : null, at.Storage);
            }
        }

        this.retentionHeld.RemoveRange(run.Start, run.End - run.Start);
    }

    // A position: its Value tests, one per node naming the Type's own layer or its Origin arguments (an abstract part's carrier node, also
    // for the Abstract storage it hides) and per Type its fit reads (Reads), and its own Origins as targets.
    private void Position(OriginPremiseEnvironment environment, BoundType type, int storage)
    {
        var (start, end) = this.Reads(type);
        foreach (var node in this.OwnNodes(environment, type))
        {
            for (var i = start; i < end; i++)
            {
                this.storagePositions.Add((storage, node, this.retentionReads[i]));
            }

            this.storageTargets.Add((storage, node));
        }

        this.retentionReads.RemoveRange(start, end - start);

        if (Abstract(type) && storage != ResultStorage)
        {
            this.AbstractPositions(environment, type, type);
        }

        this.Targets(environment, type, storage);
    }

    // The Types a position's fit reads (SPEC 5.7 "retains as that borrow would"; DECISIONS R4-M154, r2.2), appended to retentionReads as a
    // run its caller removes: a raw layer stating an Origin reads as each borrow that Origin belongs to among the contract's inputs
    // (RawMembers: an input whose outer Origin it is, a slot keeper), `ref/R` or `uniq/R` of its referent R by its mode, and as itself
    // where none does; any other Type as itself. An incomplete member walk is the Limit.
    private (int Start, int End) Reads(BoundType type)
    {
        var start = this.retentionReads.Count;
        var stated = type.Semantics == SemanticsKind.Raw && OwnedOrigin(type) is { Kind: not (OriginKind.Static or OriginKind.Unstated or OriginKind.Holder) };
        var members = this.rawMembers.Count;
        this.retentionLimit |= stated && !this.RawMembers(OwnedOrigin(type)!, CollectionsMarshal.AsSpan(this.retentionScope), this.rawMembers);
        for (var i = members; i < this.rawMembers.Count; i++)
        {
            var member = this.rawMembers[i];
            this.retentionReads.Add(this.types.Reference(member.Exclusive ? SemanticsKind.Uniq : SemanticsKind.Ref, member.Referent));
        }

        this.rawMembers.RemoveRange(members, this.rawMembers.Count - members);
        if (this.retentionReads.Count == start)
        {
            this.retentionReads.Add(type);
        }

        return (start, this.retentionReads.Count);
    }

    // The storage an abstract part hides (the Abstract one), a position per carrier it names at that carrier's ReachedNode, which only an
    // erased writer of the part reaches (AddErasedRelations; DECISIONS R4-M154, Revision 3).
    private void AbstractPositions(OriginPremiseEnvironment environment, BoundType? type, BoundType position)
    {
        if (type is null)
        {
            return;
        }

        if (IsCarrier(type))
        {
            var node = this.ReachedNode(environment, type);
            this.storagePositions.Add((AbstractStorage, node, position));
            this.storageTargets.Add((AbstractStorage, node));
            return;
        }

        if (TryPairLayer(type, out _, out var target))
        {
            this.AbstractPositions(environment, target, position);
            return;
        }

        foreach (var part in type.Components)
        {
            this.AbstractPositions(environment, part, position);
        }
    }

    // The nodes naming a Type's own value, which a Value test closes from (Position) or a source is named by (Source): an abstract part's
    // carriers (its base's, for an associated projection) and Origin arguments; a pair layer's slot; a layer's Origin and a nominal Type's
    // Origin arguments; else, for a value with none of its own (a Tuple, a handle, a callable), every Origin occurrence's, a callable's
    // per-call ones aside, up to the layers it holds (whose own Origins name them); a layer stating none names nothing (a destruction's
    // receiver, which no caller names).
    private List<int> OwnNodes(OriginPremiseEnvironment environment, BoundType type)
    {
        var nodes = this.retentionOwnNodes;
        nodes.Clear();
        if (IsCarrier(type))
        {
            nodes.Add(this.CarrierNode(environment, type));
            return nodes;
        }

        if (TryPairLayer(type, out _, out _))
        {
            if (this.OuterOrigin(type) is { } slot)
            {
                nodes.Add(this.PremiseNode(environment, slot));
            }

            return nodes;
        }

        if (OwnedOrigin(type) is { Kind: not OriginKind.Unstated } origin)
        {
            nodes.Add(this.PremiseNode(environment, origin));
        }

        foreach (var argument in type.OriginArguments)
        {
            nodes.Add(this.PremiseNode(environment, argument));
        }

        if ((nodes.Count == 0 && this.BorrowShaped(type) is null) || Abstract(type))
        {
            this.retentionOccurred.Clear();
            this.OwnOccurrences(environment, type, nodes, callable: false);
        }

        return nodes;
    }

    // What a value stored at a position brings past a layer it does not write through: every Origin occurrence's node (Occurrences), each
    // Type once per storage.
    private void Targets(OriginPremiseEnvironment environment, BoundType? type, int storage)
    {
        if (type is null || storage < 0 || !this.storageTargeted.Add((storage, type)))
        {
            return;
        }

        var nodes = this.retentionOccurrences;
        nodes.Clear();
        this.retentionOccurred.Clear();
        this.Occurrences(environment, type, nodes, callable: false);
        foreach (var node in nodes)
        {
            this.storageTargets.Add((storage, node));
        }
    }

    // Every Origin occurrence's node of a Type into `nodes` (Named), its parts' too, each Type once.
    private void Occurrences(OriginPremiseEnvironment environment, BoundType? type, List<int> nodes, bool callable)
    {
        if (!this.Named(environment, type, nodes, ref callable))
        {
            return;
        }

        if (TryPairLayer(type!, out _, out var target))
        {
            this.Occurrences(environment, target, nodes, callable);
            return;
        }

        foreach (var part in type!.Components)
        {
            this.Occurrences(environment, part, nodes, callable);
        }
    }

    // As Occurrences, not below a borrow layer or a raw layer stating a borrow's Origin, and a pair layer that may be a borrow naming its
    // slot instead (a Type's own value, OwnNodes).
    private void OwnOccurrences(OriginPremiseEnvironment environment, BoundType? type, List<int> nodes, bool callable)
    {
        if (type is not null && TryPairLayer(type, out _, out var target) && (this.LayerAdmitted(type) & SemanticsMask.Borrow) != 0)
        {
            if (this.retentionOccurred.Add((type, callable)) && this.OuterOrigin(type) is { } slot)
            {
                nodes.Add(this.PremiseNode(environment, slot));
            }

            return;
        }

        if (!this.Named(environment, type, nodes, ref callable) || this.BorrowShaped(type!) is not null)
        {
            return;
        }

        if (TryPairLayer(type!, out _, out target))
        {
            this.OwnOccurrences(environment, target, nodes, callable);
            return;
        }

        foreach (var part in type!.Components)
        {
            this.OwnOccurrences(environment, part, nodes, callable);
        }
    }

    // The nodes a Type itself names, into `nodes`: a carrier's node, else its own Origin (a callable's per-call Origins aside, `callable`:
    // they belong to no value of it, SPEC 15.3.4) and its Origin arguments; false where the walk met it before or it is a carrier, whose
    // parts it skips.
    private bool Named(OriginPremiseEnvironment environment, BoundType? type, List<int> nodes, ref bool callable)
    {
        if (type is null || !this.retentionOccurred.Add((type, callable)))
        {
            return false;
        }

        callable |= IsCallable(type);
        if (IsCarrier(type))
        {
            nodes.Add(this.CarrierNode(environment, type));
            return false;
        }

        if (OwnedOrigin(type) is { Kind: not OriginKind.Unstated } origin && !(callable && origin is { Kind: OriginKind.Input, Binder: FunctionTypeKoto }))
        {
            nodes.Add(this.PremiseNode(environment, origin));
        }

        foreach (var argument in type.OriginArguments)
        {
            nodes.Add(this.PremiseNode(environment, argument));
        }

        return true;
    }

    // A borrow layer (a pair layer admitting only borrows), a raw layer stating another Origin than `self`'s (or `static`), a Slice or a
    // UniqSlice: what it refers to; null for another Type, whose parts a value holds.
    private BoundType? BorrowShaped(BoundType type)
    {
        if (TryPairLayer(type, out _, out var target))
        {
            return (this.LayerAdmitted(type) & ~SemanticsMask.Borrow) == 0 ? target : null;
        }

        var borrow = type.Kind == BoundTypeKind.Semantics && type.Semantics is SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.ObjRef or
            SemanticsKind.ObjUniq;
        var refers = borrow || (type.Semantics == SemanticsKind.Raw && OwnedOrigin(type) is { Kind: not OriginKind.Unstated }) ||
            type.Kind == BoundTypeKind.Slice || type.Symbol?.LibraryDeclaration == KimiDeclarationId.UniqSlice;
        return refers && type.Components.Count > 0 ? type.Components[0] : null;
    }

    // The storage past an exclusive layer whose contract Type is `referent`, one per Type.
    private int StorageOf(BoundType referent)
    {
        if (!this.storageOf.TryGetValue(referent, out var storage))
        {
            storage = this.storageTypes.Count;
            this.storageTypes.Add(referent);
            this.storageOf.Add(referent, storage);
        }

        return storage;
    }

    // A carrier's node, an abstract part's hidden Origins (AtomNode's).
    private int CarrierNode(OriginPremiseEnvironment environment, BoundType carrier)
        => this.AtomNode(environment, new(CallRetention.ResultOutput, null, Writable: false, null, carrier, null));

    // The parts a value of a Type holds at its own address, the one enumeration of them (SPEC 15.6.3, 5.7) the walk (Parts) and the fits
    // (Holds, Borrows) read, appended to retentionHeld as a run (Start, End) its caller removes, each with the Type its declaration writes
    // there and the instance holding it (Held's reading): a Closure's environment bindings as its body sees them and any other
    // non-nominal Type's components (a Tuple's and a fixed array's at their inline positions); a struct's Fields, an enum's payloads and
    // the base in the instance's kept parts, and the content of each `during self` raw Field or payload (Admitted: its pointee and the
    // instance's Type arguments by their actual Types, SelfAdmitted); a nominal Type's Type arguments, with an address only in a
    // compiler-managed collection, whose elements they are (it keeps no part: SPEC 4.5, 4.9); a built-in Core's stored content
    // (TyCtxt.CoreContent: a string's `u8` bytes). A layer holds by value only what it owns: a raw layer stating no Origin its pointee,
    // a pair layer in an owning case its target and, `carried`, a shared handle its payload, which a new value built there carries. False
    // where a struct's or enum's storage is past Binding's limits.
    private bool HeldParts(BoundType type, BoundType? written, BoundType? holder, bool carried, out (int Start, int End) run)
    {
        var parts = this.retentionHeld;
        var start = parts.Count;
        if (this.Owned(type, carried, out var owned))
        {
            parts.AddRange(owned is null ? [] : [new(owned, null, null, PartPlace.Addressed)]);
            run = (start, parts.Count);
            return true;
        }

        var value = type.Kind == BoundTypeKind.Closure ? this.ClosureEnvironment(type) : type;
        var nominal = AdtDef.IsStruct(value) || AdtDef.IsEnum(value);
        if (nominal && !this.Readable(value, this.PrepareTypeStorage(value)))
        {
            run = (start, start);
            return false;
        }

        var instance = nominal ? this.Instance(value) : null;
        var addressed = instance is null or { Parts.Length: 0, PartsBase: null };
        if (TyCtxt.CoreContent(value) is { } content)
        {
            parts.Add(new(content, null, holder, PartPlace.Addressed));
        }

        for (var i = 0; i < value.Components.Count; i++)
        {
            parts.Add(new(value.Components[i], InlinePart(written, i), holder, addressed ? PartPlace.Addressed : PartPlace.Unaddressed));
        }

        if (instance is { Parts: var stored, PartsBase: var parent } && value.Symbol?.Adt is { } adt)
        {
            parts.AddRange(parent is null ? [] : [new(parent, null, null, PartPlace.Addressed)]);
            for (var i = 0; AdtDef.IsStruct(value) && i < adt.Fields.Length; i++)
            {
                parts.Add(new(stored[i], WrittenType(adt.Fields[i]), value, PartPlace.Addressed));
            }

            for (var i = 0; AdtDef.IsEnum(value) && i < stored.Length; i++)
            {
                for (var j = 0; stored[i] is { } payloads && adt.Cases[i] is { } selected && j < selected.Payload.Length; j++)
                {
                    parts.Add(new(payloads.Components[j], WrittenType(selected.Payload[j]), value, PartPlace.Addressed));
                }
            }
        }

        var end = parts.Count;
        for (var i = start; i < end; i++)
        {
            if (parts[i] is { Type: { Semantics: SemanticsKind.Raw, Components: [var pointee] } raw, Holder: { } owner } &&
                ReferenceEquals(owner, value) && this.DeclaredStep(raw, parts[i].Written, owner) == RawStep.Own)
            {
                _ = SelfAdmitted(owner, out var arguments);
                parts.Add(new(pointee, null, null, PartPlace.Admitted));
                foreach (var argument in arguments)
                {
                    parts.Add(new(argument, null, null, PartPlace.Admitted));
                }
            }
        }

        run = (start, parts.Count);
        return true;
    }

    // HeldParts' layers: whether the Type is a raw, pair or shared handle layer, and the part it owns (`owned`, null for none): a raw
    // layer's pointee where it states no Origin, a pair layer's target in an owning case, `carried` a shared handle's payload too.
    private bool Owned(BoundType type, bool carried, out BoundType? owned)
    {
        owned = null;
        if (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw, Components: [var pointee] })
        {
            owned = OwnedOrigin(type) is null or { Kind: OriginKind.Unstated } ? pointee : null;
            return true;
        }

        if (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Rc or SemanticsKind.Arc, Components: [var payload] })
        {
            owned = carried ? payload : null;
            return true;
        }

        if (!TryPairLayer(type, out _, out var target))
        {
            return false;
        }

        var shared = carried ? SemanticsMask.Rc | SemanticsMask.Arc : SemanticsMask.None;
        owned = (this.LayerAdmitted(type) & (SemanticsMask.Owner | SemanticsMask.Obj | shared)) != 0 ? target : null;
        return true;
    }

    // Where a part of a value lies (HeldParts): no address in the value's storage (a nominal Type's Type argument, but a collection's),
    // an address, or the content a `during self` layer addresses (Admitted), whose layer's step the output fold reads it through.
    private enum PartPlace : byte
    {
        Unaddressed,
        Addressed,
        Admitted,
    }

    // A part of a value (HeldParts): its Type (null: a part Binding did not bind), the Type its declaration writes there and the instance
    // holding it, and where it lies.
    private readonly record struct HeldPart(BoundType? Type, BoundType? Written, BoundType? Holder, PartPlace Place)
    {
        // Whether the part has an address in the value's storage.
        internal bool Addressed => this.Place != PartPlace.Unaddressed;
    }

    // The walk's state at a Type (StorageWalk; DECISIONS R4-M154, r2.3). For the output fold: the storage (-1 before the root, NoStorage
    // in an input fold's walk) and whether the call writes there; the Type the stored part's declaration writes there and the instance
    // holding it (a raw layer's step, as Held reads it); the depth. For the input fold, its own state (Source).
    private readonly record struct StorageAt(int Storage, bool Writable, BoundType? Written, BoundType? Holder, int Depth, SourceAt Source)
    {
        // An output's walk from a storage.
        internal static StorageAt Output(int storage, bool writable) => new(storage, writable, null, null, 0, new(-1, Held: false, -1, 0));

        // An input fold's walk from input `input`'s own value.
        internal static StorageAt Input(int input) => new(NoStorage, false, null, null, 0, new(input, Held: true, -1, 0));
    }

    // The input fold's state at a Type (StorageAt): the input (-1 in an output's walk), whether the Type is held by value from the input or
    // reached through a layer, the node of the layer above (Related) and the path's id (its reduced prefix, Revision 10).
    private readonly record struct SourceAt(int Input, bool Held, int Above, int Path);
}
