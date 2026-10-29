// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The records of TyCtxt.Retains (Binding.Retention.cs computes the atoms and premises they are read from, Binding.Retention.Storage.cs
// the storages and positions, Binding.Retention.Sources.cs the sources and fits): per storage, what the premise closure from its
// positions reaches, and per output what its hold walk meets.
public sealed partial class Binding
{
    // The cases a pair layer may admit, in SemanticsMask order (CaseFits).
    private static readonly SemanticsKind[] PairCases =
    [
        SemanticsKind.Owner, SemanticsKind.Ref, SemanticsKind.Uniq, SemanticsKind.Obj, SemanticsKind.Rc, SemanticsKind.Arc, SemanticsKind.ObjRef,
        SemanticsKind.ObjUniq, SemanticsKind.Raw,
    ];

    // The positions the hold walk of an output has met (Held: a Type with the Type its declaration writes there and, where it writes one,
    // the instance holding it, all interned, so compared by reference, and whether it is lent), the reached nodes of each closure made
    // (by target node: the start and count of its run in retentionClosureNodes), and the storages' kinds, reused.
    private readonly HashSet<(BoundType Type, BoundType? Written, BoundType? Holder, bool Lent)> retentionMet = new();
    private readonly Dictionary<int, (int Start, int Count)> retentionClosures = new();
    private readonly List<int> retentionClosureNodes = new();
    private readonly List<RetentionKind[]> retentionKinds = new();
    private readonly List<RetentionKind[]> retentionKindPool = new(); // retentionKinds' arrays, by storage, for the computation's input count.
    private readonly List<(BoundType? Type, StorageRetention Retention)> retentionEntries = new();
    private readonly List<OutputRetention> retentionWrites = new();
    private readonly List<(int Storage, int Input, int Start, int Count)> retentionPairs = new(); // Each a run of retentionConjuncts.
    private readonly List<(BoundOrigin Longer, BoundOrigin Shorter)> retentionConjuncts = new();
    private int[] retentionFirst = []; // The links of RetentionRows by node and by atom or source, and the closure marks by node, reused.
    private int[] retentionNext = [];
    private int[] retentionSourceFirst = [];
    private int[] retentionSourceNext = [];
    private int[] retentionMarks = [];
    private RetentionKind[] retentionKept = [];
    private bool[] retentionAssigned = []; // Written's storages already in an entry, and the storages whose pairs Pairs reads, reused.
    private bool[] retentionMembers = [];
    private int retentionStamp;

    // What an output's hold walk met (SPEC 15.6.4, 5.7 Calls; DECISIONS BU-40): a raw layer without an Origin the call may write (rule
    // A), a `static` one, and one without an Origin behind a borrow or a stated raw layer in the result (R′: lent, not written). The
    // output is untied when it met Unstated or Static.
    [Flags]
    private enum Holding : byte
    {
        None = 0,
        Unstated = 1,
        Static = 2,
        Lent = 4,
    }

    // Whether a reader may read a Type whose storage has this verdict (SPEC 23.1.2; DECISIONS BU-36, R4-M139): Prepared, and Rejected, whose
    // parts that do not substitute hold, place and destroy nothing, so what holds whatever they are stays; not a Limit (retention's
    // CallRetention.Limit, the MIR's own limit). Rejected storage rests on a Binding failure (DIAGNOSTICS rule 8: a part Binding did not
    // bind makes it incomplete), so in a complete compilation it is a broken contract. TyCtxt.Readable publishes it to the MIR.
    internal bool Readable(BoundType type, StorageOutcome storage)
    {
        if (storage == StorageOutcome.Rejected && this.Result.IsComplete)
        {
            throw new InvalidOperationException($"The storage of {type.Name} is Rejected in a complete compilation.");
        }

        return storage != StorageOutcome.Limit;
    }

    // A reused array of at least `length` elements (grown, never shrunk); its caller sets what it reads.
    private static T[] Reused<T>(ref T[] array, int length)
    {
        if (array.Length < length)
        {
            array = new T[Math.Max(length, array.Length * 2)];
        }

        return array;
    }

    // Joins one storage's kinds into another's.
    private static void Join(RetentionKind[] into, RetentionKind[] kinds)
    {
        for (var i = 0; i < into.Length; i++)
        {
            into[i] |= kinds[i];
        }
    }

