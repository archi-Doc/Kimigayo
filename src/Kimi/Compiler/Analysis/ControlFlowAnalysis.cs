// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // Analysis result and its diagnostic belong together.

/// <summary>A control-flow diagnostic associated with source syntax.</summary>
/// <param name="Node">The offending syntax.</param>
/// <param name="Code">The code of the requirement.</param>
/// <param name="Argument">The message argument.</param>
public sealed record ControlFlowIssue(Koto Node, DiagnosticCode Code, object? Argument = null)
{
    /// <summary>Gets the part of the node that is the primary location, when it is not the whole node.</summary>
    public SourceSpan? Span { get; init; }

    /// <summary>Gets the message formatted from the catalog.</summary>
    public string Message => DiagnosticEntries.TryGet(this.Code, out var entry) ? entry.FormatMessage(this.Argument, null) : this.Code.ToString();

    /// <summary>Gets the repair candidates the warning offers (SPEC 23.3.6.9).</summary>
    internal DiagnosticRepairFact[]? Repairs { get; init; }

    internal int Priority { get; init; } = 4;

    /// <summary>Gets the syntax whose normal completion, down to the node, decided a check of a discarded tail.</summary>
    internal Koto? CompletionRoot { get; init; }
}

/// <summary>The control-flow facts of an analyzed expression or boundary.</summary>
public sealed class ControlFlowNodeInfo
{
    /// <summary>Gets a value indicating whether normal completion is possible within the construct.</summary>
    public bool CanCompleteNormally { get; internal set; }
}

/// <summary>Analyzes lexical transfers, reachability, result coverage, unsafe permission and discards.</summary>
/// <remarks>
/// Run after compile-time directive selection. Bodies containing invalid directive groups are skipped.
/// Types, Never results, match coverage and selected calls are Binding's facts; a node Binding has not resolved leaves its
/// completion pending.
/// </remarks>
public sealed class ControlFlowAnalysis
{
    private readonly StructuralCompletion structural;
    private readonly Dictionary<Koto, ControlFlowNodeInfo> nodes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<JumpKoto, Koto?> targets = new(ReferenceEqualityComparer.Instance);
    private readonly List<ControlFlowIssue> issues = new();
    private readonly List<ControlFlowIssue> warnings = new();
    private readonly HashSet<Koto> warningNodes = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Koto> pending = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<(Koto Node, DiagnosticCode Code)> reported = new();
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
    private readonly List<ControlFlowNodeInfo> infoPool = new();
    private readonly List<HashSet<JumpKoto>> transferPool = new();
    private readonly List<List<DeferredBlockKoto>> registrationPool = new();

    // The Binding of the analyzed compilation, whose retained facts the checks read.
    private Binding binding = null!;
    private int infoCursor;
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

    private ControlFlowAnalysis()
    {
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
    /// <returns>The analysis results.</returns>
    public static ControlFlowAnalysis Analyze(Koto root)
    {
        var analysis = new ControlFlowAnalysis { binding = root.CodeContext.Compilation.Binding };
        analysis.Visit(root);
        return analysis;
    }

    /// <summary>Replaces these results using retained storage.</summary>
    /// <param name="root">The current bound tree.</param>
    public void Reanalyze(Koto root)
    {
        this.binding = root.CodeContext.Compilation.Binding;
        this.structural.Clear();
        this.nodes.Clear();
        this.targets.Clear();
        this.issues.Clear();
        this.warnings.Clear();
        this.warningNodes.Clear();
        this.pending.Clear();
        this.reported.Clear();
        this.cleanups.Clear();
        this.defaultCompletions.Clear();
        this.activeDefaults.Clear();
        this.recursiveDefaults.Clear();
        this.deferredClosures.Clear();
        this.defaultDepth = 0;
        this.childBuffer.Clear();
        this.arrivedTransfers.Clear();
        this.infoCursor = this.transferCursor = this.registrationCursor = 0;
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
            else if (ReadsBinding(issue.Code) && this.binding.FailureCauses(issue.Node) is { } causes)
            {
                issue.Node.ReportDerived(DiagnosticRequirement.ControlFlow, causes);
            }
            else
            {
                issue.Node.Report(DiagnosticRequirement.ControlFlow, issue.Code, issue.Argument);
            }
        }

        foreach (var warning in this.warnings)
        {
            if ((!ReadsBinding(warning.Code) || this.binding.FailureCauses(warning.Node) is null) && this.GuessedBy(warning) is null)
            {
                warning.Node.Report(DiagnosticRequirement.ControlFlow, warning.Code, warning.Argument, span: warning.Span, repairs: warning.Repairs);
            }
        }
    }

