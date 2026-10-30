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
    private readonly HashSet<BindingSymbol> witnessTypes = new(ReferenceEqualityComparer.Instance);
    private readonly List<FunctionKoto> witnessScratch = new();
    private readonly Collector collector;
    private readonly List<Registration> locals = new();
    private readonly List<Registration> temporaries = new();
    private readonly List<LoopFrame> loops = new();
    private readonly List<int> arguments = new();
    private readonly HashSet<InvocationKoto> referenceCalls = new(ReferenceEqualityComparer.Instance);

    // The located Unsupported_Kd at which this analysis stops (Stop, Stopped).
    [Transitional("R6-check", Reason = "The old analysis's stops (DECISIONS R4-M152, R4-M159) go with the old analysis.")]
    private readonly List<OwnershipIssue> stops = new();
    private ControlFlowAnalysis? flow;
    private OwnershipBody body = null!;
    private bool fixedArrayWitnesses;
    private bool earlierRejected;
    private bool integerPositionWitness;
    private int current;
    private int resultPlace;
    private int normalExit;
    private int abortExit;
    private int registrationSequence;
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
        this.visitingTypes.Clear();
        var root = this.compilation.Kotonoha.RootKoto;
        if (this.flow is null)
        {
            this.flow = ControlFlowAnalysis.Analyze(root);
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

        this.GateReceiverDependentCalls(binding);
        if (this.stops.Count != 0)
        {
            return this.Stopped();
        }

        if (this.ReportUnprovenOriginObligations())
        {
            // An unchecked Origin obligation that is not a relation rejects the program with a diagnostic at its use, never silently.
            return this.Result = new(false, 0, this.issues.Count);
        }

        // Control flow, a HIR lint or an Origin relation already rejected the program, so its unreachable uses add no Unsupported.
        this.earlierRejected = this.flow.Issues.Count != 0 || binding.LintErrors != 0 || this.issues.Count != 0;
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
        if (this.stops.Count != 0)
        {
            return this.Stopped();
        }

        var verified = this.flow.Issues.Count == 0 && binding.LintErrors == 0 && this.flow.PendingBinding.Count == 0 && this.issues.Count == 0;
        if (!verified)
        {
            // A caller cannot certify a program containing an unchecked callee or flow contract.
            for (var i = 0; i < this.bodies.Count; i++)
            {
                this.bodies[i].IsVerified = false;
            }
        }

        return this.Result = new(verified, this.bodies.Count, this.issues.Count);
    }

    public void ReportDiagnostics() => this.ReportDiagnostics(this.issues);

    internal void Invalidate()
    {
        this.cleanupAnswers.Clear();
        this.completeTuples.Clear();
        this.Result = default;
        for (var i = 0; i < this.bodies.Count; i++)
        {
            this.bodies[i].IsVerified = false;
        }

        this.bodies.Clear();
        this.libraryBodies.Clear();
        this.templateBodies.Clear();
        this.witnessTypes.Clear();
        this.fixedArrayWitnesses = false;
        this.integerPositionWitness = false;
        if (this.defaultBody is { } declaration)
        {
            declaration.IsVerified = false;
        }

        this.issues.Clear();
        this.stops.Clear();
        this.invalidDefaults.Clear();
        this.checkedDefaults.Clear();
        this.candidates.Clear();
    }

    // SPEC 15.8.2 (DECISIONS R4-M126 §2.13, R4-M152; the owner's D5 exception, OQ-F7-1 (vi)): Binding binds a value call whose result
    // depends on its call receiver, which this analysis cannot judge, so a program with one stops at each such call (Stop), the call's
    // current plan deciding (F4), before any body is analyzed, as R1's Binding failure left it.
    [Transitional("R6-check", Reason = "The old analysis's gate goes with the old analysis.")]
    private void GateReceiverDependentCalls(Binding binding)
    {
        foreach (var call in binding.ReceiverDependentCalls)
        {
            if (call.BoundValueCall is { ReceiverDependentResult: true })
            {
                this.Stop(call);
            }
        }
    }

    // The old analysis's one stop (the owner's D5 exceptions until R6-check, which deletes this analysis): a program with a form this
    // analysis cannot judge gets the located Unsupported_Kd at each such form and nothing else of it is reported, as if nothing else were
    // analyzed: R1's receiver-dependent calls (the gate above, R4-M126 (vi)) and every Type SupportsType refuses (the owner's (a) of
    // 2026-10-31, 「すべて推奨の内容でお願いします」, DECISIONS R4-M159: a Type it does not model, such as a Closure Type with Origin
    // arguments, would otherwise reach its Loan analysis).
    [Transitional("R6-check", Reason = "The old analysis's stops (DECISIONS R4-M152, R4-M159) go with the old analysis.")]
    private void Stop(Koto at)
    {
        foreach (var stop in this.stops)
        {
            if (ReferenceEquals(stop.Source, at))
            {
                return;
            }
        }

        this.stops.Add(new(at, OwnershipRequirement.Unsupported));
    }

    // The analysis result of a program that stopped: its stops' located Unsupported_Kd alone, no body verified.
    [Transitional("R6-check", Reason = "The old analysis's stops (DECISIONS R4-M152, R4-M159) go with the old analysis.")]
    private OwnershipResult Stopped()
    {
        this.issues.Clear();
        this.issues.AddRange(this.stops);
        for (var i = 0; i < this.bodies.Count; i++)
        {
            this.bodies[i].IsVerified = false;
        }

        return this.Result = new(false, 0, this.issues.Count);
    }

    private void ReportDiagnostics(IReadOnlyList<OwnershipIssue> reportedIssues)
    {
        // Ownership analysis runs only after complete Binding, so its checks never rest on a Binding failure. The result that a
        // Block body leaves undelivered where it falls through rests on the fallthrough that control flow reported there.
        var diagnostics = this.compilation.Diagnostics;
        diagnostics.Invalidate(DiagnosticPartition.Ownership);

        List<(Koto At, DiagnosticCode Code, object[] Evidence)>? chains = null;
        for (var i = 0; i < reportedIssues.Count; i++)
        {
            var issue = reportedIssues[i];
            var requirement = DiagnosticRequirement.Ownership(issue.Failure);
            if (issue.Failure == OwnershipRequirement.Unsupported)
            {
                // SPEC 23.1.1: a form outside the implemented subset is located only.
                issue.Source.Report(requirement, issue.Code, span: SignatureSpan(issue.Source));
                continue;
            }

            if (issue.Failure == OwnershipRequirement.StorageLimit)
            {
                var span = SignatureSpan(issue.Source) ?? issue.Source.Span;
                issue.Source.Report(requirement, issue.Code, issue.RequiredBytes, issue.LimitBytes, evidence: [issue.StorageTable], span: span);
                continue;
            }

            if (issue.Failure == OwnershipRequirement.DefaultArgumentMove)
            {
                ReportDefaultMove(issue);
                continue;
            }

            if (issue.Failure is OwnershipRequirement.DefaultArgumentAccess or OwnershipRequirement.DefaultArgumentBorrow)
            {
                var defaultCase = this.CaseFact(issue, out var singleDefaultCase);
                issue.Source.Report(
                    requirement,
                    issue.Code,
                    note: CaseNote(null, defaultCase, singleDefaultCase),
                    evidence: CaseEvidence(issue.Code, null, defaultCase),
                    related: this.WithCaseDeclarations(issue.Related is { } parameter ? [("declaration", parameter, "preceding prepared parameter")] : null, defaultCase));
                continue;
            }

            if (issue.Capture >= 0 && issue.Source is FunctionKoto { ClosureAsCreated: { } closure } capturing)
            {
                ReportCapture(issue, capturing, closure.Captures[issue.Capture].Source);
                continue;
            }

            if (issue.Failure == OwnershipRequirement.ComparisonLoanConflict && issue.Destroyed is not null)
            {
                ReportDestruction(issue);
                continue;
            }

            if (issue.Failure == OwnershipRequirement.UnprovenOrigin && issue.Obligation is { Kind: BindingObligationKind.OriginOutlives, Longer: { } longer, Shorter: { } shorter } obligation)
            {
                // SPEC 15.6.1: a fit's relation is reported at the value that supplies the longer end; a well-formedness relation
                // and a Type's clause substituted at a Type occurrence, a declared relation, at that occurrence.
                var fit = Binding.IsFitObligation(obligation);
                var at = fit && obligation.Use is VariableKoto { InitializerKoto: { } initializer } ? initializer : obligation.Use;
                var refuted = Binding.RefutesOriginRelation(longer, shorter, obligation.Body);
                var supplier = this.compilation.Binding.SuppliedAtUse(longer, obligation.Use);
                var (clause, wellFormed) = (obligation.Clause, obligation.WellFormed);
                var relation = new OriginRelationFact(at, longer, supplier, shorter, obligation.Equality, obligation.Type, refuted, clause, WellFormed: wellFormed);
                var (evidence, related) = Binding.OriginRelationFacts(relation, fit ? "fit" : obligation.Clause is not null ? "declared" : "wellFormed");
                if (ChainOrdinal(at, issue.Code, evidence) is { } chain)
                {
                    at.Report(requirement, issue.Code, evidence: evidence, related: related, condition: (ushort)(chain << 8));
                }

                continue;
            }

            if (issue.Failure == OwnershipRequirement.EffectBound && this.compilation.Binding.ReportEffectViolation(issue.Source, requirement, issue.Code))
            {
                continue; // SPEC 8.11.1: a destruction the bound excludes, reported at the violating effect.
            }

            if (issue.Failure == OwnershipRequirement.CallableEffectBound && this.compilation.Binding.ReportCallableEffectViolation(issue.Source, requirement))
            {
                continue;
            }

            if (issue.Failure == OwnershipRequirement.VirtualEffectBound && issue.Source is FunctionKoto implementation)
            {
                this.compilation.Binding.ReportVirtualEffectViolation(implementation, requirement);
                continue;
            }

            // The delivery at the normal end is the only use at the function itself apart from a constructor's field checks, whose
            // discarded body never falls through; a return delivers at its jump.
            if (issue.Failure == OwnershipRequirement.UninitializedUse && issue.Source is FunctionKoto function &&
                this.compilation.Binding.FallthroughKey(function) is { } fallthrough)
            {
                issue.Source.ReportDerived(requirement, [fallthrough]);
                continue;
            }

            // A use at the function itself, such as a constructor's completion, is shown at its signature, not its whole body. A bare
            // Place that needs @move offers the transfer as a repair candidate where its path does not refute Take (SPEC 23.1.3).
            var transfer = issue.Failure == OwnershipRequirement.TransferRequired;
            var judgment = transfer ? Binding.TakeJudgment(issue.Source) : AcquisitionJudgment.Refuted;
            var found = this.CaseFact(issue, out var single);
            issue.Source.Report(
                requirement,
                issue.Code,
                note: CaseNote(AcquisitionNote(issue), found, single),
                evidence: CaseEvidence(issue.Code, transfer ? issue.Source.ToString() : null, found),
                related: this.WithCaseDeclarations(RelatedLocations(issue), found),
                span: SignatureSpan(issue.Source),
                repairs: transfer && judgment != AcquisitionJudgment.Refuted ? Binding.TransferRepair(issue.Source, issue.Source, judgment) : null);
        }

        // SPEC 23.2.2: an unverified result without an Error in this or an earlier phase reports one fallback at its first
        // incomplete subject, a control-flow obligation still pending after complete Binding. It marks a missing report.
        if (this.compilation.Binding.Result.IsComplete && !this.Result.IsVerified && this.flow is { } flow &&
            !diagnostics.HasErrorsThrough(DiagnosticPartition.Ownership) && FirstPending(flow) is { } pending)
        {
            pending.ReportDerived(DiagnosticRequirement.ControlFlow, [DiagnosticKey.Unresolved]);
        }

        // SPEC 7.2.3: a default may Copy a preceding Copy value or inspect it through temporary shared access; it never moves the
        // prepared argument. A capture entry is located at the entry; no repair is offered, since the needed value is the author's.
        void ReportDefaultMove(in OwnershipIssue issue)
        {
            var entries = issue.Source is FunctionKoto { Captures: { } captured } && issue.Capture >= 0 && issue.Capture < captured.Length ? captured : null;
            var span = entries is not null ? entries[issue.Capture].Span : (SourceSpan?)null;
            issue.Source.Report(DiagnosticRequirement.Ownership(issue.Failure), issue.Code, span: span);
        }

        // SPEC 7.7.2: an omitted list never infers a Move, a borrow or a Reborrow, so a capture without Copy needs an entry. In a
        // default, no entry can take a preceding argument other than by Copy (SPEC 7.2.3).
        void ReportCapture(in OwnershipIssue issue, FunctionKoto function, BindingSymbol source)
        {
            var name = source.Name;
            var omitted = function.Captures is null;
            var prepared = omitted && DefaultParameters.InLaterDefault(function, source);
            var note = !omitted ? null : $"The omitted capture list captures {name} only by Copy; it never infers a Move, a borrow or a Reborrow" +
                (prepared ? ", and a default can neither move a preceding argument nor keep a borrow of it (SPEC 7.2.3)" : string.Empty);
            issue.Source.Report(DiagnosticRequirement.Ownership(issue.Failure), issue.Code, note: note, evidence: [name]);
        }

        // SPEC 15.6.2, 15.6.6, 16.2.2: a borrowed Place destroyed while a live value keeps its Loan is reported at the destruction,
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
            var found = this.CaseFact(issue, out var single);
            issue.Source.Report(
                DiagnosticRequirement.Ownership(issue.Failure),
                issue.Code,
                note: CaseNote(char.ToUpperInvariant(place[0]) + place[1..] + kept + transfer, found, single),
                evidence: CaseEvidence(issue.Code, null, found),
                related: this.WithCaseDeclarations(related, found),
                relatedSpans: spans);
        }

        // SPEC 8.15, 23.1.2: a problem found under some Semantics cases names them in its Note.
        static string? CaseNote(string? note, string? found, bool single)
            => found is null ? note : (note is null ? "Found" : note + "; found") + (single ? " under the Semantics case " : " under the Semantics cases ") + found + " (SPEC 8.15)";

        // The `case` fact closes the evidence alternative of the codes that carry it; a code without the alternative keeps its Note.
        static object?[]? CaseEvidence(DiagnosticCode code, object? target, string? found)
        {
            var withCase = found is not null && HasCaseEvidence(code);
            return target is null ? (withCase ? [found] : null) : withCase ? [target, found] : [target];
        }

        static string CaptureText(CaptureKoto entry)
            => (entry.IsMutable ? "var " : string.Empty) + entry.Name + (entry.Operation is { } operation ? "@" + operation : string.Empty);

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

    // SPEC 8.15: a definition whose scope has a resolved pair binder is verified once per Semantics case; any other body once.
    private void Build(FunctionKoto function, int declarationDefault = -1)
    {
        if (this.ResolveCases(function))
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
            this.body.ReportIssue(new(limit.SourceNode, OwnershipRequirement.ExpansionLimit));
            this.AppendIssues();
        }
        catch (OwnershipStorageLimitException limit)
        {
            this.body.IsVerified = false;
            this.body.ReportReservedElementWrites(completed: false);
            this.body.ReportIssue(new(function, OwnershipRequirement.StorageLimit, StorageTable: limit.Table, RequiredBytes: limit.RequiredBytes, LimitBytes: limit.LimitBytes));
            this.AppendIssues();
        }
    }

    private void BuildBody(FunctionKoto function, int declarationDefault)
    {
        if (declarationDefault >= 0)
        {
            // One reusable declaration scratch graph, never an executable function
            // body. Its diagnostics are retained before the next default reuses it.
            this.body = this.caseBody ?? (this.defaultBody ??= new());
        }
        else if (this.caseBody is { } caseBody)
        {
            this.body = caseBody; // A Semantics case other than the first (SPEC 8.15); never listed.
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

        this.body.Reset(function, this.compilation.Binding, this.cases.AsSpan(0, this.caseCount), this.caseBit);
        this.body.DefaultParameter = declarationDefault;
        this.defaultContext = -1;
        this.locals.Clear();
        this.temporaries.Clear();
        this.loops.Clear();
        this.arguments.Clear();
        this.referenceCalls.Clear();
        this.formattingPlaces.Clear();
        this.placeValues.Clear();
        this.resultHeads.Clear();
        this.resultJoins.Clear();
        this.pendingResults.Clear();
        this.selections.Clear();
        this.candidates.Clear();
        this.comparisonDepth = 0;
        this.activeDecompositions.Clear();
        this.patternStorageNeeded.Clear();
        this.registrationSequence = 0;
        this.deferredLoopBase = 0;
        this.deferredSelectionBase = 0;
        this.deferredDepth = 0;
        this.activeDeferred = -1;
        this.current = -1;
        this.Emit(OwnershipOperationKind.Entry, function);
        this.normalExit = this.New(OwnershipOperationKind.Exit, function);
        this.abortExit = this.New(OwnershipOperationKind.Exit, function);
        this.resultPlace = this.Place(function, declarationDefault >= 0 ? function.Parameters[declarationDefault].Type.BoundType : function.BoundSymbol?.Type ?? BoundType.Unit, OwnershipPlaceKind.Result, true);
        if (declarationDefault < 0 && function.Captures is { Length: > 0 } && function.ClosureAsCreated is null)
        {
            this.Internal(function);
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
            this.WriteResult(defaultValue, this.resultPlace, this.Expression(defaultValue), reported);
            this.Cleanup(0, parameterCount, defaultValue, CleanupReason.Return);
            this.Deliver(function);
            this.Connect(this.current, this.normalExit, OwnershipEdgeKind.Return);
            this.CompleteBody(function, declarationDefault);
            return;
        }

        if (function.ClosureAsCreated is { } closure)
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
                this.WriteResult(expression, this.resultPlace, value, reported);
            }
        }
        else
        {
            this.Internal(function);
        }

        this.CheckEnvironmentMoves(function);
        this.CheckConstruction(function);
        this.Cleanup(0, 0, function, CleanupReason.Return);
        this.Deliver(function);
        this.Connect(this.current, this.normalExit, OwnershipEdgeKind.Return);
        this.CompleteBody(function);
    }

    // SPEC 7.7.2, 7.7.3: Shared and Exclusive calls only borrow their captures, and the environment destroys them afterwards, so a
    // Move out of a capture belongs to a Consuming call; Binding classifies the receiver, and this check keeps that classification
    // honest.
    private void CheckEnvironmentMoves(FunctionKoto function)
    {
        if (function.ClosureAsCreated is not { EnvironmentType: not null, Receiver: not SemanticsKind.Owner } closure)
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
                    this.Internal(moved.Source);
                    break;
                }
            }
        }
    }

    private void CompleteBody(FunctionKoto function, int declarationDefault = -1)
    {
        this.body.Solve();
        this.FinalizeResults();
        this.body.PrepareCallReservations();
        this.body.VerifyBorrows();
        if (declarationDefault >= 0)
        {
            this.body.VerifyPreparedDefault(this.resultPlace);
        }

        this.body.VerifyCallReservations();
        this.body.ReportReservedElementWrites(completed: true);
        this.body.CheckUnreachable(this.earlierRejected);
        this.AppendIssues();
        this.body.IsVerified = this.body.IssueStorage.Count == 0 && function.BindingState == BindingState.Resolved;
    }

    private int Place(Koto source, BoundType? type, OwnershipPlaceKind kind, bool mutable, AcquisitionKind? plannedAcquisition = null)
    {
        var id = this.body.PlaceStorage.Count;
        // D5 exception (the owner's Q-O3; DECISIONS BU-24): a Place's Type states no raw Origin. Removed with the old analysis (R6-check).
        type = this.compilation.Binding.WithoutRawOrigins(this.Resolve(type, this.Active)) ?? BoundType.Unit;
        // Never has no value storage. Its result marker is only used on unreachable
        // delivery nodes; control-flow checking rejects any normal completion.
        var neverResult = kind == OwnershipPlaceKind.Result && ReferenceEquals(type, BoundType.Never);
        var invalidCopy = false;
        var acquisition = plannedAcquisition.GetValueOrDefault();
        // A case, an instance or a default's replica resolves a committed CopyOrMove to the exact effect of its substituted Type
        // (SPEC 8.15, 21.3.1); a fixed acquisition or an explicit transfer is never reselected.
        if (plannedAcquisition is null || acquisition == AcquisitionKind.CopyOrMove)
        {
            var proof = this.CopyProof(type, source, this.Active);
            invalidCopy = proof == ConstraintProof.Error;
            acquisition = this.ExactAcquisition(proof, type, source);
        }

        this.AddPlace(new(id, source, type, kind, mutable, acquisition)
        {
            DeferredExecution = kind is OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result ? this.activeDeferred : -1,
            DefaultContext = this.defaultContext,
            CleanupObservesBorrows = this.CleanupObservesBorrows(type, source),
        });
        var supported = neverResult || type.Kind == BoundTypeKind.Parameter || this.SupportsType(type);
        if (invalidCopy || !supported)
        {
            this.Unsupported(source);
        }

        if (!supported)
        {
            this.Stop(source); // The owner's (a) (DECISIONS R4-M159): the analysis stops where SupportsType refuses a Type.
        }

        return id;
    }

    // SPEC 3.5.2, 8.15 (PLAN G82): the Copy capability of a Type resolved in a context, proved in that context's environment: a
    // default's replica at its root invocation, the root context at the operation's own source. Primitive classification needs
    // no environment; Never is Copy (SPEC 3.1.3).
    private ConstraintProof CopyProof(BoundType type, Koto source, InterpretationContext context)
        => type.Kind == BoundTypeKind.Primitive
            ? (type.Name == "string" ? ConstraintProof.Refuted : ConstraintProof.Proven)
            : this.compilation.Binding.ProveCopy(type, this.body.ProofAnchor(context) ?? source);

    // SPEC 3.5, 8.15: the exact acquisition of an analyzed Type. An Unknown proof stays the finite conditional plan CopyOrMove only
    // while the Type keeps a free generic part of the analyzed body; a closed Type with an Unknown proof is an internal failure.
    private AcquisitionKind ExactAcquisition(ConstraintProof proof, BoundType type, Koto source)
    {
        if (proof == ConstraintProof.Unknown && !type.HasAbstractPart)
        {
            this.body.Invariant(false, source);
        }

        return proof == ConstraintProof.Proven ? AcquisitionKind.Copy : proof == ConstraintProof.Refuted ? AcquisitionKind.Move : AcquisitionKind.CopyOrMove;
    }

    // Every Place, including a region or an anchor that holds no value, has a row in the builder's per-Place tables.
    private int AddPlace(OwnershipPlace place)
    {
        this.body.PlaceStorage.Add(place);
        this.placeValues.Add(-1);
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
            // SPEC 15.1.5, 8.14: a bare exclusive reference is reborrowed in its own mode; @move transfers it. A pair Place
            // stores its case Type (SPEC 8.15), so the exclusive cases Reborrow here and the Copy cases Copy below.
            if (stored.Type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq, Components.Count: 1 })
            {
                return this.BorrowStruct(source, stored.Type);
            }

            // SPEC 3.5: a bare Place never Moves; a Non-Copy or Copy-unproven Place needs @move.
            if (stored.Acquisition != AcquisitionKind.Copy)
            {
                this.body.ReportIssue(new(source, OwnershipRequirement.TransferRequired));
            }
        }

        this.CheckAcquisition(place, acquisition);
        // A selected object upcast supplies its complete view Type while transferring the same handle responsibility.
        var value = this.Place(source, resultType ?? stored.Type, OwnershipPlaceKind.Temporary, true);
        this.Emit(OwnershipOperationKind.Consume, source, place, value, acquisition ?? stored.Acquisition);
        return this.RegisterTemporary(value);
    }

    private int Block(CodeBlockKoto block, int destination = -1)
    {
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

        this.Cleanup(this.temporaries.Count, mark, block, CleanupReason.ScopeExit);
        this.locals.RemoveRange(mark, this.locals.Count - mark);
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

            // A reference read also serves a receiver that is read for member selection (SPEC 3.4.3); a borrow
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
                (item.IsRequirement ? this.compilation.Binding.FunctionItemContext(node.BoundType) is null :
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
            // SPEC 13.5.5: the owner case selects the operand itself.
            return this.ExpressionCore(KotoHelper.UnwrapParentheses(pair.Left), use, acquisition);
        }

        if (node is EvaluatedKoto evaluated)
        {
            var operand = this.EvaluatedOperand(evaluated, out var projection);
            if (projection >= 0)
            {
                this.Internal(node); // An element projection is read only where the selection reads its receiver.
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
            return this.Expression(indexer, use, acquisition); // SPEC 4.7.1: receiver[key] through a user conformance reads the referent of its selected call's reference.
        }

        if (this.compilation.Binding.ViewRangeCall(node) is { } viewRange)
        {
            return this.Expression(viewRange, use, acquisition);
        }

        if (this.compilation.Binding.RangeValueCall(node) is { } rangeValue)
        {
            return this.Expression(rangeValue, use, acquisition); // SPEC 4.6.1, 4.6.2: prefix ^ and range syntax construct Kimi values.
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
            case FunctionKoto { ClosureAsCreated: { } } closure:
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
                    // SPEC 7.7.4: a Function Item converted to its fixed common Function Type is a new owned value without an
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
                // SPEC 4.3, 4.9.5: an Array literal acquires its elements as payloads that construction moves into the buffer.
                return this.ConstructAggregate(array, array.Elements);
            case DictionaryLiteralKoto { BoundType.Kind: BoundTypeKind.Dictionary } dictionary:
                return this.ConstructDictionary(dictionary);
            case TupleTypeKoto { ElementNodes.Count: 0 }:
                return this.Temporary(node);
            case InvocationKoto call when ElementAccess.IsIndexerCall(call) && !this.referenceCalls.Remove(call):
                return this.ReadIndexerElement(call, acquisition); // SPEC 4.7.1: a value use of the subscript's element.
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
                        this.Internal(conversion);
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
                        var adaptedBorrow = this.BorrowStruct(conversion.Left, referenceType);
                        if (adaptedBorrow < 0)
                        {
                            return -1;
                        }

                        var adaptedAddress = this.Temporary(conversion);
                        this.SetValue(this.Value(adaptedAddress), OwnershipValueKind.Convert, [this.Value(adaptedBorrow)]);
                        return adaptedAddress;
                    }

                    if (operation == ConversionBinding.Borrow || (operation == ConversionBinding.ObjectUpcast && targetType.IsObjectBorrow))
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
                    if (conversion.BoundType is { IsObjectBorrow: true })
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
                    var borrowed = this.BorrowStruct(conversion.Left, conversion.Right.BoundType!);
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
                    this.body.ReportIssue(new(conversion, OwnershipRequirement.TransferRequired));
                    return this.StoredReference(conversion) < 0 ? -1 : this.Temporary(conversion);
                }

                if (this.ReadsStoredReference(conversion))
                {
                    var stored = this.StoredReference(conversion);
                    return stored < 0 ? -1 : this.LoadThrough(conversion.Left, stored, 1);
                }

                if (this.FollowsReference(conversion))
                {
                    // SPEC 13.5.5: a value use of a selected referent copies one proven-Copy layer.
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
            case MemberAccessKoto member when ReferenceTypes.IsStruct(member.Left.BoundType) || ReferenceTypes.IsTuple(member.Left.BoundType) || member.Left.BoundType is { IsObjectBorrow: true } ||
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

            if (target is IndexKoto userIndex && this.compilation.Binding.IndexerCall(userIndex, true) is { } exclusiveIndexer)
            {
                return this.WriteIndexerElement(binary, exclusiveIndexer); // SPEC 4.7.1: an update selects indexUniq.
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

        if (binary.Akind is KotoKind.And or KotoKind.Or)
        {
            var output = this.ResultPlace(binary);
            var condition = this.Value(this.Expression(binary.Left, PlaceUseKind.Read));
            var branch = this.Emit(OwnershipOperationKind.Branch, binary.Left);
            if (condition >= 0)
            {
                this.SetValue(branch, OwnershipValueKind.Alias, [condition]);
            }

            var evaluate = this.New(OwnershipOperationKind.Branch, binary.Right);
            var skip = this.New(OwnershipOperationKind.Branch, binary);
            var join = this.ResultJoin(binary, output);
            var evaluateWhen = binary.Akind == KotoKind.And;
            this.Connect(branch, evaluate, evaluateWhen ? OwnershipEdgeKind.True : OwnershipEdgeKind.False);
            this.Connect(branch, skip, evaluateWhen ? OwnershipEdgeKind.False : OwnershipEdgeKind.True);

            this.current = evaluate;
            var right = this.Value(this.Expression(binary.Right, PlaceUseKind.Read));
            if (right >= 0)
            {
                var produced = this.Emit(OwnershipOperationKind.Produce, binary, output);
                this.SetValue(produced, OwnershipValueKind.Alias, [right]);
                this.ConnectResult(join, produced);
            }

            this.current = skip;
            var skipped = this.Emit(OwnershipOperationKind.Produce, binary, output);
            this.SetValue(skipped, OwnershipValueKind.Constant, [], constant: evaluateWhen ? 0 : 1);
            this.ConnectResult(join, skipped);
            return this.CompleteResult(binary, output, join);
        }

        var left = this.Expression(binary.Left, PlaceUseKind.Read);
        var leftValue = this.Value(left);
        var rightValue = this.Value(this.Expression(binary.Right, PlaceUseKind.Read));
        if (leftValue < 0 || rightValue < 0)
        {
            return -1; // Later source operands were checked, but no operator value arrived.
        }

        if (binary.Akind is KotoKind.Slash or KotoKind.Percent && ScalarTypes.Width(this.body.Places[left].Type, this.compilation.PointerWidth) == 128)
        {
            // SPEC 8.7.4, IMPL 21.3.3: an instance of a generic integer body diagnoses the profile's 128-bit division.
            this.Unsupported(binary);
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
        var output = this.ResultPlace(conditional);
        var join = this.ResultJoin(conditional, output);
        this.selections.Add(new(conditional, output, join, this.locals.Count, this.temporaries.Count, this.comparisonDepth));
        for (var i = 0; i < conditional.Branches.Count; i++)
        {
            var branch = conditional.Branches[i];
            var condition = this.Condition(branch.Condition);
            var test = this.Emit(OwnershipOperationKind.Branch, branch.Condition);
            if (condition >= 0)
            {
                this.SetValue(test, OwnershipValueKind.Alias, [condition]);
            }

            var yes = this.New(OwnershipOperationKind.Branch, branch.Body);
            var no = this.New(OwnershipOperationKind.Branch, conditional);
            this.Connect(test, yes, OwnershipEdgeKind.True);
            this.Connect(test, no, OwnershipEdgeKind.False);

            this.current = yes;
            var result = this.Block(branch.Body, output);
            if (ReferenceEquals(this.body.Places[output].Type, BoundType.Unit))
            {
                this.Emit(OwnershipOperationKind.Produce, branch.Body, output);
            }

            this.ConnectResult(join, result);
            this.current = no;
        }

        var otherwiseResult = -1;
        if (conditional.ElseBody is { } otherwise)
        {
            otherwiseResult = this.Block(otherwise, output);
            if (ReferenceEquals(this.body.Places[output].Type, BoundType.Unit))
            {
                this.Emit(OwnershipOperationKind.Produce, otherwise, output);
            }
        }
        else
        {
            this.Emit(OwnershipOperationKind.Produce, conditional, output);
        }

        this.ConnectResult(join, otherwiseResult);
        this.current = join;
        this.selections.RemoveAt(this.selections.Count - 1);
        this.body.RecordCompletion(entry, join, completes);
        return this.CompleteResult(conditional, output, join);
    }

    private int Call(InvocationKoto call, int preparedInput = -1, int preparedReceiver = -1, CallPlan? selected = null, ReadOnlySpan<int> preparedArguments = default)
    {
        if (call.BoundValueCall is { } valueCall)
        {
            return this.CallValue(call, valueCall);
        }

        if ((selected ?? call.BoundCall) is not { } plan)
        {
            this.Internal(call);
            return -1;
        }

        // A selected case call already carries its default and case/instance context.
        if (selected is null && this.defaultContext >= 0)
        {
            if (this.body.ResolveCall(plan, this.Active) is not { } substituted)
            {
                this.Internal(call);
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
        if (plan.Receiver is { } receiver)
        {
            this.arguments.Add(this.PrepareCallArgument(call, receiver, plan.ReceiverOperation));
        }

        for (var i = 0; i < call.ArgumentNodes.Count; i++)
        {
            var argument = plan.ArgumentOperations[i];
            var accessor = (plan.Target.Declaration as FunctionKoto)?.Accessor;
            var prepared = !preparedArguments.IsEmpty ? preparedArguments[i]
                : accessor is not null && argument.ParameterIndex == 0 && accessor.Receiver is not null && preparedReceiver >= 0 ? preparedReceiver
                : accessor?.Kind == PropertyAccessorKind.Set && preparedInput >= 0 ? preparedInput
                : this.PrepareCallArgument(call, call.ArgumentNodes[i], argument);
            this.arguments.Add(prepared);
        }

        this.PrepareDefaults(call, plan, mark);
        this.ActivateCallReservations(call, reservationMark);
        if (plan.Target.Declaration is FunctionKoto target && target.Parameters.Count != this.arguments.Count - mark)
        {
            this.Internal(call); // Every parameter must have an explicit or default acquisition.
        }

        var inputStart = this.body.CallInputCount;
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
                var parameter = offset < 0 ? plan.ReceiverOperation.ParameterIndex
                    : offset < plan.ArgumentOperations.Length ? plan.ArgumentOperations[offset].ParameterIndex
                    : plan.DefaultArguments[offset - plan.ArgumentOperations.Length].Parameter.Slot;
                this.body.RecordCallInput(entry, parameter, this.Resolve(contract, this.Active), offset >= plan.ArgumentOperations.Length);
            }
            else
            {
                acquired = false;
            }
        }

        this.arguments.RemoveRange(mark, this.arguments.Count - mark);
        var invoke = this.Emit(OwnershipOperationKind.Call, call);
        this.RecordCall(invoke, inputStart, plan);

        this.Connect(invoke, this.abortExit, OwnershipEdgeKind.Abort);
        if (!acquired || ReferenceEquals(call.BoundType, BoundType.Never))
        {
            this.current = -1;
            this.EndComparisonLoans(loanDepth, call);
            this.comparisonDepth = loanDepth;

            return -1;
        }

        // SPEC 7.1: a call produces its result Type, a returned reference included; only a subscript reads through it (SPEC 4.7.1).
        var result = this.Temporary(call);
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
        this.EndComparisonLoans(loanDepth, call);
        this.comparisonDepth = loanDepth;

        return result;
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

        // SPEC 4.6.1: an integer Type argument reaches the Kimi integer Position witness through its built-in conformance.
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
                // SPEC 8.13.3: an instance may select any specialization at emission.
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
        this.Internal(argument);
        return -1;
    }

    private int Condition(Koto condition)
    {
        var mark = this.temporaries.Count;
        var value = this.Value(this.Expression(condition, PlaceUseKind.Read));
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

        var success = this.New(OwnershipOperationKind.Branch, require);
        var failure = this.New(OwnershipOperationKind.Branch, require.ElseBody);
        this.Connect(test, success, OwnershipEdgeKind.True);
        this.Connect(test, failure, OwnershipEdgeKind.False);
        this.current = failure;
        if (require.ElseBody is CodeBlockKoto block)
        {
            this.Block(block);
        }
        else
        {
            var temps = this.temporaries.Count;
            var locals = this.locals.Count;
            this.Statement(require.ElseBody);
            this.Cleanup(temps, locals, require, CleanupReason.ScopeExit);
            this.temporaries.RemoveRange(temps, this.temporaries.Count - temps);
            this.locals.RemoveRange(locals, this.locals.Count - locals);
        }

        this.Connect(this.current, success);
        this.current = success;
        return -1;
    }

    private int ScopedBody(Koto owner, CodeBlockKoto block)
    {
        var entry = this.current;
        var completes = this.flow!.Nodes[owner].CanCompleteNormally;
        var output = owner is DoKoto ? this.ResultPlace(owner) : -1;
        var join = this.ResultJoin(owner, output);
        this.selections.Add(new(owner, output, join, this.locals.Count, this.temporaries.Count, this.comparisonDepth));
        var result = this.Block(block, output);
        if (output >= 0 && ReferenceEquals(this.body.Places[output].Type, BoundType.Unit))
        {
            this.Emit(OwnershipOperationKind.Produce, owner, output);
        }

        this.ConnectResult(join, result);
        this.selections.RemoveAt(this.selections.Count - 1);
        this.body.RecordCompletion(entry, join, completes);
        return this.CompleteResult(owner, output, join);
    }

    private int Repeat(LoopKoto loop)
    {
        var output = this.ResultPlace(loop);
        var head = this.Emit(OwnershipOperationKind.Branch, loop);
        var exit = this.ResultJoin(loop, output);
        this.loops.Add(new(loop, head, exit, this.locals.Count, this.temporaries.Count, output, this.comparisonDepth));
        this.Block(loop.Body);
        this.Connect(this.current, head, OwnershipEdgeKind.Back);
        this.loops.RemoveAt(this.loops.Count - 1);
        this.body.RecordCompletion(head, exit, this.flow!.Nodes[loop].CanCompleteNormally);
        return this.CompleteResult(loop, output, exit);
    }

    private void Loop(WhileKoto loop)
    {
        var head = this.Emit(OwnershipOperationKind.Branch, loop);
        var exit = this.New(OwnershipOperationKind.Branch, loop);
        var mark = this.temporaries.Count;
        var condition = this.Value(this.Expression(loop.Condition, PlaceUseKind.Read));
        this.Cleanup(mark, this.locals.Count, loop.Condition, CleanupReason.ExpressionEnd);
        this.temporaries.RemoveRange(mark, this.temporaries.Count - mark);
        var test = this.Emit(OwnershipOperationKind.Branch, loop.Condition);
        if (condition >= 0)
        {
            this.SetValue(test, OwnershipValueKind.Alias, [condition]);
        }

        var enter = this.New(OwnershipOperationKind.Branch, loop.Body);
        this.Connect(test, enter, OwnershipEdgeKind.True);
        this.Connect(test, exit, OwnershipEdgeKind.False);

        this.loops.Add(new(loop, head, exit, this.locals.Count, this.temporaries.Count, Comparisons: this.comparisonDepth));
        this.current = enter;
        this.Block(loop.Body);
        this.Connect(this.current, head, OwnershipEdgeKind.Back);
        this.loops.RemoveAt(this.loops.Count - 1);
        this.current = exit;
    }

    private int Jump(JumpKoto jump)
    {
        var reported = this.body.IssueStorage.Count;
        var value = jump.Expression is { } expression ? this.Expression(expression) : -1;
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
        this.EndComparisonLoans(loanDepth, jump);
        if (returns)
        {
            this.WriteResult(jump, this.resultPlace, value, jump.Expression is null ? -1 : reported);
            this.CheckConstruction(jump);
            this.Cleanup(0, 0, jump, CleanupReason.Return);
            this.Deliver(jump);
            this.Connect(this.current, this.normalExit, OwnershipEdgeKind.Return);
        }
        else if (selected && jump is YieldKoto or ExitKoto)
        {
            var result = selection.Result >= 0 ? this.WriteResult(jump, selection.Result, value) : -1;

            this.Cleanup(selection.Temporaries, selection.Locals, jump, CleanupReason.SelectionResult);
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
                this.ConnectResult(frame.Exit, result);
            }
        }
        else
        {
            this.Unsupported(jump);
        }

        this.current = -1;
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

    // A registration without a Place belongs to a construct whose issue left the graph partial.
    private void CleanupPlace(int place, Koto declaration, Koto source)
    {
        this.body.Invariant(place >= 0, source);
        var operation = this.Emit(OwnershipOperationKind.Cleanup, source, place);
        this.body.OperationSteps[operation] = this.body.CleanupStepStorage.Count;
        this.body.CleanupStepStorage.Add(new(operation, place, declaration, CleanupAction.Skip));
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
        return id;
    }

    private int Emit(OwnershipOperationKind kind, Koto source, int place = -1, int input = -1, AcquisitionKind acquisition = AcquisitionKind.None, LoanRequirement loanMode = LoanRequirement.None, int projection = -1, int reservation = -1)
    {
        var id = this.New(kind, source, place, input, acquisition, loanMode, projection, reservation);
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

    private void Unsupported(Koto source) => this.body.ReportIssue(new(source, OwnershipRequirement.Unsupported));

    // SPEC 21.3.2: a state that a complete Binding never leaves for this analysis.
    private void Internal(Koto source) => this.body.ReportIssue(new(source, OwnershipRequirement.Internal));

    private readonly record struct Registration(int Place, Koto Source, int Sequence, bool IsSubject = false);

    private readonly record struct LoopFrame(Koto Source, int Head, int Exit, int Locals, int Temporaries, int Result = -1, int Comparisons = 0);

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
                // and the body of a declaration that is itself a recovery of one, are explained by that Error (SPEC 23.1.2).
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
                    this.owner.issues.Add(new(node, OwnershipRequirement.Unsupported));
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