    // SPEC 15.6.4 (DECISIONS R4-M154): the records of the contract being computed. The result is one storage, every position of its Type
    // (StorageWalk); each input's writable storage is its root (Own) and the storages past its exclusive layers (Reached, by contract Type,
    // and Abstract). A storage retains an input by Content where the premise closure (OriginPremiseClosure, every Semantics case's
    // premises followed) from one of its targets reaches an occurrence of the input, by Value where the closure from a position's node
    // reaches one of the input's sources that the position can hold (CanHold), and by what the output's hold walk keeps (Held from its
    // holds, the result's whole Type or each position the storage walk names for input j, DECISIONS BU-52; each position once per lent
    // bit, BU-35, BU-40): a raw input's own value where a layer with an Origin or an abstract part can hold it (R4-M116 R-B4), every
    // input at a raw layer the call writes without an Origin (rule A, WrittenUnstated, the transfer rule narrowing it), the raw inputs at
    // one the result lends (R′); one stepping Unstated or Static, not lent, unties every storage of the output, where a transfer operation
    // writes it (TransferWrites, as for rule A), and a result atom whose non-static Origin reaches no input unties the result. A result
    // the call forms within a raw parameter's storage (`within`) retains that argument's own value. The transfer key by declaring identity
    // also gives the stores written only where the receiver refers (ReferentStores). The work is one closure per node a target names, each
    // node with its incoming premises once, and the walks; the identity key is read once per contract, and for a callee it does not name
    // the Iterator key once, at the first output that meets a written unstated layer, under `premises`, the scope TyCtxt read an instance
    // with an abstract part under (null: none, or an erased callable's, which has no callee). CallRetention.Limit where a walk meets
    // storage past its limits.
    private CallRetention RetentionRows(
        OriginPremiseEnvironment environment,
        int[] nodes,
        ReadOnlySpan<BoundType> inputs,
        BoundType result,
        FunctionKoto? callee,
        BindingScope? premises)
    {
        // The input occurrences and the sources at a node, linked indexes.
        var atoms = this.retentionAtoms;
        var (first, next) = (Reused(ref this.retentionFirst, environment.Closure.NodeCount + 1), Reused(ref this.retentionNext, atoms.Count));
        Array.Fill(first, -1);
        for (var i = 0; i < atoms.Count; i++)
        {
            if (atoms[i].Input >= 0)
            {
                (next[i], first[nodes[i]]) = (first[nodes[i]], i);
            }
        }

        this.retentionClosures.Clear();
        this.retentionClosureNodes.Clear();
        this.BeginStorages();
        this.RetentionSources(environment, inputs);
        var sources = this.retentionSources;
        var sourceFirst = Reused(ref this.retentionSourceFirst, environment.Closure.NodeCount + 1);
        var sourceNext = Reused(ref this.retentionSourceNext, sources.Count);
        Array.Fill(sourceFirst, -1);
        for (var i = 0; i < sources.Count; i++)
        {
            (sourceNext[i], sourceFirst[sources[i].Node]) = (sourceFirst[sources[i].Node], i);
        }

        var links = (first, next, sourceFirst, sourceNext, Reused(ref this.retentionMarks, environment.Closure.NodeCount + 1));
        var within = KimiLibraryCatalog.Within(callee?.BoundSymbol);
        var key = this.TransferKey(callee);
        Transfer? transfer = null;
        var kept = this.retentionKept.Length == inputs.Length ? this.retentionKept : this.retentionKept = new RetentionKind[inputs.Length];
        Array.Clear(kept);
        if (this.retentionKindPool.Count > 0 && this.retentionKindPool[0].Length != inputs.Length)
        {
            this.retentionKindPool.Clear();
        }

        var whole = (result, (BoundType?)null, (BoundType?)null);
        var untied = this.Hold(CallRetention.ResultOutput, new(in whole), inputs, kept, (key, callee, premises), ref transfer);
        if (within >= 0)
        {
            kept[within] |= RetentionKind.Value;
        }

        for (var i = 0; i < atoms.Count; i++)
        {
            untied |= atoms[i].Input == CallRetention.ResultOutput && atoms[i].Origin?.Kind != OriginKind.Static &&
                !this.Reaches(environment, nodes[i], links);
        }

        this.BeginStorages();
        this.StorageWalk(environment, result, StorageAt.Output(ResultStorage, writable: true));
        this.Storages(environment, links, kept);
        var resultKinds = this.retentionKinds[ResultStorage];
        var resultRecord = new StorageRetention([.. resultKinds], this.Pairs(resultKinds, ResultStorage), untied);
        var writes = this.retentionWrites;
        writes.Clear();
        for (var output = 0; output < inputs.Length; output++)
        {
            Array.Clear(kept);
            this.BeginStorages();
            this.RootWalk(environment, inputs[output]);
            var held = this.Hold(output, CollectionsMarshal.AsSpan(this.storageHolds), inputs, kept, (key, callee, premises), ref transfer);
            this.Storages(environment, links, kept);
            if (this.Written(output, held) is { } written)
            {
                writes.Add(written);
            }
        }

        return this.retentionLimit ? CallRetention.Limit : new(resultRecord, [.. writes], this.ReferentStores(key, callee?.BoundSymbol, inputs));
    }

