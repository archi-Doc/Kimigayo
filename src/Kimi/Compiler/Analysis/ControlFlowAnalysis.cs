// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // Analysis result and its diagnostic belong together.

/// <summary>A control-flow diagnostic associated with source syntax.</summary>
/// <param name="Node">The offending syntax.</param>
/// <param name="Code">The code of the requirement.</param>
public sealed record ControlFlowIssue(Koto Node, DiagnosticCode Code)
{
    /// <summary>Gets the message formatted from the catalog.</summary>
    public string Message => DiagnosticEntries.TryGet(this.Code, out var entry) ? entry.FormatMessage(null, null) : this.Code.ToString();
}

/// <summary>The control-flow facts of an analyzed expression or boundary.</summary>
public sealed class ControlFlowNodeInfo
{
    /// <summary>Gets a value indicating whether normal completion is possible within the construct.</summary>
    public bool CanCompleteNormally { get; internal set; }
}

/// <summary>Analyzes lexical transfers, reachability and require failure bodies; the discard, while true, owning write, Unsafe
/// Block, jump target, label and function fallthrough checks are Binding's HIR lints.</summary>
/// <remarks>
/// Run after compile-time directive selection. Bodies containing invalid directive groups are skipped.
/// Types, Never results, match coverage and selected calls are Binding's facts; a node Binding has not resolved leaves its
/// completion pending.
/// </remarks>
public sealed class ControlFlowAnalysis
{
    private readonly Dictionary<Koto, ControlFlowNodeInfo> nodes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<JumpKoto, Koto?> targets = new(ReferenceEqualityComparer.Instance);
    private readonly List<ControlFlowIssue> issues = new();
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

    private ControlFlowAnalysis()
    {
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
        this.nodes.Clear();
        this.targets.Clear();
        this.issues.Clear();
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
        this.Visit(root);
    }

    /// <summary>Records the definite errors; ownership analysis discards earlier ones when it analyzes again.</summary>
    public void ReportDiagnostics()
    {
        // The require fallthrough check is structural, independent of Binding, but rests on a recovery that guessed what it reads.
        foreach (var issue in this.issues)
        {
            if (this.GuessedBy(issue) is { } syntax)
            {
                issue.Node.ReportDerived(DiagnosticRequirement.ControlFlow, syntax);
            }
            else
            {
                issue.Node.Report(DiagnosticRequirement.ControlFlow, issue.Code);
            }
        }
    }

    internal void Append(Koto root) => this.Visit(root);

    // An omitted default with pending completion cannot be expanded by ownership; the same finite cycle boundary applies.
    internal bool DefaultCompletionPending(Koto expression) => !this.defaultCompletions.TryGetValue(expression, out var completion) || completion.Pending;

    internal bool RecursiveDefault(Koto expression) => this.recursiveDefaults.Contains(expression);

    // An expression Binding has not resolved leaves its completion unknown; a recovery completes as its written operands do,
    // and an Error expression is not evaluated.
    private static bool Unknown(Koto node)
        => node.BindingState != BindingState.Resolved && node is ExpressionKoto and not ErrorKoto && node.CodeContext.RecoveryCause(node) is null;

    private static bool IsBoundRuntimeTypeTest(Koto expression)
        => expression is IsKoto { BindingState: BindingState.Resolved, BoundRuntimeTest: not null };

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

    // SPEC 10.2: the Type an expression supplies is its one adaptation's, else its own; null while Binding has not resolved it.
    private BoundType? SuppliedType(Koto node)
        => node.BindingState != BindingState.Resolved ? null
            : this.binding.TryGetAdaptation(node, out var adaptation) ? adaptation.Type : node.ErasedFunctionType ?? node.BoundType;

    private bool IsNever(Koto node) => ReferenceEquals(this.SuppliedType(node), BoundType.Never);

    private bool IsBoundConstruction(Koto expression) => this.binding.TryGetEnumConstruction(expression, out _);

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

