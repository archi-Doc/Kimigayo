// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Binding (P2-P5s, COMPILER_ARCHITECTURE "Target pipeline"): the entry of the pass that resolves the selected syntax and publishes its facts.
// - Responsibility: Bind(mode): its runs and passes through the pass driver (Binding.Passes.cs: BeginBind, then each run's BeginRun
//   and passes, each pass ResetPass, Index, BindDeclarations, BindBodies, ValidateBoundDeclarations and Check over its modules),
//   then WalkLints in a Final Bind and the conformance table's sweep (ImplTable.Sweep); CheckBound repeats Check on a Final pass;
//   Invalidate revokes the latest pass after an edit. Each step lives in its concern's file (Binding.<Concern>.cs); this file holds the
//   entry points, the state the concerns share and the order of the declaration, body and validation steps.
// - Inputs: the compilation's source modules, the embedded Kimi library (Library) and the mode.
// - Outputs: Result, Issues, Obligations, LibraryImports, the Type context (Types), the facts published to readers outside Binding
//   (Semantics; the HIR tables and Plans), and the diagnostics ReportDiagnostics records (Binding.Diagnostics.Report.cs).
// - Invariants: a pass is never reentered; a pass that does not finish leaves no Result, and the later phases' facts are discarded
//   when a pass starts; storage is reused across passes (ResetPass, Binding.Pass.cs). The published per-pass lists (nodes, issues,
//   derived issues, obligations, imports, the effect obligations' sources, the Contract shapes, the effect bounds and the import
//   declarations) and the slots' keepers are parts (PassPart, Binding.Passes.cs): a pass writes the part the driver hands it, the
//   library pass LibraryLayer.Part and the program pass Binding's own (PLAN G949 S1-r1b), and a reader outside a pass reads the
//   composed views, both parts.
// - Prohibitions: no read of Mir or the Backend (COMPILER_ARCHITECTURE "Dependency rules"); readers outside Binding use its published
//   facts, never its private state.

/// <summary>Owns reusable Binding storage and updates semantic fields on the existing Koto tree.</summary>
public sealed partial class Binding
{
    private readonly Compilation compilation;
    private readonly KeptScopes scopes;

    private readonly DeclaredSymbols symbols;
    private readonly InferenceBatches associatedInference;
    private readonly TyCtxt types;

    // The running pass's part (PassPart), which its steps write; the driver hands it over (EnterPass), Binding's own between Binds.
    private List<Koto> nodes;
    private List<BindingIssue> issues;
    private List<LibraryImport> libraryImports;
    private List<BindingObligation> obligations;
    private HashSet<BindingObligation> obligationSet;
    private List<(Koto Node, BindingState Before, BindingState State, BindingFailure Failure)> foreignFailures;
    private bool running;

    // The edit counter: Invalidate alone advances it, never a pass, so an edit column (StampedColumn following EditCounter) holds across
    // passes and reads absent after an edit until its first write.
    private ulong edit;

