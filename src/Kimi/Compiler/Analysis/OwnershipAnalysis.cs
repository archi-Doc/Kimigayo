// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

/// <summary>Builds and solves whole-value and enum-construction ownership CFGs using committed Binding operations.</summary>
public sealed partial class OwnershipAnalysis
{
    internal const int DeferredOperationLimit = 8192;
    private readonly Compilation compilation;
    private readonly List<OwnershipBody> bodies = new();
    private readonly List<OwnershipBody> bodyPool = new();
    private readonly List<OwnershipIssue> issues = new();
    private readonly List<FunctionKoto> libraryBodies = new();
    private readonly HashSet<FunctionKoto> usedImports = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<BindingSymbol> witnessTypes = new(ReferenceEqualityComparer.Instance);
    private readonly List<FunctionKoto> witnessScratch = new();
    private readonly Collector collector;
    private readonly List<Registration> locals = new();
    private readonly List<Registration> temporaries = new();
    private readonly List<LoopFrame> loops = new();
    private readonly List<int> arguments = new();
    private readonly List<CheckingContinuation> terminalSeeds = new();
    private readonly HashSet<InvocationKoto> referenceCalls = new(ReferenceEqualityComparer.Instance);

    // SPEC 8.4.10.4: one effect region per abstract input Type of the body's generic requirement calls.
    private readonly Dictionary<BoundType, int> effectRegions = new(ReferenceEqualityComparer.Instance);
    private ControlFlowAnalysis? flow;
    private OwnershipBody body = null!;
    private bool fixedArrayWitnesses;
    private bool integerPositionWitness;
    private int current;
    private int resultPlace;
    private int normalExit;
    private int abortExit;
    private int registrationSequence;
    private int checkingRegion;
    private int deferredLoopBase;
    private int deferredSelectionBase;
    private int deferredDepth;
    private int activeDeferred;

    internal OwnershipAnalysis(Compilation compilation)
    {
        this.compilation = compilation;
        this.collector = new(this);
    }

    public OwnershipResult Result { get; private set; }

    public IReadOnlyList<OwnershipBody> Bodies => this.bodies;

    public IReadOnlyList<OwnershipIssue> Issues => this.issues;

    public ControlFlowAnalysis? ControlFlow => this.flow;

    /// <summary>Checks all selected project function bodies after final Binding, reusing storage.</summary>
    /// <returns>The current subset verification summary.</returns>
    public OwnershipResult Analyze()
    {
        var binding = this.compilation.Binding;
        if (binding.Result.Mode != BindingMode.Final)
        {
            throw new InvalidOperationException("Ownership analysis requires final Binding.");
        }

        this.compilation.Diagnostics.Invalidate(DiagnosticPartition.ControlFlow);
        this.compilation.Diagnostics.Invalidate(DiagnosticPartition.Ownership);

        this.Invalidate();
        this.supportedTypes.Clear();
        this.checkingPatternTypes.Clear();
        this.visitingTypes.Clear();
        var root = this.compilation.Kotonoha.RootKoto;
        if (this.flow is null)
        {
            this.flow = ControlFlowAnalysis.Analyze(root, binding.TypeSystem);
        }
        else
        {
            this.flow.Reanalyze(root);
        }

        for (var i = 1; i < this.compilation.SourceModules.Length; i++)
        {
            this.flow.Append(this.compilation.SourceModules[i].RootKoto);
        }

        if (!binding.Result.IsComplete)
        {
            return this.Result;
        }

        if (this.ReportUnprovenOriginObligations())
        {
            // An unchecked Origin obligation that is not a relation rejects the program with a diagnostic at its use, never silently.
            return this.Result = new(false, 0, this.issues.Count, 0);
        }

        this.collector.DefaultsOnly = true;
        foreach (var module in this.compilation.SourceModules)
        {
            this.collector.Visit(module.RootKoto);
        }

        this.collector.DefaultsOnly = false;
        foreach (var module in this.compilation.SourceModules)
        {
            this.collector.Visit(module.RootKoto);
        }

        this.AnalyzeLibraryBodies(0);
        this.ValidateEffectBounds();
        var errors = 0;
        var unsupported = 0;
        for (var i = 0; i < this.issues.Count; i++)
        {
            if (this.issues[i].Failure is OwnershipFailure.Unsupported or OwnershipFailure.ExpansionLimit)
            {
                unsupported++;
            }
            else
            {
                errors++;
            }
        }

        var verified = this.flow.Issues.Count == 0 && this.flow.PendingBinding.Count == 0 && errors == 0 && unsupported == 0;
        if (!verified)
        {
            // A caller cannot certify a program containing an unchecked callee or flow contract.
            for (var i = 0; i < this.bodies.Count; i++)
            {
                this.bodies[i].IsVerified = false;
            }
        }

        return this.Result = new(verified, this.bodies.Count, errors, unsupported);
    }

    public void ReportDiagnostics() => this.ReportDiagnostics(this.issues);

    internal void ReportInstanceDiagnostics(OwnershipBody failed, string? context, Koto? site)
        => this.ReportDiagnostics(failed.IssueStorage, DiagnosticRequirement.Emission, context, site);

    internal bool UsesImport(FunctionKoto function) => this.usedImports.Contains(function);

    internal void Invalidate()
    {
        this.ClearInstances();
        this.Result = default;
        for (var i = 0; i < this.bodies.Count; i++)
        {
            this.bodies[i].IsVerified = false;
            this.bodies[i].InvalidateChecking();
        }

        this.bodies.Clear();
        this.libraryBodies.Clear();
        this.usedImports.Clear();
        this.templateBodies.Clear();
        this.witnessTypes.Clear();
        this.fixedArrayWitnesses = false;
        this.integerPositionWitness = false;
        if (this.defaultBody is { } declaration)
        {
            declaration.IsVerified = false;
            declaration.InvalidateChecking();
        }

        this.issues.Clear();
        this.invalidDefaults.Clear();
        this.checkedDefaults.Clear();
        this.candidates.Clear();
        this.unmatchedCheckingSeeds.Clear();
    }

    private void ReportDiagnostics(IReadOnlyList<OwnershipIssue> reportedIssues, DiagnosticRequirement? requirementOverride = null, string? instanceContext = null, Koto? instanceSite = null)
    {
        // Ownership analysis runs only after complete Binding, so its checks never rest on a Binding failure. The result that a
        // Block body leaves undelivered where it falls through rests on the fallthrough that control flow reported there.
        var diagnostics = this.compilation.Diagnostics;
        if (requirementOverride is null)
        {
            diagnostics.Invalidate(DiagnosticPartition.Ownership);
        }

        List<(Koto At, DiagnosticCode Code, object[] Evidence)>? chains = null;
        for (var i = 0; i < reportedIssues.Count; i++)
        {
            var issue = reportedIssues[i];
            var requirement = Requirement(issue);
            if (issue.OperationType is { } operationType && issue.Source is BinaryKoto binary)
            {
                issue.Source.Report(requirement, issue.Code, binary.Akind == KotoKind.Slash ? "division" : "remainder", Binding.DiagnosticTypeName(operationType), note: Note(null), related: Locations(null), condition: Condition(issue));
                continue;
            }

            if (issue.Failure == OwnershipFailure.StorageLimit)
            {
                var span = SignatureSpan(issue.Source) ?? issue.Source.Span;
                issue.Source.Report(requirement, issue.Code, issue.RequiredBytes, issue.LimitBytes, note: Note(null), evidence: [issue.StorageTable], related: Locations(null), span: span, condition: Condition(issue));
                continue;
            }

            if (issue.Failure == OwnershipFailure.CaseLimit)
            {
                // SPEC 8.10, 23.3.6.1: the bound on the Semantics cases of one body is a resource limit, shown at the signature.
                var span = SignatureSpan(issue.Source) ?? issue.Source.Span;
                issue.Source.Report(requirement, issue.Code, this.CaseProduct((FunctionKoto)issue.Source), (long)CaseBound, note: Note(null), related: Locations(null), span: span, condition: Condition(issue));
                continue;
            }

            if (issue.Failure == OwnershipFailure.DefaultArgumentMove)
            {
                ReportDefaultMove(issue);
                continue;
            }

            if (issue.Failure is OwnershipFailure.DefaultArgumentAccess or OwnershipFailure.DefaultArgumentBorrow)
            {
                var advice = issue.Failure == OwnershipFailure.DefaultArgumentAccess ? "Use temporary shared inspection, or create an independent value inside the default" :
                    "Return an independent value or Copy an existing shared reference with external dependencies";
                var defaultCase = this.CaseFact(issue, out var singleDefaultCase);
                issue.Source.Report(
                    requirement,
                    issue.Code,
                    note: Note(CaseNote(null, defaultCase, singleDefaultCase)),
                    evidence: CaseEvidence(issue.Code, null, defaultCase),
                    related: Locations(this.WithCaseDeclarations(issue.Related is { } parameter ? [("declaration", parameter, "preceding prepared parameter")] : null, defaultCase)),
                    advice: advice);
                continue;
            }

            if (issue.Capture >= 0 && issue.Source is FunctionKoto { BoundClosure: { } closure } capturing)
            {
                ReportCapture(issue, capturing, closure.Captures[issue.Capture].Source);
                continue;
            }

            if (issue.Failure == OwnershipFailure.CallEffectConflict)
            {
                ReportCallEffect(issue);
                continue;
            }

            if (issue.Failure == OwnershipFailure.ComparisonLoanConflict && issue.Destroyed is not null)
            {
                ReportDestruction(issue);
                continue;
            }

            if (issue.Failure == OwnershipFailure.UnprovenOrigin && issue.Obligation is { Kind: BindingObligationKind.OriginOutlives, Longer: { } longer, Shorter: { } shorter } obligation)
            {
                // SPEC 15.6.1: a fit's relation is reported at the value that supplies the longer end; a well-formedness relation
                // and a Type's clause substituted at a Type occurrence, a declared relation, at that occurrence.
                var fit = Binding.IsFitObligation(obligation);
                var at = fit && obligation.Use is VariableKoto { InitializerKoto: { } initializer } ? initializer : obligation.Use;
                var relation = new OriginRelationFact(at, longer, shorter, obligation.Equality, obligation.Type, Binding.RefutesOriginRelation(longer, shorter), obligation.Clause);
                var (evidence, advice, related) = Binding.OriginRelationFacts(relation, fit ? "fit" : obligation.Clause is not null ? "declared" : "wellFormed");
                if (ChainOrdinal(at, issue.Code, evidence) is { } chain)
                {
                    at.Report(requirement, issue.Code, note: Note(null), evidence: evidence, advice: advice, related: Locations(related), condition: (ushort)(Condition(issue) | (chain << 8)));
                }

                continue;
            }

            if (issue.Failure == OwnershipFailure.EffectBound && this.compilation.Binding.ReportEffectViolation(issue.Source, requirement, issue.Code, instanceContext, instanceSite))
            {
                continue; // SPEC 8.4.10.6: a destruction the bound excludes, reported at the violating effect.
            }

            if (issue.Failure == OwnershipFailure.CallableEffectBound && this.compilation.Binding.ReportCallableEffectViolation(issue.Source, requirement))
            {
                continue;
            }

            if (issue.Failure == OwnershipFailure.VirtualEffectBound && issue.Source is FunctionKoto implementation)
            {
                this.compilation.Binding.ReportVirtualEffectViolation(implementation, requirement);
                continue;
            }

            // The delivery at the normal end is the only use at the function itself apart from a constructor's field checks, whose
            // discarded body never falls through; a return delivers at its jump.
            if (issue.Failure == OwnershipFailure.UninitializedUse && issue.Source is FunctionKoto { Body: { } body } &&
                this.flow?.Reported(body, DiagnosticCode.FunctionFallthrough_Kd) == true)
            {
                issue.Source.ReportDerived(requirement, [body.KeyOf(DiagnosticRequirement.ControlFlow)]);
                continue;
            }

            // A use at the function itself, such as a constructor's completion, is shown at its signature, not its whole body. A bare
            // Place that needs @move offers the transfer as a repair candidate where its path does not refute Take (SPEC 23.3.6.9).
            var transfer = issue.Failure == OwnershipFailure.TransferRequired;
            var judgment = transfer ? Binding.TakeJudgment(issue.Source) : AcquisitionJudgment.Refuted;
            var found = this.CaseFact(issue, out var single);
            issue.Source.Report(
                requirement,
                issue.Code,
                note: Note(CaseNote(AcquisitionNote(issue) ?? FeatureNote(issue.Feature), found, single)),
                evidence: CaseEvidence(issue.Code, transfer ? issue.Source.ToString() : issue.Feature != OwnershipFeature.None ? issue.Feature : null, found),
                advice: transfer ? Binding.TransferAdvice(issue.Source, judgment) : null,
                related: Locations(this.WithCaseDeclarations(RelatedLocations(issue), found)),
                condition: Condition(issue),
                span: SignatureSpan(issue.Source),
                repairs: transfer && judgment != AcquisitionJudgment.Refuted ? Binding.TransferRepair(issue.Source, issue.Source, judgment) : null);
        }

        // SPEC 23.3.3: an unverified result without an Error in this or an earlier phase reports one fallback at its first
        // incomplete subject, a control-flow obligation still pending after complete Binding. It marks a missing report.
        if (requirementOverride is null && this.compilation.Binding.Result.IsComplete && !this.Result.IsVerified && this.flow is { } flow &&
            !diagnostics.HasErrorsThrough(DiagnosticPartition.Ownership) && FirstPending(flow) is { } pending)
        {
            pending.ReportDerived(DiagnosticRequirement.ControlFlow, [DiagnosticKey.Unresolved]);
        }

        // SPEC 7.2.3: a default may Copy a preceding Copy value or inspect it through temporary shared access; it never moves the
        // prepared argument. A capture entry is located at the entry; no repair is offered, since the needed value is the author's.
        void ReportDefaultMove(in OwnershipIssue issue)
        {
            var entries = issue.Source is FunctionKoto { Captures: { } captured } && issue.Capture >= 0 && issue.Capture < captured.Length ? captured : null;
            var name = entries is not null ? entries[issue.Capture].Name : KotoHelper.UnwrapParentheses(issue.Source).ToString();
            var type = entries is not null ? CapturedType((FunctionKoto)issue.Source, name) : issue.Source.BoundType;
            var copy = type is not null && this.compilation.Binding.ProveCopy(type, issue.Source) == ConstraintProof.Proven;
            var advice = entries is not null
                ? copy ? $"Capture a Copy of the prepared argument instead, as in [{name}]"
                    : "A default closure can neither move nor borrow a preceding argument that is not Copy; build an independent value inside the default and capture that"
                : copy ? $"Copy the prepared argument instead, as in {name} or {name}@copy"
                : $"Inspect the prepared argument through a temporary shared borrow, such as Text.toString({name}), or build an independent value";
            var span = entries is not null ? entries[issue.Capture].Span : (SourceSpan?)null;
            issue.Source.Report(Requirement(issue), issue.Code, note: Note(null), related: Locations(null), condition: Condition(issue), advice: advice, span: span);
        }

        static BoundType? CapturedType(FunctionKoto closure, string name)
        {
            var storage = closure.ClosureStorage?.Storage;
            for (var i = 0; i < (storage?.Count ?? 0); i++)
            {
                if (storage![i].Source.Name == name)
                {
                    return storage[i].Source.Type;
                }
            }

            return null;
        }

        // SPEC 7.6.2: an omitted list never infers a Move, a borrow or a Reborrow, so a capture without Copy needs an entry. In a
        // default, no entry can take a preceding argument other than by Copy (SPEC 7.2.3), so the Advice changes the signature.
        void ReportCapture(in OwnershipIssue issue, FunctionKoto function, BindingSymbol source)
        {
            var name = source.Name;
            var reference = source.Type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq, Components.Count: 1 };
            var omitted = function.Captures is null;
            var prepared = omitted && DefaultParameters.InLaterDefault(function, source);
            var note = !omitted ? null : $"The omitted capture list captures {name} only by Copy; it never infers a Move, a borrow or a Reborrow" +
                (prepared ? ", and a default can neither move a preceding argument nor keep a borrow of it (SPEC 7.2.3)" : string.Empty);
            var advice = !omitted ? null : prepared ? Binding.PreparedCaptureAdvice(name)
                : reference ? $"List the capture as [{name}] to Reborrow the exclusive reference, or [{name}@move] to transfer it" : $"List the capture as [{name}@move] to transfer it, or [{name}@ref] to borrow it";
            issue.Source.Report(Requirement(issue), issue.Code, note: Note(note), related: Locations(null), condition: Condition(issue), evidence: [name], advice: advice);
        }