    // The syntax Errors of the recoveries that guessed the normal completion a require fallthrough check reads, which Binding's one
    // guess rule judges (Binding.GuessedCompletion), or null when the check reads written syntax alone.
    private DiagnosticKey[]? GuessedBy(ControlFlowIssue issue) => this.binding.GuessedCompletion(issue.Node, issue.Node);

    private void Error(Koto node, DiagnosticCode code)
    {
        if (this.reported.Add((node, code)))
        {
            this.issues.Add(new(node, code));
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
                // SPEC 4.6.1, 4.6.2: prefix ^ and range syntax that construct a value are their synthesized PositionSyntax
                // calls, whose arguments may themselves be synthesized `^x` constructions.
                flow = this.Visit(rangeValue);
                break;
            case IndexKoto when this.binding.ViewRangeCall(node) is { } viewRange:
                flow = this.Visit(viewRange);
                break;
            case IndexKoto keyed when this.binding.ResolvedKeyCall(keyed) is { } resolvedKey:
                // SPEC 4.6.3: the receiver is evaluated, then a synthesized call resolves the key against the length of that
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

                if (function.ClosureAsCreated is not null && this.defaultDepth > 0)
                {
                    // SPEC 7.2.3: creating a closure inside a default completes without running its body, so the
                    // default's completion never waits on it. The body is visited once the outermost default's completion
                    // is recorded; a call there that omits the same default, such as a recursive call, then reads that
                    // completion instead of an unfinished cycle.
                    this.deferredClosures.Add(function);
                    return this.CreatedClosure(function);
                }

                this.VisitFunctionBody(function);
                if (function.ClosureAsCreated is not null)
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
                // SPEC 8.5: associated-Type parameters and formation Types have no runtime evaluation.
                return new(true);
            case EffectBoundKoto:
                // SPEC 8.11.1: an effect item is declaration metadata; its selector and Name are never evaluated.
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
                flow = this.Visit(labeled.Target);
                this.nodes[labeled] = this.NodeInfo(labeled.Target);
                break;
            case DeferredBlockKoto deferred:
                this.Begin(deferred);
                this.cleanups[deferred] = this.Finish(deferred, this.Visit(deferred.Body));
                // Registration never evaluates its body. Its completion matters at scope exit only.
                return new(true);
            case UnsafeBlockKoto unsafeBlock:
                flow = this.Visit(unsafeBlock.Body);
                break;
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

    // A body's flow; it is pending while a written or getter contract is unresolved (Binding.DeclaredResult). Binding judges a
    // fallthrough at its end (Binding.EndArrival).
    private void VisitFunction(Koto node, Koto? body, bool contract)
    {
        if (body is null || this.HasInvalidDirective(body))
        {
            return;
        }

        this.Begin(node);
        if (contract && Binding.DeclaredResult(node) is null)
        {
            this.pending.Add(node);
        }

        var baseFlow = node is FunctionKoto { BaseInitializer: { } initializer } ? this.Visit(initializer) : new Flow(true);
        var flow = this.VisitBodyItem(body);
        flow = baseFlow.Normal ? flow with { Transfers = Union(baseFlow.Transfers, flow.Transfers), Pending = baseFlow.Pending || flow.Pending } : baseFlow;
        this.Finish(node, flow);
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
        this.targets[jump] = KotoHelper.ResolveTransferTarget(jump);
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

        // Entry to the failure body is assumed independently of the condition, even for literal true (SPEC 14.11).
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
        return flow;
    }

    /// <summary>Appends direct children without descending, so traversal needs no iterator allocations.</summary>
    private sealed class ChildCollector(List<Koto> children) : KotoVisitor
    {
        public override void Visit(Koto node) => children.Add(node);
    }

    private readonly record struct DefaultCompletion(bool Normal, bool Pending);

    private readonly record struct Flow(bool Normal, HashSet<JumpKoto>? Transfers = null, bool Pending = false);
}