    internal void Append(Koto root) => this.Visit(root);

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

    // Jump targets, labels, fallthrough, Unsafe Blocks and while true are judged from syntax alone.
    private static bool ReadsBinding(DiagnosticCode code)
        => code is not (DiagnosticCode.InvalidJumpTarget_Kd or DiagnosticCode.UnlabeledYieldTarget_Kd or DiagnosticCode.RequireFallthrough_Kd or
            DiagnosticCode.FunctionFallthrough_Kd or DiagnosticCode.OverlappingLabel_Kd or DiagnosticCode.UnsafeBlockRequired_Kd or DiagnosticCode.StaticWhileTrue_Kd);

    // An expression Binding has not resolved leaves its completion unknown; a recovery completes as its written operands do,
    // and an Error expression is not evaluated.
    private static bool Unknown(Koto node)
        => node.BindingState != BindingState.Resolved && node is ExpressionKoto and not ErrorKoto && node.CodeContext.RecoveryCause(node) is null;

    // The result a Block body must deliver: the written result, else the bound contract; Unit for discarded bodies and setters.
    private static BoundType? DeclaredResult(Koto boundary) => boundary switch
    {
        FunctionKoto { ReturnType: { BindingState: BindingState.Resolved } written } => written.BoundType,
        FunctionKoto function => KotoHelper.DiscardsFunctionBody(function) ? BoundType.Unit : function.BoundSymbol?.Type,
        PropertyAccessorKoto { AccessorKind: PropertyAccessorKind.Set } => BoundType.Unit,
        PropertyAccessorKoto { ReturnType: { BindingState: BindingState.Resolved } written } => written.BoundType,
        PropertyAccessorKoto { Parent: PropertyKoto { BindingState: BindingState.Resolved } property } => property.BoundType,
        PropertyAccessorKoto accessor => accessor.ReturnType?.BoundType ?? accessor.BoundType,
        _ => null,
    };

    private static bool IsBoundRuntimeTypeTest(Koto expression)
        => expression is IsKoto { BindingState: BindingState.Resolved, BoundRuntimeTest: not null };

    // The function Binding selected for a function reference, including a direct call's callee.
    private static FunctionKoto? GetReferencedFunction(Koto expression)
        => expression is ExpressionKoto and not InvocationKoto && expression.BindingState == BindingState.Resolved &&
            expression.BoundSymbol is { Kind: BindingSymbolKind.Function, Declaration: FunctionKoto function } ? function : null;

    // SPEC 14.3.3: whether a bound operation requires lexical unsafe permission; null while Binding has not resolved it.
    private static bool? RequiresUnsafeContext(Koto expression)
    {
        if (expression.BindingState != BindingState.Resolved)
        {
            return null;
        }

        return expression is DereferenceKoto or NoInitKoto ||
            (expression is BinaryKoto binary && ReferenceTypes.IsPointer(binary.Left.BoundType) &&
                binary.Akind is KotoKind.Index or KotoKind.Plus or KotoKind.Minus or KotoKind.PlusEquals or KotoKind.MinusEquals) ||
            (expression is ExpressionKoto &&
            expression.BoundSymbol is { Kind: BindingSymbolKind.Function, Declaration: FunctionKoto f } && (f.Modifier & ModifierKind.Unsafe) != 0);
    }

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

    // SPEC 10.2: the Type an expression supplies is its one adaptation's, else its own; null while Binding has not resolved it.
    private BoundType? SuppliedType(Koto node)
        => node.BindingState != BindingState.Resolved ? null
            : this.binding.TryGetAdaptation(node, out var adaptation) ? adaptation.Type : node.ErasedFunctionType ?? node.BoundType;

    private bool IsNever(Koto node) => ReferenceEquals(this.SuppliedType(node), BoundType.Never);

    private bool IsKimiResult(Koto expression) => expression.BindingState == BindingState.Resolved &&
        expression.BoundType is { Kind: BoundTypeKind.Constructed } type && type.Symbol == this.binding.Library.Result;

