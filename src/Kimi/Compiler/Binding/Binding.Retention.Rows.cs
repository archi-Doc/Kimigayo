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
    private readonly Dictionary<BoundType, RetentionKind[]> retentionHidden = new(ReferenceEqualityComparer.Instance); // HiddenKinds' per Type, per Storages.
    private readonly Dictionary<(BoundType Type, RetentionKind Kind), int> retentionHiddenSeen = new(); // HiddenKind's walk, per input: Enters' depths.
    private readonly List<BoundType> retentionArguments = new(); // The contract's instantiated inputs (Filled), per computation.
    private readonly Dictionary<(BoundType Position, int Input, bool Own), Fill> retentionFills = new(); // Filled's answers, per computation.
    private readonly HashSet<(HoldAt At, bool Lent, int Storage)> retentionMet = new();
    private readonly Dictionary<int, (int Start, int Count)> retentionClosures = new();
    private readonly List<int> retentionClosureNodes = new();
    private readonly List<RetentionKind[]> retentionKinds = new();
    private readonly List<RetentionKind[]> retentionKindPool = new(); // retentionKinds' arrays, by storage, for the computation's input count.
    private readonly List<RetentionKind[]> retentionKept = new(); // What the hold walk keeps, by storage (Hold), arrays as retentionKindPool's.
    private readonly List<(int Storage, BoundOrigin Origin)> retentionStated = new(); // The stated raw layers the hold walk met (Held).
    private readonly List<(BoundType? Type, StorageRetention Retention)> retentionEntries = new();
    private readonly List<OutputRetention> retentionWrites = new();
    private readonly List<(int Storage, int Input, int Start, int Count)> retentionPairs = new(); // Each a run of retentionConjuncts.
    private readonly List<(BoundOrigin Longer, BoundOrigin Shorter)> retentionConjuncts = new();
    private int[] retentionFirst = []; // The links of RetentionRows by node and by atom or source, and the closure marks by node, reused.
    private int[] retentionNext = [];
    private int[] retentionSourceFirst = [];
    private int[] retentionSourceNext = [];
    private int[] retentionMarks = [];
    private Holding[] retentionHolding = []; // What each storage's holds met and Hold's outcome there (Chained), reused.
    private bool retentionParametric; // The computation reads its callee's kept abstract Types in the callee's terms (Parametric).
    private bool retentionFilled; // A callable's write keeps a row in the Abstract storage of the walk made (FilledKinds), whose entry Written then forms.
    private (RetentionKind Raw, RetentionKind Free)[] retentionUndecided = []; // Per input, what the premise closure does not decide (FilledKinds), reused.
    private FunctionKoto? retentionCallee; // Whose own abstract Types those are (Parametric): a Closure's enclosing function's are the caller's.
    private RetentionKind[] retentionChained = []; // Rule A's rows of the output (Hold), which a Chained storage joins as it admits.
    private bool[] retentionAssigned = []; // Written's storages already in an entry, and the storages whose pairs Pairs reads, reused.
    private bool[] retentionMembers = [];
    private int retentionStamp;

    // What an output's hold walk met per storage, and Hold's outcome there (Chained) in the same table (SPEC 15.6.4, 5.7 Calls; DECISIONS
    // BU-40): a raw layer without an Origin the call may write (rule
    // A), a `static` one, and one without an Origin behind a borrow or a stated raw layer in the result (R′: lent, not written). The
    // output is untied when it met Unstated or Static. An Unstated layer bounds what the call stores through it to what its pointee Type
    // admits where the parameter's Type expression writes it, so its rows stay in the storage holding it; one a declaration states in its
    // own Fields, Case payloads or base, at any depth of a kept part, addresses storage it does not state and bounds nothing past it
    // (Unbounded: its Holdings entry holds Any, TyCtxt.RawStated; SPEC 5.7 Calls, DECISIONS BU-46's write fact): its rows reach every
    // storage of the output. What the call stores through a bounding one is retained by the pointee storage's own positions, with what
    // any chain of reads through an argument carries where such a position can hold a value that carries a Loan (Loans: a borrow, an
    // abstract part, a Type argument's or an erased environment's dependency; SPEC 15.6.4 "also as a part of a value of that layer's
    // pointee Type"): such a storage takes rule A's chain rows too, but only for the arguments its own positions admit (Chained, Hold's
    // outcome: Storages joins them where the storage's Origin tests retain the input, the fit a stated Origin makes; SPEC 5.7 Calls "what
    // its pointee Type admits", C-352's C3), since what a chain of reads brings there is a value of that Type, and an argument's own value
    // only where a position can hold it (the Value test, SPEC 15.6.4); one holding no such position
    // (Scalars, raw layers stating an Origin, which StatedFits judges) takes only its own. A part no Type states (a part Binding did not
    // bind, the storage past a layer whose content no Type states) or a Rejected Type may be any Type (Any), so no Origin bounds it and
    // its storage takes rule A's rows whole, failing closed: for the Chained verdict this departs from DECISIONS BU-36's "a part
    // that does not substitute holds nothing" (only dependent records differ, SPEC 23.1.2). Loans fails closed: a part is Loan-free only by MirTypes.HoldsLoans'
    // criterion over a fully bound, prepared Type (an entry it passes on, Hold.PassesOn, the one row both walks read, holding the part's
    // Type, in a Type whose storage is Prepared), so every other part (a borrow, an abstract or erased part, Any) holds Loans. A storage
    // whose Type holds no Loan still retains an input's own value where a position can hold it (PLAN G935's R, the owner's O-359 (a): a
    // Loan-free value carries what it was read from, 15.6 Holders; N7's Free rows are gone).
    [Flags]
    private enum Holding : ushort
    {
        None = 0,
        Unstated = 1,
        Static = 2,
        Lent = 4,
        Unbounded = 8,
        Loans = 16,
        Any = 128,
        Chained = 256,
    }

    // How a handed callable may retain an input at a position (Filled; SPEC 15.6.4 "through its result or its writes"): in the value of
    // the position's Type its result holds (Result), or through a parameter that holds the position's Type (Writes: a Self receiver, a
    // borrow of it), which also stores where a value of that Type reaches past its own hidden references (the Abstract storage; SPEC
    // 15.6.4's routes, its `later`).
    private enum Fill : byte
    {
        None,
        Result,
        Writes,
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

    // Joins one storage's kinds into another's, Chain over Content (Chain closes over every read, so it covers one).
    private static void Join(RetentionKind[] into, RetentionKind[] kinds)
    {
        for (var i = 0; i < into.Length; i++)
        {
            into[i] |= kinds[i];
            into[i] = (into[i] & RetentionKind.Chain) != 0 ? into[i] & ~RetentionKind.Content : into[i];
        }
    }

    // The k-th callable a contract hands the body, from its two lists (the Callable bounds' and requirements' signatures, then the
    // Function parts its Types hold; Filled, HandsWriter): false past the last; `callable` null for one that is no Function Type, which
    // neither takes nor writes an input.
    private static bool Handed(List<BoundType> handed, List<BoundType> callables, int k, out BoundType? callable)
    {
        var listed = k < handed.Count + callables.Count;
        var found = !listed ? null : k < handed.Count ? handed[k] : callables[k - handed.Count];
        callable = found is { Kind: BoundTypeKind.Function } ? found : null;
        return listed;
    }

    // SPEC 15.6.4 (DECISIONS R4-M154): the records of the contract being computed. The result is one storage, every position of its Type
    // (StorageWalk); each input's writable storage is its root (Own) and the storages past its exclusive layers (Reached, by contract Type,
    // and Abstract). A storage retains an input by Content where the premise closure (OriginPremiseClosure, every Semantics case's
    // premises followed) from one of its targets reaches an occurrence of the input, by Value where the closure from a position's node
    // reaches one of the input's sources that the position can hold (CanHold), and by what the output's hold walk keeps (Held from its
    // holds, the result's whole Type or each position the storage walk names for input j, DECISIONS BU-52; each position once per lent
    // bit, BU-35, BU-40): a raw source's own value (an input holding a raw layer by value) at a raw layer with an Origin and where a
    // callable may retain it at an abstract part (DECISIONS BU-66), every input at a raw layer the call writes without an Origin (rule A,
    // WrittenUnstated, the transfer rule narrowing it), the raw arguments at one the result lends (R′); one stepping Unstated or Static,
    // not lent, unties every storage of the output, where a transfer operation writes it (TransferWrites, as for rule A), and a result
    // atom whose non-static Origin reaches no input unties the result. A result the call forms within a raw parameter's storage
    // (`within`) retains that argument's own value. The transfer key by declaring identity
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
        // SPEC states a transfer operation's rows (TransferKey); every other callee's are read from its declared signature.
        var parametric = callee is not null && this.TransferKey(callee) == Transfer.None;
        this.retentionParametric = parametric;
        this.retentionCallee = callee;
        this.retentionArguments.Clear();
        this.retentionFills.Clear();
        foreach (var input in inputs)
        {
            this.retentionArguments.Add(input);
        }

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
        var undecided = Reused(ref this.retentionUndecided, inputs.Length);
        Array.Clear(undecided, 0, inputs.Length);
        foreach (var (input, kind) in this.retentionRawSources)
        {
            undecided[input].Raw |= kind ?? RetentionKind.Value; // A raw source: its own value at its own layer, else its entry's kind (BU-66, A2).
        }

        var sourceFirst = Reused(ref this.retentionSourceFirst, environment.Closure.NodeCount + 1);
        var sourceNext = Reused(ref this.retentionSourceNext, sources.Count);
        Array.Fill(sourceFirst, -1);
        for (var i = 0; i < sources.Count; i++)
        {
            // A source that names no Origin (R) is matched at every position, never through a node.
            if (sources[i].Node >= 0)
            {
                (sourceNext[i], sourceFirst[sources[i].Node]) = (sourceFirst[sources[i].Node], i);
            }
            else
            {
                undecided[sources[i].Input].Free = RetentionKind.Value;
            }
        }

        var links = (first, next, sourceFirst, sourceNext, Reused(ref this.retentionMarks, environment.Closure.NodeCount + 1));
        var within = KimiLibraryCatalog.Within(callee?.BoundSymbol);
        var key = this.TransferKey(callee);
        Transfer? transfer = null;
        if (this.retentionKindPool.Count > 0 && this.retentionKindPool[0].Length != inputs.Length)
        {
            this.retentionKindPool.Clear();
        }

        this.BeginStorages();
        this.storageResultLeaves.Clear();
        this.storageResultSeen.Clear();
        this.ResultLeaves(result, 0);
        this.StorageWalk(environment, result, StorageAt.Output(ResultStorage, writable: true) with { Signature = this.SignatureResult });
        var kept = this.Kept(inputs.Length);
        var whole = (new HoldAt(result, null, null, this.SignatureResult), ResultStorage);
        var untied = this.Hold(environment, CallRetention.ResultOutput, new(in whole), inputs, (key, callee, premises), ref transfer);
        if (within >= 0)
        {
            kept[ResultStorage][within] |= RetentionKind.Value;
        }

        for (var i = 0; i < atoms.Count; i++)
        {
            untied |= atoms[i].Input == CallRetention.ResultOutput && atoms[i].Origin?.Kind != OriginKind.Static &&
                !this.Reaches(environment, nodes[i], links);
        }

        this.Storages(environment, links, kept, CallRetention.ResultOutput);
        var resultKinds = this.retentionKinds[ResultStorage];
        var resultRecord = new StorageRetention([.. resultKinds], this.Pairs(resultKinds, ResultStorage), untied);
        var resultWrites = this.ResultWrites(environment, links, inputs, result, (key, callee, premises), ref transfer) is { } past
            ? new OutputRetention(CallRetention.ResultOutput, resultRecord, [.. past.Reached], past.All, past.Kept, past.Handed) : null;
        var writes = this.retentionWrites;
        writes.Clear();
        for (var output = 0; output < inputs.Length; output++)
        {
            if (KimiLibraryCatalog.NoStore(callee?.BoundSymbol, output))
            {
                continue; // SPEC 5.2 Placement, 5.6: KimiLibraryCatalog.NoStore, the contract stores no value here, so writes no storage.
            }

            // A by-value input that is the callee's own kept Type (x: T): the callee may destroy it, and its cleanup runs the binding's own
            // `drop` (SPEC 15.6.4 "including callee cleanup"), so that binding's storage is read instantiated, not in the callee's terms.
            this.retentionParametric = parametric && !Hides(this.SignatureInput(output));
            this.BeginStorages();
            this.RootWalk(environment, inputs[output], this.SignatureInput(output));
            kept = this.Kept(inputs.Length);
            var held = this.Hold(environment, output, CollectionsMarshal.AsSpan(this.storageHolds), inputs, (key, callee, premises), ref transfer);
            this.Storages(environment, links, kept, output);
            if (this.Written(output, held) is { } written)
            {
                writes.Add(written);
            }
        }

        return this.retentionLimit ? CallRetention.Limit
            : new(resultRecord, resultWrites, [.. writes], this.ReferentStores(key, callee?.BoundSymbol, inputs), composes: !this.StatesWrites(callee));
    }

    // The result read as an output (CallRetention.ResultWrites; G841's H2, PLAN G935): the walk an input's output takes, from the result's
    // own value as its root storage, so that each storage past a layer the call writes in the result is a Reached entry with the rows of
    // the positions and raw layers it holds (Hold from its positions, Storages); the caller keeps the root's record the whole result
    // Type's (Result), which every reader of the result reads. Null where no storage lies past such a layer.
    private OutputRetention? ResultWrites(
        OriginPremiseEnvironment environment,
        (int[] First, int[] Next, int[] SourceFirst, int[] SourceNext, int[] Marks) links,
        ReadOnlySpan<BoundType> inputs,
        BoundType result,
        (Transfer Key, FunctionKoto? Callee, BindingScope? Premises) call,
        ref Transfer? transfer)
    {
        this.BeginStorages();
        this.StorageWalk(environment, result, StorageAt.Output(OwnStorage, writable: true) with { Signature = this.SignatureResult });
        var kept = this.Kept(inputs.Length);
        var untied = this.Hold(environment, CallRetention.ResultOutput, CollectionsMarshal.AsSpan(this.storageHolds), inputs, call, ref transfer);
        this.Storages(environment, links, kept, CallRetention.ResultOutput);
        return this.Written(CallRetention.ResultOutput, untied) is { Reached.Length: > 0 } past ? past : null;
    }

    // Each storage's kinds for the walk made (retentionKinds, by storage): Content where the closure from one of its targets reaches an
    // occurrence of an input (none for a raw input's own layer), Value where the closure from a position's node reaches a source the
    // position can hold (CanHold), Content for one it cannot, Value over Content; then the hold walk's kinds of that storage (`kept`, by
    // storage), and at a storage rule A's rows reach only through its Loan-carrying positions (Chained) their Chain for each input those
    // tests retain or a conjunction names (C-352's C3: what a chain of reads brings there is a value its positions admit, SPEC 5.7 Calls;
    // the input's own value there is the Value test's, 15.6.4), Chain over Content.
    // A position whose Origin the call's Subst brings can also hold a source whose fit relates such Origins the premises do not prove: the
    // conjunction (retentionPairs) of a pair per layer of the source's path the closure misses and the position (Conjunction) is the
    // solved Origins' to judge (DECISIONS R4-M154, Revisions 2 and 10).
    private void Storages(
        OriginPremiseEnvironment environment,
        (int[] First, int[] Next, int[] SourceFirst, int[] SourceNext, int[] Marks) links,
        List<RetentionKind[]> kept,
        int output)
    {
        var kinds = this.retentionKinds;
        kinds.Clear();
        for (var s = 0; s < this.storageTypes.Count; s++)
        {
            if (s == this.retentionKindPool.Count)
            {
                this.retentionKindPool.Add(new RetentionKind[kept[0].Length]);
            }

            Array.Clear(this.retentionKindPool[s]);
            kinds.Add(this.retentionKindPool[s]);
        }

        this.retentionHidden.Clear();
        this.retentionFilled = false;
        foreach (var (storage, node, signature) in this.storageTargets)
        {
            foreach (var reached in this.Closure(environment, node))
            {
                for (var atom = reached < links.First.Length ? links.First[reached] : -1; atom >= 0; atom = links.Next[atom])
                {
                    var input = this.retentionAtoms[atom].Input;
                    var admitted = input < 0 || this.Admits(signature, input); // In the callee's kept Type's binding, an admitted input only.
                    kinds[storage][input] |= this.retentionAtoms[atom].Kind is null || !admitted ? RetentionKind.None : RetentionKind.Content;
                }
            }
        }

        this.retentionPairs.Clear();
        this.retentionConjuncts.Clear();
        foreach (var (storage, node, type, signature) in this.storagePositions)
        {
            // R's exclusion (SPEC 15.6.4): storage written through a raw layer input `output` holds never retains that input's own value.
            var excluded = output >= 0 && !this.storageBorrowed.Contains(storage) ? output : -1;
            if (type is not null && storage != AbstractStorage && Abstract(type) && !this.Parametric(signature))
            {
                this.FilledKinds(kinds[storage], type, null, excluded); // An abstract part outside the kept Type's binding, whatever its node.
            }

            if (node < 0)
            {
                this.UnnamedKinds(kinds[storage], type, signature, excluded);
                continue;
            }

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
                    if (!this.Admits(signature, found.Input))
                    {
                        continue;
                    }

                    var held = this.Fitted(found, links.Marks, stamp) && this.CanHold(type, found);
                    kinds[storage][found.Input] |= held ? RetentionKind.Value : RetentionKind.Content;
                }
            }

            var position = this.Instantiated(environment, node);
            for (var s = 0; position is not null && s < this.retentionSources.Count; s++)
            {
                var found = this.retentionSources[s];
                if (this.Admits(signature, found.Input) &&
                    this.Conjunction(environment, found, links.Marks, stamp, position) is var (start, count) && count > 0)
                {
                    this.Conjoined(storage, found.Input, start, count, this.CanHold(type, found));
                }
            }
        }

        foreach (var (storage, signature) in this.storageBound)
        {
            this.HiddenKinds(kinds[storage], signature, output); // The root of a binding of the callee's kept Type: its rows in the callee's terms, once.
        }

        for (var s = 0; s < kinds.Count; s++)
        {
            var (storage, held) = (kinds[s], kept[s]);
            var chained = (this.retentionHolding[s] & Holding.Chained) != 0;
            for (var i = 0; i < storage.Length; i++)
            {
                var admitted = chained && (storage[i] != RetentionKind.None || this.Conjoins(s, i));
                var kind = ((storage[i] & RetentionKind.Value) != 0 ? RetentionKind.Value : storage[i]) | held[i];
                kind |= admitted ? this.retentionChained[i] & RetentionKind.Chain : RetentionKind.None; // Its own value: the Value test's.
                storage[i] = (kind & RetentionKind.Chain) != 0 ? kind & ~RetentionKind.Content : kind;
            }
        }
    }

    // Whether a storage has a conjunction for an input (retentionPairs): the call's solved Origins may fit the input there.
    private bool Conjoins(int storage, int input)
    {
        foreach (var (s, i, _, _) in this.retentionPairs)
        {
            if (s == storage && i == input)
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 15.6.4's abstract-storage sentence in the callee's terms (the owner's O-353 (a), O-360 (a); PLAN G935): the storage that holds
    // the root of a binding of the called function's own abstract Type `hidden` (BU-202's kept Type, StorageAt.Signature; storageBound)
    // holds input i where i's declared Type, or a part it holds by value, is that Type (Value), where i's declared referent or pointee
    // holds it (Content; Chain further), or where a callable the contract hands the body may retain i there (FilledKinds); never by the
    // binding's Origins, which are the caller's, nor by the Type the call binds it to: every binding gets the declared record's rows,
    // and no storage past a layer of the binding gets one (a raw layer the abstract Type does not show adds no reach of its own).
    // `excluded`, the input this storage is written through (its output), keeps nothing here: its own value never (15.6.4: a pointer is
    // never part of its own pointee), and what a read or chain of reads through it gives is that storage's own content.
    private void HiddenKinds(RetentionKind[] kinds, BoundType hidden, int excluded)
    {
        var known = this.Hidden(hidden, kinds.Length);
        for (var i = 0; i < kinds.Length; i++)
        {
            // An input with sources takes its own value's row from the instantiated tests that Admits lets through (their Origin fits and
            // pairs stand); a raw input, or one holding a raw part by value, the hold walk's source, takes it here.
            var kind = this.RawInput(i) ? known[i] : known[i] & ~RetentionKind.Value;
            kinds[i] |= i == excluded ? RetentionKind.None : kind;
        }

        this.FilledKinds(kinds, null, hidden, excluded);
    }

    // The one fills arm (Revision 1's R1.3; SPEC 15.6.4 "or where a callable the instantiated contract hands the callee's body ... may
    // retain the argument there ..., through its result or its writes"): at an abstract part, the storage `kinds` is of keeps each
    // source the premise closure does not decide (retentionUndecided: a source that names no Origin by its own value, R; a raw source by
    // its kind) where a handed callable may retain that input there (Filled), and through a write also where a value of the Type reaches
    // past its hidden references (the Abstract storage, whose entry Written then forms). Outside the kept Type's binding a raw source's
    // fill is the hold walk's (Held), which walks no hidden binding.
    private void FilledKinds(RetentionKind[] kinds, BoundType? type, BoundType? signature, int excluded)
    {
        var (reached, own) = (this.retentionKinds[AbstractStorage], this.Parametric(signature));
        for (var i = 0; i < kinds.Length; i++)
        {
            var kind = own ? this.retentionUndecided[i].Raw | this.retentionUndecided[i].Free : this.retentionUndecided[i].Free;
            var fill = kind == RetentionKind.None || i == excluded ? Fill.None : this.Filled(type, signature, i);
            if (fill != Fill.None)
            {
                kinds[i] |= kind;
                reached[i] |= fill == Fill.Writes ? kind : RetentionKind.None;
                this.retentionFilled |= fill == Fill.Writes;
            }
        }
    }

    // HiddenKind per input for one kept abstract Type (each computed once per Storages: retentionHidden).
    private RetentionKind[] Hidden(BoundType hidden, int inputs)
    {
        ref var known = ref CollectionsMarshal.GetValueRefOrAddDefault(this.retentionHidden, hidden, out var met);
        if (!met)
        {
            known = new RetentionKind[inputs];
            for (var i = 0; i < inputs; i++)
            {
                this.retentionHiddenSeen.Clear();
                known[i] = this.HiddenKind(this.SignatureInput(i), hidden, RetentionKind.Value, 0);
            }
        }

        return known!;
    }

    // Whether input i is a raw pointer or holds a raw layer by value (a hold-walk source, retentionRawSources).
    private bool RawInput(int input)
    {
        foreach (var (raw, _) in this.retentionRawSources)
        {
            if (raw == input)
            {
                return true;
            }
        }

        return this.retentionArguments[input] is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw };
    }

    // Whether the instantiated tests (the closure, CanHold, pairs) may give a position input `input`'s row: anywhere but in the callee's kept
    // abstract Type's binding (Parametric), where only an input the callee's terms admit (HiddenKind: it is, holds or reads that Type) or a
    // callable the contract hands the body may retain there (Filled) keeps it, with the instantiated Origin fits and pairs (R1.3). Both
    // read the walk state's Signature, the kept Type, never the instantiated part there: every binding admits the same inputs.
    private bool Admits(BoundType? signature, int input)
        => !this.Parametric(signature) || this.Hidden(signature!, this.retentionArguments.Count)[input] != RetentionKind.None ||
            this.Filled(null, signature, input) != Fill.None;

    // How a declared Type holds an abstract Type (HiddenKinds): `kind` where it is that Type, Origins erased; through a part by value the
    // same kind, through a borrow or raw layer a read (Content), and past a second layer a chain (Chain); each (Type, kind) entered at
    // the shallowest depth it is met at (Enters), so the depth bound cuts no row a shallower route reaches. A raw
    // layer stating an Origin that steps Exclusive reads what each borrow that Origin belongs to in the declared signature admits (SPEC
    // 5.7 Calls, 15.6.4 "with an Origin, as the borrow that Origin belongs to would"; RawContent's members, over the declared inputs): its
    // members' referents are read through it too (`p: raw/u8 during a` beside `n: uniq/(K<T> during a)` reads K's kept T).
    private RetentionKind HiddenKind(BoundType? declared, BoundType hidden, RetentionKind kind, int depth)
    {
        if (declared is null || depth > StorageBound)
        {
            return RetentionKind.None;
        }

        ref var shallowest = ref CollectionsMarshal.GetValueRefOrAddDefault(this.retentionHiddenSeen, (declared, kind), out var met);
        if (!Enters(ref shallowest, met, depth))
        {
            return RetentionKind.None; // Entered at this depth or shallower: what lies below it is counted there (the bounded walks' rule).
        }

        if (this.Fits(declared, hidden) || this.Identical(declared, hidden))
        {
            return kind;
        }

        var start = this.retentionHeld.Count;
        var found = RetentionKind.None;
        if (this.Holdings(declared, null, null, this.retentionHeld))
        {
            var end = this.retentionHeld.Count;
            for (var i = start; i < end; i++)
            {
                var entry = this.retentionHeld[i];
                var layer = (entry.Flags & HoldFlags.Raw) != 0 || entry.Step is HoldStep.Shared or HoldStep.Exclusive;
                var next = this.ByValue(declared, entry) ? kind : layer ? (kind == RetentionKind.Value ? RetentionKind.Content : RetentionKind.Chain) : RetentionKind.None;
                found |= next == RetentionKind.None ? RetentionKind.None : this.HiddenKind(entry.Part, hidden, next, depth + 1);
            }
        }

        this.retentionHeld.RemoveRange(start, this.retentionHeld.Count - start);
        if (ReferenceTypes.IsPointer(declared) && this.retentionDeclared.Count > 0 && this.RawStepAt(declared, null, null, declared) == HoldStep.Exclusive)
        {
            var first = this.rawMembers.Count;
            this.retentionLimit |= !this.RawMembers(OwnedOrigin(declared)!, CollectionsMarshal.AsSpan(this.retentionDeclared)[..^1], this.rawMembers);
            var last = this.rawMembers.Count;
            for (var i = first; i < last; i++)
            {
                found |= this.HiddenKind(this.rawMembers[i].Referent, hidden, kind == RetentionKind.Value ? RetentionKind.Content : RetentionKind.Chain, depth + 1);
            }

            this.rawMembers.RemoveRange(first, last - first);
        }

        return found;
    }

    // SPEC 8.2.3, 15.6.4 ("only where the premises make it one"): whether the callee's identity premises make a declared Type the kept
    // abstract Type `hidden`: a member of its identity class in the callee's view (Binding.TypeIdentity's classes, as the key walk's View
    // reads them), Origins erased (rvPremBad: `T is V`).
    private bool Identical(BoundType declared, BoundType hidden)
    {
        if (OwnAbstract(hidden) is not { } function)
        {
            return false;
        }

        this.viewMembers.Clear();
        this.IdentityMembers(hidden, this.ConstraintScope(function), this.viewMembers);
        foreach (var member in this.viewMembers)
        {
            if (this.Fits(member, declared))
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 15.6.4 ("or where a callable the instantiated contract hands the callee's body ... may retain the argument there by this section
    // applied to that callable's signature, through its result or its writes"; Revision 1's R1.3, per input): how a callable the contract
    // hands the body (a Callable bound or a requirement; a Function part of an input or the result) that takes input `input` (Takes) may
    // retain it at a position. The Types read are the walk state's (D1): at a position in the binding of the callee's kept Type
    // (`signature`, Parametric) that Type, the input's declared Type and the callables as the callee's declaration states them
    // (retentionOwnHanded, retentionOwnCallables), so the answer is the declared record's for every binding of the Type; elsewhere the
    // instantiated position `type`, input and callables (retentionHanded, retentionCallables). Each (position, input) once per computation.
    private Fill Filled(BoundType? type, BoundType? signature, int input)
    {
        if (this.retentionErased)
        {
            return Fill.Writes; // The computation's own erased callee may retain any input at any abstract part (BU-66's `null` fill).
        }

        var own = this.Parametric(signature);
        var (position, argument) = own ? (signature, this.SignatureInput(input)) : (type, this.retentionArguments[input]);
        if (position is null || argument is null)
        {
            return Fill.None;
        }

        if (!this.retentionFills.TryGetValue((position, input, own), out var fill))
        {
            var (handed, callables) = own ? (this.retentionOwnHanded, this.retentionOwnCallables) : (this.retentionHanded, this.retentionCallables);
            for (var k = 0; fill != Fill.Writes && Handed(handed, callables, k, out var callable); k++)
            {
                if (callable is not null && this.Takes(callable, argument))
                {
                    fill = this.Writes(callable, position) ? Fill.Writes : this.Returns(callable, position) ? Fill.Result : fill;
                }
            }

            this.retentionFills.Add((position, input, own), fill);
        }

        return fill;
    }

    // Whether a handed callable takes an input of Type `argument` (Filled): the input holds the callable itself (what its environment keeps,
    // its result or writes may keep), or names Origins (the closure decides it: the handed signature's relations, R4-M52, Revision 3), or
    // one of the callable's parameters holds the input's Type, Origins erased, or is a raw pointer as the input is (BU-66).
    private bool Takes(BoundType callable, BoundType argument)
    {
        var pointer = argument is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw };
        if (this.Hands(argument, callable) || (argument.CarriesAnyOrigin && !pointer))
        {
            return true;
        }

        var parameters = callable.Components[0];
        for (var j = 0; !ReferenceEquals(parameters, BoundType.Unit) && j < parameters.Components.Count; j++)
        {
            var parameter = parameters.Components[j];
            this.retentionWalked.Clear();
            if ((pointer && parameter is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw }) || this.Occurs(parameter, argument))
            {
                return true;
            }
        }

        return false;
    }

    // Whether a value of an input's Type hands the body a callable (Filled; SPEC 15.6.4 "a callable value an input holds"): one of the
    // callables TyCtxt.HandedCallables lists for the Type, Origins erased: a Function part at any depth, a nominal Field's included
    // (`Maker<E, T>`'s `make: (E) -> T`), which the Type's components alone do not name.
    private bool Hands(BoundType argument, BoundType callable)
    {
        var erased = this.types.Erase(callable);
        foreach (var handed in this.types.HandedCallables(argument))
        {
            if (ReferenceEquals(this.types.Erase(handed), erased))
            {
                return true;
            }
        }

        return false;
    }

    // Whether a handed callable's result holds a position's Type (Fill.Result): the position occurs in it, or it occurs in the position
    // (an associated projection's base: BU-66's fill at a carrier that occurs in the part).
    private bool Returns(BoundType callable, BoundType position)
    {
        this.retentionWalked.Clear();
        if (this.Occurs(callable.Components[1], position))
        {
            return true;
        }

        this.retentionWalked.Clear();
        return this.Occurs(position, callable.Components[1]);
    }

    // Whether a handed callable may write a value of a position's Type (Fill.Writes; SPEC 15.6.4 "never through a shared Loan", 5.2.1
    // condition 3): one of its parameters reaches such a value through no shared layer (WrittenThrough).
    private bool Writes(BoundType callable, BoundType position)
    {
        var parameters = callable.Components[0];
        this.retentionWalked.Clear();
        for (var j = 0; !ReferenceEquals(parameters, BoundType.Unit) && j < parameters.Components.Count; j++)
        {
            if (this.WrittenThrough(parameters.Components[j], position, 0))
            {
                return true;
            }
        }

        return false;
    }

    // Whether a value of a Type is, or reaches through no shared layer, a value of `position`'s Type, Origins erased: by the entries
    // the hold walk steps through (Holdings): a part held by value, an exclusive layer's referent, a raw layer's pointee where its step is
    // not Shared; never past a shared borrow or a shared handle's payload, which a call reaches for reads only. Each Type entered at the
    // shallowest depth it is met at (Enters, retentionWalked).
    private bool WrittenThrough(BoundType? type, BoundType position, int depth)
    {
        if (type is null || depth > StorageBound)
        {
            return false;
        }

        ref var shallowest = ref CollectionsMarshal.GetValueRefOrAddDefault(this.retentionWalked, (type, null, null), out var met);
        if (!Enters(ref shallowest, met, depth))
        {
            return false;
        }

        if (this.Fits(type, position))
        {
            return true;
        }

        var (start, found) = (this.retentionHeld.Count, false);
        if (this.Holdings(type, null, null, this.retentionHeld))
        {
            var end = this.retentionHeld.Count;
            for (var i = start; !found && i < end; i++)
            {
                var entry = this.retentionHeld[i];
                var shared = entry.Step == HoldStep.Shared || (entry.Flags & HoldFlags.ReadOnly) != 0;
                var through = this.ByValue(type, entry) || entry.Step == HoldStep.Exclusive || (entry.Flags & HoldFlags.Raw) != 0;
                found = !shared && through && this.WrittenThrough(entry.Part, position, depth + 1);
            }
        }

        this.retentionHeld.RemoveRange(start, this.retentionHeld.Count - start);
        return found;
    }

    // R (PLAN G935; the owner's O-359 (a); SPEC 15.6.4 "decided per argument, never per Origin"): a position that names no Origin holds each
    // source that names none (an Origin-free input or a part such an input holds by value) where it can hold its value: both are leaves
    // (R1.2), so the position is the source's Type, Origins erased (Fits); an abstract part keeps such a source by a callable's fill
    // (FilledKinds), whatever its node; never by a raw layer's pointee holding any Type (CanHold's RawStorage arm is a borrow formed
    // within raw storage, not a value stored at the layer: a raw layer the call writes keeps its rows by rule A, whose exclusion of the
    // written-through argument stands: keeperTupOk); `excluded` (an input written through, R's exclusion) none.
    private void UnnamedKinds(RetentionKind[] kinds, BoundType? type, BoundType? signature, int excluded)
    {
        foreach (var source in this.retentionSources)
        {
            if (source.Node < 0 && source.Input != excluded && (kinds[source.Input] & RetentionKind.Value) == 0 && type is not null &&
                this.Admits(signature, source.Input) && this.Fits(type, source.Own!))
            {
                kinds[source.Input] |= RetentionKind.Value;
            }
        }
    }

    // The hold walk's kinds of the walk made (retentionKept, one array per storage, none set), for the computation's input count.
    private List<RetentionKind[]> Kept(int inputs)
    {
        var kept = this.retentionKept;
        if (kept.Count > 0 && kept[0].Length != inputs)
        {
            kept.Clear();
        }

        for (var s = 0; s < this.storageTypes.Count; s++)
        {
            if (s == kept.Count)
            {
                kept.Add(new RetentionKind[inputs]);
            }

            Array.Clear(kept[s]);
        }

        return kept;
    }

    // An output's record from the storages' kinds and pairs (Storages): its root's, the storages past it joined by Origin-erased Type, each
    // storage in one entry (an Origin-only difference between them is in their pairs, DECISIONS R4-M154, Revision 2), the Abstract one
    // where the walk met an abstract part or a handed callable's write keeps a row there (retentionFilled), and All, their join with every one of their pairs; every one untied where the output is; null
    // where the call keeps nothing there. Read in the callee's terms, it names the callee (Kept) and whether a handed callable writes a
    // kept Type the walk met (Handed, HandsWriter).
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

        var hidden = this.storagePositions.Exists(static x => x.Storage == AbstractStorage) || this.retentionFilled;
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
        var kept = this.retentionParametric ? this.retentionCallee : null;
        return new(output, own, [.. reached], new(all, this.Pairs(all, joined), untied), kept, kept is not null && this.HandsWriter());
    }

    // OutputRetention.Handed (SPEC 15.6.4 "or where a callable the instantiated contract hands the callee's body ... through its result
    // or its writes"; PLAN G935, C-370): whether a callable the callee's declaration hands its body (the list Filled reads in a kept
    // binding: retentionOwnHanded, retentionOwnCallables) writes a kept Type the output's walk met (storageKept), by the one test Filled's
    // writes take (Writes); input-agnostic. One Writes walk per (kept Type, callable); none where the contract hands no callable.
    private bool HandsWriter()
    {
        foreach (var kept in this.storageKept)
        {
            for (var k = 0; Handed(this.retentionOwnHanded, this.retentionOwnCallables, k, out var callable); k++)
            {
                if (callable is not null && this.Writes(callable, kept))
                {
                    return true;
                }
            }
        }

        return false;
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
    // emits with the Type its declaration writes there and the instance holding it, DECISIONS BU-52; each position once per lent bit and
    // storage) and what it keeps, by storage (retentionKept; C-352: the rows of a raw layer belong to the storage holding it, never to
    // every storage of the output): rule A's rows at a written unstated layer (WrittenUnstated, the transfer read once per contract; formed
    // once per output, whole where the storage holds such a layer, a declaration's own layer reaches it or it holds a part no Type bounds,
    // Holding.Any, and through Storages' admission where it holds only Loan-carrying positions past a bounding one, Holding.Chained), the
    // raw arguments at a lent one (R′: SPEC's "the raw arguments", the raw sources at their own layer, never a wrapped one), and at a
    // stated layer the raw sources its Origin admits (StatedFits); whether it met a layer stepping Unstated or Static, not lent, which unties
    // the output. A published transfer operation writes only its own outputs (TransferWrites, by the declaring-identity key: an Iterator's
    // `next` writes each), so at another it neither forms rule A's rows nor unties it: the output has no record from these layers.
    private bool Hold(
        OriginPremiseEnvironment environment,
        int output,
        ReadOnlySpan<(HoldAt At, int Storage)> holds,
        ReadOnlySpan<BoundType> inputs,
        (Transfer Key, FunctionKoto? Callee, BindingScope? Premises) call,
        ref Transfer? transfer)
    {
        this.retentionMet.Clear();
        this.retentionStated.Clear();
        var count = this.storageTypes.Count;
        var met = Reused(ref this.retentionHolding, count);
        Array.Clear(met, 0, count);
        foreach (var (at, storage) in holds)
        {
            met[storage] |= this.Held(at, output, storage, lent: false);
        }

        this.StatedFits(environment, output);
        var writes = TransferWrites(call.Key, call.Callee?.BoundSymbol, output);
        var reach = Holding.None; // What the output's written unstated layers reach: Unbounded, every storage; Unstated, those holding Loans.
        for (var s = 0; writes && s < count; s++)
        {
            reach |= (met[s] & Holding.Unstated) != 0 ? met[s] & (Holding.Unstated | Holding.Unbounded) : Holding.None;
        }

        var untied = false;
        var rows = Reused(ref this.retentionChained, inputs.Length);
        Array.Clear(rows);
        var ruled = false; // Rule A's rows are the output's, the same in every storage they reach: formed once.
        for (var s = 0; s < count; s++)
        {
            // Storage of the callee's kept Type takes no row of rule A: it holds no layer the call may write as one (its rows are
            // HiddenKinds', the callee's terms; SPEC 15.6.4, the owner's O-360 (a)), whatever the holders its binding addresses reach.
            var whole = ((met[s] & (Holding.Unstated | Holding.Any)) != 0 || (reach & Holding.Unbounded) != 0) && !(met[s] == Holding.None && this.KeptStorage(s));
            if (writes && (reach & Holding.Unstated) != 0 && (whole || (met[s] & Holding.Loans) != 0))
            {
                if (!ruled)
                {
                    transfer ??= call.Key == Transfer.None ? this.IteratorTransfer(call.Callee?.BoundSymbol, inputs, call.Premises) : call.Key;
                    this.WrittenUnstated(rows, output, inputs, transfer.Value, call.Callee?.BoundSymbol);
                    ruled = true;
                }

                if (whole)
                {
                    Join(this.retentionKept[s], rows);
                }
                else
                {
                    met[s] |= Holding.Chained; // Through its Loan-carrying positions only: Storages joins the rows its Origin tests admit.
                }
            }
            else if ((met[s] & Holding.Lent) != 0)
            {
                foreach (var (input, kind) in this.retentionRawSources)
                {
                    this.retentionKept[s][input] |= kind is null ? RetentionKind.Value | RetentionKind.Chain : RetentionKind.None; // R′: SPEC's "the raw arguments".
                }
            }

            untied |= writes && (met[s] & (Holding.Unstated | Holding.Static)) != 0;
        }

        for (var s = 0; s < count; s++)
        {
            Join(this.retentionKept[AbstractStorage], this.retentionKept[s]); // What an abstract part hides may be any Type: every storage's rows.
        }

        return untied;
    }

    // Whether a storage of the output's walk is the root of a binding of the callee's kept Type (storageBound): with no hold of its own
    // (Hold's `met` is None there), the storage is that Type's and nothing else's.
    private bool KeptStorage(int storage)
    {
        foreach (var (bound, _) in this.storageBound)
        {
            if (bound == storage)
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 15.6.4 "with an Origin, as the borrow that Origin belongs to would", 5.7 Fits and claims (the owner's O-329 (a); C-352): at each
    // raw layer stating an Origin the hold walk met (retentionStated), in its storage, each raw source but `output` (a pointer is never part
    // of its own pointee) by its kind where one of its layers states an Origin that the stated one's premise closure reaches, whatever the
    // pointee Types (a conversion between raw pointer Types keeps the Origin, SPEC 5.4; DECISIONS BU-66's P2): a source fits there. A layer
    // stating none (retentionRawStated has no entry) fits a stated layer only by a claim, which promises that storage.
    private void StatedFits(OriginPremiseEnvironment environment, int output)
    {
        foreach (var (storage, origin) in this.retentionStated)
        {
            var target = this.PremiseNode(environment, origin);
            var stamp = ++this.retentionStamp;
            var closure = this.Closure(environment, target);
            var marks = Reused(ref this.retentionMarks, environment.Closure.NodeCount + 1);
            foreach (var reached in closure)
            {
                marks[reached] = stamp;
            }

            foreach (var (input, stated) in this.retentionRawStated)
            {
                if (input != output && this.PremiseNode(environment, stated) is var node && node < marks.Length && marks[node] == stamp)
                {
                    this.KeepRaw(storage, input);
                }
            }
        }
    }

    // A raw source's kinds in a storage's kept rows: the input's own value at its own layer, else its entry's kind (DECISIONS BU-66, A2).
    private void KeepRaw(int storage, int input)
    {
        foreach (var (source, kind) in this.retentionRawSources)
        {
            this.retentionKept[storage][input] |= source == input ? kind ?? RetentionKind.Value : RetentionKind.None;
        }
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

    // SPEC 15.6.4, 5.7 Calls (DECISIONS R4-M116 items 2 and 5, R-B2, BU-30, BU-35, BU-40), the hold walk: a position of `output` the call may
    // write or lend, a hold (Hold's: the result's whole Type, or a position of input j's writable storage) or a position below one, in
    // `storage` (the hold's: a value stored at a position brings its whole Type there), with `written`, the Type a struct's Field or an enum's
    // Case payload declares there while it is a Tuple, a fixed array or a raw layer (Written; null elsewhere: off a declaration, or below a
    // Type parameter or another part it does not write inline), the instance holding it there (`holder`, whose case a declared slot's step
    // reads, SPEC 15.3.5; DECISIONS BU-44), and whether it is `lent`: below a borrow in the result (BorrowReferent: a borrow layer, a pair
    // layer admitting only borrows, a Slice's or UniqSlice's elements) or below a result raw layer stating an input's or slot's Origin
    // (stepping Exclusive or Shared: it retains as that borrow would, SPEC 5.7 Calls, so what it addresses is that borrow's storage, 5.2.1;
    // PLAN G255's C1). Each raw source other than `output` retains its own value at a raw layer stating an Origin or `static` where it fits
    // that Origin, whatever its pointee Type (it retains as the borrow its Origin belongs to would, SPEC 15.6.4, and a raw cast keeps the
    // address and the Origin, 5.4; StatedFits judges the fit by the premise closure), at a `during self` layer or a pair layer's raw case, which
    // state no Origin the closure judges, as before, and at an abstract part only where a callable the call may run may retain it there
    // (Filled, per input: rule 3, DECISIONS BU-66, PLAN G935 R1.3; no premise makes an abstract part such storage); at a raw layer without an Origin rule A or R′
    // decides it (RetentionRows); a pointer is never part of its own pointee. A raw layer steps by DeclaredStep (its declaration's cap where
    // `written` states the layer, its Origin's kind elsewhere, DECISIONS BU-24): Unstated is what the call writes there (Holding.Unstated) or
    // lends (Holding.Lent), and Static is storage the body does not own (Holding.Static, lent or not). A value stored at a writable position
    // brings its whole Type (BU-1), so the walk goes below every layer that stores (IsCallable), as RetentionParts' writable positions do, and
    // into a struct's or enum's stored content. What a position gives depends on its Type, `written`, its holder, `lent` and `storage` alone
    // and only accumulates, so each position is walked at most once per lent bit, storage and output (retentionMet): the work is linear in the
    // distinct positions per storage, not in the paths to them through the interned Types (V16). A struct or enum whose storage is a Limit, a
    // growing one among them (PrepareTypeStorage's limits, DECISIONS BU-12), makes the contract CallRetention.Limit; Rejected storage is
    // walked as far as it substitutes (Readable, BU-36), and it and a part that does not (null) may be any Type: Loans and Any, failing
    // closed (Holding; superseding BU-36's "holds nothing" for the Chained verdict).
    private Holding Held(in HoldAt at, int output, int storage, bool lent)
    {
        if (at.Type is not { } type)
        {
            return Holding.Loans | Holding.Any; // A part that does not substitute, or storage no Type states: it may be any Type.
        }

        var written = Written(at.Written);
        var holder = written is null ? null : at.Holder;
        if (!this.retentionMet.Add((new(type, written, holder), lent, storage)))
        {
            return Holding.None;
        }

        var run = this.HeldRun(type, written, holder, out var complete);
        if (!complete)
        {
            this.retentionLimit = true;
            return Holding.None;
        }

        HoldStep? step = null;
        var refers = run.End > run.Start;
        var stepped = false;
        var unbounded = false;
        var any = this.PrepareTypeStorage(type) != StorageOutcome.Prepared; // Rejected storage: its failed parts may be any Type.
        var loans = any;
        var hidden = false;
        for (var i = run.Start; i < run.End; i++)
        {
            var entry = this.retentionHeld[i];
            var layer = (entry.Flags & HoldFlags.Raw) != 0;
            step ??= layer ? entry.Step : null;
            refers &= Dereference(entry);
            stepped |= layer && entry.Step != HoldStep.Address;
            unbounded |= layer && entry is { Step: HoldStep.Address, Holds: HoldContent.Any }; // A layer a declaration wrote (TyCtxt.RawStated).
            loans |= !layer && !(entry.PassesOn && entry.Holds == HoldContent.Its); // Hold.PassesOn, the row MirTypes.HoldsLoans reads.
            hidden |= entry.Step == HoldStep.Abstract;
        }

        var stated = stepped && type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw } &&
            OwnedOrigin(type) is { Kind: not OriginKind.Unstated } origin ? origin : null;
        if (stated is not null && this.retentionRawSources.Count > 0)
        {
            this.retentionStated.Add((storage, stated)); // StatedFits keeps the raw sources that fit it.
        }

        var holds = stepped && stated is null;
        foreach (var (input, kind) in this.retentionRawSources)
        {
            var kept = input != output && (holds || (hidden && this.Filled(type, null, input) != Fill.None)); // An abstract part: a callable's fill, per input (R1.3).
            this.retentionKept[storage][input] |= kept ? kind ?? RetentionKind.Value : RetentionKind.None;
        }

        var met = loans ? Holding.Loans : Holding.None;
        met |= any ? Holding.Any : Holding.None;
        met |= step switch
        {
            HoldStep.Static => Holding.Static,
            HoldStep.Address when lent => Holding.Lent,
            HoldStep.Address when unbounded => Holding.Unstated | Holding.Unbounded,
            HoldStep.Address => Holding.Unstated,
            _ => Holding.None,
        };
        var below = lent || (output == CallRetention.ResultOutput && refers);
        if (type.Symbol?.LibraryDeclaration == KimiDeclarationId.UniqSlice)
        {
            this.retentionLimit |= !this.StoredHoldings(type, holder, this.retentionHeld); // Its raw Field, for rule A's raw sources until U3b reads entries.
            run.End = this.retentionHeld.Count;
        }

        for (var i = run.Start; i < run.End; i++)
        {
            var part = this.retentionHeld[i];
            met |= part.Step == HoldStep.Abstract ? this.HeldComponents(type, output, storage, below)
                : this.Held(new(part.Part, part.Written, part.Holder), output, storage, below);
        }

        this.retentionHeld.RemoveRange(run.Start, run.End - run.Start);
        return met;
    }

    // An abstract part's components (a projection's base and Contract, an adaptation's cases), which rule A's hold walk goes through though
    // row 17 publishes none: dev's answer, removed at U3b, where rule A reads entries (G416).
    private Holding HeldComponents(BoundType type, int output, int storage, bool lent)
    {
        var met = Holding.None;
        for (var i = 0; i < type.Components.Count; i++)
        {
            met |= this.Held(new(type.Components[i], null, null), output, storage, lent);
        }

        return met;
    }
}
