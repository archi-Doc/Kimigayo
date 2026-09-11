// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Builds and solves whole-value and enum-construction ownership CFGs using committed Binding operations.</summary>
public sealed partial class OwnershipAnalysis
{
    private readonly Compilation compilation;
    private readonly List<OwnershipBody> bodies = new();
    private readonly List<OwnershipBody> bodyPool = new();
    private readonly List<OwnershipIssue> issues = new();
    private readonly Collector collector;
    private readonly List<Registration> locals = new();
    private readonly List<Registration> temporaries = new();
    private readonly List<LoopFrame> loops = new();
    private readonly List<int> arguments = new();
    private ControlFlowAnalysis? flow;
    private OwnershipBody body = null!;
    private int current;
    private int resultPlace;
    private int normalExit;
    private int abortExit;
    private int registrationSequence;

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

        this.Invalidate();
        this.supportedTypes.Clear();
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

        if (!binding.Result.IsComplete || binding.Obligations.Count != 0)
        {
            return this.Result;
        }

        this.collector.Visit(root);
        var errors = 0;
        var unsupported = 0;
        for (var i = 0; i < this.issues.Count; i++)
        {
            if (this.issues[i].Failure == OwnershipFailure.Unsupported)
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

    public void ReportDiagnostics()
    {
        for (var i = 0; i < this.issues.Count; i++)
        {
            var issue = this.issues[i];
            issue.Source.AddDiagnostic(issue.Failure switch
            {
                OwnershipFailure.UninitializedUse => DiagnosticCode.UninitializedPlace_Kd,
                OwnershipFailure.PossiblyMovedUse => DiagnosticCode.MovedPlace_Kd,
                OwnershipFailure.ReassignedLet => DiagnosticCode.ReassignedLet_Kd,
                _ => DiagnosticCode.UnsupportedOwnership_Kd,
            });
        }
    }

    internal void Invalidate()
    {
        this.Result = default;
        for (var i = 0; i < this.bodies.Count; i++)
        {
            this.bodies[i].IsVerified = false;
        }

        this.bodies.Clear();
        this.issues.Clear();
    }

    private void Build(FunctionKoto function)
    {
        if (this.bodies.Count == this.bodyPool.Count)
        {
            this.bodyPool.Add(new());
        }

        this.body = this.bodyPool[this.bodies.Count];
        this.body.Reset(function);
        this.bodies.Add(this.body);
        this.locals.Clear();
        this.temporaries.Clear();
        this.loops.Clear();
        this.arguments.Clear();
        this.selections.Clear();
        this.activeDecompositions.Clear();
        this.patternStorageNeeded.Clear();
        this.registrationSequence = 0;
        this.current = -1;
        this.Emit(OwnershipOperationKind.Entry, function);
        this.normalExit = this.New(OwnershipOperationKind.Exit, function);
        this.abortExit = this.New(OwnershipOperationKind.Exit, function);
        this.resultPlace = this.Place(function, function.BoundSymbol?.Type ?? BoundType.Unit, OwnershipPlaceKind.Result, true);
        if (function.Captures is { Length: > 0 })
        {
            this.Unsupported(function);
        }

        for (var i = 0; i < function.Parameters.Count; i++)
        {
            var parameter = function.Parameters[i];
            var type = parameter.Type.BoundType;
            var place = this.Place(parameter.Type, type, OwnershipPlaceKind.Parameter, false);
            this.body.SymbolPlaces[this.compilation.Binding.ParameterSymbol(function, i)] = place;
            this.locals.Add(new(place, parameter.Type, this.registrationSequence++));
            this.Emit(OwnershipOperationKind.Produce, parameter.Type, place);
            if (parameter.IsOptional || parameter.DefaultValue is not null)
            {
                this.Unsupported(parameter.DefaultValue ?? parameter.Type);
            }
        }

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
            var value = this.Expression(expression);
            this.Emit(OwnershipOperationKind.Write, expression, this.resultPlace, value);
        }
        else
        {
            this.Unsupported(function);
        }

        this.Cleanup(0, 0, function, CleanupReason.Return);
        this.Emit(OwnershipOperationKind.Deliver, function, this.resultPlace);
        this.Connect(this.current, this.normalExit, OwnershipEdgeKind.Return);
        this.body.Solve();
        for (var i = 0; i < this.body.IssueStorage.Count; i++)
        {
            this.issues.Add(this.body.IssueStorage[i]);
        }

        this.body.IsVerified = this.body.IssueStorage.Count == 0 && function.BindingState == BindingState.Resolved;
    }

