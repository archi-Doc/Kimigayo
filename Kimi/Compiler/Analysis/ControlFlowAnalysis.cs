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
/// Run after compile-time directive selection. Bodies containing invalid directive groups are skipped.
/// Supply bound type facts to discharge
/// obligations which the default syntax-only type provider cannot decide.
/// </remarks>
public sealed class ControlFlowAnalysis
{
    private const string PointerPrefix = "unsafe/";
    private static readonly ControlFlowType IsizeType = new("isize");

    private readonly ControlFlowTypeSystem types;
    private readonly Dictionary<Koto, ControlFlowNodeInfo> nodes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<JumpKoto, Koto?> targets = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Koto, Boundary> boundaries = new(ReferenceEqualityComparer.Instance);
    private readonly List<ControlFlowIssue> issues = new();
    private readonly HashSet<Koto> pending = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<(Koto Node, string Message)> reported = new();
    private readonly Dictionary<IdentifierNameKoto, ControlFlowType?> names = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DeferredBlockKoto, Flow> cleanups = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<JumpKoto> blockedDeliveries = new(ReferenceEqualityComparer.Instance);

    // Direct children are collected into one shared stack-like buffer instead of iterator objects.
    // A traversal appends its children, visits them by index, and truncates the buffer afterwards.
    private readonly List<Koto> childBuffer = new();
    private readonly ChildCollector childCollector;
    private readonly List<JumpKoto> arrivedTransfers = new();
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

    private ControlFlowAnalysis(ControlFlowTypeSystem types)
    {
        this.types = types;
        this.childCollector = new(this.childBuffer);
    }

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

