// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The HIR publisher (plan R2b): the Op, Res, Completion, Adjust and PlaceShape columns and the jump targets of every module, read from the facts
// the latest pass keeps; it decides nothing Binding has not decided. Tests call it in R2b and the MIR builder from R3; R7 writes the columns inline.
public sealed partial class Binding
{
    private HirPublisher? hirPublisher;
    private ulong hirVersion;

    // Where a node stands: evaluated, inside a Pattern (its match plan describes it), covered by its parent (the callee of a selected
    // call or construction, the call shape of a formatting macro, the name Type arguments apply to), or inside an Error, which the
    // builder replaces whole.
    [Flags]
    private enum HirPosition : byte
    {
        None = 0,
        Evaluated = 1,
        Pattern = 2,
        Covered = 4,
        Replaced = 8,
    }

    // Returns the pass the published columns describe (0: none), by which the MIR builder caches its program.
    internal ulong PublishHir()
    {
        if (this.running || this.Result == default || this.hirVersion == this.storageVersion)
        {
            return this.hirVersion;
        }

        this.hirVersion = this.storageVersion;
        var publisher = this.hirPublisher ??= new(this);
        foreach (var module in this.compilation.SourceModules)
        {
            publisher.Publish(module.Syntax, module.RootKoto);
        }

        publisher.Publish(this.Library.Kotonoha.Syntax, this.Library.Kotonoha.RootKoto);
        return this.hirVersion;
    }

    // One walk of the tree through its child slots; each node's position follows from the slot its parent holds it in.
    private sealed class HirPublisher(Binding binding) : KotoVisitor
    {
        // The parameter defaults the walk is inside: the function and the parameter's slot.
        private (Koto Function, int Slot)[] defaults = new (Koto, int)[4];
        private int defaultCount;
        private Koto? parent;
        private HirOp parentOp;
        private HirPosition position;
        private bool exclusive;

        // Binding's structural completion with its Never evidence, one per body (StructuralCompletion moves into Binding in R6).
        private StructuralCompletion? completion;
        private SyntaxTable? table;

        public override void Visit(Koto node)
        {
            var (parent, parentOp, outer, defaults, exclusive) = (this.parent, this.parentOp, this.position, this.defaultCount, this.exclusive);
            var position = this.PositionOf(node);
            var op = this.OpOf(node, position);
            if (op == HirOp.Body)
            {
                this.completion?.Clear();
            }

            this.exclusive = this.ExclusiveUse(node);
            if (node.SyntaxOwner is { Hir.Ops: { } ops, Hir.Res: { } res, Hir.Completions: { } completions, Hir.Places: { } places } && node.SyntaxId < ops.Length)
            {
                ops[node.SyntaxId] = op;
                res[node.SyntaxId] = this.ResOf(node);
                completions[node.SyntaxId] = op is HirOp.Unset or HirOp.Syntax or HirOp.Body ? HirCompletion.Unset
                    : (this.completion ??= new(binding.ResultNeverEvidence)).CanComplete(node) ? HirCompletion.Completes : HirCompletion.Diverges;
                if (completions[node.SyntaxId] != HirCompletion.Unset)
                {
                    places[node.SyntaxId] = new(this.KindOf(node, op, res[node.SyntaxId]), this.Layers(node));
                }
            }

            if (op is HirOp.Jump or HirOp.Continue)
            {
                var target = KotoHelper.ResolveTransferTarget((JumpKoto)node);
                HirTables.PlanFor(node, target is not null).Plan = target;
            }

            // A number literal's value as Binding fits it (SPEC 3.1, 13.5.4): its floating-point Type's bits, else the integer it names.
            if (op == HirOp.Literal && KotoHelper.SignedNumber(node, out var negative) is { } number)
            {
                var value = node.BoundType is { IsFloatingPoint: true } type ? ScalarTypes.TryFloatLiteral(number, type, negative, out var bits) ? (Int128)bits : null
                    : number.TryGetIntegerMagnitude(out var magnitude) ? unchecked((Int128)(negative ? 0 - magnitude : magnitude)) : (Int128?)null;
                HirTables.PlanFor(node, value is not null).Folded = value;
            }

            if (op is not (HirOp.Unset or HirOp.Error) && (op != HirOp.Syntax || (node is FunctionKoto && (position & HirPosition.Replaced) == 0)))
            {
                this.Consume(node, op);
            }

            (this.parent, this.parentOp, this.position) = (node, op, position);
            node.VisitChildren(this);
            if (node is ForKoto { Iteration: { } iteration } && op == HirOp.For)
            {
                this.Visit(iteration.Receiver); // The step's receiver, the iterator local the loop declares (SPEC 14.6.2).
            }

            (this.parent, this.parentOp, this.position, this.defaultCount, this.exclusive) = (parent, parentOp, outer, defaults, exclusive);
        }