    // Each storage's kinds for the walk made (retentionKinds, by storage): Content where the closure from one of its targets reaches an
    // occurrence of an input (none for a raw input's own layer), Value where the closure from a position's node reaches a source the
    // position can hold (CanHold), Content for one it cannot, Value over Content; then the output's hold walk's kinds, Chain over Content.
    // A position whose Origin the call's Subst brings can also hold a source whose fit relates such Origins the premises do not prove: the
    // conjunction (retentionPairs) of a pair per layer of the source's path the closure misses and the position (Conjunction) is the
    // solved Origins' to judge (DECISIONS R4-M154, Revisions 2 and 10).
    private void Storages(
        OriginPremiseEnvironment environment,
        (int[] First, int[] Next, int[] SourceFirst, int[] SourceNext, int[] Marks) links,
        RetentionKind[] kept)
    {
        var kinds = this.retentionKinds;
        kinds.Clear();
        for (var s = 0; s < this.storageTypes.Count; s++)
        {
            if (s == this.retentionKindPool.Count)
            {
                this.retentionKindPool.Add(new RetentionKind[kept.Length]);
            }

            Array.Clear(this.retentionKindPool[s]);
            kinds.Add(this.retentionKindPool[s]);
        }

        foreach (var (storage, node) in this.storageTargets)
        {
            foreach (var reached in this.Closure(environment, node))
            {
                for (var atom = reached < links.First.Length ? links.First[reached] : -1; atom >= 0; atom = links.Next[atom])
                {
                    kinds[storage][this.retentionAtoms[atom].Input] |= this.retentionAtoms[atom].Kind is null ? RetentionKind.None : RetentionKind.Content;
                }
            }
        }

        this.retentionPairs.Clear();
        this.retentionConjuncts.Clear();
        foreach (var (storage, node, type) in this.storagePositions)
        {
            var stamp = ++this.retentionStamp;
            foreach (var reached in this.Closure(environment, node))
            {
                if (reached < links.Marks.Length)
                {
                    links.Marks[reached] = stamp;
                }
            }

            foreach (var reached in this.Closure(environment, node))
            {
                for (var source = reached < links.SourceFirst.Length ? links.SourceFirst[reached] : -1; source >= 0; source = links.SourceNext[source])
                {
                    var found = this.retentionSources[source];
                    var held = this.Fitted(found, links.Marks, stamp) && this.CanHold(type, found);
                    kinds[storage][found.Input] |= held ? RetentionKind.Value : RetentionKind.Content;
                }
            }

            var position = this.Instantiated(environment, node);
            for (var s = 0; position is not null && s < this.retentionSources.Count; s++)
            {
                var found = this.retentionSources[s];
                if (this.Conjunction(environment, found, links.Marks, stamp, position) is var (start, count) && count > 0)
                {
                    this.Conjoined(storage, found.Input, start, count, this.CanHold(type, found));
                }
            }
        }

        foreach (var storage in kinds)
        {
            for (var i = 0; i < storage.Length; i++)
            {
                var kind = ((storage[i] & RetentionKind.Value) != 0 ? RetentionKind.Value : storage[i]) | kept[i];
                storage[i] = (kind & RetentionKind.Chain) != 0 ? kind & ~RetentionKind.Content : kind;
            }
        }
    }