    private bool IsBoundConstruction(Koto expression) => this.binding.TryGetEnumConstruction(expression, out _);

    // Whether a discarded expression's complete Type is proven Copy, so its destruction is not observable.
    private bool IsProvenCopy(Koto expression)
        => expression.BindingState == BindingState.Resolved && expression.BoundType is { } type && this.binding.ProveCopy(type, expression) == ConstraintProof.Proven;

    // A committed direct call and its receiver, evaluated before the explicit arguments; false while its evaluation is unknown.
    private bool TryGetCallReceiver(InvocationKoto call, out Koto? receiver)
    {
        if (this.IsBoundConstruction(call))
        {
            receiver = null;
            return true;
        }

        var bound = call.BoundCall;
        receiver = bound?.Receiver ?? call.BoundValueCall?.CalleeValue;
        return bound is not null || call.BoundValueCall is not null;
    }

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
    // `_ =` puts the expression in Value Context (SPEC 14.2.4) and changes no scope, so no condition is relevant.
    private void WarnDiscard(Koto node, DiagnosticCode code, int priority)
    {
        node = KotoHelper.UnwrapParentheses(node);
        if (!this.warningNodes.Add(node))
        {
            return;
        }

        DiagnosticRepairFact[]? repairs = null;
        if (node.Parent is CodeBlockKoto { IsExpressionBody: false })
        {
            var start = new SourceSpan(node.Span.Start, 0);
            var discard = new DiagnosticRepairFact(RepairKind.ExplicitDiscard, null, [node.Edit(start, "_ = ")], RepairConditionSet.None);
            repairs = code == DiagnosticCode.DiscardedResult_Kd && FailureTargetFits(node)
                ? [new(RepairKind.PropagateFailure, null, [node.Edit(start, "_ = try ")], RepairConditionSet.None), discard]
                : [discard];
        }

        this.warnings.Add(new(node, code) { Priority = priority, Repairs = repairs });
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
            return IsBoundRuntimeTypeTest(test) && KotoHelper.UnwrapParentheses(test.Left) is
                IdentifierNameKoto { BoundSymbol.Kind: BindingSymbolKind.Local or BindingSymbolKind.Parameter };
        }

        // Tuples and Case constructions are effect-free when every nested operand is (SPEC 17.4.2).
        // A Case construction must also have a proven Copy Type, so its destruction is not observable.
        if (node is TupleLiteralKoto tuple)
        {
            return this.AreEffectFree(tuple.Elements);
        }

        if (this.IsBoundConstruction(node))
        {
            return this.IsProvenCopy(node) && (node is not InvocationKoto call || this.AreEffectFree(call.ArgumentNodes));
        }

        var primitive = this.SuppliedType(node)?.Underlying.Name is "bool" or "char" or
            "i8" or "i16" or "i32" or "i64" or "i128" or "u8" or "u16" or "u32" or "u64" or "u128" or "isize" or "usize" or "f32" or "f64";
        if (!primitive)
        {
            return false;
        }