        internal void Publish(SyntaxTable table, Koto root)
        {
            ref var hir = ref table.Hir;
            if (hir.Ops?.Length != hir.States.Length)
            {
                (hir.Ops, hir.Res, hir.Completions, hir.Adjusts, hir.Places) = (new HirOp[hir.States.Length], new HirRes[hir.States.Length], new HirCompletion[hir.States.Length], new HirAdjust[hir.States.Length], new HirPlace[hir.States.Length]);
            }
            else
            {
                Array.Clear(hir.Ops);
                Array.Clear(hir.Res!);
                Array.Clear(hir.Completions!);
                Array.Clear(hir.Adjusts!);
                Array.Clear(hir.Places!);
            }

            this.table = table;
            this.completion?.Clear();
            (this.parent, this.position, this.exclusive) = (null, HirPosition.None, false);
            this.Visit(root);
        }

        // The operand a parent projects, updates, groups or borrows.
        private static Koto? Operand(Koto? parent) => (parent as BinaryKoto)?.Left ?? (parent as UnaryKoto)?.Operand;

        // The position of a child: a declaration evaluates its body, defaults and initializer, an expression its operands, and
        // nothing evaluates Types, names it declares or selects, attributes, Origin clauses or a fill length.
        private HirPosition PositionOf(Koto node)
        {
            if (this.parentOp == HirOp.Error || (this.position & HirPosition.Replaced) != 0)
            {
                return HirPosition.Replaced;
            }

            var position = this.position & ~HirPosition.Covered;
            var body = this.parentOp is HirOp.Body or HirOp.Closure ? HirPosition.Evaluated : HirPosition.None;
            switch (this.parent)
            {
                case null or TypeKoto or AttributeKoto or OriginRelationKoto or DeclarationContainerKoto:
                    return HirPosition.None;
                case PropertyAccessorKoto accessor:
                    return ReferenceEquals(node, accessor.Body) ? body : HirPosition.None;
                case FunctionKoto function:
                    if (ReferenceEquals(node, function.Body) || ReferenceEquals(node, function.ExpressionBody) || ReferenceEquals(node, function.BaseInitializer))
                    {
                        return body;
                    }

                    for (var i = 0; i < function.Parameters.Count; i++)
                    {
                        if (ReferenceEquals(node, function.Parameters[i].DefaultValue))
                        {
                            if (this.defaultCount == this.defaults.Length)
                            {
                                Array.Resize(ref this.defaults, this.defaultCount * 2);
                            }

                            this.defaults[this.defaultCount++] = (function, i);
                            return HirPosition.Evaluated;
                        }
                    }

                    return HirPosition.None;
                case VariableKoto variable:
                    return ReferenceEquals(node, variable.InitializerKoto) && this.parentOp is HirOp.Body or HirOp.Let ? HirPosition.Evaluated : HirPosition.None;
                case MatchKoto match:
                    for (var i = 0; i < match.Arms.Count; i++)
                    {
                        if (ReferenceEquals(node, match.Arms[i].Pattern))
                        {
                            return HirPosition.Pattern;
                        }
                    }

                    return position;
                case ForKoto loop:
                    return ReferenceEquals(node, loop.Iterable) || ReferenceEquals(node, loop.Body) || ReferenceEquals(node, loop.Iteration?.Receiver) ? position : HirPosition.None;
                case InvocationKoto call: // A value call's callee is evaluated unless its receiver operation takes what the callee reaches.
                    return ReferenceEquals(node, call.Method) && (CallPlan.Of(call) is not { Kind: CalleeKind.Value } plan ||
                        (plan.ReceiverOperation.Source is { } source && !ReferenceEquals(source, node))) ? position | HirPosition.Covered : position;
                case MacroKoto macro:
                    return macro.Formatting is null ? position : position | HirPosition.Covered;
                case GenericsKoto generics:
                    return ReferenceEquals(node, generics.Identifier) ? position | HirPosition.Covered : HirPosition.None;
                case MemberAccessKoto or ConversionKoto or IsKoto:
                    return ReferenceEquals(node, ((BinaryKoto)this.parent).Left) ? position : HirPosition.None;
                case ArrayLiteralKoto array:
                    return ReferenceEquals(node, array.FillLength) ? HirPosition.None : position;
                case SyntaxFormKoto:
                    return position & HirPosition.Pattern;
                default:
                    return position;
            }
        }

