// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Call retention, per storage (SPEC 15.6.4 read as (iii) with (β′); DECISIONS R4-M154; contract in TyCtxt.Retention.cs).
// Responsibility: the one walk of what a value holds by value and reaches through its layers (StorageWalk; DECISIONS R4-M154, r2.3),
// which the output fold reads into the storages a call may write through an input and their positions (Position, which also start the
// hold walk, DECISIONS BU-52) and the input fold into an input's sources (Binding.Retention.Sources.cs, Source), over a value's entries
// (Holdings), which the fits read too; Binding.Retention.Rows.cs reads the storages into StorageRetentions.
// Inputs: the contract's instantiated inputs and result, its premise environment's nodes (Binding.Retention.cs), and the stored parts
// Binding's instances keep (Instance), with each raw layer's step by its position (RawStepAt: the Type a declaration writes there, the
// walked signature's declared Type there, DECISIONS BU-113).
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
    private readonly HashSet<(int Storage, bool Writable, bool Captured, SourceAt Source, BoundType Type, BoundType? Signature)> storageSeen = new();
    private readonly HashSet<(int Storage, BoundType Type)> storageTargeted = new();
    private readonly List<(int Storage, int Node, BoundType? Type)> storagePositions = new();
    private readonly List<(int Storage, int Node)> storageTargets = new();
    private readonly List<(BoundType Type, BoundType? Written, BoundType? Holder)> storageHolds = new();
    private readonly List<int> retentionOwnNodes = new();
    private readonly List<int> retentionOccurrences = new();
    private readonly HashSet<(BoundType Type, bool Callable)> retentionOccurred = new();
    private readonly List<Hold> retentionHeld = new(); // HeldRun's runs, a stack: each caller reads its run and removes it.
    private readonly List<BoundType> retentionReads = new(); // Reads' runs, a stack.

    // An abstract part a carrier stands for: a Type parameter that is no pair layer, or a pair's target (RetentionWalk's carriers).
    private static bool IsCarrier(BoundType type)
        => type.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection && !TryPairLayer(type, out _, out _);

    // An abstract part, a carrier or an associated projection, whose instance's storage the body does not see (SPEC 8.15.1): a position it
    // is holds what reaches it, and a referent it is holds any part by value.
    private static bool Abstract(BoundType type) => type.IsAbstract && !TryPairLayer(type, out _, out _);

    // The declared counterpart of a Type's component `index` (StorageAt.Signature, RetentionAtom.Signature): that component of the declared
    // Type where it has the same constructor; null below a Type parameter's image or any other declared Type.
    private static BoundType? DeclaredComponent(BoundType? declared, BoundType type, int index)
        => Same(declared, type) && (uint)index < (uint)declared!.Components.Count ? declared.Components[index] : null;

    // Whether a declared Type has the constructor of an instantiated one: its kind, Semantics, symbol and arity.
    private static bool Same(BoundType? declared, BoundType type)
        => declared is not null && declared.Kind == type.Kind && declared.Semantics == type.Semantics && ReferenceEquals(declared.Symbol, type.Symbol) &&
            declared.Components.Count == type.Components.Count;

    // Whether an entry is a borrow's referent (SPEC 15.6.3): a shared or exclusive dereference, a raw layer's only where its Origin's
    // members state what it admits; a pair's raw case states none.
    private static bool Dereference(in Hold entry)
        => entry.Step is HoldStep.Shared or HoldStep.Exclusive && (entry.Holds == HoldContent.Members || (entry.Flags & HoldFlags.Raw) == 0);

    // Whether an entry is raw storage a value holds: the content a `during self` layer addresses, or a raw layer's pointee where it states no
    // Origin, which holds a value of any Type in the fits (SPEC 5.7, 5.2: raw storage holds what is written there, a punned Place any
    // punnable Type over its bytes).
    private static bool RawStorage(in Hold entry) => (entry.Flags & HoldFlags.Raw) != 0 && entry.Step is HoldStep.Address or HoldStep.Own;

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
    private void RootWalk(OriginPremiseEnvironment environment, BoundType input, BoundType? declared)
    {
        if (this.BorrowReferent(input, out var outer, out var exclusive) is { } referent &&
            outer is { Kind: OriginKind.Projection, Slot: ClosureValueSlot, Binder: FunctionKoto receiver })
        {
            var (captures, bindings) = (receiver.ClosureDefinition!.Captures, this.ClosureEnvironment(referent));
            for (var i = 0; exclusive && i < bindings.Components.Count; i++)
            {
                var binding = StorageAt.Output(OwnStorage, captures[i].Environment.MutableCapture) with { Captured = true };
                this.StorageWalk(environment, bindings.Components[i], binding);
            }

            return;
        }

        this.StorageWalk(environment, input, StorageAt.Output(-1, writable: false) with { Signature = declared });
    }

    // The one walk (DECISIONS R4-M154, r2.3): a Type the walk visits, once per state it is in (StorageAt, the path's reduced prefix
    // included). Each visit emits to the fold its state names: the output fold its position where the call writes it (Position; an input's
    // also a hold, storageHolds), the input fold its sources (Source). Then what the Type's entries (Holdings) reach (Reach).
    private void StorageWalk(OriginPremiseEnvironment environment, BoundType? type, StorageAt at)
    {
        var source = at.Source.Input >= 0;
        var visit = (at.Storage, at.Writable, at.Captured, at.Source with { Above = -1 }, type!, at.Signature); // The layer above is no part of the key.
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

        var run = this.BodyRun(type, at.Written, at.Holder, out var complete);
        this.retentionLimit |= !complete && at.Storage != NoStorage;
        var layer = source ? this.Source(environment, type, at, run) : -1;
        this.Reach(environment, type, at, run, targets, layer);
        this.retentionHeld.RemoveRange(run.Start, run.End - run.Start);
    }

    // What a Type's entries reach, by the first: a pair layer's one reading, until U3b reads its cases (G416 D2); what a borrow, shared handle
    // or raw layer refers to (Past; a raw layer by its position's step, `during self` content as Admitted); its parts by value (Parts); an
    // abstract part or a callable, nothing; an unlisted form, the Limit.
    private void Reach(OriginPremiseEnvironment environment, BoundType type, StorageAt at, HeldRange run, int targets, int layer)
    {
        var below = at with { Written = null, Holder = null, Depth = at.Depth + 1, Signature = null };
        var declared = at.Signature;
        if (TryPairLayer(type, out _, out var target))
        {
            var written = declared is not null && TryPairLayer(declared, out _, out var declaredTarget) ? declaredTarget : null;
            this.Past(environment, target, targets, IsInvariantLayer(type, this), layer, below with { Signature = written });
            if (at.Source.Input >= 0 && this.Referent(run, out _) is null)
            {
                this.Parts(environment, type, at, run); // A pair layer admitting no borrow holds its target by value in its owning cases.
            }

            return;
        }

        if (run.Start == run.End)
        {
            return;
        }

        var first = this.retentionHeld[run.Start];
        if ((first.Flags & HoldFlags.Raw) != 0)
        {
            this.RawReach(environment, type, first.Part!, at, run, targets, layer);
        }
        else if (first.Step is HoldStep.Shared or HoldStep.Exclusive)
        {
            var written = declared is null ? null : this.BorrowReferent(declared, out _, out _);
            this.Past(environment, first.Part!, targets, first.Step == HoldStep.Exclusive, layer, below with { Signature = written });
        }
        else if ((first.Flags & HoldFlags.ReadOnly) != 0)
        {
            this.Past(environment, first.Part!, targets, written: false, layer: -1, below with { Signature = DeclaredComponent(declared, type, 0) });
        }
        else if (first.Step == HoldStep.Unsupported)
        {
            this.retentionLimit = true;
        }
        else if (first.Step is not (HoldStep.Abstract or HoldStep.Erased))
        {
            this.Parts(environment, type, at, run);
        }
    }

    // A raw layer. An input fold's walk names no write, so any step's layer is gone through. An output fold's steps by its position (RawStepAt,
    // as RetentionParts reads it; SPEC 5.2.1 condition 3, 5.7; DECISIONS BU-113), the cap of a contract's walk, which stays outside the fact.
    private void RawReach(OriginPremiseEnvironment environment, BoundType type, BoundType pointee, StorageAt at, HeldRange run, int targets, int layer)
    {
        var below = at with { Written = null, Holder = null, Depth = at.Depth + 1, Signature = null };
        if (at.Storage == NoStorage)
        {
            this.Through(environment, pointee, layer, below);
            return;
        }

        var step = this.RawStepAt(type, at.Written, at.Holder, at.Signature);
        below = below with { Signature = DeclaredComponent(at.Signature, type, 0) };
        if (step == HoldStep.Own)
        {
            this.Admitted(environment, run, at.Holder, below);
            return;
        }

        this.Past(environment, pointee, targets, step != HoldStep.Shared, layer, below);
        if (step == HoldStep.Exclusive && at.Holder is null)
        {
            this.Members(environment, OwnedOrigin(type)!, below);
        }
    }

    // What a `during self` layer of a stored part admits (row 13's entries, SPEC 5.7 Calls), for the output fold: its pointee and each Type it
    // states in the same storage; an AnyOf entry, the write fact: any value at each Origin argument's node (a position of no Type), in the
    // same storage and in the Abstract one, what such a value may refer to. The input fold reads that content among the holder's parts by
    // value (SelfContent) and goes through the layer to its pointee.
    private void Admitted(OriginPremiseEnvironment environment, HeldRange run, BoundType? holder, StorageAt at)
    {
        for (var i = run.Start; i < run.End; i++)
        {
            if (this.retentionHeld[i].Holds != HoldContent.AnyOf)
            {
                this.StorageWalk(environment, this.retentionHeld[i].Part, i == run.Start ? at : at with { Signature = null });
                continue;
            }

            foreach (var argument in at.Writable && at.Storage >= 0 && at.Storage != ResultStorage ? holder!.OriginArguments : [])
            {
                var node = this.PremiseNode(environment, argument);
                this.storagePositions.AddRange([(at.Storage, node, null), (AbstractStorage, node, null)]);
                this.storageTargets.AddRange([(at.Storage, node), (AbstractStorage, node)]);
            }
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

    // A value's parts by value (StorageWalk; `run`, its entries), walked in the same state with the Type each part's declaration writes
    // there and the instance holding it; a Type argument, which has no address (a Loan entry), brings the output fold only targets.
    // For the input fold, after them, the content of each `during self` Field or payload (SelfContent).
    private void Parts(OriginPremiseEnvironment environment, BoundType type, StorageAt at, HeldRange run)
    {
        var captured = at.Captured || type.Kind == BoundTypeKind.Closure; // A Closure's parts are its environment's bindings.
        var declared = new HeldRange(run.End, run.End);
        if (!captured && Same(at.Signature, type))
        {
            declared = this.HeldRun(at.Signature!, null, null, out var complete);
            if (!complete)
            {
                this.retentionHeld.RemoveRange(declared.Start, declared.End - declared.Start);
                declared = new(run.End, run.End);
            }
        }

        var paired = declared.End - declared.Start == run.End - run.Start; // The declared Type's parts, in the same order.
        for (var i = run.Start; i < run.End; i++)
        {
            var part = this.retentionHeld[i];
            if (this.ByValue(type, part))
            {
                var signature = paired ? this.retentionHeld[declared.Start + i - run.Start].Part : null;
                var holder = type.Kind == BoundTypeKind.Closure ? at.Holder : part.Holder; // A binding lies in the storage holding the Closure.
                var below = at with { Written = part.Written, Holder = holder, Depth = at.Depth + 1, Captured = captured, Signature = signature };
                this.StorageWalk(environment, part.Part, below);
            }
            else if (part.Step == HoldStep.Loan)
            {
                this.Targets(environment, at.Writable && at.Storage >= 0 ? part.Part : null, at.Storage);
            }
        }

        this.retentionHeld.RemoveRange(declared.Start, declared.End - declared.Start);
        for (var i = run.Start; at.Storage == NoStorage && i < run.End; i++)
        {
            if (this.retentionHeld[i] is { Part: { } stored, Holder: { } owner } part && ReferenceEquals(owner, type) && ReferenceTypes.IsPointer(part.Written))
            {
                this.SelfContent(environment, stored, part.Written, owner, at with { Written = null, Holder = null, Depth = at.Depth + 1, Captured = captured });
            }
        }
    }

    // A `during self` raw layer's content (row 13's entries), for the input fold: each Type it states, in the state of the holder's parts.
    private void SelfContent(OriginPremiseEnvironment environment, BoundType layer, BoundType? written, BoundType holder, StorageAt at)
    {
        var run = this.HeldRun(layer, written, holder, out _);
        for (var i = run.Start; i < run.End && this.retentionHeld[run.Start] is { Step: HoldStep.Own, Flags: HoldFlags.Raw }; i++)
        {
            this.StorageWalk(environment, this.retentionHeld[i].Part, at);
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
    // where none does; any other Type as itself. OwnedOrigin leaves out `self` (Holder, SPEC 5.7): it names the holding value, no input
    // of the contract, and what a `during self` layer admits is its entries', so such a layer reads as itself. An incomplete
    // member walk is the Limit.
    private (int Start, int End) Reads(BoundType type)
    {
        var start = this.retentionReads.Count;
        var stated = type.Semantics == SemanticsKind.Raw && OwnedOrigin(type) is { Kind: not (OriginKind.Static or OriginKind.Unstated) };
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

        if ((nodes.Count == 0 && !this.Refers(type)) || Abstract(type))
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

        if (!this.Named(environment, type, nodes, ref callable) || this.Refers(type!))
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

    // Whether a value of a Type only refers to what it reaches, so its own value has no parts: every entry a dereference (a pair layer
    // admitting only borrows, a raw layer stating an Origin other than `self`, a Slice or a UniqSlice) or static storage.
    private bool Refers(BoundType type)
    {
        var run = this.HeldRun(type, null, null, out _);
        var refers = run.End > run.Start;
        for (var i = run.Start; refers && i < run.End; i++)
        {
            refers = this.retentionHeld[i].Step == HoldStep.Static || Dereference(this.retentionHeld[i]);
        }

        this.retentionHeld.RemoveRange(run.Start, run.End - run.Start);
        return refers;
    }

    // What a run's layer refers to, through which a borrow of it is formed (SPEC 15.6.3; a raw layer stating an Origin retains as the borrow
    // that Origin belongs to would, SPEC 5.7), and whether it is exclusive (invariant in its referent, SPEC 15.3.5: a pair layer by any
    // admitted case); null for any other.
    private BoundType? Referent(HeldRange run, out bool exclusive)
    {
        BoundType? referent = null;
        var invariant = false;
        for (var i = run.Start; i < run.End; i++)
        {
            var entry = this.retentionHeld[i];
            referent ??= Dereference(entry) ? entry.Part : null;
            invariant |= entry.Step == HoldStep.Exclusive || (entry.Flags & HoldFlags.Raw) != 0 || entry is { Step: HoldStep.Own, Flags: HoldFlags.None };
        }

        exclusive = referent is not null && invariant;
        return referent;
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

    // A Type's entries (Holdings) as a run on retentionHeld, a stack: its caller reads it and removes it. At Binding's Limit, none, and false.
    private HeldRange HeldRun(BoundType type, BoundType? written, BoundType? holder, out bool complete)
    {
        var start = this.retentionHeld.Count;
        complete = this.Holdings(type, written, holder, this.retentionHeld);
        this.retentionHeld.RemoveRange(start, complete ? 0 : this.retentionHeld.Count - start);
        return new(start, this.retentionHeld.Count);
    }

    // HeldRun of a value a contract reads, a Closure's as its body sees it (ClosureEnvironment).
    private HeldRange BodyRun(BoundType type, BoundType? written, BoundType? holder, out bool complete)
        => this.HeldRun(type.Kind == BoundTypeKind.Closure ? this.ClosureEnvironment(type) : type, written, holder, out complete);

    // Whether a value of a Type has an entry at its own address or past an own step: no dereference, raw step, or erased or abstract part.
    // A Type argument has no address (SPEC 15.3.5), but a Loan<T>'s until U3a (G416 D4; row 10).
    private bool ByValue(BoundType type, in Hold entry) => entry.Step switch
    {
        HoldStep.Loan => type.Symbol?.LibraryDeclaration == KimiDeclarationId.Loan,
        HoldStep.Own => (entry.Flags & (HoldFlags.Raw | HoldFlags.ReadOnly)) == 0,
        HoldStep.Inline => true,
        _ => false,
    };

    // A run of retentionHeld (HeldRun).
    private record struct HeldRange(int Start, int End);

    // The walk's state at a Type (StorageWalk; DECISIONS R4-M154, r2.3). For the output fold: the storage (-1 before the root, NoStorage
    // in an input fold's walk) and whether the call writes there; the Type the stored part's declaration writes there and the instance
    // holding it (a raw layer's step, as Held reads it); the depth; whether it lies in a Closure's environment (Captured, as
    // RetentionPlace.Captured); the walked signature's declared Type at the position (Signature, DECISIONS BU-113: null inside a Type
    // parameter's image, in a Closure's environment and below a part whose declared counterpart differs). For the input fold, its own
    // state (Source).
    private readonly record struct StorageAt(
        int Storage, bool Writable, BoundType? Written, BoundType? Holder, int Depth, SourceAt Source, bool Captured = false, BoundType? Signature = null)
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
