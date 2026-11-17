// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The pass driver (PLAN G949 S1-r1a and S1-r1b; DECISIONS BU-220): a Bind's runs and, in each run, its passes in order: the library
// pass (the library module, LibraryLayer.Part), then the program pass (the source modules, Binding's own part).
// - Responsibility: the three levels of a Bind's state: per Bind (BeginBind: the SyntaxIds, the Result discarded with the later
//   phases' facts, StartRuns), per run (BeginRun: the counter every stamp compares, the work counters, the local-region graph's
//   version) and per pass (ResetPass); the runs, a new one whenever a pass asks for it (VerifyEarlyReads); each pass's steps in order
//   over its module set (BindingPass); each pass's part of the per-pass collections (PassPart: the published lists, the local-region
//   graph, the slots' keepers, the Contract shapes, the effect bounds, the import declarations, the prune steps' sets, the contract
//   solver's Closure(view) entries and the documents' unnamed aliases), handed over as the fields every step writes (EnterPass);
//   R1.1's one composition point per aggregate: the composed views, the program part then the library layer's, and the Bind's Result
//   (ComposeResult); a keyed collection's one find (PassPart.Find).
// - Inputs: the compilation's modules, the mode. Outputs: the parts (Binding's own, programPart; LibraryLayer.Part), the composed
//   views, the Result.
// - Invariants: one counter value per run; a restart starts a new run of every pass, never a new Bind (StartRuns runs once per Bind,
//   so a second run cannot ask again and the runs end); a step walks the running pass's modules, in their order, and writes its part;
//   between Binds, and after a throw, the running pass is the program pass with Binding's own part, and a reader outside a pass reads
//   the views, never a field a pass writes; the pass that binds the library publishes its module at its end in every mode and sets
//   the library's publication version (PublishLibrary, RB1) and the library's size (LibraryLayer.Size); a library declaration's Origin
//   requirement work is never an edge source in the program pass (its Pass is the library pass's requirement call, which
//   CollectRequirementEdges compares with the running pass's).
// - Prohibitions: no step body here; no flag or mode steering a step; no second copy of a step for the library; no consumer
//   composing parts.
public sealed partial class Binding
{
    // The one currency rule (DECISIONS BU-105 as PLAN G949 S1-1 amends it; Current below): Frozen is the reserved stamp value of each
    // width (ulong, uint, int), outside its counter's range and every reset or never-set marker of that stamp (int.MaxValue, never -1:
    // LocalRegion.Version's "never computed").
    internal const ulong Frozen = ulong.MaxValue;

    internal const uint Frozen32 = uint.MaxValue;

    internal const int FrozenRegion = int.MaxValue;

    // Binding's own part of the published lists: the program pass's. Its node list starts at the program's room (the library's nodes
    // are the layer's part's, which starts at the library's stated count: KimiLibrarySize).
    private readonly PassPart programPart = new(KimiLibrarySize.NodeRoom);

    // The empty second part of a pass's view when the pass's own part is the library layer's (selectableEffectClauses); never written.
    private readonly List<IsKoto> noClauses = new();

    // The composed views (R1.1; DECISIONS BU-220): each list's program part, then the library layer's (LibraryLayer.Part), built once
    // over the two fixed parts, so a read outside a pass allocates nothing and sees every pass's entries.
    private readonly LayerList<Koto> nodesView;
    private readonly LayerList<BindingIssue> issuesView;
    private readonly LayerList<Koto> derivedIssuesView;
    private readonly LayerList<BindingObligation> obligationsView;
    private readonly LayerList<LibraryImport> libraryImportsView;
    private readonly LayerList<FunctionKoto> virtualDeclarationsView;
    private readonly LayerList<CallableEffectUse> callableEffectUsesView;
    private readonly LayerList<BoundContract> contractShapesView;
    private readonly LayerList<BoundEffectBound> boundedRequirementsView;
    private readonly LayerList<BindingSymbol> importDeclarationsView;

    // The running pass (EnterPass); between Binds the program pass of the latest Bind, which PublishHir publishes.
    private BindingPass pass;