        return node switch
        {
            NumberLiteralKoto or BoolLiteralKoto or CharLiteralKoto => true,
            IdentifierNameKoto { BoundSymbol.Kind: BindingSymbolKind.Local or BindingSymbolKind.Parameter } => true,
            UnaryKoto { Akind: KotoKind.Not } unary => this.IsEffectFree(unary.Operand),
            BinaryKoto { Akind: KotoKind.EqualsEquals or KotoKind.ExclamationEquals or KotoKind.LessThan or KotoKind.LessThanEquals or KotoKind.GreaterThan or KotoKind.GreaterThanEquals } comparison =>
                this.IsEffectFree(comparison.Left) && this.IsEffectFree(comparison.Right),
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
            this.SuppliedType(body) is { } type && !ReferenceEquals(type, BoundType.Unit) && !ReferenceEquals(type, BoundType.Never))
        {
            this.Warn(body, DiagnosticCode.DiscardedTail_Kd, 1, root);
        }
    }

    private void Error(Koto node, DiagnosticCode code, object? argument = null)
    {
        if (this.reported.Add((node, code)))
        {
            this.issues.Add(new(node, code, argument));
        }
    }

    private ControlFlowNodeInfo RentInfo()
    {
        if (this.infoCursor == this.infoPool.Count)
        {
            this.infoPool.Add(new());
        }

        var info = this.infoPool[this.infoCursor++];
        info.CanCompleteNormally = false;
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

    private Flow Visit(Koto node)
    {
        if (node is FunctionKoto && TestDefinition.Marker(node) is not null && !TestDefinition.IsIncluded(node))
        {
            return new(true);
        }

        if (node is ExpressionKoto && !KotoHelper.IsValueContext(node) && node.Parent is not ParenthesizedKoto)
        {
            var value = KotoHelper.UnwrapParentheses(node);
            if (this.IsKimiResult(value))
            {
                this.WarnDiscard(node, DiagnosticCode.DiscardedResult_Kd, 2);
            }
            else if (value is TryKoto && this.SuppliedType(value) is { } type && !ReferenceEquals(type, BoundType.Unit) && !ReferenceEquals(type, BoundType.Never))
            {
                this.WarnDiscard(node, DiagnosticCode.UnusedTrySuccess_Kd, 3);
            }
            else if (this.IsEffectFree(node))
            {
                this.Warn(node, DiagnosticCode.DiscardedValue_Kd);
            }
        }

        var referencedFunction = GetReferencedFunction(node);
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

        var requiresUnsafe = RequiresUnsafeContext(node);
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
                flow = this.Visit(creation);
                break;
            case ConversionKoto { Adaptation.Creates: true, CreationStorage: { } conditionalCreation }:
                // Every case evaluates the same input once. Register the retained invocation's completion so a closed
                // creation case can lower it; an identity case uses only that input and never executes the invocation.
                flow = this.Visit(conditionalCreation);
                break;
            case BinaryKoto { Akind: KotoKind.Equals } assignment when this.binding.PropertyCall(assignment.Left, PropertyAccessorKind.Set) is { } setter:
                flow = this.Visit(setter);
                break;
            case IdentifierNameKoto or MemberAccessKoto when this.binding.PropertyCall(node, PropertyAccessorKind.Get) is { } getter:
                flow = this.Visit(getter);
                break;
            case RangeKoto or FromEndIndexKoto when this.binding.RangeValueCall(node) is { } rangeValue:
                // SPEC 4.6.2, 4.6.3: prefix ^ and range syntax that construct a value are their synthesized PositionSyntax
                // calls, whose arguments may themselves be synthesized `^x` constructions.
                flow = this.Visit(rangeValue);
                break;
            case IndexKoto when this.binding.ViewRangeCall(node) is { } viewRange:
                flow = this.Visit(viewRange);
                break;
            case IndexKoto keyed when this.binding.ResolvedKeyCall(keyed) is { } resolvedKey:
                // SPEC 4.6.4: the receiver is evaluated, then a synthesized call resolves the key against the length of that
                // evaluated receiver; the selection completes as both do.
                var selectedFlow = this.Visit(keyed.Left);
                var keyFlow = this.Visit(resolvedKey);
                flow = new(selectedFlow.Normal && keyFlow.Normal, Union(selectedFlow.Transfers, selectedFlow.Normal ? keyFlow.Transfers : null), selectedFlow.Pending || keyFlow.Pending);
                break;
            case EvaluatedKoto:
                flow = new(true); // Its desugaring evaluated the source already.
                break;
            case IdentifierNameKoto when this.binding.StorageProjection(node) is { } storage:
                flow = this.Visit(storage);
                break;
            case BinaryKoto { ComparisonCall: { } comparisonCall }:
                flow = this.Visit(comparisonCall);
                break;
            case BinaryKoto { ArithmeticCall: { } arithmeticCall }:
                flow = this.Visit(arithmeticCall);
                break;
            case UnaryKoto { ArithmeticCall: { } unaryCall }:
                flow = this.Visit(unaryCall);
                break;
            case MacroKoto { Formatting: { Acquisition: { } acquisition } tryWrite }:
                var rootFlow = this.Visit(acquisition);
                var normalRoot = rootFlow.Normal;
                foreach (var write in tryWrite.Writes)
                {
                    var part = this.Visit(write);
                    rootFlow = new(rootFlow.Normal && part.Normal, Union(rootFlow.Transfers, rootFlow.Normal ? part.Transfers : null), rootFlow.Pending || part.Pending);
                }

                this.Visit(tryWrite.Outcome);
                flow = rootFlow with { Normal = normalRoot };
                break;
            case InterpolatedStringKoto { Formatting: { } formatting }:
                var formattingFlow = this.Visit(formatting.Heap);
                var adapterFlow = this.Visit(formatting.Adapter);
                formattingFlow = new(formattingFlow.Normal && adapterFlow.Normal, Union(formattingFlow.Transfers, adapterFlow.Transfers), formattingFlow.Pending || adapterFlow.Pending);
                foreach (var write in formatting.Writes)
                {
                    var writeFlow = this.Visit(write);
                    formattingFlow = new(formattingFlow.Normal && writeFlow.Normal, Union(formattingFlow.Transfers, formattingFlow.Normal ? writeFlow.Transfers : null), formattingFlow.Pending || writeFlow.Pending);
                }

                flow = formattingFlow;
                break;
            case SyntaxFormKoto { Akind: KotoKind.EnumCase }:
                // Case payloads are declaration Types, including their Origin names.
                // Binding validates them; constructing a Case is a separate expression.
                flow = new(true);
                break;
            case IsKoto { BoundConstraint: not null }:
                // A declaration constraint has no runtime evaluation. Its bound
                // Container path is checked by Binding, including every qualifier.
                flow = new(true);
                break;
            case not InvocationKoto when this.IsBoundConstruction(node):
                flow = new(true);
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

                return new(true); // A function value is not its body.
            case PropertyAccessorKoto accessor:
                // A getter without a written result returns its Property's Type, which Binding must resolve first.
                this.VisitFunction(accessor, accessor.Body, accessor.ReturnType is not null || accessor.AccessorKind == PropertyAccessorKind.Get);
                return new(true);
            case DeclarationContainerKoto container:
                this.VisitDeclarations(container);
                return new(true);
            case SyntaxFormKoto { Akind: KotoKind.AssociatedType }:
                // SPEC 8.4.3: associated-Type parameters and formation Types have no runtime evaluation.
                return new(true);
            case EffectBoundKoto:
                // SPEC 8.4.10.1: an effect item is declaration metadata; its selector and Name are never evaluated.
                return new(true);
            case OriginRelationKoto:
                // SPEC 15.3.3: a Type's origin clause, visited with its declarations, is a contract relation, never evaluated.
                return new(true);
            case AttributeKoto { BindingState: BindingState.Resolved, LayoutMode: not null }:
                // SPEC 21.1.2: a checked layout attribute is declaration metadata;
                // its syntax argument is not a runtime call or string acquisition.
                return new(true);
            case CompileTimeSwitchKoto:
                // Invalid groups already have parser diagnostics; their arms are not executable.
                return new(true);
            case FieldKoto field:
                // Declaration annotations (including Origin names) are not evaluations.
                flow = field.InitializerKoto is { } initializer ? this.Visit(initializer) : new(true);
                break;
            case PropertyKoto storedProperty:
                flow = storedProperty.InitializerKoto is { } fieldInitializer ? this.Visit(fieldInitializer) : new(true);
                for (var i = 0; i < storedProperty.Accessors.Count; i++)
                {
                    this.Visit(storedProperty.Accessors[i]);
                }

                break;
            case LabeledKoto labeled:
                this.CheckLabel(labeled);
                flow = this.Visit(labeled.Target);
                this.nodes[labeled] = this.NodeInfo(labeled.Target);
                break;
            case DeferredBlockKoto deferred:
                this.Begin(deferred);
                this.cleanups[deferred] = this.Finish(deferred, this.Visit(deferred.Body));
                // Registration never evaluates its body. Its completion matters at scope exit only.
                return new(true);
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
                return flow;
            case DoKoto scoped:
                this.Begin(scoped);
                flow = this.Finish(scoped, this.VisitBranch(scoped.Body));
                break;
            case IfKoto conditional:
                flow = this.VisitIf(conditional);
                break;
            case MatchKoto match:
                flow = this.VisitMatch(match);
                break;
            case LoopKoto or WhileKoto or ForKoto:
                flow = this.VisitIteration(node);
                break;
            case JumpKoto jump:
                flow = this.VisitJump(jump);
                break;
            case DiscardKoto discard:
                flow = this.Visit(discard.Operand);
                break;
            case RequireKoto require:
                flow = this.VisitRequire(require);
                break;
            case TestVerificationKoto verification:
                var conditionFlow = this.Visit(verification.Condition);
                var messageFlow = verification.Message is { } message ? this.Visit(message) : new Flow(true);
                flow = new(conditionFlow.Normal, Union(conditionFlow.Transfers, conditionFlow.Normal ? messageFlow.Transfers : null), conditionFlow.Pending || messageFlow.Pending);
                break;
            case CodeBlockKoto block:
                flow = this.VisitSequence(block.Items, 0, block.Items.Count);
                break;
            case InvocationKoto call when this.TryGetCallReceiver(call, out var receiver):
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
                var receiverFlow = receiver is null ? new Flow(true) : this.Visit(receiver);
                var argumentsFlow = this.VisitSequence(call.ArgumentNodes, 0, call.ArgumentNodes.Count, call.RightFirstArguments);
                var callType = this.SuppliedType(call);
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
                    acquired && !ReferenceEquals(callType, BoundType.Never),
                    Union(receiverFlow.Transfers, receiverFlow.Normal ? argumentsFlow.Transfers : null),
                    receiverFlow.Pending || (receiverFlow.Normal && argumentsFlow.Pending) || defaultPending || callType is null);
                if (callType is null || defaultPending)
                {
                    this.pending.Add(call);
                }

                break;
            case ParenthesizedKoto p:
                flow = this.Visit(p.Operand);
                break;
            case MemberAccessKoto { BoundSymbol.Kind: BindingSymbolKind.Function, BoundType.Kind: BoundTypeKind.FunctionItem or BoundTypeKind.Function }:
                // A selected function reference names a declaration. Its group/type qualifiers are designators,
                // including every segment of a nested group, rather than runtime receiver expressions.
                flow = new(true);
                break;
            case MemberAccessKoto { BoundSymbol.Property: not null } member:
                // A selected property name is a designator, not an evaluated local.
                flow = member.Left.BoundSymbol?.Kind == BindingSymbolKind.Container ? new(true) : this.Visit(member.Left);
                break;
            case MacroKoto macro when this.IsNever(macro):
                flow = this.Visit(macro.Operand);
                break;
            case IdentifierNameKoto name:
                var unknownName = Unknown(name);
                if (unknownName)
                {
                    this.pending.Add(name);
                }

                flow = new(!this.IsNever(name), Pending: unknownName);
                break;
            case BinaryKoto { Akind: KotoKind.And or KotoKind.Or } logical:
                var left = this.Visit(logical.Left);
                var right = this.Visit(logical.Right);
                // Runtime short-circuiting may skip the right operand. Do not constant-fold it for reachability.
                flow = new(left.Normal, Union(left.Transfers, left.Normal ? right.Transfers : null), left.Pending || right.Pending);
                break;
            case IsKoto { IsRuntimeTest: true } test:
                flow = this.Visit(test.Left);
                if (!IsBoundRuntimeTypeTest(test))
                {
                    // The target's validity requires Binding.
                    this.pending.Add(test);
                    flow = flow with { Pending = true };
                }

                break;
            case ConversionKoto conversion:
                // The target is checked Type syntax, not a runtime expression.
                flow = this.Visit(conversion.Left);
                if (conversion.Right.BindingState != BindingState.Resolved)
                {
                    this.pending.Add(conversion);
                    flow = flow with { Pending = true };
                }

                break;
            case GenericsKoto generic:
                // Bound Type/length arguments, including Origin names, are compile-time syntax. Only the
                // referenced expression is evaluated; traversing the arguments invents runtime pending values.
                flow = generic.Identifier is { } identifier ? this.Visit(identifier) : new(true);
                break;
            default:
                flow = this.VisitChildSequence(node);
                if (Unknown(node))
                {
                    this.pending.Add(node);
                    flow = flow with { Pending = true };
                }

                if (this.IsNever(node))
                {
                    flow = flow with { Normal = false };
                }

                break;
        }

        var info = this.NodeInfo(node);
        info.CanCompleteNormally = flow.Normal;
        var updatedTarget = node switch
        {
            BinaryKoto binary when binary.Akind is > KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals => binary.Left,
            UnaryKoto unary when ElementAccess.UpdateOperator(unary.Akind) != KotoKind.Invalid => unary.Operand,
            _ => null,
        };
        if (updatedTarget is not null && this.binding.PropertyCall(updatedTarget, PropertyAccessorKind.Set) is { } updateSetter)
        {
            if (updateSetter.ArgumentNodes[0] is EvaluatedKoto computedInput)
            {
                this.Visit(computedInput);
            }

            // The receiver and RHS were visited once in source order. The final call
            // consumes their prepared values and adds no second evaluation or transfer.
            this.nodes[updateSetter] = info;
        }

        return flow;
    }

    private DefaultCompletion VisitDefault(FunctionKoto function, int parameterIndex)
    {
        var value = function.Parameters[parameterIndex].DefaultValue!;
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
        this.defaultDepth++;
        var flow = this.Visit(value);

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
        => this.VisitFunction(function, function.Body ?? function.ExpressionBody, function.ReturnType is not null);

    // Creation does not execute the body.
    private Flow CreatedClosure(FunctionKoto function)
    {
        this.NodeInfo(function).CanCompleteNormally = true;
        return new(true);
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
    // lines allow the edit then offers Repair.RemoveUnsafe.
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

        DiagnosticRepairFact[]? repairs = null;
        if (!scoped && block.Parent is CodeBlockKoto { IsExpressionBody: false } && RemoveUnsafeEdits(block) is { } edits)
        {
            repairs = [new(RepairKind.RemoveUnsafe, null, edits, RepairConditionSet.Structure)];
        }

        this.warnings.Add(new(block, DiagnosticCode.UnnecessaryUnsafeBlock_Kd) { Span = new(block.Span.Start, Constants.UnsafeKeyword.Length), Repairs = repairs });
    }

    private void VisitDeclarations(DeclarationContainerKoto container)
    {
        // Header Types, generic parameters and Constraints have no runtime evaluation. In particular, a base
        // Type's Origin arguments must not become unresolved value reads. Binding checks these declarations.
        for (var i = 0; i < container.Members.Count; i++)
        {
            this.Visit(container.Members[i]);
        }

        for (var i = 0; i < container.NestedContainers.Count; i++)
        {
            this.Visit(container.NestedContainers[i]);
        }

        if (container is StructKoto { ImplicitConstructor: { } constructor })
        {
            this.Visit(constructor);
        }
        else if (container is GroupKoto group && ReferenceEquals(group, group.Kotonoha.RootKoto) && group.Kotonoha.GeneratedFunction is { } startup)
        {
            this.Visit(startup);
        }
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

    private Flow VisitSequence(IReadOnlyList<Koto> items, int start, int count, bool reverse = false)
    {
        var normal = true;
        var pendingCompletion = false;
        HashSet<JumpKoto>? transfers = null;
        List<DeferredBlockKoto>? registrations = null;
        for (var i = start; i < start + count; i++)
        {
            var item = items[reverse ? start + count - 1 - (i - start) : i];
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

        return new(normal, transfers, pendingCompletion);
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
                    return new(false, Pending: pendingCompletion);
                }
            }
        }

        return new(true, Pending: pendingCompletion);
    }

    private Flow VisitBodyItem(Koto item)
    {
        var flow = this.Visit(item);
        return item is DeferredBlockKoto deferred ? this.cleanups[deferred] : flow;
    }

    // SPEC 7.1: an indented body supplies Unit at its structural end, so a non-Unit result cannot fall through. The result is
    // pending while a written or getter contract is unresolved.
    private void VisitFunction(Koto node, Koto? body, bool contract)
    {
        if (body is null || this.HasInvalidDirective(body))
        {
            return;
        }

        this.Begin(node);
        var result = DeclaredResult(node);
        if (contract && result is null)
        {
            this.pending.Add(node);
        }

        var baseFlow = node is FunctionKoto { BaseInitializer: { } initializer } ? this.Visit(initializer) : new Flow(true);
        var flow = this.VisitBodyItem(body);
        flow = baseFlow.Normal ? flow with { Transfers = Union(baseFlow.Transfers, flow.Transfers), Pending = baseFlow.Pending || flow.Pending } : baseFlow;
        this.Finish(node, flow);
        if (body is CodeBlockKoto && this.structural.CanComplete(body) && !flow.Pending && result is not null && !ReferenceEquals(result, BoundType.Unit))
        {
            this.Error(body, DiagnosticCode.FunctionFallthrough_Kd);
        }
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

    // A boundary's information precedes that of the syntax it contains.
    private void Begin(Koto node) => this.nodes[node] = this.RentInfo();

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

        var operand = jump.Expression is { } expression ? this.Visit(expression) : new Flow(true);
        var transfers = operand.Transfers;
        if (operand.Normal)
        {
            (transfers ??= this.RentTransfers()).Add(jump);
        }

        return new(false, transfers, operand.Pending);
    }

    /// <summary>Analyzes a require statement, which is transparent to transfer lookup and is not a selection.</summary>
    private Flow VisitRequire(RequireKoto node)
    {
        var condition = this.Visit(node.Condition);

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
        return new(condition.Normal, transfers, condition.Pending || (condition.Normal && failure.Pending));
    }

    private Flow VisitIf(IfKoto node)
    {
        this.Begin(node);
        var next = true;
        var normal = false;
        var pendingCompletion = false;
        HashSet<JumpKoto>? transfers = null;
        for (var index = 0; index < node.Branches.Count; index++)
        {
            var branch = node.Branches[index];
            var condition = this.Visit(branch.Condition);
            if (next)
            {
                transfers = Union(transfers, condition.Transfers);
                pendingCompletion |= condition.Pending;
            }

            var chosen = next && condition.Normal;
            var body = this.VisitBranch(branch.Body);
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
            var body = this.VisitBranch(otherwise);
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
        }

        return this.Finish(node, new(normal, transfers, pendingCompletion));
    }

    // Binding's coverage decides whether a match can complete without an arm; it is pending until Binding validates the Patterns.
    private Flow VisitMatch(MatchKoto node)
    {
        var subject = this.Visit(node.Expression);
        this.Begin(node);
        var exhaustive = this.binding.TryGetMatch(node, out var plan) ? plan!.Coverage.IsExhaustive : null;
        if (exhaustive is null)
        {
            this.pending.Add(node);
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
                var guardFlow = this.Visit(guard);
                guardNormal = guardFlow.Normal;
                if (subject.Normal)
                {
                    transfers = Union(transfers, guardFlow.Transfers);
                }

                pendingCompletion |= subject.Normal && guardFlow.Pending;
            }

            var body = this.VisitBranch(arm.Body);
            normal |= guardNormal && body.Normal;
            pendingCompletion |= subject.Normal && guardNormal && body.Pending;
            if (subject.Normal && guardNormal)
            {
                transfers = Union(transfers, body.Transfers);
            }
        }

        return this.Finish(node, new(subject.Normal && normal, transfers, pendingCompletion));
    }

    // A branch body shares the information of its single item.
    private Flow VisitBranch(Koto body)
    {
        var expression = body is CodeBlockKoto { IsExpressionBody: true } block ? block.Items[0] : body;
        var flow = this.VisitBodyItem(expression);
        if (body != expression)
        {
            this.nodes[body] = this.NodeInfo(expression);
        }

        return flow;
    }

    private Flow VisitIteration(Koto node)
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

                header = this.Visit(w.Condition);
                body = w.Body;
                mayFinish = true;
                break;
            default:
                header = new(true);
                body = ((LoopKoto)node).Body;
                break;
        }

        this.Begin(node);
        var bodyFlow = this.Visit(body);
        var transfers = Union(header.Transfers, header.Normal ? bodyFlow.Transfers : null);
        return this.Finish(node, new(header.Normal && mayFinish, transfers, header.Pending || bodyFlow.Pending));
    }

    private Flow Finish(Koto node, Flow flow)
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
                flow = flow with { Normal = flow.Normal || jump is not ContinueKoto };
                transfers.Remove(jump);
            }

            arrived.Clear();
        }

        this.nodes[node].CanCompleteNormally = flow.Normal;

        // SPEC 17.4.1: a selection or do whose Unit result Binding inferred, rather than a declaration, a construct rule or
        // an expected Type fixing it, warns at a non-Unit tail it discards.
        if (node is DoKoto or IfKoto or MatchKoto and not TryKoto && this.binding.InfersUnit(node))
        {
            var root = node is DoKoto scoped ? scoped.Body : node;
            this.WarnUnitTail(root, root);
        }

        return flow;
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

    private readonly record struct Flow(bool Normal, HashSet<JumpKoto>? Transfers = null, bool Pending = false);
}
