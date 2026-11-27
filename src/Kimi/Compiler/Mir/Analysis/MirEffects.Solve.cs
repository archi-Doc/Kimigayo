// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Numerics;
using System.Runtime.InteropServices;
using Kimi.Diagnostics;
using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

// A4's summaries and verdicts (DECISIONS R4-M22): the least fixed point of the summaries over one worklist, each node re-evaluated when a
// summary it read grows, then P5's static keys joined (Spread in MirEffects.Keys, R4-M38), then each obligation judged with its remaining
// atoms resolved by its definition's premises: an owing body per view, a Callable binding as a call through it.
// A round's rule (PLAN G951): Compose is the one read of a body's nodes, and a read of a body A2's table does not hold (MirDropTable.Holds:
// a body built since the driver's last step, which has no local facts) misses: it adds nothing, and the round goes on to its end, so it
// demands every body it reads; the report holds its obligations' problems until it ends (MirReport.Begin, the round rule's one commit,
// which the instance collector's runs share): a clean round commits them, a missed one drops them.
internal sealed partial class MirEffects
{
    private readonly List<EffectObligation> rows = [];
    private readonly List<(BindingSymbol Target, Subst Instance)> targets = []; // What requirements on Types that are not abstract run, a stack.
    private readonly Dictionary<BoundType, byte> glued = new(ReferenceEqualityComparer.Instance); // One evaluation's destruction walks' slots (MirDepths).
    private byte[] classes = []; // Each node's summary: its classes,
    private int[] atomStarts = []; // and its atoms, a segment of atoms (a node that grows appends a new segment).
    private int[] atomCounts = [];
    private Atom[] atoms = [];
    private Atom[] scratch = []; // The atoms of the current evaluation.
    private int[] queue = [];
    private ulong[] queued = [];
    private int[] readers = []; // The last node that read each node, so consecutive reads record one dependency,
    private int[] dependentHeads = []; // and each node's readers, a list in dependents (whom the worklist re-evaluates);
    private (int Node, int Next)[] dependents = [];
    private int[] readHeads = []; // the same reads forward, each node's successors in the read graph (what it runs, MirEffects.Keys):
    private (int Node, int Init, int Next)[] reads = []; // a node it read (Init -1) or, appended by Spread, a view of an entry's initializer
    private int readCount; // (Init the entry's number; Node -1 for an initializer with a validator finding, which has no node: Derived).
    private MirProgram program = null!;
    private MirReport report = null!; // The round's report, which holds its obligations' problems until the round ends.
    private BindingSymbol? definition; // The definition whose premises resolve atoms: the evaluated node's, or an obligation's.
    private int reader; // The evaluated node, -1 while judging an obligation, whose atoms are resolved at once.
    private int atomCount;
    private int scratchCount;
    private int dependentCount;
    private bool missed; // Whether the round read a body A2's table does not hold.

    /// <summary>Gets the node evaluations of the last <see cref="Run"/>, linear in the program's calls (V16).</summary>
    internal int Evaluations { get; private set; }

    // The current evaluation's classes: what it found, with Deep where its destruction walks met a part past the bound whose Type they
    // never entered within it (MirDepths: a slot left Past).
    private byte Found => (byte)(this.found | (this.glued.ContainsValue(MirDepths.Past) ? Deep : 0));

