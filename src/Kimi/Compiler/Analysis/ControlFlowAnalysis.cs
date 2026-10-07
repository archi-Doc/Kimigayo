// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // Analysis result and its diagnostic belong together.

/// <summary>A control-flow diagnostic associated with source syntax.</summary>
/// <param name="Node">The offending syntax.</param>
/// <param name="Code">The code of the requirement.</param>
/// <param name="Argument">The first message argument.</param>
/// <param name="Argument2">The second message argument.</param>
public sealed record ControlFlowIssue(Koto Node, DiagnosticCode Code, object? Argument = null, object? Argument2 = null)
{
    /// <summary>Gets the part of the node that is the primary location, when it is not the whole node.</summary>
    public SourceSpan? Span { get; init; }

    /// <summary>Gets Advice formed from the facts.</summary>
    public string? Advice { get; init; }

    /// <summary>Gets the message formatted from the catalog.</summary>
    public string Message => DiagnosticEntries.TryGet(this.Code, out var entry) ? entry.FormatMessage(this.Argument, this.Argument2) : this.Code.ToString();

    /// <summary>Gets the code's evidence facts, all of them or none.</summary>
    public object?[]? Evidence { get; init; }

    /// <summary>Gets a Note formed from the facts.</summary>
    public string? Note { get; init; }

    /// <summary>Gets the repair candidates the warning offers (SPEC 23.3.6.9).</summary>
    internal DiagnosticRepairFact[]? Repairs { get; init; }

    /// <summary>Gets the related locations of an Error formed from Binding's facts, such as an Origin relation's borrow end.</summary>
    internal (string Role, Koto At, string? Label)[]? Related { get; init; }

    internal int Priority { get; init; } = 4;

    /// <summary>Gets the syntax whose normal completion, down to the node, decided a check of a discarded tail.</summary>
    internal Koto? CompletionRoot { get; init; }
}

/// <summary>Separates normal-expression typing from the target's result contract.</summary>
public sealed class ControlFlowNodeInfo
{
    /// <summary>Gets the expression type; null indicates pending Binding.</summary>
    public ControlFlowType? ExpressionType { get; internal set; }

    /// <summary>Gets the target result type; no candidates alone never sets this to Never.</summary>
    public ControlFlowType? TargetResultType { get; internal set; }

    /// <summary>Gets a value indicating whether a selection requires a result.</summary>
    public bool IsResultRequiring { get; internal set; }

    /// <summary>Gets a value indicating whether normal completion is possible within the construct.</summary>
    public bool CanCompleteNormally { get; internal set; }

    /// <summary>Gets the callable return type of a function boundary, separately from its expression type.</summary>
    public ControlFlowType? FunctionResultType { get; internal set; }
}

/// <summary>Analyzes lexical transfers, reachability, result coverage, and known result types.</summary>
/// <remarks>
/// Run after compile-time directive selection. Bodies containing invalid directive groups are skipped.
/// Supply bound type facts to discharge
/// obligations which the default syntax-only type provider cannot decide.
/// </remarks>
public sealed class ControlFlowAnalysis
{
    private const string PointerPrefix = "raw/";
    private static readonly ControlFlowType IsizeType = new("isize");

    private readonly ControlFlowTypeSystem types;
    private readonly StructuralCompletion structural;
    private readonly Dictionary<Koto, ControlFlowNodeInfo> nodes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<JumpKoto, Koto?> targets = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Koto, Boundary> boundaries = new(ReferenceEqualityComparer.Instance);
    private readonly List<ControlFlowIssue> issues = new();
    private readonly List<ControlFlowIssue> warnings = new();
    private readonly HashSet<Koto> warningNodes = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Koto> pending = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<(Koto Node, DiagnosticCode Code)> reported = new();
    private readonly Dictionary<IdentifierNameKoto, ControlFlowType?> names = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DeferredBlockKoto, Flow> cleanups = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Koto, DefaultCompletion> defaultCompletions = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Koto> activeDefaults = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Koto> recursiveDefaults = new(ReferenceEqualityComparer.Instance);
    private readonly List<FunctionKoto> deferredClosures = new();

    // Direct children are collected into one shared stack-like buffer instead of iterator objects.
    // A traversal appends its children, visits them by index, and truncates the buffer afterwards.
    private readonly List<Koto> childBuffer = new();
    private readonly ChildCollector childCollector;
    private readonly List<JumpKoto> arrivedTransfers = new();
    private readonly HashSet<JumpKoto> normalTransferArrivals = new(ReferenceEqualityComparer.Instance);
    private readonly Stack<List<ControlFlowResultSource>> candidateLists = new();
    private readonly Dictionary<string, ControlFlowType> pointeeTypes = new(StringComparer.Ordinal);
    private readonly List<ControlFlowNodeInfo> infoPool = new();
    private readonly List<Boundary> boundaryPool = new();
    private readonly List<HashSet<JumpKoto>> transferPool = new();
    private readonly List<List<DeferredBlockKoto>> registrationPool = new();
    private int infoCursor;
    private int boundaryCursor;
    private int transferCursor;
    private int registrationCursor;

    // The depth of the defaults being visited; the closures they create are visited when it returns to zero.
    private int defaultDepth;

    // Created only after a parser recovery: completion that assumes the bodies the parser supplied never complete normally, and
    // the syntax Errors of the supplied bodies it met.
    private StructuralCompletion? writtenCompletion;
    private List<DiagnosticKey>? suppliedCauses;

    // SPEC 14.3.3: whether an operation in the innermost Unsafe Block being visited used its permission, and whether one whose
    // need is unknown, such as an unresolved operation, may have.
    private bool unsafeUsed;
    private bool unsafeUncertain;

    private ControlFlowAnalysis(ControlFlowTypeSystem types)
    {
        this.types = types;
        this.structural = new(this.IsNever);
        this.childCollector = new(this.childBuffer);
    }

    /// <summary>Gets information for each analyzed expression or boundary.</summary>
    public IReadOnlyDictionary<Koto, ControlFlowNodeInfo> Nodes => this.nodes;

    /// <summary>Gets resolved lexical transfer targets.</summary>
    public IReadOnlyDictionary<JumpKoto, Koto?> Targets => this.targets;

    /// <summary>Gets definite errors.</summary>
    public IReadOnlyList<ControlFlowIssue> Issues => this.issues;

    /// <summary>Gets warnings that do not affect type fitting or acceptance.</summary>
    public IReadOnlyList<ControlFlowIssue> Warnings => this.warnings;

    /// <summary>Gets obligations requiring directive selection or further type Binding.</summary>
    public IReadOnlyCollection<Koto> PendingBinding => this.pending;

    /// <summary>Analyzes attached syntax without mutating or serializing analysis state into the tree.</summary>
    /// <param name="root">The tree or function to analyze.</param>
    /// <param name="types">Bound type facts, or the default syntax-only provider.</param>
    /// <returns>The analysis results.</returns>
    public static ControlFlowAnalysis Analyze(Koto root, ControlFlowTypeSystem? types = null)
    {
        var analysis = new ControlFlowAnalysis(types ?? new SyntaxControlFlowTypes());
        analysis.Visit(root);
        return analysis;
    }

    /// <summary>Replaces these results using retained storage and the same type provider.</summary>
    /// <param name="root">The current bound tree.</param>
    public void Reanalyze(Koto root)
    {
        this.structural.Clear();
        this.nodes.Clear();
        this.targets.Clear();
        this.boundaries.Clear();
        this.issues.Clear();
        this.warnings.Clear();
        this.warningNodes.Clear();
        this.pending.Clear();
        this.reported.Clear();
        this.names.Clear();
        this.cleanups.Clear();
        this.defaultCompletions.Clear();
        this.activeDefaults.Clear();
        this.recursiveDefaults.Clear();
        this.deferredClosures.Clear();
        this.defaultDepth = 0;
        this.childBuffer.Clear();
        this.arrivedTransfers.Clear();
        this.normalTransferArrivals.Clear();
        this.infoCursor = this.boundaryCursor = this.transferCursor = this.registrationCursor = 0;
        this.unsafeUsed = this.unsafeUncertain = false;
        this.Visit(root);
    }

    /// <summary>Records the definite errors and warnings; ownership analysis discards earlier ones when it analyzes again.</summary>
    public void ReportDiagnostics()
    {
        // A check that reads Binding's result at a node that Binding failed, or left resting on a failure, is not decided on
        // valid input: its Error is derived from those causes, which also covers Binding's own check of the same requirement,
        // and its warning is dropped. Structural checks stay independent of Binding, but rest on a recovery that guessed what
        // they read.
        foreach (var issue in this.issues)
        {
            if (this.GuessedBy(issue) is { } syntax)
            {
                issue.Node.ReportDerived(DiagnosticRequirement.ControlFlow, syntax);
            }
            else if (ReadsBinding(issue.Code) && Causes(issue) is { } causes)
            {
                issue.Node.ReportDerived(DiagnosticRequirement.ControlFlow, causes);
            }
            else
            {
                issue.Node.Report(DiagnosticRequirement.ControlFlow, issue.Code, issue.Argument, issue.Argument2, issue.Note, evidence: issue.Evidence, advice: issue.Advice, related: issue.Related);
            }
        }

        foreach (var warning in this.warnings)
        {
            if ((!ReadsBinding(warning.Code) || warning.Node.CodeContext.Compilation.Binding.FailureCauses(warning.Node) is null) &&
                this.GuessedBy(warning) is null)
            {
                warning.Node.Report(DiagnosticRequirement.ControlFlow, warning.Code, warning.Argument, warning.Argument2, advice: warning.Advice, span: warning.Span, repairs: warning.Repairs);
            }
        }
    }

    internal void Append(Koto root) => this.Visit(root);

    // Assumes entry to the resolved target, independently of its outer runtime
    // reachability. Dead transfers still supply result Types, not normal arrivals.
    internal bool ReachesTarget(JumpKoto jump) => this.normalTransferArrivals.Contains(jump);

    // Whether this analysis found an Error of the code at the node; a later phase that meets the same problem rests on it.
    internal bool Reported(Koto node, DiagnosticCode code) => this.reported.Contains((node, code));

    // An omitted default with pending completion cannot be expanded by ownership; the same finite cycle boundary applies.
    internal bool DefaultCompletionPending(Koto expression) => !this.defaultCompletions.TryGetValue(expression, out var completion) || completion.Pending;