    // The bounded Callable clauses the running pass may select (SPEC 8.11.4), its view of a list another pass also fills (PassPart's
    // in-pass rule): its own part's, then the library layer's when its part is another. Between Binds it is the program pass's, so a
    // reader after a Bind sees every pass's clauses.
    private LayerList<IsKoto> selectableEffectClauses;

    // The library pass (PLAN G949 S1-r1b): the library module, with the library layer's part; first in every run.
    private BindingPass LibraryPass => new([], this.Library.Kotonoha, this.Library.Layer.Part);

    // The program pass: the source modules, with Binding's own part.
    private BindingPass ProgramPass => new(this.compilation.SourceModules, null, this.programPart);

    // Every module of the compilation, the set a reader outside a pass walks (a family's Values; the composed views' modules): the source
    // modules, then the library, with Binding's own part.
    private BindingPass Modules => new(this.compilation.SourceModules, this.Library.Kotonoha, this.programPart);

    // The one currency rule: a stamp is current when it equals its counter's value or is Frozen. A counter that could reach a Frozen
    // value skips it: the run counter's uint image (BeginRun) and the requirement counter (SolveOriginRequirements); the edit counter
    // (ulong) and the region version (one run's bounds) stay far below theirs. Every stamp compared with a counter is read here: a
    // declaration query's (BindingSymbol), a stamped column's (StampedColumn), an instance's ADT (Instance), a VTable (FillVTable), a local
    // region (LocalRegionSources) and a slot's keepers (Keepers). Not ImplTable's objects, current by their own store's Pass, and not an
    // Origin requirement work, active only in its own call. Only S1-1's freeze writes Frozen; until then no stamp holds it.
    internal static bool Current(ulong stamp, ulong counter) => stamp == counter || stamp == Frozen;

    internal static bool Current(uint stamp, uint counter) => stamp == counter || stamp == Frozen32;

    internal static bool Current(int stamp, int counter) => stamp == counter || stamp == FrozenRegion;

    // Per Bind: the SyntaxIds, the Result and the later phases' facts discarded, the runs started.
    private void BeginBind(BindingMode mode)
    {
        // SyntaxIds: each module is renumbered only when parsing or an edit changed its syntax since it was last numbered. Its
        // ordinal is its position; the library's is the fixed SyntaxTable.LibraryModule.
        var modules = this.compilation.SourceModules;
        for (var i = 0; i < modules.Length; i++)
        {
            modules[i].Syntax.Number(modules[i].RootKoto, (ushort)i);
        }

        this.Library.Kotonoha.Syntax.Number(this.Library.Kotonoha.RootKoto, SyntaxTable.LibraryModule);

        // Every later phase rests on Binding, so its facts are discarded with Binding's; a pass that does not finish
        // leaves no result behind.
        this.Result = default;
        this.compilation.Diagnostics.InvalidateSemantics();
        this.compilation.InvalidateMir();
        this.StartRuns(mode);
    }

    // Per run, a restart's included, and per edit (Invalidate): a new counter value, so no stamp of an earlier run reads current, and
    // the work counters, which the scaling checks read as the last run's work. The local-region graph's version starts again: every
    // bound a run records moves it, whichever part holds the row, so it only rises within a run (n55-design 2.4).
    private void BeginRun()
    {
        if ((uint)++this.storageVersion == Frozen32)
        {
            this.storageVersion++; // Its uint image is BindingSymbol's query stamp: never Frozen32 (Current).
        }

        this.regionVersion = 0;
        this.signatureWalks = 0;
        this.memberWalks = 0;
        this.termWalks = 0;
        this.baseSteps = 0;
        this.inheritedSteps = 0;
        this.inheritedPremiseSteps = 0;
        this.sourceChecks = 0;
        this.ResetInferenceMetrics();
    }

