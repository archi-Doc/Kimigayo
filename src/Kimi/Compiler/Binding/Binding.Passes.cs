// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// The pass driver (PLAN G949 S1-r1a, S1-r1b, S1-1 and Stage 2 U3; DECISIONS BU-220): a Bind's runs and, in each run, its passes in
// order: the library pass (the library module, LibraryLayer.Part) only in the freeze compilation, which holds the one library neither
// frozen nor refused, then frozen (FreezeLibrary), and the program pass (the source modules, Binding's own part).
// - Responsibility: the three levels of a Bind's state: per Bind (BeginBind: the SyntaxIds, the Result discarded with the later
//   phases' facts, StartRuns), per run (BeginRun: the counter every stamp compares, the work counters, the local-region graph's
//   version) and per pass (ResetPass); the runs, a new one whenever the program pass asks for it (VerifyEarlyReads); the library's
//   freeze (FreezeLibrary: verify, stamp, publish); each pass's steps in order
//   over its module set (BindingPass); each pass's part of the per-pass collections (PassPart: the published lists, the local-region
//   graph, the slots' keepers, the Contract shapes, the effect bounds, the import declarations, the prune steps' sets, the contract
//   solver's Closure(view) entries and the documents' unnamed aliases), handed over as the fields every step writes (EnterPass);
//   R1.1's one composition point per aggregate: the composed views, the program part then the library layer's, and the Bind's Result
//   (ComposeResult); a keyed collection's one find (PassPart.Find) and the one level order of the keyed parts and the interners
//   (PassPart.Levels: Parts, Interned; PLAN G949 Stage 2 U4), whose seal is the freeze's library table freeze (Syntax.Freeze()).
// - Inputs: the compilation's modules, the mode. Outputs: the parts (Binding's own, programPart; LibraryLayer.Part), the composed
//   views, the Result.
// - Invariants: one counter value per run; a restart starts a new run of the program pass, never a new Bind (StartRuns runs once per
//   Bind, so a second run cannot ask again and the runs end); the library pass runs only while the compilation's library is neither
//   frozen nor refused (the freeze compilation's), in the first run of a Bind, and its freeze seals the library last (Syntax.Freeze()),
//   so a library pass that throws leaves no Frozen stamp and the next Bind runs it again; after the freeze the library's stamps are
//   Frozen and its facts the library pass's, the same in every Bind of the compilation, and the library HIR identical in each (C-379,
//   G949 ruling 174); a step walks the running pass's modules, in their order, and writes its part; a library pass with a record of any
//   kind is refused, not frozen (PLAN G949 Stage 2 U2a; design u2-design 2.4, C2): its records are kept as the layer's refusal while
//   its tables stand, the library part and Binding's own part are reset, no program pass runs and the Bind leaves no Result; a refused
//   library is never bound again (PLAN G949 Stage 2 U3: the driver reads its refusal first and runs no pass, so a compilation given it
//   does no Binding work beyond publishing the refusal); between Binds, and after a throw, the running pass is the program pass with
//   Binding's own part, and a reader outside a pass reads the views, never a field a pass writes; the library pass runs Final whatever
//   the Bind's mode (PLAN G949 S1-1, B2: no library fact is a Provisional pass's), so its Check publishes the library module; a
//   library declaration's Origin requirement work is never an edge source in the program pass (its Pass is Frozen32, never the
//   running pass's requirement call, which CollectRequirementEdges compares).
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

    // Binding's own part of the published lists: the program pass's (the library's nodes are the layer's part's).
    private readonly PassPart programPart = new();

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

    // Whether this Binding binds its library: neither frozen nor refused, the freeze compilation's (PLAN G949 Stage 2 U3).
    private bool BindsLibrary => !this.Library.Frozen && this.Library.Layer.Refusal is null;

    // The library pass (PLAN G949 S1-r1b): the library module, with the library layer's part; first in every run that binds it.
    private BindingPass LibraryPass => new([], this.Library.Kotonoha, this.Library.Layer.Part);

    // The program pass: the source modules, with Binding's own part.
    private BindingPass ProgramPass => new(this.compilation.SourceModules, null, this.programPart);

    // Every module of the compilation (Compilation.BoundModules: the source modules, then the library), the set a reader outside a pass
    // walks (a family's Values; the composed views' modules), with Binding's own part.
    private BindingPass Modules
    {
        get
        {
            var (sources, library) = this.compilation.BoundModules;
            return new(sources, library, this.programPart);
        }
    }

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

        if (this.BindsLibrary)
        {
            this.Library.Kotonoha.Syntax.Number(this.Library.Kotonoha.RootKoto, SyntaxTable.LibraryModule); // Only a Bind that runs the library pass.
        }

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
        this.borrowVersion = 0;
        this.signatureWalks = 0;
        this.memberWalks = 0;
        this.termWalks = 0;
        this.baseSteps = 0;
        this.inheritedSteps = 0;
        this.inheritedPremiseSteps = 0;
        this.sourceChecks = 0;
        this.ResetInferenceMetrics();
    }

    // The runs: the library pass while the library is not frozen (its publication version 0), Final whatever the Bind's mode (no
    // Provisional answer on a library object; a Provisional Bind's program pass reads Final library facts), and its freeze; then the
    // program pass, whose request for a rerun starts a new run of the program pass alone. False: the library pass was refused (its
    // refusal kept, FreezeLibrary), so the library part is reset, Binding's own part entered and reset, and no program pass runs.
    private bool Run(BindingMode mode)
    {
        if (this.Library.Layer.Refusal is not null)
        {
            // A refused library is never bound again (PLAN G949 Stage 2 U3; ruling 15): no run, no pass, no intern; Binding's own part
            // entered empty, and the compilation publishes the refusal (ReportDiagnostics).
            this.StartPass(this.ProgramPass, mode);
            return false;
        }

        do
        {
            this.BeginRun();
            if (this.BindsLibrary && !this.FreezeLibrary(this.RunPass(this.LibraryPass, BindingMode.Final)))
            {
                this.StartPass(this.LibraryPass, BindingMode.Final); // The refused library part emptied: no reader sees a partial layer (C2).
                this.StartPass(this.ProgramPass, mode); // Binding's own part, empty; no program is bound over a refused library.
                return false;
            }
        }
        while (!this.RunPass(this.ProgramPass, mode));
        return true;
    }

    // PLAN G949 S1-1 (s11-design 2.3; DECISIONS BU-105 as BU-219 amends it): the library's freeze at the end of its pass, in three phases.
    // Verify (writes nothing; a failure is a compiler-build defect, thrown as the internal-invariant form before any Frozen is written):
    // the library pass asked for no rerun (it is the same in every run, so a rerun would ask again), it has no record of any kind (its
    // issues, derived issues and non-failing records, warnings and lints included: each list is the pass's, which the program pass's
    // reset empties, so a layer record would be dropped or explained by another pass's tables; a siteless height refusal too: its
    // at-once report does not survive ReportDiagnostics, which invalidates the Binding partition and re-reports it only while the flag
    // holds, and the program pass's ResetPass clears the flag, so a library one would be dropped silently; the term goes with the flag's
    // Transitional, G651 a1. A startup record is never the library pass's: CheckStartup runs after a Final Bind, outside every pass,
    // over the source modules only; and, from PLAN G949 Stage 2 U2a (C1 (x)), no record in the library module's syntax list), else it
    // is refused: its records are reported by the per-record steps while its tables stand and kept as typed facts in the layer's
    // refusal (LibraryLayer.Refusal; a syntax record stays its module's list's, which the refusal only notes: form (R)), false
    // returned and nothing stamped; its query states are complete (LibraryQueryFault); and every conformance path it made current is
    // verified, none at its limit (ImplTable.UnsettledPath) and no verification open (the obligation table, ObligationDepth; PLAN G949
    // Stage 2 U3: no reading compilation verifies a frozen path). Stamp (field stores only, each owner its own): every stamp the run made current on a library object or base store turns
    // Frozen, so it reads current in every later pass; a stamp of an earlier run stays stale and reads absent, as the library pass left
    // it. Publish, last: L (LibraryLayer.TypeCount, the ids of the Types the library pass made), then the seal (Syntax.Freeze():
    // KimiLibrary.Frozen, from which the library's publication version is Frozen in every compilation, HirTables.LibraryVersion, and the
    // driver skips the library pass).
    private bool FreezeLibrary(bool completed)
    {
        if (!completed)
        {
            throw new InvalidOperationException("The library pass asked for a rerun (InternalInvariant; PLAN G949 S1-1).");
        }

        var diagnostics = this.compilation.Diagnostics;
        var syntax = diagnostics.HasSyntaxRecords(this.Library.Kotonoha);
        if (this.issues.Count + this.derivedIssues.Count != 0 || this.HasNonFailingRecords || this.sitelessHeightRefusal || syntax)
        {
            // The Binding partition holds only this pass's records (BeginBind discarded the semantic ones), so it is the refusal.
            this.ReportFailures(this.issues, this.derivedIssues);
            this.ReportDuplicateDeclarations();
            this.ReportNonFailingRecords();
            var records = diagnostics.TakeLocated(DiagnosticPartition.Binding);
            this.Library.Layer.Refusal = new(records, diagnostics.FirstSyntaxRecord(this.Library.Kotonoha) ?? records[0]);
            return false;
        }

        var symbols = this.LibraryQuerySymbols();
        if (this.LibraryQueryFault(symbols) is { } incomplete)
        {
            throw new InvalidOperationException($"The library pass left the query states of `{incomplete.Name}` incomplete (InternalInvariant; PLAN G949 S1-1).");
        }

        if (this.ObligationDepth != 0)
        {
            throw new InvalidOperationException("The library pass left a conformance verification open (InternalInvariant; PLAN G949 Stage 2 U3).");
        }

        if (this.Library.Layer.Impls.UnsettledPath() is { } unsettled)
        {
            throw new InvalidOperationException($"The library pass left the conformance path `{unsettled.Type.Name} is {unsettled.Contract.Name}` unverified (InternalInvariant; PLAN G949 Stage 2 U3).");
        }

        this.Library.Layer.Refusal = null;

        FreezeQueries(symbols);
        foreach (var symbol in this.symbols.Values.In(this.pass))
        {
            this.FreezeStorage(symbol.Adt);
        }

        this.Library.Layer.Impls.Freeze();
        this.Library.Kotonoha.Syntax.Hir.Families.Freeze(this);
        this.FreezeLocalRegions();
        this.FreezeRequirementWorks();
        this.FreezeKeepers();
        this.Library.Layer.TypeCount = this.types.TypeCount;
        this.Library.Kotonoha.Syntax.Freeze(); // Last: also the seal of the layer's interner levels (KimiLibrary.Frozen; PLAN G949 Stage 2 U4).
        return true;
    }

    // One pass over its modules: its reset, indexing, declarations, bodies, the bound-declaration validations and Check. False: its
    // declarations asked for a rerun (VerifyEarlyReads).
    private bool RunPass(BindingPass pass, BindingMode mode)
    {
        this.producing = true; // Each pass produces its failures from its start until its Check has counted them (Binding.Check.cs).
        this.StartPass(pass, mode);
        this.Index();
        if (!this.BindDeclarations(mode))
        {
            return false;
        }

        this.BindBodies();
        this.ValidateBoundDeclarations(mode);
        this.Check(mode);
        this.borrowsComplete = true; // RetentionState's complete value from here: Check's acquisitions are the last borrow writer (Binding.RawOrigins.Borrows.cs).
        if (pass.Library is not null)
        {
            // The library module's place in the pass's set, never which pass runs (RB1): the facts a later pass would otherwise form
            // lazily on a library object completed (PLAN G949 S1-1, B1: the effect bounds of every Contract the library declares or
            // references, the scopes' view serials, the local regions' sources, each its owner's step over the pass's own stores).
            this.CompleteEffectBounds();
            this.SettleViewSerials();
            this.CompleteLocalRegions();
            this.CompleteNegations(); // D-4 (PLAN G949 Stage 2 U2b): every library constraint's negation.
            pass.Library.Syntax.CompleteLazyFields(); // PLAN G1072: every library node's and document's lazily filled fields, before the freeze counts records.
        }

        return true;
    }

    // A pass's start: its part handed over and every per-pass fact of the latest pass over its modules discarded (the one reset site, which
    // ends the capability epoch): each pass's start, and a refused library pass's end, which leaves both parts empty (PLAN G949 Stage 2 U2a).
    private void StartPass(BindingPass pass, BindingMode mode)
    {
        this.borrowsComplete = false;
        this.EnterPass(pass);
        this.ResetPass(mode);
    }

    // Hands a pass its part: the fields every step writes become the part's lists.
    [MemberNotNull(nameof(nodes), nameof(issues), nameof(derivedIssues), nameof(obligations), nameof(obligationSet), nameof(libraryImports))]
    [MemberNotNull(nameof(virtualDeclarations), nameof(callableEffectUses), nameof(callableEffectClauses), nameof(localRegions), nameof(borrows))]
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
        this.borrows = part.Borrows;
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
    // ForeignFailures lists the nodes of a module the pass does not bind that it wrote, each with its state before and the state and
    // failure the pass recorded in place of the node's columns (the recorder, Binding.Diagnostics.Prerequisites.cs): their records and
    // counts are the failing pass's (CountStates). Counts is the pass's
    // contribution to the Bind's Result (ComposeResult), not a tally of the part's nodes, which only the views count: a program pass's
    // own nodes plus each foreign node's final state less its earlier one.
    internal sealed class PassPart
    {
        // The facts the failures in ForeignFailures carry, each at its entry's index (Binding.SetRecordedFact).
        private NodeColumns foreignFacts;

        internal List<Koto> Nodes { get; } = new();

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

        // The Origin-atom borrows by atom (Binding.RawOrigins.Borrows.cs), keyed by reference: formed while the pass's bodies bind,
        // checked and marked at its HIR publication.
        internal Dictionary<BoundOrigin, BorrowList> Borrows { get; } = new(ReferenceEqualityComparer.Instance);

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

        // The nodes outside the pass's modules that the pass wrote, in the order it first wrote them: each with its state before (its own
        // pass's), and the state and failure the pass recorded, which the recorder reads in place of its columns (RecordedState).
        internal List<(Koto Node, BindingState Before, BindingState State, BindingFailure Failure)> ForeignFailures { get; } = new();

        // The facts the failures of ForeignFailures carry, at their entries' indexes: a pass family's row, stamped by the pass as every
        // pass family's (the list and these rows end with the pass).
        internal ref NodeColumns ForeignFacts => ref this.foreignFacts;

        internal BindingResult Counts { get; set; }

        // The one find of a keyed collection over its levels (Levels: a part's, the layer's then the running pass's; an interner's, the
        // layer's then the compilation's once the library is frozen). Returns the map that holds the key, null when none does.
        internal static Dictionary<TKey, TValue>? Find<TKey, TValue>(Levels<Dictionary<TKey, TValue>> levels, TKey key, out TValue? value)
            where TKey : notnull
        {
            foreach (var level in levels)
            {
                if (level.TryGetValue(key, out value))
                {
                    return level;
                }
            }

            value = default;
            return null;
        }

        // A keyed part's levels (Regions, Keepers, BoundContracts): the layer's part, then the running pass's, its open level (the
        // layer's own in the library pass).
        internal static Levels<T> Parts<T>(T layer, T running)
            where T : class
            => new(layer, running);

        // An interner's levels (TyCtxt: Types, substitutions; Binding: Origin intersections, lengths, constraints; PLAN G949 Stage 2 U4):
        // its library layer's frozen level, then the compilation's, its open level once the library is frozen (KimiLibrary.Frozen, the
        // seal, Binding.FreezeLibrary) and the layer's own before, so equal content is one object in both levels and nothing enters the
        // layer's after the seal.
        internal static Levels<T> Interned<T>(KimiLibrary library, T layer, T own)
            where T : class
            => new(layer, library.Frozen ? own : layer);

        // The levels of a two-level store in their one order (PLAN G949 Stage 2 U4; Parts, Interned): the layer's, then the open level
        // when it is not the layer's; Open is where a key no level holds goes. A struct enumerator: a find allocates nothing.
        internal readonly struct Levels<T>(T layer, T open)
            where T : class
        {
            internal T Open => open;

            public Enumerator GetEnumerator() => new(layer, open);

            internal struct Enumerator(T layer, T open)
            {
                private int index = -1;

                public readonly T Current => this.index == 0 ? layer : open;

                public bool MoveNext() => ++this.index == 0 || (this.index == 1 && !ReferenceEquals(layer, open));
            }
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
