// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Binding (P2-P5s, COMPILER_ARCHITECTURE "Target pipeline"): the entry of the pass that resolves the selected syntax and publishes its facts.
// - Responsibility: one pass per Bind(mode) in the order written here: SyntaxId numbering, ResetPass, IndexSources, IndexLibrary,
//   BindDeclarations, BindBodies, ValidateBoundDeclarations, Check, WalkLints in a Final pass, and the conformance table's sweep
//   (ImplTable.Sweep); CheckBound repeats Check on a Final
//   pass; Invalidate revokes the latest pass after an edit. Each step lives in its concern's file (Binding.<Concern>.cs); this file
//   holds the entry points, the state the concerns share and the pass order.
// - Inputs: the compilation's source modules, the embedded Kimi library (Library) and the mode.
// - Outputs: Result, Issues, Obligations, LibraryImports, the Type context (Types), the facts published to readers outside Binding
//   (Semantics; the HIR tables and Plans), and the diagnostics ReportDiagnostics records (Binding.Diagnostics.Report.cs).
// - Invariants: a pass is never reentered; a pass that does not finish leaves no Result, and the later phases' facts are discarded
//   when a pass starts; storage is reused across passes (ResetPass, Binding.Pass.cs).
// - Prohibitions: no read of Mir or the Backend (COMPILER_ARCHITECTURE "Dependency rules"); readers outside Binding use its published
//   facts, never its private state.

/// <summary>Owns reusable Binding storage and updates semantic fields on the existing Koto tree.</summary>
public sealed partial class Binding
{
    private readonly Compilation compilation;
    private readonly List<Koto> nodes = new(KimiLibrarySize.NodeCapacity(KimiLibrarySize.Stated.Nodes));
    private readonly KeptScopes scopes;

    private readonly DeclaredSymbols symbols;
    private readonly List<BindingIssue> issues = new();
    private readonly List<LibraryImport> libraryImports = new();
    private readonly TyCtxt types;
    private readonly List<BindingObligation> obligations = new();
    private readonly HashSet<BindingObligation> obligationSet = new();
    private bool running;

    // The edit counter: Invalidate alone advances it, never a pass, so a store's edit columns (NodeColumns.EditStamp) hold across passes
    // and read absent after an edit until their first write.
    private ulong edit;

    internal Binding(Compilation compilation)
    {
        this.compilation = compilation;
        this.indexer = new(this);
        this.Library = new(compilation);
        this.impls = new(this.Library.Layer.Impls);
        this.types = new(this, this.Library);
        this.symbols = new(this);
        this.scopes = new(this);
        this.originAtoms = new(this);
        this.associatedOrigins = new(this);
        this.Library.Kotonoha.Syntax.Hir.Families.Reserve(KimiLibrarySize.Stated);
        foreach (var symbol in this.Library.RegisteredSymbols)
        {
            this.symbols.Add(symbol.Declaration, symbol);
        }
    }

    /// <summary>Gets the latest pass summary. Results are replaced by the next Bind.</summary>
    public BindingResult Result { get; private set; }

    /// <summary>Gets the compiler-designated requirement identities for this compilation.</summary>
    public KimiLibrary Library { get; }

    /// <summary>Gets final failures; provisional passes do not publish missing-name diagnostics.</summary>
    public IReadOnlyList<BindingIssue> Issues => this.issues;

    /// <summary>Gets requirements to discharge during subsequent semantic analysis.</summary>
    public IReadOnlyList<BindingObligation> Obligations => this.obligations;

    /// <summary>Gets the valid foreign import declarations of the latest pass (SPEC 22.6), in source order.</summary>
    internal IReadOnlyList<LibraryImport> LibraryImports => this.libraryImports;

    /// <summary>Gets the facts this Binding publishes to readers outside Binding.</summary>
    internal SemanticQuery Semantics => field ??= new(this.compilation, this, this.nodes, this.symbols, this.impls);

    /// <summary>Gets the Type context: interned Types and substitutions.</summary>
    internal TyCtxt Types => this.types;