    // The runs: each runs every pass in order, the library pass, then the program pass; a pass whose declarations ask for a rerun ends
    // its run, and the next run starts with the library pass again (until S1-1 freezes the library).
    private void Run(BindingMode mode)
    {
        do
        {
            this.BeginRun();
        }
        while (!(this.RunPass(this.LibraryPass, mode) && this.RunPass(this.ProgramPass, mode)));
    }

    // An edit's revocation of the latest Bind (Invalidate): each pass's facts discarded as its next run would discard them, the
    // library's first, and then the fields hold Binding's own part again (between Binds).
    private void ResetPasses()
    {
        this.EnterPass(this.LibraryPass);
        this.ResetPass(BindingMode.Provisional);
        this.EnterPass(this.ProgramPass);
        this.ResetPass(BindingMode.Provisional);
    }

    // One pass over its modules: its reset, indexing, declarations, bodies, the bound-declaration validations and Check. False: its
    // declarations asked for a rerun (VerifyEarlyReads).
    private bool RunPass(BindingPass pass, BindingMode mode)
    {
        this.EnterPass(pass);
        this.ResetPass(mode);
        this.Index();
        if (!this.BindDeclarations(mode))
        {
            return false;
        }

        this.BindBodies();
        this.ValidateBoundDeclarations(mode);
        this.Check(mode);
        if (pass.Library is { } library)
        {
            // The library module's place in the pass's set, never which pass runs (RB1): its publication at its pass's end, and the
            // library's size, the pass's table counts before the program pass empties its per-pass tables (O8 (a)): the layer's part's
            // nodes, not the composed view's.
            this.PublishLibrary(library, mode);
            this.Library.Layer.Size = this.TableCounts with { Nodes = this.nodes.Count };
        }

        return true;
    }

    // Hands a pass its part: the fields every step writes become the part's lists.
    [MemberNotNull(nameof(nodes), nameof(issues), nameof(derivedIssues), nameof(obligations), nameof(obligationSet), nameof(libraryImports))]
    [MemberNotNull(nameof(virtualDeclarations), nameof(callableEffectUses), nameof(callableEffectClauses), nameof(localRegions))]
    [MemberNotNull(nameof(keepers), nameof(contractShapes), nameof(boundedRequirements), nameof(importDeclarations), nameof(foreignFailures))]
    [MemberNotNull(nameof(enumerateIndex), nameof(enumerateIndexPool), nameof(aliasesByDocument), nameof(boundContracts))]
    private void EnterPass(BindingPass pass)
    {
        this.pass = pass;
        var part = pass.Part;
        this.nodes = part.Nodes;
        this.issues = part.Issues;
        this.derivedIssues = part.DerivedIssues;
        this.obligations = part.Obligations;
        this.obligationSet = part.ObligationSet;
        this.libraryImports = part.Imports;
        this.virtualDeclarations = part.VirtualDeclarations;
        this.callableEffectUses = part.CallableEffectUses;
        this.callableEffectClauses = part.CallableEffectClauses;
        var layer = this.Library.Layer.Part;
        this.selectableEffectClauses = new(part.CallableEffectClauses, ReferenceEquals(part, layer) ? this.noClauses : layer.CallableEffectClauses);
        this.localRegions = part.Regions;
        this.keepers = part.Keepers;
        this.contractShapes = part.ContractShapes;
        this.boundedRequirements = part.BoundedRequirements;
        this.importDeclarations = part.ImportDeclarations;
        this.foreignFailures = part.ForeignFailures;
        this.enumerateIndex = part.EnumerateIndex;
        this.enumerateIndexPool = part.EnumerateIndexPool;
        this.aliasesByDocument = part.AliasesByDocument;
        this.boundContracts = part.BoundContracts;
    }

    // R1.1's one composition point of the Bind's summary: the library layer's counts and the program part's, in the program pass's mode.
    private BindingResult ComposeResult()
    {
        var (layer, program) = (this.Library.Layer.Part.Counts, this.programPart.Counts);
        return program with
        {
            ResolvedCount = layer.ResolvedCount + program.ResolvedCount,
            UnresolvedCount = layer.UnresolvedCount + program.UnresolvedCount,
            InvalidCount = layer.InvalidCount + program.InvalidCount,
        };
    }