    private int Place(Koto source, BoundType? type, OwnershipPlaceKind kind, bool mutable, AcquisitionKind? plannedAcquisition = null)
    {
        var id = this.body.PlaceStorage.Count;
        type ??= BoundType.Unit;
        // Never has no value storage. Its result marker is only used on unreachable
        // delivery nodes; control-flow checking rejects any normal completion.
        var neverResult = kind == OwnershipPlaceKind.Result && ReferenceEquals(type, BoundType.Never);
        var invalidCopy = false;
        var acquisition = plannedAcquisition.GetValueOrDefault();
        if (plannedAcquisition is null)
        {
            // Primitive classification needs no Constraint environment (SPEC 3.5.1).
            var proof = type.Kind == BoundTypeKind.Primitive && (!ReferenceEquals(type, BoundType.Never) || neverResult)
                ? (type.Name == "string" ? ConstraintProof.Refuted : ConstraintProof.Proven)
                : this.compilation.Binding.ProveCopy(type, source);
            invalidCopy = proof == ConstraintProof.Error;
            acquisition = proof == ConstraintProof.Proven ? AcquisitionKind.Copy : proof == ConstraintProof.Refuted ? AcquisitionKind.Move : AcquisitionKind.CopyOrMove;
        }

        this.body.PlaceStorage.Add(new(id, source, type, kind, mutable, acquisition));
        this.body.IsConcrete &= type.Kind != BoundTypeKind.Parameter;
        if (invalidCopy || !(neverResult || type.Kind == BoundTypeKind.Parameter || this.SupportsType(type)))
        {
            this.Unsupported(source);
        }

        return id;
    }

    private int Temporary(Koto source, bool produce = true)
    {
        // A non-completing block/selection may reserve a result destination, but
        // Never has no produced value and no temporary cleanup registration.
        var kind = !produce && ReferenceEquals(source.BoundType, BoundType.Never) ? OwnershipPlaceKind.Result : OwnershipPlaceKind.Temporary;
        var id = this.Place(source, source.BoundType, kind, true);
        if (produce)
        {
            this.Emit(OwnershipOperationKind.Produce, source, id);
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
        if (symbol is not null && this.body.SymbolPlaces.TryGetValue(symbol, out var id))
        {
            return id;
        }

        this.Unsupported(source);
        return -1;
    }

    private int Use(Koto source, int place, PlaceUseKind use, AcquisitionKind? acquisition = null)
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

        // Only locals and parameters reach here; temporaries transfer without a Place use.
        this.CheckAcquisition(place, acquisition);
        var value = this.Temporary(source, false);
        this.Emit(OwnershipOperationKind.Consume, source, place, value, acquisition ?? this.body.PlaceStorage[place].Acquisition);
        return this.RegisterTemporary(value);
    }

    private void Block(CodeBlockKoto block, int destination = -1)
    {
        var mark = this.locals.Count;
        for (var i = 0; i < block.Items.Count; i++)
        {
            var item = block.Items[i];
            var temps = this.temporaries.Count;
            if (destination >= 0 && block.HasTrailingExpression && i == block.Items.Count - 1)
            {
                var value = this.Expression(item);
                this.Emit(OwnershipOperationKind.Write, item, destination, value);
            }
            else
            {
                this.Statement(item);
            }

            this.Cleanup(temps, this.locals.Count, item, CleanupReason.ExpressionEnd);
            this.temporaries.RemoveRange(temps, this.temporaries.Count - temps);
        }

        this.Cleanup(this.temporaries.Count, mark, block, CleanupReason.ScopeExit);
        this.locals.RemoveRange(mark, this.locals.Count - mark);
    }