    internal bool RecursiveDefault(Koto expression) => this.recursiveDefaults.Contains(expression);

    // The edits that remove an Unsafe Block statement from an indented body: delete `unsafe => ` before an inline Body on the same
    // line, or delete the unsafe line and one indentation level of every line of an indented Body. No candidate when the unsafe line
    // holds other text, a Body line lacks a full level of space indentation, or a string literal spans lines (its content would change).
    private static DiagnosticEditFact[]? RemoveUnsafeEdits(UnsafeBlockKoto block)
    {
        if (block.CodeContext.SourceDocument is not { } document)
        {
            return null;
        }

        var text = document.SourceText.AsSpan();
        var body = block.Body;
        var keywordEnd = block.Span.Start + Constants.UnsafeKeyword.Length;
        var line = document.GetPosition(block.Span.Start).Line;
        var lineStart = document.LineStarts[line];
        if (!text[lineStart..block.Span.Start].IsWhiteSpace())
        {
            return null;
        }

        if (body.IsExpressionBody)
        {
            var bodyStart = body.Span.Start;
            return document.GetPosition(bodyStart).Line == line && text[keywordEnd..bodyStart].Trim().SequenceEqual("=>")
                ? [block.Edit(SourceSpan.FromBounds(block.Span.Start, bodyStart), string.Empty)] : null;
        }

        var lineEnd = lineStart + document.GetLineSpan(line).Length;
        var lastLine = document.GetPosition(Math.Max(body.Span.Start, body.Span.End - 1)).Line;
        if (!text[keywordEnd..lineEnd].IsWhiteSpace() || lastLine <= line || line + 1 >= document.LineCount || SpansLines(body, document))
        {
            return null;
        }

        var edits = new List<DiagnosticEditFact>(lastLine - line + 1) { block.Edit(SourceSpan.FromBounds(lineStart, document.LineStarts[line + 1]), string.Empty) };
        for (var current = line + 1; current <= lastLine; current++)
        {
            var content = document.GetLineSpan(current);
            if (content.IsWhiteSpace())
            {
                continue;
            }

            if (content.Length < Constants.IndentationSpaces || content[..Constants.IndentationSpaces].ContainsAnyExcept(' '))
            {
                return null;
            }

            edits.Add(block.Edit(new(document.LineStarts[current], Constants.IndentationSpaces), string.Empty));
        }

        return edits.ToArray();
    }

    // Whether a string literal inside the node spans lines; dedenting its continuation lines would change its content.
    private static bool SpansLines(Koto node, SourceDocument document)
    {
        var finder = new MultiLineLiteralFinder(document);
        finder.Visit(node);
        return finder.Found;
    }

    // SPEC 17.2.4: `try` propagates a Result's error only to a function whose result is a Result of the same error Type.
    private static bool FailureTargetFits(Koto node)
    {
        if (node.BoundType is not { Kind: BoundTypeKind.Constructed, Components.Count: 2 } result || result.Symbol?.LibraryDeclaration != KimiDeclarationId.Result)
        {
            return false;
        }

        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is FunctionKoto function)
            {
                return function.BoundSymbol?.Type is { Kind: BoundTypeKind.Constructed, Components.Count: 2 } target && target.Symbol?.LibraryDeclaration == KimiDeclarationId.Result &&
                    (ReferenceEquals(target.Components[1], result.Components[1]) || Equals(target.Components[1], result.Components[1]));
            }