    /// <summary>Replaces these results using retained storage and the same type provider.</summary>
    /// <param name="root">The current bound tree.</param>
    public void Reanalyze(Koto root)
    {
        this.nodes.Clear();
        this.targets.Clear();
        this.boundaries.Clear();
        this.issues.Clear();
        this.pending.Clear();
        this.reported.Clear();
        this.names.Clear();
        this.cleanups.Clear();
        this.blockedDeliveries.Clear();
        this.childBuffer.Clear();
        this.arrivedTransfers.Clear();
        this.infoCursor = this.boundaryCursor = this.transferCursor = this.registrationCursor = 0;
        this.Visit(root, true);
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
        => KotoHelper.UnwrapParentheses(node) is BoolLiteralKoto b ? b.Value : null;

    private static bool IsPointer(ControlFlowType? type)
        => type?.Name.StartsWith(PointerPrefix, StringComparison.Ordinal) == true;

    private static bool IsNullLiteral(Koto node)
        => KotoHelper.UnwrapParentheses(node) is NullLiteralKoto;

    private static FieldKoto? GetConditionBinding(ParenthesizedKoto node)
    {
        if (node.Operand is not CodeBlockKoto { Items.Count: 1 } block ||
            block.Items[0] is not FieldKoto { InitializerKoto: not null } field)
        {
            return null;
        }

        Koto condition = node;
        while (condition.Parent is ParenthesizedKoto outer)
        {
            condition = outer;
        }

        switch (condition.Parent)
        {
            case IfKoto selection:
                for (var index = 0; index < selection.Branches.Count; index++)
                {
                    var branch = selection.Branches[index];
                    if (branch.Condition == condition)
                    {
                        return field;
                    }
                }

                return null;
            case WhileKoto iteration when iteration.Condition == condition:
                return field;
            default:
                return null;
        }
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

    private ControlFlowNodeInfo RentInfo()
    {
        if (this.infoCursor == this.infoPool.Count)
        {
            this.infoPool.Add(new());
        }

        var info = this.infoPool[this.infoCursor++];
        info.ExpressionType = info.TargetResultType = info.FunctionResultType = null;
        info.IsResultRequiring = info.CanCompleteNormally = info.IsCompletionPending = false;
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

    private Flow Visit(Koto node, bool reachable, ControlFlowType? expected = null)
    {
        expected ??= this.types.GetExpectedType(node);
        var referencedFunction = this.types.GetReferencedFunction(node);
        if (referencedFunction?.Modifier.HasFlag(ModifierKind.Unsafe) == true)
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
                this.Error(node, "An unsafe function can only be called directly; it cannot be taken as a function value.");
            }
        }

        if (this.types.RequiresUnsafeContext(node) == true)
        {
            this.CheckUnsafePermission(node);
        }

        Flow flow;
        switch (node)
        {
            case not InvocationKoto when this.types.IsBoundConstruction(node):
                flow = new(true, this.types.GetExpressionType(node));
                break;
            case FunctionKoto function:
                this.VisitFunction(
                    function,
                    function.Body ?? function.ExpressionBody,
                    function.ReturnType,
                    function.IsDestructor ? ControlFlowType.Unit : this.types.GetExpectedResultType(function));
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
            case CompileTimeMatchKoto:
                // Invalid groups already have parser diagnostics; their arms are not executable.
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
                    ? this.VisitLabeledBlock(labeled, labeledBlock, reachable, expected)
                    : this.Visit(labeled.Target, reachable, expected);
                this.nodes[labeled] = this.nodes[labeled.Target];
                break;
            case DeferredBlockKoto deferred:
                var cleanupBoundary = this.Begin(deferred, ControlFlowType.Unit);
                this.cleanups[deferred] = this.Finish(deferred, this.Visit(deferred.Body, reachable), cleanupBoundary);
                this.nodes[deferred].ExpressionType = null;
                // Registration never evaluates its body. Its completion matters at scope exit only.
                return new(true, null);
            case UnsafeBlockKoto unsafeBlock:
                flow = this.Visit(unsafeBlock.Body, reachable);
                var unsafeInfo = this.RentInfo();
                unsafeInfo.CanCompleteNormally = flow.Normal;
                unsafeInfo.IsCompletionPending = flow.Pending;
                this.nodes[unsafeBlock] = unsafeInfo;
                return flow with { Type = null };
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
            case RequireKoto require:
                flow = this.VisitRequire(require, reachable);
                break;
            case CodeBlockKoto block:
                flow = this.VisitSequence(block.Items, 0, block.Items.Count, reachable);
                break;
            case InvocationKoto call when this.types.TryGetCallReceiver(call, out var receiver):
                // A committed direct callee is a designator, not a function-value acquisition.
                // Bound receiver syntax precedes the explicit arguments exactly once.
                var receiverFlow = receiver is null ? new Flow(true, ControlFlowType.Unit) : this.Visit(receiver, reachable);
                var argumentsFlow = this.VisitSequence(call.ArgumentNodes, 0, call.ArgumentNodes.Count, reachable && receiverFlow.Normal);
                var callType = this.types.GetExpressionType(call);
                flow = new(
                    receiverFlow.Normal && argumentsFlow.Normal && callType != ControlFlowType.Never,
                    callType,
                    Union(receiverFlow.Transfers, receiverFlow.Normal ? argumentsFlow.Transfers : null),
                    receiverFlow.Pending || (receiverFlow.Normal && argumentsFlow.Pending) || callType is null);
                if (callType is null)
                {
                    this.pending.Add(call);
                }

                break;
            case ParenthesizedKoto p:
                if (GetConditionBinding(p) is { } binding)
                {
                    // The declaration still produces Unit; the condition tests its bound value.
                    flow = this.Visit(p.Operand, reachable);
                    flow = flow with { Type = this.names.GetValueOrDefault(binding.NameKoto) };
                }
                else
                {
                    flow = this.Visit(p.Operand, reachable, expected);
                }

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
                        this.Error(node, "A null literal requires an expected raw-pointer Type.");
                    }
                }
                else if (!IsPointer(expected))
                {
                    this.Error(node, "A null literal requires an expected raw-pointer Type.");
                }

                break;
            case EqualsEqualsKoto or ExclamationEqualsKoto when IsNullLiteral(((BinaryKoto)node).Left) || IsNullLiteral(((BinaryKoto)node).Right):
                var comparison = (BinaryKoto)node;
                var nullOnLeft = IsNullLiteral(comparison.Left);
                var other = nullOnLeft ? comparison.Right : comparison.Left;
                var nullOperand = nullOnLeft ? comparison.Left : comparison.Right;
                var otherFlow = this.Visit(other, reachable);
                var nullFlow = this.Visit(nullOperand, reachable, otherFlow.Type);
                if (IsNullLiteral(other))
                {
                    this.Error(node, "A null comparison requires a pointer operand to determine its Type.");
                }

                flow = new(otherFlow.Normal, ControlFlowType.Boolean, otherFlow.Transfers, otherFlow.Pending || nullFlow.Pending);
                break;
            case AndKoto or OrKoto:
                var logical = (BinaryKoto)node;
                var left = this.Visit(logical.Left, reachable, ControlFlowType.Boolean);
                var right = this.Visit(logical.Right, reachable && left.Normal, ControlFlowType.Boolean);
                // Runtime short-circuiting may skip the right operand. Do not constant-fold it for reachability.
                flow = new(left.Normal, ControlFlowType.Boolean, Union(left.Transfers, left.Normal ? right.Transfers : null), left.Pending || right.Pending);
                break;
            case ConversionKoto conversion:
                flow = this.VisitChildSequence(conversion, reachable);
                var sourceType = this.nodes[conversion.Left].ExpressionType;
                var destinationType = this.types.GetDeclaredType(conversion.Right);
                var sourcePointer = IsPointer(sourceType);
                var destinationPointer = IsPointer(destinationType);
                if (sourcePointer || destinationPointer)
                {
                    this.CheckUnsafePermission(conversion);
                    if ((sourcePointer && destinationType is not null && !destinationPointer && destinationType.Name != "usize") ||
                        (destinationPointer && sourceType is not null && !sourcePointer && sourceType.Name is not ("usize" or "Never" or "integer literal")))
                    {
                        this.Error(conversion, "Pointer conversions support only another pointer Type or usize.");
                    }
                }

                flow = flow with { Type = destinationType, Pending = flow.Pending || destinationType is null };
                if (destinationType is null)
                {
                    this.pending.Add(conversion);
                }

                break;
            default:
                flow = this.VisitChildSequence(node, reachable);
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
            this.nodes[node] = info = this.RentInfo();
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

    private void CheckUnsafePermission(Koto node)
    {
        if (!KotoHelper.IsUnsafeContext(node))
        {
            this.Error(node, "This operation requires an Unsafe Block; unsafe func does not grant permission to its body.");
        }
    }

    private void VisitDeclarations(Koto container)
    {
        var start = this.childBuffer.Count;
        container.VisitChildren(this.childCollector);
        var end = this.childBuffer.Count;
        for (var i = start; i < end; i++)
        {
            this.Visit(this.childBuffer[i], true);
        }

        this.childBuffer.RemoveRange(start, end - start);
    }

    private Flow VisitChildSequence(Koto node, bool reachable)
    {
        var start = this.childBuffer.Count;
        node.VisitChildren(this.childCollector);
        var count = this.childBuffer.Count - start;
        var flow = this.VisitSequence(this.childBuffer, start, count, reachable);
        this.childBuffer.RemoveRange(start, count);
        return flow;
    }

    private Flow VisitSequence(IReadOnlyList<Koto> items, int start, int count, bool reachable)
    {
        var normal = true;
        var pendingCompletion = false;
        HashSet<JumpKoto>? transfers = null;
        List<DeferredBlockKoto>? registrations = null;
        for (var i = start; i < start + count; i++)
        {
            var item = items[i];
            var flow = this.Visit(item, reachable && normal);
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
                        this.blockedDeliveries.UnionWith(departing);
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

        var flow = this.Visit(body, true, body is CodeBlockKoto ? null : boundary.Expected);
        if (flow.Normal && !(body is CodeBlockKoto && flow.Pending))
        {
            boundary.Sources.Add(new(body, body is CodeBlockKoto ? ControlFlowType.Unit : flow.Type, true));
        }

        var finished = this.Finish(node, flow, boundary);
        var info = this.nodes[node];
        if (body is CodeBlockKoto && flow.Normal && !flow.Pending &&
            info.TargetResultType is { } resultType && resultType != ControlFlowType.Unit)
        {
            this.Error(body, "A non-Unit Block-bodied function cannot fall through.");
        }

        // The function's callable return type falls back to Never, while its target contract remains absent.
        info.FunctionResultType = boundary.Expected ?? info.TargetResultType ?? finished.Type;
        info.ExpressionType = this.types.GetExpressionType(node);
    }

    /// <summary>Determines whether a body contains an unselected directive group, excluding nested function bodies.</summary>
    private bool HasInvalidDirective(Koto node)
    {
        if (node is CompileTimeMatchKoto)
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

    private Flow VisitJump(JumpKoto jump, bool reachable)
    {
        var target = KotoHelper.ResolveTransferTarget(jump);
        this.targets[jump] = target;
        if (target is null)
        {
            this.Error(jump, $"No valid target for {jump.Keyword}.");
        }

        if (jump is ExitKoto && jump.Expression is not null && target is not (null or LoopKoto or CodeBlockKoto { Parent: LabeledKoto }))
        {
            this.Error(jump, "Only loop or an explicitly named Labeled Block accepts an exit result operand.");
        }

        if (jump is ExitKoto { Expression: null } && target is CodeBlockKoto { Parent: LabeledKoto } &&
            this.nodes.TryGetValue(target, out var targetInfo) && targetInfo.IsResultRequiring)
        {
            this.Error(jump, "A Result-requiring Labeled Block requires an explicit exit result operand.");
        }

        this.boundaries.TryGetValue(target ?? jump, out var boundary);
        var operand = jump.Expression is { } expression ? this.Visit(expression, reachable, boundary?.Expected) : new Flow(true, ControlFlowType.Unit);
        if (operand.Normal && jump is not ContinueKoto && boundary is not null)
        {
            var source = new ControlFlowResultSource(jump.Expression ?? jump, operand.Type, reachable) { Transfer = jump };
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
    private Flow VisitRequire(RequireKoto node, bool reachable)
    {
        var condition = this.Visit(node.Condition, reachable, ControlFlowType.Boolean);

        // Entry to the failure body is assumed independently of the condition, even for literal true (SPEC 14.11.2).
        var failure = this.Visit(node.ElseBody, reachable && condition.Normal);
        if (failure.Normal)
        {
            if (failure.Pending)
            {
                this.pending.Add(node);
            }
            else
            {
                this.Error(node.ElseBody, "A require failure body must not continue normally to the statement after require.");
            }
        }

        // Success continues with the enclosing scope; require false has no normal successor (SPEC 14.11.3).
        var transfers = Union(condition.Transfers, condition.Normal ? failure.Transfers : null);
        var normal = condition.Normal && LiteralCondition(node.Condition) != false;
        return new(normal, ControlFlowType.Unit, transfers, condition.Pending || (condition.Normal && failure.Pending));
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
        for (var index = 0; index < node.Branches.Count; index++)
        {
            var branch = node.Branches[index];
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
        var coverage = this.types.GetMatchCoverage(node, subject.Type);
        var exhaustive = coverage.IsExhaustive;

        if (exhaustive is null)
        {
            this.pending.Add(node);
        }
        else if (required && exhaustive == false)
        {
            boundary.InvalidResult = true;
            this.Error(node, coverage.Describe());
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
                // Guard acquisition/Loan verification is pending, but transfers and unsafe
                // operations must still be visited by syntax/control-flow analysis.
                var guardFlow = this.Visit(guard, reachable && subject.Normal, ControlFlowType.Boolean);
                this.CheckCompatibility(new(guard, guardFlow.Type, reachable && subject.Normal), ControlFlowType.Boolean);
                guardNormal = guardFlow.Normal;
                transfers = Union(transfers, guardFlow.Transfers);
                this.pending.Add(guard);
                pendingCompletion = true;
            }

            var body = this.VisitBranch(arm.Body, arm.Body is not CodeBlockKoto, reachable && subject.Normal && guardNormal, required, boundary);
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
                var literal = LiteralCondition(w.Condition);
                mayFinish = literal != true;
                enterBody = literal != false;
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

    private Flow VisitLabeledBlock(LabeledKoto labeled, CodeBlockKoto block, bool reachable, ControlFlowType? expected)
    {
        var required = KotoHelper.IsResultRequiringLabeledBlock(labeled);
        var boundary = this.Begin(block, required ? expected : ControlFlowType.Unit);
        this.nodes[block].IsResultRequiring = required;
        var flow = this.VisitSequence(block.Items, 0, block.Items.Count, reachable);
        if (flow.Normal)
        {
            if (!required)
            {
                boundary.Sources.Add(new(block, ControlFlowType.Unit, reachable));
            }
            else if (reachable && !flow.Pending)
            {
                boundary.InvalidResult = true;
                this.Error(block, "A Result-requiring Labeled Block cannot fall through without an explicit exit result.");
            }
            else if (reachable)
            {
                this.pending.Add(block);
            }
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

        var candidates = this.candidateLists.TryPop(out var list) ? list : new();
        var allNull = true;
        foreach (var source in boundary.Sources)
        {
            if (source.IsReachable &&
                (source.Transfer is not { } jump || !this.blockedDeliveries.Contains(jump)))
            {
                candidates.Add(source);
                allNull &= IsNullLiteral(source.Node);
            }
        }

        var targetType = boundary.Expected ?? (boundary.InferenceBlocked ? null : this.types.InferResultType(candidates));
        var candidateCount = candidates.Count;
        candidates.Clear();
        this.candidateLists.Push(candidates);

        var info = this.nodes[node];
        info.TargetResultType = targetType;
        info.CanCompleteNormally = flow.Normal;
        info.IsCompletionPending = flow.Pending;
        info.ExpressionType = boundary.InvalidResult ? null : !flow.Normal ? ControlFlowType.Never :
            flow.Pending ? null : candidateCount == 0 ? ControlFlowType.Never : targetType;
        if (candidateCount > 0 && targetType is null)
        {
            this.pending.Add(node);
            if (!boundary.InferenceBlocked && allNull)
            {
                this.Error(node, "Null results require an expected raw-pointer Type.");
            }
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
        if (node is NullLiteralKoto && IsPointer(type))
        {
            this.pending.Remove(node);
            this.nodes[node].ExpressionType = type;
            this.nodes[node].IsCompletionPending = false;
        }

        if (node is ParenthesizedKoto p && GetConditionBinding(p) is null)
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
                    this.Error(node, "Dereference requires a raw-pointer operand.");
                }

                return null;
            }

            if (rawPointer && node is PrefixPlusKoto or PrefixMinusKoto or PrefixPlusPlusKoto or PrefixMinusMinusKoto or PostfixIncrementKoto or PostfixDecrementKoto)
            {
                this.Error(node, "Unary pointer arithmetic is not defined.");
                return null;
            }

            if (node is NotKoto)
            {
                this.Constrain(unary.Operand, ControlFlowType.Boolean);
                return ControlFlowType.Boolean;
            }

            if (node is PrefixMinusKoto or PrefixPlusKoto)
            {
                if (operandType?.Name is "()" or "bool" or "string")
                {
                    this.Error(node, "A numeric unary operator requires a numeric operand.");
                }

                return operandType;
            }

            return null;
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
                        this.Error(node, "Pointer equality requires operands with the same pointer Type.");
                    }

                    return ControlFlowType.Boolean;
                }

                if (node is PlusKoto or MinusKoto or IndexKoto)
                {
                    this.CheckUnsafePermission(node);
                    if (!leftPointer || rightPointer || KotoHelper.UnwrapParentheses(binary.Right) is FromEndIndexKoto or RangeKoto)
                    {
                        this.Error(node, "Pointer arithmetic and indexing require a pointer on the left and a signed isize offset.");
                    }
                    else
                    {
                        this.Constrain(binary.Right, IsizeType);
                    }

                    return node is IndexKoto && leftPointer ? this.PointeeType(left.ExpressionType!) : left.ExpressionType;
                }

                if (node is LessThanKoto or LessThanEqualsKoto or GreaterThanKoto or GreaterThanEqualsKoto)
                {
                    this.Error(node, "Pointer ordering comparisons are not defined.");
                    return ControlFlowType.Boolean;
                }

                if (node is AsteriskKoto or SlashKoto or PercentKoto or LessThanLessThanKoto or GreaterThanGreaterThanKoto or
                    AmpersandKoto or BarKoto or CaretKoto or AsteriskEqualsKoto or SlashEqualsKoto or PercentEqualsKoto or
                    LessThanLessThanEqualsKoto or GreaterThanGreaterThanEqualsKoto or AmpersandEqualsKoto or BarEqualsKoto or CaretEqualsKoto)
                {
                    this.Error(node, "This pointer arithmetic operation is not defined.");
                    return null;
                }
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

    /// <summary>Appends direct children without descending, so traversal needs no iterator allocations.</summary>
    private sealed class ChildCollector(List<Koto> children) : KotoVisitor
    {
        public override void Visit(Koto node) => children.Add(node);
    }

    private readonly record struct Flow(bool Normal, ControlFlowType? Type, HashSet<JumpKoto>? Transfers = null, bool Pending = false);
}
