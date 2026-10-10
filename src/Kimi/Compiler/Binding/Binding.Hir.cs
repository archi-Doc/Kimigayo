// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The HIR publisher (plan R2b): the Op and Res columns and the jump targets of every module, read from the facts the latest pass
// keeps; it decides nothing Binding has not decided. Tests call it in R2b and the MIR builder from R3; R7 writes the columns inline.
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

    internal void PublishHir()
    {
        if (this.running || this.Result == default || this.hirVersion == this.storageVersion)
        {
            return;
        }

        this.hirVersion = this.storageVersion;
        var publisher = this.hirPublisher ??= new(this);
        foreach (var module in this.compilation.SourceModules)
        {
            publisher.Publish(module.Syntax, module.RootKoto);
        }

        publisher.Publish(this.Library.Kotonoha.Syntax, this.Library.Kotonoha.RootKoto);
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

        public override void Visit(Koto node)
        {
            var (parent, parentOp, outer, defaults) = (this.parent, this.parentOp, this.position, this.defaultCount);
            var position = this.PositionOf(node);
            var op = this.OpOf(node, position);
            if (node.SyntaxOwner is { Hir.Ops: { } ops, Hir.Res: { } res } && node.SyntaxId < ops.Length)
            {
                ops[node.SyntaxId] = op;
                res[node.SyntaxId] = this.ResOf(node);
            }

            if (op is HirOp.Jump or HirOp.Continue)
            {
                var target = KotoHelper.ResolveTransferTarget((JumpKoto)node);
                HirTables.PlanFor(node, target is not null).Plan = target;
            }

            (this.parent, this.parentOp, this.position) = (node, op, position);
            node.VisitChildren(this);
            (this.parent, this.parentOp, this.position, this.defaultCount) = (parent, parentOp, outer, defaults);
        }

        internal void Publish(SyntaxTable table, Koto root)
        {
            ref var hir = ref table.Hir;
            if (hir.Ops?.Length != hir.States.Length)
            {
                (hir.Ops, hir.Res) = (new HirOp[hir.States.Length], new HirRes[hir.States.Length]);
            }
            else
            {
                Array.Clear(hir.Ops);
                Array.Clear(hir.Res!);
            }

            (this.parent, this.position) = (null, HirPosition.None);
            this.Visit(root);
        }

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
                    return ReferenceEquals(node, loop.Iterable) || ReferenceEquals(node, loop.Body) ? position : HirPosition.None;
                case InvocationKoto call:
                    return ReferenceEquals(node, call.Method) && CallPlan.Of(call)?.Kind != CalleeKind.Value ? position | HirPosition.Covered : position;
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
            InterpolatedStringKoto or MacroKoto { Formatting: not null } => HirOp.Interpolation,
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
            FunctionKoto => HirOp.Closure,
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
    }
}