    // An output's record from the storages' kinds and pairs (Storages): its root's, the storages past it joined by Origin-erased Type, each
    // storage in one entry (an Origin-only difference between them is in their pairs, DECISIONS R4-M154, Revision 2), the Abstract one
    // where the walk met an abstract part, and All, their join with every one of their pairs; every one untied where the output is; null
    // where the call keeps nothing there.
    private OutputRetention? Written(int output, bool untied)
    {
        var kinds = this.retentionKinds;
        var inputs = kinds[OwnStorage].Length;
        var all = new RetentionKind[inputs];
        var reached = this.retentionEntries;
        reached.Clear();
        var assigned = Reused(ref this.retentionAssigned, kinds.Count);
        Array.Clear(assigned, 0, kinds.Count);
        for (var s = ResultStorage + 1; s < kinds.Count; s++)
        {
            if (assigned[s])
            {
                continue;
            }

            var (erased, join, members) = (this.types.Erase(this.storageTypes[s]!), new RetentionKind[inputs], this.Members());
            for (var t = s; t < kinds.Count; t++)
            {
                if (!assigned[t] && ReferenceEquals(this.types.Erase(this.storageTypes[t]!), erased))
                {
                    (assigned[t], members[t]) = (true, true);
                    Join(join, kinds[t]);
                }
            }

            Join(all, join);
            reached.Add((erased, new(join, this.Pairs(join, members), untied)));
        }

        var hidden = this.storagePositions.Exists(static x => x.Storage == AbstractStorage);
        if (hidden)
        {
            reached.Add((null, new([.. kinds[AbstractStorage]], this.Pairs(kinds[AbstractStorage], AbstractStorage), untied)));
            Join(all, kinds[AbstractStorage]);
        }

        var any = untied;
        for (var i = 0; i < inputs; i++)
        {
            any |= kinds[OwnStorage][i] != RetentionKind.None || all[i] != RetentionKind.None;
        }

        if (!any)
        {
            return null;
        }

        var own = new StorageRetention([.. kinds[OwnStorage]], this.Pairs(kinds[OwnStorage], OwnStorage), untied);
        var joined = this.Members();
        Array.Fill(joined, true, ResultStorage + 1, kinds.Count - ResultStorage - 1);
        joined[AbstractStorage] = hidden;
        return new(output, own, [.. reached], new(all, this.Pairs(all, joined), untied));
    }

    // Whether a position's closure (marked `stamp`) reaches every layer of a source's path (RetentionSource.Path: the layers the premise
    // catalog's well-formedness rule does not relate to the layer above, Revision 10), the conjunction its fit is.
    private bool Fitted(in RetentionSource source, int[] marks, int stamp)
    {
        for (var i = source.Path.Start; i < source.Path.Start + source.Path.Count; i++)
        {
            if (this.retentionPaths[i] is var layer && (layer >= marks.Length || marks[layer] != stamp))
            {
                return false;
            }
        }

        return true;
    }

    // The pairs of a source's path layers a position's closure (marked `stamp`) misses with the position's Origin, appended to
    // retentionConjuncts as a run (Start, Count): the conjunction the call's solved Origins judge, where each such layer's Origin the
    // call's Subst brings (Instantiated); none (Count 0) where the closure reaches each layer or a missed one is no Subst Origin, whose
    // relation no solved Origin decides.
    private (int Start, int Count) Conjunction(OriginPremiseEnvironment environment, in RetentionSource source, int[] marks, int stamp, BoundOrigin position)
    {
        var start = this.retentionConjuncts.Count;
        for (var i = source.Path.Start; i < source.Path.Start + source.Path.Count; i++)
        {
            var layer = this.retentionPaths[i];
            if (layer < marks.Length && marks[layer] == stamp)
            {
                continue;
            }

            if (this.Instantiated(environment, layer) is not { } longer)
            {
                this.retentionConjuncts.RemoveRange(start, this.retentionConjuncts.Count - start);
                return (start, 0);
            }

            this.retentionConjuncts.Add((longer, position));
        }

        return (start, this.retentionConjuncts.Count - start);
    }