    internal Binding(Compilation compilation, KimiLibrary library)
    {
        if (library.Target != compilation.TargetTriple)
        {
            throw new InvalidOperationException($"A Binding for `{compilation.TargetTriple.Value}` is given the library of `{library.Target.Value}` (InternalInvariant; PLAN G949 Stage 2 U3).");
        }

        this.compilation = compilation;
        this.Library = library;
        this.impls = new(this.Library.Layer.Impls);
        this.types = new(this, this.Library);
        this.symbols = new(this);
        this.scopes = new(this);
        this.originAtoms = new(this);
        this.openOrigins = new(this);
        this.associatedOrigins = new(this);
        this.shapeContractHeads = new(this);
        this.objectErasures = new(this);
        this.enumConstructions = new(this);
        this.copyDeclarations = new(this);
        this.copyByType = new(this);
        this.inheritedOriginTypes = new(this);
        this.deferredConstraints = new(this);
        this.matches = new(this);
        this.adaptations = new(this);
        this.associatedInference = new(this);
        this.implicitConstructorDecisions = new(this);
        this.specializationsByOriginal = new(this);
        this.specializations = new(this);
        this.virtualEffectBounds = new(this);
        this.overrideEntries = new(this);
        this.virtualOverrides = new(this);
        this.originDeclarations = new(this);
        this.discoveredOrigins = new(this);
        this.initializerOrigins = new(this);
        this.propertyCalls = new(this);
        this.storageProjections = new(this);
        this.propertyUpdateStorage = new(this);
        this.propertyUpdateInputs = new(this);
        this.indexerCalls = new(this);
        this.viewRangeCalls = new(this);
        this.rangeCalls = new(this);
        this.resolvedKeys = new(this);
        this.aliasTargets = new(this);
        this.importedContainers = new(this);
        this.qualificationFailures = new(this);
        this.perCallOrigins = new(this);
        this.pairFollows = new(this);
        this.implicitPairFollows = new(this);
        this.phantomRawOrigins = new(this);
        this.invalidStaticOriginSlots = new(this);
        this.effectBoundRejections = new(this);
        this.receiverOperations = new(this);
        this.reachedThroughBorrow = new(this);
        this.inferredArrayLengths = new(this);
        this.arrayInferenceShapes = new(this);
        this.baseCallFailures = new(this);
        this.objectErasureFailures = new(this);
        this.resultContexts = new(this);
        this.callableConstraints = new(this);
        this.uniqIndexing = new(this);
        this.requirementGroups = new(this);
        this.formationCauses = new(this);
        this.knownBases = new(this);
        this.earlyReads = new(this);
        this.resolvedSlices = new(this);
        this.matchPlans = new(this);
        this.indexedPatterns = new(this);
        this.prerequisites = new(this);
        this.partPrerequisites = new(this);
        this.prerequisiteVisits = new(this);
        this.duplicateDeclarations = new(this);
        this.resolvingTypes = new(this);
        this.synthesizedCalls = new(this);
        var (own, layer) = (this.programPart, this.Library.Layer.Part);
        this.nodesView = new(own.Nodes, layer.Nodes);
        this.issuesView = new(own.Issues, layer.Issues);
        this.derivedIssuesView = new(own.DerivedIssues, layer.DerivedIssues);
        this.obligationsView = new(own.Obligations, layer.Obligations);
        this.libraryImportsView = new(own.Imports, layer.Imports);
        this.virtualDeclarationsView = new(own.VirtualDeclarations, layer.VirtualDeclarations);
        this.callableEffectUsesView = new(own.CallableEffectUses, layer.CallableEffectUses);
        this.contractShapesView = new(own.ContractShapes, layer.ContractShapes);
        this.boundedRequirementsView = new(own.BoundedRequirements, layer.BoundedRequirements);
        this.importDeclarationsView = new(own.ImportDeclarations, layer.ImportDeclarations);
        (this.Issues, this.Obligations, this.LibraryImports, this.DerivedIssues) = (this.issuesView, this.obligationsView, this.libraryImportsView, this.derivedIssuesView);
        this.ImportDeclarations = this.importDeclarationsView;
        this.EnterPass(this.ProgramPass);
    }

    /// <summary>Gets the latest pass summary. Results are replaced by the next Bind.</summary>
    public BindingResult Result { get; private set; }

    /// <summary>Gets the compiler-designated requirement identities for this compilation.</summary>
    public KimiLibrary Library { get; }

    /// <summary>Gets final failures; provisional passes do not publish missing-name diagnostics.</summary>
    public IReadOnlyList<BindingIssue> Issues { get; }

    /// <summary>Gets requirements to discharge during subsequent semantic analysis.</summary>
    public IReadOnlyList<BindingObligation> Obligations { get; }

    /// <summary>Gets the valid foreign import declarations of the latest pass (SPEC 22.6), in source order.</summary>
    internal IReadOnlyList<LibraryImport> LibraryImports { get; }

    /// <summary>Gets the facts this Binding publishes to readers outside Binding: the program pass's nodes (BoundNodes).</summary>
    internal SemanticQuery Semantics => field ??= new(this.compilation, this, this.programPart.Nodes, this.symbols, this.impls);

    /// <summary>Gets the Type context: interned Types and substitutions.</summary>
    internal TyCtxt Types => this.types;

    /// <summary>Checks the latest final Binding without resolving names or rebuilding the tree.</summary>
    /// <returns>The final semantic completeness summary.</returns>
    public BindingResult CheckBound()
    {
        if (!CompilerThread.IsCurrent)
        {
            return CompilerThread.Run(this, static s => s.CheckBound());
        }

        if (this.Result.Mode != BindingMode.Final)
        {
            throw new InvalidOperationException("Bound checking requires a final Binding pass.");
        }

        // The program pass's Check publishes both kinds of failure again, on the part the fields hold between Binds, Binding's own; the
        // library pass's records stay in the layer's part.
        this.issues.Clear();
        this.derivedIssues.Clear();
        this.Check(BindingMode.Final);
        return this.Result = this.ComposeResult();
    }

