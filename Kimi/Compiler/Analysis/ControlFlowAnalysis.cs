// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // Analysis result and its diagnostic belong together.

/// <summary>A control-flow diagnostic associated with source syntax.</summary>
/// <param name="Node">The offending syntax.</param>
/// <param name="Message">The diagnostic text.</param>
public sealed record ControlFlowIssue(Koto Node, string Message);

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

    /// <summary>Gets a value indicating whether completion depends on unresolved Binding facts.</summary>
    public bool IsCompletionPending { get; internal set; }

    /// <summary>Gets the callable return type of a function boundary, separately from its expression type.</summary>
    public ControlFlowType? FunctionResultType { get; internal set; }
}

/// <summary>Analyzes lexical transfers, reachability, result coverage, and known result types.</summary>
/// <remarks>
/// Run after compile-time directive selection. Bodies with deferred directives are reported as pending,
/// rather than treating conditional compilation as runtime branching. Supply bound type facts to discharge
/// obligations which the default syntax-only type provider cannot decide.
/// </remarks>
public sealed class ControlFlowAnalysis
{
    private readonly ControlFlowTypeSystem types;
    private readonly Dictionary<Koto, ControlFlowNodeInfo> nodes = new();
    private readonly Dictionary<JumpKoto, Koto?> targets = new();
    private readonly Dictionary<Koto, Boundary> boundaries = new();
    private readonly List<ControlFlowIssue> issues = new();
    private readonly HashSet<Koto> pending = new();
    private readonly HashSet<(Koto Node, string Message)> reported = new();
    private readonly Dictionary<IdentifierNameKoto, ControlFlowType?> names = new();

    private ControlFlowAnalysis(ControlFlowTypeSystem types) => this.types = types;

    /// <summary>Gets information for each analyzed expression or boundary.</summary>
    public IReadOnlyDictionary<Koto, ControlFlowNodeInfo> Nodes => this.nodes;

    /// <summary>Gets resolved lexical transfer targets.</summary>
    public IReadOnlyDictionary<JumpKoto, Koto?> Targets => this.targets;

    /// <summary>Gets definite errors.</summary>
    public IReadOnlyList<ControlFlowIssue> Issues => this.issues;

    /// <summary>Gets obligations requiring directive selection or further type Binding.</summary>
    public IReadOnlyCollection<Koto> PendingBinding => this.pending;

    /// <summary>Analyzes attached syntax without mutating or serializing analysis state into the tree.</summary>
    /// <param name="root">The tree or function to analyze.</param>
    /// <param name="types">Bound type facts, or the default syntax-only provider.</param>
    /// <returns>The analysis results.</returns>
    public static ControlFlowAnalysis Analyze(Koto root, ControlFlowTypeSystem? types = null)
    {
        var analysis = new ControlFlowAnalysis(types ?? new SyntaxControlFlowTypes());
        analysis.Visit(root, true);
        return analysis;
    }

    /// <summary>Copies definite errors into their source diagnostic collections.</summary>
    public void ReportDiagnostics()
    {
        foreach (var issue in this.issues)
        {
            issue.Node.AddDiagnostic(DiagnosticCode.ControlFlow_Kd, issue.Message);
        }
    }

    private static bool? LiteralCondition(Koto node)
    {
        while (node is ParenthesizedKoto p)
        {
            node = p.Operand;
        }

        return node is BoolLiteralKoto b ? b.Value : null;
    }

    private static bool HasDeferredDirective(Koto node)
        => node is CompileTimeIfKoto or CompileTimeCaseGroupKoto ||
            node.ChildNodes.Any(child => child is not (FunctionKoto or PropertyAccessorKoto) && HasDeferredDirective(child));

    private static HashSet<JumpKoto>? Union(HashSet<JumpKoto>? left, HashSet<JumpKoto>? right)
    {
        if (right is null)
        {
            return left;
        }

        left ??= new();
        left.UnionWith(right);
        return left;
    }

    private void Error(Koto node, string message)
    {
        if (this.reported.Add((node, message)))
        {
            this.issues.Add(new(node, message));
        }
    }

