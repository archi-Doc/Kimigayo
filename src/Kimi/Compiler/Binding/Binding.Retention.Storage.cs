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
// node (Content targets), and for an input's walk each position with the Type its declaration writes there, the instance holding
// it and its storage (storageHolds: where rule A's hold walk starts, in that storage, so a by-value input's Fields, payloads and
// elements reach it as a borrowed input's referent does) and each abstract part whose hidden storage it writes at no position
// (storageHidden: rule 3's fill alone).
// Invariants: a position is storage the call may write: a part a value of the storage holds by value (Tuple and fixed-array elements, a
// struct's Fields and base, an enum's payloads, a Closure's environment bindings, a raw layer's pointee by its own step and what a
// `during self` layer admits, Admitted), the layer of a borrow, pair or raw layer, an abstract part and a callable. What a part hides
// (an abstract part's storage; what a value a `during self` layer's AnyOf content holds may refer to) is the Abstract storage, written
// wherever an input's walk meets the part (StorageAt.HiddenWritable), whether or not the part is a position (PLAN G963). Past an
// exclusive borrow, pair layer admitting one, UniqSlice or raw layer the call may write lies another storage; past a shared one, what a
// stored value brings (Content targets) but no position. Each state emits once and is walked
// from the shallowest depth it is met at (Enters), so the walk names every storage past the root within the bound, whatever route meets
// a state first; a Type whose storage is past Binding's limits (a growing recursive Type) is the Limit. A state is StorageAt less the
// layer above (BU-108): that layer decides only whether the next one joins a path (Related), and a layer left out is one the premise
// catalog relates to the layer above, which no fit needs (Revision 10), so meetings that differ only there give the same fits.
// Must not: judge an Origin relation (the premise closure's), decide which source a position can hold (the fits'), or read the call's
// arguments or solved Origins.
// The walk's by-value criterion (ByValue) also serves the key walk (OwnParts, Binding.Retention.PlaceKeys.cs); the walked signature's
// declared counterparts are paired by the one rule in Binding.Retention.Declared.cs.

public sealed partial class Binding
{
    // The storage walk's root storage, the Abstract one (what an abstract part hides), the result's (every position of its Type, one
    // storage), and Binding's depth bound of the source and part walks, which counts entries: a Dictionary's level is two, (K, V) and then K or
    // V. A storage nest past 32 entries is past it, however few Dictionaries it holds: the Front's named limit (the owner's O-47, O-49; PLAN G408's class).
    private const int OwnStorage = 0;
    private const int AbstractStorage = 1;
    private const int ResultStorage = 2;
    private const int StorageBound = 32;
    private const int NoStorage = -2; // An input fold's walk (RetentionSources), which names no position.

    // The storages of the walk being made (by index: Own, Abstract, the result's, then one per contract Type past an exclusive layer)
    // with each one's contract Type, its index by that Type, the states walked (each with the shallowest depth it was met at and the
    // node its sources go through, Source), the (storage, Type) pairs targeted, the positions (Value tests: a position's node and Type,
    // null for any value), the Content targets (every Origin occurrence's node), an input's holds and its hidden parts; reused.
    private readonly List<BoundType?> storageTypes = new();
    private readonly Dictionary<BoundType, int> storageOf = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(int Storage, bool Writable, bool Captured, bool Routed, bool Dropped, SourceAt Source, BoundType Type, BoundType? Signature), (int Depth, int Layer)> storageSeen = new();
    private readonly HashSet<(int Storage, BoundType Type, BoundType? Signature)> storageTargeted = new();
    private readonly List<(int Storage, int Node, BoundType? Type, BoundType? Signature)> storagePositions = new();
    private readonly HashSet<int> storageBorrowed = new(); // The storages the walk entered past a borrow or pair layer (R's exclusion, Storages).
    private readonly HashSet<BoundType> storageResultLeaves = new(ReferenceEqualityComparer.Instance); // ResultLeaves' answer, per computation.
    private readonly List<(int Storage, int Node, BoundType? Signature)> storageTargets = new(); // With the walked signature there (Admits).
    private readonly List<(int Storage, BoundType Signature, BoundType Bound)> storageBound = new(); // The written roots of the kept abstract Type's bindings, by storage, with the Type the call binds it to there (HiddenKinds).
    private readonly HashSet<BoundType> storageKept = new(ReferenceEqualityComparer.Instance); // The kept abstract Types the output's walk met (Written's Handed).
    private readonly Dictionary<BoundType, int> storageResultSeen = new(ReferenceEqualityComparer.Instance); // ResultLeaves' walk: Enters' depths.
    private readonly List<(HoldAt At, int Storage)> storageHolds = new();
    private readonly List<BoundType> storageHidden = new();
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