    /// <summary>Solves the summaries of the nodes <see cref="Local"/> recorded (driver step 3, a round) and judges each obligation whose
    /// subject is in the root range (the library's are the library check's, PLAN G951 E3): per view of its target an effect that fails its
    /// rule (a <c>confined</c> bound's environment or unknown effect, a platform member's static access or unknown effect) is its code at
    /// its subject, else a limit <c>Unsupported_Kd</c>; a failure the premises decide covers every view. A round that read a body A2's
    /// table does not hold missed and adds no problem; the driver steps the bodies built since its last step and runs the next
    /// round.</summary>
    /// <param name="program">The program whose bodies were recorded.</param>
    /// <param name="report">The problems of the program version.</param>
    /// <param name="rootModules">The root range: the obligations whose subject's module is below it are judged (DECISIONS R5-M168).</param>
    /// <returns>Whether the round was clean and added its problems.</returns>
    internal bool Run(MirProgram program, MirReport report, int rootModules)
    {
        (this.program, this.report, this.missed, this.excludes) = (program, report, false, false);
        report.Begin(MirProblem.NoBody);
        (this.atomCount, this.dependentCount, this.readCount, this.Evaluations, this.Walked) = (0, 0, 0, 0, 0);
        var count = this.NodeCount;
        MirDataflow.Grow(ref this.classes, count);
        MirDataflow.Grow(ref this.atomStarts, count);
        MirDataflow.Grow(ref this.atomCounts, count);
        MirDataflow.Grow(ref this.queue, count);
        MirDataflow.Grow(ref this.queued, (count + 63) >> 6);
        MirDataflow.Grow(ref this.readers, count);
        MirDataflow.Grow(ref this.dependentHeads, count);
        MirDataflow.Grow(ref this.readHeads, count);
        var (words, slots, classWords) = (((2 * this.fieldIds.Count) + 63) >> 6, (this.fixedIds.Count + 63) >> 6, this.initIds.Count > 0 ? 1 : 0);
        var row = words + slots + classWords;
        MirDataflow.Grow(ref this.keys, count * row);
        MirDataflow.Grow(ref this.keyScratch, row);
        MirDataflow.Grow(ref this.excluding, (this.initIds.Count + 63) >> 6);
        MirDataflow.Grow(ref this.reached, count);
        MirDataflow.Grow(ref this.reaching, count);
        this.keys.AsSpan(0, count * row).Clear();
        (this.keyWords, this.slotWords, this.classWords, this.rowWords) = (0, 0, 0, 0); // No key or entry is composed before Spread solves them.
        this.classes.AsSpan(0, count).Clear();
        this.atomCounts.AsSpan(0, count).Clear();
        this.readers.AsSpan(0, count).Fill(-1);
        this.dependentHeads.AsSpan(0, count).Fill(-1);
        this.readHeads.AsSpan(0, count).Fill(-1);
        this.queued.AsSpan(0, (count + 63) >> 6).Fill(ulong.MaxValue);
        for (var node = 0; node < count; node++)
        {
            this.queue[node] = node;
        }

        // A ring of the queued nodes, seeded in program order: a node is queued at most once at a time.
        var (head, length) = (0, count);
        var run = (Owner: -1, Parent: default(OwnerState));
        try
        {
            while (length > 0)
            {
                var node = this.queue[head];
                (head, length) = (head + 1 == count ? 0 : head + 1, length - 1);
                this.queued[node >> 6] &= ~(1UL << node);
                for (var d = this.Sited(node, ref run) ? this.dependentHeads[node] : -1; d >= 0; d = this.dependents[d].Next)
                {
                    var dependent = this.dependents[d].Node;
                    if ((this.queued[dependent >> 6] & (1UL << dependent)) == 0)
                    {
                        this.queued[dependent >> 6] |= 1UL << dependent;
                        this.queue[(head + length++) % count] = dependent;
                    }
                }
            }
        }
        finally
        {
            this.CloseRun(ref run);
        }

        (this.keyWords, this.slotWords, this.classWords, this.rowWords) = (words, slots, classWords, row);
        if (this.rowWords > 0)
        {
            this.Spread(count);
        }

        this.Obligations(rootModules);
        if (this.missed)
        {
            report.Drop();
            return false;
        }

        report.Commit();
        return true;
    }

