// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

/// <summary>Owns reusable Binding storage and updates semantic fields on the existing Koto tree.</summary>
public sealed partial class Binding
{
    private readonly Compilation compilation;
    private readonly List<Koto> nodes = new(256);
    private readonly Dictionary<object, BindingScope> scopes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, BindingSymbol> symbols = new(ReferenceEqualityComparer.Instance);
    private readonly List<AliasKoto> aliases = new();
    private readonly List<BindingIssue> issues = new();
    private readonly List<LibraryImport> libraryImports = new();
    private readonly Dictionary<string, (string Signature, string? Kind)> importSymbols = new(StringComparer.Ordinal);
    private readonly List<string?> importSignatures = new();
    private readonly IndexVisitor indexer;
    private readonly Dictionary<int, List<BoundType>> types = new();
    private readonly Dictionary<GenericParameterKoto, BindingSymbol> pairSymbols = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(Koto Binder, OriginKind Kind, int Slot), BoundOrigin> originAtoms = new();
    private readonly Dictionary<int, List<BoundOrigin>> originExpressions = new();
    private readonly List<BindingObligation> obligations = new();
    private readonly HashSet<BindingObligation> obligationSet = new();
    private readonly HashSet<Koto> resolvingTypes = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<BindingSymbol> borrowVisiting = new(ReferenceEqualityComparer.Instance);
    private Dictionary<Kotonoha, BindingSymbol>? moduleSymbols;
    private TestSyntaxVisitor? testSyntaxVisitor;
    private bool running;
    private bool kimiValid;