    // A storage's conjunction for an input (the run of retentionConjuncts from `start`), kept where the position can hold the source
    // (`holds`) and the storage does not already list it, else dropped.
    private void Conjoined(int storage, int input, int start, int count, bool holds)
    {
        var listed = !holds;
        foreach (var (s, i, from, n) in this.retentionPairs)
        {
            listed |= s == storage && i == input && this.SameConjunction(from, n, start, count);
        }

        if (listed)
        {
            this.retentionConjuncts.RemoveRange(start, count);
            return;
        }

        this.retentionPairs.Add((storage, input, start, count));
    }

    // Whether two runs of retentionConjuncts are one conjunction.
    private bool SameConjunction(int a, int count, int b, int other)
    {
        var same = count == other;
        for (var k = 0; same && k < count; k++)
        {
            same = this.retentionConjuncts[a + k] == this.retentionConjuncts[b + k];
        }

        return same;
    }

    // A node's Origin where the call's Subst brings it (retentionInstantiated), else null.
    private BoundOrigin? Instantiated(OriginPremiseEnvironment environment, int node)
        => environment.Closure.Origin(node) is { } origin && this.retentionInstantiated.Contains(origin) ? origin : null;

    // The conjunctions of one storage (retentionPairs) whose input `kinds` does not already give Value, each once.
    private (int Input, BoundOrigin Longer, BoundOrigin Shorter, bool Joined)[] Pairs(RetentionKind[] kinds, int storage)
    {
        if (this.retentionPairs.Count == 0)
        {
            return [];
        }

        var members = this.Members();
        members[storage] = true;
        return this.Pairs(kinds, members);
    }

    // The conjunctions of the storages marked in `members` (retentionPairs) whose input `kinds` does not already give Value, each once, as a
    // record's pairs (a pair after the first of its conjunction Joined).
    private (int Input, BoundOrigin Longer, BoundOrigin Shorter, bool Joined)[] Pairs(RetentionKind[] kinds, bool[] members)
    {
        List<(int Input, int Start, int Count)>? listed = null;
        foreach (var (storage, input, start, count) in this.retentionPairs)
        {
            var open = members[storage] && (kinds[input] & RetentionKind.Value) == 0;
            if (open && !(listed ??= []).Exists(x => x.Input == input && this.SameConjunction(x.Start, x.Count, start, count)))
            {
                listed.Add((input, start, count));
            }
        }

        var pairs = new List<(int Input, BoundOrigin Longer, BoundOrigin Shorter, bool Joined)>();
        foreach (var (input, start, count) in listed ?? [])
        {
            for (var k = 0; k < count; k++)
            {
                pairs.Add((input, this.retentionConjuncts[start + k].Longer, this.retentionConjuncts[start + k].Shorter, k > 0));
            }
        }

        return pairs.Count == 0 ? [] : [.. pairs];
    }

    // The storages of the walk made, none marked (Pairs' members), reused.
    private bool[] Members()
    {
        var members = Reused(ref this.retentionMembers, this.storageTypes.Count);
        Array.Clear(members, 0, this.storageTypes.Count);
        return members;
    }

    // The nodes the premise closure from a node reaches, once per computation (OriginPremiseClosure.Close's work, OriginProofMetrics measured).
    private ReadOnlySpan<int> Closure(OriginPremiseEnvironment environment, int target)
    {
        if (!this.retentionClosures.TryGetValue(target, out var run))
        {
            var closure = environment.Closure;
            var start = this.OriginProofMetrics is null ? 0 : Stopwatch.GetTimestamp();
            closure.Close(target, -1, ulong.MaxValue);
            this.OriginProofMetrics?.Closure(closure, this.originPremiseDepth > 1, Stopwatch.GetTimestamp() - start);
            run = (this.retentionClosureNodes.Count, closure.LastWork.Insertions);
            for (var k = 0; k < run.Count; k++)
            {
                this.retentionClosureNodes.Add(closure.ReachedNode(k));
            }

            this.retentionClosures.Add(target, run);
        }

        return CollectionsMarshal.AsSpan(this.retentionClosureNodes).Slice(run.Start, run.Count);
    }

    // Whether the closure from a node reaches an occurrence of an input.
    private bool Reaches(OriginPremiseEnvironment environment, int node, (int[] First, int[] Next, int[] SourceFirst, int[] SourceNext, int[] Marks) links)
    {
        foreach (var reached in this.Closure(environment, node))
        {
            if (reached < links.First.Length && links.First[reached] >= 0)
            {
                return true;
            }
        }

        return false;
    }