        private HirOp OpOf(Koto node, HirPosition position)
        {
            if (node is FunctionKoto { IsAnonymous: false } or PropertyKoto or PropertyAccessorKoto)
            {
                return this.HasBody(node) ? HirOp.Body : HirOp.Syntax;
            }

            if (position != HirPosition.Evaluated || node is TypeKoto or AttributeKoto or OriginRelationKoto or EffectBoundKoto or
                DeclarationKoto and not (FieldKoto or FunctionKoto) or SyntaxFormKoto { Akind: not (KotoKind.InferredCase or KotoKind.RootName) })
            {
                return HirOp.Syntax;
            }

            // Binding rejected the node, or left it unresolved without the fact its Op needs.
            if (IsRecovery(node, out _) || node is CompileTimeSwitchKoto || node.BindingFailure != BindingFailure.None)
            {
                return HirOp.Error;
            }

            var op = node.BindingState == BindingState.Unvisited ? HirOp.Unset : this.KindOp(node);
            return op == HirOp.Unset && node.BindingState != BindingState.Unvisited ? HirOp.Error : op;
        }

        // SPEC 13.5: @move, @copy and a same-Type conversion acquire their operand, @follow selects a referent, the rest convert.
        private HirOp KindOp(Koto node) => node switch
        {
            BoolLiteralKoto or CharLiteralKoto or NumberLiteralKoto or StringLiteralKoto or UnitLiteralKoto or NullLiteralKoto => HirOp.Literal,
            UnaryKoto when KotoHelper.SignedNumber(node, out _) is not null && CallPlan.Of(node) is null => HirOp.Literal, // Binding fits a signed number once.
            InterpolatedStringKoto or MacroKoto when node.Formatting is not null => HirOp.Interpolation,
            InterpolatedStringKoto => HirOp.Syntax, // The text a $tryWrite writes: its segments and expressions are the macro's writes.
            LabeledKoto or ParenthesizedKoto or MacroKoto or ConversionKoto { ConversionBinding: ConversionBinding.Identity or ConversionBinding.Transfer } => HirOp.Group,
            ConversionKoto { ConversionBinding: ConversionBinding.Borrow or ConversionBinding.Address } => HirOp.Borrow,
            DereferenceKoto or ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PayloadFollow or ConversionBinding.PairFollow } => HirOp.Deref,
            ConversionKoto { ConversionBinding: ConversionBinding.ObjectCreation } => HirOp.Call,
            ConversionKoto conversion => conversion.ConversionBinding == ConversionBinding.None ? HirOp.Unset : HirOp.Convert,
            TupleLiteralKoto or ArrayLiteralKoto => HirOp.Aggregate,
            DictionaryLiteralKoto => HirOp.DictionaryLiteral,
            CodeBlockKoto => HirOp.Block,
            IfKoto => HirOp.If,
            TryKoto => HirOp.Try,
            MatchKoto => HirOp.Match,
            LoopKoto => HirOp.Loop,
            WhileKoto => HirOp.While,
            ForKoto => HirOp.For,
            DoKoto => HirOp.Do,
            ContinueKoto => HirOp.Continue,
            JumpKoto => HirOp.Jump,
            RequireKoto => HirOp.Require,
            DeferredBlockKoto => HirOp.Defer,
            UnsafeBlockKoto => HirOp.Unsafe,
            DiscardKoto => HirOp.Discard,
            NoInitKoto => HirOp.NoInit,
            TestVerificationKoto => HirOp.TestVerification,
            FieldKoto => HirOp.Let,
            FunctionKoto closure => BoundClosure.Of(closure) is null ? HirOp.Unset : HirOp.Closure, // Without its definition (ClosureDef).
            IsKoto { IsRuntimeTest: true } => HirOp.RuntimeTypeTest,
            IndexKoto index => (binding.IndexerCall(index, false) ?? binding.IndexerCall(index, true) ?? binding.ViewRangeCall(index)) is null ? HirOp.Index : HirOp.Call,
            RangeKoto or FromEndIndexKoto => CallPlan.Of(node) is null ? HirOp.Range : HirOp.Call,
            InvocationKoto => CallPlan.Of(node) is not null ? HirOp.Call : node.BoundSymbol?.Kind == BindingSymbolKind.EnumCase ? HirOp.Aggregate : HirOp.Unset,
            MemberAccessKoto { Right: NumberLiteralKoto } => HirOp.Field,
            MemberAccessKoto or IdentifierNameKoto or GenericsKoto or SyntaxFormKoto => this.NameOp(node),
            UnaryKoto { Akind: KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus or KotoKind.PostfixIncrement or KotoKind.PostfixDecrement } or
                BinaryKoto { Akind: > KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals } => HirOp.CompoundAssign,
            BinaryKoto { Akind: KotoKind.Equals } => HirOp.Assign,
            BinaryKoto { Akind: KotoKind.And or KotoKind.Or } => HirOp.Logical,
            UnaryKoto or BinaryKoto { Akind: not (KotoKind.OriginRelation or KotoKind.Is) } => CallPlan.Of(node) is null ? HirOp.PrimitiveOp : HirOp.Call,
            _ => HirOp.Unset,
        };