            if (parent is PropertyAccessorKoto or DeferredBlockKoto)
            {
                return false;
            }
        }

        return false;
    }

    // A result is checked against its consumer, the one node that reads it, which Binding checks for the same requirement.
    private static DiagnosticKey[]? Causes(ControlFlowIssue issue)
    {
        var binding = issue.Node.CodeContext.Compilation.Binding;
        var causes = binding.FailureCauses(issue.Node);
        if (causes is not null || issue.Code is not (DiagnosticCode.IncompatibleResult_Kd or DiagnosticCode.UnsatisfiedOriginRelation_Kd or DiagnosticCode.UnprovenOriginRelation_Kd))
        {
            return causes;
        }

        var consumer = issue.Node.Parent;
        while (consumer is ParenthesizedKoto or LabeledKoto or CodeBlockKoto { IsExpressionBody: true })
        {
            consumer = consumer.Parent;
        }

        return consumer is null ? null : binding.FailureCauses(consumer);
    }

    // Jump targets, labels, fallthrough, Unsafe Blocks and while true are judged from syntax alone.
    private static bool ReadsBinding(DiagnosticCode code)
        => code is not (DiagnosticCode.InvalidJumpTarget_Kd or DiagnosticCode.UnlabeledYieldTarget_Kd or DiagnosticCode.RequireFallthrough_Kd or
            DiagnosticCode.FunctionFallthrough_Kd or DiagnosticCode.OverlappingLabel_Kd or DiagnosticCode.UnsafeBlockRequired_Kd or DiagnosticCode.StaticWhileTrue_Kd);

    private static bool IsPointer(ControlFlowType? type)
        => type is BoundType bound ? ReferenceTypes.IsPointer(bound) : type?.Name.StartsWith(PointerPrefix, StringComparison.Ordinal) == true;

    private static bool IsNullLiteral(Koto node)
        => KotoHelper.UnwrapParentheses(node) is NullLiteralKoto;

    /// <summary>Merges transfer sets. A child's set is consumed exactly once, so it can be reused as the result.</summary>
    private static HashSet<JumpKoto>? Union(HashSet<JumpKoto>? left, HashSet<JumpKoto>? right)
    {
        if (right is null || right.Count == 0)
        {
            return left;
        }

        if (left is null)
        {
            return right;
        }

        StructuralCompletion.UnionTransfers(left, right);
        return left;
    }

    // A single-item body: the item of a body introduced by =>, a require failure body or a match arm's body.
    private static bool IsBodyItem(Koto node) => node.Parent switch
    {
        CodeBlockKoto block => block.IsExpressionBody,
        RequireKoto require => require.ElseBody == node,
        MatchKoto match => KotoHelper.IsSelectionBody(match, node),
        _ => false,
    };

    private bool IsNever(Koto node)
        => this.types.GetExpressionType(node) == ControlFlowType.Never || this.nodes.GetValueOrDefault(node)?.ExpressionType == ControlFlowType.Never;

    // The syntax Errors of the recoveries that guessed what a check reads, or null when it reads written syntax alone. A recovered
    // operand does not invalidate independently parsed target syntax, so only the guessed target of an unlabeled jump or a label
    // counts. A fallthrough or a discarded tail reads normal completion: it rests on the bodies the parser supplied for missing
    // ones when the syntax completes normally only through them, since a written body might have returned or exited.
    private DiagnosticKey[]? GuessedBy(ControlFlowIssue issue)
    {
        var node = issue.Node;
        if (!node.CodeContext.HasRecoveries)
        {
            return null;
        }

        if (node is LabeledKoto or JumpKoto { Label: "" })
        {
            return node.CodeContext.RecoveryCause(node) is { } syntax ? [syntax] : null;
        }

        return issue.Code is DiagnosticCode.FunctionFallthrough_Kd or DiagnosticCode.RequireFallthrough_Kd or DiagnosticCode.DiscardedTail_Kd
            ? this.SuppliedCompletion(node, issue.CompletionRoot ?? node) : null;
    }

    // Each syntax from the node up to the root completed normally for the check; one that completes only through supplied bodies
    // makes the check rest on their syntax Errors.
    private DiagnosticKey[]? SuppliedCompletion(Koto node, Koto root)
    {
        var causes = this.suppliedCauses ??= new();
        var written = this.writtenCompletion ??= new(this.IsNever, this.IsSuppliedBody);
        causes.Clear();
        written.Clear();
        var guessed = false;
        for (Koto? part = node; part is not null && !guessed; part = part == root ? null : part.Parent)
        {
            guessed = this.structural.CanComplete(part) && !written.CanComplete(part);
        }

        return guessed && causes.Count > 0 ? [.. causes] : null;
    }

    // A body the parser supplied for a missing one, a recovery: the empty block in place of an indented body, or the Error
    // expression in place of a single-item body (other Error expressions stand for operands). Records its syntax Error.
    private bool IsSuppliedBody(Koto node)
    {
        if ((node is CodeBlockKoto || (node is ErrorKoto && IsBodyItem(node))) && node.CodeContext.RecoveryCause(node) is { } cause)
        {
            this.suppliedCauses!.Add(cause);
            return true;
        }

        return false;
    }

    // SPEC 17.4.3, 23.3.6.9: a discarded Result or try success value that is a direct item of an indented body offers `_ = ` before it
    // and, for a Result whose error Type the enclosing function's failure return target takes (SPEC 17.2.4), `_ = try ` as well;
    // `_ =` puts the expression in Value Context (SPEC 14.2.4) and changes no scope, so no condition is relevant. The Advice then
    // states the alternatives the candidates cannot express; elsewhere the catalog's Advice describes every repair.
    private void WarnDiscard(Koto node, DiagnosticCode code, int priority)
    {
        node = KotoHelper.UnwrapParentheses(node);
        if (!this.warningNodes.Add(node))
        {
            return;
        }

        DiagnosticRepairFact[]? repairs = null;
        string? advice = null;
        if (node.Parent is CodeBlockKoto { IsExpressionBody: false })
        {
            var start = new SourceSpan(node.Span.Start, 0);
            var discard = new DiagnosticRepairFact(RepairKind.ExplicitDiscard, null, [node.Edit(start, "_ = ")], RepairConditionSet.None);
            repairs = code == DiagnosticCode.DiscardedResult_Kd && FailureTargetFits(node)
                ? [new(RepairKind.PropagateFailure, null, [node.Edit(start, "_ = try ")], RepairConditionSet.None), discard]
                : [discard];
            advice = code == DiagnosticCode.DiscardedResult_Kd ? "Handle the Result with match, or use its success value" : "Use the extracted value";
        }

        this.warnings.Add(new(node, code) { Priority = priority, Advice = advice, Repairs = repairs });
    }

    private void Warn(Koto node, DiagnosticCode code, int priority = 4, Koto? completionRoot = null)
    {
        node = KotoHelper.UnwrapParentheses(node);
        if (this.warningNodes.Add(node))
        {
            this.warnings.Add(new(node, code) { Priority = priority, CompletionRoot = completionRoot });
            return;
        }

        for (var i = 0; i < this.warnings.Count; i++)
        {
            if (this.warnings[i].Node == node && priority < this.warnings[i].Priority)
            {
                this.warnings[i] = new(node, code) { Priority = priority, CompletionRoot = completionRoot };
                break;
            }
        }
    }

    private bool IsEffectFree(Koto node)
    {
        node = KotoHelper.UnwrapParentheses(node);

        if (node is IsKoto { IsRuntimeTest: true } test)
        {
            // A stable name needs no acquisition/destruction. Calls and Properties
            // retain their effects; the target Type is never a value expression.
            return this.types.IsBoundRuntimeTypeTest(test) && KotoHelper.UnwrapParentheses(test.Left) is
                IdentifierNameKoto { BoundSymbol.Kind: BindingSymbolKind.Local or BindingSymbolKind.Parameter };
        }

        // Tuples and Case constructions are effect-free when every nested operand is (SPEC 17.4.2).
        // A Case construction must also have a proven Copy Type, so its destruction is not observable.
        if (node is TupleLiteralKoto tuple)
        {
            return this.AreEffectFree(tuple.Elements);
        }

        if (this.types.IsBoundConstruction(node))
        {
            return this.types.IsProvenCopy(node) && (node is not InvocationKoto call || this.AreEffectFree(call.ArgumentNodes));
        }

        var type = this.types.GetExpressionType(node) ?? this.nodes.GetValueOrDefault(node)?.ExpressionType;
        var name = type is BoundType bound ? bound.Underlying.Name : type?.Name;
        var primitive = name is "bool" or "char" or "integer literal" or "float literal" or
            "i8" or "i16" or "i32" or "i64" or "i128" or "u8" or "u16" or "u32" or "u64" or "u128" or "isize" or "usize" or "f32" or "f64";
        if (!primitive)
        {
            return false;
        }

        return node switch
        {
            NumberLiteralKoto or BoolLiteralKoto or CharLiteralKoto => true,
            IdentifierNameKoto { BoundSymbol.Kind: BindingSymbolKind.Local or BindingSymbolKind.Parameter } => true,
            NotKoto unary => this.IsEffectFree(unary.Operand),
            EqualsEqualsKoto or ExclamationEqualsKoto or LessThanKoto or LessThanEqualsKoto or GreaterThanKoto or GreaterThanEqualsKoto =>
                this.IsEffectFree(((BinaryKoto)node).Left) && this.IsEffectFree(((BinaryKoto)node).Right),
            _ => false,
        };
    }

    private bool AreEffectFree(IReadOnlyList<Koto> operands)
    {
        for (var i = 0; i < operands.Count; i++)
        {
            if (!this.IsEffectFree(operands[i]))
            {
                return false;
            }
        }

        return true;
    }

    // Descends from the root through syntax that completes normally; the warning keeps the root, so it can tell whether that
    // completion passes only through bodies the parser supplied.
    private void WarnUnitTail(Koto body, Koto root)
    {
        if (!this.structural.CanComplete(body))
        {
            return;
        }

        if (body is CodeBlockKoto block)
        {
            if (block.Items.Count > 0)
            {
                this.WarnUnitTail(block.Items[^1], root);
            }

            return;
        }

        body = KotoHelper.UnwrapParentheses(body);
        if (body is LabeledKoto label)
        {
            this.WarnUnitTail(label.Target, root);
        }
        else if (body is DoKoto scoped)
        {
            this.WarnUnitTail(scoped.Body, root);
        }
        else if (body is IfKoto conditional)
        {
            for (var i = 0; i < conditional.Branches.Count; i++)
            {
                this.WarnUnitTail(conditional.Branches[i].Body, root);
            }

            if (conditional.ElseBody is { } other)
            {
                this.WarnUnitTail(other, root);
            }
        }
        else if (body is MatchKoto match && body is not TryKoto)
        {
            for (var i = 0; i < match.Arms.Count; i++)
            {
                this.WarnUnitTail(match.Arms[i].Body, root);
            }
        }
        else if (body is ExpressionKoto and not (UnitLiteralKoto or JumpKoto or LoopKoto or ForKoto or WhileKoto) &&
            this.nodes.GetValueOrDefault(body)?.ExpressionType is { } type && type != ControlFlowType.Unit && type != ControlFlowType.Never)
        {
            this.Warn(body, DiagnosticCode.DiscardedTail_Kd, 1, root);
        }
    }

    private void Error(Koto node, DiagnosticCode code, object? argument = null, object? argument2 = null, object?[]? evidence = null, string? note = null)
    {
        if (this.reported.Add((node, code)))
        {
            this.issues.Add(new(node, code, argument, argument2) { Evidence = evidence, Note = note });
        }
    }

    private ControlFlowNodeInfo RentInfo()
    {
        if (this.infoCursor == this.infoPool.Count)
        {
            this.infoPool.Add(new());
        }

        var info = this.infoPool[this.infoCursor++];
        info.ExpressionType = info.TargetResultType = info.FunctionResultType = null;
        info.IsResultRequiring = info.CanCompleteNormally = false;
        return info;
    }

    // The info of a visited node. A declaration is not evaluated and records none of its own, so a construct sharing the info
    // of a declaration it holds, such as a label or a branch body recovered over one (LabelTargetExpected_Kd), rents it here.
    private ControlFlowNodeInfo NodeInfo(Koto node)
    {
        if (!this.nodes.TryGetValue(node, out var info))
        {
            this.nodes[node] = info = this.RentInfo();
        }

        return info;
    }

    private HashSet<JumpKoto> RentTransfers()
    {
        if (this.transferCursor == this.transferPool.Count)
        {
            this.transferPool.Add(new(ReferenceEqualityComparer.Instance));
        }

        var set = this.transferPool[this.transferCursor++];
        set.Clear();
        return set;
    }

    private List<DeferredBlockKoto> RentRegistrations()
    {
        if (this.registrationCursor == this.registrationPool.Count)
        {
            this.registrationPool.Add(new());
        }

        var list = this.registrationPool[this.registrationCursor++];
        list.Clear();
        return list;
    }

    private Flow Visit(Koto node, ControlFlowType? expected = null)
    {
        if (node is FunctionKoto && TestDefinition.Marker(node) is not null && !TestDefinition.IsIncluded(node))
        {
            return new(true, ControlFlowType.Unit);
        }

        expected ??= this.types.GetExpectedType(node);
        if (node is ExpressionKoto && !KotoHelper.IsValueContext(node) && node.Parent is not ParenthesizedKoto)
        {
            var value = KotoHelper.UnwrapParentheses(node);
            var type = this.types.GetExpressionType(value);
            if (this.types.IsKimiResult(value))
            {
                this.WarnDiscard(node, DiagnosticCode.DiscardedResult_Kd, 2);
            }
            else if (value is TryKoto && type is not null && type != ControlFlowType.Unit && type != ControlFlowType.Never)
            {
                this.WarnDiscard(node, DiagnosticCode.UnusedTrySuccess_Kd, 3);
            }
            else if (this.IsEffectFree(node))
            {
                this.Warn(node, DiagnosticCode.DiscardedValue_Kd);
            }
        }

        var referencedFunction = this.types.GetReferencedFunction(node);
        if (referencedFunction is not null && (referencedFunction.Modifier & ModifierKind.Unsafe) != 0)
        {
            var reference = node;
            while ((reference.Parent is GenericsKoto generics && generics.Identifier == reference) ||
                reference.Parent is ParenthesizedKoto ||
                (reference.Parent is MemberAccessKoto member && member.Right == reference))
            {
                reference = reference.Parent;
            }

            if (reference.Parent is InvocationKoto invocation && invocation.Method == reference)
            {
                this.CheckUnsafePermission(invocation);
            }
            else
            {
                this.Error(node, DiagnosticCode.UnsafeFunctionValue_Kd);
            }
        }

        var requiresUnsafe = this.types.RequiresUnsafeContext(node);
        if (requiresUnsafe == true)
        {
            this.CheckUnsafePermission(node);
        }
        else if (requiresUnsafe is null && node is ExpressionKoto or NoInitKoto)
        {
            // An operation whose need is unknown may use the permission, so the enclosing block is not reported as unused.
            this.unsafeUncertain = true;
        }

        Flow flow;
        switch (node)
        {
            case ConversionKoto { CreationCall: { } creation }:
                flow = this.Visit(creation) with { Type = this.types.GetExpressionType(node) };
                break;
            case ConversionKoto { Adaptation.Creates: true, CreationStorage: { } conditionalCreation }:
                // Every case evaluates the same input once. Register the retained invocation's completion so a closed
                // creation case can lower it; an identity case uses only that input and never executes the invocation.
                flow = this.Visit(conditionalCreation) with { Type = this.types.GetExpressionType(node) };
                break;
            case BinaryKoto { Akind: KotoKind.Equals } assignment when node.CodeContext.Compilation.Binding.PropertyCall(assignment.Left, PropertyAccessorKind.Set) is { } setter:
                flow = this.Visit(setter) with { Type = this.types.GetExpressionType(node) };
                break;
            case IdentifierNameKoto or MemberAccessKoto when node.CodeContext.Compilation.Binding.PropertyCall(node, PropertyAccessorKind.Get) is { } getter:
                flow = this.Visit(getter) with { Type = this.types.GetExpressionType(node) };
                break;
            case RangeKoto or FromEndIndexKoto when node.CodeContext.Compilation.Binding.RangeValueCall(node) is { } rangeValue:
                // SPEC 4.6.2, 4.6.3: prefix ^ and range syntax that construct a value are their synthesized PositionSyntax
                // calls, whose arguments may themselves be synthesized `^x` constructions.
                flow = this.Visit(rangeValue) with { Type = this.types.GetExpressionType(node) };
                break;
            case IndexKoto when node.CodeContext.Compilation.Binding.ViewRangeCall(node) is { } viewRange:
                flow = this.Visit(viewRange) with { Type = this.types.GetExpressionType(node) };
                break;
            case IndexKoto keyed when node.CodeContext.Compilation.Binding.ResolvedKeyCall(keyed) is { } resolvedKey:
                // SPEC 4.6.4: the receiver is evaluated, then a synthesized call resolves the key against the length of that
                // evaluated receiver; the selection completes as both do.
                var selectedFlow = this.Visit(keyed.Left);
                var keyFlow = this.Visit(resolvedKey);
                flow = new(selectedFlow.Normal && keyFlow.Normal, this.types.GetExpressionType(node), Union(selectedFlow.Transfers, selectedFlow.Normal ? keyFlow.Transfers : null), selectedFlow.Pending || keyFlow.Pending);
                break;
            case EvaluatedKoto:
                flow = new(true, this.types.GetExpressionType(node)); // Its desugaring evaluated the source already.
                break;
            case IdentifierNameKoto when node.CodeContext.Compilation.Binding.StorageProjection(node) is { } storage:
                flow = this.Visit(storage);
                break;
            case BinaryKoto { ComparisonCall: { } comparisonCall }:
                flow = this.Visit(comparisonCall) with { Type = this.types.GetExpressionType(node) };
                break;
            case MacroKoto { Formatting: { Acquisition: { } acquisition } tryWrite }:
                var rootFlow = this.Visit(acquisition);
                var normalRoot = rootFlow.Normal;
                foreach (var write in tryWrite.Writes)
                {
                    var part = this.Visit(write);
                    rootFlow = new(rootFlow.Normal && part.Normal, part.Type, Union(rootFlow.Transfers, rootFlow.Normal ? part.Transfers : null), rootFlow.Pending || part.Pending);
                }

                this.Visit(tryWrite.Outcome);
                flow = rootFlow with { Normal = normalRoot, Type = this.types.GetExpressionType(node) };
                break;
            case InterpolatedStringKoto { Formatting: { } formatting }:
                var formattingFlow = this.Visit(formatting.Heap);
                var adapterFlow = this.Visit(formatting.Adapter);
                formattingFlow = new(formattingFlow.Normal && adapterFlow.Normal, formattingFlow.Type, Union(formattingFlow.Transfers, adapterFlow.Transfers), formattingFlow.Pending || adapterFlow.Pending);
                foreach (var write in formatting.Writes)
                {
                    var writeFlow = this.Visit(write);
                    formattingFlow = new(formattingFlow.Normal && writeFlow.Normal, writeFlow.Type, Union(formattingFlow.Transfers, formattingFlow.Normal ? writeFlow.Transfers : null), formattingFlow.Pending || writeFlow.Pending);
                }

                flow = formattingFlow with { Type = this.types.GetExpressionType(node) };
                break;
            case SyntaxFormKoto { Akind: KotoKind.EnumCase }:
                // Case payloads are declaration Types, including their Origin names.
                // Binding validates them; constructing a Case is a separate expression.
                flow = new(true, ControlFlowType.Unit);
                break;
            case IsKoto { BoundConstraint: not null }:
                // A declaration constraint has no runtime evaluation. Its bound
                // Container path is checked by Binding, including every qualifier.
                flow = new(true, ControlFlowType.Unit);
                break;
            case not InvocationKoto when this.types.IsBoundConstruction(node):
                flow = new(true, this.types.GetExpressionType(node));
                break;
            case FunctionKoto function:
                // Every default is a separate declaration-time expression, even for
                // unused requirements or calls supplying all arguments. Its completion
                // does not enter the callee body or its enclosing declaration's flow.
                for (var i = 0; i < function.Parameters.Count; i++)
                {
                    var parameter = function.Parameters[i];
                    if (parameter.DefaultValue is { } value && !this.HasInvalidDirective(value))
                    {
                        this.VisitDefault(function, i);
                    }
                }

                if (function.BoundClosure is not null && this.defaultDepth > 0)
                {
                    // SPEC 7.2.3: creating a closure inside a default completes without running its body, so the
                    // default's completion never waits on it. The body is visited once the outermost default's completion
                    // is recorded; a call there that omits the same default, such as a recursive call, then reads that
                    // completion instead of an unfinished cycle.
                    this.deferredClosures.Add(function);
                    return this.CreatedClosure(function);
                }

                this.VisitFunctionBody(function);
                if (function.BoundClosure is not null)
                {
                    return this.CreatedClosure(function);
                }

                return new(true, null); // A function value is not its body or its return type.
            case PropertyAccessorKoto accessor:
                var getterResult = accessor.Parent is PropertyKoto property
                    ? this.types.GetDefaultGetterResultType(property)
                    : null;
                this.VisitFunction(
                    accessor,
                    accessor.Body,
                    accessor.ReturnType,
                    accessor.AccessorKind == PropertyAccessorKind.Set ? ControlFlowType.Unit : getterResult ?? this.types.GetExpectedResultType(accessor),
                    accessor.AccessorKind == PropertyAccessorKind.Get && accessor.ReturnType is null && getterResult is null);
                return new(true, null);
            case DeclarationContainerKoto:
                this.VisitDeclarations(node);
                return new(true, ControlFlowType.Unit);
            case SyntaxFormKoto { Akind: KotoKind.AssociatedType }:
                // SPEC 8.4.3: associated-Type parameters and formation Types have no runtime evaluation.
                return new(true, ControlFlowType.Unit);
            case EffectBoundKoto:
                // SPEC 8.4.10.1: an effect item is declaration metadata; its selector and Name are never evaluated.
                return new(true, ControlFlowType.Unit);
            case OriginRelationKoto:
                // SPEC 15.3.3: a Type's origin clause, visited with its declarations, is a contract relation, never evaluated.
                return new(true, ControlFlowType.Unit);
            case AttributeKoto { BindingState: BindingState.Resolved, LayoutMode: not null }:
                // SPEC 21.1.2: a checked layout attribute is declaration metadata;
                // its syntax argument is not a runtime call or string acquisition.
                return new(true, ControlFlowType.Unit);
            case CompileTimeSwitchKoto:
                // Invalid groups already have parser diagnostics; their arms are not executable.
                return new(true, null);
            case FieldKoto field:
                var declared = this.types.GetDeclaredType(field.TypeKoto);
                flow = field.InitializerKoto is { } initializer ? this.Visit(initializer, declared) : new(true, declared);
                if (field.TypeKoto is not null && declared is null)
                {
                    this.pending.Add(field);
                }

                this.names[field.NameKoto] = declared ?? flow.Type;
                flow = flow with { Type = ControlFlowType.Unit };
                break;
            case PropertyKoto storedProperty:
                // Stored declaration annotations (including Origin names) are not evaluations.
                flow = storedProperty.InitializerKoto is { } fieldInitializer ? this.Visit(fieldInitializer, this.types.GetDeclaredType(storedProperty.TypeKoto)) : new(true, ControlFlowType.Unit);
                for (var i = 0; i < storedProperty.Accessors.Count; i++)
                {
                    this.Visit(storedProperty.Accessors[i]);
                }

                break;
            case LabeledKoto labeled:
                this.CheckLabel(labeled);
                flow = this.Visit(labeled.Target, expected);
                this.nodes[labeled] = this.NodeInfo(labeled.Target);
                break;
            case DeferredBlockKoto deferred:
                var cleanupBoundary = this.Begin(deferred, ControlFlowType.Unit);
                this.cleanups[deferred] = this.Finish(deferred, this.Visit(deferred.Body), cleanupBoundary);
                this.nodes[deferred].ExpressionType = null;
                // Registration never evaluates its body. Its completion matters at scope exit only.
                return new(true, ControlFlowType.Unit);
            case UnsafeBlockKoto unsafeBlock:
                // SPEC 14.3.3: an operation uses the permission of the innermost enclosing Unsafe Block.
                var (outerUsed, outerUncertain) = (this.unsafeUsed, this.unsafeUncertain);
                (this.unsafeUsed, this.unsafeUncertain) = (false, false);
                flow = this.Visit(unsafeBlock.Body);
                if (!this.unsafeUsed && !this.unsafeUncertain)
                {
                    this.WarnUnnecessaryUnsafe(unsafeBlock);
                }

                (this.unsafeUsed, this.unsafeUncertain) = (outerUsed, outerUncertain || this.unsafeUncertain);
                var unsafeInfo = this.RentInfo();
                unsafeInfo.CanCompleteNormally = flow.Normal;
                this.nodes[unsafeBlock] = unsafeInfo;
                return flow with { Type = ControlFlowType.Unit };
            case DoKoto scoped:
                flow = this.VisitDo(scoped, expected);
                break;
            case IfKoto conditional:
                flow = this.VisitIf(conditional, expected);
                break;
            case TryKoto propagation:
                flow = this.VisitMatch(propagation, this.types.GetExpressionType(propagation));
                break;
            case MatchKoto match:
                flow = this.VisitMatch(match, expected);
                break;
            case LoopKoto or WhileKoto or ForKoto:
                flow = this.VisitIteration(node, expected);
                break;
            case JumpKoto jump:
                flow = this.VisitJump(jump);
                break;
            case DiscardKoto discard:
                flow = this.Visit(discard.Operand) with { Type = ControlFlowType.Unit };
                break;
            case RequireKoto require:
                flow = this.VisitRequire(require);
                break;
            case TestVerificationKoto verification:
                var conditionFlow = this.Visit(verification.Condition, ControlFlowType.Boolean);
                var messageFlow = verification.Message is { } message ? this.Visit(message, new("string")) : new Flow(true, ControlFlowType.Unit);
                flow = new(conditionFlow.Normal, ControlFlowType.Unit, Union(conditionFlow.Transfers, conditionFlow.Normal ? messageFlow.Transfers : null), conditionFlow.Pending || messageFlow.Pending);
                break;
            case CodeBlockKoto block:
                flow = this.VisitSequence(block.Items, 0, block.Items.Count);
                break;
            case InvocationKoto call when this.types.TryGetCallReceiver(call, out var receiver):
                if (call.Method is not FormattingKoto && call.BoundCall is { Target.CompilerFunction: CompilerFunctionKind.WriterWrite } selected)
                {
                    for (var i = 0; i < call.ArgumentNodes.Count; i++)
                    {
                        if (selected.ArgumentToParameter[i] == 1 && call.ArgumentNodes[i] is InterpolatedStringKoto literal)
                        {
                            this.Warn(literal, DiagnosticCode.OwningWriteArgument_Kd);
                        }
                    }
                }

                // A committed direct callee is a designator, not a function-value acquisition.
                // Bound receiver syntax precedes the explicit arguments exactly once.
                var receiverFlow = receiver is null ? new Flow(true, ControlFlowType.Unit) : this.Visit(receiver);
                var argumentsFlow = this.VisitSequence(call.ArgumentNodes, 0, call.ArgumentNodes.Count);
                var callType = this.types.GetExpressionType(call);
                var acquired = receiverFlow.Normal && argumentsFlow.Normal;
                var defaultPending = false;
                if (acquired && call.BoundCall is { Target.Declaration: FunctionKoto target } plan)
                {
                    foreach (var omitted in plan.DefaultArguments)
                    {
                        var completion = this.VisitDefault(target, omitted.Parameter.Slot);
                        defaultPending |= completion.Pending;
                        acquired &= completion.Normal;
                        if (!acquired)
                        {
                            break;
                        }
                    }
                }

                flow = new(
                    acquired && callType != ControlFlowType.Never,
                    callType,
                    Union(receiverFlow.Transfers, receiverFlow.Normal ? argumentsFlow.Transfers : null),
                    receiverFlow.Pending || (receiverFlow.Normal && argumentsFlow.Pending) || defaultPending || callType is null);
                if (callType is null || defaultPending)
                {
                    this.pending.Add(call);
                }

                break;
            case ParenthesizedKoto p:
                flow = this.Visit(p.Operand, expected);
                break;
            case MemberAccessKoto { BoundSymbol.Kind: BindingSymbolKind.Function, BoundType.Kind: BoundTypeKind.FunctionItem or BoundTypeKind.Function } reference:
                // A selected function reference names a declaration. Its group/type qualifiers are designators,
                // including every segment of a nested group, rather than runtime receiver expressions.
                flow = new(true, this.types.GetExpressionType(reference));
                break;
            case MemberAccessKoto { BoundSymbol.Property: not null } member:
                // A selected property name is a designator, not an evaluated local.
                var ownerFlow = member.Left.BoundSymbol?.Kind == BindingSymbolKind.Container
                    ? new Flow(true, ControlFlowType.Unit) : this.Visit(member.Left);
                flow = new(ownerFlow.Normal, this.types.GetExpressionType(member), ownerFlow.Transfers, ownerFlow.Pending);
                break;
            case MacroKoto macro when this.types.GetExpressionType(macro) == ControlFlowType.Never:
                flow = this.Visit(macro.Operand, expected);
                break;
            case IdentifierNameKoto name:
                var nameType = this.types.GetExpressionType(name) ?? this.ResolveNameType(name);
                flow = new(nameType != ControlFlowType.Never, nameType, Pending: nameType is null);
                break;
            case NullLiteralKoto:
                flow = new(true, expected); // Missing type context cannot make a literal diverge.
                if (expected is null)
                {
                    this.pending.Add(node);
                    var position = node;
                    while (position.Parent is ParenthesizedKoto parentheses)
                    {
                        position = parentheses;
                    }

                    if (position.Parent is FieldKoto { TypeKoto: null } ||
                        (position.Parent is CodeBlockKoto { IsExpressionBody: false } && !KotoHelper.IsValueContext(position)))
                    {
                        this.Error(node, DiagnosticCode.UntypedNull_Kd);
                    }
                }
                else if (!IsPointer(expected))
                {
                    this.Error(node, DiagnosticCode.UntypedNull_Kd);
                }

                break;
            case EqualsEqualsKoto or ExclamationEqualsKoto when IsNullLiteral(((BinaryKoto)node).Left) || IsNullLiteral(((BinaryKoto)node).Right):
                var comparison = (BinaryKoto)node;
                var nullOnLeft = IsNullLiteral(comparison.Left);
                var other = nullOnLeft ? comparison.Right : comparison.Left;
                var nullOperand = nullOnLeft ? comparison.Left : comparison.Right;
                var otherFlow = this.Visit(other);
                var nullFlow = this.Visit(nullOperand, otherFlow.Type);
                if (IsNullLiteral(other))
                {
                    this.Error(node, DiagnosticCode.UntypedNullComparison_Kd);
                }

                flow = new(otherFlow.Normal, ControlFlowType.Boolean, otherFlow.Transfers, otherFlow.Pending || nullFlow.Pending);
                break;
            case AndKoto or OrKoto:
                var logical = (BinaryKoto)node;
                var left = this.Visit(logical.Left, ControlFlowType.Boolean);
                var right = this.Visit(logical.Right, ControlFlowType.Boolean);
                // Runtime short-circuiting may skip the right operand. Do not constant-fold it for reachability.
                flow = new(left.Normal, ControlFlowType.Boolean, Union(left.Transfers, left.Normal ? right.Transfers : null), left.Pending || right.Pending);
                break;
            case IsKoto { IsRuntimeTest: true } test:
                flow = this.Visit(test.Left) with { Type = ControlFlowType.Boolean };
                if (!this.types.IsBoundRuntimeTypeTest(test))
                {
                    // bool is known from syntax; target validity still requires Binding.
                    this.pending.Add(test);
                    flow = flow with { Pending = true };
                }

                break;
            case ConversionKoto conversion:
                // The target is checked Type syntax, not a runtime expression.
                flow = this.Visit(conversion.Left);
                var sourceType = this.nodes[conversion.Left].ExpressionType;
                var destinationType = this.types.GetDeclaredType(conversion.Right);
                var sourcePointer = IsPointer(sourceType);
                var destinationPointer = IsPointer(destinationType);
                // SPEC 5: a conversion cannot cause undefined behavior, so it needs no unsafe context. An address (@raw, SPEC 5.4),
                // a borrow and a follow convert no pointer.
                var borrow = conversion.Right is TypeSemanticsKoto { Type: null } bare
                    ? bare.Identifier == Constants.FollowOperation ||
                        (CompilerHelper.TryParse(bare.Identifier, out var semantics) && semantics is SemanticsKind.Raw or SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.ObjRef or SemanticsKind.ObjUniq)
                    : conversion.Right is TypeSemanticsKoto { SemanticsKind: SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.ObjRef or SemanticsKind.ObjUniq };
                if (!borrow && (sourcePointer || destinationPointer) && !(sourceType is BoundType && ReferenceEquals(sourceType, destinationType)))
                {
                    if ((sourcePointer && destinationType is not null && !destinationPointer && destinationType.Name != "usize") ||
                        (destinationPointer && sourceType is not null && !sourcePointer && sourceType.Name is not ("usize" or "Never" or "integer literal")))
                    {
                        this.Error(conversion, DiagnosticCode.InvalidPointerConversion_Kd);
                    }
                }

                // SPEC 10.2: the explicit conversion runs first, then its selected expected-Type adaptation supplies
                // the result (for example, a value read of `position@ref` in a range-valued branch).
                flow = flow with { Type = flow.Normal ? this.types.GetExpressionType(conversion) ?? destinationType : ControlFlowType.Never, Pending = flow.Pending || destinationType is null };
                if (destinationType is null)
                {
                    this.pending.Add(conversion);
                }

                break;
            case GenericsKoto generic:
                // Bound Type/length arguments, including Origin names, are compile-time syntax. Only the
                // referenced expression is evaluated; traversing the arguments invents runtime pending values.
                flow = generic.Identifier is { } identifier ? this.Visit(identifier) : new(true, ControlFlowType.Unit);
                flow = flow with { Type = this.types.GetExpressionType(generic) };
                break;
            default:
                flow = this.VisitChildSequence(node);
                flow = flow with { Type = this.types.GetExpressionType(node) ?? this.InferLocalType(node) };
                if (flow.Type is null && node is ExpressionKoto && node is not (TypeKoto or ErrorKoto))
                {
                    this.pending.Add(node);
                    flow = flow with { Pending = true };
                }

                if (flow.Type == ControlFlowType.Never)
                {
                    flow = flow with { Normal = false };
                }

                break;
        }

        var info = this.NodeInfo(node);
        info.ExpressionType = this.boundaries.TryGetValue(node, out var resultBoundary) && resultBoundary.InvalidResult
            ? null : flow.Type;
        info.CanCompleteNormally = flow.Normal;
        var updatedTarget = node switch
        {
            BinaryKoto binary when binary.Akind is > KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals => binary.Left,
            UnaryKoto unary when ElementAccess.UpdateOperator(unary.Akind) != KotoKind.Invalid => unary.Operand,
            _ => null,
        };
        if (updatedTarget is not null && node.CodeContext.Compilation.Binding.PropertyCall(updatedTarget, PropertyAccessorKind.Set) is { } updateSetter)
        {
            // The receiver and RHS were visited once in source order. The final call
            // consumes their prepared values and adds no second evaluation or transfer.
            this.nodes[updateSetter] = info;
        }

        if (expected is not null)
        {
            this.Constrain(node, expected);
        }

        return flow;
    }

    private DefaultCompletion VisitDefault(FunctionKoto function, int parameterIndex)
    {
        var parameter = function.Parameters[parameterIndex];
        var value = parameter.DefaultValue!;
        if (this.defaultCompletions.TryGetValue(value, out var cached))
        {
            if (this.activeDefaults.Contains(value))
            {
                this.recursiveDefaults.Add(value);
            }

            return cached;
        }

        // A recursive default uses its resolved signature as an ordinary recursive call does. Completion is possible,
        // not guaranteed; generation uses a separate evaluator entry instead of expanding the declaration without bound.
        this.defaultCompletions[value] = new(true, false);
        if (this.HasInvalidDirective(value))
        {
            return this.defaultCompletions[value] = new(true, true);
        }

        this.activeDefaults.Add(value);
        var parameterType = this.types.GetDeclaredType(parameter.Type);
        this.defaultDepth++;
        var flow = this.Visit(value, parameterType);
        if (parameterType is not null)
        {
            this.CheckCompatibility(new(value, flow.Type), parameterType);
        }

        // Transfers belong to the declaration's internal targets, never the caller.
        var completion = new DefaultCompletion(flow.Normal, flow.Pending);
        this.activeDefaults.Remove(value);
        this.defaultCompletions[value] = completion;
        if (--this.defaultDepth == 0)
        {
            // The bodies of the closures that the defaults create, after every enclosing default's completion is recorded.
            while (this.deferredClosures.Count > 0)
            {
                var closure = this.deferredClosures[^1];
                this.deferredClosures.RemoveAt(this.deferredClosures.Count - 1);
                this.VisitFunctionBody(closure);
                this.CreatedClosure(closure);
            }
        }

        return completion;
    }

    private void VisitFunctionBody(FunctionKoto function)
        => this.VisitFunction(
            function,
            function.Body ?? function.ExpressionBody,
            function.ReturnType,
            KotoHelper.DiscardsFunctionBody(function) ? ControlFlowType.Unit : this.types.GetExpectedResultType(function));

    // Creation does not execute the body.
    private Flow CreatedClosure(FunctionKoto function)
    {
        var closureType = this.types.GetExpressionType(function);
        var info = this.NodeInfo(function);
        info.ExpressionType = closureType;
        info.CanCompleteNormally = true;
        return new(true, closureType);
    }

    private void CheckUnsafePermission(Koto node)
    {
        if (!KotoHelper.IsUnsafeContext(node))
        {
            this.Error(node, DiagnosticCode.UnsafeBlockRequired_Kd);
        }
        else
        {
            this.unsafeUsed = true;
        }
    }

    // SPEC 14.3.3, 23.3.6.9: the warning is at the unsafe keyword. Removing the block keeps the statements' meaning only when its Body,
    // an independent scope (SPEC 14.3.1), declares no Name and registers no defer (Structure); a statement of an indented body whose
    // lines allow the edit then offers Repair.RemoveUnsafe, and otherwise the Advice says what would change or what is left to the author.
    private void WarnUnnecessaryUnsafe(UnsafeBlockKoto block)
    {
        if (!this.warningNodes.Add(block))
        {
            return;
        }

        var scoped = false;
        if (block.Body is CodeBlockKoto { IsExpressionBody: false } body)
        {
            foreach (var item in body.Items)
            {
                scoped |= item is FieldKoto or FunctionKoto or DeferredBlockKoto or DeclarationContainerKoto;
            }
        }

        string? advice = null;
        DiagnosticRepairFact[]? repairs = null;
        if (scoped)
        {
            advice = "Removing 'unsafe' would merge its Body's declarations or defer registrations into the enclosing scope, changing where Names are visible or when values are destroyed and defer bodies run; keep the block or restructure the code deliberately";
        }
        else if (block.Parent is not CodeBlockKoto { IsExpressionBody: false })
        {
            advice = "The Body declares no Name and registers no defer, but the block is not a statement of an indented body, so removing 'unsafe' may change what the enclosing expression evaluates to; keep the block or rewrite the enclosing expression";
        }
        else if (RemoveUnsafeEdits(block) is { } edits)
        {
            repairs = [new(RepairKind.RemoveUnsafe, null, edits, RepairConditionSet.Structure)];
        }
        else
        {
            advice = "Remove 'unsafe' and keep the statements; the Body declares no Name and registers no defer, so their meaning does not change";
        }

        this.warnings.Add(new(block, DiagnosticCode.UnnecessaryUnsafeBlock_Kd) { Span = new(block.Span.Start, Constants.UnsafeKeyword.Length), Advice = advice, Repairs = repairs });
    }

    private void VisitDeclarations(Koto container)
    {
        var start = this.childBuffer.Count;
        container.VisitChildren(this.childCollector);
        var end = this.childBuffer.Count;
        for (var i = start; i < end; i++)
        {
            this.Visit(this.childBuffer[i]);
        }

        this.childBuffer.RemoveRange(start, end - start);
    }

    private Flow VisitChildSequence(Koto node)
    {
        var start = this.childBuffer.Count;
        StructuralCompletion.CollectEvaluationChildren(node, this.childBuffer, this.childCollector);
        var count = this.childBuffer.Count - start;
        var flow = this.VisitSequence(this.childBuffer, start, count);
        this.childBuffer.RemoveRange(start, count);
        return flow;
    }

    private Flow VisitSequence(IReadOnlyList<Koto> items, int start, int count)
    {
        var normal = true;
        var pendingCompletion = false;
        HashSet<JumpKoto>? transfers = null;
        List<DeferredBlockKoto>? registrations = null;
        for (var i = start; i < start + count; i++)
        {
            var item = items[i];
            var flow = this.Visit(item);
            if (normal)
            {
                if (item is DeferredBlockKoto deferred)
                {
                    (registrations ??= this.RentRegistrations()).Add(deferred);
                }

                var departing = flow.Transfers;
                if (departing is { Count: > 0 })
                {
                    var cleanup = this.Cleanup(registrations);
                    pendingCompletion |= cleanup.Pending;
                    if (!cleanup.Normal)
                    {
                        departing = null;
                    }
                }

                transfers = Union(transfers, departing);
                normal = flow.Normal;
                pendingCompletion |= flow.Pending;
            }
        }

        if (normal)
        {
            var cleanup = this.Cleanup(registrations);
            normal = cleanup.Normal;
            pendingCompletion |= cleanup.Pending;
        }

        return new(normal, ControlFlowType.Unit, transfers, pendingCompletion);
    }

    private Flow Cleanup(List<DeferredBlockKoto>? registrations)
    {
        var pendingCompletion = false;
        if (registrations is not null)
        {
            for (var i = registrations.Count - 1; i >= 0; i--)
            {
                var flow = this.cleanups[registrations[i]];
                pendingCompletion |= flow.Pending;
                if (!flow.Normal)
                {
                    return new(false, null, Pending: pendingCompletion);
                }
            }
        }

        return new(true, null, Pending: pendingCompletion);
    }

    private Flow VisitBodyItem(Koto item, ControlFlowType? expected = null)
    {
        var flow = this.Visit(item, expected);
        if (item is DeferredBlockKoto deferred)
        {
            flow = this.cleanups[deferred] with { Type = ControlFlowType.Unit };
        }

        return flow;
    }

    private void VisitFunction(Koto node, Koto? body, Koto? declaration, ControlFlowType? expected, bool unresolvedContract = false)
    {
        if (body is null || this.HasInvalidDirective(body))
        {
            return;
        }

        var boundary = this.Begin(node, this.types.GetDeclaredType(declaration) ?? expected);
        if ((declaration is not null || unresolvedContract) && boundary.Expected is null)
        {
            boundary.InferenceBlocked = true;
            this.pending.Add(node);
        }

        var discards = !KotoHelper.IsBodyExpression(body) || boundary.Expected == ControlFlowType.Unit;
        var baseFlow = node is FunctionKoto { BaseInitializer: { } initializer } ? this.Visit(initializer, null) : new Flow(true, ControlFlowType.Unit);
        var flow = this.VisitBodyItem(body, discards ? null : boundary.Expected);
        flow = baseFlow.Normal ? flow with { Transfers = Union(baseFlow.Transfers, flow.Transfers), Pending = baseFlow.Pending || flow.Pending } : baseFlow;
        if (!discards || (!flow.Pending && this.structural.CanComplete(body)))
        {
            boundary.Sources.Add(new(body, discards ? ControlFlowType.Unit : flow.Type) { IsFallthrough = discards && body is CodeBlockKoto });
        }

        if (discards && flow.Pending)
        {
            boundary.InferenceBlocked = true;
        }

        var finished = this.Finish(node, flow, boundary);
        var info = this.nodes[node];
        if (body is CodeBlockKoto && this.structural.CanComplete(body) && !flow.Pending &&
            info.TargetResultType is { } resultType && resultType != ControlFlowType.Unit)
        {
            this.Error(body, DiagnosticCode.FunctionFallthrough_Kd);
        }

        // The function's callable return type falls back to Never, while its target contract remains absent.
        info.FunctionResultType = boundary.Expected ?? info.TargetResultType ?? finished.Type;
        info.ExpressionType = this.types.GetExpressionType(node);
    }

    /// <summary>Determines whether a body contains an unselected directive group, excluding nested function bodies.</summary>
    private bool HasInvalidDirective(Koto node)
    {
        if (node is CompileTimeSwitchKoto)
        {
            return true;
        }

        var start = this.childBuffer.Count;
        node.VisitChildren(this.childCollector);
        var end = this.childBuffer.Count;
        var found = false;
        for (var i = start; i < end && !found; i++)
        {
            var child = this.childBuffer[i];
            found = child is not (FunctionKoto or PropertyAccessorKoto) && this.HasInvalidDirective(child);
        }

        this.childBuffer.RemoveRange(start, end - start);
        return found;
    }

    private Boundary Begin(Koto node, ControlFlowType? expected)
    {
        if (this.boundaryCursor == this.boundaryPool.Count)
        {
            this.boundaryPool.Add(new(null));
        }

        var boundary = this.boundaryPool[this.boundaryCursor++];
        boundary.Expected = expected;
        boundary.InferenceBlocked = boundary.InvalidResult = false;
        boundary.Sources.Clear();
        this.boundaries[node] = boundary;
        var info = this.RentInfo();
        info.IsResultRequiring = KotoHelper.IsResultRequiringSelection(node);
        this.nodes[node] = info;
        return boundary;
    }

    private Flow VisitJump(JumpKoto jump)
    {
        var target = KotoHelper.ResolveTransferTarget(jump);
        this.targets[jump] = target;
        if (target is null)
        {
            this.Error(jump, DiagnosticCode.InvalidJumpTarget_Kd, jump.Keyword);
        }

        if (jump is YieldKoto { Label: null } && target is IfKoto or MatchKoto && !KotoHelper.IsValueContext(target))
        {
            this.Error(jump, DiagnosticCode.UnlabeledYieldTarget_Kd);
        }

        this.boundaries.TryGetValue(target ?? jump, out var boundary);
        // A jump that failed Binding has reported its value's mismatch; its value is not checked against the target again.
        var operand = jump.Expression is { } expression ? this.Visit(expression, jump.BindingState == BindingState.Invalid ? null : boundary?.Expected) : new Flow(true, ControlFlowType.Unit);
        if (jump is not ContinueKoto && boundary is not null)
        {
            var source = new ControlFlowResultSource(jump.Expression ?? jump, operand.Type) { Transfer = jump };
            boundary.Sources.Add(source);
        }

        var transfers = operand.Transfers;
        if (operand.Normal)
        {
            (transfers ??= this.RentTransfers()).Add(jump);
        }

        return new(false, ControlFlowType.Never, transfers, operand.Pending);
    }

    /// <summary>Analyzes a require statement, which is transparent to transfer lookup and is not a selection.</summary>
    private Flow VisitRequire(RequireKoto node)
    {
        var condition = this.Visit(node.Condition, ControlFlowType.Boolean);

        // Entry to the failure body is assumed independently of the condition, even for literal true (SPEC 14.11.2).
        var failure = this.VisitBodyItem(node.ElseBody);
        if (failure.Normal)
        {
            if (failure.Pending)
            {
                this.pending.Add(node);
            }
            else
            {
                this.Error(node.ElseBody, DiagnosticCode.RequireFallthrough_Kd);
            }
        }

        // Both condition outcomes remain static paths, including Boolean literals.
        var transfers = Union(condition.Transfers, condition.Normal ? failure.Transfers : null);
        var normal = condition.Normal;
        return new(normal, ControlFlowType.Unit, transfers, condition.Pending || (condition.Normal && failure.Pending));
    }

    private Flow VisitIf(IfKoto node, ControlFlowType? expected)
    {
        var required = KotoHelper.IsValueContext(node);
        var boundary = this.Begin(node, required ? expected : ControlFlowType.Unit);
        var next = true;
        var normal = false;
        var pendingCompletion = false;
        HashSet<JumpKoto>? transfers = null;
        for (var index = 0; index < node.Branches.Count; index++)
        {
            var branch = node.Branches[index];
            var condition = this.Visit(branch.Condition, ControlFlowType.Boolean);
            if (next)
            {
                transfers = Union(transfers, condition.Transfers);
                pendingCompletion |= condition.Pending;
            }

            var chosen = next && condition.Normal;
            var body = this.VisitBranch(branch.Body, branch.Body.IsExpressionBody, required, boundary);
            if (chosen)
            {
                normal |= body.Normal;
                pendingCompletion |= body.Pending;
                transfers = Union(transfers, body.Transfers);
            }

            next &= condition.Normal;
        }

        if (node.ElseBody is { } otherwise)
        {
            var body = this.VisitBranch(otherwise, otherwise.IsExpressionBody, required, boundary);
            if (next)
            {
                normal |= body.Normal;
                pendingCompletion |= body.Pending;
                transfers = Union(transfers, body.Transfers);
            }
        }
        else
        {
            normal |= next;
            boundary.Sources.Add(new(node, ControlFlowType.Unit));
        }

        return this.Finish(node, new(normal, null, transfers, pendingCompletion), boundary);
    }

    private Flow VisitMatch(MatchKoto node, ControlFlowType? expected)
    {
        var subject = this.Visit(node.Expression);
        var required = KotoHelper.IsResultRequiringSelection(node);
        var boundary = this.Begin(node, required ? expected : ControlFlowType.Unit);
        var coverage = this.types.GetMatchCoverage(node, subject.Type);
        var exhaustive = coverage.IsExhaustive;

        if (exhaustive is null)
        {
            this.pending.Add(node);
        }
        else if (exhaustive == false)
        {
            boundary.InvalidResult = true;
            this.Error(node, DiagnosticCode.NonExhaustiveMatch_Kd, evidence: [coverage.Requirement], note: coverage.Describe());
        }

        var normal = exhaustive != true;
        var pendingCompletion = subject.Pending || exhaustive is null;
        var transfers = subject.Transfers;
        for (var index = 0; index < node.Arms.Count; index++)
        {
            var arm = node.Arms[index];
            var guardNormal = true;
            if (arm.Guard is { } guard)
            {
                // Visit even checked-only guards, but propagate transfers only after
                // normally completing Subject evaluation.
                var guardFlow = this.Visit(guard, ControlFlowType.Boolean);
                this.CheckCompatibility(new(guard, guardFlow.Type), ControlFlowType.Boolean);
                guardNormal = guardFlow.Normal;
                if (subject.Normal)
                {
                    transfers = Union(transfers, guardFlow.Transfers);
                }

                pendingCompletion |= subject.Normal && guardFlow.Pending;
            }

            var body = this.VisitBranch(arm.Body, arm.Body is not CodeBlockKoto, required, boundary);
            normal |= guardNormal && body.Normal;
            pendingCompletion |= subject.Normal && guardNormal && body.Pending;
            if (subject.Normal && guardNormal)
            {
                transfers = Union(transfers, body.Transfers);
            }
        }

        if (exhaustive != true && !required)
        {
            boundary.Sources.Add(new(node, ControlFlowType.Unit));
        }

        return this.Finish(node, new(subject.Normal && normal, null, transfers, pendingCompletion), boundary);
    }

    private Flow VisitBranch(Koto body, bool expressionBody, bool required, Boundary boundary)
    {
        var expression = body is CodeBlockKoto { IsExpressionBody: true } block ? block.Items[0] : body;
        var usesValue = expressionBody && required && KotoHelper.IsBodyExpression(expression);
        var flow = this.VisitBodyItem(expression, usesValue ? boundary.Expected : null);
        if (usesValue || (!flow.Pending && this.structural.CanComplete(expression)))
        {
            boundary.Sources.Add(new(expression, usesValue ? flow.Type : ControlFlowType.Unit));
        }

        if (!usesValue && flow.Pending)
        {
            boundary.InferenceBlocked = true;
            this.pending.Add(body);
        }

        if (body != expression)
        {
            this.nodes[body] = this.NodeInfo(expression);
        }

        return flow;
    }

    private Flow VisitIteration(Koto node, ControlFlowType? expected)
    {
        Flow header;
        CodeBlockKoto body;
        var mayFinish = false;
        switch (node)
        {
            case ForKoto f:
                header = this.Visit(f.Iteration is { Decomposition.IsCurrent: true } ? f.EntryCall! : f.Iterable);
                if (f.Iteration is { Decomposition.IsCurrent: true } protocol)
                {
                    this.Visit(protocol.Next);
                }

                body = f.Body;
                mayFinish = true;
                break;
            case WhileKoto w:
                if (KotoHelper.UnwrapParentheses(w.Condition) is BoolLiteralKoto { Value: true })
                {
                    this.Warn(w, DiagnosticCode.StaticWhileTrue_Kd);
                }

                header = this.Visit(w.Condition, ControlFlowType.Boolean);
                body = w.Body;
                mayFinish = true;
                break;
            default:
                header = new(true, ControlFlowType.Unit);
                body = ((LoopKoto)node).Body;
                break;
        }

        var boundary = this.Begin(node, node is LoopKoto && KotoHelper.IsValueContext(node) ? expected : ControlFlowType.Unit);
        var bodyFlow = this.Visit(body);
        if (mayFinish && (node is ForKoto iteration ? this.structural.CanComplete(iteration.Iterable) : this.structural.CanComplete(((WhileKoto)node).Condition)))
        {
            boundary.Sources.Add(new(node, ControlFlowType.Unit));
        }

        var transfers = Union(header.Transfers, header.Normal ? bodyFlow.Transfers : null);
        return this.Finish(node, new(header.Normal && mayFinish, null, transfers, header.Pending || bodyFlow.Pending), boundary);
    }

    private Flow VisitDo(DoKoto node, ControlFlowType? expected)
    {
        var required = KotoHelper.IsValueContext(node);
        var boundary = this.Begin(node, required ? expected : ControlFlowType.Unit);
        this.nodes[node].IsResultRequiring = required;
        var flow = this.VisitBranch(node.Body, node.Body.IsExpressionBody, required, boundary);
        return this.Finish(node, flow, boundary);
    }

    private Flow Finish(Koto node, Flow flow, Boundary boundary)
    {
        if (flow.Pending)
        {
            this.pending.Add(node);
        }

        if (flow.Transfers is { Count: > 0 } transfers)
        {
            // Collect first: a HashSet cannot be modified while it is enumerated.
            var arrived = this.arrivedTransfers;
            foreach (var jump in transfers)
            {
                if (this.targets.GetValueOrDefault(jump) == node)
                {
                    arrived.Add(jump);
                }
            }

            foreach (var jump in arrived)
            {
                this.normalTransferArrivals.Add(jump);
                flow = flow with { Normal = flow.Normal || jump is not ContinueKoto };
                transfers.Remove(jump);
            }

            arrived.Clear();
        }

        var candidates = this.candidateLists.TryPop(out var list) ? list : new();
        var allNull = true;
        foreach (var source in boundary.Sources)
        {
            candidates.Add(source);
            allNull &= IsNullLiteral(source.Node);
        }

        var targetType = boundary.Expected ?? (boundary.InferenceBlocked ? null : this.types.InferResultType(candidates));
        var candidateCount = candidates.Count;
        candidates.Clear();
        this.candidateLists.Push(candidates);

        var info = this.nodes[node];
        info.TargetResultType = targetType;
        info.CanCompleteNormally = flow.Normal;
        var neverSources = true;
        foreach (var source in boundary.Sources)
        {
            neverSources &= source.Type == ControlFlowType.Never;
        }

        var structuralNormal = node is FunctionKoto function ? this.structural.CanComplete(function.Body ?? function.ExpressionBody!) :
            node is PropertyAccessorKoto accessor ? this.structural.CanComplete(accessor.Body!) : this.structural.CanComplete(node);
        info.ExpressionType = boundary.InvalidResult ? null : neverSources && !structuralNormal ? ControlFlowType.Never : targetType;
        if (candidateCount > 0 && targetType is null && !neverSources)
        {
            this.pending.Add(node);
            if (!boundary.InferenceBlocked && allNull)
            {
                this.Error(node, DiagnosticCode.UntypedNull_Kd);
            }
        }

        if (targetType is not null)
        {
            this.CheckSources(node, boundary, targetType);
        }

        if (boundary.Expected is null && targetType == ControlFlowType.Unit)
        {
            if (node is DoKoto scoped)
            {
                this.WarnUnitTail(scoped.Body, scoped.Body);
            }
            else if (node is IfKoto or MatchKoto)
            {
                this.WarnUnitTail(node, node);
            }
            else if (node is FunctionKoto { IsAnonymous: true, Body: { } body })
            {
                this.WarnUnitTail(body, body);
            }
        }

        return flow with { Type = info.ExpressionType };
    }

    private void CheckSources(Koto target, Boundary boundary, ControlFlowType type)
    {
        foreach (var source in boundary.Sources)
        {
            if (source.Transfer is { BindingState: BindingState.Invalid } || source.IsFallthrough)
            {
                // Binding reported the transferred value's mismatch, and a non-Unit function's fallthrough is reported as
                // such; a result error would repeat either.
                continue;
            }

            // Propagate a later inferred contract into nested Never expressions as well.
            if (source.Node != target && source.Node is not JumpKoto && source.Type != ControlFlowType.Unit)
            {
                this.Constrain(source.Node, type);
            }

            this.CheckCompatibility(source, type);
        }
    }

    private void Constrain(Koto node, ControlFlowType type)
    {
        if (node is NullLiteralKoto && IsPointer(type))
        {
            this.pending.Remove(node);
            this.nodes[node].ExpressionType = type;
        }

        if (node is ParenthesizedKoto p)
        {
            this.Constrain(p.Operand, type);
            if (IsNullLiteral(p))
            {
                this.nodes[p].ExpressionType = this.nodes[p.Operand].ExpressionType;
            }
        }
        else if (node is LabeledKoto labeled)
        {
            this.Constrain(labeled.Target, type);
        }

        if (this.boundaries.TryGetValue(node, out var boundary))
        {
            boundary.Expected ??= type;
            this.nodes[node].TargetResultType ??= type;
            this.CheckSources(node, boundary, boundary.Expected);
        }

        if (this.nodes.TryGetValue(node, out var info))
        {
            this.CheckCompatibility(new(node, info.ExpressionType), type);
        }
    }

    private void CheckCompatibility(ControlFlowResultSource source, ControlFlowType type)
    {
        var compatible = this.types.IsCompatible(source, type);
        if (compatible == false)
        {
            if (KotoHelper.UnwrapParentheses(source.Node) is TryKoto propagation)
            {
                var (evidence, note) = Binding.TryPayloadFacts(propagation, type);
                this.Error(source.Node, DiagnosticCode.TryPayloadMismatch_Kd, evidence: evidence, note: note);
            }
            else if (source.Type is null)
            {
                this.Error(source.Node, DiagnosticCode.UntypedNull_Kd); // Only a null literal is judged without its own Type.
            }
            else if (this.types.OriginRelation(source, type) is { } relation)
            {
                // SPEC 15.6.1: a result whose structural part fits fails only an Origin relation, never as an incompatible Type.
                var code = Binding.OriginRelationCode(relation);
                var (evidence, advice, related) = Binding.OriginRelationFacts(relation, "fit");
                if (this.reported.Add((source.Node, code)))
                {
                    this.issues.Add(new(source.Node, code) { Evidence = evidence, Advice = advice, Related = related });
                }
            }
            else
            {
                this.Error(source.Node, DiagnosticCode.IncompatibleResult_Kd, source.Type.Name, type.Name);
            }
        }
        else if (compatible is null)
        {
            this.pending.Add(source.Node);
        }
    }

    private ControlFlowType? ResolveNameType(IdentifierNameKoto name)
    {
        Koto child = name;
        for (var parent = child.Parent; parent is not null; child = parent, parent = parent.Parent)
        {
            if (parent is CodeBlockKoto block)
            {
                for (var index = 0; index < block.Items.Count; index++)
                {
                    var item = block.Items[index];
                    if (item == child)
                    {
                        break;
                    }

                    if (item is FieldKoto field && field.NameKoto.IdentifierName == name.IdentifierName)
                    {
                        return this.names.GetValueOrDefault(field.NameKoto);
                    }
                }
            }

            if (parent is FunctionKoto function)
            {
                for (var index = 0; index < function.Parameters.Count; index++)
                {
                    var parameter = function.Parameters[index];
                    if (parameter.InternalName == name.IdentifierName)
                    {
                        return this.types.GetDeclaredType(parameter.Type);
                    }
                }
            }
        }

        this.pending.Add(name);
        return null;
    }

    private ControlFlowType PointeeType(ControlFlowType pointer)
    {
        if (pointer is BoundType bound)
        {
            return bound.Components[0];
        }

        if (!this.pointeeTypes.TryGetValue(pointer.Name, out var pointee))
        {
            pointee = new(pointer.Name[PointerPrefix.Length..]);
            this.pointeeTypes.Add(pointer.Name, pointee);
        }

        return pointee;
    }

    private ControlFlowType? InferLocalType(Koto node)
    {
        if (node is UnaryKoto unary && this.nodes.TryGetValue(unary.Operand, out var operand))
        {
            var operandType = operand.ExpressionType;
            var rawPointer = IsPointer(operandType);
            if (node is DereferenceKoto)
            {
                if (rawPointer)
                {
                    return this.PointeeType(operandType!);
                }

                if (operandType is not null && operandType != ControlFlowType.Never)
                {
                    this.Error(node, DiagnosticCode.InvalidDereference_Kd);
                }

                return null;
            }

            if (rawPointer && node is PrefixPlusKoto or PrefixMinusKoto or PrefixPlusPlusKoto or PrefixMinusMinusKoto or PostfixIncrementKoto or PostfixDecrementKoto)
            {
                this.Error(node, DiagnosticCode.InvalidPointerArithmetic_Kd);
                return null;
            }

            if (node is NotKoto)
            {
                this.Constrain(unary.Operand, ControlFlowType.Boolean);
                return ControlFlowType.Boolean;
            }

            // SPEC 13.2: Binding rejects a non-numeric operand (NonNumericOperand_Kd); the result has the operand's Type.
            if (node is PrefixMinusKoto or PrefixPlusKoto)
            {
                return operandType;
            }

            return null;
        }

        if (node is IsKoto { IsRuntimeTest: true })
        {
            return ControlFlowType.Boolean;
        }

        if (node is BinaryKoto binary && this.nodes.TryGetValue(binary.Left, out var left) && this.nodes.TryGetValue(binary.Right, out var right))
        {
            var leftPointer = IsPointer(left.ExpressionType);
            var rightPointer = IsPointer(right.ExpressionType);
            if (leftPointer || rightPointer)
            {
                if (node is EqualsEqualsKoto or ExclamationEqualsKoto)
                {
                    var pointerType = leftPointer ? left.ExpressionType! : right.ExpressionType!;
                    this.Constrain(leftPointer ? binary.Right : binary.Left, pointerType);
                    var otherType = leftPointer ? right.ExpressionType : left.ExpressionType;
                    if (otherType is not null && otherType != pointerType && otherType != ControlFlowType.Never)
                    {
                        this.Error(node, DiagnosticCode.InvalidPointerComparison_Kd);
                    }

                    return ControlFlowType.Boolean;
                }

                if (node is PlusKoto or MinusKoto or IndexKoto)
                {
                    this.CheckUnsafePermission(node);
                    if (!leftPointer || rightPointer || KotoHelper.UnwrapParentheses(binary.Right) is FromEndIndexKoto or RangeKoto)
                    {
                        this.Error(node, DiagnosticCode.InvalidPointerArithmetic_Kd);
                    }
                    else
                    {
                        this.Constrain(binary.Right, IsizeType);
                    }

                    return node is IndexKoto && leftPointer ? this.PointeeType(left.ExpressionType!) : left.ExpressionType;
                }

                if (node is LessThanKoto or LessThanEqualsKoto or GreaterThanKoto or GreaterThanEqualsKoto)
                {
                    this.Error(node, DiagnosticCode.InvalidPointerComparison_Kd);
                    return ControlFlowType.Boolean;
                }

                if (node is AsteriskKoto or SlashKoto or PercentKoto or LessThanLessThanKoto or GreaterThanGreaterThanKoto or
                    AmpersandKoto or BarKoto or CaretKoto or AsteriskEqualsKoto or SlashEqualsKoto or PercentEqualsKoto or
                    LessThanLessThanEqualsKoto or GreaterThanGreaterThanEqualsKoto or AmpersandEqualsKoto or BarEqualsKoto or CaretEqualsKoto)
                {
                    this.Error(node, DiagnosticCode.InvalidPointerArithmetic_Kd);
                    return null;
                }
            }

            if (node is IndexKoto)
            {
                if (binary.BindingState != BindingState.Invalid)
                {
                    this.Constrain(binary.Right, IsizeType); // A failed index reported its own diagnostic.
                }

                return null; // Complete element Types come from Binding, never from the receiver Type.
            }

            if (node is MemberAccessKoto or AsKoto or IsKoto)
            {
                return null;
            }

            // Shift results come only from the left. Count Types are independent (SPEC 13.3).
            if (node is LessThanLessThanKoto or GreaterThanGreaterThanKoto or LessThanLessThanEqualsKoto or GreaterThanGreaterThanEqualsKoto)
            {
                return node is LessThanLessThanEqualsKoto or GreaterThanGreaterThanEqualsKoto ? ControlFlowType.Unit : left.ExpressionType;
            }

            // A binary that failed Binding has reported its operand mismatch; a dependent result error would repeat it.
            if (left.ExpressionType is { } type && binary.BindingState != BindingState.Invalid)
            {
                this.Constrain(binary.Right, type);
            }

            if (right.ExpressionType is { } rightType && left.ExpressionType is null or { Name: "Never" })
            {
                this.Constrain(binary.Left, rightType);
            }

            if (node is EqualsEqualsKoto or ExclamationEqualsKoto or LessThanKoto or LessThanEqualsKoto or GreaterThanKoto or GreaterThanEqualsKoto)
            {
                return ControlFlowType.Boolean;
            }

            // SPEC 13.3: Binding rejects a non-numeric operand (NonNumericOperand_Kd); the result has the left operand's Type.
            if (node is PlusKoto or MinusKoto or AsteriskKoto or SlashKoto or PercentKoto or AmpersandKoto or BarKoto or CaretKoto)
            {
                return left.ExpressionType;
            }

            return null;
        }

        return null;
    }

    private void CheckLabel(LabeledKoto label)
    {
        for (var parent = label.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is FunctionKoto or PropertyAccessorKoto)
            {
                break;
            }

            if (parent is LabeledKoto outer && outer.Label == label.Label && KotoHelper.IsInsideLabeledBody(label, outer))
            {
                this.Error(label, DiagnosticCode.OverlappingLabel_Kd, label.Label);
                break;
            }
        }
    }

    private sealed class Boundary(ControlFlowType? expected)
    {
        public ControlFlowType? Expected { get; set; } = expected;

        public bool InferenceBlocked { get; set; }

        public bool InvalidResult { get; set; }

        public List<ControlFlowResultSource> Sources { get; } = new();
    }

    /// <summary>Appends direct children without descending, so traversal needs no iterator allocations.</summary>
    private sealed class ChildCollector(List<Koto> children) : KotoVisitor
    {
        public override void Visit(Koto node) => children.Add(node);
    }

    private sealed class MultiLineLiteralFinder(SourceDocument document) : KotoVisitor
    {
        public bool Found { get; private set; }

        public override void Visit(Koto node)
        {
            if (this.Found)
            {
                return;
            }

            if (node is StringLiteralKoto or InterpolatedStringKoto && document.SourceText.AsSpan(node.Span.Start, node.Span.Length).Contains('\n'))
            {
                this.Found = true;
                return;
            }

            base.Visit(node);
        }
    }

    private readonly record struct DefaultCompletion(bool Normal, bool Pending);

    private readonly record struct Flow(bool Normal, ControlFlowType? Type, HashSet<JumpKoto>? Transfers = null, bool Pending = false);
}
