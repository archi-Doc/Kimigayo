// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // Analysis result and its diagnostic belong together.

/// <summary>A control-flow diagnostic associated with source syntax.</summary>
/// <param name="Node">The offending syntax.</param>
/// <param name="Message">The diagnostic text.</param>
public sealed record ControlFlowIssue(Koto Node, string Message)
{
    internal int Priority { get; init; } = 4;
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
    private readonly StructuralCompletion structural;
    private readonly Dictionary<Koto, ControlFlowNodeInfo> nodes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<JumpKoto, Koto?> targets = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Koto, Boundary> boundaries = new(ReferenceEqualityComparer.Instance);
    private readonly List<ControlFlowIssue> issues = new();
    private readonly List<ControlFlowIssue> warnings = new();
    private readonly HashSet<Koto> warningNodes = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Koto> pending = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<(Koto Node, string Message)> reported = new();
    private readonly Dictionary<IdentifierNameKoto, ControlFlowType?> names = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DeferredBlockKoto, Flow> cleanups = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Koto, DefaultCompletion> defaultCompletions = new(ReferenceEqualityComparer.Instance);

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

    private ControlFlowAnalysis(ControlFlowTypeSystem types)
    {
        this.types = types;
        this.structural = new(node => this.types.GetExpressionType(node) == ControlFlowType.Never || this.nodes.GetValueOrDefault(node)?.ExpressionType == ControlFlowType.Never);
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
        analysis.Visit(root, true);
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
        this.childBuffer.Clear();
        this.arrivedTransfers.Clear();
        this.normalTransferArrivals.Clear();
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

        foreach (var warning in this.warnings)
        {
            warning.Node.AddDiagnostic(DiagnosticCode.ControlFlowWarning_Kd, warning.Message);
        }
    }

    internal void Append(Koto root) => this.Visit(root, true);

    // Assumes entry to the resolved target, independently of its outer runtime
    // reachability. Dead transfers still supply result Types, not normal arrivals.
    internal bool ReachesTarget(JumpKoto jump) => this.normalTransferArrivals.Contains(jump);

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