    // An output's hold walk (Held from each of its holds: the result's whole Type, or each position of input j's storages the storage walk
    // emits with the Type its declaration writes there and the instance holding it, DECISIONS BU-52; each position once per lent bit)
    // and what it keeps (`kept`): rule A's rows at a written unstated layer (WrittenUnstated, the transfer read once per contract), the
    // raw inputs at a lent one (R′); whether it met a layer stepping Unstated or Static, not lent, which unties the output. A published
    // transfer operation writes only its own outputs (TransferWrites, by the declaring-identity key: an Iterator's `next` writes each),
    // so at another it neither forms rule A's rows nor unties it: the output has no record from these layers.
    private bool Hold(
        int output,
        ReadOnlySpan<(BoundType Type, BoundType? Written, BoundType? Holder)> holds,
        ReadOnlySpan<BoundType> inputs,
        RetentionKind[] kept,
        (Transfer Key, FunctionKoto? Callee, BindingScope? Premises) call,
        ref Transfer? transfer)
    {
        this.retentionMet.Clear();
        var met = Holding.None;
        foreach (var (type, written, holder) in holds)
        {
            met |= this.Held(type, written, holder, output, kept, lent: false);
        }

        var writes = (met & (Holding.Unstated | Holding.Static)) != 0 && TransferWrites(call.Key, call.Callee?.BoundSymbol, output);
        if (writes && (met & Holding.Unstated) != 0)
        {
            transfer ??= call.Key == Transfer.None ? this.IteratorTransfer(call.Callee?.BoundSymbol, inputs, call.Premises) : call.Key;
            this.WrittenUnstated(kept, output, inputs, transfer.Value, call.Callee?.BoundSymbol);
        }
        else if ((met & Holding.Lent) != 0)
        {
            foreach (var (input, _) in this.retentionRawInputs)
            {
                kept[input] |= RetentionKind.Value | RetentionKind.Chain;
            }
        }

        return writes;
    }

    // SPEC 15.6.4's rule A (DECISIONS R4-M131, BU-40): at a raw layer without an Origin that the call may write in `output`, every input
    // keeps what any chain of reads through it carries, and every input but the one written through keeps its own value; narrower for a
    // published transfer operation (TransferKept), at an output it writes (Hold reads TransferWrites).
    private void WrittenUnstated(RetentionKind[] kept, int output, ReadOnlySpan<BoundType> inputs, Transfer transfer, BindingSymbol? callee)
    {
        for (var i = 0; i < inputs.Length; i++)
        {
            kept[i] |= i == output ? RetentionKind.Chain : this.TransferKept(transfer, callee, inputs[i], i, output);
        }
    }