    internal Binding(Compilation compilation)
    {
        this.compilation = compilation;
        this.indexer = new(this);
        this.TypeSystem = new BindingControlFlowTypes(this);
        this.Library = new(compilation);
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

    /// <summary>Gets the valid foreign import declarations of the latest pass (SPEC 22.3), in source order.</summary>
    internal IReadOnlyList<LibraryImport> LibraryImports => this.libraryImports;

    /// <summary>Checks the latest final Binding without resolving names or rebuilding the tree.</summary>
    /// <returns>The final semantic completeness summary.</returns>
    public BindingResult CheckBound()
    {
        if (this.Result.Mode != BindingMode.Final)
        {
            throw new InvalidOperationException("Bound checking requires a final Binding pass.");
        }

        // Check publishes both kinds of failure again.
        this.issues.Clear();
        this.derivedIssues.Clear();
        return this.Result = this.Check(BindingMode.Final);
    }

    /// <summary>Gets the bridge supplying actual Binding facts to control-flow analysis.</summary>
    public ControlFlowTypeSystem TypeSystem { get; }

    /// <summary>Rebinds all selected syntax, reusing nodes, symbols, types, scopes, and collection capacity.</summary>
    /// <param name="mode">Whether this is a provisional or final pass.</param>
    /// <returns>The current pass summary.</returns>
    public BindingResult Bind(BindingMode mode)
    {
        if (this.running)
        {
            throw new InvalidOperationException("Binding cannot be reentered.");
        }

        this.running = true;
        try
        {
            // Every later phase rests on Binding, so its facts are discarded with Binding's; a pass that does not finish
            // leaves no result behind.
            this.Result = default;
            this.compilation.Diagnostics.InvalidateSemantics();
            this.compilation.InvalidateOwnership();
            this.ResetPass(mode);
            this.IndexSources();
            this.kimiValid = this.IndexLibrary();
            if (this.kimiValid)
            {
                this.BindDeclarations(mode);
                this.BindBodies();
                this.ValidateBoundDeclarations(mode);
                this.kimiValid = this.Library.ValidateBoundDeclarations();
            }
            else
            {
                // A malformed compiler library must not enter indexing/overload chains; only the indexed sources are pruned.
                this.PruneCandidateScopes();
                this.PrunePatternScopes();
                this.PruneMatchPlans();
            }

            if (!this.kimiValid)
            {
                this.Fail(this.compilation.Kotonoha.RootKoto, BindingFailure.InvalidKimi);
            }

            return this.Result = this.Check(mode);
        }
        finally
        {
            this.running = false;
        }
    }

    /// <summary>Records Binding's facts, replacing any it recorded before. Invoke after final analysis, not between Mods.</summary>
    public void ReportDiagnostics()
    {
        var diagnostics = this.compilation.Diagnostics;
        diagnostics.Invalidate(DiagnosticPartition.Binding);
        for (var i = 0; i < this.issues.Count; i++)
        {
            var issue = this.issues[i];
            if (issue.Failure == BindingFailure.Duplicate && this.duplicateDeclarations?.ContainsKey(issue.Node) == true)
            {
                continue; // Pairwise declaration failures are normalized as one group below.
            }

            this.ReportIssue(issue);
        }

        foreach (var node in this.derivedIssues)
        {
            node.ReportDerived(DiagnosticRequirement.Binding(node.BindingFailure), this.PrerequisiteKeys(node));
        }

        if (this.Result.Mode == BindingMode.Final)
        {
            this.ReportDuplicateDeclarations();
            var warning = DiagnosticRequirement.Binding(BindingFailure.None);
            foreach (var alias in this.aliasWarnings)
            {
                alias.Report(warning, DiagnosticCode.HiddenNamedAlias_Kd);
            }

            foreach (var pattern in this.patternWarnings)
            {
                pattern.Pattern.Report(warning, DiagnosticCode.UnreachablePattern_Kd, pattern.CoveringArm + 1);
            }

            foreach (var position in this.positionWarnings)
            {
                position.Node.Report(warning, DiagnosticCode.PositionAlwaysFails_Kd, position.Kind, evidence: position.FixedLength < 0 ? null : [position.FixedLength]);
            }

            // SPEC 23.3.3: an incomplete Binding without an Error in it or an earlier phase reports one fallback at its
            // first incomplete node. It marks a missing report, a compiler defect to repair where it occurs.
            if (!this.Result.IsComplete && !diagnostics.HasErrorsThrough(DiagnosticPartition.Binding) &&
                this.nodes.Find(static x => x.BindingState != BindingState.Resolved) is { } first)
            {
                first.ReportDerived(DiagnosticRequirement.Binding(first.BindingFailure == BindingFailure.None ? BindingFailure.MissingType : first.BindingFailure), [DiagnosticKey.Unresolved]);
            }
        }
    }

    internal BindingSymbol ParameterSymbol(FunctionKoto function, int index) => function.Accessor is { } accessor
        ? index == 0 && accessor.Receiver is not null ? accessor.SelfSymbol! : accessor.ValueSymbol!
        : this.symbols[function.Parameters[index]];

    internal bool IsRunning => this.running;

    internal void Invalidate()
    {
        // A Bind in progress rebuilds everything it published; clearing its state midway would lose its own issues.
        if (this.Result == default || this.running)
        {
            return;
        }

        // An edit revokes the whole pass, exactly as the next Bind discards it; associated Origin parameters of the edited
        // trees are recreated.
        this.Result = default;
        this.ResetPass(BindingMode.Provisional);
        this.associatedOrigins.Clear();
        this.localRegions.Clear();
    }

    private static bool InvalidDeclarationContext(Koto declaration)
        => InvalidDeclarationContextCause(declaration) is not null;

    // The nearest enclosing declaration (or conditional conformance) that failed; a member's check in that context rests on it.
    // A declaration whose only failure is a form rule (IsFormFailure) keeps a valid signature and body, so it is no such context.
    private static Koto? InvalidDeclarationContextCause(Koto declaration)
    {
        for (Koto? node = declaration; node is not null; node = node.Parent)
        {
            if ((node is DeclarationKoto or SyntaxFormKoto { Akind: KotoKind.ConditionalConformance }) && node.BindingState == BindingState.Invalid && !IsFormFailure(node))
            {
                return node;
            }
        }

        return null;
    }

    private static bool UnresolvedTypeDeclarationContext(Koto declaration)
    {
        for (Koto? node = declaration; node is not null; node = node.Parent)
        {
            if ((node is StructKoto or EnumKoto or ContractKoto && node.BindingState == BindingState.Unresolved) || node is ContractKoto { BoundSymbol.Contract.HasUnresolvedParents: true })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gets the receiver shape of a function with a receiver (SPEC 7.3): its receiver's Semantics, or null.</summary>
    private static SemanticsKind? ReceiverShape(BindingSymbol symbol)
        => symbol.ReceiverIndex >= 0 && symbol.Declaration is FunctionKoto { IsSpecialization: false } function &&
            function.Parameters[symbol.ReceiverIndex].Type.BoundType is { } receiver ? receiver.Semantics : null;

    // The target of the plain numeric conversion whose direct literal operand failed to fit, if the node is such a literal.
    private static BoundType? LiteralConversionTarget(Koto node)
    {
        if (node is not (NumberLiteralKoto or PrefixMinusKoto { Operand: NumberLiteralKoto } or PrefixPlusKoto { Operand: NumberLiteralKoto }))
        {
            return null;
        }

        var parent = node.Parent;
        while (parent is ParenthesizedKoto)
        {
            parent = parent.Parent;
        }

        return parent is ConversionKoto { Right: not TypeSemanticsKoto { ConversionOperation: not null } } conversion ? conversion.Right.BoundType : null;
    }

    private static BoundType? Complete(Koto node, BoundType? type)
    {
        node.BoundType = type;
        if (node.BindingState != BindingState.Invalid)
        {
            node.BindingState = type is null ? BindingState.Unresolved : BindingState.Resolved;
        }

        return type;
    }

    // SPEC 7.6.3: the call a closure's minimum receiver needs, and the calls a Callable receiver permits.
    private static string CallReceiverText(SemanticsKind receiver)
        => receiver switch { SemanticsKind.Ref => "a Shared call", SemanticsKind.Uniq => "an Exclusive call", _ => "a Consuming call" };

    private static string PermittedCallsText(SemanticsKind receiver)
        => receiver switch { SemanticsKind.Ref => "Shared calls only", SemanticsKind.Uniq => "Shared and Exclusive calls only", _ => "every call" };

    private static string SufficientCallText(SemanticsKind receiver)
        => receiver switch { SemanticsKind.Ref => "a Shared call", SemanticsKind.Uniq => "a Shared or Exclusive call", _ => "any call" };

    private static bool SignatureSlotEquals(BindingSymbol? a, BindingSymbol? b, Koto aBinder, Koto bBinder)
        => ReferenceEquals(a, b) || (a is not null && b is not null && ReferenceEquals(a.Scope.Owner, aBinder) && ReferenceEquals(b.Scope.Owner, bBinder) && a.Slot == b.Slot);

    private static bool SignatureEquals(BoundType a, BoundType b, Koto aBinder, Koto bBinder)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a.Kind != b.Kind || a.ResultMode != b.ResultMode || a.Semantics != b.Semantics || a.Length != b.Length || !SameLengthSignature(a.LengthExpression, b.LengthExpression, aBinder, bBinder) || a.Components.Count != b.Components.Count || a.LengthArguments.Length != b.LengthArguments.Length)
        {
            return false;
        }

        for (var i = 0; i < a.LengthArguments.Length; i++)
        {
            if (!SameLengthSignature(a.LengthArguments[i], b.LengthArguments[i], aBinder, bBinder))
            {
                return false;
            }
        }

        if (a.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection)
        {
            return SignatureSlotEquals(a.Symbol, b.Symbol, aBinder, bBinder);
        }

        if (a.Components.Count == 0)
        {
            return a.Symbol is not null && ReferenceEquals(a.Symbol, b.Symbol);
        }

        if (a.Kind == BoundTypeKind.SemanticsApplication ? !SignatureSlotEquals(a.Symbol, b.Symbol, aBinder, bBinder) : a.Symbol != b.Symbol)
        {
            return false;
        }

        for (var i = 0; i < a.Components.Count; i++)
        {
            if (!SignatureEquals(a.Components[i], b.Components[i], aBinder, bBinder))
            {
                return false;
            }
        }

        return true;
    }

    // Discards every fact of the latest pass. Cross-pass storage stays: interned Types, Origins and Constraints, scopes and
    // symbols (reset here, removed by the prune steps), synthesized nodes, pooled plans and scratch buffers.
    private void ResetPass(BindingMode mode)
    {
        this.storageVersion++;
        this.issues.Clear();
        this.callableSelectionFailures.Clear();
        this.callableSelectionParties.Clear();
        this.libraryImports.Clear();
        this.constraintDiagnosticCauses?.Clear();
        this.ResetPrerequisites();
        this.objectPayloadCauses?.Clear();
        this.ResetMatches();
        this.waitingNestedCalls.Clear();
        this.nestedArgumentProbe = null;
        this.resultContexts.Clear();
        this.resultLocalEvidence.Clear();
        this.resultCursor = 0;
        this.ResetStartup();
        this.ResetSpecializations();
        this.receiverOperations.Clear();
        this.adaptations.Clear();
        this.inferredArrayLengths.Clear();
        this.arrayInferenceShapes.Clear();
        this.arrayInferenceFailures?.Clear();
        this.noInitFailures?.Clear();
        this.ResetSyntheticCalls();
        this.pairFollows.Clear();
        this.implicitPairFollows.Clear();
        foreach (var construction in this.enumConstructions.Values)
        {
            construction.IsValid = false;
        }

        this.nodes.Clear();
        this.aliases.Clear();
        this.ResetAliases();
        this.obligations.Clear();
        this.ResetLocalRegions();
        this.obligationSet.Clear();
        this.inheritedOriginTypes.Clear();
        this.ResetCapabilities(mode);
        this.ResetContracts();
        foreach (var scope in this.scopes.Values)
        {
            scope.Reset();
        }

        foreach (var symbol in this.symbols.Values)
        {
            if (symbol.Property is { } property)
            {
                property.IsVerified = false;
            }

            if (symbol.Kind is not (BindingSymbolKind.Type or BindingSymbolKind.TypeParameter or BindingSymbolKind.SemanticsTarget))
            {
                symbol.Type = null;
            }

            symbol.Next = null;
            symbol.ConditionalDeclaration = null;
            symbol.Resolving = false;
            symbol.HeaderBound = false;
            symbol.ReceiverIndex = -1;
            symbol.ObjectPayloadOptOut = null;
        }
    }

    // The indexer resets every semantic field of the source trees before any header or expression is evaluated.
    private void IndexSources()
    {
        foreach (var module in this.compilation.SourceModules)
        {
            this.indexer.Scope = this.GetScope(module.RootKoto, null);
            this.indexer.Visit(module.RootKoto);
        }

        this.IndexModuleReferences();
    }

    // Restores and indexes the embedded Kimi library; false when its declarations are malformed.
    private bool IndexLibrary()
    {
        this.Library.Restore();
        if (!this.Library.ValidateDeclarations())
        {
            return false;
        }

        this.scopes[this.Library.Kotonoha.RootKoto] = this.Library.Scope;
        this.scopes[this.Library.Intrinsics] = this.Library.IntrinsicsScope;
        this.scopes[this.Library.Console] = this.Library.ConsoleScope;
        this.scopes[this.Library.Test] = this.Library.TestScope;
        this.scopes[this.Library.Text] = this.Library.TextScope;
        this.indexer.Scope = this.Library.Scope;
        var libraryRoot = this.Library.Kotonoha.RootKoto;
        for (var i = 0; i < libraryRoot.NestedContainers.Count; i++)
        {
            var declaration = libraryRoot.NestedContainers[i];
            if (declaration.BoundSymbol?.Intrinsic is not (null or IntrinsicKind.None))
            {
                continue;
            }

            if (this.Library.SignatureScope(declaration) is { } signatureScope)
            {
                this.indexer.Scope = signatureScope;
                for (var m = 0; m < declaration.Members.Count; m++)
                {
                    this.indexer.Visit(declaration.Members[m]);
                }

                this.indexer.Scope = this.Library.Scope;
            }
            else
            {
                this.indexer.Visit(declaration);
            }
        }

        for (var i = 0; i < libraryRoot.Members.Count; i++)
        {
            this.indexer.Visit(libraryRoot.Members[i]);
        }

        return true;
    }

    // Declarations, Constraints, Contracts, headers, storage and signatures, before any body is bound.
    private void BindDeclarations(BindingMode mode)
    {
        // Source guards are all indexed now; a removed guard may have become an arm body.
        // Try arms are indexed later, so retain their Pattern scopes until binding finishes.
        this.PruneCandidateScopes();
        this.cLayoutInstances.Clear();
        this.storagePrepared = false;
        this.ValidateDefaultAliases();
        this.PrepareOriginDeclarations();
        this.BindSchemas();
        this.PrepareAssociatedOrigins();
        this.PrepareAliases();
        this.PrepareContracts();
        this.BindConstraints();
        this.BindTypeOriginContracts();
        for (var i = 0; i < this.nodes.Count; i++)
        {
            if (this.nodes[i].BoundSymbol is { Kind: BindingSymbolKind.Function or BindingSymbolKind.Property } symbol && ReferenceEquals(symbol.Declaration, this.nodes[i]))
            {
                this.BindHeader(symbol);
            }
        }

        this.ValidateLayoutFragments();
        this.ValidateLibraryImports();
        this.ValidateBaseDeclarations();
        this.PrepareStorage();
        this.ValidateInlineLayouts();
        this.ValidateCLayoutFields();
        this.ComputeOriginRequirements();
        this.ValidateSignatures();
        this.ValidateContractDeclarations();
        this.PrepareEffectBounds();
        this.capabilitiesReady = true;
        this.ValidateConformances(mode, false);
        this.ValidateConstraintEnvironments();
        this.PrepareSpecializations();
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
            if (this.Library.SignatureScope(declaration) is { } signatureScope)
            {
                for (var m = 0; m < declaration.Members.Count; m++)
                {
                    this.BindNode(declaration.Members[m], signatureScope);
                }
            }
            else if (declaration.BoundSymbol?.Intrinsic == IntrinsicKind.None)
            {
                this.BindNode(declaration, this.Library.Scope);
            }
        }

        for (var i = 0; i < libraryRoot.Members.Count; i++)
        {
            this.BindNode(libraryRoot.Members[i], this.Library.Scope);
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
        // Property certificates must include final Origin and declaration API validity.
        this.ValidateProperties(mode);
        this.ValidateConformances(mode, true);
        this.ValidateConstraintUses(mode);
        // Late witness failures can invalidate declarations that normalized their projections.
        // Revisit dependent certificates only while declaration states change monotonically.
        while (this.ValidateClosedTypeConstraints(mode) | this.ValidateDeclarationProjectionInputs(mode) | this.ValidateConstraintEnvironments(sourcesOnly: true))
        {
            this.ClearCapabilityResults();
            this.ValidateBaseDeclarations(mode);
            this.ValidateProperties(mode);
            this.ValidateConformances(mode, true);
        }

        this.ClearCapabilityResults();
        this.ValidateEffectBounds();
        this.ValidateIterationWitnesses();
        this.ValidateExpressionProjectionInputs(mode);
        this.CompleteEnumAcquisitions();
        this.CompletePatternAcquisitions();
        if (mode == BindingMode.Final)
        {
            this.CompleteAliasWarnings();
        }
    }

    private BoundType? Fail(Koto node, BindingFailure failure, bool unresolved = false)
    {
        // A node failed while another was being checked, such as a qualifier resolved without BindNode: the checked node consulted it.
        if (this.consultationStart >= 0 && !ReferenceEquals(node, this.consultationNode))
        {
            this.consulted.Add(node);
        }

        if (node.BindingFailure != BindingFailure.None)
        {
            return null;
        }

        node.BindingState = unresolved ? BindingState.Unresolved : BindingState.Invalid;
        node.BindingFailure = failure;
        return null;
    }

    // Publishes one direct failure with the facts its check recorded. Each fact table holds the explanation of its node's one
    // failure only (FailExplained), so at most one table answers for a node.
    private void ReportIssue(BindingIssue issue)
    {
        var requirement = DiagnosticRequirement.Binding(issue.Failure);
        if (issue.Code == DiagnosticCode.InvalidConstraint_Kd &&
            this.constraintDiagnosticCauses?.TryGetValue(issue.Node, out var cause) == true &&
            cause.BindingFailure is BindingFailure.MissingName or BindingFailure.MissingType or BindingFailure.Unsupported)
        {
            // The recorded missing Name of the Constraint is its prerequisite.
            issue.Node.ReportDerived(requirement, [cause.KeyOf(DiagnosticRequirement.Binding(cause.BindingFailure))]);
        }
        else if (issue.Failure == BindingFailure.NoInit)
        {
            var variable = (VariableKoto)issue.Node.Parent!;
            var reason = this.noInitFailures![issue.Node] switch
            {
                NoInitProblem.LocalVar => "the declaration is not a local var",
                NoInitProblem.Holes => "the annotation contains a length or element Type hole",
                NoInitProblem.Element => $"owner Scalar is not proven for element {DiagnosticTypeName(variable.TypeKoto!.BoundType!.Components[0])}",
                _ => "the declaration requires an explicit fixed-array Type annotation",
            };
            issue.Node.Report(
                requirement,
                issue.Code,
                evidence: [reason],
                related: [("declaration", variable.TypeKoto ?? variable.NameKoto, "declaration requiring initialization")],
                note: "noinit skips stores but completes construction; the programmer must write a valid value before any element read, including a whole-array Copy or Move");
        }
        else if (issue.Failure == BindingFailure.ArrayAnnotationInference)
        {
            var fact = this.arrayInferenceFailures![issue.Node];
            var reason = fact.Problem switch
            {
                ArrayInferenceProblem.Element => "the initializer supplies no element Type for this hole",
                ArrayInferenceProblem.Length => "the initializer supplies no fixed-array length for this hole",
                ArrayInferenceProblem.LengthConflict => $"this dimension has length {DiagnosticLengthName(fact.Actual!)} but earlier initializer evidence established {DiagnosticLengthName(fact.Expected!)}",
                _ => $"the annotation requires a fixed array here, but the initializer has {DiagnosticTypeName(fact.Type!)}",
            };
            issue.Node.Report(
                requirement,
                issue.Code,
                evidence: [reason],
                related: [("context", fact.Related, fact.Problem is ArrayInferenceProblem.Element or ArrayInferenceProblem.Length ? "initializer supplying the evidence" : "fixed-array annotation")],
                advice: "Supply consistent initializer evidence or write the missing length or element Type explicitly; a dynamic Array's runtime length is not fixed-array evidence");
        }
        else if (issue.Code == DiagnosticCode.NonExhaustiveMatch_Kd)
        {
            // Only a match plan fails NonExhaustiveMatch, so its coverage is always known.
            var coverage = this.matches[(MatchKoto)issue.Node].Coverage;
            issue.Node.Report(requirement, issue.Code, note: coverage.Describe(), evidence: [coverage.Requirement]);
        }
        else if (issue.Code == DiagnosticCode.InvalidNumericLiteral_Kd && LiteralConversionTarget(issue.Node) is { } literalTarget)
        {
            // SPEC 13.5.4.2: a direct literal is converted at compile time, so its range failure is explained at the literal.
            var truncated = (issue.Node as NumberLiteralKoto ?? ((UnaryKoto)issue.Node).Operand) is NumberLiteralKoto { IsInteger: false };
            issue.Node.Report(requirement, issue.Code, note: $"The direct literal is converted at compile time and its {(truncated ? "truncated " : string.Empty)}value is outside the range of {DiagnosticTypeName(literalTarget)} (SPEC 13.5.4.2)");
        }
        else if (issue.Code == DiagnosticCode.InvalidTry_Kd)
        {
            var (code, evidence, note) = this.TryFailure(issue.Node);
            issue.Node.Report(requirement, code, note: note, evidence: evidence);
        }
        else if (issue.Code == DiagnosticCode.InvalidTypeFormation_Kd && issue.Node is GenericsKoto { BoundSymbol.LibraryDeclaration: KimiDeclarationId.Loan })
        {
            // SPEC 15.3.5: the formation condition of Loan<T>.
            issue.Node.Report(
                requirement,
                issue.Code,
                note: "Loan<T> keeps the dependency of a borrow value, so T must be a complete ref, uniq, objref or objuniq borrow Type",
                advice: "Name the borrow whose dependency the Field keeps, as in Loan<ref/T during source>");
        }
        else if (issue.Code == DiagnosticCode.NotObjectPayload_Kd)
        {
            // FailObjectPayload records the declaring Type before it fails the use.
            issue.Node.Report(requirement, issue.Code, this.objectPayloadCauses![issue.Node].Name);
        }
        else if (this.ReportCallableSelection(issue.Node, requirement, issue.Code))
        {
        }
        else if (issue.Code is DiagnosticCode.NoApplicableOverload_Kd or DiagnosticCode.AmbiguousBinding_Kd && this.rejectedCandidates?.TryGetValue(issue.Node, out var rejected) == true)
        {
            var candidates = new (string Role, Koto At, string? Label)[rejected.Length];
            string? shapeNote = issue.Code == DiagnosticCode.AmbiguousBinding_Kd ? "No candidate is better than every other remaining candidate under the argument, parameter Type, generic and default ranking rules. Anonymous bodies, captures and waiting function references do not select an overload" : null;
            string? advice = null;
            for (var c = 0; c < rejected.Length; c++)
            {
                var candidate = rejected[c];
                var label = candidate.Function.Name;
                if (candidate.ReferenceSignature && issue.Code == DiagnosticCode.AmbiguousBinding_Kd)
                {
                    shapeNote = "No function reference candidate is better than every other fitting candidate under the parameter Type and generic ranking rules. Results do not rank candidates";
                    advice = "Write explicit Type arguments or a Type annotation that uniquely selects the intended function reference";
                }

                if (candidate.UnfixedReference)
                {
                    // SPEC 10.5: without a fixed expected call signature, an overload set is not a value.
                    shapeNote = "A function reference without a fixed expected call signature is a value only when exactly one candidate remains and its Type parameters are bound";
                    advice = "Annotate the expected Function Type, or write explicit Type arguments, so that one function is referenced";
                    candidates[c] = ("candidate", candidate.Function, candidate.Actual is { } signature ? $"{label}: callable signature {DiagnosticText.Bound(DiagnosticTypeName(signature), 48).Text}" : label);
                    continue;
                }

                if (candidate.ErasureIncomparable && issue.Code == DiagnosticCode.AmbiguousBinding_Kd)
                {
                    shapeNote = ErasureAmbiguityNote;
                }

                if (candidate.Selected)
                {
                    shapeNote = "This declaration was selected before completing its callable arguments; its argument constraints failed. Another overload is not selected";
                    if (candidate.ActualReceiver is { } actualReceiver && candidate.RequiredReceiver is { } requiredReceiver)
                    {
                        label = $"{candidate.Function.Name}: closure requires {actualReceiver.ToString().ToLowerInvariant()}; Callable requires {requiredReceiver.ToString().ToLowerInvariant()}";
                        shapeNote = $"{label}. {shapeNote}";
                    }
                }
                else if (candidate.ActualReceiver is { } closureReceiver && candidate.RequiredReceiver is { } callableReceiver &&
                    (uint)candidate.ReceiverParameter < (uint)candidate.Function.Parameters.Count)
                {
                    // SPEC 7.6.3, 8.6: the closure argument's minimum call receiver is the candidate's only refuted condition
                    // (TryCandidate), so the Advice names the parameter form and Callable receiver that admit it.
                    var parameter = candidate.Function.Parameters[candidate.ReceiverParameter];
                    var pattern = parameter.Type.BoundType;
                    var slot = (pattern is { Kind: BoundTypeKind.Semantics, Components.Count: 1 } ? pattern.Components[0] : pattern)?.Symbol?.Name ?? "F";
                    var needed = closureReceiver.ToString().ToLowerInvariant();
                    label = $"{candidate.Function.Name}: closure requires {needed}; Callable requires {callableReceiver.ToString().ToLowerInvariant()}";
                    shapeNote ??= $"{label}. The closure argument of {parameter.InternalName} needs {CallReceiverText(closureReceiver)}, and the Callable Constraint of {slot} permits {PermittedCallsText(callableReceiver)} (SPEC 7.6.3, 8.6)";
                    var form = closureReceiver == SemanticsKind.Uniq ? $"uniq/{slot}, with {slot} is Callable<uniq, ...>, and pass the closure with @uniq" : $"{slot} by value, with {slot} is Callable<owner, ...>, and pass the closure with @move";
                    advice ??= $"Declare {parameter.InternalName} as {form}, or change the closure so that {SufficientCallText(callableReceiver)} suffices";
                }

                if (candidate.Actual is { } actual && candidate.Expected is { } expected)
                {
                    var (shownActual, shownExpected) = DiagnosticText.BoundPair(DiagnosticTypeName(actual), DiagnosticTypeName(expected));
                    label = candidate.ReferenceSignature && issue.Code == DiagnosticCode.AmbiguousBinding_Kd ? $"{candidate.Function.Name}: callable signature {shownActual.Text}"
                        : candidate.CallableSignature ? $"{candidate.Function.Name}: callable signature is {shownActual.Text}; requires {shownExpected.Text}"
                        : candidate.SharedReceiver ? $"{candidate.Function.Name}: receiver has {shownActual.Text}; requires {shownExpected.Text}"
                        : $"{candidate.Function.Name}: argument has {shownActual.Text}; parameter requires {shownExpected.Text}";
                    // Keep the compared Types even when the related-location limit omits this candidate.
                    shapeNote ??= candidate.ReferenceSignature ? $"The function reference requires the fixed call signature {shownExpected.Text}; no candidate applies"
                        : candidate.CallableSignature ? $"The argument's known call signature is {shownActual.Text}; the candidate requires {shownExpected.Text} from the supplied Type evidence"
                        : candidate.SharedReceiver ? $"{SharedObjectAuthorityNote}. Receiver: {DiagnosticText.Bound(DiagnosticTypeName(actual), 48).Text}; required: {DiagnosticText.Bound(DiagnosticTypeName(expected), 48).Text}"
                        : $"The range argument has {shownActual.Text}; a candidate parameter requires {shownExpected.Text}";
                    if (!candidate.SharedReceiver && !candidate.CallableSignature)
                    {
                        advice ??= RangeShapeAdvice;
                    }
                }

                advice ??= candidate.ObjectClone ? StrongCloneAdvice : null;
                candidates[c] = ("candidate", candidate.Function, label);
            }

            // A synthesized formatting write spans its whole literal; its failure is located at the value it writes, so the
            // writes of one literal are distinct problems at distinct locations (SPEC 23.3.6.2, 23.3.6.6).
            var at = issue.Node is InvocationKoto { Method: FormattingKoto or GenericsKoto { Identifier: FormattingKoto }, ArgumentNodes: [_, var value] } ? value : null;
            issue.Node.Report(requirement, issue.Code, evidence: [rejected.Length], related: candidates, note: shapeNote, advice: advice, at: at);
        }
        else if (issue.Code == DiagnosticCode.UnboundTypeArgument_Kd && this.unboundSlots?.TryGetValue(issue.Node, out var unboundSlot) == true)
        {
            // SPEC 10.6, 10.8: the selected call's slot that no evidence binds, with the waiting argument whose signature holds it.
            ReportUnboundSlots(issue.Node, unboundSlot, requirement, issue.Code);
        }
        else if (issue.Code == DiagnosticCode.UnboundTypeArgument_Kd && KotoHelper.UnwrapParentheses(issue.Node).BoundSymbol?.Declaration is FunctionKoto { GenericArguments.Count: > 0 } generic)
        {
            // SPEC 10.5: the generic parameter that no expected call signature or explicit argument binds.
            var lengths = ItemTypeArgumentCount(generic) != generic.GenericArguments.Count;
            var note = lengths ? "A function reference without a fixed expected call signature binds its Type and length parameters only from a complete explicit argument list (SPEC 10.5)" : UnboundReferenceNote;
            var advice = lengths ? "Write every Type and length argument in declaration order, or annotate a Function Type whose signature binds every parameter" : UnboundReferenceAdvice;
            issue.Node.Report(requirement, issue.Code, evidence: [generic.GenericArguments[0].Identifier], related: [("declaration", generic, null)], note: note, advice: advice);
        }
        else if (issue.Code == DiagnosticCode.BoundMethodValue_Kd && KotoHelper.UnwrapParentheses(issue.Node) is { BoundSymbol.Declaration: FunctionKoto method })
        {
            // SPEC 7.3: the method named through a value; its declaration shows the receiver it would need.
            issue.Node.Report(requirement, issue.Code, evidence: [method.Name], related: [("declaration", method, null)]);
        }
        else if (issue.Code == DiagnosticCode.ParameterShapeMismatch_Kd && this.parameterShapeConflicts.TryGetValue(issue.Node, out var shapes))
        {
            this.ReportParameterShapes(issue.Node, requirement, shapes);
        }
        else if (issue.Code == DiagnosticCode.AccessorReceiverShape_Kd && issue.Node is PropertyAccessorKoto { ReceiverType: { } writtenReceiver } shapedAccessor)
        {
            this.ReportAccessorReceiverShape(shapedAccessor, writtenReceiver, requirement);
        }
        else if (issue.Code == DiagnosticCode.ProtectedPlacement_Kd)
        {
            ReportProtectedPlacement(issue.Node, requirement);
        }
        else if (issue.Code == DiagnosticCode.InvalidEffectBound_Kd && issue.Node is EffectBoundKoto effect)
        {
            this.ReportEffectBound(effect, requirement);
        }
        else if (issue.Code == DiagnosticCode.IncompatibleContractImplementation_Kd &&
            (this.ReportCallablePremiseFailure(issue.Node, requirement) || this.ReportEffectViolation(issue.Node, requirement, issue.Code)))
        {
            // SPEC 8.4.10.6: reported at the violating effect.
        }
        else if (issue.Code == DiagnosticCode.MissingOriginBinding_Kd && this.perCallSlots?.TryGetValue(issue.Node, out var perCall) == true)
        {
            ReportPerCallSlot(issue.Node, perCall, requirement, issue.Code);
        }
        else if (issue.Code == DiagnosticCode.MissingOriginBinding_Kd && this.perCallOrigins?.TryGetValue(issue.Node, out var perCallOrigin) == true)
        {
            // SPEC 15.3.6, 10.8: only the argument's own per-call Origin would satisfy the slot; the argument is related.
            issue.Node.Report(requirement, issue.Code, evidence: [perCallOrigin.Reason], related: [("argument", perCallOrigin.Argument, null)], advice: PerCallOriginAdvice);
        }
        else if (issue.Code == DiagnosticCode.MissingOriginBinding_Kd && this.ReportUndeclaredStorageOrigin(issue.Node, requirement, issue.Code))
        {
            // SPEC 15.3.2: an undeclared storage name, at the name.
        }
        else if (issue.Code == DiagnosticCode.InvalidOriginBinding_Kd && this.ReportAbsentSlot(issue.Node, requirement, issue.Code))
        {
            // SPEC 15.3.2: a projection of a slot its Type does not declare, at the slot name.
        }
        else if (issue.Code is DiagnosticCode.TransferRequired_Kd or DiagnosticCode.ExclusiveBorrowRequired_Kd && this.acquisitionPlaces?.TryGetValue(issue.Node, out var acquisition) == true)
        {
            this.ReportAcquisition(issue.Node, acquisition.Place, acquisition.Object, requirement, issue.Code);
        }
        else if (issue.Code == DiagnosticCode.InvalidCaptureBinding_Kd && issue.Node is IdentifierNameKoto { BoundSymbol: { } contextual } && IsContextualBinding(contextual))
        {
            this.ReportContextualCapture(issue.Node, contextual, requirement, issue.Code);
        }
        else if (issue.Code is DiagnosticCode.UnsatisfiedOriginRelation_Kd or DiagnosticCode.UnprovenOriginRelation_Kd && this.originRelations?.TryGetValue(issue.Node, out var relation) == true)
        {
            ReportOriginRelation(issue.Node, relation, requirement, issue.Code, this.BorrowOriginHint(issue.Node));
            this.ReportMoreCallRelations(issue.Node, requirement);
        }
        else if (issue.Code == DiagnosticCode.UnprovenOriginContract_Kd && this.originContracts?.TryGetValue(issue.Node, out var contract) == true)
        {
            ReportOriginContract(issue.Node, contract, requirement, issue.Code);
        }
        else if (issue.Code == DiagnosticCode.InvalidTypeFormation_Kd && this.arityFailures?.TryGetValue(issue.Node, out var arity) == true)
        {
            ReportArity(issue.Node, arity, requirement, issue.Code);
        }
        else if (issue.Code == DiagnosticCode.UnsupportedBinding_Kd && this.originQualifierLimits?.TryGetValue(issue.Node, out var qualifierLimit) == true)
        {
            this.ReportOriginQualifier(issue.Node, qualifierLimit, requirement, issue.Code);
        }
        else if (issue.Code == DiagnosticCode.DuplicateBinding_Kd && issue.Node is TypeSemanticsKoto { BindingSetName: { } reusedSet } bindingSet)
        {
            this.ReportDuplicateBindingSet(bindingSet, reusedSet, requirement, issue.Code);
        }
        else if (issue.Code == DiagnosticCode.DuplicateBinding_Kd && this.captureRepeats?.TryGetValue(issue.Node, out var repeats) == true)
        {
            this.ReportCaptureRepeats(issue.Node, repeats, requirement, issue.Code);
        }
        else if (this.captureFailures?.TryGetValue(issue.Node, out var entry) == true)
        {
            this.ReportCaptureEntry(issue.Node, entry.Capture, entry.Type, entry.Source, requirement, issue.Code);
        }
        else if (this.writeTargets?.TryGetValue(issue.Node, out var target) == true)
        {
            this.ReportWrite(issue.Node, target, requirement, issue.Code);
        }
        else if (issue.Code is DiagnosticCode.NonNumericOperand_Kd or DiagnosticCode.NonIntegerOperand_Kd or DiagnosticCode.InvalidShiftCount_Kd &&
            this.operatorOperands?.TryGetValue(issue.Node, out var operand) == true)
        {
            this.ReportOperatorOperand(issue.Node, operand, requirement, issue.Code);
        }
        else if (issue.Code == DiagnosticCode.TypeMismatch_Kd && this.mismatches?.TryGetValue(issue.Node, out var mismatch) == true)
        {
            // The subject stays the failed node; the location is the syntax that shows the two Types. A numeric conversion
            // rejected for a wrapping integer Type explains the same-argument rule (SPEC 13.5.4.1).
            var wrappingConversion = issue.Node is ConversionKoto && (mismatch.Actual is BoundType { IsWrappingInteger: true } || mismatch.Expected is BoundType { IsWrappingInteger: true });
            // A default at a generic parameter Type is checked for every binding (SPEC 7.2.3).
            var conversion = wrappingConversion ? null : this.ClosureConversionNote(issue.Node, mismatch.Actual, mismatch.Expected);
            string? defaultAdvice = null;
            var defaultNote = wrappingConversion || conversion is not null ? null : this.GenericDefaultNote(issue.Node, mismatch.Actual, mismatch.Expected, out defaultAdvice);
            issue.Node.Report(requirement, issue.Code, note: wrappingConversion ? WrappingConversionNote : conversion ?? defaultNote ?? this.BorrowOriginHint(issue.Node), advice: wrappingConversion ? WrappingConversionAdvice : defaultAdvice, at: mismatch.At, evidence: [DiagnosticTypeName(mismatch.Actual), DiagnosticTypeName(mismatch.Expected)]);
        }
        else if (issue.Code is DiagnosticCode.UnsatisfiedConstraint_Kd or DiagnosticCode.UnprovenConstraint_Kd && this.ownedConversions?.TryGetValue(issue.Node, out var owned) == true)
        {
            this.ReportOwnedConversion(issue.Node, owned, requirement, issue.Code);
        }
        else if (issue.Code == DiagnosticCode.UnprovenConstraint_Kd && this.callableConstraints?.TryGetValue(issue.Node, out var callable) == true)
        {
            // SPEC 8.7, 15.6.1: a Callable proof that is Unknown only in its Origin part, at the argument whose Type binds F.
            ReportCallableConstraint(issue.Node, callable, requirement, issue.Code);
        }
        else if (issue.Code == DiagnosticCode.UnsatisfiedConstraint_Kd && this.rangeIterationFailures?.TryGetValue(issue.Node, out var rangeFailure) == true)
        {
            var subject = rangeFailure.Subject;
            var integers = subject.Components[0].IsInteger && subject.Components[1].IsInteger;
            var advice = integers ? "Explicitly convert both boundaries to the same integer Type before constructing the range" :
                "To enumerate positions in a sequence, resolve the range against its length first, for example r.resolve(values.length)";
            issue.Node.Report(requirement, issue.Code, note: "Range iteration requires both boundaries to have the same integer Type", advice: advice, evidence: [DiagnosticTypeName(subject), rangeFailure.Entry.Name, DiagnosticTypeName(subject.Components[0]), DiagnosticTypeName(subject.Components[1])]);
        }
        else if (issue.Code == DiagnosticCode.UnprovenConstraint_Kd && this.rangeIterationFailures?.TryGetValue(issue.Node, out var unproven) == true)
        {
            // SPEC 4.6.3.4: the boundary Types are not known to be one integer Type in this generic context.
            var start = DiagnosticTypeName(unproven.Subject.Components[0]);
            var end = DiagnosticTypeName(unproven.Subject.Components[1]);
            var repair = ReferenceEquals(unproven.Subject.Components[0], unproven.Subject.Components[1])
                ? $"If the boundaries are meant to be integers, require {start} is PrimitiveInteger"
                : $"If both boundaries are meant to be integers of one Type, require {start} is PrimitiveInteger and {end} is {start}, or convert the boundaries explicitly";
            issue.Node.Report(requirement, issue.Code, note: $"Range iteration requires both boundaries to have one integer Type; the boundary Types {start} and {end} are not proven to be one integer Type", advice: repair);
        }
        else
        {
            issue.Node.Report(requirement, issue.Code, note: this.BorrowOriginHint(issue.Node), evidence: issue.Code is DiagnosticCode.SharedPathAccess_Kd or DiagnosticCode.TransferRequired_Kd ? [issue.Node.ToString()] : null);
        }
    }

    private BindingResult Check(BindingMode mode)
    {
        if (mode == BindingMode.Final)
        {
            for (var i = 0; i < this.obligations.Count; i++)
            {
                if (this.obligations[i].Deadline == BindingDeadline.Definition)
                {
                    this.Fail(this.obligations[i].Use, BindingFailure.UnprovenConstraint);
                }
            }
        }

        var resolved = 0;
        var unresolved = 0;
        var invalid = 0;
        for (var i = 0; i < this.nodes.Count; i++)
        {
            var node = this.nodes[i];
            // API and constraint validation can invalidate a target or Type after call selection; a target whose declaration
            // failed leaves the call resting on that failure (SPEC 23.3.6.4).
            if (node is InvocationKoto { BoundCall: { } call })
            {
                if (node.BindingFailure == BindingFailure.None && InvalidDeclarationContextCause(call.Target.Declaration) is { } invalidTarget)
                {
                    this.CompleteDependent(node, invalidTarget);
                }
                else
                {
                    this.RequireConstraint(node, this.CheckCallTypeConstraints(call, this.ConstraintScope(node)), mode);
                }
            }
            else if (node is IsKoto { BoundRuntimeTest: { } runtimeTest } test)
            {
                var proof = this.CheckRuntimeTestTypeConstraints(runtimeTest, this.ConstraintScope(test));
                this.RequireConstraint(test, proof, mode);
                if (proof != ConstraintProof.Proven)
                {
                    test.BoundRuntimeTest = null;
                }
            }

            if (node.BindingState == BindingState.Unvisited)
            {
                // Skipped dependent syntax has no Type evidence, not a proof of an unsupported feature.
                // Actual subset gates diagnose themselves; the final fallback still rejects unresolved work.
                node.BindingState = BindingState.Unresolved;
            }

            switch (node.BindingState)
            {
                case BindingState.Resolved:
                    resolved++;
                    break;
                case BindingState.Invalid:
                    invalid++;
                    break;
                default:
                    unresolved++;
                    break;
            }

            if (mode == BindingMode.Final && node.BindingState != BindingState.Resolved && node.BindingFailure != BindingFailure.None && (this.IsDerived(node) || this.RestsOnAbsentSlot(node)))
            {
                this.derivedIssues.Add(node);
            }
            else if (mode == BindingMode.Final && node.BindingState != BindingState.Resolved && node.BindingFailure != BindingFailure.None)
            {
                var code = node.BindingFailure switch
                {
                    BindingFailure.InvalidTestDefinition => DiagnosticCode.InvalidTestDefinition_Kd,
                    BindingFailure.InvalidLayoutAttribute => DiagnosticCode.InvalidLayoutAttribute_Kd,
                    BindingFailure.ConflictingLayout => DiagnosticCode.ConflictingLayout_Kd,
                    BindingFailure.InvalidLibraryImport => DiagnosticCode.InvalidLibraryImport_Kd,
                    BindingFailure.MissingNativeRequirement => DiagnosticCode.MissingNativeRequirement_Kd,
                    BindingFailure.UnsupportedImportSignature => DiagnosticCode.UnsupportedImportSignature_Kd,
                    BindingFailure.ConflictingImportSignature => DiagnosticCode.ConflictingImportSignature_Kd,
                    BindingFailure.ConflictingRuntimeSymbol => DiagnosticCode.ConflictingRuntimeSymbol_Kd,
                    BindingFailure.ConflictingImportSupply => DiagnosticCode.ConflictingImportSupply_Kd,
                    BindingFailure.UnsafeFunctionValue => DiagnosticCode.UnsafeFunctionValue_Kd,
                    BindingFailure.UnavailableReservedImport => DiagnosticCode.UnavailableReservedImport_Kd,
                    BindingFailure.SplitCLayoutStorage => DiagnosticCode.SplitCLayoutStorage_Kd,
                    BindingFailure.InvalidCLayout => DiagnosticCode.InvalidCLayout_Kd,
                    BindingFailure.InvalidInlineLayout => DiagnosticCode.InvalidInlineLayout_Kd,
                    BindingFailure.MissingName or BindingFailure.MissingType => DiagnosticCode.UnresolvedBinding_Kd,
                    BindingFailure.Ambiguous => DiagnosticCode.AmbiguousBinding_Kd,
                    BindingFailure.Duplicate => DiagnosticCode.DuplicateBinding_Kd,
                    BindingFailure.DuplicateDictionaryKey => DiagnosticCode.DuplicateDictionaryKey_Kd,
                    BindingFailure.TypeMismatch => DiagnosticCode.TypeMismatch_Kd,
                    BindingFailure.NotCallable => DiagnosticCode.NotCallable_Kd,
                    BindingFailure.NoApplicableCandidate => DiagnosticCode.NoApplicableOverload_Kd,
                    BindingFailure.Cycle => DiagnosticCode.CyclicBinding_Kd,
                    BindingFailure.InvalidAssignment => DiagnosticCode.InvalidAssignment_Kd,
                    BindingFailure.InvalidLiteral => DiagnosticCode.InvalidNumericLiteral_Kd,
                    BindingFailure.Access => DiagnosticCode.InaccessibleBinding_Kd,
                    BindingFailure.Capture => DiagnosticCode.InvalidCaptureBinding_Kd,
                    BindingFailure.InvalidOrigin => DiagnosticCode.InvalidOriginBinding_Kd,
                    BindingFailure.MissingOrigin => DiagnosticCode.MissingOriginBinding_Kd,
                    BindingFailure.InvalidTypeFormation => DiagnosticCode.InvalidTypeFormation_Kd,
                    BindingFailure.InvalidConstraint => DiagnosticCode.InvalidConstraint_Kd,
                    BindingFailure.InvalidSelfClause => DiagnosticCode.InvalidSelfClause_Kd,
                    BindingFailure.ClosedContractConformance => DiagnosticCode.ClosedContractConformance_Kd,
                    BindingFailure.NotIndexable => DiagnosticCode.NotIndexable_Kd,
                    BindingFailure.NotObjectPayload => DiagnosticCode.NotObjectPayload_Kd,
                    BindingFailure.UnprovenConstraint => DiagnosticCode.UnprovenConstraint_Kd,
                    BindingFailure.UnsatisfiedConstraint => DiagnosticCode.UnsatisfiedConstraint_Kd,
                    BindingFailure.InvalidKimi => DiagnosticCode.InvalidKimiLibrary_Kd,
                    BindingFailure.MissingImplementation => DiagnosticCode.MissingContractImplementation_Kd,
                    BindingFailure.IncompatibleImplementation => DiagnosticCode.IncompatibleContractImplementation_Kd,
                    BindingFailure.InvalidAssociatedType => DiagnosticCode.InvalidAssociatedType_Kd,
                    BindingFailure.InvalidPattern => DiagnosticCode.InvalidPattern_Kd,
                    BindingFailure.NonExhaustiveMatch => DiagnosticCode.NonExhaustiveMatch_Kd,
                    BindingFailure.TransferRequired => DiagnosticCode.TransferRequired_Kd,
                    BindingFailure.MissingSpecializationTarget => DiagnosticCode.MissingSpecializationTarget_Kd,
                    BindingFailure.SpecializationInputMismatch => DiagnosticCode.SpecializationInputMismatch_Kd,
                    BindingFailure.ExclusiveBorrowRequired => DiagnosticCode.ExclusiveBorrowRequired_Kd,
                    BindingFailure.ParameterShapeMismatch => DiagnosticCode.ParameterShapeMismatch_Kd,
                    BindingFailure.UnboundTypeArgument => DiagnosticCode.UnboundTypeArgument_Kd,
                    BindingFailure.BoundMethodValue => DiagnosticCode.BoundMethodValue_Kd,
                    BindingFailure.OriginRelation => this.OriginRelationCode(node),
                    BindingFailure.OriginContract => DiagnosticCode.UnprovenOriginContract_Kd,
                    BindingFailure.InvalidEffectBound => DiagnosticCode.InvalidEffectBound_Kd,
                    BindingFailure.ArrayAnnotationInference => DiagnosticCode.ArrayAnnotationInference_Kd,
                    BindingFailure.NoInit => DiagnosticCode.InvalidNoInit_Kd,
                    BindingFailure.SharedBindingAssignment => DiagnosticCode.SharedBindingAssignment_Kd,
                    BindingFailure.ExclusiveBindingAssignment => DiagnosticCode.ExclusiveBindingAssignment_Kd,
                    BindingFailure.SharedPathAccess => DiagnosticCode.SharedPathAccess_Kd,
                    BindingFailure.ExclusivePathTake => DiagnosticCode.ExclusivePathTake_Kd,
                    BindingFailure.PlaceRequired => DiagnosticCode.PlaceRequired_Kd,
                    BindingFailure.ReceiverShapeMismatch => DiagnosticCode.ReceiverShapeMismatch_Kd,
                    BindingFailure.AccessorReceiverShape => DiagnosticCode.AccessorReceiverShape_Kd,
                    BindingFailure.BareOwningShorthand => DiagnosticCode.BareOwningShorthand_Kd,
                    BindingFailure.NonCopyOperand => DiagnosticCode.NonCopyOperand_Kd,
                    BindingFailure.NonNumericOperand => DiagnosticCode.NonNumericOperand_Kd,
                    BindingFailure.NonIntegerOperand => DiagnosticCode.NonIntegerOperand_Kd,
                    BindingFailure.InvalidShiftCount => DiagnosticCode.InvalidShiftCount_Kd,
                    BindingFailure.InvalidWrapConversion => DiagnosticCode.InvalidWrapConversion_Kd,
                    BindingFailure.InvalidBitConversion => DiagnosticCode.InvalidBitConversion_Kd,
                    BindingFailure.GenericBitConversion => DiagnosticCode.GenericBitConversion_Kd,
                    BindingFailure.ProtectedPlacement => DiagnosticCode.ProtectedPlacement_Kd,
                    _ => DiagnosticCode.UnsupportedBinding_Kd,
                };
                if (node.BindingFailure == BindingFailure.TypeMismatch && (node is TryKoto || node is ReturnKoto { Parent: TryKoto }))
                {
                    code = DiagnosticCode.InvalidTry_Kd;
                }

                this.issues.Add(new(code == DiagnosticCode.InvalidKimiLibrary_Kd ? this.Library.InvalidDeclaration ?? node : node, code) { Failure = node.BindingFailure });
            }
        }

        return new(mode, resolved, unresolved, invalid);
    }

    private BindingScope GetScope(object key, BindingScope? parent, Koto? owner = null)
    {
        if (!this.scopes.TryGetValue(key, out var scope))
        {
            scope = new(owner ?? (Koto)key);
            this.scopes.Add(key, scope);
        }

        scope.Parent = parent;
        scope.Function = scope.Owner is FunctionKoto function ? function : parent?.Function;
        return scope;
    }

    private BindingScope NodeScope(Koto node, BindingScope fallback)
    {
        if (node.Parent is CodeBlockKoto { Parent: FunctionKoto { IsGenerated: true } } && node.CodeContext.SourceDocument is { } source && this.scopes.TryGetValue(source, out var sourceScope))
        {
            fallback = sourceScope;
        }

        return this.scopes.TryGetValue(node, out var scope) ? scope : fallback;
    }

    private BindingSymbol Declare(object key, string name, BindingSymbolKind kind, Koto node, BindingScope scope)
    {
        if (!this.symbols.TryGetValue(key, out var symbol))
        {
            symbol = new(name, kind, node, scope);
            this.symbols.Add(key, symbol);
        }

        symbol.Scope = scope;
        if (name == "_" && node.Parent is ForKoto)
        {
            node.BoundSymbol = symbol;
            return symbol;
        }

        var table = kind is BindingSymbolKind.Container or BindingSymbolKind.Type or BindingSymbolKind.TypeParameter or BindingSymbolKind.SemanticsParameter or BindingSymbolKind.SemanticsTarget or BindingSymbolKind.AssociatedType ? scope.Types : scope.Values;
        if (table.TryGetValue(name, out var previous))
        {
            symbol.Next = previous;
            if ((kind != BindingSymbolKind.Function || previous.Kind != BindingSymbolKind.Function) &&
                !(kind == BindingSymbolKind.Type && node is DeclarationContainerKoto declaration && DistinctTypeArities(declaration, previous)))
            {
                this.FailDuplicate(node, previous.Declaration);
            }
        }

        table[name] = symbol;
        node.BoundSymbol = symbol;
        return symbol;
    }

    private void BindHeader(BindingSymbol symbol)
    {
        if (symbol.HeaderBound || symbol.Resolving || symbol.Declaration is FunctionKoto { IsAnonymous: true })
        {
            // An anonymous header belongs to its fixed expression context, including on a later binding pass.
            return;
        }

        symbol.Resolving = true;
        if (symbol.Declaration is FunctionKoto function)
        {
            var scope = this.scopes[function];
            var originDeclaration = this.BeginOriginDeclaration(function, scope);
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                var parameter = function.Parameters[i];
                this.symbols[parameter].Type = this.BindType(parameter.Type, scope);
            }

            // Named signatures never infer a result from their body or callers (SPEC 10.5).
            symbol.Type = function.ReturnType is { } result ? this.BindType(result, scope) : BoundType.Unit;
            this.CompleteOriginDeclaration(originDeclaration);
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                this.symbols[function.Parameters[i]].Type = function.Parameters[i].Type.BoundType;
            }

            if (symbol.ReceiverIndex >= 0)
            {
                var receiver = function.Parameters[symbol.ReceiverIndex];
                if (!this.IsReceiverType(receiver.Type.BoundType, symbol.Scope.Owner.BoundSymbol!) || receiver.ExternalName != "self" || receiver.DefaultValue is not null)
                {
                    this.Fail(function, BindingFailure.InvalidTypeFormation);
                }
            }
        }
        else if (symbol.Property is { } property)
        {
            this.BindPropertyHeader(property);
        }
        else if (symbol.Declaration is VariableKoto variable && variable.TypeKoto is { } type)
        {
            symbol.Type = this.BindType(type, symbol.Scope);
        }