    private void Statement(Koto node)
    {
        if (node is FunctionKoto or DeclarationContainerKoto or AliasKoto)
        {
            return;
        }

        if (node is FieldKoto field)
        {
            var id = this.Place(field, field.BoundType, OwnershipPlaceKind.Local, field.VariableKind == VariableKind.Var);
            if (field.BoundSymbol is { } symbol)
            {
                this.body.SymbolPlaces[symbol] = id;
            }

            this.locals.Add(new(id, field, this.registrationSequence++));
            this.Emit(OwnershipOperationKind.Declare, field, id);
            if (field.InitializerKoto is { } initializer)
            {
                var value = this.Expression(initializer);
                this.Emit(OwnershipOperationKind.Write, field, id, value);
            }
        }
        else if (node is DeferredBlockKoto deferred)
        {
            this.locals.Add(new(-1, deferred, this.registrationSequence++));
            this.Unsupported(deferred);
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
        if (this.compilation.Binding.TryGetEnumConstruction(node, out var construction))
        {
            return this.ConstructEnum(node, construction!);
        }

        switch (node)
        {
            case ParenthesizedKoto parentheses:
                return this.Expression(parentheses.Operand, use, acquisition);
            case IdentifierNameKoto:
                return this.Use(node, this.Local(node), use, acquisition);
            case StringLiteralKoto or NumberLiteralKoto or BoolLiteralKoto or CharLiteralKoto or UnitLiteralKoto:
                return this.Temporary(node);
            case TupleLiteralKoto tuple when tuple.Elements.Count == 0:
                return this.Temporary(node);
            case TupleTypeKoto { ElementNodes.Count: 0 }:
                return this.Temporary(node);
            case InvocationKoto call:
                return this.Call(call);
            case BinaryKoto binary:
                return this.Binary(binary);
            case IfKoto conditional:
                return this.Conditional(conditional);
            case MatchKoto match:
                return this.Match(match);
            case WhileKoto loop:
                this.Loop(loop);
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

                if (!block.HasTrailingExpression)
                {
                    this.Emit(OwnershipOperationKind.Produce, block, blockValue);
                }

                return this.RegisterTemporary(blockValue);
            case UnaryKoto unary when node.Akind is KotoKind.Not or KotoKind.PrefixPlus or KotoKind.PrefixMinus or KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus or KotoKind.PostfixIncrement or KotoKind.PostfixDecrement:
                this.Expression(unary.Operand, PlaceUseKind.Read);
                if (node.Akind is KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus or KotoKind.PostfixIncrement or KotoKind.PostfixDecrement)
                {
                    this.Emit(OwnershipOperationKind.Write, node, this.Local(KotoHelper.UnwrapParentheses(unary.Operand)));
                }

                return this.Temporary(node);
            default:
                this.Unsupported(node);
                return -1;
        }
    }

    private int Binary(BinaryKoto binary)
    {
        var assignment = binary.Akind is >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals;
        if (assignment)
        {
            var target = KotoHelper.UnwrapParentheses(binary.Left);
            if (binary.Akind != KotoKind.Equals)
            {
                this.Expression(binary.Left, PlaceUseKind.Read);
                if (binary.Left.BoundType?.IsNumeric != true)
                {
                    this.Unsupported(binary);
                }
            }

            var input = this.Expression(binary.Right);
            this.Emit(OwnershipOperationKind.Write, binary, this.Local(target), input);
            return this.Temporary(binary);
        }

        if (binary is AndKoto or OrKoto)
        {
            var output = this.Temporary(binary, false);
            this.Expression(binary.Left, PlaceUseKind.Read);
            var branch = this.Emit(OwnershipOperationKind.Branch, binary.Left);
            var evaluate = this.New(OwnershipOperationKind.Branch, binary.Right);
            var skip = this.New(OwnershipOperationKind.Branch, binary);
            var join = this.New(OwnershipOperationKind.Branch, binary);
            var literal = KotoHelper.UnwrapParentheses(binary.Left) as BoolLiteralKoto;
            var evaluateWhen = binary is AndKoto;
            if (literal is null || literal.Value == evaluateWhen)
            {
                this.Connect(branch, evaluate, evaluateWhen ? OwnershipEdgeKind.True : OwnershipEdgeKind.False);
            }

            if (literal is null || literal.Value != evaluateWhen)
            {
                this.Connect(branch, skip, evaluateWhen ? OwnershipEdgeKind.False : OwnershipEdgeKind.True);
            }

            this.current = evaluate;
            this.Expression(binary.Right, PlaceUseKind.Read);
            this.Emit(OwnershipOperationKind.Produce, binary, output);
            this.Connect(this.current, join);
            this.current = skip;
            this.Emit(OwnershipOperationKind.Produce, binary, output);
            this.Connect(this.current, join);
            this.current = join;
            return this.RegisterTemporary(output);
        }

        var left = this.Expression(binary.Left, PlaceUseKind.Read);
        this.Expression(binary.Right, PlaceUseKind.Read);
        if (left >= 0 && this.body.PlaceStorage[left] is { Kind: not OwnershipPlaceKind.Temporary, Acquisition: not AcquisitionKind.Copy } &&
            KotoHelper.UnwrapParentheses(binary.Right) is not (IdentifierNameKoto or StringLiteralKoto))
        {
            // Retaining a non-Copy operand view across effectful RHS evaluation needs a Loan.
            this.Unsupported(binary);
        }

        if (binary is PlusKoto && ReferenceEquals(binary.BoundType, BoundType.String))
        {
            this.Unsupported(binary);
        }

        return this.Temporary(binary);
    }

    private int Conditional(IfKoto conditional)
    {
        var output = this.Temporary(conditional, false);
        var join = this.New(OwnershipOperationKind.Branch, conditional);
        for (var i = 0; i < conditional.Branches.Count; i++)
        {
            var branch = conditional.Branches[i];
            this.Expression(branch.Condition, PlaceUseKind.Read);
            var test = this.Emit(OwnershipOperationKind.Branch, branch.Condition);
            var yes = this.New(OwnershipOperationKind.Branch, branch.Body);
            var no = this.New(OwnershipOperationKind.Branch, conditional);
            var literal = KotoHelper.UnwrapParentheses(branch.Condition) as BoolLiteralKoto;
            if (literal?.Value != false)
            {
                this.Connect(test, yes, OwnershipEdgeKind.True);
            }

            if (literal?.Value != true)
            {
                this.Connect(test, no, OwnershipEdgeKind.False);
            }

            this.current = yes;
            this.Block(branch.Body, output);
            if (!branch.Body.HasTrailingExpression)
            {
                this.Emit(OwnershipOperationKind.Produce, branch.Body, output);
            }

            this.Connect(this.current, join);
            this.current = no;
        }

        if (conditional.ElseBody is { } otherwise)
        {
            this.Block(otherwise, output);
            if (!otherwise.HasTrailingExpression)
            {
                this.Emit(OwnershipOperationKind.Produce, otherwise, output);
            }
        }
        else
        {
            this.Emit(OwnershipOperationKind.Produce, conditional, output);
        }

        this.Connect(this.current, join);
        this.current = join;
        if (!this.flow!.Nodes[conditional].CanCompleteNormally)
        {
            this.current = -1;
            return -1;
        }

        return this.RegisterTemporary(output);
    }

    private int Call(InvocationKoto call)
    {
        if (call.BoundCall is not { } plan)
        {
            this.Unsupported(call);
            return -1;
        }

        var mark = this.arguments.Count;
        if (plan.Receiver is { } receiver)
        {
            this.arguments.Add(this.Argument(receiver, plan.ReceiverOperation.Kind));
        }

        for (var i = 0; i < call.ArgumentNodes.Count; i++)
        {
            this.arguments.Add(this.Argument(call.ArgumentNodes[i], plan.ArgumentOperations[i].Kind));
        }

        if (plan.Target.Declaration is FunctionKoto target && target.Parameters.Count != this.arguments.Count - mark)
        {
            this.Unsupported(call); // Default evaluation needs its own operation sequence.
        }

        for (var i = mark; i < this.arguments.Count; i++)
        {
            if (this.arguments[i] >= 0)
            {
                this.Emit(OwnershipOperationKind.CallEntry, call, this.arguments[i]);
            }
        }

        this.arguments.RemoveRange(mark, this.arguments.Count - mark);
        var invoke = this.Emit(OwnershipOperationKind.Call, call);
        this.Connect(invoke, this.abortExit, OwnershipEdgeKind.Abort);
        if (ReferenceEquals(call.BoundType, BoundType.Never))
        {
            this.current = -1;
            return -1;
        }

        return this.Temporary(call);
    }

    private int Argument(Koto argument, ArgumentOperationKind kind, AcquisitionKind? acquisition = null)
    {
        if (kind == ArgumentOperationKind.Value)
        {
            var value = this.Expression(argument, acquisition: acquisition);
            this.CheckAcquisition(value, acquisition);
            return value;
        }

        // A borrowed source keeps its value and responsibility; it never enters the callee.
        this.Expression(argument, PlaceUseKind.Borrow);
        this.Unsupported(argument);
        return -1;
    }

    private void Loop(WhileKoto loop)
    {
        var head = this.Emit(OwnershipOperationKind.Branch, loop);
        var exit = this.New(OwnershipOperationKind.Branch, loop);
        var mark = this.temporaries.Count;
        this.Expression(loop.Condition, PlaceUseKind.Read);
        this.Cleanup(mark, this.locals.Count, loop.Condition, CleanupReason.ExpressionEnd);
        this.temporaries.RemoveRange(mark, this.temporaries.Count - mark);
        var test = this.Emit(OwnershipOperationKind.Branch, loop.Condition);
        var enter = this.New(OwnershipOperationKind.Branch, loop.Body);
        var literal = KotoHelper.UnwrapParentheses(loop.Condition) as BoolLiteralKoto;
        if (literal?.Value != false)
        {
            this.Connect(test, enter, OwnershipEdgeKind.True);
        }

        if (literal?.Value != true)
        {
            this.Connect(test, exit, OwnershipEdgeKind.False);
        }

        this.loops.Add(new(loop, head, exit, this.locals.Count, this.temporaries.Count));
        this.current = enter;
        this.Block(loop.Body);
        this.Connect(this.current, head, OwnershipEdgeKind.Back);
        this.loops.RemoveAt(this.loops.Count - 1);
        this.current = exit;
    }

    private int Jump(JumpKoto jump)
    {
        var value = jump.Expression is { } expression ? this.Expression(expression) : -1;
        var target = this.flow!.Targets.GetValueOrDefault(jump);
        if (jump is ReturnKoto && ReferenceEquals(target, this.body.Function))
        {
            this.Emit(OwnershipOperationKind.Write, jump, this.resultPlace, value);
            this.Cleanup(0, 0, jump, CleanupReason.Return);
            this.Emit(OwnershipOperationKind.Deliver, jump, this.resultPlace);
            this.Connect(this.current, this.normalExit, OwnershipEdgeKind.Return);
        }
        else if (jump is YieldKoto && this.TryGetSelection(target, out var selection))
        {
            this.Emit(OwnershipOperationKind.Write, jump, selection.Result, value);
            this.Cleanup(selection.Temporaries, selection.Locals, jump, CleanupReason.SelectionResult);
            this.Connect(this.current, selection.Join);
        }
        else
        {
            if (jump.Expression is not null)
            {
                this.Unsupported(jump); // Value-bearing loop transfers need a secured loop-result Place.
            }

            var found = false;
            for (var i = this.loops.Count - 1; i >= 0; i--)
            {
                var loop = this.loops[i];
                if (ReferenceEquals(loop.Source, target) && jump is ExitKoto or ContinueKoto)
                {
                    this.Cleanup(loop.Temporaries, loop.Locals, jump, CleanupReason.LoopTransfer);
                    this.Connect(this.current, jump is ContinueKoto ? loop.Head : loop.Exit, jump is ContinueKoto ? OwnershipEdgeKind.Back : OwnershipEdgeKind.Normal);
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                this.Unsupported(jump);
            }
        }

        this.current = -1;
        return -1;
    }

    private void Cleanup(int tempStart, int localStart, Koto source, CleanupReason reason)
    {
        var start = this.body.CleanupStepStorage.Count;
        var edge = this.current >= 0 ? this.body.EdgeStorage.Count : -1;
        // Merge lexical registrations without allocating or removing live outer entries.
        // Separate marks let ExpressionEnd clean temporaries without ending a local's lifetime.
        var temporary = this.temporaries.Count - 1;
        var local = this.locals.Count - 1;
        while (temporary >= tempStart || local >= localStart)
        {
            var registration = temporary >= tempStart && (local < localStart || this.temporaries[temporary].Sequence > this.locals[local].Sequence)
                ? this.temporaries[temporary--] : this.locals[local--];
            if (registration.IsSubject)
            {
                this.CleanupSubject(registration.Place, source);
            }
            else
            {
                this.CleanupPlace(registration.Place, registration.Source, source);
            }
        }

        var count = this.body.CleanupStepStorage.Count - start;
        if (count != 0)
        {
            this.body.CleanupPlanStorage.Add(new(edge < this.body.EdgeStorage.Count ? edge : -1, start, count, reason));
        }
    }

    private void CleanupPlace(int place, Koto declaration, Koto source)
    {
        var operation = this.Emit(OwnershipOperationKind.Cleanup, source, place);
        this.body.OperationSteps[operation] = this.body.CleanupStepStorage.Count;
        this.body.CleanupStepStorage.Add(new(operation, place, declaration, place < 0 ? CleanupAction.Unsupported : CleanupAction.Skip));
    }

    private int New(OwnershipOperationKind kind, Koto source, int place = -1, int input = -1, AcquisitionKind acquisition = AcquisitionKind.None)
    {
        var id = this.body.OperationStorage.Count;
        this.body.OperationStorage.Add(new(kind, source, place, input, acquisition));
        this.body.EdgeHeads.Add(-1);
        this.body.IncomingEdges.Add(-1);
        this.body.OperationSteps.Add(-1);
        return id;
    }

    private int Emit(OwnershipOperationKind kind, Koto source, int place = -1, int input = -1, AcquisitionKind acquisition = AcquisitionKind.None)
    {
        var id = this.New(kind, source, place, input, acquisition);
        this.Connect(this.current, id);
        this.current = id;
        return id;
    }

    private void Connect(int from, int to, OwnershipEdgeKind kind = OwnershipEdgeKind.Normal)
    {
        if (from < 0)
        {
            return;
        }

        this.body.EdgeStorage.Add(new(from, to, kind, this.body.EdgeHeads[from]));
        this.body.EdgeHeads[from] = this.body.EdgeStorage.Count - 1;
        this.body.IncomingEdges[to] = this.body.EdgeStorage.Count - 1;
    }

    private void Unsupported(Koto source)
    {
        this.Emit(OwnershipOperationKind.Unsupported, source);
        this.body.IssueStorage.Add(new(source, OwnershipFailure.Unsupported));
    }

    private readonly record struct Registration(int Place, Koto Source, int Sequence, bool IsSubject = false);

    private readonly record struct LoopFrame(Koto Source, int Head, int Exit, int Locals, int Temporaries);

    private sealed class Collector : KotoVisitor
    {
        private readonly OwnershipAnalysis owner;

        internal Collector(OwnershipAnalysis owner)
        {
            this.owner = owner;
        }

        public override void Visit(Koto node)
        {
            if (node is FunctionKoto function)
            {
                // Requirement declarations and foreign imports have no body to verify.
                if (function.Body is not null || function.ExpressionBody is not null || !(function.IsRequirement || Parser.HasLibraryImport(function.AttributeChain)))
                {
                    this.owner.Build(function);
                }
            }
            else if (node is PropertyAccessorKoto)
            {
                this.owner.issues.Add(new(node, OwnershipFailure.Unsupported));
            }

            node.VisitChildren(this);
        }
    }
}