    // Whether an entry is a borrow's referent (SPEC 15.6.3): a shared or exclusive dereference, a raw layer's only where its Origin's
    // members state what it admits; a pair's raw case states none.
    private static bool Dereference(in Hold entry)
        => entry.Step is HoldStep.Shared or HoldStep.Exclusive && (entry.Holds == HoldContent.Members || (entry.Flags & HoldFlags.Raw) == 0);

    // Whether an entry is raw storage a value holds: the content a `during self` layer addresses, or a raw layer's pointee where it states no
    // Origin, which holds a value of any Type in the fits (SPEC 5.7, 5.2: raw storage holds what is written there, a punned Place any
    // punnable Type over its bytes).
    private static bool RawStorage(in Hold entry) => (entry.Flags & HoldFlags.Raw) != 0 && entry.Step is HoldStep.Address or HoldStep.Own;

    // SPEC 15.6.4 ("A destruction retains as a call of each drop it runs"; PLAN G964, F's ruling 7): whether a struct declares a `drop`,
    // the declaring code its destruction runs, which Parts reads per Type of the kept binding (Dropped).
    private static bool DeclaresDrop(BoundType type) => AdtDef.Destructor(type) is not null;

    // Begins a storage walk: no storage but the three fixed ones.
    private void BeginStorages()
    {
        this.storageTypes.Clear();
        this.storageTypes.AddRange([null, null, null]);
        this.storageOf.Clear();
        this.storageSeen.Clear();
        this.storageTargeted.Clear();
        this.storagePositions.Clear();
        this.storageBorrowed.Clear();
        this.storageTargets.Clear();
        this.storageBound.Clear();
        this.storageKept.Clear();
        this.storageHolds.Clear();
        this.storageHidden.Clear();
    }