    // SPEC 15.6.4, 5.7 Calls (DECISIONS R4-M116 items 2 and 5, R-B2, BU-30, BU-35, BU-40), the hold walk: a position of `output` the call
    // may write or lend, a hold (Hold's: the result's whole Type, or a position of input j's writable storage) or a position below
    // one, with `written`, the Type a struct's Field or an enum's Case payload declares there while it is a Tuple, a fixed array or a raw
    // layer (null elsewhere: off a declaration, or below a Type parameter or another part it does not write inline), the instance holding
    // it there (`holder`, whose case a declared slot's step reads, SPEC 15.3.5; DECISIONS BU-44), and whether it is
    // `lent`: below a borrow in the result (BorrowReferent: a borrow layer, a pair layer admitting only borrows, a Slice's or UniqSlice's
    // elements) or below a result raw layer stating an input's or slot's Origin (stepping Exclusive or Shared: it retains as that borrow
    // would, SPEC 5.7 Calls, so what it addresses is that borrow's storage, 5.2.1; PLAN G255's C1). Each raw input other than `output`
    // whose Type the position can hold retains its own value there (R-B4: a raw layer stating an Origin or `static` that agrees with it,
    // Origins fitted as rule 2 fits a position, Fits, SPEC 3.8; or an abstract part, which an instance may make one); at a raw
    // layer without an Origin rule A or R′ decides it (RetentionRows); a pointer is never part of its own pointee. A raw layer steps by
    // DeclaredStep (its declaration's cap where `written` states the layer, its Origin's kind elsewhere, DECISIONS BU-24): Unstated is
    // what the call writes there (Holding.Unstated) or lends (Holding.Lent), and Static is storage the body does not own (Holding.Static,
    // lent or not). A value stored at a writable position brings its whole Type (BU-1), so the walk goes below every layer that stores
    // (IsCallable), as RetentionParts' writable positions do, and into a struct's or enum's stored content. What a position gives depends
    // on its Type, `written`, its holder and `lent` alone and only accumulates, so each position is walked at most once per lent bit and output
    // (retentionMet): the work is linear in the distinct positions, not in the paths to them through the interned Types (V16). A struct or
    // enum whose storage is a Limit, a growing one among them (PrepareTypeStorage's limits, DECISIONS BU-12), makes the contract
    // CallRetention.Limit; Rejected storage is walked as far as it substitutes, a part that does not (null) holding nothing (Readable,
    // BU-36).
    private Holding Held(BoundType? type, BoundType? written, BoundType? holder, int output, RetentionKind[] kept, bool lent)
    {
        if (type is null)
        {
            return Holding.None;
        }

        written = ReferenceTypes.IsPointer(written) || written?.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray ? written : null;
        holder = written is null ? null : holder;
        if (!this.retentionMet.Add((type, written, holder, lent)))
        {
            return Holding.None;
        }

        var nominal = AdtDef.IsStruct(type) || AdtDef.IsEnum(type);
        if (nominal && !this.Readable(type, this.PrepareTypeStorage(type)))
        {
            this.retentionLimit = true;
            return Holding.None;
        }

        RawStep? step = type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw } ? this.DeclaredStep(type, written, holder) : null;
        foreach (var (input, pointer) in this.retentionRawInputs)
        {
            var holds = type.IsAbstract || (step is not (null or RawStep.Unstated) && this.Fits(type, pointer));
            kept[input] |= input != output && holds ? RetentionKind.Value : RetentionKind.None;
        }

        var met = step switch
        {
            RawStep.Static => Holding.Static,
            RawStep.Unstated when lent => Holding.Lent,
            RawStep.Unstated => Holding.Unstated,
            _ => Holding.None,
        };
        var below = lent ||
            (output == CallRetention.ResultOutput && (this.BorrowReferent(type, out _, out _) is not null || step is RawStep.Exclusive or RawStep.Shared));
        met |= nominal ? this.Stores(type, output, kept, below) : Holding.None;
        for (var i = 0; !IsCallable(type) && i < type.Components.Count; i++)
        {
            met |= this.Held(type.Components[i], InlinePart(written, i), holder, output, kept, below);
        }

        return met;
    }

    // The hold walk (Held) of a struct's or enum's stored content (SPEC 5.7 Slots and ownership): its base, Fields and Case payloads in
    // the instance's kept parts (Instance, DECISIONS BU-41), the latter with the Types their declarations write; what they met. A
    // Case's payloads are walked only where its instance substituted that Case (its kept payload Tuple set), the part the storage verdict
    // walked (DECISIONS BU-37): the per-part rule at the Case's grain, which bounds the walk, so a failed Case's other payloads hold
    // nothing (walked by payload, a failed Case's growing siblings would not end).
    private Holding Stores(BoundType instance, int output, RetentionKind[] kept, bool lent)
    {
        if (instance.Symbol?.Adt is not { } adt)
        {
            return Holding.None;
        }

        var storage = this.Instance(instance);
        var met = storage.PartsBase is { } parent ? this.Held(parent, null, null, output, kept, lent) : Holding.None;
        var parts = storage.Parts;
        for (var i = 0; AdtDef.IsStruct(instance) && i < adt.Fields.Length; i++)
        {
            met |= this.Held(parts[i], WrittenType(adt.Fields[i]), instance, output, kept, lent);
        }

        for (var i = 0; AdtDef.IsEnum(instance) && i < parts.Length; i++)
        {
            for (var j = 0; parts[i] is { } payloads && adt.Cases[i] is { } selected && j < selected.Payload.Length; j++)
            {
                met |= this.Held(payloads.Components[j], WrittenType(selected.Payload[j]), instance, output, kept, lent);
            }
        }

        return met;
    }
}