    // An obligation row's verdict, at Binding's row, which no body describes (R4-M71), by its classes' one answer under its rule (Outcome):
    // decided (a class that fails the rule) fails it; for every rule a Limit is the located Unsupported_Kd and Deep the depth limit
    // (R4-M153).
    private void Verdict(in EffectObligation row, byte found)
    {
        var outcome = Outcome(found, row.Rule);
        if (outcome.IsDecided)
        {
            this.report.Add(MirProblem.WithoutBody(row.Code, row.Requirement, row.Subject));
        }
        else if (outcome.IsLimit)
        {
            var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.Unsupported);
            this.report.Add(MirProblem.WithoutBody(DiagnosticCode.Unsupported_Kd, requirement, row.Subject));
        }
        else if (outcome.IsDeep)
        {
            var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.DepthLimit);
            this.report.Add(MirProblem.WithoutBody(DiagnosticCode.OwnershipDepthLimit_Kd, requirement, row.Subject));
        }
    }

    // Evaluate in the node's owner body's site (PLAN G651 U2b R5, OP2 (a)): a refused read in the round marks the body's declaration.
    // One body site covers a run of one body's consecutive nodes (ruling 30 (1); slot T-348): it opens at the run's first node and closes
    // when the owner body changes and when the round ends (CloseRun, in Run's finally), so every node is still evaluated in its body's site.
    private bool Sited(int node, ref (int Owner, OwnerState Parent) run)
    {
        var owner = this.Of(node)[node].Owner;
        if (owner != run.Owner)
        {
            this.CloseRun(ref run);
            run = (owner, types.OpenBodySite(this.program.Body(owner).Source));
        }

        return this.Evaluate(node);
    }

    // Closes the open run's body site, its marks uses of the body's declaration (MirReport.Uses).
    private void CloseRun(ref (int Owner, OwnerState Parent) run)
    {
        if (run.Owner >= 0)
        {
            this.report.Uses(types, types.CloseBodySite(run.Parent), this.program.Body(run.Owner).Key);
            run.Owner = -1;
        }
    }

    // PLAN G651 U2b R5 (ruling 18): an obligation row's site, a body site at its subject with a node site inside it where the node has
    // syntax (TyCtxt.OpenSite), closed in a finally, its marks uses of the body's declaration (MirReport.Uses).
    private (OwnerState Body, (Binding.AfterRunOwner Kind, SourceRef Subject) Node) Open(SourceRef body, SourceRef node)
        => (types.OpenBodySite(body), types.OpenSite(node));

    private void Close((OwnerState Body, (Binding.AfterRunOwner Kind, SourceRef Subject) Node) site, MirDefKey key)
    {
        types.CloseSite(site.Node);
        this.report.Uses(types, types.CloseBodySite(site.Body), key);
    }

    // A node's summary from its local facts and its callees' current summaries: whether it grew.
    private bool Evaluate(int node)
    {
        var store = this.Of(node);
        var (facts, start) = (store[node], this.Start(store, node));
        var body = this.program.Body(facts.Owner);
        this.Evaluations++;
        cases.Select(body, node - this.Base(facts.Owner));
        (this.definition, this.reader, this.found, this.scratchCount) = (body.Key.Decl, node, facts.Classes, 0);
        this.glued.Clear();
        this.Inputs(store, start.Items, facts.Items, start.Edges, facts.Edges, start.Inits, facts.Inits);
        var found = this.Found;
        if (found == this.classes[node] && this.scratchCount == this.atomCounts[node])
        {
            return false; // The summary only grows, so equal counts are equal sets.
        }

        MirDataflow.Grow(ref this.atoms, this.atomCount + this.scratchCount);
        this.scratch.AsSpan(0, this.scratchCount).CopyTo(this.atoms.AsSpan(this.atomCount));
        (this.classes[node], this.atomStarts[node], this.atomCounts[node]) = (found, this.atomCount, this.scratchCount);
        this.atomCount += this.scratchCount;
        return true;
    }

    // A node's or a site's local inputs, a store's items [item, itemEnd), edges [edge, edgeEnd) and initialization entries [init,
    // initEnd), into the current evaluation: an entry's initializer is demanded as an edge's callee is (Compose: the round misses where
    // A2's table does not hold one without a finding) and, once Spread has solved the rows, composed unless the query excludes it; a
    // static with several slots' entry is its slot bit (its edge composes the initializer).
    private void Inputs(Locals store, int item, int itemEnd, int edge, int edgeEnd, int init, int initEnd)
    {
        var items = store.Items;
        var edges = store.Edges;
        for (var i = item; i < itemEnd; i++)
        {
            var local = items[i];
            this.Process(local.Type, local.Kind, local.Requirement, local.Contract, local.Call, local.Signature);
        }

        for (var e = edge; e < edgeEnd; e++)
        {
            this.Compose(edges[e].Body, edges[e].Views, edges[e].Subst);
        }

        foreach (var entry in store.Inits[init..initEnd])
        {
            if (entry < 0)
            {
                if (this.rowWords > 0)
                {
                    this.keyScratch[this.keyWords + (~entry >> 6)] |= 1UL << ~entry;
                }

                continue;
            }

            var initializer = this.program.Body(this.initBodies[entry]);
            this.missed |= !initializer.HasFinding && !this.program.Drops.Holds(initializer.Ordinal);
            if (this.rowWords > 0 && !this.Excluded(entry))
            {
                this.Compose(initializer.Ordinal, this.Views(initializer, null), null);
            }
        }
    }

    // The summaries of a body's views a call selects, composed through its substitution under the selected view, their keys unchanged
    // (P5); a read node records its reader once, in both directions: the worklist's dependents and the read graph (Read). A body A2's table
    // does not hold has no node yet: the round misses (PLAN G951). After Run a reader composes a node's row and classes with its
    // initializations (Spread), a query that excludes some what the read graph reaches without their entries' edges (Without).
    private void Compose(int body, ulong views, Subst? subst)
    {
        if (views != 0 && !this.program.Drops.Holds(body))
        {
            this.missed = true;
            return;
        }

        for (var mask = views; mask != 0; mask &= mask - 1)
        {
            var node = this.Base(body) + BitOperations.TrailingZeroCount(mask);
            if (this.reader >= 0 && this.readers[node] != this.reader)
            {
                this.readers[node] = this.reader;
                MirDataflow.Grow(ref this.dependents, this.dependentCount + 1);
                this.dependents[this.dependentCount] = (this.reader, this.dependentHeads[node]);
                this.dependentHeads[node] = this.dependentCount++;
                this.Read(this.reader, node, -1);
            }

            if (this.excludes)
            {
                this.Without(node);
            }
            else
            {
                this.found |= this.Classes(node);
                var words = this.rowWords - this.classWords;
                _ = MirDataflow.Union(this.keyScratch.AsSpan(0, words), this.keys.AsSpan(node * this.rowWords, words));
            }

            for (var a = this.atomStarts[node]; a < this.atomStarts[node] + this.atomCounts[node]; a++)
            {
                var atom = this.atoms[a];
                // An atom in the call's instance as every reader reads one (TyCtxt.ReadableInstance, DECISIONS R4-M139): Rejected storage is the
                // instance, whose rejected parts destroy nothing (MirTypes.Destruction); null, past Binding's storage bound: Deep (Process),
                // the call's selected signature as its Type (the signature stays in the premises' terms, no case Type: SPEC 8.11.4 matches
                // it with the premises).
                var type = subst is null ? atom.Type : types.ReadableInstance(atom.Type, subst);
                var signature = subst is null || atom.Signature is null ? atom.Signature : types.ReadableInstance(atom.Signature, subst);
                var formed = type is not null && (signature is not null || atom.Signature is null);
                this.Process(formed ? cases.Type(type!) : null, atom.Kind, atom.Requirement, atom.Contract, atom.Call, signature);
            }
        }
    }

    // An input of a summary in the selected view: a part destroyed (every part its destruction reaches), a published requirement on a Type
    // (Require), or a call through a Callable value with its signature (SPEC 8.11.4): a Closure's or a function item's body entered through
    // its substitution, a common Function Type unknown (erasure keeps no bound), an abstract input by the definition's premises with the
    // call's signature. Binding proves no other Type Callable (SPEC 8.10) and acquires a value call's callee to one of these (TryCallable,
    // AcquireValueCallee): any other is Unsited.
    private void Process(BoundType? type, byte kind, BindingSymbol? requirement = null, BindingSymbol? contract = null, Subst? call = null, BoundType? signature = null)
    {
        if (type is not null && kind == Requires)
        {
            this.Require(type, requirement!, contract, call);
            return;
        }

        if (type is null || kind == Drop)
        {
            var glue = new Glue(this);
            this.found |= type is null ? Deep : (byte)0; // An instance past Binding's storage bound (R4-M153), as A5 reads retention's.
            MirTypes.Destruction(types, type, ref glue);
            return;
        }

        var receiver = (SemanticsKind)(kind - 1);
        if (type.IsAbstract)
        {
            // A call through an abstract input: the bound's class where the premises with the call's signature declare confined
            // (Discharged), else the summary's atom with its signature. Every call through an abstract Type carries the signature it selected (Atom.Signature's producers);
            // Obligations' Callable row reaches here only with a concrete Type (Binding.AddCallableObligation decides an abstract one).
            if (signature is null)
            {
                this.found |= this.Unsited(
                    $"A call through `{TyCtxt.Name(type)}` reaches A4 without the signature it selected: every call through an abstract Type carries one");
                return;
            }

            if (types.CallableBounds(this.definition, type, signature, receiver))
            {
                this.found |= Discharged(bound: true, callee: null);
            }
            else
            {
                this.AddAtom(type, kind, signature: signature);
            }

            return;
        }

        switch (type.Kind)
        {
            case BoundTypeKind.Closure:
                this.Enter(this.program[new(type.Symbol, MirBodyKind.Closure)], types.SubstOfClosure(type), type.Symbol!);
                break;
            case BoundTypeKind.FunctionItem: // A closed requirement item runs the witness its substitution names; another dispatches.
                var item = types.SubstOfItem(type);
                var function = item?.Target ?? type.Symbol!;
                if (types.DispatchBounds(this.definition, function, item?.Self, types.ItemContract(type)) is { } dispatched)
                {
                    this.found |= Discharged(dispatched, function);
                    break;
                }

                this.Enter(this.program.Callee(new(MirCalleeKind.Fn, function)), item, function); // Its declaration (SPEC 15.6.4, Local).
                break;
            case BoundTypeKind.Function: // SPEC 8.11.4: a common Function Type has no evidence (erasure keeps no bound).
                this.found |= Unknown;
                break;
            default:
                this.found |= this.Unsited(
                    $"A call through `{TyCtxt.Name(type)}`: every Callable A4 calls through is a Closure, a function item, a Function value or abstract");
                break;
        }
    }

    // A body a part's destruction or a call enters, composed in the views its substitution selects; a published operation's row instead
    // (SPEC 4.9.6); a function without a body is what Binding classifies, and one it does not classify a broken contract, unreachable by
    // any program (Local records the one at a call, Broken; the solve's, Unsited) in a complete compilation, and in an incomplete one a
    // dependent of Binding's records (Derived; R5-M183: a Closure in a declaration Binding rejected has no body).
    private void Enter(MirBody? body, Subst? subst, BindingSymbol symbol)
    {
        if (types.Published(symbol) is { } row)
        {
            if (row.Runs is { } requirement)
            {
                this.Process(this.Below(symbol, subst, row.On), Requires, requirement, TyCtxt.Contract(requirement));
            }

            if (row.Destroys >= 0)
            {
                this.Process(this.Below(symbol, subst, row.Destroys), Drop);
            }

            if (row.Calls >= 0)
            {
                var callable = this.Below(symbol, subst, row.Calls); // SPEC 4.9.6, 8.11.4: its Callable input, called then destroyed.
                var signature = this.CallsSignature(symbol, subst, row.Calls);
                this.Process(signature is null ? null : callable, Shared, signature: signature);
                this.Process(callable, Drop);
            }

            return;
        }

        if (body is null)
        {
            this.found |= Class(symbol) ?? this.Unsited(
                $"`{symbol}` has no MIR body and no Bodyless category in a complete compilation: every callee A4 enters has one or the other");
            return;
        }

        this.Compose(body.Ordinal, this.Views(body, subst), subst);
    }

    // A broken contract the solve meets, with no site to record it at (the class header's second form; DECISIONS R7-M57, BU-102): a throw in
    // a complete compilation (TyCtxt.BindingComplete), the check ending as CheckFaulted_Kd; in an incomplete one Derived, resting on Binding's
    // records.
    private byte Unsited(string contract)
        => types.BindingComplete ? throw new InvalidOperationException($"{contract} (InternalInvariant).") : Derived;

    // SPEC 4.9.6, 8.11.3: a published requirement on a Type under the Contract reference its call recorded. An abstract Type has no
    // implementation: the requirement is the bound's class where the definition's premises declare it confined (Discharged), else the
    // summary's atom, composed at the callers (unknown at the top), which keeps the recorded Contract alone (the reference names a
    // conformance only of a closed Self; a built-in target's requirement, the one a Terminator records on an abstract Self, resolves
    // without it). Any other Type runs each
    // target Binding resolves under that reference and those premises, at the instance Binding gives it for the call (a conformance witness
    // at the call's own Types and lengths, SPEC 8.6.3; TyCtxt.Witnesses: a compiler function, a composite comparison's leaf witnesses, a
    // witness body, a library witness at the Self's arguments among them, or an accessor as a dynamic edge, a virtual witness by its slot's
    // bound, DispatchBounds: a built-in target keeps its requirement's declaration), each entered at that instance as a call enters it (its
    // declaration's summary, specializations included: SPEC 15.6.4), and none resolving (a composite with an abstract part, a standard
    // Property witness) is a limit. The call is the item's, in its view (Terminator); a requirement no call names (a summary's atom, a
    // published row's) is the requirement on the Type under its own Contract, which binds no own slot.
    private void Require(BoundType type, BindingSymbol requirement, BindingSymbol? contract, Subst? call)
    {
        if (type.IsAbstract)
        {
            if (types.DispatchBounds(this.definition, requirement, type, null) == true)
            {
                this.found |= Discharged(bound: true, requirement);
            }
            else
            {
                this.AddAtom(type, Requires, requirement, contract);
            }

            return;
        }

        var start = this.targets.Count;
        call ??= types.Synthesized(requirement, type, TyCtxt.Contract(requirement).Type!);
        this.found |= types.Witnesses(requirement, contract, call, this.definition, this.targets) ? (byte)0 : Limit;
        for (var i = start; i < this.targets.Count; i++)
        {
            var (target, instance) = this.targets[i];
            if (target.Definition.Kind == DispositionKind.Body && types.DispatchBounds(this.definition, target, null, null) is { } dispatched)
            {
                this.found |= Discharged(dispatched, target); // SPEC 6.4.6: a virtual witness dispatches; its slot's bound decides.
            }
            else
            {
                this.Enter(this.program.Callee(new(MirCalleeKind.Fn, target)), instance, target); // As a call enters it (SPEC 15.6.4, Local).
            }
        }

        this.targets.RemoveRange(start, this.targets.Count - start);
    }

    // SPEC 16.3, 8.11.3 rule 1: a part a destruction reaches, at its depth in the walk. An abstract part is an atom; a Function value's
    // environment is unknown; an owning handle destroys its payload when it is proven Sealed (SPEC 16.3.3, MirTypes.SealedPayload, the one
    // test A5 reads too), walked on below the handle so that a growing recursive Type (SPEC 15.3.5) meets the walk's bound, else any
    // more-derived destructor may run; a struct's destructor runs; a string's, an Array's or a Dictionary's storage is released (its
    // elements are parts of their own).
    private void Destroy(BoundType type, int depth)
    {
        if (type.IsAbstract)
        {
            this.AddAtom(type, Drop);
        }
        else if (type.Kind == BoundTypeKind.Function)
        {
            this.found |= Unknown;
        }
        else if (type.Kind == BoundTypeKind.Semantics)
        {
            var glue = new Glue(this);
            var payload = MirTypes.SealedPayload(type, types, this.definition);
            this.found |= payload is null ? Unknown : (byte)0;
            MirTypes.Destruction(types, payload, ref glue, depth + 1);
        }
        else if (AdtDef.DestructorSymbol(type) is { } destructor)
        {
            this.Enter(this.program.Callee(new(MirCalleeKind.Fn, destructor)), types.Synthesized(destructor, type, [], []), destructor);
        }
    }

    // An atom of the evaluated summary, once. It is resolved at once by the definition's premises while judging an obligation, and where its
    // Type is a projection with a recursive base, which a summary never keeps (SPEC 15.6.4; TyCtxt.RecursiveBase): a destroyed abstract part
    // proven Copy does nothing (SPEC 16.3.3 "unless the Type is Copy", 8.11.2 "not proven Copy"), any other is unknown (a call through an
    // input reaches here only without confined).
    private void AddAtom(BoundType type, byte kind, BindingSymbol? requirement = null, BindingSymbol? contract = null, BoundType? signature = null)
    {
        if (this.reader < 0 || types.RecursiveBase(this.definition, type))
        {
            this.found |= kind == Drop && types.Copies(this.definition).Copy(type) ? (byte)0 : Unknown;
            return;
        }

        var atom = new Atom(type, kind, requirement, contract, Signature: signature);
        for (var i = 0; i < this.scratchCount; i++)
        {
            if (ReferenceEquals(this.scratch[i].Type, type) && this.scratch[i] == atom)
            {
                return;
            }
        }

        MirDataflow.Grow(ref this.scratch, this.scratchCount + 1);
        this.scratch[this.scratchCount++] = atom;
    }

    // R4-M22: each obligation Binding published. A Callable binding is a call through its subject with the selected receiver, judged in
    // every view under the use's premises with no caller view (its open pair layers admit every case), and an owner acquisition also
    // destroys the remaining environment; a failure the premises decide is reported in every view. An owing definition owes with every
    // implementation (SPEC 8.4), which its summary includes (Owe).
    // A row whose subject is outside the root range is the library check's (PLAN G951 E3, DECISIONS R5-M168's input rule): Binding lists a
    // library declaration's rows from the library alone, so their verdicts are the same in every program.
    // PLAN G651 U2b ruling 18: a row is judged in the site of its premise definition's body (the module's top-level statements when it has
    // none): an owing definition's at its body's Source, a fallback; a Callable binding's at its use, a node site where it has syntax. A row
    // that reads past no body (no Callable, no owing body) opens none. The site is keyed by that body's own key, the one lookup Owe reads
    // (MirProgram.Callee: a function's, a Getter's or a Setter's body; ruling 25 N7), so a read there merges with the body's own; a #Test
    // body, which Callee does not answer, keeps MirDefKey(Definition, Fn) (ruling 21's open issue).
    private void Obligations(int rootModules)
    {
        this.rows.Clear();
        types.EffectObligations(this.rows);
        cases.Deselect();
        this.reader = -1;
        foreach (var row in this.rows)
        {
            if (row.Subject.Module >= rootModules)
            {
                continue;
            }

            (this.found, this.scratchCount, this.definition) = (0, 0, row.Definition);
            this.glued.Clear();
            var body = row.Definition is { } target ? this.program.Callee(new(MirCalleeKind.Fn, target)) : null;
            var owing = row.Callable is null ? body : null;
            if (row.Callable is null && owing is null)
            {
                this.Judge(row, null);
                continue;
            }

            var site = this.Open(owing?.Source ?? row.Subject, owing is null ? row.Subject : default);
            try
            {
                this.Judge(row, owing);
            }
            finally
            {
                this.Close(site, body?.Key ?? new(row.Definition, MirBodyKind.Fn, row.Definition is null ? row.Subject.Module : 0));
            }
        }
    }

    // An obligation row's verdict: a Callable binding's call through it, a row without a definition directly, an owing definition's bodies.
    private void Judge(in EffectObligation row, MirBody? owing)
    {
        if (row.Callable is { } callable)
        {
            this.Begin(default);
            this.Process(callable, (byte)(1 + (int)row.Receiver));
            if (row.Owner)
            {
                this.Process(callable, Drop);
            }

            this.Verdict(row, this.Found); // SPEC 15.6.4: its first-access initializations among them (Compose).
        }
        else if (row.Definition is not { } target)
        {
            this.report.Add(MirProblem.WithoutBody(row.Code, row.Requirement, row.Subject));
        }
        else
        {
            this.Owe(row, target, owing);
        }
    }

    // An owing definition (SPEC 8.4: every implementation, which its summary includes, SPEC 15.6.4, Local): its body per view; a function
    // without a body in every view at once.
    private void Owe(in EffectObligation row, BindingSymbol definition, MirBody? body)
    {
        for (var mask = body is null ? 1UL : this.Views(body, null); mask != 0; mask &= mask - 1)
        {
            (this.found, this.scratchCount) = (0, 0);
            this.glued.Clear();
            this.Begin(default);
            if (body is null)
            {
                this.Enter(null, null, definition);
            }
            else
            {
                this.Compose(body.Ordinal, mask & ~(mask - 1), null); // Its lowest view.
            }

            this.Verdict(row, this.Found); // SPEC 15.6.4: its first-access initializations among them (Compose).
        }
    }

    // The destruction walk's visitor: each part reached, and past the walk's depth bound a part this implementation does not follow (Deep).
    private readonly struct Glue(MirEffects owner) : IMirDestruction
    {
        public bool First(BoundType type, int depth) => MirDepths.Enter(ref CollectionsMarshal.GetValueRefOrAddDefault(owner.glued, type, out _), depth);

        public bool Visit(BoundType type, int depth)
        {
            owner.Destroy(type, depth);
            return false;
        }

        public bool Deep(BoundType type)
        {
            MirDepths.Meet(ref CollectionsMarshal.GetValueRefOrAddDefault(owner.glued, type, out _));
            return false;
        }
    }
}