        // A resolved declaration whose body Binding bound builds it, unless the body is virtual and no instance applies it.
        private bool HasBody(Koto declaration) => declaration.BindingState == BindingState.Resolved && declaration switch
        {
            FunctionKoto function => ((Koto?)function.Body ?? function.ExpressionBody) is { BindingState: not BindingState.Unvisited } && !binding.IsInapplicableVirtualBody(function),
            PropertyAccessorKoto accessor => accessor.Body is { BindingState: not BindingState.Unvisited },
            _ => ((PropertyKoto)declaration).InitializerKoto is { BindingState: not BindingState.Unvisited },
        };

        // A name evaluates what its symbol is; a Property with a custom accessor is its accessor calls.
        private HirOp NameOp(Koto node) => node.BoundSymbol?.Kind switch
        {
            null => node is MemberAccessKoto { BindingState: BindingState.Resolved } ? HirOp.PrimitiveOp : HirOp.Unset, // The metadata of a sequence.
            BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Capture or BindingSymbolKind.PatternCandidate or
                BindingSymbolKind.Storage or BindingSymbolKind.LengthParameter or BindingSymbolKind.Function => HirOp.Name,
            BindingSymbolKind.Property => CallPlan.Of(node) is not null ? HirOp.Call : this.ResOf(node) == HirRes.Static ? HirOp.Name : HirOp.Field,
            BindingSymbolKind.EnumCase => HirOp.Aggregate,
            _ => HirOp.Syntax,
        };

        private HirRes ResOf(Koto node)
        {
            var symbol = node.BoundSymbol;
            switch (symbol?.Kind)
            {
                case null:
                    return HirRes.None;
                case BindingSymbolKind.Local or BindingSymbolKind.PatternCandidate:
                    return HirRes.Local;
                case BindingSymbolKind.Parameter:
                    // A parameter read in the default of a later parameter of its function names the prepared slot (SPEC 7.2.3).
                    for (var i = 0; i < this.defaultCount; i++)
                    {
                        if (ReferenceEquals(this.defaults[i].Function, symbol.Scope.Owner) && symbol.Slot < this.defaults[i].Slot)
                        {
                            return HirRes.PreparedParam;
                        }
                    }

                    return HirRes.Param;
                case BindingSymbolKind.Capture:
                    return HirRes.Capture;
                case BindingSymbolKind.Storage:
                    return HirRes.Field;
                case BindingSymbolKind.Property:
                    return symbol.Scope.Owner is GroupKoto ? HirRes.Static : HirRes.Field;
                case BindingSymbolKind.EnumCase:
                    return HirRes.Variant;
                default:
                    return HirRes.Decl;
            }
        }