        // SPEC 8.4.10.6: the Reason names the access and the Loan; the Note states that no available bound excludes it, without
        // asserting that a conflict occurs; the related locations give the Place keeping the Loan and the call that created it.
        void ReportCallEffect(in OwnershipIssue issue)
        {
            var call = issue.Source as InvocationKoto;
            var name = call?.BoundCall?.Target.Name ?? call?.BoundValueCall?.Receiver.ToString() ?? "the callable";
            var holder = issue.LoanSource is VariableKoto { NameKoto.IdentifierName: { } named } ? named : issue.LoanSource?.ToString() ?? "a value";
            (string Role, Koto At, string? Label)[]? related = issue.LoanSource is not { } loan ? null
                : issue.Related is { } earlier ? [("loan", loan, "value retaining the conflicting loan"), ("call", earlier, "the earlier call whose result keeps the loan")]
                : [("loan", loan, "value retaining the conflicting loan")];
            var advice = call?.BoundValueCall is not null
                ? $"End the use of {holder} before this call, or declare effect preserves results on the Callable Constraint when every bound callable satisfies it"
                : $"End the use of {holder} before this call, or require a Contract that declares preserves results for {name}, when every use Type conforms to it";
            issue.Source.Report(
                Requirement(issue),
                issue.Code,
                note: Note($"{name} may affect every Loan that the Type of its inputs may denote, and {holder} keeps such a Loan from an earlier call; under the premises here no bound excludes it (SPEC 8.4.10.4)"),
                evidence: [$"{name} may affect a Loan that {holder} keeps"],
                advice: advice,
                related: Locations(related),
                condition: Condition(issue));
        }

        // SPEC 15.6.2, 15.6.5, 16.2.2: a borrowed Place destroyed while a live value keeps its Loan is reported at the destruction,
        // relating that value and the Borrow or capture entry that created the Loan. A transfer cleans up the scopes it leaves
        // before it delivers its result, so a secured result never carries a borrow of a destroyed local out of its scope.
        void ReportDestruction(in OwnershipIssue issue)
        {
            var entries = issue.Borrow is FunctionKoto { Captures: { } captured } && (uint)issue.BorrowCapture < (uint)captured.Length ? captured : null;
            var borrow = entries is not null ? CaptureText(entries[issue.BorrowCapture]) : issue.Borrow?.ToString();
            var place = issue.Destroyed!.Length > 0 ? $"`{issue.Destroyed}`" : issue.DestroyedTemporary is { } temporary ? $"the temporary `{temporary}`" : "the borrowed Place";
            var kept = borrow is null ? " is destroyed here while a live value keeps its Loan" : $" is destroyed here while a live value keeps the Loan of `{borrow}`";
            var transfer = issue.Source is JumpKoto ? "; the transfer cleans up the scopes it leaves before it delivers its result (SPEC 16.2.2)" : string.Empty;
            (string Role, Koto At, string? Label)[]? related = issue.LoanSource is not { } loan ? null
                : entries is null && issue.Borrow is { } site ? [("loan", loan, RetainedLoanLabel), ("borrow", site, BorrowLabel)]
                : [("loan", loan, RetainedLoanLabel)];
            (string Role, Koto In, SourceSpan Span, string? Label)[]? spans = entries is not null ? [("borrow", issue.Borrow!, entries[issue.BorrowCapture].Span, BorrowLabel)] : null;
            var advice = (issue.Destroyed.Length > 0 ? $"Declare {place} in a scope" : $"Keep {place} in a local") + " that outlives the value keeping its Loan, or keep an owned value instead of the borrow";
            var found = this.CaseFact(issue, out var single);
            issue.Source.Report(
                Requirement(issue),
                issue.Code,
                note: Note(CaseNote(char.ToUpperInvariant(place[0]) + place[1..] + kept + transfer, found, single)),
                evidence: CaseEvidence(issue.Code, null, found),
                advice: advice,
                related: Locations(this.WithCaseDeclarations(related, found)),
                relatedSpans: spans,
                condition: Condition(issue));
        }

        // SPEC 8.10, 23.3.6.4: a problem found under some Semantics cases names them in its Note.
        static string? CaseNote(string? note, string? found, bool single)
            => found is null ? note : (note is null ? "Found" : note + "; found") + (single ? " under the Semantics case " : " under the Semantics cases ") + found + " (SPEC 8.10)";

        // The `case` fact closes the evidence alternative of the codes that carry it; a code without the alternative keeps its Note.
        static object?[]? CaseEvidence(DiagnosticCode code, object? target, string? found)
        {
            var withCase = found is not null && HasCaseEvidence(code);
            return target is null ? (withCase ? [found] : null) : withCase ? [target, found] : [target];
        }

        static string CaptureText(CaptureKoto entry)
            => (entry.IsMutable ? "var " : string.Empty) + entry.Name + (entry.Operation is { } operation ? "@" + operation : string.Empty);

        DiagnosticRequirement Requirement(in OwnershipIssue issue) => requirementOverride ?? DiagnosticRequirement.Ownership(issue.Failure);

        ushort Condition(in OwnershipIssue issue) => requirementOverride is null ? (ushort)0 : (ushort)issue.Failure;

        // SPEC 15.6.1 Identity: an Origin relation problem is its location, its relation's source and its longer end, and every failed
        // chain is reported, so the chains at one location, such as the failing operands of a meet or the failing relations of one call,
        // are several problems of one node (docs/dev/DIAGNOSTICS.md §4.3), numbered in report order; a chain reported again with the same
        // facts keeps its number and merges. Null past the condition's range, which no source reaches.
        int? ChainOrdinal(Koto at, DiagnosticCode code, object[] evidence)
        {
            chains ??= [];
            var ordinal = 0;
            for (var c = 0; c < chains.Count; c++)
            {
                if (ReferenceEquals(chains[c].At, at))
                {
                    if (chains[c].Code == code && chains[c].Evidence.AsSpan().SequenceEqual(evidence))
                    {
                        return ordinal;
                    }

                    ordinal++;
                }
            }

            if (ordinal > byte.MaxValue)
            {
                return null;
            }

            chains.Add((at, code, evidence));
            return ordinal;
        }

        string? Note(string? note) => instanceContext is null ? note : note is null ? instanceContext : note + "; " + instanceContext;

        (string Role, Koto At, string? Label)[]? Locations((string Role, Koto At, string? Label)[]? related)
        {
            if (instanceSite is null)
            {
                return related;
            }

            var locations = new (string Role, Koto At, string? Label)[(related?.Length ?? 0) + 1];
            related?.CopyTo(locations, 0);
            locations[^1] = ("instantiation", instanceSite, "the call requesting this instance");
            return locations;
        }

        static SourceSpan? SignatureSpan(Koto source)
            => source is FunctionKoto { SignatureSpan.Length: > 0 } function ? function.SignatureSpan : null;