    // A pass's part of the per-pass collections (DECISIONS BU-220's pass driver): exactly two for a Binding's life, Binding's own (the
    // program pass's) and LibraryLayer.Part (the library pass's, PLAN G949 S1-r1b). Its collections are never
    // replaced, only emptied by its pass's resets and refilled, so a view built over them once stays current. A list is read through its
    // composed view; a keyed collection (Regions, Keepers, BoundContracts) through one find (Find): the layer's part first, then the running pass's, a
    // write going into the row found or, for a key in neither part, into the running pass's, so a key has one row, and a reader then
    // reads the stamp of the part the row was found in (Keepers: RequirementCall); a keyed collection whose values index a per-pass list
    // keeps that list in its own part. An in-pass read of a list another pass also fills reads the pass's view: the running part, then
    // the layer's when the running part is not the layer's (R1.1's list order), which the driver hands over with the part
    // (selectableEffectClauses), so no pass reads another pass's part of the previous Bind. Two forms of access, by meaning: a composed
    // list, a collection found by the find and a per-pass memo (EnumerateIndex) are handed over as the fields their steps write or read
    // (EnterPass); a per-pass node-keyed table (a member the "Node-keyed table members in Binding" ratchet counts: PatternNodes and
    // CandidateScopes, with their previous-run lists) is declared here once and read through the running pass's part (this.pass.Part),
    // so a member that ratchet counts never returns to Binding (DECISIONS BU-220: node-keyed tables leave Binding batch by batch).
    // ForeignFailures lists the nodes of a module the pass does not bind that it failed, each with its state before the failure (the
    // recorder, Binding.Diagnostics.Prerequisites.cs): their records and counts are the failing pass's (CountStates). Counts is the pass's
    // contribution to the Bind's Result (ComposeResult), not a tally of the part's nodes, which only the views count: a program pass's
    // own nodes plus each foreign node's final state less its earlier one.
    internal sealed class PassPart(int nodes)
    {
        internal List<Koto> Nodes { get; } = new(nodes);

        internal List<BindingIssue> Issues { get; } = new();

        internal List<Koto> DerivedIssues { get; } = new();

        internal List<BindingObligation> Obligations { get; } = new();

        internal HashSet<BindingObligation> ObligationSet { get; } = new();

        internal List<LibraryImport> Imports { get; } = new();

        internal List<FunctionKoto> VirtualDeclarations { get; } = new();

        internal List<CallableEffectUse> CallableEffectUses { get; } = new();

        internal List<IsKoto> CallableEffectClauses { get; } = new();

        // The local-region graph's rows by their target Origin (Binding.LocalRegions.cs), keyed by reference.
        internal Dictionary<BoundOrigin, LocalRegion> Regions { get; } = new(ReferenceEqualityComparer.Instance);

        // Each declared slot's keepers (Binding.RawOrigins.Keepers.cs), keyed by reference: a slot is kept only by its declaration's
        // pass, and a list is current while its Pass is RequirementCall, the requirement counter's value at the pass's latest call.
        internal Dictionary<OriginParameter, KeeperList> Keepers { get; } = new(ReferenceEqualityComparer.Instance);

        internal uint RequirementCall { get; set; }

        // The bound Contract references by their reference Type (Binding.BoundContracts.cs): each made and reset (ResetContracts) by its
        // pass, so the layer's part holds exactly the entries the library pass made, each filled from entries found first in the layer's
        // part, and a program pass makes, resets and rebuilds its own (PLAN G949 S1-1, E3; census O15).
        internal Dictionary<BoundType, BindingSymbol> BoundContracts { get; } = new(ReferenceEqualityComparer.Instance);

        // The pass's Contract shapes and the effect bounds they declare (Binding.EffectDeclarations.cs).
        internal List<BoundContract> ContractShapes { get; } = new();

        internal List<BoundEffectBound> BoundedRequirements { get; } = new();