    private void Warn(Koto node, string message, int priority = 4)
    {
        node = KotoHelper.UnwrapParentheses(node);
        if (this.warningNodes.Add(node))
        {
            this.warnings.Add(new(node, message) { Priority = priority });
            return;
        }

        for (var i = 0; i < this.warnings.Count; i++)
        {
            if (this.warnings[i].Node == node && priority < this.warnings[i].Priority)
            {
                this.warnings[i] = new(node, message) { Priority = priority };
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
        var primitive = type?.Name is "bool" or "char" or "integer literal" or "float literal" or
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

    private void WarnUnitTail(Koto body)
    {
        if (!this.structural.CanComplete(body))
        {
            return;
        }

        if (body is CodeBlockKoto block)
        {
            if (block.Items.Count > 0)
            {
                this.WarnUnitTail(block.Items[^1]);
            }

            return;
        }

        body = KotoHelper.UnwrapParentheses(body);
        if (body is LabeledKoto label)
        {
            this.WarnUnitTail(label.Target);
        }
        else if (body is DoKoto scoped)
        {
            this.WarnUnitTail(scoped.Body);
        }
        else if (body is IfKoto conditional)
        {
            for (var i = 0; i < conditional.Branches.Count; i++)
            {
                this.WarnUnitTail(conditional.Branches[i].Body);
            }

            if (conditional.ElseBody is { } other)
            {
                this.WarnUnitTail(other);
            }
        }
        else if (body is MatchKoto match && body is not TryKoto)
        {
            for (var i = 0; i < match.Arms.Count; i++)
            {
                this.WarnUnitTail(match.Arms[i].Body);
            }
        }
        else if (body is ExpressionKoto and not (UnitLiteralKoto or JumpKoto or LoopKoto or ForKoto or WhileKoto) &&
            this.nodes.GetValueOrDefault(body)?.ExpressionType is { } type && type != ControlFlowType.Unit && type != ControlFlowType.Never)
        {
            this.Warn(body, "Unit was inferred from this discarded tail; use yield, return, a named exit, or a single-item body to supply the value.", 1);
        }
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
                this.Warn(node, "The Result is discarded; propagate and use success (or write _ = try ...), handle it with match, or write _ = ... to ignore errors too.", 2);
            }
            else if (value is TryKoto && type is not null && type != ControlFlowType.Unit && type != ControlFlowType.Never)
            {
                this.Warn(node, "The extracted try success value is unused; use it or write _ = try ... .", 3);
            }
            else if (this.IsEffectFree(node))
            {
                this.Warn(node, "This effect-free value is discarded; use its value or add the intended return type.");
            }
        }

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
            case BinaryKoto { Akind: KotoKind.Equals } assignment when node.CodeContext.Compilation.Binding.PropertyCall(assignment.Left, PropertyAccessorKind.Set) is { } setter:
                flow = this.Visit(setter, reachable) with { Type = this.types.GetExpressionType(node) };
                break;
            case IdentifierNameKoto or MemberAccessKoto when node.CodeContext.Compilation.Binding.PropertyCall(node, PropertyAccessorKind.Get) is { } getter:
                flow = this.Visit(getter, reachable) with { Type = this.types.GetExpressionType(node) };
                break;
            case IdentifierNameKoto when node.CodeContext.Compilation.Binding.StorageProjection(node) is { } storage:
                flow = this.Visit(storage, reachable);
                break;
            case BinaryKoto { ComparisonCall: { } comparisonCall }:
                flow = this.Visit(comparisonCall, reachable) with { Type = this.types.GetExpressionType(node) };
                break;
            case MacroKoto { Formatting: { Acquisition: { } acquisition } tryWrite }:
                var rootFlow = this.Visit(acquisition, reachable);
                var normalRoot = rootFlow.Normal;
                foreach (var write in tryWrite.Writes)
                {
                    var part = this.Visit(write, reachable && rootFlow.Normal);
                    rootFlow = new(rootFlow.Normal && part.Normal, part.Type, Union(rootFlow.Transfers, rootFlow.Normal ? part.Transfers : null), rootFlow.Pending || part.Pending);
                }

                this.Visit(tryWrite.Outcome, reachable && normalRoot);
                flow = rootFlow with { Normal = normalRoot, Type = this.types.GetExpressionType(node) };
                break;
            case InterpolatedStringKoto { Formatting: { } formatting }:
                var formattingFlow = this.Visit(formatting.Heap, reachable);
                var adapterFlow = this.Visit(formatting.Adapter, reachable && formattingFlow.Normal);
                formattingFlow = new(formattingFlow.Normal && adapterFlow.Normal, formattingFlow.Type, Union(formattingFlow.Transfers, adapterFlow.Transfers), formattingFlow.Pending || adapterFlow.Pending);
                foreach (var write in formatting.Writes)
                {
                    var writeFlow = this.Visit(write, reachable && formattingFlow.Normal);
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

                this.VisitFunction(
                    function,
                    function.Body ?? function.ExpressionBody,
                    function.ReturnType,
                    KotoHelper.DiscardsFunctionBody(function) ? ControlFlowType.Unit : this.types.GetExpectedResultType(function));
                if (function.BoundClosure is not null)
                {
                    var closureType = this.types.GetExpressionType(function);
                    this.nodes[function].ExpressionType = closureType;
                    this.nodes[function].CanCompleteNormally = true;
                    return new(true, closureType); // Creation does not execute the body.
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
            case AttributeKoto { BindingState: BindingState.Resolved, LayoutMode: not null }:
                // SPEC 21.1.2: a checked layout attribute is declaration metadata;
                // its syntax argument is not a runtime call or string acquisition.
                return new(true, ControlFlowType.Unit);
            case CompileTimeSwitchKoto:
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
            case PropertyKoto storedProperty:
                // Stored declaration annotations (including Origin names) are not evaluations.
                flow = storedProperty.InitializerKoto is { } fieldInitializer ? this.Visit(fieldInitializer, reachable, this.types.GetDeclaredType(storedProperty.TypeKoto)) : new(true, ControlFlowType.Unit);
                for (var i = 0; i < storedProperty.Accessors.Count; i++)
                {
                    this.Visit(storedProperty.Accessors[i], reachable);
                }

                break;
            case LabeledKoto labeled:
                this.CheckLabel(labeled);
                flow = this.Visit(labeled.Target, reachable, expected);
                this.nodes[labeled] = this.nodes[labeled.Target];
                break;
            case DeferredBlockKoto deferred:
                var cleanupBoundary = this.Begin(deferred, ControlFlowType.Unit);
                this.cleanups[deferred] = this.Finish(deferred, this.Visit(deferred.Body, reachable), cleanupBoundary);
                this.nodes[deferred].ExpressionType = null;
                // Registration never evaluates its body. Its completion matters at scope exit only.
                return new(true, ControlFlowType.Unit);
            case UnsafeBlockKoto unsafeBlock:
                flow = this.Visit(unsafeBlock.Body, reachable);
                var unsafeInfo = this.RentInfo();
                unsafeInfo.CanCompleteNormally = flow.Normal;
                unsafeInfo.IsCompletionPending = flow.Pending;
                this.nodes[unsafeBlock] = unsafeInfo;
                return flow with { Type = ControlFlowType.Unit };
            case DoKoto scoped:
                flow = this.VisitDo(scoped, reachable, expected);
                break;
            case IfKoto conditional:
                flow = this.VisitIf(conditional, reachable, expected);
                break;
            case TryKoto propagation:
                flow = this.VisitMatch(propagation, reachable, this.types.GetExpressionType(propagation));
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
            case DiscardKoto discard:
                flow = this.Visit(discard.Operand, reachable) with { Type = ControlFlowType.Unit };
                break;
            case RequireKoto require:
                flow = this.VisitRequire(require, reachable);
                break;
            case TestVerificationKoto verification:
                var conditionFlow = this.Visit(verification.Condition, reachable, ControlFlowType.Boolean);
                var messageFlow = verification.Message is { } message ? this.Visit(message, reachable && conditionFlow.Normal, new("string")) : new Flow(true, ControlFlowType.Unit);
                flow = new(conditionFlow.Normal, ControlFlowType.Unit, Union(conditionFlow.Transfers, conditionFlow.Normal ? messageFlow.Transfers : null), conditionFlow.Pending || messageFlow.Pending);
                break;
            case CodeBlockKoto block:
                flow = this.VisitSequence(block.Items, 0, block.Items.Count, reachable);
                break;
            case InvocationKoto call when this.types.TryGetCallReceiver(call, out var receiver):
                if (call.Method is not FormattingKoto && call.BoundCall is { Target.CompilerFunction: CompilerFunctionKind.WriterWrite } selected)
                {
                    for (var i = 0; i < call.ArgumentNodes.Count; i++)
                    {
                        if (selected.ArgumentToParameter[i] == 1 && call.ArgumentNodes[i] is InterpolatedStringKoto literal)
                        {
                            this.Warn(literal, "This argument creates an owning string before writing. Use $tryWrite(writer, literal) to write directly and skip later expressions after failure.");
                        }
                    }
                }

                // A committed direct callee is a designator, not a function-value acquisition.
                // Bound receiver syntax precedes the explicit arguments exactly once.
                var receiverFlow = receiver is null ? new Flow(true, ControlFlowType.Unit) : this.Visit(receiver, reachable);
                var argumentsFlow = this.VisitSequence(call.ArgumentNodes, 0, call.ArgumentNodes.Count, reachable && receiverFlow.Normal);
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
                flow = this.Visit(p.Operand, reachable, expected);
                break;
            case MemberAccessKoto { BoundSymbol.Property: not null } member:
                // A selected property name is a designator, not an evaluated local.
                var ownerFlow = member.Left.BoundSymbol?.Kind == BindingSymbolKind.Container
                    ? new Flow(true, ControlFlowType.Unit) : this.Visit(member.Left, reachable);
                flow = new(ownerFlow.Normal, this.types.GetExpressionType(member), ownerFlow.Transfers, ownerFlow.Pending);
                break;
            case MacroKoto macro when this.types.GetExpressionType(macro) == ControlFlowType.Never:
                flow = this.Visit(macro.Operand, reachable, expected);
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
            case IsKoto { IsRuntimeTest: true } test:
                flow = this.Visit(test.Left, reachable) with { Type = ControlFlowType.Boolean };
                if (!this.types.IsBoundRuntimeTypeTest(test))
                {
                    // bool is known from syntax; target validity still requires Binding.
                    this.pending.Add(test);
                    flow = flow with { Pending = true };
                }

                break;
            case ConversionKoto conversion:
                // The target is checked Type syntax, not a runtime expression.
                flow = this.Visit(conversion.Left, reachable);
                var sourceType = this.nodes[conversion.Left].ExpressionType;
                var destinationType = this.types.GetDeclaredType(conversion.Right);
                var sourcePointer = IsPointer(sourceType);
                var destinationPointer = IsPointer(destinationType);
                // SPEC 5.4: same-Type acquisition needs no unsafe context.
                if ((sourcePointer || destinationPointer) && !(sourceType is BoundType && ReferenceEquals(sourceType, destinationType)))
                {
                    this.CheckUnsafePermission(conversion);
                    if ((sourcePointer && destinationType is not null && !destinationPointer && destinationType.Name != "usize") ||
                        (destinationPointer && sourceType is not null && !sourcePointer && sourceType.Name is not ("usize" or "Never" or "integer literal")))
                    {
                        this.Error(conversion, "Pointer conversions support only another pointer Type or usize.");
                    }
                }

                flow = flow with { Type = flow.Normal ? destinationType : ControlFlowType.Never, Pending = flow.Pending || destinationType is null };
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
            ? null : flow.Type;
        info.CanCompleteNormally = flow.Normal;
        info.IsCompletionPending = flow.Pending;
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
            return cached;
        }

        // Defaults can refer to later declarations or recursively select another
        // omitted default. A cycle stays pending instead of expanding without bound.
        this.defaultCompletions[value] = new(true, true);
        if (this.HasInvalidDirective(value))
        {
            return new(true, true);
        }

        var parameterType = this.types.GetDeclaredType(parameter.Type);
        var flow = this.Visit(value, true, parameterType);
        if (parameterType is not null)
        {
            this.CheckCompatibility(new(value, flow.Type, true), parameterType);
        }

        // Transfers belong to the declaration's internal targets, never the caller.
        var completion = new DefaultCompletion(flow.Normal, flow.Pending);
        this.defaultCompletions[value] = completion;
        return completion;
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
        StructuralCompletion.CollectEvaluationChildren(node, this.childBuffer, this.childCollector);
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

    private Flow VisitBodyItem(Koto item, bool reachable, ControlFlowType? expected = null)
    {
        var flow = this.Visit(item, reachable, expected);
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
        var baseFlow = node is FunctionKoto { BaseInitializer: { } initializer } ? this.Visit(initializer, true, null) : new Flow(true, ControlFlowType.Unit);
        var flow = this.VisitBodyItem(body, baseFlow.Normal, discards ? null : boundary.Expected);
        flow = baseFlow.Normal ? flow with { Transfers = Union(baseFlow.Transfers, flow.Transfers), Pending = baseFlow.Pending || flow.Pending } : baseFlow;
        if (!discards || (!flow.Pending && this.structural.CanComplete(body)))
        {
            boundary.Sources.Add(new(body, discards ? ControlFlowType.Unit : flow.Type, true));
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
            this.Error(body, "A non-Unit Block-bodied function cannot fall through.");
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

    private Flow VisitJump(JumpKoto jump, bool reachable)
    {
        var target = KotoHelper.ResolveTransferTarget(jump);
        this.targets[jump] = target;
        if (target is null)
        {
            this.Error(jump, $"No valid target for {jump.Keyword}.");
        }

        if (jump is YieldKoto { Label: null } && target is IfKoto or MatchKoto && !KotoHelper.IsValueContext(target))
        {
            this.Error(jump, "An unlabeled yield cannot target a discarded selection; name the intended target.");
        }

        this.boundaries.TryGetValue(target ?? jump, out var boundary);
        var operand = jump.Expression is { } expression ? this.Visit(expression, reachable, boundary?.Expected) : new Flow(true, ControlFlowType.Unit);
        if (jump is not ContinueKoto && boundary is not null)
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
        var failure = this.VisitBodyItem(node.ElseBody, reachable && condition.Normal);
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

        // Both condition outcomes remain static paths, including Boolean literals.
        var transfers = Union(condition.Transfers, condition.Normal ? failure.Transfers : null);
        var normal = condition.Normal;
        return new(normal, ControlFlowType.Unit, transfers, condition.Pending || (condition.Normal && failure.Pending));
    }

    private Flow VisitIf(IfKoto node, bool reachable, ControlFlowType? expected)
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
            var condition = this.Visit(branch.Condition, reachable && next, ControlFlowType.Boolean);
            if (next)
            {
                transfers = Union(transfers, condition.Transfers);
                pendingCompletion |= condition.Pending;
            }

            var chosen = next && condition.Normal;
            var body = this.VisitBranch(branch.Body, branch.Body.IsExpressionBody, reachable && chosen, required, boundary);
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
            var body = this.VisitBranch(otherwise, otherwise.IsExpressionBody, reachable && next, required, boundary);
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
            boundary.Sources.Add(new(node, ControlFlowType.Unit, reachable));
        }

        return this.Finish(node, new(normal, null, transfers, pendingCompletion), boundary);
    }

    private Flow VisitMatch(MatchKoto node, bool reachable, ControlFlowType? expected)
    {
        var subject = this.Visit(node.Expression, reachable);
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
                // Visit even checked-only guards, but propagate transfers only after
                // normally completing Subject evaluation.
                var guardFlow = this.Visit(guard, reachable && subject.Normal, ControlFlowType.Boolean);
                this.CheckCompatibility(new(guard, guardFlow.Type, reachable && subject.Normal), ControlFlowType.Boolean);
                guardNormal = guardFlow.Normal;
                if (subject.Normal)
                {
                    transfers = Union(transfers, guardFlow.Transfers);
                }

                pendingCompletion |= subject.Normal && guardFlow.Pending;
            }

            var body = this.VisitBranch(arm.Body, arm.Body is not CodeBlockKoto, reachable && subject.Normal && guardNormal, required, boundary);
            normal |= guardNormal && body.Normal;
            pendingCompletion |= subject.Normal && guardNormal && body.Pending;
            if (subject.Normal && guardNormal)
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
        var usesValue = expressionBody && required && KotoHelper.IsBodyExpression(expression);
        var flow = this.VisitBodyItem(expression, reachable, usesValue ? boundary.Expected : null);
        if (usesValue || (!flow.Pending && this.structural.CanComplete(expression)))
        {
            boundary.Sources.Add(new(expression, usesValue ? flow.Type : ControlFlowType.Unit, reachable));
        }

        if (!usesValue && flow.Pending)
        {
            boundary.InferenceBlocked = true;
            this.pending.Add(body);
        }

        if (body != expression)
        {
            this.nodes[body] = this.nodes[expression];
        }

        return flow;
    }

    private Flow VisitIteration(Koto node, bool reachable, ControlFlowType? expected)
    {
        Flow header;
        CodeBlockKoto body;
        var mayFinish = false;
        switch (node)
        {
            case ForKoto f:
                header = this.Visit(f.Iterable, reachable);
                body = f.Body;
                mayFinish = true;
                break;
            case WhileKoto w:
                if (KotoHelper.UnwrapParentheses(w.Condition) is BoolLiteralKoto { Value: true })
                {
                    this.Warn(w, "Use loop for unconditional iteration; while true retains a static false path.");
                }

                header = this.Visit(w.Condition, reachable, ControlFlowType.Boolean);
                body = w.Body;
                mayFinish = true;
                break;
            default:
                header = new(true, ControlFlowType.Unit);
                body = ((LoopKoto)node).Body;
                break;
        }

        var boundary = this.Begin(node, node is LoopKoto && KotoHelper.IsValueContext(node) ? expected : ControlFlowType.Unit);
        var bodyFlow = this.Visit(body, reachable && header.Normal);
        if (mayFinish && (node is ForKoto iteration ? this.structural.CanComplete(iteration.Iterable) : this.structural.CanComplete(((WhileKoto)node).Condition)))
        {
            boundary.Sources.Add(new(node, ControlFlowType.Unit, reachable && header.Normal));
        }

        var transfers = Union(header.Transfers, header.Normal ? bodyFlow.Transfers : null);
        return this.Finish(node, new(header.Normal && mayFinish, null, transfers, header.Pending || bodyFlow.Pending), boundary);
    }

    private Flow VisitDo(DoKoto node, bool reachable, ControlFlowType? expected)
    {
        var required = KotoHelper.IsValueContext(node);
        var boundary = this.Begin(node, required ? expected : ControlFlowType.Unit);
        this.nodes[node].IsResultRequiring = required;
        var flow = this.VisitBranch(node.Body, node.Body.IsExpressionBody, reachable, required, boundary);
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
        info.IsCompletionPending = flow.Pending;
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
                this.Error(node, "Null results require an expected raw-pointer Type.");
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
                this.WarnUnitTail(scoped.Body);
            }
            else if (node is IfKoto or MatchKoto)
            {
                this.WarnUnitTail(node);
            }
            else if (node is FunctionKoto { IsAnonymous: true, Body: { } body })
            {
                this.WarnUnitTail(body);
            }
        }

        return flow with { Type = info.ExpressionType };
    }

    private void CheckSources(Koto target, Boundary boundary, ControlFlowType type)
    {
        foreach (var source in boundary.Sources)
        {
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
            this.nodes[node].IsCompletionPending = false;
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
            this.CheckCompatibility(new(node, info.ExpressionType, false), type);
        }
    }

    private void CheckCompatibility(ControlFlowResultSource source, ControlFlowType type)
    {
        var compatible = this.types.IsCompatible(source, type);
        if (compatible == false)
        {
            var message = KotoHelper.UnwrapParentheses(source.Node) is TryKoto propagation
                ? Binding.DescribeTryPayloadFailure(propagation, type)
                : $"Result of type {source.Type?.Name} is incompatible with {type.Name}.";
            this.Error(source.Node, message);
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

            if (node is IndexKoto)
            {
                this.Constrain(binary.Right, IsizeType);
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

            if (node is AmpersandKoto or BarKoto or CaretKoto)
            {
                return left.ExpressionType;
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

    private readonly record struct DefaultCompletion(bool Normal, bool Pending);

    private readonly record struct Flow(bool Normal, ControlFlowType? Type, HashSet<JumpKoto>? Transfers = null, bool Pending = false);
}