        static Koto? FirstPending(ControlFlowAnalysis flow)
        {
            Koto? first = null;
            foreach (var node in flow.PendingBinding)
            {
                if (first is null)
                {
                    first = node;
                    continue;
                }

                var path = string.CompareOrdinal(node.CodeContext.SourceDocument?.Path, first.CodeContext.SourceDocument?.Path);
                if (path < 0 || (path == 0 && node.Span.Start < first.Span.Start))
                {
                    first = node;
                }
            }

            return first;
        }
    }

    // SPEC 8.10: a definition whose scope has a resolved pair binder is verified once per Semantics case; any other body once.
    private void Build(FunctionKoto function, int declarationDefault = -1)
    {
        if (this.instance is null && this.ResolveCases(function))
        {
            this.BuildCases(function, declarationDefault);
        }
        else
        {
            this.BuildOnce(function, declarationDefault);
        }
    }

    private void BuildOnce(FunctionKoto function, int declarationDefault)
    {
        try
        {
            this.BuildBody(function, declarationDefault);
        }
        catch (DeferredExpansionLimitException limit)
        {
            this.body.ReportReservedElementWrites(completed: false);
            this.body.ReportIssue(new(limit.SourceNode, OwnershipFailure.ExpansionLimit));
            this.AppendIssues();
        }
        catch (OwnershipStorageLimitException limit)
        {
            this.body.IsVerified = false;
            this.body.ReportReservedElementWrites(completed: false);
            this.body.ReportIssue(new(function, OwnershipFailure.StorageLimit, StorageTable: limit.Table, RequiredBytes: limit.RequiredBytes, LimitBytes: limit.LimitBytes));
            this.AppendIssues();
            if (this.instance is not null)
            {
                this.InstanceStorageLimit = limit.Message;
            }
        }
    }

    private void BuildBody(FunctionKoto function, int declarationDefault)
    {
        if (declarationDefault >= 0)
        {
            // One reusable declaration scratch graph, never an executable function
            // body. Its diagnostics are retained before the next default reuses it.
            this.body = this.instanceBody ?? this.caseBody ?? (this.defaultBody ??= new());
        }
        else if (this.instanceBody is { } instanceBody)
        {
            this.body = instanceBody; // Never listed with the checked source bodies.
        }
        else if (this.caseBody is { } caseBody)
        {
            this.body = caseBody; // A Semantics case other than the first (SPEC 8.10); never listed.
        }
        else
        {
            if (this.bodies.Count == this.bodyPool.Count)
            {
                this.bodyPool.Add(new());
            }

            this.body = this.bodyPool[this.bodies.Count];
            this.templateBodies[function] = this.body;
            this.bodies.Add(this.body);
        }

        this.body.Reset(function, this.instance, this.compilation.Binding, this.cases.AsSpan(0, this.caseCount), this.caseBit);
        this.body.DefaultParameter = declarationDefault;
        this.defaultContext = -1;
        // Abstract Origin bindings affect field Types even when layout is fully
        // concrete. Prepare the same substituted metadata used by closed calls.
        for (var parameterIndex = 0; parameterIndex < function.Parameters.Count; parameterIndex++)
        {
            if (function.Parameters[parameterIndex].Type.BoundType is { } parameterType)
            {
                this.compilation.Binding.PrepareTypeStorage(parameterType);
            }
        }

        this.locals.Clear();
        this.temporaries.Clear();
        this.loops.Clear();
        this.arguments.Clear();
        this.referenceCalls.Clear();
        this.effectRegions.Clear();
        this.formattingPlaces.Clear();
        this.placeValues.Clear();
        this.resultHeads.Clear();
        this.resultJoins.Clear();
        this.resultDeclarations.Clear();
        this.pendingResults.Clear();
        this.selections.Clear();
        this.candidates.Clear();
        this.comparisonDepth = 0;
        this.activeDecompositions.Clear();
        this.patternStorageNeeded.Clear();
        this.registrationSequence = 0;
        this.checkingRegion = 0;
        this.terminalSeeds.Clear();
        this.normalCheckingSeeds.Clear();
        this.caughtCheckingSeeds.Clear();
        this.deferredLoopBase = 0;
        this.deferredSelectionBase = 0;
        this.deferredDepth = 0;
        this.activeDeferred = -1;
        this.current = -1;
        this.Emit(OwnershipOperationKind.Entry, function);
        this.normalExit = this.New(OwnershipOperationKind.Exit, function);
        this.abortExit = this.New(OwnershipOperationKind.Exit, function);
        this.resultPlace = this.Place(function, declarationDefault >= 0 ? function.Parameters[declarationDefault].Type.BoundType : function.BoundSymbol?.Type ?? BoundType.Unit, OwnershipPlaceKind.Result, true);
        if (declarationDefault < 0 && function.Captures is { Length: > 0 } && function.BoundClosure is null)
        {
            this.Unsupported(function);
        }

        var parameterCount = declarationDefault >= 0 ? declarationDefault : function.Parameters.Count;
        for (var i = 0; i < parameterCount; i++)
        {
            var parameter = function.Parameters[i];
            var type = this.Resolve(parameter.Type.BoundType, this.Active);
            var place = this.Place(parameter.Type, type, OwnershipPlaceKind.Parameter, false);
            this.body.SymbolPlaces[this.compilation.Binding.ParameterSymbol(function, i)] = place;
            this.locals.Add(new(place, parameter.Type, this.registrationSequence++));
            var initialized = this.Emit(OwnershipOperationKind.Produce, parameter.Type, place);
            if (type is not null && (ScalarResult(type) || ReferenceTypes.IsString(type)))
            {
                this.SetValue(initialized, OwnershipValueKind.Parameter, [], constant: i);
            }
        }

        if (declarationDefault >= 0)
        {
            // SPEC 7.2.3: the default is checked as a body that delivers its value to the parameter, so an owned result, such as
            // an erased Function, is acquired once. Prepared arguments remain owned by the pending call. Declaration
            // checking neither destroys them nor applies the callee's return contract.
            var defaultValue = function.Parameters[declarationDefault].DefaultValue!;
            var reported = this.body.IssueStorage.Count;
            var securedDefault = this.WriteResult(defaultValue, this.resultPlace, this.Expression(defaultValue), reported);
            this.Cleanup(0, parameterCount, defaultValue, CleanupReason.Return);
            this.Deliver(function, securedDefault);
            this.Connect(this.current, this.normalExit, OwnershipEdgeKind.Return);
            this.CompleteBody(function, declarationDefault);
            return;
        }

        if (function.BoundClosure is { } closure)
        {
            for (var i = 0; i < closure.Captures.Count; i++)
            {
                var capture = closure.Captures[i].Environment;
                var place = this.Place(function, capture.Type, closure.EnvironmentType is null ? OwnershipPlaceKind.Parameter : OwnershipPlaceKind.Local, capture.MutableCapture);
                this.body.SymbolPlaces[capture] = place;
                var initialized = this.Emit(OwnershipOperationKind.Produce, function, place);
                this.SetValue(initialized, OwnershipValueKind.Capture, [], constant: i);
                if (closure.EnvironmentType is not null && closure.Receiver == SemanticsKind.Owner)
                {
                    this.locals.Insert(i, new(place, function, i - closure.Captures.Count));
                }
            }
        }

        this.PrepareReceiverFields(function);
        var secured = -1;
        if (function.Body is { } block)
        {
            this.Block(block);
            if (ReferenceEquals(this.body.PlaceStorage[this.resultPlace].Type, BoundType.Unit))
            {
                this.Emit(OwnershipOperationKind.Produce, function, this.resultPlace);
            }
        }
        else if (function.ExpressionBody is { } expression)
        {
            if (KotoHelper.DiscardsFunctionBody(function) || !KotoHelper.IsBodyExpression(expression))
            {
                this.Statement(expression);
                this.Emit(OwnershipOperationKind.Produce, expression, this.resultPlace);
            }
            else
            {
                var reported = this.body.IssueStorage.Count;
                var value = this.Expression(expression);
                secured = this.WriteResult(expression, this.resultPlace, value, reported);
            }
        }
        else
        {
            this.Unsupported(function);
        }

        this.CheckEnvironmentMoves(function);
        this.CheckConstruction(function);
        this.Cleanup(0, 0, function, CleanupReason.Return);
        this.Deliver(function, secured);
        this.Connect(this.current, this.normalExit, OwnershipEdgeKind.Return);
        this.CompleteBody(function);
    }

    // SPEC 7.6.2, 7.6.3: Shared and Exclusive calls only borrow their captures, and the environment destroys them afterwards, so a
    // Move out of a capture belongs to a Consuming call; Binding classifies the receiver, and this check keeps that classification
    // honest.
    private void CheckEnvironmentMoves(FunctionKoto function)
    {
        if (function.BoundClosure is not { EnvironmentType: not null, Receiver: not SemanticsKind.Owner } closure)
        {
            return;
        }

        for (var op = 0; op < this.body.Operations.Count; op++)
        {
            if (this.body.Operations[op] is not { Kind: OwnershipOperationKind.Consume, Acquisition: AcquisitionKind.Move, Input: >= 0 } moved)
            {
                continue;
            }

            for (var i = 0; i < closure.Captures.Count; i++)
            {
                if (this.body.SymbolPlaces.TryGetValue(closure.Captures[i].Environment, out var place) && place == moved.Input)
                {
                    this.Unsupported(moved.Source);
                    break;
                }
            }
        }
    }

    private void CompleteBody(FunctionKoto function, int declarationDefault = -1)
    {
        this.body.Solve();
        this.FinalizeResults();
        this.body.CheckUnreachable();
        this.body.PrepareCallReservations();
        this.body.VerifyBorrows();
        if (declarationDefault >= 0)
        {
            this.body.VerifyPreparedDefault(this.resultPlace);
        }

        this.body.VerifyCallReservations();
        this.body.ReportReservedElementWrites(completed: true);
        this.AppendIssues();
        this.body.IsVerified = this.body.IssueStorage.Count == 0 && function.BindingState == BindingState.Resolved;
    }

    private int Place(Koto source, BoundType? type, OwnershipPlaceKind kind, bool mutable, AcquisitionKind? plannedAcquisition = null)
    {
        var id = this.body.PlaceStorage.Count;
        type = this.Resolve(type, this.Active) ?? BoundType.Unit;
        // Never has no value storage. Its result marker is only used on unreachable
        // delivery nodes; control-flow checking rejects any normal completion.
        var neverResult = kind == OwnershipPlaceKind.Result && ReferenceEquals(type, BoundType.Never);
        var invalidCopy = false;
        var acquisition = plannedAcquisition.GetValueOrDefault();
        // A case or an instance resolves a committed CopyOrMove to the exact effect of its substituted Type (SPEC 8.10, 21.3.1).
        if (plannedAcquisition is null || (acquisition == AcquisitionKind.CopyOrMove && this.Substituting))
        {
            // Primitive classification needs no Constraint environment (SPEC 3.5.1).
            var proof = type.Kind == BoundTypeKind.Primitive && (!ReferenceEquals(type, BoundType.Never) || neverResult)
                ? (type.Name == "string" ? ConstraintProof.Refuted : ConstraintProof.Proven)
                : this.compilation.Binding.ProveCopy(type, source);
            invalidCopy = proof == ConstraintProof.Error;
            acquisition = proof == ConstraintProof.Proven ? AcquisitionKind.Copy : proof == ConstraintProof.Refuted ? AcquisitionKind.Move : AcquisitionKind.CopyOrMove;
        }

        this.AddPlace(new(id, source, type, kind, mutable, acquisition)
        {
            DeferredExecution = kind is OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result ? this.activeDeferred : -1,
            DefaultContext = this.defaultContext,
        });
        this.body.IsConcrete &= !AbstractTypes.HasAbstractPart(type);
        if (invalidCopy || !(neverResult || type.Kind == BoundTypeKind.Parameter || this.SupportsType(type)))
        {
            this.Unsupported(source);
        }

        return id;
    }

    // Every Place, including a region or an anchor that holds no value, has a row in the builder's per-Place tables.
    private int AddPlace(OwnershipPlace place)
    {
        this.body.PlaceStorage.Add(place);
        this.placeValues.Add(-1);
        this.resultDeclarations.Add(-1);
        return place.Id;
    }

    private int Temporary(Koto source, bool produce = true, int projection = -1)
    {
        // A non-completing block/selection may reserve a result destination, but
        // Never has no produced value and no temporary cleanup registration.
        var kind = !produce && ReferenceEquals(source.BoundType, BoundType.Never) ? OwnershipPlaceKind.Result : OwnershipPlaceKind.Temporary;
        var id = this.Place(source, source.BoundType, kind, true);
        if (produce)
        {
            this.Emit(OwnershipOperationKind.Produce, source, id, acquisition: projection >= 0 && this.body.Places[id].Acquisition is AcquisitionKind.Move or AcquisitionKind.CopyOrMove ? this.body.Places[id].Acquisition : AcquisitionKind.None, projection: projection);
            this.RegisterTemporary(id);
        }

        return id;
    }

    // A temporary of a Type other than the syntax's own: the reference a Place call returns.
    private int ReferenceTemporary(Koto source, BoundType type)
    {
        var id = this.Place(source, type, OwnershipPlaceKind.Temporary, true);
        this.Emit(OwnershipOperationKind.Produce, source, id);
        return this.RegisterTemporary(id);
    }

    private int RegisterTemporary(int place)
    {
        this.temporaries.Add(new(place, this.body.PlaceStorage[place].Source, this.registrationSequence++));
        return place;
    }

    private int Local(Koto source)
    {
        var symbol = source.BoundSymbol;
        if (this.TryDefaultSlot(symbol, out var slot))
        {
            return slot;
        }

        if (symbol is not null && this.body.TrySymbolPlace(symbol, this.defaultContext, out var id))
        {
            return id;
        }

        this.Unsupported(source);
        return -1;
    }

    private int LocalPlace(BindingSymbol? symbol, Koto source, BoundType? type, bool mutable, AcquisitionKind? acquisition = null)
    {
        if (this.defaultContext >= 0 && symbol is not null)
        {
            var places = this.body.DefaultSymbolPlaces ??= new();
            var key = (symbol, this.defaultContext);
            if (!places.TryGetValue(key, out var local))
            {
                local = this.Place(source, type, OwnershipPlaceKind.Local, mutable, acquisition);
                places.Add(key, local);
                this.body.SymbolPlaces.TryAdd(symbol, local);
            }

            return local;
        }

        // Deferred replicas have separate operation/value IDs but nonoverlapping lifetimes
        // of the same lexical binding. Declare resets the shared Place on each execution.
        if (symbol is not null && this.body.SymbolPlaces.TryGetValue(symbol, out var existing))
        {
            return existing;
        }

        var place = this.Place(source, type, OwnershipPlaceKind.Local, mutable, acquisition);
        if (symbol is not null)
        {
            this.body.SymbolPlaces.Add(symbol, place);
        }

        return place;
    }

    private int Use(Koto source, int place, PlaceUseKind use, AcquisitionKind? acquisition = null, BoundType? resultType = null)
    {
        if (place < 0)
        {
            return -1;
        }

        if (use != PlaceUseKind.Consume)
        {
            this.Emit(use == PlaceUseKind.Read ? OwnershipOperationKind.Read : OwnershipOperationKind.Borrow, source, place);
            return place;
        }

        // Locals, parameters and the prepared slots named by a default reach here. A Copy retains the slot's complete
        // Type, including its caller's Origins; the declaration syntax may name different Origin binders.
        var stored = this.body.PlaceStorage[place];
        if (acquisition is null)
        {
            // SPEC 15.1.5, 8.9: a bare exclusive reference is reborrowed in its own mode; @move transfers it. A pair Place
            // stores its case Type (SPEC 8.10), so the exclusive cases Reborrow here and the Copy cases Copy below.
            if (stored.Type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq, Components.Count: 1 })
            {
                return this.BorrowStruct(source, stored.Type);
            }

            // SPEC 3.5: a bare Place never Moves; a Non-Copy or Copy-unproven Place needs @move.
            if (stored.Acquisition != AcquisitionKind.Copy)
            {
                this.body.ReportIssue(new(source, OwnershipFailure.TransferRequired));
            }
        }

        this.CheckAcquisition(place, acquisition);
        // A selected object upcast supplies its complete view Type while transferring the same handle responsibility.
        var value = this.Place(source, resultType ?? stored.Type, OwnershipPlaceKind.Temporary, true);
        this.Emit(OwnershipOperationKind.Consume, source, place, value, acquisition ?? stored.Acquisition);
        return this.RegisterTemporary(value);
    }

    private int Block(CodeBlockKoto block, int destination = -1)
        => this.Block(block, out _, destination);

    private int Block(CodeBlockKoto block, out CheckingContinuation continuation, int destination = -1, bool retainCheckingRegion = false)
    {
        var region = this.checkingRegion;
        var result = -1;
        var mark = this.locals.Count;
        for (var i = 0; i < block.Items.Count; i++)
        {
            var item = block.Items[i];
            var temps = this.temporaries.Count;
            if (destination >= 0 && block.HasTrailingExpression && KotoHelper.IsValueContext(item) && i == block.Items.Count - 1)
            {
                var value = this.Expression(item);
                result = this.WriteResult(item, destination, value);
            }
            else
            {
                this.Statement(item);
            }

            this.Cleanup(temps, this.locals.Count, item, CleanupReason.ExpressionEnd);
            this.temporaries.RemoveRange(temps, this.temporaries.Count - temps);
        }

        if (ReferenceEquals(block, this.body.Function.Body))
        {
            // Before the body's cleanup, so a deferred initialization does not complete construction. The constructor is the
            // use, as after cleanup, so one missing field is one record.
            this.CheckConstruction(this.body.Function);
        }

        // Keep the terminal source state before this body's implicit cleanup and
        // lexical-region restoration. A caller must prove that no alternative
        // terminal path was discarded before using this checking-only seed.
        continuation = this.checkingRegion != region ? this.Continuation() : new(-1);
        this.Cleanup(this.temporaries.Count, mark, block, CleanupReason.ScopeExit);
        this.locals.RemoveRange(mark, this.locals.Count - mark);
        // A transfer's source continuation ends with this lexical body. It is not
        // a normal branch completion or a loop backedge, even inside dead source.
        if (!retainCheckingRegion || !this.flow!.Nodes[block].CanCompleteNormally)
        {
            this.checkingRegion = region;
        }

        return result;
    }

    private void Statement(Koto node)
    {
        // An anonymous function is an expression whose evaluation creates its closure, acquiring its capture entries.
        if (node is FunctionKoto { IsAnonymous: false } or DeclarationContainerKoto or AliasKoto or UnitLiteralKoto)
        {
            return;
        }

        if (node is FieldKoto field)
        {
            var id = this.LocalPlace(field.BoundSymbol, field, field.BoundType, field.VariableKind == VariableKind.Var);

            this.locals.Add(new(id, field, this.registrationSequence++));
            this.Emit(OwnershipOperationKind.Declare, field, id);
            if (field.InitializerKoto is NoInitKoto directive)
            {
                // SPEC 4.3.4: ordinary complete construction, with no initializer value to acquire and no element stores.
                this.Emit(OwnershipOperationKind.Produce, directive, id);
            }
            else if (field.InitializerKoto is { } initializer)
            {
                var reported = this.body.IssueStorage.Count;
                var value = this.Expression(initializer);
                if (value >= 0)
                {
                    this.Emit(OwnershipOperationKind.Write, field, id, value);
                }
                else if (this.body.IssueStorage.Count > reported)
                {
                    // The initializer's analysis reported why it has no value; the binding is initialized, so its uses are
                    // not reported again as uninitialized.
                    this.Emit(OwnershipOperationKind.Produce, field, id);
                }
            }
        }
        else if (node is DeferredBlockKoto deferred)
        {
            this.locals.Add(new(-1, deferred, this.registrationSequence++));
        }
        else if (node is UnsafeBlockKoto unsafeBlock)
        {
            this.Block(unsafeBlock.Body);
        }
        else if (KotoHelper.UnwrapParentheses(node) is InvocationKoto call && ElementAccess.IsPlaceCall(call))
        {
            // SPEC 7.1.1: an unacquired Place statement performs the call but reads and destroys no stored value.
            this.Call(call);
        }
        else
        {
            this.Expression(node);
        }
    }

    private int Expression(Koto node, PlaceUseKind use = PlaceUseKind.Consume, AcquisitionKind? acquisition = null)
    {
        // SPEC 10.2: the one implicit adaptation selected at the expression's fixed expected Type.
        if (this.compilation.Binding.TryGetAdaptation(node, out var adaptation))
        {
            if (adaptation.Kind == ExpectedAdaptationKind.ReferentRead)
            {
                return this.LoadReferent(node);
            }

            // A reference read also serves a receiver that is read for member selection (SPEC 3.4.1); a borrow
            // adaptation is formed only where the value is acquired.
            if (adaptation.Kind == ExpectedAdaptationKind.ReferenceRead && acquisition is null && use is PlaceUseKind.Consume or PlaceUseKind.Read)
            {
                return this.ReadReference(node, adaptation.Type);
            }

            if (use == PlaceUseKind.Consume && acquisition is null)
            {
                return this.BorrowStruct(node, adaptation.Type);
            }
        }

        if (node.ErasedFunctionType is { } erased)
        {
            if (node.BoundType?.Kind == BoundTypeKind.FunctionItem && node.BoundSymbol is { Declaration: FunctionKoto item } symbol &&
                (item.IsRequirement ? this.compilation.Binding.FunctionItemContext(node.BoundType, requireClosed: false) is null :
                    (item.Body ?? item.ExpressionBody) is null && symbol.CompilerFunction == CompilerFunctionKind.None))
            {
                this.Unsupported(node); // A requirement without a selected executable entry cannot be erased.
                return -1;
            }

            var source = this.ExpressionCore(node, PlaceUseKind.Consume, acquisition);
            if (source < 0)
            {
                return -1;
            }

            var acquired = this.Value(source);
            this.Emit(OwnershipOperationKind.CallEntry, node, source);
            var result = this.Place(node, erased, OwnershipPlaceKind.Temporary, false);
            var create = this.Emit(OwnershipOperationKind.Produce, node, result);
            this.SetValue(create, OwnershipValueKind.ClosureErasure, [acquired]);
            return this.RegisterTemporary(result);
        }

        return this.ExpressionCore(node, use, acquisition);
    }

    private int ExpressionCore(Koto node, PlaceUseKind use, AcquisitionKind? acquisition)
    {
        if (node is ConversionKoto { ConversionBinding: ConversionBinding.PairFollow } pair && !this.FollowsReference(pair))
        {
            // SPEC 13.5.5.1: the owner case selects the operand itself.
            return this.ExpressionCore(KotoHelper.UnwrapParentheses(pair.Left), use, acquisition);
        }

        if (node is EvaluatedKoto evaluated)
        {
            var operand = this.EvaluatedOperand(evaluated, out var projection);
            if (projection >= 0)
            {
                this.Unsupported(node); // An element projection is read only where the selection reads its receiver.
                return -1;
            }

            return operand;
        }

        if (this.compilation.Binding.PropertyCall(node, PropertyAccessorKind.Get) is { } getter)
        {
            return this.Call(getter);
        }

        if (this.compilation.Binding.IndexerCall(node, false) is { } indexer)
        {
            return this.Expression(indexer, use, acquisition); // SPEC 4.6.9: receiver[key] through a user conformance reads the published Place.
        }

        if (this.compilation.Binding.ViewRangeCall(node) is { } viewRange)
        {
            return this.Expression(viewRange, use, acquisition);
        }

        if (this.compilation.Binding.RangeValueCall(node) is { } rangeValue)
        {
            return this.Expression(rangeValue, use, acquisition); // SPEC 4.6.2, 4.6.3: prefix ^ and range syntax construct Kimi values.
        }

        if (this.compilation.Binding.StorageProjection(node) is { } storage)
        {
            return this.ReadBorrowedField(storage);
        }

        if (node is IdentifierNameKoto or MemberAccessKoto && StaticScalar.TryGet(node.BoundSymbol?.Property, out var staticValue))
        {
            var result = this.Temporary(node);
            this.SetValue(this.Value(result), OwnershipValueKind.Constant, [], constant: staticValue);
            return result;
        }

        if (node is IdentifierNameKoto or MemberAccessKoto && StaticScalar.IsDynamic(node.BoundSymbol?.Property))
        {
            if (use == PlaceUseKind.Borrow)
            {
                this.Unsupported(node);
                return -1;
            }

            var initializer = StaticScalar.Initializer(node.BoundSymbol!.Property!);
            if (ReferenceEquals(node.CodeContext.Kotonoha, this.compilation.Library.Kotonoha) ||
                ReferenceEquals(initializer.CodeContext.Kotonoha, this.compilation.Library.Kotonoha))
            {
                this.CollectLibraryBody(initializer);
            }

            var result = this.Temporary(node);
            this.SetValue(this.Value(result), OwnershipValueKind.StaticRead, []);
            return result;
        }

        if (this.SpecialField(node))
        {
            var place = this.Local(node);
            if (place >= 0 && use == PlaceUseKind.Consume && this.body.Places[place].Acquisition != AcquisitionKind.Copy)
            {
                this.Unsupported(node); // Special receivers cannot lose initialized fields.
            }

            return this.Use(node, place, use, acquisition);
        }

        if (this.compilation.Binding.TryGetEnumConstruction(node, out var construction))
        {
            return this.ConstructEnum(node, construction!);
        }

        if (node.BoundSymbol is { Kind: BindingSymbolKind.Function, Declaration: FunctionKoto itemDefinition } && node.BoundType is { Kind: BoundTypeKind.FunctionItem } itemType)
        {
            this.CollectLibraryBody(itemDefinition);
            for (var i = 0; i < itemType.Components.Count; i++)
            {
                this.CollectLibraryWitnesses(itemType.Components[i]); // The bound arguments of a generic Item, as for a call.
            }

            return this.Temporary(node);
        }

        switch (node)
        {
            case FunctionKoto { BoundClosure: { } } closure:
                return this.CreateClosure(closure);
            case ParenthesizedKoto parentheses:
                return this.Expression(parentheses.Operand, use, acquisition);
            case MacroKoto { Formatting: { } tryWrite }:
                return this.TryWrite(tryWrite);
            case MacroKoto { Operand: InvocationKoto abort } when ReferenceEquals(abort.BoundCall?.Target, this.compilation.Library.Abort):
                return this.Call(abort);
            case InterpolatedStringKoto { Formatting: { } formatting }:
                return this.Formatting(formatting);
            case FormattingKoto formattingValue:
                return this.FormattingValue(formattingValue);
            case IdentifierNameKoto:
                if (node.BoundSymbol?.Kind == BindingSymbolKind.PatternCandidate)
                {
                    return this.ReadCandidate(node, use);
                }

                if (node.BoundSymbol?.Kind == BindingSymbolKind.Function && this.Resolve(node.BoundType, this.Active)?.Kind == BoundTypeKind.Function)
                {
                    // SPEC 7.6.4: a Function Item converted to its fixed common Function Type is a new owned value without an
                    // environment; the item itself is not a Place.
                    var item = this.Temporary(node);
                    this.SetValue(this.Value(item), OwnershipValueKind.ClosureErasure, []);
                    return item;
                }

                return this.Use(node, this.Local(node), use, acquisition);
            case StringLiteralKoto or NumberLiteralKoto or BoolLiteralKoto or CharLiteralKoto or UnitLiteralKoto or NullLiteralKoto:
                return this.Temporary(node);
            case TupleLiteralKoto tuple when tuple.Elements.Count == 0:
                return this.Temporary(node);
            case TupleLiteralKoto tuple:
                return this.ConstructAggregate(tuple, tuple.Elements);
            case ArrayLiteralKoto array when array.BoundType?.Kind == BoundTypeKind.FixedArray:
                return this.ConstructAggregate(array, array.Elements);
            case ArrayLiteralKoto array when array.BoundType?.Kind == BoundTypeKind.Array:
                // SPEC 4.3, 4.7.4: an Array literal acquires its elements as payloads that construction moves into the buffer.
                return this.ConstructAggregate(array, array.Elements);
            case DictionaryLiteralKoto { BoundType.Kind: BoundTypeKind.Dictionary } dictionary:
                return this.ConstructDictionary(dictionary);
            case TupleTypeKoto { ElementNodes.Count: 0 }:
                return this.Temporary(node);
            case InvocationKoto call when ElementAccess.IsPlaceCall(call) && !this.referenceCalls.Remove(call):
                return this.ReadPlaceCall(call, acquisition); // SPEC 7.1.1: a value use of the published Place.
            case InvocationKoto call:
                return this.Call(call);
            case IsKoto { IsRuntimeTest: true } test:
                return this.RuntimeTypeTest(test);
            case ConversionKoto conversion:
                if (conversion.Adaptation is { } adaptation)
                {
                    var sourceType = this.Resolve(adaptation.Source, this.Active)!;
                    var targetType = this.Resolve(adaptation.Target, this.Active)!;
                    var operation = ExplicitAdaptationPlan.Select(sourceType, targetType, adaptation.IsShorthand);
                    if ((adaptation.Operations & (1U << (int)operation)) == 0)
                    {
                        this.Unsupported(conversion);
                        return -1;
                    }

                    if (operation == ConversionBinding.ObjectCreation)
                    {
                        var selected = this.compilation.Binding.ResolveObjectCreation(conversion, sourceType, targetType, this.body.NextResolvedCall());
                        return this.Call(conversion.CreationStorage!, selected: selected);
                    }

                    if (operation == ConversionBinding.Address)
                    {
                        var referenceType = this.Resolve(adaptation.AddressBorrow, this.Active)!;
                        var adaptedBorrow = this.BorrowStruct(conversion.Left, referenceType, address: true);
                        if (adaptedBorrow < 0)
                        {
                            return -1;
                        }

                        var adaptedAddress = this.Temporary(conversion);
                        this.SetValue(this.Value(adaptedAddress), OwnershipValueKind.Convert, [this.Value(adaptedBorrow)]);
                        return adaptedAddress;
                    }

                    if (operation == ConversionBinding.Borrow || (operation == ConversionBinding.ObjectUpcast && ObjectTypes.IsBorrow(targetType)))
                    {
                        return this.BorrowStruct(conversion.Left, targetType);
                    }

                    if (operation == ConversionBinding.ObjectUpcast)
                    {
                        var adaptedOwner = this.Expression(conversion.Left, PlaceUseKind.Read);
                        return this.Use(conversion, adaptedOwner, PlaceUseKind.Consume, AcquisitionKind.Move, targetType);
                    }

                    return this.ConversionValue(conversion, operation);
                }

                if (conversion.CreationCall is { } creation)
                {
                    return this.Call(creation);
                }

                if (conversion.ConversionBinding == ConversionBinding.ObjectUpcast)
                {
                    if (ObjectTypes.IsBorrow(conversion.BoundType))
                    {
                        return this.BorrowStruct(conversion.Left, conversion.BoundType!);
                    }

                    var owner = this.Expression(conversion.Left, PlaceUseKind.Read);
                    return this.Use(conversion, owner, PlaceUseKind.Consume, AcquisitionKind.Move, this.Resolve(conversion.BoundType, this.Active));
                }

                if (conversion.ConversionBinding == ConversionBinding.Borrow && ReferenceTypes.IsBorrow(conversion.BoundType))
                {
                    return this.BorrowStruct(conversion.Left, conversion.BoundType!);
                }

                if (conversion.ConversionBinding == ConversionBinding.Address)
                {
                    // SPEC 5.4: P@raw checks P as an immediately ending shared borrow and converts the borrowed address. The
                    // pointer carries no Origin, so the Loan ends with the borrow's only use.
                    var borrowed = this.BorrowStruct(conversion.Left, conversion.Right.BoundType!, address: true);
                    if (borrowed < 0)
                    {
                        return -1;
                    }

                    var address = this.Temporary(conversion);
                    this.SetValue(this.Value(address), OwnershipValueKind.Convert, [this.Value(borrowed)]);
                    return address;
                }

                if (use == PlaceUseKind.Consume && this.FollowsReference(conversion) && this.Resolve(conversion.BoundType, this.Active) is { } referent &&
                    this.compilation.Binding.ProveCopy(referent, conversion) != ConstraintProof.Proven)
                {
                    // SPEC 3.5: a selected referent is a Place, never moved by bare acquisition, and a reference offers no Take.
                    // The reference is still read and the value modeled, so the result is delivered and later uses are checked.
                    this.body.ReportIssue(new(conversion, OwnershipFailure.TransferRequired));
                    return this.StoredReference(conversion) < 0 ? -1 : this.Temporary(conversion);
                }

                if (this.ReadsStoredReference(conversion))
                {
                    var stored = this.StoredReference(conversion);
                    return stored < 0 ? -1 : this.LoadThrough(conversion.Left, stored, 1);
                }

                if (this.FollowsReference(conversion))
                {
                    // SPEC 13.5.5.1: a value use of a selected referent copies one proven-Copy layer.
                    return this.LoadReferent(conversion.Left, 1);
                }

                if (conversion.ConversionBinding == ConversionBinding.PayloadFollow)
                {
                    return this.ReadObjectPayload(conversion);
                }

                return this.ConversionValue(conversion);
            case BinaryKoto element when ElementAccess.IsSyntax(element) && ElementAccess.IsRawPlace(element):
                return this.ReadPointer(element, use, acquisition);
            case MemberAccessKoto member when member.Left.BoundType?.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Slice or BoundTypeKind.Array or BoundTypeKind.Dictionary || ReferenceTypes.IsArray(member.Left.BoundType) || ReferenceTypes.IsDynamicArray(member.Left.BoundType) || ReferenceTypes.IsDictionary(member.Left.BoundType) ||
                (member.Right is IdentifierNameKoto { IdentifierName: "length" } && (FormattingTypes.IsUtf8Slice(member.Left.BoundType) || FormattingTypes.IsSliceBorrow(member.Left.BoundType))) ||
                (member.Right is IdentifierNameKoto { IdentifierName: "length" or "isEmpty" or "indices" } && ReferenceTypes.IsSlice(member.Left.BoundType)):
                return this.SequenceMember(member);
            case MemberAccessKoto member when ReferenceTypes.IsStruct(member.Left.BoundType) || ReferenceTypes.IsTuple(member.Left.BoundType) || ObjectTypes.IsBorrow(member.Left.BoundType) ||
                ElementAccess.BorrowedPathRoot(member) is not null:
                return this.ReadBorrowedField(member);
            case IndexKoto element when ReferenceTypes.IsPointer(element.Left.BoundType):
                return this.ReadPointer(element, use, acquisition);
            case IndexKoto slice when ElementAccess.IsSlicing(slice):
                return this.CreateSlice(slice);
            case IndexKoto element when element.Left.BoundType?.Kind is BoundTypeKind.Slice or BoundTypeKind.Array:
                return this.ReadSlice(element, acquisition);
            case IndexKoto element when ReferenceTypes.IsArray(ElementAccess.AccessType(element.Left)) || ReferenceTypes.IsDynamicArray(element.Left.BoundType) || ReferenceTypes.IsDictionary(element.Left.BoundType):
                return this.ReadSlice(element, acquisition);
            case BinaryKoto element when ElementAccess.IsSyntax(element):
                return this.ElementValue(element, use, acquisition);
            case BinaryKoto binary:
                return this.Binary(binary);
            case IfKoto conditional:
                return this.Conditional(conditional);
            case MatchKoto match:
                return this.Match(match);
            case LabeledKoto labeled:
                return this.Expression(labeled.Target);
            case DoKoto scoped:
                return this.ScopedBody(scoped, scoped.Body);
            case LoopKoto repeat:
                return this.Repeat(repeat);
            case DiscardKoto discard:
                this.Expression(discard.Operand);
                return this.Temporary(node);
            case RequireKoto require:
                return this.Require(require);
            case TestVerificationKoto verification:
                return this.Verification(verification);
            case ForKoto iteration:
                this.Iterate(iteration);
                return this.Temporary(node);
            case WhileKoto loop:
                this.Loop(loop);
                if (!this.flow!.Nodes[loop].CanCompleteNormally)
                {
                    this.current = -1;
                    return -1;
                }

                return this.Temporary(node);
            case JumpKoto jump:
                return this.Jump(jump);
            case CodeBlockKoto block:
                var blockValue = this.Temporary(node, false);
                this.Block(block, blockValue);
                if (!this.flow!.Nodes[block].CanCompleteNormally)
                {
                    this.current = -1;
                    return -1;
                }

                if (!block.HasTrailingExpression || !KotoHelper.IsValueContext(block))
                {
                    this.Emit(OwnershipOperationKind.Produce, block, blockValue);
                }

                return this.RegisterTemporary(blockValue);
            case DereferenceKoto dereference:
                return this.ReadPointer(dereference, use, acquisition);
            case UnaryKoto { ArithmeticCall: { } arithmeticCall }:
                return this.Call(arithmeticCall);
            case UnaryKoto unary when node.Akind is KotoKind.Not or KotoKind.PrefixPlus or KotoKind.PrefixMinus or KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus or KotoKind.PostfixIncrement or KotoKind.PostfixDecrement:
                return this.UnaryValue(unary);
            default:
                this.Unsupported(node);
                return -1;
        }
    }

    private int Binary(BinaryKoto binary)
    {
        if (binary.ArithmeticCall is { } arithmeticCall)
        {
            return arithmeticCall.RightFirstArguments ? this.ArithmeticUpdate(binary, arithmeticCall) : this.Call(arithmeticCall);
        }

        if (binary.ComparisonCall is { } comparisonCall)
        {
            var compared = this.Call(comparisonCall);
            if (compared < 0)
            {
                return -1;
            }

            var comparisonResult = this.Temporary(binary);
            this.SetValue(this.Value(comparisonResult), OwnershipValueKind.ContractComparison, [this.Value(compared)], binary.Akind);
            return comparisonResult;
        }

        if ((ReferenceTypes.EndsInString(binary.Left.BoundType) || ReferenceTypes.EndsInString(binary.Right.BoundType)) && ReferenceEquals(binary.BoundType, BoundType.Boolean))
        {
            return this.StringComparison(binary);
        }

        var assignment = binary.Akind is >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals;
        if (assignment)
        {
            var target = KotoHelper.UnwrapParentheses(binary.Left);
            if (binary.Akind != KotoKind.Equals && this.compilation.Binding.PropertyUpdateStorage(target) is { } updateStorage)
            {
                return this.UpdateProperty(binary, target, updateStorage);
            }

            if (this.compilation.Binding.PropertyCall(target, PropertyAccessorKind.Set) is { } setter)
            {
                return this.Call(setter);
            }

            target = this.SelectedPlace(this.compilation.Binding.StorageProjection(target) ?? target);
            if (target is ConversionKoto followed && this.FollowsReference(followed))
            {
                return this.WriteReferent(binary, followed);
            }

            if (target is InvocationKoto placeCall && ElementAccess.IsPlaceCall(placeCall))
            {
                return this.WritePlaceCall(binary, placeCall); // SPEC 7.1.1: a write through a place uniq/T result.
            }

            if (target is IndexKoto userIndex && this.compilation.Binding.IndexerCall(userIndex, true) is { } exclusiveIndexer)
            {
                return this.WritePlaceCall(binary, exclusiveIndexer); // SPEC 4.6.9: an update selects indexUniq.
            }

            if (ElementAccess.IsRawPlace(target))
            {
                return this.WritePointer(binary, target);
            }

            if (target is MemberAccessKoto borrowedField && ElementAccess.BorrowedPathRoot(borrowedField) is not null)
            {
                return this.WriteBorrowedField(binary, borrowedField);
            }

            if (target is IndexKoto borrowedElement && ElementAccess.IsExclusiveArrayElement(borrowedElement))
            {
                return this.WriteBorrowedArrayElement(binary, borrowedElement);
            }

            if (target is BinaryKoto element && ElementAccess.IsSyntax(element) && !this.SpecialField(target))
            {
                return binary.Akind == KotoKind.Equals ? this.AssignElement(binary, element) : this.UpdateElement(binary, element);
            }

            // SPEC 13.7: simple and compound assignment secure the RHS before the target is located and read.
            var input = this.Expression(binary.Right);
            if (input < 0)
            {
                // Like a local initializer, an abrupt RHS has no value to place.
                return -1;
            }

            if (binary.Akind != KotoKind.Equals)
            {
                var op = KotoHelper.CompoundOperation(binary.Akind);
                var previous = this.Value(this.Expression(binary.Left, PlaceUseKind.Read));
                // SPEC 5.3: p += n and p -= n displace a pointer local like p + n and p - n.
                if (!this.SupportsUpdate(binary.Left, binary.Left.BoundType, op, pointer: true))
                {
                    this.Unsupported(binary);
                }

                input = this.ComputeUpdate(binary, binary.Left.BoundType, previous, this.Value(input), op);
            }

            this.Emit(OwnershipOperationKind.Write, binary, this.Local(target), input);
            return this.Temporary(binary);
        }

        if (binary is AndKoto or OrKoto)
        {
            var mark = this.terminalSeeds.Count;
            var completes = this.flow!.Nodes[binary].CanCompleteNormally;
            var output = this.ResultPlace(binary);
            var condition = this.Value(this.Expression(binary.Left, PlaceUseKind.Read));
            var branch = this.Emit(OwnershipOperationKind.Branch, binary.Left);
            if (condition >= 0)
            {
                this.SetValue(branch, OwnershipValueKind.Alias, [condition]);
            }

            var terminalRight = !this.flow.Nodes[binary.Right].CanCompleteNormally;
            var partialRight = completes && !terminalRight && this.body.CheckingRegions[this.checkingRegion].MixedTargets &&
                !(this.scopedCheckingProof ??= new(this)).Check(binary.Right, false) && this.scopedCheckingProof.Check(binary.Right);
            var fork = !completes || terminalRight || partialRight ? this.ForkChecking(branch) : null;
            var normalMark = this.normalCheckingSeeds.Count;
            var evaluate = this.New(OwnershipOperationKind.Branch, binary.Right);
            var skip = this.New(OwnershipOperationKind.Branch, binary);
            var join = this.ResultJoin(binary, output);
            var evaluateWhen = binary is AndKoto;
            this.Connect(branch, evaluate, evaluateWhen ? OwnershipEdgeKind.True : OwnershipEdgeKind.False);
            this.Connect(branch, skip, evaluateWhen ? OwnershipEdgeKind.False : OwnershipEdgeKind.True);

            this.EnterCheckingBranch(evaluate, fork);
            var region = this.checkingRegion;
            var right = this.Value(this.Expression(binary.Right, PlaceUseKind.Read));
            if (right >= 0)
            {
                var produced = this.Emit(OwnershipOperationKind.Produce, binary, output);
                this.SetValue(produced, OwnershipValueKind.Alias, [right]);
                this.ConnectResult(join, produced);
                if (partialRight && fork is not null)
                {
                    this.AddCheckingSeed(this.normalCheckingSeeds, this.Continuation());
                }
            }

            if (terminalRight)
            {
                this.AddTerminalSeed(this.Continuation());
            }
            else if (!completes)
            {
                // Partial RHS terminal paths are already pending; include its
                // normal tail as well before joining with the skipped path.
                this.AddTerminalSeed((this.scopedCheckingProof ??= new(this)).Check(binary.Right) ? this.Continuation() : new(-1));
            }

            this.checkingRegion = region;
            this.EnterCheckingBranch(skip, fork);
            var skipped = this.Emit(OwnershipOperationKind.Produce, binary, output);
            this.SetValue(skipped, OwnershipValueKind.Constant, [], constant: evaluateWhen ? 0 : 1);
            this.ConnectResult(join, skipped);
            if (partialRight && fork is not null)
            {
                this.AddCheckingSeed(this.normalCheckingSeeds, this.Continuation());
                this.JoinNormalChecking(binary, normalMark, join);
            }
            else if (fork is not null && completes)
            {
                // A terminal RHS contributes no normal result. Only the skipped
                // branch reaches this checking join; its target facts stay separate.
                this.body.OperationRegions[join] = this.checkingRegion;
            }

            if (!completes)
            {
                this.AddTerminalSeed(this.Continuation());
            }

            var completed = this.CompleteResult(binary, output, join);
            if (!completes)
            {
                this.JoinChecking(binary, mark);
            }
            else
            {
                this.FilterTerminalSeeds(binary, mark);
            }

            return completed;
        }

        var left = this.Expression(binary.Left, PlaceUseKind.Read);
        var leftValue = this.Value(left);
        var rightValue = this.Value(this.Expression(binary.Right, PlaceUseKind.Read));
        if (left >= 0 && this.body.PlaceStorage[left] is { Kind: not OwnershipPlaceKind.Temporary, Acquisition: not AcquisitionKind.Copy } &&
            KotoHelper.UnwrapParentheses(binary.Right) is not IdentifierNameKoto)
        {
            // Retaining a non-Copy operand view across effectful RHS evaluation needs a Loan.
            this.Unsupported(binary);
        }

        if (leftValue < 0 || rightValue < 0)
        {
            return -1; // Later source operands were checked, but no operator value arrived.
        }

        if (binary.Akind is KotoKind.Slash or KotoKind.Percent && ScalarTypes.Width(this.body.Places[left].Type, this.compilation.PointerWidth) == 128)
        {
            // SPEC 8.4.7.3, IMPL 21.5.3: an instance of a generic integer body diagnoses the profile's 128-bit division.
            this.Unsupported(binary, this.body.Places[left].Type);
            return -1;
        }

        var result = this.Temporary(binary);
        if (binary.Akind is KotoKind.EqualsEquals or KotoKind.ExclamationEquals && ReferenceEquals(this.body.Places[left].Type, BoundType.Unit))
        {
            // SPEC 13.4: Unit has one value, so after both operands are evaluated equality is constant.
            this.SetValue(this.Value(result), OwnershipValueKind.Constant, [], constant: binary.Akind == KotoKind.EqualsEquals ? 1 : 0);
            return result;
        }

        this.SetValue(this.Value(result), OwnershipValueKind.Binary, [leftValue, rightValue], binary.Akind);
        return result;
    }

    private int Conditional(IfKoto conditional)
    {
        var entry = this.current;
        var completes = this.flow!.Nodes[conditional].CanCompleteNormally;
        var forkChecking = this.body.CheckingRegions[this.checkingRegion].MixedTargets && this.CanForkCheckingBranches(conditional);
        var normalConditions = true;
        var normalMark = this.normalCheckingSeeds.Count;
        var caughtMark = this.caughtCheckingSeeds.Count;
        var mark = this.terminalSeeds.Count;
        var output = this.ResultPlace(conditional);
        var join = this.ResultJoin(conditional, output);
        this.selections.Add(new(conditional, output, join, this.locals.Count, this.temporaries.Count, this.comparisonDepth, forkChecking && completes));
        for (var i = 0; i < conditional.Branches.Count; i++)
        {
            var branch = conditional.Branches[i];
            var condition = this.Condition(branch.Condition);
            normalConditions &= this.flow.Nodes[branch.Condition].CanCompleteNormally;
            var test = this.Emit(OwnershipOperationKind.Branch, branch.Condition);
            if (condition >= 0)
            {
                this.SetValue(test, OwnershipValueKind.Alias, [condition]);
            }

            var fork = forkChecking ? this.ForkChecking(test) : null;
            var yes = this.New(OwnershipOperationKind.Branch, branch.Body);
            var no = this.New(OwnershipOperationKind.Branch, conditional);
            this.Connect(test, yes, OwnershipEdgeKind.True);
            this.Connect(test, no, OwnershipEdgeKind.False);

            this.EnterCheckingBranch(yes, fork);
            var result = this.Block(branch.Body, out var continuation, output, forkChecking);
            this.RecordTerminalSeed(branch.Body, continuation, completes && normalConditions);

            if (ReferenceEquals(this.body.Places[output].Type, BoundType.Unit))
            {
                this.Emit(OwnershipOperationKind.Produce, branch.Body, output);
            }

            this.ConnectResult(join, result);
            if (forkChecking && completes && normalConditions && this.flow.Nodes[branch.Body].CanCompleteNormally)
            {
                this.AddCheckingSeed(this.normalCheckingSeeds, this.Continuation());
            }

            this.EnterCheckingBranch(no, fork);
        }

        var otherwiseResult = -1;
        if (conditional.ElseBody is { } otherwise)
        {
            otherwiseResult = this.Block(otherwise, out var continuation, output, forkChecking);
            this.RecordTerminalSeed(otherwise, continuation, completes && normalConditions);

            if (ReferenceEquals(this.body.Places[output].Type, BoundType.Unit))
            {
                this.Emit(OwnershipOperationKind.Produce, otherwise, output);
            }
        }
        else
        {
            this.Emit(OwnershipOperationKind.Produce, conditional, output);
            if (!completes || !normalConditions)
            {
                this.AddTerminalSeed(this.Continuation());
            }
        }

        this.ConnectResult(join, otherwiseResult);
        if (forkChecking && completes)
        {
            if (normalConditions && (conditional.ElseBody is null || this.flow.Nodes[conditional.ElseBody].CanCompleteNormally))
            {
                this.AddCheckingSeed(this.normalCheckingSeeds, this.Continuation());
            }

            this.CollectCaughtChecking(conditional, caughtMark);
            this.JoinNormalChecking(conditional, normalMark, join);
        }
        else if (completes)
        {
            // A later terminal condition may have opened a checking region.
            // Earlier normal arrivals still belong to the original join's region.
            this.checkingRegion = this.body.OperationRegions[join];
        }

        this.current = join;
        this.selections.RemoveAt(this.selections.Count - 1);
        this.body.RecordCompletion(entry, join, completes);
        var completed = this.CompleteResult(conditional, output, join);
        if (!completes)
        {
            this.JoinChecking(conditional, mark);
        }
        else
        {
            this.FilterTerminalSeeds(conditional, mark);
        }

        return completed;
    }

    // A terminal branch of a completing selection stays pending until the enclosing
    // selection or scope joins every terminal path; a partial early transfer must
    // not be discarded by a later termination. Each seed keeps its target until
    // the construct handling that transfer removes it from the pending paths.
    private void RecordTerminalSeed(CodeBlockKoto block, CheckingContinuation continuation, bool completes = true)
    {
        if (!this.flow!.Nodes[block].CanCompleteNormally)
        {
            this.AddTerminalSeed(continuation);
        }
        else if (!completes)
        {
            // Nested terminal branches already retain their own histories. The
            // remaining normal tail joins them after this body's local cleanup.
            this.AddTerminalSeed((this.scopedCheckingProof ??= new(this)).Check(block) ? this.Continuation() : new(-1));
        }
    }

    private int Call(InvocationKoto call, int preparedInput = -1, int preparedReceiver = -1, BoundCall? selected = null, ReadOnlySpan<int> preparedArguments = default)
    {
        if (call.BoundValueCall is { } valueCall)
        {
            return this.CallValue(call, valueCall);
        }

        if ((selected ?? call.BoundCall) is not { } plan)
        {
            this.Unsupported(call);
            return -1;
        }

        // A selected case call already carries its default and case/instance context.
        if (selected is null && this.defaultContext >= 0)
        {
            if (this.body.ResolveCall(plan, this.Active) is not { } substituted)
            {
                this.Unsupported(call);
                return -1;
            }

            plan = substituted;
        }

        if (plan.Target.CompilerFunction is CompilerFunctionKind.Replace or CompilerFunctionKind.Exchange or CompilerFunctionKind.Swap)
        {
            return this.WholeValueUpdate(call, plan);
        }

        if (plan.Target.Declaration is FunctionKoto libraryBody && plan.Target.CompilerFunction == CompilerFunctionKind.None)
        {
            this.CollectLibraryBody(libraryBody);
            if (Parser.HasLibraryImport(libraryBody.AttributeChain))
            {
                this.usedImports.Add(libraryBody);
            }
        }

        for (var i = 0; i < plan.TypeArguments.Length; i++)
        {
            this.CollectLibraryWitnesses(plan.TypeArguments[i]);
        }

        for (var i = 0; plan.DeclaringType is { } declaring && i < declaring.Components.Count; i++)
        {
            this.CollectLibraryWitnesses(declaring.Components[i]);
        }

        var mark = this.arguments.Count;
        var loanDepth = this.comparisonDepth++;
        var reservationMark = this.body.CallReservations.Count;
        var borrows = false;
        if (plan.Receiver is { } receiver)
        {
            var prepared = this.PrepareCallArgument(call, receiver, plan.ReceiverOperation);
            borrows |= this.HasCallInspection(prepared);
            this.arguments.Add(prepared);
        }

        for (var i = 0; i < call.ArgumentNodes.Count; i++)
        {
            var argument = plan.ArgumentOperations[i];
            var accessor = (plan.Target.Declaration as FunctionKoto)?.Accessor;
            var prepared = !preparedArguments.IsEmpty ? preparedArguments[i]
                : accessor is not null && argument.ParameterIndex == 0 && accessor.Receiver is not null && preparedReceiver >= 0 ? preparedReceiver
                : accessor?.Kind == PropertyAccessorKind.Set && preparedInput >= 0 ? preparedInput
                : this.PrepareCallArgument(call, call.ArgumentNodes[i], argument);
            borrows |= this.HasCallInspection(prepared);
            this.arguments.Add(prepared);
        }

        this.PrepareDefaults(plan, mark);
        this.ActivateCallReservations(call, reservationMark);
        if (plan.Target.Declaration is FunctionKoto target && target.Parameters.Count != this.arguments.Count - mark)
        {
            this.Unsupported(call); // Every parameter must have an explicit or default acquisition.
        }

        var acquired = true;
        for (var i = mark; i < this.arguments.Count; i++)
        {
            if (this.arguments[i] >= 0)
            {
                var entry = this.Emit(OwnershipOperationKind.CallEntry, call, this.arguments[i]);
                var offset = i - mark - (plan.Receiver is null ? 0 : 1);
                var contract = offset < 0 ? plan.ReceiverOperation.ParameterType
                    : offset < plan.ArgumentOperations.Length ? plan.ArgumentOperations[offset].ParameterType
                    : plan.DefaultArguments[offset - plan.ArgumentOperations.Length].ParameterType;
                this.body.RecordCallInput(entry, this.Resolve(contract, this.Active));
            }
            else
            {
                acquired = false;
            }
        }

        this.arguments.RemoveRange(mark, this.arguments.Count - mark);
        var invoke = this.Emit(OwnershipOperationKind.Call, call);
        if (selected is not null)
        {
            this.body.RecordResolvedCall(invoke, plan);
        }

        this.Connect(invoke, this.abortExit, OwnershipEdgeKind.Abort);
        if (!acquired || ReferenceEquals(call.BoundType, BoundType.Never))
        {
            if (borrows)
            {
                this.body.CallLoans.Add(new(invoke, -1, -1, LoanRequirement.None));
            }

            this.current = -1;
            // Source after a nonreturning call is checked from the acquired argument
            // state, without adding a runtime continuation or a result initialization.
            this.BeginChecking(invoke);
            this.EndComparisonLoans(loanDepth, call);
            this.comparisonDepth = loanDepth;

            return -1;
        }

        // SPEC 7.1.1: a Place call returns the reference it publishes; the use selects the referent.
        var result = ElementAccess.IsPlaceCall(call) ? this.ReferenceTemporary(call, plan.ReturnType) : this.Temporary(call);
        var scalar = ScalarResult(this.body.Places[result].Type);
        if (scalar || SlotTypes.IsResult(this.body.Places[result].Type))
        {
            // A stored-result Call names storage; only its normal successor Produce initializes it.
            this.body.OperationStorage[invoke] = this.body.Operations[invoke] with { Place = result };
        }

        if (scalar)
        {
            this.SetValue(invoke, OwnershipValueKind.Call, []);
            this.SetValue(this.Value(result), OwnershipValueKind.Alias, [invoke]);
        }

        // Result Origins retain their source Loans independently of call-argument Loans.
        // Secure the result before ending the latter.
        var loanEnd = this.EndComparisonLoans(loanDepth, call);
        if (borrows)
        {
            this.body.CallLoans.Add(new(invoke, this.Value(result), loanEnd, ReferenceTypes.IndependentResult(plan.ReturnType) ? LoanRequirement.None : LoanRequirement.Ref));
        }

        if (this.instance is null && plan.Target.Declaration is FunctionKoto callee && plan.Target.CompilerFunction == CompilerFunctionKind.None)
        {
            this.RequirementEffects(call, plan, callee, invoke, result);
        }

        this.comparisonDepth = loanDepth;

        return result;
    }

    // SPEC 8.4.10.4: a call in a generic body may affect every Loan that its abstract inputs' Types may denote, in the inputs'
    // modes, and its result may keep those Loans. A requirement call's effects are bounded by the bounds available to it; an
    // ordinary generic function's are not, since its body is not part of the caller's contract. Concrete inputs keep their
    // Origins, which the ordinary Loans track. Each instance runs concrete code, so only the definition records these effects.
    private void RequirementEffects(InvocationKoto call, BoundCall plan, FunctionKoto requirement, int invoke, int result)
    {
        var receiver = plan.Receiver is { } syntax ? this.ValueIdentity(syntax) : new(-1, null);
        var effects = this.body.RequirementEffects ??= new();
        var mark = effects.Count;
        var preserves = false;
        var bounds = false;
        if (plan.Receiver is not null)
        {
            this.RequirementEffect(call, plan.ReceiverOperation.ParameterType, requirement, invoke, receiver, receiver, plan.ConformingType, ref bounds, ref preserves);
        }

        for (var i = 0; i < plan.ArgumentOperations.Length; i++)
        {
            var input = plan.ArgumentOperations[i].Source is { } source ? this.ValueIdentity(source) : new(-1, null);
            this.RequirementEffect(call, plan.ArgumentOperations[i].ParameterType, requirement, invoke, input, receiver, plan.ConformingType, ref bounds, ref preserves);
        }

        if (result < 0 || effects.Count == mark || !AbstractTypes.HasAbstractPart(plan.ReturnType) || ReferenceTypes.IndependentResult(plan.ReturnType))
        {
            return;
        }

        var results = this.body.RequirementResults ??= new();
        for (var i = mark; i < effects.Count; i++)
        {
            results.Add(new(invoke, result, effects[i].Region, effects[i].Mode, requirement, effects[i].Input, receiver, plan.RequirementContract));
        }
    }

    // The value an input expression designates: a local or parameter, or its first Field; unknown otherwise.
    private OwnershipValueIdentity ValueIdentity(Koto syntax)
    {
        BindingSymbol? field = null;
        var node = KotoHelper.UnwrapParentheses(syntax);
        while (node is MemberAccessKoto { BoundSymbol: { } member } access)
        {
            field = member;
            node = KotoHelper.UnwrapParentheses(access.Left);
        }

        return node is IdentifierNameKoto { BoundSymbol: { } symbol } && this.body.SymbolPlaces.TryGetValue(symbol, out var root) ? new(root, field) : new(-1, null);
    }

    private void RequirementEffect(InvocationKoto call, BoundType? parameter, object requirement, int invoke, OwnershipValueIdentity input, OwnershipValueIdentity receiver, BoundType? conforming, ref bool bounds, ref bool preserves)
    {
        var borrowed = parameter is { Kind: BoundTypeKind.Semantics, Semantics: not SemanticsKind.Owner, Components.Count: 1 };
        var region = borrowed ? parameter!.Components[0] : parameter;
        if (region is null || !AbstractTypes.IsAbstract(region))
        {
            return;
        }

        // An owned input is the callee's; through a borrow the call has the borrow's access. A projection formed over a shared
        // borrow of its root, such as Iterable's IteratorType, reaches every Loan of its Origins through that shared borrow.
        var mode = !borrowed || parameter!.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq ? LoanRequirement.Uniq : LoanRequirement.Ref;
        if (region.Kind == BoundTypeKind.AssociatedProjection && region.Symbol?.Declaration is { } declaration &&
            Binding.AssociatedFormationType(declaration) is { Semantics: SemanticsKind.Ref or SemanticsKind.ObjRef })
        {
            mode = LoanRequirement.Ref;
        }

        if (!bounds)
        {
            bounds = true;
            preserves = requirement is FunctionKoto { IsRequirement: true } function && this.compilation.Binding.AvailableEffectBounds(function, conforming, call).Preserves;
        }

        if (!this.effectRegions.TryGetValue(region, out var place))
        {
            // A region holds no value: it needs no Copy proof, storage or cleanup, and no operation initializes it.
            place = this.AddPlace(new(this.body.PlaceStorage.Count, call, region, OwnershipPlaceKind.EffectRegion, false, AcquisitionKind.None));
            this.effectRegions.Add(region, place);
        }

        this.body.RequirementEffects!.Add(new(invoke, place, mode, requirement, input, receiver, preserves, call.BoundCall?.RequirementContract));
    }

    // Type arguments reach library bodies and standard Property bridges through their verified witnesses.
    private void CollectLibraryWitnesses(BoundType? type)
    {
        if (type is null)
        {
            return;
        }

        var start = this.witnessScratch.Count;
        if (type.Symbol is { Declaration: StructKoto or EnumKoto } symbol &&
            this.witnessTypes.Add(symbol))
        {
            this.compilation.Binding.CollectWitnesses(symbol, this.witnessScratch);
        }

        // PLAN G32: a fixed array used as a Type argument reaches the Kimi fixed-array members through its entry conformances.
        if (type.Kind == BoundTypeKind.FixedArray && !this.fixedArrayWitnesses)
        {
            this.fixedArrayWitnesses = true;
            this.compilation.Binding.CollectFixedArrayWitnesses(this.witnessScratch);
        }

        // SPEC 4.6.2: an integer Type argument reaches the Kimi integer Position witness through its built-in conformance.
        if (type.IsInteger && !this.integerPositionWitness)
        {
            this.integerPositionWitness = true;
            this.compilation.Binding.CollectIntegerPositionWitness(this.witnessScratch);
        }

        // Dispatch through an associated Type reaches that Type's witnesses too (an entry's iterator and its `next`), so each
        // witness's result Type is collected like a Type argument. Nested collection appends past this range.
        var end = this.witnessScratch.Count;
        for (var i = start; i < end; i++)
        {
            this.CollectLibraryBody(this.witnessScratch[i]);
        }

        for (var i = start; i < end; i++)
        {
            this.CollectLibraryWitnesses(this.witnessScratch[i].BoundSymbol?.Type);
        }

        this.witnessScratch.RemoveRange(start, this.witnessScratch.Count - start);
        for (var i = 0; i < type.Components.Count; i++)
        {
            this.CollectLibraryWitnesses(type.Components[i]);
        }
    }

    private void CollectLibraryBody(FunctionKoto function)
    {
        if ((function.Body is not null || function.ExpressionBody is not null) &&
            (function.IsPropertyWitness || ReferenceEquals(function.CodeContext.Kotonoha, this.compilation.Library.Kotonoha)) && !this.libraryBodies.Contains(function))
        {
            this.libraryBodies.Add(function);
            if (function.GenericArguments.Count != 0 && function.BoundSymbol is { } original &&
                this.compilation.Binding.Specializations(original) is { } specializations)
            {
                // SPEC 8.8.3: an instance may select any specialization at emission.
                foreach (var specialization in specializations)
                {
                    this.CollectLibraryBody(specialization);
                }
            }
        }
    }

    private int Argument(Koto argument, ArgumentOperationKind kind, AcquisitionKind? acquisition = null)
    {
        if (kind is ArgumentOperationKind.Value or ArgumentOperationKind.CopyRead)
        {
            // SPEC 10.2: a Copy read acquires the referent as a fresh Copy temporary through the reference.
            // Binding may already have adapted the expression before committing this Value operation. Its acquisition
            // applies to the adapted value, not to the original reference slot (SPEC 10.2).
            var adapted = kind == ArgumentOperationKind.Value && this.compilation.Binding.TryGetAdaptation(argument, out _);
            var value = kind == ArgumentOperationKind.CopyRead ? this.LoadReferent(argument) : this.Expression(argument, acquisition: adapted ? null : acquisition);
            this.CheckAcquisition(value, acquisition);
            return value;
        }

        if (kind == ArgumentOperationKind.Reborrow && KotoHelper.UnwrapParentheses(argument) is ConversionKoto { ConversionBinding: ConversionBinding.Transfer })
        {
            // SPEC 10.2: a same-Type temporary, here a transferred reference, is transferred as is rather than Reborrowed.
            var transferred = this.Expression(argument, acquisition: acquisition);
            this.CheckAcquisition(transferred, acquisition);
            return transferred;
        }

        // A borrowed source keeps its value and responsibility; it never enters the callee.
        this.Expression(argument, PlaceUseKind.Borrow);
        this.Unsupported(argument);
        return -1;
    }

    private int Condition(Koto condition) => this.Condition(condition, out _);

    private int Condition(Koto condition, out int cleanupStart)
    {
        var mark = this.temporaries.Count;
        var value = this.Value(this.Expression(condition, PlaceUseKind.Read));
        cleanupStart = this.body.Operations.Count;
        this.Cleanup(mark, this.locals.Count, condition, CleanupReason.ExpressionEnd);
        this.temporaries.RemoveRange(mark, this.temporaries.Count - mark);
        return value;
    }

    private int Require(RequireKoto require)
    {
        var condition = this.Condition(require.Condition);
        var test = this.Emit(OwnershipOperationKind.Branch, require.Condition);
        if (condition >= 0)
        {
            this.SetValue(test, OwnershipValueKind.Alias, [condition]);
        }

        var fork = this.ForkChecking(test);
        var success = this.New(OwnershipOperationKind.Branch, require);
        var failure = this.New(OwnershipOperationKind.Branch, require.ElseBody);
        this.Connect(test, success, OwnershipEdgeKind.True);
        this.Connect(test, failure, OwnershipEdgeKind.False);
        this.EnterCheckingBranch(failure, fork);
        var region = this.checkingRegion;
        if (require.ElseBody is CodeBlockKoto block)
        {
            this.Block(block, out var continuation);
            this.AddTerminalSeed(continuation);
        }
        else
        {
            var temps = this.temporaries.Count;
            var locals = this.locals.Count;
            this.Statement(require.ElseBody);
            this.AddTerminalSeed(this.checkingRegion != region ? this.Continuation() : new(-1));
            this.Cleanup(temps, locals, require, CleanupReason.ScopeExit);
            this.temporaries.RemoveRange(temps, this.temporaries.Count - temps);
            this.locals.RemoveRange(locals, this.locals.Count - locals);
        }

        this.Connect(this.current, success);
        this.checkingRegion = region;
        this.EnterCheckingBranch(success, fork);
        return -1;
    }

    private int ScopedBody(Koto owner, CodeBlockKoto block)
    {
        var entry = this.current;
        var completes = this.flow!.Nodes[owner].CanCompleteNormally;
        var retainNormal = completes && this.body.CheckingRegions[this.checkingRegion].MixedTargets &&
            !(this.scopedCheckingProof ??= new(this)).Check(block, false) && this.scopedCheckingProof.Check(block);
        var mark = this.terminalSeeds.Count;
        var normalMark = this.normalCheckingSeeds.Count;
        var caughtMark = this.caughtCheckingSeeds.Count;
        var output = owner is DoKoto ? this.ResultPlace(owner) : -1;
        var join = this.ResultJoin(owner, output);
        this.selections.Add(new(owner, output, join, this.locals.Count, this.temporaries.Count, this.comparisonDepth, retainNormal));
        var result = this.Block(block, out var continuation, output, retainNormal);
        if (output >= 0 && ReferenceEquals(this.body.Places[output].Type, BoundType.Unit))
        {
            this.Emit(OwnershipOperationKind.Produce, owner, output);
        }

        this.ConnectResult(join, result);
        if (retainNormal)
        {
            if (this.flow.Nodes[block].CanCompleteNormally)
            {
                this.AddCheckingSeed(this.normalCheckingSeeds, this.Continuation());
            }

            this.CollectCaughtChecking(owner, caughtMark);
            this.JoinNormalChecking(owner, normalMark, join);
        }

        this.selections.RemoveAt(this.selections.Count - 1);
        this.body.RecordCompletion(entry, join, completes);
        var completed = this.CompleteResult(owner, output, join);
        if (!completes)
        {
            // A completing scope leaves its pending terminal paths to the enclosing join.
            if (continuation.Seed >= 0 && (this.scopedCheckingProof ??= new(this)).Check(block))
            {
                this.AddTerminalSeed(continuation);
                this.JoinChecking(owner, mark);
            }

            this.terminalSeeds.RemoveRange(mark, this.terminalSeeds.Count - mark);
        }
        else
        {
            this.RecordTerminalSeed(block, continuation);
            this.FilterTerminalSeeds(owner, mark);
        }

        return completed;
    }

    private int Repeat(LoopKoto loop)
    {
        var output = this.ResultPlace(loop);
        var head = this.Emit(OwnershipOperationKind.Branch, loop);
        var exit = this.ResultJoin(loop, output);
        var fork = this.CanForkTerminalLoop(loop, loop.Body) ? this.ForkChecking(head) : null;
        var normalMark = this.normalCheckingSeeds.Count;
        var caughtMark = this.caughtCheckingSeeds.Count;
        if (fork is not null)
        {
            var enter = this.New(OwnershipOperationKind.Branch, loop.Body);
            this.Connect(head, enter);
            this.EnterCheckingBranch(enter, fork);
        }

        this.loops.Add(new(loop, head, exit, this.locals.Count, this.temporaries.Count, output, this.comparisonDepth, fork is not null));
        var mark = this.terminalSeeds.Count;
        this.Block(loop.Body, out var continuation);
        this.RecordTerminalSeed(loop.Body, continuation);
        this.FilterTerminalSeeds(loop, mark);
        if (fork is null)
        {
            this.Connect(this.current, head, OwnershipEdgeKind.Back);
        }
        else
        {
            // Every normal arrival is an explicit, post-cleanup exit. Return
            // histories remain pending at their original enclosing extent.
            this.CollectCaughtChecking(loop, caughtMark);
            this.JoinNormalChecking(loop, normalMark, exit);
        }

        this.loops.RemoveAt(this.loops.Count - 1);
        this.body.RecordCompletion(head, exit, this.flow!.Nodes[loop].CanCompleteNormally);
        var result = this.CompleteResult(loop, output, exit);
        if (!this.flow.Nodes[loop].CanCompleteNormally && this.IsStateNeutralDivergence(loop))
        {
            // These loops cannot change an entry fact. General divergent bodies
            // need a join of their effects; using their entry would restore Moves.
            this.BeginChecking(head);
            this.terminalSeeds.RemoveRange(mark, this.terminalSeeds.Count - mark); // The proof covers loop-internal transfers.
        }

        return result;
    }

    private bool IsStateNeutralDivergence(Koto source)
    {
        while (true)
        {
            source = KotoHelper.UnwrapParentheses(source);
            if (source is LabeledKoto labeled)
            {
                source = labeled.Target;
            }
            else if (source is DoKoto { Body.Items.Count: 1 } scoped)
            {
                source = scoped.Body.Items[0];
            }
            else if (source is LoopKoto loop)
            {
                if (loop.Body.Items.Count == 1)
                {
                    var item = KotoHelper.UnwrapParentheses(loop.Body.Items[0]);
                    if (item is UnitLiteralKoto or TupleLiteralKoto { Elements.Count: 0 } or TupleTypeKoto { ElementNodes.Count: 0 } ||
                        (item is ContinueKoto { Expression: null } again && ReferenceEquals(this.flow!.Targets.GetValueOrDefault(again), loop)))
                    {
                        return true;
                    }
                }

                return (this.localLoopProof ??= new(this)).Check(loop);
            }
            else
            {
                return false;
            }
        }
    }

    private void Loop(WhileKoto loop)
    {
        var head = this.Emit(OwnershipOperationKind.Branch, loop);
        var exit = this.New(OwnershipOperationKind.Branch, loop);
        var mark = this.temporaries.Count;
        var condition = this.Value(this.Expression(loop.Condition, PlaceUseKind.Read));
        this.Cleanup(mark, this.locals.Count, loop.Condition, CleanupReason.ExpressionEnd);
        this.temporaries.RemoveRange(mark, this.temporaries.Count - mark);
        var continuation = this.CheckingSeed();
        var test = this.Emit(OwnershipOperationKind.Branch, loop.Condition);
        if (condition >= 0)
        {
            this.SetValue(test, OwnershipValueKind.Alias, [condition]);
        }

        // A terminal body has no ordinary backedge. Keep its checking histories
        // separate from zero iterations, including exits caught by this while.
        var fork = this.CanForkTerminalLoop(loop, loop.Body) ? this.ForkChecking(test) : null;
        var normalMark = this.normalCheckingSeeds.Count;
        var caughtMark = this.caughtCheckingSeeds.Count;
        var enter = this.New(OwnershipOperationKind.Branch, loop.Body);
        var skipped = fork is not null ? this.New(OwnershipOperationKind.Branch, loop) : exit;
        this.Connect(test, enter, OwnershipEdgeKind.True);
        this.Connect(test, skipped, OwnershipEdgeKind.False);

        this.loops.Add(new(loop, head, exit, this.locals.Count, this.temporaries.Count, Comparisons: this.comparisonDepth, Checking: fork is not null));
        this.EnterCheckingBranch(enter, fork);
        var seedMark = this.terminalSeeds.Count;
        this.Block(loop.Body, out var bodyContinuation);
        this.RecordTerminalSeed(loop.Body, bodyContinuation);
        this.FilterTerminalSeeds(loop, seedMark);
        if (fork is null)
        {
            this.Connect(this.current, head, OwnershipEdgeKind.Back);
        }
        else
        {
            this.EnterCheckingBranch(skipped, fork);
            this.AddCheckingSeed(this.normalCheckingSeeds, this.Continuation());
            this.Connect(this.current, exit);
            this.CollectCaughtChecking(loop, caughtMark);
            this.JoinNormalChecking(loop, normalMark, exit);
        }

        this.loops.RemoveAt(this.loops.Count - 1);
        this.current = exit;
        if (!this.flow!.Nodes[loop.Condition].CanCompleteNormally && continuation >= 0 &&
            (this.localLoopProof ??= new(this)).Check(loop, loop.Body))
        {
            // The condition's acquisitions are already reflected in this seed.
            // Only body-local effects may be omitted from the enclosing state.
            this.BeginChecking(continuation);
            this.current = -1;
            this.terminalSeeds.RemoveRange(seedMark, this.terminalSeeds.Count - seedMark); // The body proof covers its transfers.
        }
    }

    private int Jump(JumpKoto jump)
    {
        var reported = this.body.IssueStorage.Count;
        var value = jump.Expression is { } expression ? this.Expression(expression) : -1;
        // The seed includes operand acquisition, but never this transfer's cleanup.
        // Consecutive bare transfers can reuse an as-yet unused seed: no source
        // operation changed its state. Other missing origins remain unsupported.
        var seed = this.CheckingSeed();
        var target = this.flow!.Targets.GetValueOrDefault(jump);
        if (target is not null && ReferenceEquals(target, this.body.Function.Accessor?.Declaration))
        {
            target = this.body.Function;
        }

        // The construct the transfer leaves to: the function, an enclosing selection or an enclosing loop of this execution.
        var returns = jump is ReturnKoto && this.deferredDepth == 0 && ReferenceEquals(target, this.body.Function);
        var selected = this.TryGetSelection(target, out var selection) && !returns;
        var loop = returns || selected ? -1 : this.EnclosingLoop(target);
        var loanDepth = returns ? 0 : selected ? selection.Comparisons : loop >= 0 ? this.loops[loop].Comparisons : this.comparisonDepth;
        var beforeEnd = this.current;
        this.EndComparisonLoans(loanDepth, jump);
        if (this.current != beforeEnd)
        {
            seed = this.current; // Ownership state is unchanged; abandoned Loans stay ended in checking code.
        }

        var continuationRegion = this.body.CheckingRegions[this.checkingRegion];
        Koto? caughtTarget = null;
        if (returns)
        {
            var secured = this.WriteResult(jump, this.resultPlace, value, jump.Expression is null ? -1 : reported);
            this.CheckConstruction(jump);
            this.Cleanup(0, 0, jump, CleanupReason.Return);
            this.Deliver(jump, secured);
            this.Connect(this.current, this.normalExit, OwnershipEdgeKind.Return);
        }
        else if (selected && jump is YieldKoto or ExitKoto)
        {
            var result = selection.Result >= 0 ? this.WriteResult(jump, selection.Result, value) : -1;

            this.Cleanup(selection.Temporaries, selection.Locals, jump, CleanupReason.SelectionResult);
            if (selection.Checking && this.flow.ReachesTarget(jump))
            {
                this.RecordCaughtChecking(selection.Source);
                caughtTarget = selection.Source;
            }

            this.ConnectResult(selection.Join, result);
        }
        else if (loop >= 0 && jump is ExitKoto or ContinueKoto)
        {
            var frame = this.loops[loop];
            var result = jump is ExitKoto && frame.Result >= 0 ? this.WriteResult(jump, frame.Result, value) : -1;

            this.Cleanup(frame.Temporaries, frame.Locals, jump, CleanupReason.LoopTransfer);
            if (jump is ContinueKoto)
            {
                this.Connect(this.current, frame.Head, OwnershipEdgeKind.Back);
            }
            else
            {
                if (frame.Checking && this.flow.ReachesTarget(jump))
                {
                    this.RecordCaughtChecking(frame.Source);
                    caughtTarget = frame.Source;
                }

                this.ConnectResult(frame.Exit, result);
            }
        }
        else
        {
            this.Unsupported(jump);
        }

        this.current = -1;
        this.BeginChecking(seed, ReferenceEquals(target, this.body.Function) ? null : target, continuationRegion, caughtTarget);
        return -1;
    }

    // The innermost loop of this execution that the target names, or -1; a deferred body does not see the loops around it.
    private int EnclosingLoop(Koto? target)
    {
        for (var i = this.loops.Count - 1; i >= this.deferredLoopBase; i--)
        {
            if (ReferenceEquals(this.loops[i].Source, target))
            {
                return i;
            }
        }

        return -1;
    }

    private void Cleanup(int tempStart, int localStart, Koto source, CleanupReason reason)
    {
        var start = this.body.CleanupStepStorage.Count;
        // Merge lexical registrations without allocating or removing live outer entries.
        // Separate marks let ExpressionEnd clean temporaries without ending a local's lifetime.
        var temporary = this.temporaries.Count - 1;
        var local = this.locals.Count - 1;
        while (temporary >= tempStart || local >= localStart)
        {
            var registration = temporary >= tempStart && (local < localStart || this.temporaries[temporary].Sequence > this.locals[local].Sequence)
                ? this.temporaries[temporary--] : this.locals[local--];
            if (registration.Source is DeferredBlockKoto deferred)
            {
                this.FinishCleanupSegment(start, reason);
                this.ExecuteDeferred(deferred);
                start = this.body.CleanupStepStorage.Count;
            }
            else if (registration.IsSubject)
            {
                this.CleanupSubject(registration.Place, source);
            }
            else
            {
                this.CleanupPlace(registration.Place, registration.Source, source);
            }
        }

        this.FinishCleanupSegment(start, reason);
    }

    private void FinishCleanupSegment(int start, CleanupReason reason)
    {
        var count = this.body.CleanupStepStorage.Count - start;
        if (count != 0)
        {
            var operation = this.body.CleanupStepStorage[start].Operation;
            this.body.CleanupPlanStorage.Add(new(this.body.IncomingEdges[operation], start, count, reason));
        }
    }

    private void CleanupPlace(int place, Koto declaration, Koto source)
    {
        var operation = this.Emit(OwnershipOperationKind.Cleanup, source, place);
        this.body.OperationSteps[operation] = this.body.CleanupStepStorage.Count;
        this.body.CleanupStepStorage.Add(new(operation, place, declaration, place < 0 ? CleanupAction.Unsupported : CleanupAction.Skip));
    }

    private int New(OwnershipOperationKind kind, Koto source, int place = -1, int input = -1, AcquisitionKind acquisition = AcquisitionKind.None, LoanRequirement loanMode = LoanRequirement.None, int projection = -1, int reservation = -1)
    {
        var id = this.body.OperationStorage.Count;
        if (id >= DeferredOperationLimit && this.body.DeferredPlans.Count != 0)
        {
            throw new DeferredExpansionLimitException(source);
        }

        this.body.OperationStorage.Add(new(kind, source, place, input, acquisition, LoanMode: loanMode, Projection: projection, Reservation: reservation));
        this.RecordComparisonState(source);
        this.resultHeads.Add(-2);
        this.RecordValue(id, kind, source, place, input);
        this.body.EdgeHeads.Add(-1);
        this.body.IncomingEdges.Add(-1);
        this.body.OperationSteps.Add(-1);
        this.body.OperationRegions.Add(this.checkingRegion);
        return id;
    }

    private int Emit(OwnershipOperationKind kind, Koto source, int place = -1, int input = -1, AcquisitionKind acquisition = AcquisitionKind.None, LoanRequirement loanMode = LoanRequirement.None, int projection = -1, int reservation = -1)
    {
        var id = this.New(kind, source, place, input, acquisition, loanMode, projection, reservation);
        if (this.checkingRegion > 0 && this.body.CheckingRegions[this.checkingRegion].Entry < 0)
        {
            var region = this.body.CheckingRegions[this.checkingRegion];
            this.body.CheckingRegions[this.checkingRegion] = region with { Entry = id };
        }

        this.Connect(this.current, id);
        this.current = id;
        return id;
    }

    private int Connect(int from, int to, OwnershipEdgeKind kind = OwnershipEdgeKind.Normal)
    {
        if (from < 0)
        {
            return -1;
        }

        this.body.EdgeStorage.Add(new(from, to, kind, this.body.EdgeHeads[from]));
        this.body.EdgeHeads[from] = this.body.EdgeStorage.Count - 1;
        this.body.IncomingEdges[to] = this.body.EdgeStorage.Count - 1;
        return this.body.EdgeStorage.Count - 1;
    }

    private void Unsupported(Koto source, BoundType? operationType = null)
    {
        this.Emit(OwnershipOperationKind.Unsupported, source);
        this.body.ReportIssue(new(source, OwnershipFailure.Unsupported, OperationType: operationType));
    }

    private readonly record struct Registration(int Place, Koto Source, int Sequence, bool IsSubject = false);

    private readonly record struct LoopFrame(Koto Source, int Head, int Exit, int Locals, int Temporaries, int Result = -1, int Comparisons = 0, bool Checking = false);

    private sealed class Collector : KotoVisitor
    {
        private readonly OwnershipAnalysis owner;

        internal bool DefaultsOnly { get; set; }

        internal Collector(OwnershipAnalysis owner)
        {
            this.owner = owner;
        }

        public override void Visit(Koto node)
        {
            if (node is FunctionKoto && TestDefinition.Marker(node) is not null && !TestDefinition.IsIncluded(node))
            {
                return;
            }

            if (this.DefaultsOnly)
            {
                if (node is FunctionKoto declaration)
                {
                    this.owner.CheckDefaultDeclarations(declaration);
                }

                node.VisitChildren(this);
                return;
            }

            if (node is FunctionKoto function)
            {
                this.owner.CheckDefaultDeclarations(function);
                // Requirement declarations and foreign imports have no body to verify; a body missing because of a syntax Error,
                // and the body of a declaration that is itself a recovery of one, are explained by that Error (SPEC 23.3.6.4).
                if (function.CodeContext.RecoveryCause(function) is null &&
                    (function.Body is not null || function.ExpressionBody is not null || !(function.IsRequirement || function.MissingBody || Parser.HasLibraryImport(function.AttributeChain))))
                {
                    this.owner.Build(function);
                }
            }
            else if (node is PropertyKoto propertySyntax && StaticScalar.IsDynamic(propertySyntax.BoundSymbol?.Property))
            {
                var initializer = StaticScalar.Initializer(propertySyntax.BoundSymbol!.Property!);
                this.owner.flow!.Append(initializer);
                this.owner.Build(initializer);
            }
            else if (node is PropertyAccessorKoto { Body: not null } syntax)
            {
                var property = ((PropertyKoto)syntax.Parent!).BoundSymbol!.Property!;
                var accessor = syntax.AccessorKind == PropertyAccessorKind.Get ? property.Getter : property.Setter;
                if (accessor.Result is null || accessor.Input is { CarriesOrigin: true })
                {
                    this.owner.issues.Add(new(node, OwnershipFailure.Unsupported));
                }
                else
                {
                    var executable = this.owner.compilation.Binding.AccessorFunction(accessor);
                    this.owner.flow!.Append(executable);
                    this.owner.Build(executable);
                }
            }

            node.VisitChildren(this);
        }
    }
}
