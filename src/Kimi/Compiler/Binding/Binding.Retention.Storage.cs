// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Call retention, per storage (SPEC 15.6.4 read as (iii) with (β′); DECISIONS R4-M154; contract in TyCtxt.Retention.cs).
// Responsibility: the storages a call may write through an input and their positions (StorageWalk), and the parts a value holds at its
// own address (HeldParts), which the sources and fits read too (Binding.Retention.Sources.cs); Binding.Retention.Rows.cs reads the
// storages into StorageRetentions.
// Inputs: the contract's instantiated inputs and result, its premise environment's nodes (Binding.Retention.cs), and the stored parts
// Binding's instances keep (Instance), with each stored raw layer's step (DeclaredStep, SignatureStep).
// Outputs: per walk, the storages (the root, Own; the Abstract one, what an abstract part hides, at each carrier's ReachedNode; one per
// contract Type past an exclusive layer, keyed for its Origin-erased Type), each position's node and Type (Value tests; no Type: any
// value) and every Origin occurrence's node (Content targets).
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

    // The storages of the walk being made (by index: Own, Abstract, the result's, then one per contract Type past an exclusive layer)
    // with each one's contract Type, its index by that Type, the (storage, Type) pairs walked and targeted, the positions (Value tests: a
    // position's node and Type, null for any value) and the Content targets (every Origin occurrence's node); reused.
    private readonly List<BoundType?> storageTypes = new();
    private readonly Dictionary<BoundType, int> storageOf = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<(int Storage, bool Writable, BoundType Type)> storageSeen = new();
    private readonly HashSet<(int Storage, BoundType Type)> storageTargeted = new();
    private readonly List<(int Storage, int Node, BoundType? Type)> storagePositions = new();
    private readonly List<(int Storage, int Node)> storageTargets = new();
    private readonly List<int> retentionOwnNodes = new();
    private readonly List<int> retentionOccurrences = new();
    private readonly HashSet<(BoundType Type, bool Callable)> retentionOccurred = new();
    private readonly List<HeldPart> retentionHeld = new(); // HeldParts' and OwnParts' runs, a stack: each caller reads its run and removes it.

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
                this.StorageWalk(environment, bindings.Components[i], OwnStorage, captures[i].Environment.MutableCapture, (null, null, 0));
            }

            return;
        }

        this.StorageWalk(environment, input, -1, writable: false, (null, null, 0));
    }

    // A Type at a position of a storage (`storage`, -1 before the root; `writable`, whether the call writes the position; `at`, the Type
    // the stored part's declaration writes there, the instance holding it and the walk's depth, as the hold walk reads a stored raw
    // layer, Held): its position and targets, then its parts by value in the same storage, and what an exclusive layer refers to in the
    // storage past it (the root's, Own, from before it; the result's own one).
    private void StorageWalk(
        OriginPremiseEnvironment environment,
        BoundType? type,
        int storage,
        bool writable,
        (BoundType? Written, BoundType? Holder, int Depth) at)
    {
        if (type is null || !this.storageSeen.Add((storage, writable, type)))
        {
            return;
        }

        var targets = writable && storage >= 0 ? storage : -1;
        if (targets >= 0)
        {
            this.Position(environment, type, storage);
        }

        var below = (Written: (BoundType?)null, Holder: (BoundType?)null, Depth: at.Depth + 1);
        if (TryPairLayer(type, out _, out var target))
        {
            this.Past(environment, target, storage, targets, IsInvariantLayer(type, this), below);
        }
        else if (Abstract(type) || IsCallable(type))
        {
            return;
        }
        else if (this.BorrowReferent(type, out _, out var exclusive) is { } referent)
        {
            this.Past(environment, referent, storage, targets, exclusive, below);
        }
        else if (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Rc or SemanticsKind.Arc, Components: [var payload] })
        {
            this.Past(environment, payload, storage, targets, written: false, below);
        }
        else if (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw, Components: [var pointee] })
        {
            var step = at.Holder is not null ? this.DeclaredStep(type, at.Written, at.Holder)
                : this.SignatureStep(OwnedOrigin(type), CollectionsMarshal.AsSpan(this.retentionScope)[..this.retentionInputs]);
            if (step == RawStep.Own)
            {
                this.Admitted(environment, pointee, at.Holder, storage, writable, below);
                return;
            }

            this.Past(environment, pointee, storage, targets, step != RawStep.Shared, below);
            if (step == RawStep.Exclusive && at.Holder is null)
            {
                this.Members(environment, OwnedOrigin(type)!, storage, below);
            }
        }
        else
        {
            this.Parts(environment, type, storage, writable, at);
        }
    }

    // What a `during self` layer of a stored part admits (SelfAdmitted, SPEC 5.7 Calls): its pointee and the holding instance's Type
    // arguments by their actual Types in the same storage; where the instance has an Origin argument, the write fact: any value at each
    // argument's node (a position of no Type), in the same storage and in the Abstract one, what such a value may refer to.
    private void Admitted(
        OriginPremiseEnvironment environment,
        BoundType pointee,
        BoundType? holder,
        int storage,
        bool writable,
        (BoundType? Written, BoundType? Holder, int Depth) at)
    {
        this.StorageWalk(environment, pointee, storage, writable, at);
        var any = SelfAdmitted(holder, out var arguments);
        foreach (var argument in arguments)
        {
            this.StorageWalk(environment, argument, storage, writable, at);
        }

        foreach (var argument in any && writable && storage >= 0 && storage != ResultStorage ? holder!.OriginArguments : [])
        {
            var node = this.PremiseNode(environment, argument);
            this.storagePositions.AddRange([(storage, node, null), (AbstractStorage, node, null)]);
            this.storageTargets.AddRange([(storage, node), (AbstractStorage, node)]);
        }
    }

    // What a raw layer of the contract whose Origin steps Exclusive lets the call write (RawContent's members: the referent of each input
    // whose outer Origin it is, the keepers of each slot it binds), each past the layer; an incomplete member walk is the Limit.
    private void Members(OriginPremiseEnvironment environment, BoundOrigin origin, int storage, (BoundType? Written, BoundType? Holder, int Depth) at)
    {
        var start = this.rawMembers.Count;
        this.retentionLimit |= !this.RawMembers(origin, CollectionsMarshal.AsSpan(this.retentionScope), this.rawMembers);
        var end = this.rawMembers.Count;
        for (var i = start; i < end; i++)
        {
            this.Past(environment, this.rawMembers[i].Referent, storage, targets: -1, written: true, at);
        }

        this.rawMembers.RemoveRange(start, end - start);
    }

    // What a layer refers to: the storage past it where the call may write through it (the root from before the root), and the result's
    // own past any layer (a result carries its whole Type); else, below a position (`targets`, its storage; -1 below none), only what a
    // value stored there brings.
    private void Past(
        OriginPremiseEnvironment environment,
        BoundType referent,
        int storage,
        int targets,
        bool written,
        (BoundType? Written, BoundType? Holder, int Depth) at)
    {
        if (written || storage == ResultStorage)
        {
            var past = storage switch
            {
                ResultStorage => ResultStorage,
                < 0 => OwnStorage,
                _ => this.StorageOf(referent),
            };
            this.StorageWalk(environment, referent, past, writable: true, at);
        }
        else if (targets >= 0)
        {
            this.Targets(environment, referent, targets);
        }
    }

    // A value's parts by value (StorageWalk; HeldParts): each with an address a position in the same storage, walked with the Type its
    // declaration writes there and the instance holding it; a nominal Type's Type arguments, which have none, bring only targets.
    private void Parts(OriginPremiseEnvironment environment, BoundType type, int storage, bool writable, (BoundType? Written, BoundType? Holder, int Depth) at)
    {
        this.retentionLimit |= !this.HeldParts(type, at.Written, at.Holder, out var run);
        for (var i = run.Start; i < run.End; i++)
        {
            if (this.retentionHeld[i] is { Addressed: true } part)
            {
                this.StorageWalk(environment, part.Type, storage, writable, (part.Written, part.Holder, at.Depth + 1));
            }
            else
            {
                this.Targets(environment, writable && storage >= 0 ? this.retentionHeld[i].Type : null, storage);
            }
        }

        this.retentionHeld.RemoveRange(run.Start, run.End - run.Start);
    }

    // A position: its Value tests, one per node naming the Type's own layer or its Origin arguments (an abstract part's carrier node, also
    // for the Abstract storage it hides), and its own Origins as targets.
    private void Position(OriginPremiseEnvironment environment, BoundType type, int storage)
    {
        foreach (var node in this.OwnNodes(environment, type))
        {
            this.storagePositions.Add((storage, node, type));
            this.storageTargets.Add((storage, node));
        }

        if (Abstract(type) && storage != ResultStorage)
        {
            this.AbstractPositions(environment, type, type);
        }

        this.Targets(environment, type, storage);
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

    // The nodes naming a Type's own value, which a Value test closes from (Position) or a source is named by (Sources): an abstract part's
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

    // The parts a value of a Type holds at its own address, the one enumeration of them (SPEC 15.6.3, 5.7) the storage walk (Parts) and
    // the sources and fits (OwnParts) read, appended to retentionHeld as a run (Start, End) its caller removes, each with the Type its
    // declaration writes there and the instance holding it (Held's reading): a Closure's environment bindings as its body sees them and
    // any other non-nominal Type's components (a Tuple's and a fixed array's at their inline positions); a struct's Fields, an enum's
    // payloads and the base in the instance's kept parts; a nominal Type's Type arguments, with an address only in a compiler-managed
    // collection, whose elements they are (it keeps no part: SPEC 4.5, 4.9). A layer of the value (a borrow, pair, raw or shared handle
    // layer) is its caller's. False where a struct's or enum's storage is past Binding's limits.
    private bool HeldParts(BoundType type, BoundType? written, BoundType? holder, out (int Start, int End) run)
    {
        var parts = this.retentionHeld;
        var start = parts.Count;
        var value = type.Kind == BoundTypeKind.Closure ? this.ClosureEnvironment(type) : type;
        var nominal = AdtDef.IsStruct(value) || AdtDef.IsEnum(value);
        if (nominal && !this.Readable(value, this.PrepareTypeStorage(value)))
        {
            run = (start, start);
            return false;
        }

        var instance = nominal ? this.Instance(value) : null;
        var addressed = instance is null or { Parts.Length: 0, PartsBase: null };
        for (var i = 0; i < value.Components.Count; i++)
        {
            parts.Add(new(value.Components[i], InlinePart(written, i), holder, addressed));
        }

        if (instance is { Parts: var stored, PartsBase: var parent } && value.Symbol?.Adt is { } adt)
        {
            parts.AddRange(parent is null ? [] : [new(parent, null, null, true)]);
            for (var i = 0; AdtDef.IsStruct(value) && i < adt.Fields.Length; i++)
            {
                parts.Add(new(stored[i], WrittenType(adt.Fields[i]), value, true));
            }

            for (var i = 0; AdtDef.IsEnum(value) && i < stored.Length; i++)
            {
                for (var j = 0; stored[i] is { } payloads && adt.Cases[i] is { } selected && j < selected.Payload.Length; j++)
                {
                    parts.Add(new(payloads.Components[j], WrittenType(selected.Payload[j]), value, true));
                }
            }
        }

        run = (start, parts.Count);
        return true;
    }

    // A part of a value (HeldParts): its Type (null: a part Binding did not bind), the Type its declaration writes there and the instance
    // holding it, and whether it has an address in the value's storage (a nominal Type's Type argument has none, but a collection's).
    private readonly record struct HeldPart(BoundType? Type, BoundType? Written, BoundType? Holder, bool Addressed);
}