    /// <summary>Gets the entries of the tables KimiLibrarySize sizes, as the latest pass left them.</summary>
    internal KimiLibrarySize.Tables TableCounts
        => new(
            this.nodes.Count,
            this.Library.Kotonoha.Syntax.Hir.Families.Symbols.Used,
            this.Library.Kotonoha.Syntax.Hir.Families.Parameters.Used,
            this.Library.Kotonoha.Syntax.Hir.Families.Scopes.Used,
            this.Library.Kotonoha.Syntax.Hir.Families.OriginAtoms.Used,
            this.Library.Kotonoha.Syntax.Hir.Families.AssociatedOrigins.Used,
            this.types.KeyCount,
            this.constraints.Count,
            this.pendingAssociatedTypes.Count,
            this.memberSelections.Count,
            this.memberSides.Count,
            this.capabilityNodes.Count,
            this.substitutedParts.Count);

    /// <summary>Gets the free slots of the tables KimiLibrarySize sizes: each table's capacity less its entries; a node family's column
    /// (symbols, their parameters, scopes, Origin atoms, associated Origins) its arena's slots no page holds.</summary>
    internal KimiLibrarySize.Tables TableFree
        => new(
            this.nodes.Capacity - this.nodes.Count,
            this.Library.Kotonoha.Syntax.Hir.Families.Symbols.Free,
            this.Library.Kotonoha.Syntax.Hir.Families.Parameters.Free,
            this.Library.Kotonoha.Syntax.Hir.Families.Scopes.Free,
            this.Library.Kotonoha.Syntax.Hir.Families.OriginAtoms.Free,
            this.Library.Kotonoha.Syntax.Hir.Families.AssociatedOrigins.Free,
            this.types.KeyFree,
            this.constraints.Capacity - this.constraints.Count,
            this.pendingAssociatedTypes.Capacity - this.pendingAssociatedTypes.Count,
            this.memberSelections.Capacity - this.memberSelections.Count,
            this.memberSides.Capacity - this.memberSides.Count,
            this.capabilityNodes.Capacity - this.capabilityNodes.Count,
            this.substitutedParts.Free);

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

        // Check publishes both kinds of failure again.
        this.issues.Clear();
        this.derivedIssues.Clear();
        return this.Result = this.Check(BindingMode.Final);
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
            do
            {
                this.ResetPass(mode);
                this.IndexSources();
                this.IndexLibrary();
            }
            while (!this.BindDeclarations(mode));

            this.BindBodies();
            this.ValidateBoundDeclarations(mode);
            this.Result = this.Check(mode);
            if (mode == BindingMode.Final)
            {
                this.WalkLints();
            }

            this.impls.Sweep(); // No identity or path an edit removed is kept (PLAN G949, DECISIONS BU-220).
            this.knownMode = mode; // knownBases, if any, are this completed Bind's (StartRuns).
            return this.Result;
        }
        finally
        {
            this.running = false;
        }
    }

    internal bool IsRunning => this.running;

    internal void Invalidate()
    {
        // A Bind in progress rebuilds everything it published; clearing its state midway would lose its own issues.
        if (this.running)
        {
            return;
        }

        // An edit revokes the whole pass, exactly as the next Bind discards it, unless no pass has finished since the last revocation.
        if (this.Result != default)
        {
            this.Result = default;
            this.ResetPass(BindingMode.Provisional);
        }

        // The facts kept across unchanged passes are revoked by every edit, also after a pass that did not finish (a Bind that throws
        // leaves no result): associated Origin parameters of the edited trees (the edit families: the edit counter's advance leaves every
        // module's edit columns stale, NodeColumns.EditStamp), local regions, item contexts and the Type context's view memos.
        this.edit++;
        this.knownBases?.Clear();
        this.localRegions.Clear();
        this.functionItemContexts.Clear();
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
        this.ValidateConstraintEnvironments(mode);
        this.PrepareSpecializations();
        this.CompleteImplicitConstructors();
        this.MakeLibraryAccessorFunctions(); // The library's facts a program may demand, complete before any body (PLAN G949 S1-e).
        this.FormLibrarySignatures();
        if (this.CaptureOmittedBaseQueries)
        {
            this.CaptureOmittedBases();
        }

        return true;
    }

    // Source bodies, then the library bodies outside the compiler-intrinsic declarations.
    private void BindBodies()
    {
        foreach (var module in this.compilation.SourceModules)
        {
            this.BindNode(module.RootKoto, this.scopes[module.RootKoto]);
        }

        var libraryRoot = this.Library.Kotonoha.RootKoto;
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
}