    private Flow Visit(Koto node, bool reachable, ControlFlowType? expected = null)
    {
        expected ??= this.types.GetExpectedType(node);
        Flow flow;
        switch (node)
        {
            case FunctionKoto function:
                this.VisitFunction(
                    function,
                    function.Body ?? function.ExpressionBody,
                    function.ReturnType,
                    function.IsDestructor ? ControlFlowType.Unit : this.types.GetExpectedResultType(function));
                return new(true, null); // A function value is not its body or its return type.
            case PropertyAccessorKoto accessor:
                this.VisitFunction(
                    accessor,
                    accessor.Body,
                    accessor.AccessorKind == PropertyAccessorKind.Get ? (accessor.Parent as PropertyKoto)?.TypeKoto : null,
                    accessor.AccessorKind == PropertyAccessorKind.Set ? ControlFlowType.Unit : this.types.GetExpectedResultType(accessor));
                return new(true, null);
            case DeclarationContainerKoto:
                foreach (var child in node.ChildNodes)
                {
                    this.Visit(child, true);
                }

                return new(true, ControlFlowType.Unit);
            case CompileTimeIfKoto or CompileTimeCaseGroupKoto:
                this.pending.Add(node);
                return new(true, null);
            case FieldKoto field:
                var declared = this.types.GetDeclaredType(field.TypeKoto);
                flow = field.InitializerKoto is { } initializer ? this.Visit(initializer, reachable, declared) : new(true, declared);
                if (field.TypeKoto is not null && declared is null)
                {
                    this.pending.Add(field);
                }

                this.names[field.NameKoto] = declared ?? flow.Type;
                flow = flow with { Type = ControlFlowType.Unit };
                break;
            case LabeledKoto labeled:
                this.CheckLabel(labeled);
                flow = labeled.Target is CodeBlockKoto labeledBlock
                    ? this.VisitLabeledBlock(labeledBlock, reachable)
                    : this.Visit(labeled.Target, reachable, expected);
                break;
            case IfKoto conditional:
                flow = this.VisitIf(conditional, reachable, expected);
                break;
            case MatchKoto match:
                flow = this.VisitMatch(match, reachable, expected);
                break;
            case LoopKoto or WhileKoto or ForKoto:
                flow = this.VisitIteration(node, reachable, expected);
                break;
            case JumpKoto jump:
                flow = this.VisitJump(jump, reachable);
                break;
            case CodeBlockKoto block:
                flow = this.VisitSequence(block.Items, reachable);
                break;
            case ParenthesizedKoto p:
                flow = this.Visit(p.Operand, reachable, expected);
                break;
            case IdentifierNameKoto name:
                var nameType = this.types.GetExpressionType(name) ?? this.ResolveNameType(name);
                flow = new(nameType != ControlFlowType.Never, nameType, Pending: nameType is null);
                break;
            case AndKoto or OrKoto:
                var logical = (BinaryKoto)node;
                var left = this.Visit(logical.Left, reachable, ControlFlowType.Boolean);
                var right = this.Visit(logical.Right, reachable && left.Normal, ControlFlowType.Boolean);
                // Runtime short-circuiting may skip the right operand. Do not constant-fold it for reachability.
                flow = new(left.Normal, ControlFlowType.Boolean, Union(left.Transfers, left.Normal ? right.Transfers : null), left.Pending || right.Pending);
                break;
            default:
                flow = this.VisitSequence(node.ChildNodes, reachable);
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

        if (!this.nodes.TryGetValue(node, out var info))
        {
            this.nodes[node] = info = new();
        }

        info.ExpressionType = this.boundaries.TryGetValue(node, out var resultBoundary) && resultBoundary.InvalidResult
            ? null : flow.Normal ? flow.Type : ControlFlowType.Never;
        info.CanCompleteNormally = flow.Normal;
        info.IsCompletionPending = flow.Pending;
        if (expected is not null)
        {
            this.Constrain(node, expected);
        }

        return flow;
    }

    private Flow VisitSequence(IEnumerable<Koto> items, bool reachable)
    {
        var normal = true;
        var pendingCompletion = false;
        HashSet<JumpKoto>? transfers = null;
        foreach (var item in items)
        {
            var flow = this.Visit(item, reachable && normal);
            if (normal)
            {
                transfers = Union(transfers, flow.Transfers);
                normal = flow.Normal;
                pendingCompletion |= flow.Pending;
            }
        }

        return new(normal, ControlFlowType.Unit, transfers, pendingCompletion);
    }

    private void VisitFunction(Koto node, Koto? body, Koto? declaration, ControlFlowType? expected)
    {
        if (body is null)
        {
            return;
        }

        if (HasDeferredDirective(body))
        {
            this.pending.Add(node);
            return;
        }

        var boundary = this.Begin(node, this.types.GetDeclaredType(declaration) ?? expected);
        if (declaration is not null && boundary.Expected is null)
        {
            boundary.InferenceBlocked = true;
            this.pending.Add(node);
        }

        var flow = this.Visit(body, true, body is CodeBlockKoto ? null : boundary.Expected);
        if (flow.Normal && !(body is CodeBlockKoto && flow.Pending))
        {
            boundary.Sources.Add(new(body, body is CodeBlockKoto ? ControlFlowType.Unit : flow.Type, true));
        }

        var finished = this.Finish(node, flow, boundary);
        if (body is CodeBlockKoto && flow.Normal && !flow.Pending &&
            this.nodes[node].TargetResultType is { } resultType && resultType != ControlFlowType.Unit)
        {
            this.Error(body, "A non-Unit Block-bodied function cannot fall through.");
        }

        // The function's callable return type falls back to Never, while its target contract remains absent.
        this.nodes[node].FunctionResultType = boundary.Expected ?? this.nodes[node].TargetResultType ?? finished.Type;
        this.nodes[node].ExpressionType = this.types.GetExpressionType(node);
    }

    private Boundary Begin(Koto node, ControlFlowType? expected)
    {
        var boundary = new Boundary(expected);
        this.boundaries[node] = boundary;
        this.nodes[node] = new() { IsResultRequiring = KotoHelper.IsResultRequiringSelection(node) };
        return boundary;
    }

    private Flow VisitJump(JumpKoto jump, bool reachable)
    {
        var target = KotoHelper.ResolveTransferTarget(jump);
        this.targets[jump] = target;
        if (target is null)
        {
            this.Error(jump, $"No valid target for {jump.Keyword}.");
        }

        if (jump is ExitKoto && jump.Expression is not null && target is not (null or LoopKoto))
        {
            this.Error(jump, "Only loop accepts an exit result operand.");
        }

        this.boundaries.TryGetValue(target ?? jump, out var boundary);
        var operand = jump.Expression is { } expression ? this.Visit(expression, reachable, boundary?.Expected) : new Flow(true, ControlFlowType.Unit);
        if (operand.Normal && jump is not ContinueKoto && boundary is not null)
        {
            boundary.Sources.Add(new(jump.Expression ?? jump, operand.Type, reachable));
        }

        var transfers = operand.Transfers;
        if (operand.Normal)
        {
            transfers = Union(transfers, new HashSet<JumpKoto> { jump });
        }

        return new(false, ControlFlowType.Never, transfers, operand.Pending);
    }

    private Flow VisitIf(IfKoto node, bool reachable, ControlFlowType? expected)
    {
        var boundary = this.Begin(node, expected);
        var required = this.nodes[node].IsResultRequiring;
        if (required && node.ElseBody is null)
        {
            boundary.InvalidResult = true;
            this.Error(node, "A Result-requiring if requires a final else.");
        }

        var next = true;
        var normal = false;
        var pendingCompletion = false;
        HashSet<JumpKoto>? transfers = null;
        foreach (var branch in node.Branches)
        {
            var condition = this.Visit(branch.Condition, reachable && next, ControlFlowType.Boolean);
            if (next)
            {
                transfers = Union(transfers, condition.Transfers);
                pendingCompletion |= condition.Pending;
            }

            var literal = LiteralCondition(branch.Condition);
            var chosen = next && condition.Normal && literal != false;
            var body = this.VisitBranch(branch.Body, branch.Body.IsExpressionBody, reachable && chosen, required, boundary);
            if (chosen)
            {
                normal |= body.Normal;
                pendingCompletion |= body.Pending;
                transfers = Union(transfers, body.Transfers);
            }

            next &= condition.Normal && literal != true;
        }

        if (node.ElseBody is { } otherwise)
        {
            var body = this.VisitBranch(otherwise, otherwise.IsExpressionBody, reachable && next, required, boundary);
            if (next)
            {
                normal |= body.Normal;
                pendingCompletion |= body.Pending;
                transfers = Union(transfers, body.Transfers);
            }
        }
        else if (next)
        {
            normal = true;
            if (!required)
            {
                boundary.Sources.Add(new(node, ControlFlowType.Unit, reachable));
            }
        }

        return this.Finish(node, new(normal, null, transfers, pendingCompletion), boundary);
    }

    private Flow VisitMatch(MatchKoto node, bool reachable, ControlFlowType? expected)
    {
        var subject = this.Visit(node.Expression, reachable);
        var boundary = this.Begin(node, expected);
        var required = this.nodes[node].IsResultRequiring;
        var exhaustive = this.types.IsExhaustive(node);
        if (exhaustive is null && subject.Type == ControlFlowType.Boolean)
        {
            exhaustive = node.Arms.Any(x => LiteralCondition(x.Pattern) == true) &&
                node.Arms.Any(x => LiteralCondition(x.Pattern) == false);
        }

        if (exhaustive is null)
        {
            this.pending.Add(node);
        }
        else if (required && exhaustive == false)
        {
            boundary.InvalidResult = true;
            this.Error(node, "A Result-requiring match must be exhaustive.");
        }

        var normal = exhaustive != true;
        var pendingCompletion = subject.Pending || exhaustive is null;
        var transfers = subject.Transfers;
        foreach (var arm in node.Arms)
        {
            if (arm.Pattern is not IdentifierNameKoto { IdentifierName: "_" } && subject.Type is { } subjectType)
            {
                this.CheckCompatibility(new(arm.Pattern, this.types.GetExpressionType(arm.Pattern), true), subjectType);
            }

            var body = this.VisitBranch(arm.Body, arm.Body is not CodeBlockKoto, reachable && subject.Normal, required, boundary);
            normal |= body.Normal;
            pendingCompletion |= subject.Normal && body.Pending;
            if (subject.Normal)
            {
                transfers = Union(transfers, body.Transfers);
            }
        }

        if (exhaustive != true && !required)
        {
            boundary.Sources.Add(new(node, ControlFlowType.Unit, reachable && subject.Normal));
        }

        return this.Finish(node, new(subject.Normal && normal, null, transfers, pendingCompletion), boundary);
    }

    private Flow VisitBranch(Koto body, bool expressionBody, bool reachable, bool required, Boundary boundary)
    {
        var expression = body is CodeBlockKoto { IsExpressionBody: true } block ? block.Items[0] : body;
        var flow = this.Visit(expression, reachable, expressionBody ? boundary.Expected : null);
        if (flow.Normal)
        {
            if (expressionBody || !required)
            {
                boundary.Sources.Add(new(expression, expressionBody ? flow.Type : ControlFlowType.Unit, reachable));
            }
            else if (reachable)
            {
                if (flow.Pending)
                {
                    this.pending.Add(body);
                }
                else
                {
                    boundary.InvalidResult = true;
                    this.Error(body, "A result-requiring Block branch must yield a result on every normally completing path.");
                }
            }
        }

        return flow;
    }

    private Flow VisitIteration(Koto node, bool reachable, ControlFlowType? expected)
    {
        Flow header;
        CodeBlockKoto body;
        var mayFinish = false;
        var enterBody = true;
        switch (node)
        {
            case ForKoto f:
                header = this.Visit(f.Iterable, reachable);
                body = f.Body;
                mayFinish = true;
                break;
            case WhileKoto w:
                header = this.Visit(w.Condition, reachable, ControlFlowType.Boolean);
                body = w.Body;
                mayFinish = LiteralCondition(w.Condition) != true;
                enterBody = LiteralCondition(w.Condition) != false;
                break;
            default:
                header = new(true, ControlFlowType.Unit);
                body = ((LoopKoto)node).Body;
                break;
        }

        var boundary = this.Begin(node, node is LoopKoto ? expected : ControlFlowType.Unit);
        var bodyFlow = this.Visit(body, reachable && header.Normal && enterBody);
        if (mayFinish)
        {
            boundary.Sources.Add(new(node, ControlFlowType.Unit, reachable && header.Normal));
        }

        var transfers = Union(header.Transfers, header.Normal && enterBody ? bodyFlow.Transfers : null);
        return this.Finish(node, new(header.Normal && mayFinish, null, transfers, header.Pending || (enterBody && bodyFlow.Pending)), boundary);
    }

    private Flow VisitLabeledBlock(CodeBlockKoto block, bool reachable)
    {
        var boundary = this.Begin(block, ControlFlowType.Unit);
        var flow = this.VisitSequence(block.Items, reachable);
        if (flow.Normal)
        {
            boundary.Sources.Add(new(block, ControlFlowType.Unit, reachable));
        }

        return this.Finish(block, flow, boundary);
    }

    private Flow Finish(Koto node, Flow flow, Boundary boundary)
    {
        if (flow.Pending)
        {
            boundary.InferenceBlocked = true;
            this.pending.Add(node);
        }

        if (flow.Transfers is { } transfers)
        {
            foreach (var jump in transfers.ToArray())
            {
                if (this.targets.GetValueOrDefault(jump) == node)
                {
                    flow = flow with { Normal = flow.Normal || jump is not ContinueKoto };
                    transfers.Remove(jump);
                }
            }
        }

        var candidates = boundary.Sources.Where(x => x.IsReachable).ToArray();
        var targetType = boundary.Expected ?? (boundary.InferenceBlocked ? null : this.types.InferResultType(candidates));
        var info = this.nodes[node];
        info.TargetResultType = targetType;
        info.CanCompleteNormally = flow.Normal;
        info.IsCompletionPending = flow.Pending;
        info.ExpressionType = boundary.InvalidResult ? null : !flow.Normal ? ControlFlowType.Never :
            flow.Pending ? null : candidates.Length == 0 ? ControlFlowType.Never : targetType;
        if (candidates.Length > 0 && targetType is null)
        {
            this.pending.Add(node);
        }

        if (targetType is not null)
        {
            this.CheckSources(node, boundary, targetType);
        }

        return flow with { Type = info.ExpressionType };
    }

    private void CheckSources(Koto target, Boundary boundary, ControlFlowType type)
    {
        foreach (var source in boundary.Sources)
        {
            // Propagate a later inferred contract into nested Never expressions as well.
            if (source.Node != target && source.Node is not JumpKoto)
            {
                this.Constrain(source.Node, type);
            }

            this.CheckCompatibility(source, type);
        }
    }

    private void Constrain(Koto node, ControlFlowType type)
    {
        if (node is ParenthesizedKoto p)
        {
            this.Constrain(p.Operand, type);
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
            this.CheckCompatibility(new(node, info.ExpressionType, false), type);
        }
    }

    private void CheckCompatibility(ControlFlowResultSource source, ControlFlowType type)
    {
        var compatible = this.types.IsCompatible(source, type);
        if (compatible == false)
        {
            this.Error(source.Node, $"Result of type {source.Type?.Name} is incompatible with {type.Name}.");
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
                foreach (var item in block.Items)
                {
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
                var parameter = function.Parameters.FirstOrDefault(x => x.InternalName == name.IdentifierName);
                if (parameter is not null)
                {
                    return this.types.GetDeclaredType(parameter.Type);
                }
            }
        }

        this.pending.Add(name);
        return null;
    }

    private ControlFlowType? InferLocalType(Koto node)
    {
        if (node is UnaryKoto unary && this.nodes.TryGetValue(unary.Operand, out var operand))
        {
            if (node is NotKoto)
            {
                this.Constrain(unary.Operand, ControlFlowType.Boolean);
                return ControlFlowType.Boolean;
            }

            if (node is PrefixMinusKoto or PrefixPlusKoto)
            {
                if (operand.ExpressionType?.Name is "()" or "bool" or "string")
                {
                    this.Error(node, "A numeric unary operator requires a numeric operand.");
                }

                return operand.ExpressionType;
            }

            return null;
        }

        if (node is BinaryKoto binary && this.nodes.TryGetValue(binary.Left, out var left) && this.nodes.TryGetValue(binary.Right, out var right))
        {
            if (node is ConversionKoto)
            {
                return this.types.GetDeclaredType(binary.Right);
            }

            if (node is MemberAccessKoto or AsKoto or IsKoto)
            {
                return null;
            }

            if (left.ExpressionType is { } type)
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

            if (node is PlusKoto or MinusKoto or AsteriskKoto or SlashKoto or PercentKoto)
            {
                if (left.ExpressionType?.Name is "()" or "bool" ||
                    (left.ExpressionType?.Name == "string" && node is not PlusKoto))
                {
                    this.Error(node, "An arithmetic operator requires numeric operands.");
                }

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
                this.Error(label, $"Label {label.Label} overlaps an enclosing Label.");
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

    private sealed record Flow(bool Normal, ControlFlowType? Type, HashSet<JumpKoto>? Transfers = null, bool Pending = false);
}