        // The children a node's Op consumes (SPEC 3.5, 10.2, 15.1.5): its own operands first, then its calls' receivers and arguments by
        // their operations, in evaluation order; a deeper consumer, such as a setter taking an assignment's value, is written last.
        private void Consume(Koto node, HirOp op)
        {
            switch (node)
            {
                case FunctionKoto function:
                    for (var i = 0; i < function.Parameters.Count; i++)
                    {
                        this.Take(function.Parameters[i].DefaultValue);
                    }

                    this.Result(op is HirOp.Body or HirOp.Closure ? function.ExpressionBody : null);
                    break;
                case PropertyAccessorKoto or CodeBlockKoto or DoKoto:
                    this.Result(node is PropertyAccessorKoto accessor ? accessor.Body : node is DoKoto scoped ? scoped.Body : ((CodeBlockKoto)node).TrailingExpression);
                    break;
                case VariableKoto or PropertyKoto:
                    this.Take(node is VariableKoto variable ? variable.InitializerKoto : ((PropertyKoto)node).InitializerKoto);
                    break;
                case ParenthesizedKoto or MacroKoto or LabeledKoto when op == HirOp.Group:
                    this.Write(node is LabeledKoto label ? label.Target : ((UnaryKoto)node).Operand, new(HirAcquire.None, HirAdaptation.None)); // Its group acquires it.
                    break;
                case ConversionKoto { ConversionBinding: ConversionBinding.Transfer } transfer:
                    this.Write(transfer.Left, new(HirAcquire.Move, HirAdaptation.None));
                    break;
                case ConversionKoto or DereferenceKoto or IsKoto: // A borrow, a followed referent and a runtime test take their operand in place.
                    this.Take(node is DereferenceKoto dereference ? dereference.Operand : ((BinaryKoto)node).Left, inPlace: op is HirOp.Borrow or HirOp.Deref or HirOp.RuntimeTypeTest);
                    break;
                case MemberAccessKoto member when op is HirOp.Field or HirOp.PrimitiveOp: // A stored Field or Tuple element, or a sequence's metadata.
                    this.Take(member.Left, binding.receiverOperations.TryGetValue(member, out var receiver) ? HirAdjust.Of(receiver) : HirAdaptation.None, true);
                    break;
                case IndexKoto index when op == HirOp.Index:
                    this.Take(index.Left, inPlace: true);
                    this.Take(index.Right);
                    break;
                case BinaryKoto or UnaryKoto when op is HirOp.Assign or HirOp.CompoundAssign:
                    this.Write(node is BinaryKoto target ? target.Left : ((UnaryKoto)node).Operand, new(HirAcquire.None, HirAdaptation.None));
                    this.Take((node as BinaryKoto)?.Right);
                    break;
                case BinaryKoto or UnaryKoto or RangeKoto when op is HirOp.PrimitiveOp or HirOp.Logical or HirOp.Range:
                    this.Take(node is BinaryKoto binary ? binary.Left : node is RangeKoto range ? range.Start : ((UnaryKoto)node).Operand, inspect: op == HirOp.PrimitiveOp);
                    this.Take((node as BinaryKoto)?.Right ?? (node as RangeKoto)?.End, inspect: op == HirOp.PrimitiveOp);
                    break;
                case TupleLiteralKoto or ArrayLiteralKoto:
                    foreach (var element in node is TupleLiteralKoto tuple ? tuple.Elements : ((ArrayLiteralKoto)node).Elements)
                    {
                        this.Take(element);
                    }

                    break;
                case DictionaryLiteralKoto dictionary:
                    foreach (var entry in dictionary.Entries)
                    {
                        this.Take(entry.Key);
                        this.Take(entry.Value);
                    }

                    break;
                case IfKoto conditional:
                    for (var i = 0; i < conditional.Branches.Count; i++)
                    {
                        this.Take(conditional.Branches[i].Condition);
                        this.Result(conditional.Branches[i].Body);
                    }

                    this.Result(conditional.ElseBody);
                    break;
                case JumpKoto or WhileKoto or RequireKoto or TestVerificationKoto or DiscardKoto:
                    this.Take(node switch { JumpKoto jump => jump.Expression, WhileKoto loop => loop.Condition, RequireKoto require => require.Condition, DiscardKoto discard => discard.Operand, _ => ((TestVerificationKoto)node).Condition });
                    this.Take((node as TestVerificationKoto)?.Message);
                    break;
                case MatchKoto match:
                    var plan = BoundMatch.Of(match);
                    this.Subject(match.Expression, plan?.SubjectBorrow);
                    this.Bindings(plan);
                    for (var i = 0; i < match.Arms.Count; i++)
                    {
                        this.Take(match.Arms[i].Guard);
                        this.Result(match.Arms[i].Body);
                    }

                    break;
                case ForKoto loop:
                    this.Take(loop.Iterable);
                    var decomposition = BoundMatch.Of(loop);
                    for (var i = 0; decomposition is null && i < loop.Bindings.Count; i++)
                    {
                        this.Write(loop.Bindings[i], new(HirAcquire.Copy, HirAdaptation.None)); // SPEC 4.6.3.4: a ResolvedRange loop binds isize positions.
                    }

                    this.Bindings(decomposition);
                    break;
                case MacroKoto { Formatting.Acquisition.BoundCall.ArgumentOperations: [var writer, ..] }:
                    this.Take(writer.Source, HirAdjust.Of(writer)); // A $tryWrite acquires its writer operand (utf8-formatting 5.3).
                    break;
            }

            // An enum Case's payloads, then each call of the node: a value call's callee in its receiver mode (SPEC 7.6.3), a reference to the
            // callable passed as it is in that mode. Of a user index's index and indexUniq, an update or an exclusive borrow of the element
            // takes indexUniq (SPEC 4.6.9), which its plan row records for its ExpansionPlan.
            if (op == HirOp.Aggregate && binding.TryGetEnumConstruction(node, out var construction))
            {
                foreach (ref readonly var payload in construction!.PayloadOperations)
                {
                    this.Take(payload.Source, HirAdjust.Of(payload));
                }
            }

            CallPlan? unselected = null;
            if (node is IndexKoto)
            {
                var uniq = this.exclusive && binding.IndexerCall(node, true) is not null;
                HirTables.PlanFor(node, uniq).Mode = uniq ? (byte)1 : (byte)0;
                unselected = binding.IndexerCall(node, !uniq)?.CallStorage;
            }

            for (var i = 0; CallPlan.Of(node, i) is { } call; i++)
            {
                if (ReferenceEquals(call, unselected))
                {
                    continue;
                }

                if (call.Kind == CalleeKind.Value && call.ReceiverOperation.Source is null)
                {
                    var reference = call.CalleeType is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq } callee ? callee.Semantics : SemanticsKind.Owner;
                    var mode = call.ReceiverKind == SemanticsKind.Owner || reference == call.ReceiverKind ? HirAdaptation.None : reference != SemanticsKind.Owner ? HirAdaptation.ReferenceRead
                        : call.ReceiverKind == SemanticsKind.Uniq ? HirAdaptation.ExclusiveBorrow : HirAdaptation.SharedBorrow;
                    this.Take(call.CalleeValue, mode);
                }
                else
                {
                    this.Take(call.Kind == CalleeKind.Value ? call.ReceiverOperation.Source : call.Receiver, HirAdjust.Of(call.ReceiverOperation));
                }

                foreach (ref readonly var argument in call.ArgumentOperations)
                {
                    this.Take(argument.Source, HirAdjust.Of(argument));
                }
            }
        }

        // A body or operand that supplies its construct's value (SPEC 14.9).
        private void Result(Koto? source) => this.Take(source is not null && KotoHelper.IsValueContext(source) ? source : null);

        // A consumed child: an adaptation Binding recorded at the node itself (at its fixed expected Type, or as a receiver; an erasure to its
        // Function Type, SPEC 7.6.4) wins over its consumer's operation. A Place adapted or used in place is not acquired, an adapted value is
        // a Temporary, and an unadapted one taken by value, or erased, is acquired by the bare rule. A built-in operator's operand that is not
        // Copy, a comparison's, is inspected through a shared Loan from its evaluation instead (SPEC 13.4): a Place in place, a value other
        // than a literal in its temporary.
        private void Take(Koto? child, HirAdaptation adaptation = HirAdaptation.None, bool inPlace = false, bool inspect = false)
        {
            if (child is not null)
            {
                adaptation = binding.adaptations.TryGetValue(child, out var recorded) ? HirAdjust.Of(recorded, child.BoundType)
                    : HirTables.ErasedType(child) is not null ? HirAdaptation.Erasure : adaptation;
                var acquire = adaptation is not (HirAdaptation.None or HirAdaptation.Erasure) || inPlace ? this.IsPlace(child) ? HirAcquire.None : HirAcquire.Temporary
                    : this.Bare(child);
                if (inspect && adaptation == HirAdaptation.None && (acquire is HirAcquire.Move or HirAcquire.Reborrow or HirAcquire.ByCase ||
                    (acquire == HirAcquire.Temporary && child is not StringLiteralKoto && child.BoundType is { } type && binding.ProveCopy(type, child) != ConstraintProof.Proven)))
                {
                    (acquire, adaptation) = (acquire == HirAcquire.Temporary ? acquire : HirAcquire.None, HirAdaptation.SharedBorrow);
                }

                this.Write(child, new(acquire, adaptation));
            }
        }

        // Binding's bare acquisition of a Place (BareAcquire) as it is: a Move there is the explicit Move a Place whose Copy is unproven needs
        // (SPEC 3.5, TransferRequired), and only an Error proof leaves it unset; a value that is not a Place is a Temporary.
        private HirAcquire Bare(Koto child)
        {
            var place = this.IsPlace(child);
            if (!place || child.BoundType is not { } type)
            {
                return place ? HirAcquire.Unset : HirAcquire.Temporary;
            }

            var bare = binding.BareAcquire(type, child, out var proof);
            return bare == HirAcquire.Move && proof == ConstraintProof.Error ? HirAcquire.Unset : bare;
        }

        // Whether a node's consumer updates it or borrows it exclusively, directly or through the projections of a Place path, where the old
        // path selects a user index's indexUniq (SPEC 4.6.9); the parent is the node's while the node is visited.
        private bool ExclusiveUse(Koto node) => HirTables.AdjustOf(node).Adaptation == HirAdaptation.ExclusiveBorrow ||
            (this.parentOp == HirOp.Borrow && this.parent!.BoundType?.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq) ||
            ((this.parentOp is HirOp.Assign or HirOp.CompoundAssign || (this.exclusive && (this.parentOp is HirOp.Field or HirOp.Index || this.parent is ParenthesizedKoto))) &&
                ReferenceEquals(Operand(this.parent), node));

        // SPEC 3.4.1, 3.5, 7.1.1, 13.5.5.1, 15.1.3: the Place a node designates, as its Op, Res and Binding's element, follow and call facts
        // give it; a parenthesized Place is its operand's.
        private HirPlaceKind KindOf(Koto node, HirOp op, HirRes res) => op switch
        {
            HirOp.Name => res switch
            {
                HirRes.Local or HirRes.Param or HirRes.PreparedParam or HirRes.Capture => HirPlaceKind.Local,
                HirRes.Static => HirPlaceKind.Static,
                HirRes.Field => HirPlaceKind.Field, // An accessor's storage: Binding.StorageProjection, a Field of the implicit self.
                _ => HirPlaceKind.Value,
            },
            HirOp.Field => HirPlaceKind.Field,
            HirOp.Deref => binding.PairAdmitted(node) == SemanticsMask.None ? HirPlaceKind.Deref : HirPlaceKind.PairLayer,
            HirOp.Index => ElementAccess.IsSlicing(node) ? HirPlaceKind.Value : ElementAccess.StaticSelector((IndexKoto)node) < 0 ? HirPlaceKind.Index : HirPlaceKind.ConstIndex,
            HirOp.Call => ElementAccess.IsUserIndex(node) || ElementAccess.IsPlaceCall(binding.PropertyCall(node, PropertyAccessorKind.Get) ?? node) ? HirPlaceKind.CallResult : HirPlaceKind.Value,
            HirOp.Group when node is ParenthesizedKoto && KotoHelper.UnwrapParentheses(node) is var inner => this.KindOf(inner, this.OpOf(inner, HirPosition.Evaluated), this.ResOf(inner)),
            _ => HirPlaceKind.Value,
        };

        // The layers a node's consumer follows before it projects or borrows the node (SPEC 3.4.1, 13.5.5.1): every reference, object
        // handle and pair layer of the receiver of a stored Field, a Tuple element, a sequence's metadata or a built-in element, and the pair
        // layers Binding follows at a receiver or Subject down to their terminal target.
        private byte Layers(Koto node)
        {
            var projected = (this.parentOp is HirOp.Field or HirOp.Index || (this.parentOp == HirOp.PrimitiveOp && this.parent is MemberAccessKoto)) && ReferenceEquals(Operand(this.parent), node);
            var terminal = projected || !TryPairLayer(node.BoundType, out _, out _) || binding.ImplicitPairAdmitted(node) == SemanticsMask.None ? null : binding.PairTerminalOf(node);
            var type = node.BoundType;
            byte layers = 0;
            while ((projected || terminal is not null) && layers < 16 && !ReferenceEquals(type, terminal) && TyCtxt.Layer(type, out type, out _) != HirPlaceKind.Value)
            {
                layers++;
            }

            return layers;
        }

        // A bare Place (SPEC 3.5), a raw Place (SPEC 5.2), a static, or the storage a formatting plan prepared (its buffer, its writer).
        private bool IsPlace(Koto child) => IsBarePlace(child) || ElementAccess.IsRawPlace(child) || child is FormattingKoto { Operation: FormattingOperation.Storage } ||
            this.ResOf(KotoHelper.UnwrapParentheses(child)) == HirRes.Static;

        // SPEC 15.1.6: a Subject is acquired as written, but a bare Place is borrowed in place: its stored exclusive reference Reborrowed, a
        // stored exclusive reference reached through a shared layer read as one shared reference, and any other Place borrowed in its mode.
        private void Subject(Koto subject, BoundType? borrow)
        {
            if (borrow is null)
            {
                this.Take(subject);
                return;
            }

            var mode = borrow.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq ? HirAdaptation.ExclusiveBorrow
                : subject.BoundType is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq } ? HirAdaptation.ReferenceRead : HirAdaptation.SharedBorrow;
            this.Write(subject, ReferenceEquals(borrow, subject.BoundType) ? new(HirAcquire.Reborrow, HirAdaptation.None) : new(HirAcquire.None, mode));
        }

        // A Pattern binding its plan borrows, below a reference layer, designates the payload of its Case position or an element of its Tuple
        // position (SPEC 15.1.6); a binding of the whole Subject acquires it.
        private void Bindings(BoundMatch? plan)
        {
            for (var i = 0; plan is { IsCurrent: true } && i < plan.Positions.Count; i++)
            {
                if (plan.Positions[i] is { Kind: BoundPatternKind.Binding } position && HirAdjust.Of(position.Acquisition, position.AccessMode) is var adjust)
                {
                    var place = adjust.Acquire != HirAcquire.None || position.Parent < 0 ? HirPlaceKind.Unset
                        : plan.Positions[position.Parent].Kind == BoundPatternKind.Case ? HirPlaceKind.Downcast : HirPlaceKind.Field;
                    this.Write(position.Source, adjust, place);
                }
            }
        }

        // Only the nodes of the module being published; a call shared across modules names nodes of another.
        private void Write(Koto? node, HirAdjust adjust, HirPlaceKind place = HirPlaceKind.Unset)
        {
            if (node is not null && ReferenceEquals(node.SyntaxOwner, this.table) && this.table!.Hir.Adjusts is { } adjusts && node.SyntaxId < adjusts.Length)
            {
                adjusts[node.SyntaxId] = adjust;
                if (place != HirPlaceKind.Unset)
                {
                    this.table.Hir.Places![node.SyntaxId] = new(place, 0);
                }
            }
        }
    }
}