        symbol.HeaderBound = true;
        symbol.Resolving = false;
    }

    private void ValidateSignatures()
    {
        foreach (var scope in this.scopes.Values)
        {
            foreach (var first in scope.Values.Values)
            {
                // SPEC 7.3.1: the functions of one Name acquire corresponding parameters of overlapping Types in one mode; a Contract's
                // requirements are checked below with the ones they inherit.
                if (scope.Owner is not ContractKoto)
                {
                    this.ValidateParameterShapes(first);
                }

                for (var a = first; a is not null; a = a.Next)
                {
                    if (a.Declaration is not FunctionKoto fa || fa.IsSpecialization)
                    {
                        continue;
                    }

                    // SPEC 7.3: every function with a receiver in one member group shares one receiver shape.
                    if (ReceiverShape(a) is { } shape)
                    {
                        for (var b = a.Next; b is not null; b = b.Next)
                        {
                            if (ReceiverShape(b) is { } other && other != shape)
                            {
                                this.Fail(fa, BindingFailure.ReceiverShapeMismatch);
                                this.Fail(b.Declaration, BindingFailure.ReceiverShapeMismatch);
                            }
                        }
                    }

                    for (var b = a.Next; b is not null; b = b.Next)
                    {
                        if (b.Declaration is not FunctionKoto fb || fb.IsSpecialization || fa.GenericArguments.Count != fb.GenericArguments.Count || fa.Parameters.Count != fb.Parameters.Count)
                        {
                            continue;
                        }

                        var equal = true;
                        for (var i = 0; i < fa.Parameters.Count; i++)
                        {
                            var ta = fa.Parameters[i].Type.BoundType;
                            var tb = fb.Parameters[i].Type.BoundType;
                            if (ta is null || tb is null || !SignatureEquals(ta, tb, fa, fb))
                            {
                                equal = false;
                                break;
                            }
                        }

                        if (equal)
                        {
                            this.FailDuplicate(fa, fb);
                        }
                    }
                }
            }
        }

        // SPEC 8.4.1: a Contract's same-name requirements, including those inherited by refinement, share one receiver shape.
        for (var n = 0; n < this.nodes.Count; n++)
        {
            if (this.nodes[n] is not ContractKoto { BoundSymbol.Contract: { } shape } contract)
            {
                continue;
            }

            foreach (var members in shape.MembersByName.Values)
            {
                this.ValidateContractParameterShapes(contract, members);
                SemanticsKind? expected = null;
                for (var i = 0; i < members.Count; i++)
                {
                    if (ReceiverShape(members[i]) is not { } current)
                    {
                        continue;
                    }

                    if (expected is null)
                    {
                        expected = current;
                    }
                    else if (expected != current)
                    {
                        this.Fail(contract, BindingFailure.ReceiverShapeMismatch);
                        if (ReferenceEquals(members[i].Declaration.Parent, contract))
                        {
                            this.Fail(members[i].Declaration, BindingFailure.ReceiverShapeMismatch);
                        }
                    }
                }
            }
        }
    }

    private sealed class TestSyntaxVisitor(Binding binding) : KotoVisitor
    {
        public override void Visit(Koto node)
        {
            if (node is AttributeKoto { IdentifierKoto: IdentifierNameKoto { IdentifierName: "Layout" } } layout)
            {
                binding.IndexLayoutAttribute(layout);
                if (layout.AttributeChain is { } precedingAttribute)
                {
                    this.Visit(precedingAttribute);
                }

                return;
            }

            var marker = node is FunctionKoto function && !TestDefinition.IsValidSyntax(function) ? TestDefinition.Marker(function) : null;
            if (node is AttributeKoto { IdentifierKoto: IdentifierNameKoto { IdentifierName: "Test" } } attribute &&
                (attribute.Parent is not FunctionKoto owner || TestDefinition.Marker(owner) is null) &&
                attribute.CodeContext.RecoveryCause(attribute) is null)
            {
                marker = attribute; // A misplaced marker the parser kept is its syntax Error's recovery and marks nothing.
            }

            if (marker is not null)
            {
                marker.BindingFailure = BindingFailure.None;
                binding.Fail(marker, BindingFailure.InvalidTestDefinition);
                binding.nodes.Add(marker);
            }

            node.VisitChildren(this);
        }
    }

    private sealed class IndexVisitor(Binding binding) : KotoVisitor
    {
        private void IndexCandidates(Koto syntax, bool guarded)
        {
            syntax = KotoHelper.UnwrapParentheses(syntax);
            if (syntax is not SyntaxFormKoto form)
            {
                return;
            }

            if (form.Akind == KotoKind.BindingPattern && form.Operands[0] is IdentifierNameKoto name)
            {
                if (!guarded)
                {
                    binding.symbols.Remove(name);
                    return;
                }

                if (binding.symbols.TryGetValue(name, out var candidate) && candidate.Name != name.IdentifierName)
                {
                    binding.symbols.Remove(name);
                }

                var bodySymbol = form.BoundSymbol;
                binding.Declare(name, name.IdentifierName, BindingSymbolKind.PatternCandidate, form, this.Scope);
                form.BoundSymbol = bodySymbol;
                return;
            }

            foreach (var operand in form.Operands)
            {
                this.IndexCandidates(operand, guarded);
            }
        }

        private int patternDepth;

        internal BindingScope Scope { get; set; } = null!;

        public void IndexMatchArms(MatchKoto match, BindingScope scope)
        {
            var previous = this.Scope;
            this.Scope = scope;
            var outer = this.Scope;
            for (var i = 0; i < match.Arms.Count; i++)
            {
                var arm = match.Arms[i];
                var armScope = binding.GetScope(arm.Pattern, outer);
                this.Scope = armScope;
                this.patternDepth++;
                this.Visit(arm.Pattern);
                this.patternDepth--;
                if (arm.Guard is { } guard)
                {
                    this.Scope = binding.GetScope(guard, outer);
                    binding.candidateScopes.Add(guard);
                    this.IndexCandidates(arm.Pattern, true);

                    this.Visit(guard);
                }
                else
                {
                    this.IndexCandidates(arm.Pattern, false);
                }

                this.Scope = armScope;
                this.Visit(arm.Body);
                this.Scope = outer;
            }

            this.Scope = previous;
        }

        public override void Visit(Koto node)
        {
            node.BindingState = BindingState.Unvisited;
            if (node is BinaryKoto binary)
            {
                binary.ComparisonActive = false;
            }

            if (node.FormattingStorage is { } formatting)
            {
                formatting.Active = false;
            }

            node.BindingFailure = BindingFailure.None;
            node.BoundMeaning = null;
            node.ErasedFunctionType = null;
            node.BoundSymbol = null;
            if (node is FunctionKoto test && TestDefinition.Marker(test) is { } marker && !TestDefinition.IsIncluded(test))
            {
                // Product lookup and analysis never visit the test's signature names or body.
                test.BoundType = BoundType.Unit;
                test.BindingState = BindingState.Resolved;
                if (!TestDefinition.IsValidSyntax(test))
                {
                    marker.BindingFailure = BindingFailure.None;
                    binding.Fail(marker, BindingFailure.InvalidTestDefinition);
                    binding.nodes.Add(marker);
                    binding.Fail(test, BindingFailure.InvalidTestDefinition);
                }
                else
                {
                    for (var attribute = test.AttributeChain; attribute is not null; attribute = attribute.AttributeChain)
                    {
                        if (!ReferenceEquals(attribute, marker))
                        {
                            if (attribute.IdentifierKoto is IdentifierNameKoto { IdentifierName: "Layout" })
                            {
                                // The syntax visitor reports Layout's wrong target below.
                                continue;
                            }

                            // No Mod marker registry exists yet; selection does not recognize an unknown marker.
                            attribute.BindingFailure = BindingFailure.None;
                            binding.Fail(attribute, BindingFailure.Unsupported, true);
                            binding.nodes.Add(attribute);
                        }
                    }
                }

                test.VisitChildren(binding.testSyntaxVisitor ??= new(binding));
                return;
            }

            if (node is AttributeKoto misplaced && node.CodeContext.RecoveryCause(node) is not null)
            {
                // A misplaced attribute the parser kept for the source round trip is its syntax Error's recovery (SPEC 6.5): it
                // marks nothing, so the node it was kept on is checked on its own.
                misplaced.BoundType = BoundType.Unit;
                misplaced.BindingState = BindingState.Resolved;
                if (misplaced.AttributeChain is { } precedingMisplaced)
                {
                    this.Visit(precedingMisplaced);
                }

                return;
            }

            if (node is AttributeKoto { IdentifierKoto: IdentifierNameKoto { IdentifierName: "Test" } } invalidTest)
            {
                if (invalidTest.Parent is FunctionKoto testOwner && TestDefinition.IsIncluded(testOwner) && TestDefinition.IsValidSyntax(testOwner))
                {
                    invalidTest.BoundType = BoundType.Unit;
                    invalidTest.BindingState = BindingState.Resolved;
                    if (invalidTest.AttributeChain is { } earlier)
                    {
                        this.Visit(earlier);
                    }

                    return;
                }

                binding.Fail(node, BindingFailure.InvalidTestDefinition);
                binding.nodes.Add(node);
                if (AttributeTarget(invalidTest) is { } invalidTarget)
                {
                    binding.Fail(invalidTarget, BindingFailure.InvalidTestDefinition);
                }

                if (invalidTest.AttributeChain is { } precedingMarker)
                {
                    this.Visit(precedingMarker);
                }

                return;
            }

            if (node is AttributeKoto { IdentifierKoto: IdentifierNameKoto { IdentifierName: "Layout" } } layout)
            {
                binding.IndexLayoutAttribute(layout);
                if (layout.AttributeChain is { } precedingAttribute)
                {
                    this.Visit(precedingAttribute);
                }

                return;
            }

            if (node is AttributeKoto { IdentifierKoto: IdentifierNameKoto { IdentifierName: "LibraryImport" } } import && AttributeTarget(import) is FunctionKoto)
            {
                // SPEC 22.3.1: the operand names a library and symbol; it is validated, never evaluated.
                import.BindingFailure = BindingFailure.None;
                import.BindingState = BindingState.Unvisited;
                import.BoundType = null;
                binding.nodes.Add(import);
                if (import.AttributeChain is { } precedingImport)
                {
                    this.Visit(precedingImport);
                }

                return;
            }

            if (node is AttributeKoto selectedAttribute && AttributeTarget(selectedAttribute) is { } target &&
                (selectedAttribute.IdentifierKoto is not IdentifierNameKoto { IdentifierName: "LibraryImport" } || target is not FunctionKoto))
            {
                // Layout/Test are handled above. Unrecognized markers and non-function
                // LibraryImport targets cannot certify; retain syntax and diagnostics.
                binding.Fail(target, BindingFailure.InvalidTypeFormation);
            }

            if (node is EffectBoundKoto)
            {
                // SPEC 8.4.10.1: the selector and Name of an effect item designate a Contract and its requirement; they are
                // resolved with the item, never as expressions.
                binding.nodes.Add(node);
                return;
            }

            if (node is ConversionKoto conversion)
            {
                conversion.ConversionBinding = ConversionBinding.None;
            }

            if (node is IsKoto clause)
            {
                clause.BoundConstraint = null;
                clause.BoundRuntimeTest = null;
            }

            binding.nodes.Add(node);
            if (this.patternDepth != 0)
            {
                binding.patternNodes.Add(node);
                if (node is SyntaxFormKoto { Akind: KotoKind.BindingPattern } pattern && pattern.Operands[0] is IdentifierNameKoto name)
                {
                    if (binding.symbols.TryGetValue(pattern, out var existing) && existing.Name != name.IdentifierName)
                    {
                        binding.symbols.Remove(pattern);
                    }

                    binding.Declare(pattern, name.IdentifierName, BindingSymbolKind.Local, pattern, this.Scope);
                }

                node.VisitChildren(this);
                return;
            }

            var previous = this.Scope;
            if (node.Parent is CodeBlockKoto { Parent: FunctionKoto { IsGenerated: true } } && node.CodeContext.SourceDocument is { } source)
            {
                this.Scope = binding.GetScope(source, binding.ModuleScope(node), node.Parent);
            }

            switch (node)
            {
                case IsKoto or SyntaxFormKoto when AssociatedHead(node) is OriginApplicationKoto:
                    this.Scope = binding.GetScope(node, this.Scope);
                    break;
                case ForKoto iteration:
                    this.Visit(iteration.Iterable);
                    this.Scope = binding.GetScope(iteration.Body, this.Scope);
                    for (var i = 0; i < iteration.Bindings.Count; i++)
                    {
                        var name = iteration.Bindings[i];
                        binding.Declare(name, name.IdentifierName, BindingSymbolKind.Local, name, this.Scope);
                    }

                    this.Visit(iteration.Body);
                    this.Scope = previous;
                    return;
                case MatchKoto match:
                    binding.IndexMatch(match);
                    this.Visit(match.Expression);
                    if (match is TryKoto propagation)
                    {
                        propagation.SemanticsIndexed = false;
                    }
                    else
                    {
                        this.IndexMatchArms(match, this.Scope);
                    }

                    this.Scope = previous;
                    return;
                case SyntaxFormKoto { Akind: KotoKind.EnumCase, Parent: EnumKoto } enumeration when TryEnumPayload(enumeration, out _) && enumeration.Operands[0] is IdentifierNameKoto caseName:
                    var caseSymbol = binding.Declare(node, caseName.IdentifierName, BindingSymbolKind.EnumCase, node, this.Scope);
                    caseSymbol.EnumCase ??= new(caseSymbol, this.Scope.Owner.BoundSymbol!);
                    caseSymbol.EnumCase.Owner = this.Scope.Owner.BoundSymbol!;
                    break;
                case SyntaxFormKoto { Akind: KotoKind.ConditionalConformance, Parent: DeclarationContainerKoto }:
                    this.Scope = binding.GetScope(node, this.Scope);
                    this.Scope.ConformancePath = null;
                    break;
                case DeclarationContainerKoto container:
                    if (container is StructKoto structure)
                    {
                        structure.PrepareImplicitConstructor();
                    }

                    if (!container.IsRoot)
                    {
                        var kind = container is GroupKoto ? BindingSymbolKind.Container : BindingSymbolKind.Type;
                        var symbol = binding.Declare(node, container.Name, kind, node, this.Scope);
                        if (kind == BindingSymbolKind.Type)
                        {
                            symbol.Type ??= binding.InternType(BoundTypeKind.Nominal, symbol, SemanticsKind.Owner, []);
                        }
                    }

                    this.Scope = container.IsRoot ? binding.ModuleScope(node) : binding.GetScope(node, this.Scope);
                    break;
                case FunctionKoto function:
                    if (function.IsAnonymous)
                    {
                        if (!binding.symbols.TryGetValue(function, out var closureSymbol))
                        {
                            closureSymbol = new(string.Empty, BindingSymbolKind.Function, function, this.Scope);
                            binding.symbols.Add(function, closureSymbol);
                        }

                        closureSymbol.Scope = this.Scope;
                    }

                    if (!function.IsGenerated && !function.IsAnonymous)
                    {
                        var memberScope = this.Scope.Owner is SyntaxFormKoto { Akind: KotoKind.ConditionalConformance } ? this.Scope.Parent! : this.Scope;
                        var conditional = this.Scope.Owner as SyntaxFormKoto;
                        if (IsRootMain(function))
                        {
                            memberScope = binding.ModuleScope(node);
                        }

                        var symbol = binding.Declare(node, function.Name, BindingSymbolKind.Function, node, memberScope);
                        symbol.ConditionalDeclaration = conditional is { Akind: KotoKind.ConditionalConformance } ? conditional : null;
                        // The members of the fixed array and the integer Position witness are receiver functions of internal
                        // Kimi groups (SPEC 22.1, 4.6.2, PLAN G32).
                        if (memberScope.Owner is StructKoto or EnumKoto or ContractKoto || binding.Library.IsBuiltinMemberGroup(memberScope.Owner))
                        {
                            for (var p = 0; p < function.Parameters.Count; p++)
                            {
                                if (function.Parameters[p].InternalName == "self")
                                {
                                    if (symbol.ReceiverIndex >= 0)
                                    {
                                        binding.Fail(function, BindingFailure.InvalidTypeFormation);
                                    }

                                    symbol.ReceiverIndex = p;
                                }
                            }
                        }
                    }

                    this.Scope = binding.GetScope(node, this.Scope);
                    for (var i = 0; i < function.Parameters.Count; i++)
                    {
                        var parameter = function.Parameters[i];
                        var symbol = binding.Declare(parameter, parameter.InternalName, BindingSymbolKind.Parameter, node, this.Scope);
                        symbol.Slot = i;
                    }

                    node.BoundSymbol = binding.symbols.GetValueOrDefault(node);
                    binding.IndexSpecialReceiver(function, this.Scope);
                    break;
                case PropertyAccessorKoto accessor:
                    this.Scope = binding.GetScope(node, this.Scope);
                    binding.IndexAccessor(accessor, this.Scope);
                    break;
                case CodeBlockKoto:
                    if (node.Parent is ForKoto || node.Parent is SyntaxFormKoto { Akind: KotoKind.ConditionalConformance } ||
                        (node.Parent is MatchKoto && binding.patternNodes.Contains(this.Scope.Owner)))
                    {
                        binding.scopes[node] = this.Scope;
                    }
                    else if (node.Parent is not FunctionKoto)
                    {
                        this.Scope = binding.GetScope(node, this.Scope);
                    }
                    else
                    {
                        binding.scopes[node] = this.Scope;
                    }

                    break;
                case VariableKoto variable:
                    var declarationScope = node is PropertyKoto && this.Scope.Owner is SyntaxFormKoto { Akind: KotoKind.ConditionalConformance } ? this.Scope.Parent! : this.Scope;
                    var variableSymbol = binding.Declare(node, variable.NameKoto.IdentifierName, node is PropertyKoto ? BindingSymbolKind.Property : BindingSymbolKind.Local, node, declarationScope);
                    variableSymbol.ConditionalDeclaration = ReferenceEquals(declarationScope, this.Scope) ? null : (SyntaxFormKoto)this.Scope.Owner;
                    if (node is PropertyKoto property)
                    {
                        var bound = variableSymbol.Property ??= new(variableSymbol);
                        bound.IsVerified = false;
                        var getter = property.GetAccessor(PropertyAccessorKind.Get);
                        Reset(bound.Getter, getter, bound.IsStored || getter is not null);
                        Reset(bound.Setter, property.GetAccessor(PropertyAccessorKind.Set), property.DeclarationKind == PropertyDeclarationKind.Var || property.GetAccessor(PropertyAccessorKind.Set) is not null);
                        if (!bound.IsStored && getter is null)
                        {
                            binding.Fail(property, BindingFailure.InvalidTypeFormation);
                        }
                    }

                    break;
                case GenericParameterKoto parameter:
                    var typeSymbol = binding.Declare(node, parameter.Identifier, parameter.SemanticsParameter is null ? BindingSymbolKind.TypeParameter : BindingSymbolKind.SemanticsTarget, node, this.Scope);
                    typeSymbol.Slot = this.Scope.Types.Count - 1;
                    typeSymbol.WholeType ??= new(parameter.Identifier, BoundTypeKind.Parameter, typeSymbol);
                    typeSymbol.Type ??= parameter.SemanticsParameter is null ? typeSymbol.WholeType : new(parameter.Identifier, BoundTypeKind.TargetProjection, typeSymbol);
                    if (parameter.SemanticsParameter is not null)
                    {
                        if (!binding.pairSymbols.TryGetValue(parameter, out var semanticsSymbol))
                        {
                            semanticsSymbol = new(parameter.SemanticsParameter, BindingSymbolKind.SemanticsParameter, node, this.Scope);
                            binding.pairSymbols.Add(parameter, semanticsSymbol);
                        }

                        semanticsSymbol.Scope = this.Scope;
                        semanticsSymbol.Pair = typeSymbol;
                        typeSymbol.Pair = semanticsSymbol;
                        if (!this.Scope.Types.TryAdd(parameter.SemanticsParameter, semanticsSymbol))
                        {
                            binding.Fail(node, BindingFailure.Duplicate);
                        }
                    }

                    break;
                case LengthParameterKoto parameter:
                    binding.Declare(node, parameter.Identifier, BindingSymbolKind.LengthParameter, node, this.Scope).Type = BoundType.ISize;
                    break;
                case AliasKoto alias:
                    binding.aliases.Add(alias);
                    break;
            }

            node.VisitChildren(this);
            this.Scope = previous;
        }

        private static void Reset(BoundAccessor accessor, PropertyAccessorKoto? declaration, bool present)
        {
            accessor.Declaration = declaration;
            if (accessor.SignatureSymbol is { } signature && !ReferenceEquals(signature.Declaration, declaration))
            {
                accessor.SignatureSymbol = null;
            }

            accessor.IsPresent = present;
            accessor.Receiver = accessor.Input = accessor.Result = null;
            if (accessor.SelfSymbol is { } self)
            {
                self.Type = null;
            }

            if (accessor.ValueSymbol is { } value)
            {
                value.Type = null;
            }

            if (accessor.StorageSymbol is { } storage)
            {
                storage.Type = null;
            }
        }
    }
}