    /// <summary>Rebinds all selected syntax, reusing nodes, symbols, types, scopes, and collection capacity.</summary>
    /// <param name="mode">Whether this is a provisional or final pass.</param>
    /// <returns>The current pass summary.</returns>
    public BindingResult Bind(BindingMode mode)
    {
        if (!CompilerThread.IsCurrent)
        {
            return CompilerThread.Run((this, mode), static s => s.Item1.Bind(s.mode));
        }

        if (this.running)
        {
            throw new InvalidOperationException("Binding cannot be reentered.");
        }

        this.running = true;
        try
        {
            this.BeginBind(mode);
            if (this.Run(mode))
            {
                this.Result = this.ComposeResult();
                if (mode == BindingMode.Final)
                {
                    this.WalkLints();
                }

                this.knownMode = mode; // knownBases, if any, are this completed Bind's (StartRuns).
            }

            // A refused library pass (Run false; PLAN G949 Stage 2 U2a, C2) leaves no Result, as a pass that does not finish.
            this.impls.Sweep(); // No identity or path an edit removed is kept (PLAN G949, DECISIONS BU-220).
            return this.Result;
        }
        finally
        {
            this.EnterPass(this.ProgramPass); // Between Binds, and after a throw, the fields hold Binding's own part.
            this.running = false;
            this.producing = false; // A pass that did not finish produced none.
        }
    }

    internal bool IsRunning => this.running;

    // Whether a node's plans are the current pass's: the node resolved by the running or the last completed pass, not an earlier one.
    internal bool IsCurrent(Koto node) => HirTables.State(node) == BindingState.Resolved && (this.running || this.Result != default);

    internal void Invalidate()
    {
        // A Bind in progress rebuilds everything it published; clearing its state midway would lose its own issues.
        if (this.running)
        {
            return;
        }

        // An edit revokes the program pass of the latest Bind, exactly as the next Bind discards it, unless no pass has finished since the
        // last revocation; the frozen library is never revoked (PLAN G949 S1-1: a source edit never reaches it). Between Binds the
        // fields hold the program pass's part.
        if (this.Result != default)
        {
            this.Result = default;
            this.BeginRun();
            this.ResetPass(BindingMode.Provisional);
        }

        // The facts kept across unchanged passes are revoked by every edit, also after a pass that did not finish (a Bind that throws
        // leaves no result): associated Origin parameters of the edited trees (the edit families: the edit counter's advance leaves every
        // module's edit columns stale, StampedColumn), local regions, item contexts with the atoms their plans hold at library binders
        // (programAtoms, G949 ruling 205 N4) and the Type context's view memos.
        this.edit++;
        this.knownBases.Empty();
        this.localRegions.Clear();
        this.functionItemContexts.Clear();
        this.programAtoms.Empty();
        this.types.ResetViews();
    }

    // Declarations, Constraints, Contracts, headers, storage and signatures, before any body is bound. False: run them again (VerifyEarlyReads).
    private bool BindDeclarations(BindingMode mode)
    {
        // Source guards are all indexed now; a removed guard may have become an arm body.
        // Try arms are indexed later, so retain their Pattern scopes until binding finishes.
        this.PruneCandidateScopes();
        this.ValidateDefaultAliases();
        this.PrepareOriginDeclarations();
        this.BindSchemas();
        this.PrepareAssociatedOrigins();
        this.PrepareAliases();
        this.PrepareContracts();
        this.BindConstraints();
        this.clausesBindable = true;
        this.PublishInheritedOptOuts();
        this.VerifyEarlyObjectForms();
        this.RegisterInheritedConformances();
        this.BindTypeOriginContracts();
        this.PrepareAssociatedInference();
        this.PrepareCases();
        for (var i = 0; i < this.nodes.Count; i++)
        {
            if (this.nodes[i].BoundSymbol is { Kind: BindingSymbolKind.Function or BindingSymbolKind.Property } symbol && ReferenceEquals(symbol.Declaration, this.nodes[i]))
            {
                this.BindHeader(symbol);
            }
        }

        this.PrepareBuiltinTargets();
        this.ValidateVirtualDeclarations();
        this.ValidateLayoutFragments();
        this.ValidateBaseDeclarations();
        if (this.VerifyEarlyReads())
        {
            return false;
        }

        this.PrepareStorage();
        // SPEC 5.7, 15.3.5: the slots' requirements and keepers exist before inline layouts substitute instances.
        this.ComputeOriginRequirements();
        this.PrepareInheritedScopes(); // SPEC 8.8.2: the heir scopes before their readers (inline layouts, verification).
        this.ValidateInlineLayouts();
        this.ValidateLibraryImports(); // Reads the C-layout fact and the prepared storage (SPEC 22.6).
        this.ValidateSignatures();
        this.PrepareVirtualOverrides();
        this.ValidateContractDeclarations();
        this.PrepareEffectBounds();
        this.capabilitiesReady = true;
        this.ValidateConformances(mode, false);
        this.VerifyEarlyProjections(); // The proofs answer from here on (contractHeadersReady).
        this.SettleVirtualOverridePremises(); // R7-C2c2 c2-4: a refuted inherited condition fails the override (SPEC 6.4.3).
        this.SettlePremiseFailures(mode);
        this.PrepareSpecializations();
        this.RecordDuplicateGroups(); // After PrepareSpecializations, the pass's last pairwise duplicate check (Index declared every scope).
        this.CompleteImplicitConstructors();
        this.MakeLibraryAccessorFunctions(); // The library's facts a program may demand, complete before any body (PLAN G949 S1-e).
        this.FormLibrarySignatures();
        return true;
    }