    // The storage walk from input j's value (DECISIONS R4-M154): the value itself is the callee's, not storage; what its first exclusive
    // layers refer to is its root (Own), a borrowed input's referent among them. A Closure's call receiver's referent holds its environment
    // bindings as Fields where the receiver is exclusive (past a shared one the call writes nothing, SPEC 5.2.1): a `var` binding is a
    // position, a `let` binding never, and what an exclusive layer in either refers to lies past it (SPEC 15.8.2, DECISIONS R4-M148); the
    // receiver by the one test (ReceiverClosure), each binding by its component index.
    private void RootWalk(OriginPremiseEnvironment environment, BoundType input, BoundType? declared)
    {
        if (this.BorrowReferent(input, out var outer, out var exclusive) is { } referent && ReceiverClosure(outer, referent) is { } receiver)
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
    // also a hold, storageHolds) and what an abstract part hides (AbstractPositions; at no position the part, storageHidden), the input
    // fold its sources (Source), each once, at the state's first meeting. Then what the Type's
    // entries (Holdings) reach (Reach). An input fold's walk stops at the depth bound, so it enters a state by the bounded walks' rule
    // (Enters: again where it is met shallower; PLAN G571), reusing the node the first meeting's sources go through; an output's walk has
    // no bound, so its state counts depth 0 and is entered once.
    private void StorageWalk(OriginPremiseEnvironment environment, BoundType? type, StorageAt at)
    {
        var source = at.Source.Input >= 0;
        if (type is null || (source && at.Depth > StorageBound))
        {
            return;
        }

        var visit = (at.Storage, at.Writable, at.Captured, at.Routed, at.Dropped, at.Source with { Above = -1 }, type, at.Signature); // The layer above is no part of the key.
        ref var seen = ref CollectionsMarshal.GetValueRefOrAddDefault(this.storageSeen, visit, out var met);
        if (!Enters(ref seen.Depth, met, source ? at.Depth : 0))
        {
            return;
        }

        var layer = met ? seen.Layer : -1; // `seen` is read no more: the walk below inserts.
        var targets = at.Writable && at.Storage >= 0 ? at.Storage : -1; // Only in an output's walk, whose state is entered once.
        var (named, bound) = (false, this.Parametric(at.Signature));
        if (targets >= 0)
        {
            named = this.Position(environment, type, at.Storage, at.Written, at.Signature);
            if (at.Storage != ResultStorage && !bound)
            {
                this.storageHolds.Add((new(type, at.Written, at.Holder, at.Signature, at.Routed), at.Storage)); // A hidden binding's layers add no reach (SPEC 15.6.4).
            }

            // The root of a binding of the callee's kept Type in this storage; in the result's, where the result holds it by value or a
            // node names it (ResultLeaves: past its own borrow or raw layer the result lends a referent or pointee, K2).
            if (bound && !at.Bound && (named || at.Storage != ResultStorage || this.storageResultSeen.ContainsKey(type)))
            {
                this.storageBound.Add((at.Storage, at.Signature!, type));
            }
        }

        // What an abstract part hides is storage the call may write wherever an input's walk meets the part (HiddenWritable), whether the
        // part is a position or the callee's own value (a by-value input's part): its binding may hold an exclusive reference with Origins
        // of its own (SPEC 15.6.4 route 2; PLAN G963, DECISIONS BU-225). So the Abstract storage's positions; at a position the hold above
        // fills it with what a value stored there brings, elsewhere the part takes rule 3's fill alone (storageHidden): its own value is
        // not written.
        var hides = at.HiddenWritable && Abstract(type);
        if (hides)
        {
            this.AbstractPositions(environment, type, type);
        }

        if (hides && targets < 0)
        {
            this.storageHidden.Add(type);
        }

        if (bound && !at.Bound)
        {
            this.storageKept.Add(at.Signature!); // Entered at any position, written or not (a by-value input's part included).
        }

        at = at with { Bound = bound };

        var run = this.BodyRun(type, at.Written, at.Holder, out var complete);
        this.retentionLimit |= !complete && at.Storage != NoStorage;
        if (targets >= 0 && !named && this.Leaf(type, run) && (at.Storage != ResultStorage || this.storageResultLeaves.Contains(type)))
        {
            this.storagePositions.Add((at.Storage, -1, type, at.Signature)); // A part that names no Origin (PLAN G935 R1.2).
        }

        if (source && !met)
        {
            layer = this.Source(environment, type, at, run);
            this.storageSeen[visit] = (at.Depth, layer);
        }

        this.Reach(environment, type, at, run, targets, layer);
        this.retentionHeld.RemoveRange(run.Start, run.End - run.Start);
    }

    // What a Type's entries reach, by the first: a pair layer's one reading, until U3b reads its cases (G416 D2); what a borrow, shared handle
    // or raw layer refers to (Past; a raw layer by its position's step, `during self` content as Admitted); its parts by value (Parts); an
    // abstract part or a callable, nothing.
    private void Reach(OriginPremiseEnvironment environment, BoundType type, StorageAt at, HeldRange run, int targets, int layer)
    {
        var below = at with { Written = null, Holder = null, Depth = at.Depth + 1, Signature = null };
        if (TryPairLayer(type, out _, out var target))
        {
            var written = this.SignaturePast(at.Signature, type, default);
            this.Borrowed(this.Past(environment, target, targets, IsInvariantLayer(type, this, null, default), layer, below with { Signature = written }));
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
            this.RawReach(environment, type, at, run, targets, layer);
        }
        else if (first.Step is HoldStep.Shared or HoldStep.Exclusive)
        {
            var written = this.SignaturePast(at.Signature, type, first);
            this.Borrowed(this.Past(environment, first.Part!, targets, first.Step == HoldStep.Exclusive, layer, below with { Signature = written }));
        }
        else if ((first.Flags & HoldFlags.ReadOnly) != 0)
        {
            this.Past(environment, first.Part!, targets, written: false, layer: -1, below with { Signature = this.SignaturePast(at.Signature, type, first) });
        }
        else if (first.Step is not (HoldStep.Abstract or HoldStep.Erased))
        {
            this.Parts(environment, type, at, run);
        }
    }

    // A raw layer. An input fold's walk names no write, so any step's layer is gone through. An output fold's steps by its position (RawStepAt,
    // as RetentionParts reads it; SPEC 5.2.1 condition 3, 5.7; DECISIONS BU-113), the cap of a contract's walk, which stays outside the fact;
    // one stepping Exclusive also writes its Origin's members wherever the input holds it (SPEC 15.6.4 "a Tuple element, an Option payload,
    // a Field or a Type argument included"; PLAN G583): its holding instance decides its step, never its members, and the walk meets the
    // layers an input holds at its own address by ByValue, as OwnLayers lists them. A layer whose content no Type states (Holds Any: one a
    // declaration wrote, TyCtxt.RawStated) addresses storage its pointee Type does not describe (SPEC 5.7 Calls, 5.2; DECISIONS BU-46), so
    // the storage it writes past it gets a hold of no Type, which the hold walk reads as a part no Type states (Held: Loans and Any, rule A's
    // rows whole; Hold's Untyped forms them whatever the output's written layers, PLAN G941; the hold names the layer's Signature, in the
    // binding of the callee's kept Type that Type, where UntypedRows keeps only the inputs a handed callable writes there). So does a
    // layer stating no Origin in a Closure's environment (`Captured`, at any depth of a binding's Type: SPEC 5.7 Calls, "one the Closure
    // states in its own Fields, as a declaration does"; C-352's C10), which the walk's state, not the binding's Type, knows: the
    // environment's Types are the MIR locals' (DECISIONS BU-50), and its layers stating an Origin keep their binder's step (RawStepAt,
    // BU-113). The hold is formed only where the call's writes and reads compose: an operation whose writes SPEC states (StatesWrites,
    // the walk's callee) stores nothing past such a layer (SPEC 15.6.4's exception, 5.2.1).
    private void RawReach(OriginPremiseEnvironment environment, BoundType type, StorageAt at, HeldRange run, int targets, int layer)
    {
        var (pointee, content) = (this.retentionHeld[run.Start].Part!, this.retentionHeld[run.Start].Holds);
        var signature = this.SignaturePast(at.Signature, type, this.retentionHeld[run.Start]);
        var below = at with { Written = null, Holder = null, Depth = at.Depth + 1, Signature = signature };
        if (at.Storage == NoStorage)
        {
            this.Through(environment, pointee, layer, below);
            return;
        }

        var step = this.RawStepAt(type, at.Written, at.Holder, at.Signature);
        if (step == HoldStep.Own)
        {
            this.Admitted(environment, run, below);
            return;
        }

        // The holder a kept binding's raw layer addresses is written by route 1 as this layer states it; past that holder the walk is
        // Routed unless the declaring code acts there (Routes), so no layer met there makes storage of no Type (PLAN G964).
        below = below with { Routed = at.Routed || this.Routes(at, this.retentionHeld[run.Start]) };
        var past = this.Past(environment, pointee, targets, step != HoldStep.Shared, layer, below);
        if (!at.Routed && (content == HoldContent.Any || (at.Captured && step == HoldStep.Address)) && past >= 0 && past != ResultStorage &&
            !this.StatesWrites(this.retentionCallee))
        {
            this.storageHolds.Add((new(null, null, null, at.Signature), past)); // Storage no Type states: a declaration's layer's, or a Closure's (C-352's C10).
        }

        if (step == HoldStep.Exclusive)
        {
            this.Members(environment, OwnedOrigin(type)!, at.Written, at.Signature, below);
        }
    }

    // What a `during self` layer of a stored part admits (row 13's entries, SPEC 5.7 Calls), for the output fold: its pointee and each Type it
    // states in the same storage; an AnyOf entry, the write fact: any value at each Origin argument's node (a position of no Type), in the
    // Abstract storage, what such a value may refer to, wherever the walk meets the layer (HiddenWritable), and in the same storage where
    // the walk writes it (a by-value holder's own content is the callee's; PLAN G963). The input fold reads that content among the holder's parts by
    // value (SelfContent) and goes through the layer to its pointee.
    private void Admitted(OriginPremiseEnvironment environment, HeldRange run, StorageAt at)
    {
        for (var i = run.Start; i < run.End; i++)
        {
            if (this.retentionHeld[i].Holds != HoldContent.AnyOf)
            {
                this.StorageWalk(environment, this.retentionHeld[i].Part, i == run.Start ? at : at with { Signature = Below(at.Signature, null) });
                continue;
            }

            var arguments = at.HiddenWritable ? AnyOfOrigins(this.retentionHeld[i].Holder!) : [];
            for (var j = 0; j < arguments.Count; j++)
            {
                var node = this.PremiseNode(environment, arguments[j]);
                this.storagePositions.Add((AbstractStorage, node, null, null));
                this.storageTargets.Add((AbstractStorage, node, null));
                if (at.Writable && at.Storage >= 0)
                {
                    this.storagePositions.Add((at.Storage, node, null, null)); // The holder's own content: the callee's own where it is (PLAN G963).
                    this.storageTargets.Add((at.Storage, node, at.Signature));
                }
            }
        }
    }

    // What a raw layer of the contract whose Origin steps Exclusive lets the call write, at any holder (RawContent's members, at the
    // layer's position, ContractMembers: over the contract scope, the referent of each input whose outer Origin it is and the keepers of
    // each slot it binds, a holding instance's included; or the Origin's own where it is bound), each past the layer, for the output fold;
    // unanswered members are the Limit.
    private void Members(OriginPremiseEnvironment environment, BoundOrigin origin, BoundType? written, BoundType? signature, StorageAt at)
    {
        if (at.Storage == NoStorage)
        {
            return;
        }

        var start = this.rawMembers.Count;
        this.ContractMembers(origin, written, signature, CollectionsMarshal.AsSpan(this.retentionScope));
        var end = this.rawMembers.Count;
        for (var i = start; i < end; i++)
        {
            this.Past(environment, this.rawMembers[i].Referent, targets: -1, written: true, layer: -1, at);
        }

        this.rawMembers.RemoveRange(start, end - start);
    }

    // Retention's one read of the members of a stated raw layer, at its step's position (Z6's R5.1, R5.2, R5.5), appended to rawMembers:
    // within `scope`, the walked contract's, where RawStepAt judges it there (a layer the declaration or the walked signature writes),
    // else by its Origin's binder (the one BinderReading BinderStep made at the layer, by the same key), whose referents are in the
    // caller's terms, so their Origins join the call's instantiated ones (R4-M154 Revision 2's set); unanswered members are the Limit.
    private void ContractMembers(BoundOrigin origin, BoundType? written, BoundType? signature, ReadOnlySpan<BoundType> scope)
    {
        if (!this.BoundElsewhere(written, signature))
        {
            this.retentionLimit |= !this.RawMembers(origin, scope, this.rawMembers);
            return;
        }

        var reading = this.BinderReading(SlotArgument(origin)!);
        this.retentionLimit |= !reading.Complete;
        for (var i = reading.Start; i < reading.Start + reading.Count; i++)
        {
            this.rawMembers.Add(this.binderReadingMembers[i]);
            this.Instantiation(this.binderReadingMembers[i].Referent);
        }
    }

    // What a layer refers to. For the output fold: the storage past it where the call may write through it (the root from before the
    // root), and the result's own past any layer (a result carries its whole Type); else, below a position (`targets`, its storage; -1
    // below none), only what a value stored there brings. For the input fold: what a source goes through the layer to (Through, `layer`
    // its node, -1 for none). The storage it walks past the layer, -1 for none.
    private int Past(OriginPremiseEnvironment environment, BoundType referent, int targets, bool written, int layer, StorageAt at)
    {
        var past = -1;
        if (at.Storage != NoStorage && (written || at.Storage == ResultStorage))
        {
            past = at.Storage switch
            {
                ResultStorage => ResultStorage,
                < 0 => OwnStorage,
                _ => this.StorageOf(this.Parametric(at.Signature) ? at.Signature! : referent), // The callee's kept T: its own entry (PLAN G935 R1.2).
            };
            this.StorageWalk(environment, referent, at with { Storage = past, Writable = true });
        }
        else if (targets >= 0)
        {
            this.Targets(environment, referent, targets, at.Signature);
        }

        this.Through(environment, referent, layer, at);
        return past;
    }

    // A value's parts by value (StorageWalk; `run`, its entries), walked in the same state with the Type each part's declaration writes
    // there and the instance holding it; a Type argument, which has no address (a Loan entry), brings the output fold only targets.
    // For the input fold, after them, the content of each `during self` Field or payload (SelfContent).
    private void Parts(OriginPremiseEnvironment environment, BoundType type, StorageAt at, HeldRange run)
    {
        var captured = at.Captured || type.Kind == BoundTypeKind.Closure; // A Closure's parts are its environment's bindings.
        var dropped = at.Dropped || (Hides(at.Signature) && DeclaresDrop(type)); // The kept binding's own `drop` (Routes).
        var declared = this.DeclaredRun(at.Captured ? null : at.Signature, type, run);
        for (var i = run.Start; i < run.End; i++)
        {
            var part = this.retentionHeld[i];
            if (this.ByValue(type, part))
            {
                var signature = this.DeclaredPart(at.Signature, declared, run, i);
                var holder = type.Kind == BoundTypeKind.Closure ? at.Holder : part.Holder; // A binding lies in the storage holding the Closure.
                var below = at with { Written = part.Written, Holder = holder, Depth = at.Depth + 1, Captured = captured, Signature = signature, Dropped = dropped };
                if (at.Storage > ResultStorage && this.Parametric(signature) && !this.Parametric(at.Signature))
                {
                    below = below with { Storage = this.StorageOf(signature!) }; // The T part of a mixed storage: its own entry (PLAN G935 R1.2).
                }

                this.StorageWalk(environment, part.Part, below);
            }
            else if (part.Step == HoldStep.Loan)
            {
                this.Targets(environment, at.Writable && at.Storage >= 0 ? part.Part : null, at.Storage, at.Signature);
            }
        }

        this.retentionHeld.RemoveRange(declared.Start, declared.End - declared.Start);
        for (var i = run.Start; at.Storage == NoStorage && i < run.End; i++)
        {
            if (this.retentionHeld[i] is { Part: { } stored, Holder: { } owner } part && ReferenceEquals(owner, type) && part.Written is { IsPointer: true })
            {
                var below = at with { Written = null, Holder = null, Depth = at.Depth + 1, Captured = captured, Signature = null };
                this.SelfContent(environment, stored, part.Written, owner, below);
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

    // A position: its Value tests, one per node naming the Type's own layer or its Origin arguments (an abstract part's carrier node) and
    // per Type its fit reads (Reads), each with the walked signature's declared Type there, and its own Origins as targets; whether a node
    // names it. The storage an abstract part hides is the visit's (StorageWalk). A position in the binding of the callee's kept abstract
    // Type (Hides) is tested in the callee's terms (Binding.Retention.Rows.cs, Storages), so it has no target: the binding's Origins are
    // the caller's.
    private bool Position(OriginPremiseEnvironment environment, BoundType type, int storage, BoundType? written, BoundType? signature)
    {
        var (start, end) = this.Reads(type, written, signature);
        var named = false;
        foreach (var node in this.OwnNodes(environment, type))
        {
            for (var i = start; i < end; i++)
            {
                this.storagePositions.Add((storage, node, this.retentionReads[i], signature));
            }

            named = true;
            this.storageTargets.Add((storage, node, signature));
        }

        this.retentionReads.RemoveRange(start, end - start);
        this.Targets(environment, type, storage, signature);
        return named;
    }

    // Whether a Type holds no part by value (ByValue), so a position of it names no Origin of a part below (PLAN G935 R1.2: an aggregate
    // held by value is its parts' positions).
    private bool Leaf(BoundType type, HeldRange run)
    {
        for (var i = run.Start; i < run.End; i++)
        {
            if (this.ByValue(type, this.retentionHeld[i]))
            {
                return false;
            }
        }

        return true;
    }

    // Whether a position lies in the binding of the called function's own abstract Type that the call reads in the callee's terms (PLAN
    // G935; the owner's O-353 (a), O-360 (a)): a kept abstract Type (Hides, BU-202) of a callee whose rows its declared signature gives,
    // every callee but a published transfer operation, whose rows SPEC states and the identity-keyed table holds (TransferKey; 15.6.4, 15.7,
    // 4.9.6): a user-declared twin of `exchange` follows the signature (ex-rows.txt).
    private bool Parametric(BoundType? signature)
        => this.retentionParametric && OwnAbstract(signature) is { } function && ReferenceEquals(function, this.retentionCallee);

    // The Types the result holds by value that hold no part by value (R's node-less positions in the result; PLAN G935, K2): the result
    // holds an input's own value only by value (SPEC 15.6.4), never past its own borrow or raw layer, whose referent or pointee it lends.
    private void ResultLeaves(BoundType? type, int depth)
    {
        if (type is null || depth > StorageBound)
        {
            return;
        }

        ref var shallowest = ref CollectionsMarshal.GetValueRefOrAddDefault(this.storageResultSeen, type, out var met);
        if (!Enters(ref shallowest, met, depth))
        {
            return; // Entered at this depth or shallower: a Type's leaves depend on the Type alone (the bounded walks' rule, PLAN G571).
        }

        var run = this.BodyRun(type, null, null, out _);
        var leaf = true;
        for (var i = run.Start; i < run.End; i++)
        {
            if (this.ByValue(type, this.retentionHeld[i]))
            {
                leaf = false;
                this.ResultLeaves(this.retentionHeld[i].Part, depth + 1);
            }
        }

        this.retentionHeld.RemoveRange(run.Start, run.End - run.Start);
        if (leaf)
        {
            this.storageResultLeaves.Add(type);
        }
    }

    // Marks a storage entered past a borrow or pair layer (Past's answer, -1: none): R's exclusion (SPEC 15.6.4: storage written through a
    // raw layer an input holds never retains that input's own value) holds only for storage entered past raw layers alone.
    private void Borrowed(int past)
    {
        if (past >= 0)
        {
            this.storageBorrowed.Add(past);
        }
    }

    // The Types a position's fit reads (SPEC 5.7 "retains as that borrow would"; DECISIONS R4-M154, r2.2), appended to retentionReads as a
    // run its caller removes: a raw layer stating an Origin reads as each borrow that Origin belongs to at the position's arm
    // (ContractMembers: among the contract's inputs, an input whose outer Origin it is or a slot keeper; or the Origin's own where it is
    // bound), `ref/R` or `uniq/R` of its referent R
    // by its mode, and as itself where none does; any other Type as itself. OwnedOrigin leaves out `self` (Holder, SPEC 5.7): it names the
    // holding value, no input of the contract, and what a `during self` layer admits is its entries', so such a layer reads as itself.
    // Unanswered members are the Limit.
    private (int Start, int End) Reads(BoundType type, BoundType? written, BoundType? signature)
    {
        var start = this.retentionReads.Count;
        var stated = type.IsPointer && OwnedOrigin(type) is { Kind: not (OriginKind.Static or OriginKind.Unstated) };
        var members = this.rawMembers.Count;
        if (stated)
        {
            this.ContractMembers(OwnedOrigin(type)!, written, signature, CollectionsMarshal.AsSpan(this.retentionScope));
        }

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
            this.storagePositions.Add((AbstractStorage, node, position, null));
            this.storageTargets.Add((AbstractStorage, node, null));
            return;
        }

        if (TryPairLayer(type, out _, out var target))
        {
            this.AbstractPositions(environment, target, position);
            return;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            var part = type.Components[i];
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

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            var argument = type.OriginArguments[i];
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
    private void Targets(OriginPremiseEnvironment environment, BoundType? type, int storage, BoundType? signature)
    {
        if (type is null || storage < 0 || !this.storageTargeted.Add((storage, type, signature)))
        {
            return;
        }

        var nodes = this.retentionOccurrences;
        nodes.Clear();
        this.retentionOccurred.Clear();
        this.Occurrences(environment, type, nodes, callable: false);
        foreach (var node in nodes)
        {
            this.storageTargets.Add((storage, node, signature));
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

        for (var i = 0; i < type!.Components.Count; i++)
        {
            var part = type!.Components[i];
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

        for (var i = 0; i < type!.Components.Count; i++)
        {
            var part = type!.Components[i];
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

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            var argument = type.OriginArguments[i];
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
    // admitted case); null for any other. The entry, which Holds reads as its question.
    private Hold? Referent(HeldRange run, out bool exclusive)
    {
        Hold? referent = null;
        var invariant = false;
        for (var i = run.Start; i < run.End; i++)
        {
            var entry = this.retentionHeld[i];
            referent ??= Dereference(entry) && entry.Part is not null ? entry : null;
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

    // HeldRun of a value a contract reads as its body sees it (BodySees); a Closure whose environment does not substitute is the Limit.
    private HeldRange BodyRun(BoundType type, BoundType? written, BoundType? holder, out bool complete)
    {
        var sees = this.BodySees(type);
        this.retentionLimit |= sees is null;
        return this.HeldRun(sees ?? type, written, holder, out complete);
    }

    // A part of a contract input as the contract's body sees it (DECISIONS R4-M148, one rule): a Closure's environment
    // (TyCtxt.ClosureEnvironment), null where it does not substitute; any other Type itself. The storage walk (BodyRun) and OwnLayers read it.
    private BoundType? BodySees(BoundType type) => type.Kind == BoundTypeKind.Closure ? this.types.ClosureEnvironment(type) : type;

    // Whether a value of a Type has an entry at its own address or past an own step: no dereference, raw step, or erased or abstract part.
    // A Type argument has no address (SPEC 15.3.5), but a Loan<T>'s until U3a (G416 D4; row 10). The one by-value criterion, at any holder:
    // the storage walk's parts (Parts) and the parts an input holds at its own address (OwnLayers) read it.
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
    // RetentionPlace.Captured); the walked signature's declared Type at the position (Signature, DECISIONS BU-113: inside the binding of an
    // abstract Type of the walked function's own declaration that abstract Type, Below; null inside another Type parameter's image, in a
    // Closure's environment and below a part whose declared counterpart differs), and whether the walk is already inside that kept
    // Type's binding (Bound: below its root, whose storage takes the callee's-terms rows, storageBound), whether it lies in or past a
    // holder that a raw layer of that binding addresses where only a generic body acts (Routed, Routes: there it writes only by SPEC
    // 15.6.4's routes, 5.7 Calls' last sentence, so no layer's declaration statement, Holds Any, makes storage of no Type there; PLAN
    // G964), and whether a struct of the kept binding holding it by value declares a `drop` (Dropped: that destruction, the declaring
    // code, may act on it; a generic container's `drop` is a generic body, which acts by the routes).
    // For the input fold, its own state (Source).
    private readonly record struct StorageAt(
        int Storage, bool Writable, BoundType? Written, BoundType? Holder, int Depth, SourceAt Source, bool Captured = false, BoundType? Signature = null,
        bool Bound = false, bool Routed = false, bool Dropped = false)
    {
        // Whether what a part hides (an abstract part's storage, what a value an AnyOf content holds may refer to) is storage the call may
        // write: in every state of an output's walk from a storage the call writes (an input's, or the result's read as an output,
        // ResultWrites), which meets none past a shared layer, whether or not the part is a position (SPEC 15.6.4 route 2; PLAN G963,
        // DECISIONS BU-225); never in the result's own walk (ResultStorage) or an input fold's. The atom walk states the same condition
        // (RetentionAtom.HiddenWritable), and a handed callable's write test reads it per layer (HiddenWritableBelow, WrittenThrough).
        internal bool HiddenWritable => this.Storage is not (NoStorage or ResultStorage);

        // An output's walk from a storage.
        internal static StorageAt Output(int storage, bool writable) => new(storage, writable, null, null, 0, new(-1, Held: false, -1, 0));

        // An input fold's walk from input `input`'s own value.
        internal static StorageAt Input(int input) => new(NoStorage, false, null, null, 0, new(input, Held: true, -1, 0));
    }

    // A position of the hold walk as its declaration states it (Hold, Held; storageHolds): the Type there, the Type a struct's Field or
    // an enum's Case payload declares there while it is a Tuple, a fixed array or a raw layer (Written), the instance holding it
    // (Holder, whose case a declared slot's step reads), and the walked signature's declared Type there (Signature, as StorageAt's:
    // DECISIONS BU-113; null where none pairs), and whether it is Routed (StorageAt's: no layer there is unbounded). A Type of null is
    // storage no Type states, past a raw layer whose Signature it names (RawReach; the kept Type in its binding, which UntypedRows reads).
    private readonly record struct HoldAt(BoundType? Type, BoundType? Written, BoundType? Holder, BoundType? Signature = null, bool Routed = false);

    // The input fold's state at a Type (StorageAt): the input (-1 in an output's walk), whether the Type is held by value from the input or
    // reached through a layer, the node of the layer above (Related) and the path's id (its reduced prefix, Revision 10).
    private readonly record struct SourceAt(int Input, bool Held, int Above, int Path);
}