        // The pass's import declarations (Binding.Attributes.cs).
        internal List<BindingSymbol> ImportDeclarations { get; } = new();

        // The pattern nodes and guard scopes the pass indexed, and those of its previous run, which its prune steps compare
        // (Binding.Patterns.cs: PruneCandidateScopes, PrunePatternScopes): each pass's own, so a pass prunes against its own previous
        // run and never removes a scope or symbol another pass indexed. Their one declaration is here (a node-keyed table is counted
        // once, DECISIONS BU-220's ratchet), so their steps read them through the running pass's part.
        internal HashSet<Koto> PatternNodes { get; } = new(ReferenceEqualityComparer.Instance);

        internal List<Koto> PreviousPatternNodes { get; } = new();

        internal HashSet<Koto> CandidateScopes { get; } = new();

        internal List<Koto> PreviousCandidateScopes { get; } = new();

        // The contract solver's Closure(view) entries of the pass and its free entries (Binding.ContractSolver.Rows.cs), a per-pass
        // memo handed over as the fields the solver reads: an entry lasts its pass (ClearClosure, when the pass runs again) and goes
        // back to the free entries of the pass that formed it, so a pass reuses its own entries in its own order whichever pass runs
        // between (each list then settles at the largest view it meets in its pass), and no pass returns an entry to another pass's
        // part. A reader after the Bind (CheckStartup, Analyze) uses the program pass's, the running pass between Binds.
        internal Dictionary<(EvidenceView View, BindingSymbol? Requirement), ViewIndex> EnumerateIndex { get; } = new();

        internal Stack<ViewIndex> EnumerateIndexPool { get; } = new();

        // The unnamed aliases of each source document of the pass (Binding.Aliases.cs: PrepareAliases, read by the name lookups): each
        // pass's own, emptied by its reset and pruned of its emptied documents by its own PrepareAliases, so the library pass, which
        // declares no alias, never removes a program document's entry that the program pass would then make again.
        internal Dictionary<SourceDocument, List<AliasImport>> AliasesByDocument { get; } = new();

        // The nodes outside the pass's modules that the pass failed, in the order it failed them, each with its earlier state.
        internal List<(Koto Node, BindingState Before)> ForeignFailures { get; } = new();

        internal BindingResult Counts { get; set; }

        // The one find of a keyed collection: the layer's map, then the running pass's when it is not the layer's (a test of the map,
        // never of which pass runs). Returns the map that holds the key, null when neither does.
        internal static Dictionary<TKey, TValue>? Find<TKey, TValue>(Dictionary<TKey, TValue> layer, Dictionary<TKey, TValue> running, TKey key, out TValue? value)
            where TKey : notnull
        {
            if (layer.TryGetValue(key, out value))
            {
                return layer;
            }

            return !ReferenceEquals(running, layer) && running.TryGetValue(key, out value) ? running : null;
        }
    }

    // A pass's module set (DECISIONS BU-220's pass driver): its source modules by ordinal, then the library module when the pass binds
    // it (d8's order). A step walks Sources and Library where their walks differ, and the modules in that order (the indexer) where they
    // do not; which walk a module takes follows from its place in the set, never from which pass runs. One set is not a pass's:
    // Binding.Modules, every module with Binding's own part, which a family's Values walks for readers outside a pass.
    internal readonly struct BindingPass(Kotonoha[] sources, Kotonoha? library, PassPart part)
    {
        // The modules: the source modules, then the library module, if any.
        internal int Count => sources.Length + (library is null ? 0 : 1);

        internal ReadOnlySpan<Kotonoha> Sources => sources;

        internal Kotonoha? Library => library;

        internal PassPart Part => part;

        internal Kotonoha this[int index] => index < sources.Length ? sources[index] : library!;

        // Whether the pass binds the module of a table: the library's when it holds the library module, a source module's when it holds
        // the sources (a pass holds every source module or none). A node of no table yet is the running pass's own.
        internal bool Binds(SyntaxTable? table) => table is null || (table.IsLibrary ? library is not null : sources.Length != 0);
    }
}