    // The pass's bodies: its source modules', then, when it binds the library, the library's outside the compiler-intrinsic declarations.
    private void BindBodies()
    {
        foreach (var module in this.pass.Sources)
        {
            this.BindNode(module.RootKoto, this.scopes[module.RootKoto]);
        }

        if (this.pass.Library is not { } library)
        {
            return;
        }

        var libraryRoot = library.RootKoto;
        for (var i = 0; i < libraryRoot.NestedContainers.Count; i++)
        {
            var declaration = libraryRoot.NestedContainers[i];
            if (declaration.BoundSymbol?.Intrinsic == IntrinsicKind.None)
            {
                this.BindNode(declaration, this.Library.Scope);
            }
        }

        for (var i = 0; i < libraryRoot.Members.Count; i++)
        {
            this.BindNode(libraryRoot.Members[i], this.Library.Scope);
        }
    }

    // BindBodies' walk into a container, part of the pass order: its members, nested Types, generated function and implicit constructor.
    // SPEC 23.1.2 (DECISIONS G52-1): each is its own declaration, and the container's verdict reads no binding result of them, so they are
    // bound with its consultation frame suspended: none is its prerequisite, and one that rests on a syntax recovery leaves the container's
    // own record. Its generic parameters, bases and Constraints stay in the frame (BindNodeCore), since its own checks read them.
    private void BindContainedDeclarations(DeclarationContainerKoto container, BindingScope scope)
    {
        var consultation = this.SuspendConsultation();
        try
        {
            for (var i = 0; i < container.Members.Count; i++)
            {
                this.BindNode(container.Members[i], scope);
            }

            for (var i = 0; i < container.NestedContainers.Count; i++)
            {
                this.BindNode(container.NestedContainers[i], scope);
            }

            if (container.IsRoot && container.Kotonoha.GeneratedFunction is { } generated)
            {
                this.BindNode(generated, scope);
            }

            if (container is StructKoto { ImplicitConstructor: { } constructor })
            {
                this.BindNode(constructor, scope);
            }
        }
        finally
        {
            this.ResumeConsultation(consultation);
        }
    }

    // Declaration checks that need the bound bodies: Origin requirements, API access, certificates and witnesses.
    private void ValidateBoundDeclarations(BindingMode mode)
    {
        this.PrunePatternScopes();
        this.PruneMatchPlans();
        this.ClearCapabilityResults();
        this.ValidateOriginRelations();
        this.ValidateAssociatedApplications();
        this.ValidateCopyDeclarations(mode);
        this.ComputeOriginRequirements();
        this.ValidateOriginRequirements();
        this.ValidateApiAccess(mode);
        // Base constraints need capability evidence; propagate failures before certificates.
        this.ValidateBaseDeclarations(mode);
        this.ReportInheritedInlineCycles(); // After the base clauses' own checks (DIAGNOSTICS rule 4).
        this.RevalidateImplicitConstructors();
        // Property certificates must include final Origin and declaration API validity.
        this.ValidateProperties(mode);
        this.ValidateConformances(mode, true);
        this.ValidateConstraintUses(mode);
        this.ClearCapabilityResults();
        this.CollectCallableEffectUses();
        this.InvalidateProjectionCertificates(mode);
        this.ValidateIterationWitnesses();
        this.ValidateExpressionProjectionInputs(mode);
        this.CompleteEnumAcquisitions();
        this.CompletePatternAcquisitions();
        this.CompleteIterationAcquisitions();
        if (mode == BindingMode.Final)
        {
            this.CompleteAliasWarnings();
        }

        this.ValidateVirtualOverrideConditions();
        this.GuardPendingVirtualDeclarations();
    }

    /// <summary>The pass counter a pass family's column follows (StampedColumn, DECISIONS BU-220): storageVersion, which ResetPass
    /// advances.</summary>
    internal readonly struct PassCounter : IStampCounter
    {
        public static ulong Of(Binding binding) => binding.storageVersion;
    }

    /// <summary>The edit counter an edit family's column follows (StampedColumn, DECISIONS BU-220): edit, which only Invalidate
    /// advances.</summary>
    internal readonly struct EditCounter : IStampCounter
    {
        public static ulong Of(Binding binding) => binding.edit;
    }
}
