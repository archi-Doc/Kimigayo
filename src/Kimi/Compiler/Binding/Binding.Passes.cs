// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The pass driver (PLAN G949 S1-r1a; DECISIONS BU-220): a Bind's runs and, in each run, its passes in order.
// - Responsibility: the three levels of a Bind's state: per Bind (BeginBind: the SyntaxIds, the Result discarded with the later
//   phases' facts, StartRuns), per run (BeginRun: the counter every stamp compares, the work counters, the local-region graph's
//   version) and per pass (ResetPass); the runs, a new one whenever a pass asks for it (VerifyEarlyReads); each pass's steps in order
//   over its module set (BindingPass); each pass's part of the per-pass collections (PassPart: the published lists, the local-region
//   graph, the slots' keepers, the Contract shapes, the effect bounds, the import declarations and the prune steps' sets), handed over as the fields every
//   step writes (EnterPass); R1.1's one composition point per aggregate: the composed views,
//   the program part then the library layer's, and the Bind's Result (ComposeResult); a keyed collection's one find (PassPart.Find).
// - Inputs: the compilation's modules, the mode. Outputs: the parts (Binding's own, programPart; LibraryLayer.Part), the composed
//   views, the Result.
// - Invariants: one counter value per run; a restart starts a new run of every pass, never a new Bind (StartRuns runs once per Bind,
//   so a second run cannot ask again and the runs end); a step walks the running pass's modules, in their order, and writes its part;
//   between Binds, and after a throw, the running pass is the program pass with Binding's own part, and a reader outside a pass reads
//   the views, never a field a pass writes; until PLAN G949 S1-r1b one pass binds every module (ProgramPass) and the layer's part is
//   empty.
// - Prohibitions: no step body here; no flag or mode steering a step; no second copy of a step for the library; no consumer
//   composing parts.
public sealed partial class Binding
{
    // Binding's own part of the published lists: the program pass's (until PLAN G949 S1-r1b the one pass's, every module's).
    private readonly PassPart programPart = new(KimiLibrarySize.NodeCapacity(KimiLibrarySize.Stated.Nodes));

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

    // The program pass: the source modules, with Binding's own part; until PLAN G949 S1-r1b splits the library pass off, also the
    // library (the one pass).
    private BindingPass ProgramPass => new(this.compilation.SourceModules, this.Library.Kotonoha, this.programPart);

    // Every module of the compilation, the set a reader outside a pass walks (a family's Values; the composed views' modules): the source
    // modules, then the library, with Binding's own part. Until PLAN G949 S1-r1b it equals the program pass's set.
    private BindingPass Modules => new(this.compilation.SourceModules, this.Library.Kotonoha, this.programPart);

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
        this.storageVersion++;
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

    // The runs: each runs every pass in order; a pass whose declarations ask for a rerun ends its run, and the next run starts.
    private void Run(BindingMode mode)
    {
        do
        {
            this.BeginRun();
        }
        while (!this.RunPass(this.ProgramPass, mode));
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
        return true;
    }

    // Hands a pass its part: the fields every step writes become the part's lists.
    [MemberNotNull(nameof(nodes), nameof(issues), nameof(derivedIssues), nameof(obligations), nameof(obligationSet), nameof(libraryImports))]
    [MemberNotNull(nameof(virtualDeclarations), nameof(callableEffectUses), nameof(callableEffectClauses), nameof(localRegions))]
    [MemberNotNull(nameof(keepers), nameof(contractShapes), nameof(boundedRequirements), nameof(importDeclarations))]
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
    // program pass's) and LibraryLayer.Part (the library pass's from PLAN G949 S1-r1b, empty until then). Its collections are never
    // replaced, only emptied by its pass's resets and refilled, so a view built over them once stays current. A list is read through its
    // composed view; a keyed collection (Regions, Keepers) through one find (Find): the layer's part first, then the running pass's, a
    // write going into the row found or, for a key in neither part, into the running pass's, so a key has one row, and a reader then
    // reads the stamp of the part the row was found in (Keepers: RequirementCall); a keyed collection whose values index a per-pass list
    // keeps that list in its own part. An in-pass read of a list another pass also fills reads the pass's view: the running part, then
    // the layer's when the running part is not the layer's (R1.1's list order), which the driver hands over with the part
    // (selectableEffectClauses), so no pass reads another pass's part of the previous Bind. Counts is the pass's contribution to the Bind's Result (ComposeResult), not
    // a tally of the part's nodes, which only the views count: from S1-r1b a program pass's own nodes plus each foreign node's final
    // state less its earlier one.
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
    }
}
