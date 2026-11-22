// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The evaluated Op classification and what a node's parent covers, part of the Adjust column's rule (Binding.Hir.Adjust), and the HIR
// publisher's evaluation positions.
// - The rule (lasting): a node's evaluated Op (OpOf: Body, Syntax, Error or the Op its form and Binding's facts give, published only with
//   the facts it guarantees, HasFacts) and Covers, whether a parent covers a child. Inputs: syntax slots and completed Binding facts; it
//   classifies only existing Binding decisions.
// - The publisher's positions: each child's position (PositionOf), the parts an Error or a closure keeps (Keeps, the one rule of an Error's
//   evaluated children, DECISIONS Z16; Parts) and the Op at a position, from the inputs the walk passes; the publisher owns the walk,
//   retained storage and column writes.
// Must not resolve syntax again or change the facts each Op guarantees (Binding.HasFacts).

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // Where a node stands: evaluated, inside a Pattern (its match plan describes it), covered by its parent (Covers), or inside an Error,
    // whose value the builder replaces, but for the parts it keeps (Keeps; DECISIONS R5-M63 keeps a closure definition's parts, Parts).
    [Flags]
    private enum HirPosition : byte
    {
        None = 0,
        Evaluated = 1,
        Pattern = 2,
        Covered = 4,
        Replaced = 8,
    }

    // Whether the lexer, the parser or Binding rejected a node: a recovery, a #switch that selected no arm (SPEC 19.3), a failure, a
    // literal the lexer could not read, which keeps its Type and lacks only its value (its published absence, CharLiteralKoto.Value), or
    // the value of an admitted misfit (MisfitWrite), whose fit Binding rejected: the write places a value derived from that failure.
    private static bool Rejected(Koto node) => IsRecovery(node, out _) || node is CompileTimeSwitchKoto or CharLiteralKoto { Value: null } ||
        node.BindingFailure != BindingFailure.None || MisfitWrite(node) is not null;

    // SPEC 23.1.2: a definition's parts (body, defaults, base initializer, initializer, accessor bodies) are published when Binding bound
    // them, whatever the definition's own failure, so their independent records stay visible; a check in them rests only where it reads
    // a failed input (rule W). Not published: parts no case of which is verified (Binding.CaseLimits' PartsVerified). A definition whose
    // own Type failed (a function's or accessor's result, a Property's Type) publishes them as any other: its slot of no Type is the
    // failed slot (HasFacts' slot rows, PLAN G1073).
    private bool PartsPublished(Koto definition) => HirTables.State(definition) != BindingState.Unvisited && this.PartsVerified(definition);

    // A published declaration whose body Binding bound builds it, a virtual body no instance applies included (checked, never generated:
    // virtual-dispatch 3, 6.3); an accessor's body is a part of its Property.
    private bool HasBody(Koto declaration) => this.PartsPublished(declaration) && declaration switch
    {
        FunctionKoto function => ((Koto?)function.Body ?? function.ExpressionBody) is { BindingState: not BindingState.Unvisited },
        PropertyAccessorKoto accessor => this.PartsPublished(accessor.Parent!) && accessor.Body is { BindingState: not BindingState.Unvisited },
        _ => ((PropertyKoto)declaration).InitializerKoto is { BindingState: not BindingState.Unvisited },
    };

    // Whether a parent covers a child, which it does not evaluate as a value: the callee of a selected call or construction (a value call's
    // callee is evaluated unless its receiver operation uses what the callee reaches), any child of a macro (its call shape), the name
    // Type arguments apply to.
    private bool Covers(Koto parent, Koto child) => parent switch
    {
        InvocationKoto call => ReferenceEquals(child, call.Method) && (CallPlan.Of(this, call) is not { Kind: CalleeKind.Value } plan ||
            (plan.ReceiverOperation.Source is { } source && !ReferenceEquals(source, child))),
        MacroKoto => true,
        GenericsKoto generics => ReferenceEquals(child, generics.Identifier),
        _ => false,
    };

    // A name evaluates what its symbol is; a Property with a custom accessor is its accessor calls. A projection path (its published
    // lookup PathEnd.Projection, SPEC 8.5.2, 9.6) names a Type whatever its symbol: a Type reading, which nothing evaluates.
    private HirOp NameOp(Koto node) => HirTables.LookupOf(node) == PathEnd.Projection ? HirOp.Syntax : node.BoundSymbol?.Kind switch
    {
        null => node is MemberAccessKoto { BindingState: BindingState.Resolved } ? HirOp.PrimitiveOp : HirOp.Name, // The metadata of a sequence.
        BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Capture or BindingSymbolKind.PatternCandidate or
            BindingSymbolKind.Storage or BindingSymbolKind.LengthParameter or BindingSymbolKind.Function => HirOp.Name,
        BindingSymbolKind.Property => CallPlan.Of(this, node) is not null ? HirOp.Call : IsStatic(node.BoundSymbol) ? HirOp.Name : HirOp.Field,
        BindingSymbolKind.EnumCase => HirOp.Aggregate,
        _ => HirOp.Syntax,
    };

    // A node's evaluated Op: a declaration's Body (with its slots stated, HasFacts, else Error) or Syntax; Syntax for a form nothing
    // evaluates (a Type, a name it declares, an attribute, an Origin clause or effect bound); Error for a node the lexer, the parser or
    // Binding rejected but an admitted misfit, which places (AdmittedMisfit, SPEC 13.7.1, 23.1.2: its value is the Error); else its form's
    // Op, published only with the facts it guarantees (HasFacts).
    private HirOp OpOf(Koto node)
    {
        if (node is FunctionKoto { IsAnonymous: false } or PropertyKoto or PropertyAccessorKoto)
        {
            return !this.HasBody(node) ? HirOp.Syntax : this.HasFacts(node, HirOp.Body) ? HirOp.Body : HirOp.Error;
        }

        if (node is TypeKoto or AttributeKoto or OriginRelationKoto or EffectBoundKoto or
            DeclarationKoto and not (FieldKoto or FunctionKoto) or SyntaxFormKoto { Akind: not (KotoKind.InferredCase or KotoKind.RootName) })
        {
            return HirOp.Syntax;
        }

        if (Rejected(node) && !AdmittedMisfit(node))
        {
            return HirOp.Error;
        }

        // A form no member describes keeps Unset, which the builder reports as Unsupported, unless Binding left it unresolved; an Op is
        // published only with the facts it guarantees (HasFacts), else the node is an Error.
        var op = node.BindingState == BindingState.Unvisited ? HirOp.Unset : this.KindOp(node);
        if (op == HirOp.Unset)
        {
            return node.BindingState is BindingState.Unresolved or BindingState.Invalid ? HirOp.Error : op;
        }

        return this.HasFacts(node, op) ? op : HirOp.Error;
    }

    // SPEC 13.5: @move, @copy and a same-Type conversion acquire their operand, @follow selects a referent, the rest convert.
    private HirOp KindOp(Koto node) => node switch
    {
        BoolLiteralKoto or CharLiteralKoto or NumberLiteralKoto or StringLiteralKoto or UnitLiteralKoto or NullLiteralKoto => HirOp.Literal,
        UnaryKoto when KotoHelper.SignedNumber(node, out _) is not null && CallPlan.Of(this, node) is null => HirOp.Literal, // Binding fits a signed number once.
        InterpolatedStringKoto or MacroKoto when node.Formatting is not null => HirOp.Interpolation,
        InterpolatedStringKoto => HirOp.Syntax, // The text a $tryWrite writes: its segments and expressions are the macro's writes.
        LabeledKoto or ParenthesizedKoto or ConversionKoto { ConversionBinding: ConversionBinding.Identity or ConversionBinding.Transfer } => HirOp.Group,
        ConversionKoto { ConversionBinding: ConversionBinding.Borrow or ConversionBinding.Address } => HirOp.Borrow,
        DereferenceKoto or ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PayloadFollow or ConversionBinding.PairFollow } => HirOp.Deref,
        ConversionKoto { ConversionBinding: ConversionBinding.ObjectCreation } => HirOp.Call,
        ConversionKoto => HirOp.Convert,
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
        IndexKoto index => (this.IndexerCall(index, false) ?? this.IndexerCall(index, true) ?? this.ViewRangeCall(index)) is null ? HirOp.Index : HirOp.Call,
        RangeKoto or FromEndIndexKoto => CallPlan.Of(this, node) is null ? HirOp.Range : HirOp.Call,
        // A Case construction is an application Binding takes as one (ConstructsCase, over the Case it selected at the callee, which a
        // construction that failed keeps too; SPEC 6.5.1, 6.5.2); an application of any other callee, a construction's value included, is a call.
        InvocationKoto invocation => CallPlan.Of(this, node) is null && ConstructsCase(invocation, invocation.Method.BoundSymbol) ? HirOp.Aggregate : HirOp.Call,
        MemberAccessKoto { Right: NumberLiteralKoto } => HirOp.Field,
        MemberAccessKoto or IdentifierNameKoto or GenericsKoto or SyntaxFormKoto => this.NameOp(node),
        UnaryKoto { Akind: KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus or KotoKind.PostfixIncrement or KotoKind.PostfixDecrement } or
            BinaryKoto { Akind: > KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals } => HirOp.CompoundAssign,
        BinaryKoto { Akind: KotoKind.Equals } => HirOp.Assign,
        BinaryKoto { Akind: KotoKind.And or KotoKind.Or } => HirOp.Logical,
        UnaryKoto or BinaryKoto { Akind: not (KotoKind.OriginRelation or KotoKind.Is) } => CallPlan.Of(this, node) is null ? HirOp.PrimitiveOp : HirOp.Call,
        _ => HirOp.Unset,
    };

    private sealed partial class HirPublisher
    {
        // SPEC 12, 13.7, 14: whether a form evaluates a child as a value, where it is evaluated, on every path through it: the paths are
        // structural completion's (StructuralCompletion.OnEveryPath, the one table of them); and of those parts, not a block's items,
        // statements its block builds (a rejected block is one the parser supplied), nor a write's, an update's or an increment's target,
        // which the write locates once after securing its value (SPEC 13.7.1, 13.7.2), not a value: a derived write's target is the MIR
        // builder's (DECISIONS G1022's DerivedWrite; G147 ruling 5).
        private static bool Unconditional(Koto parent, Koto child) => StructuralCompletion.OnEveryPath(parent, child) && parent switch
        {
            BinaryKoto { Akind: >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals } write => ReferenceEquals(child, write.Right),
            UnaryKoto { Akind: KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus or KotoKind.PostfixIncrement or KotoKind.PostfixDecrement } => false,
            CodeBlockKoto => false,
            _ => true,
        };

        // The position of a child: a declaration evaluates its body, defaults and initializer, an expression its operands, and
        // nothing evaluates Types, names it declares or selects, attributes, Origin clauses or a fill length; an Error replaces its
        // children but those its form evaluates that it keeps (Keeps).
        private HirPosition PositionOf(Koto node, Koto? parent, HirOp parentOp, HirPosition position)
        {
            if ((position & HirPosition.Replaced) != 0)
            {
                return HirPosition.Replaced;
            }

            var form = this.FormPosition(node, parent, parentOp, position & ~HirPosition.Covered);
            return parentOp != HirOp.Error || (form == HirPosition.Evaluated && this.Keeps(node, parent!)) ? form : HirPosition.Replaced;
        }

        // The position of a child in its parent's form, whatever Binding's verdict on the parent: an Error's form evaluates its children
        // as its Op would.
        private HirPosition FormPosition(Koto node, Koto? parent, HirOp parentOp, HirPosition position)
        {
            var body = parentOp is HirOp.Body or HirOp.Closure ? HirPosition.Evaluated : HirPosition.None;
            switch (parent)
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
                        if (ReferenceEquals(node, function.Parameters[i].DefaultValue) && binding.PartsPublished(function))
                        {
                            this.table!.Hir.Defaults!.Add(new(function, i));
                            return HirPosition.Evaluated;
                        }
                    }

                    return HirPosition.None;
                case VariableKoto variable:
                    return ReferenceEquals(node, variable.InitializerKoto) && parentOp is HirOp.Body or HirOp.Let or HirOp.Error ? HirPosition.Evaluated : HirPosition.None;
                case MatchKoto:
                    return node.SelectionRole == SelectionRole.Pattern ? HirPosition.Pattern : position;
                case ForKoto loop:
                    return ReferenceEquals(node, loop.Iterable) || ReferenceEquals(node, loop.Body) || ReferenceEquals(node, loop.Iteration?.Receiver) ? position : HirPosition.None;
                case InvocationKoto or MacroKoto:
                    return binding.Covers(parent, node) ? position | HirPosition.Covered : position;
                case GenericsKoto:
                    return binding.Covers(parent, node) ? position | HirPosition.Covered : HirPosition.None;
                case MemberAccessKoto or ConversionKoto or IsKoto:
                    return ReferenceEquals(node, ((BinaryKoto)parent).Left) ? position : HirPosition.None;
                case ArrayLiteralKoto array:
                    return ReferenceEquals(node, array.FillLength) ? HirPosition.None : position;
                case SyntaxFormKoto:
                    return position & HirPosition.Pattern;
                default:
                    return position;
            }
        }

        // SPEC 12, 14, 23.1.2 (DECISIONS Z16; R4-M88, R5-M63's compound Place and BU-106's do are its cases; G147 ruling 5): the one rule of
        // the children an Error keeps, evaluated in place before its derived value; the others stay replaced. The parent, a node Binding
        // rejected or left unresolved, keeps each child its form evaluates as a value on every path through it (Unconditional) that has an
        // Op the builder evaluates and that the builder can form, judged at the part itself, under any groups and a label (inner): a part
        // Binding resolved with its Op's facts and a Type, or an Error whose form is a value (not Syntax: a rejected Type or container name
        // in a qualifier is none) and rests on causes (RestingCauses), which keeps its own children by this rule. Stay replaced: an Error
        // Binding left unresolved without causes of its own, such as a closure whose parameters the failed call was to give their Types,
        // whose value rests on the parent's failure; a part without a Type that is no Error, since no destination Type holds it (a
        // construct, PLAN G121's kept-construct design, its parts' problems lost; a noinit); a function's name Binding resolved, whose value
        // its consumer forms (its erasure, which a kept part has none of; one left unresolved is an Error, HasFacts); and a part holding a
        // jump published without failure whose target row is the Error, a form a jump can target (StructuralCompletion.IsTransferTarget),
        // since the builder resolves such a row against the targets it opened, and an Error opens none (its InternalInvariant_Kd;
        // DECISIONS BU-106).
        private bool Keeps(Koto child, Koto parent)
        {
            var op = binding.OpOf(child);
            if (op is HirOp.Unset or HirOp.Syntax || !Unconditional(parent, child))
            {
                return false;
            }

            var inner = KotoHelper.UnwrapParentheses(child);
            inner = inner is LabeledKoto label ? label.Target : inner;
            var innerOp = ReferenceEquals(inner, child) ? op : binding.OpOf(inner);
            var formed = innerOp == HirOp.Error
                ? binding.KindOp(inner) != HirOp.Syntax && (inner.BindingState == BindingState.Resolved || binding.HasRestingCauses(inner))
                : (HirTables.Meaning(inner) is not null || HirTables.Meaning(child) is not null) &&
                    !(innerOp == HirOp.Name && HirTables.Symbol(inner) is { Kind: BindingSymbolKind.Function });
            return formed && !(StructuralCompletion.IsTransferTarget(parent) && (this.targets ??= new(binding)).Targeted(child, parent));
        }

        // The Op a node's parts are published under (DECISIONS R5-M63): a closure Binding bound in this pass, at an evaluated or a replaced
        // position, whose definition it completed keeps its parts as a Closure whatever its creation is (built, failed, or part of a
        // replaced parent); another node's parts are its Op's. Bound is "visited this pass": a creation whose slot failed is Unresolved
        // with no failure of its own (PLAN G1110); the parts' facts are Binding.ClosureParts (Binding.Hir.Facts).
        private HirOp Parts(Koto node, HirOp op, HirPosition position)
        {
            var bound = (position & (HirPosition.Evaluated | HirPosition.Replaced)) != 0 && node.BindingState != BindingState.Unvisited;
            return node is FunctionKoto { IsAnonymous: true } function && bound && binding.ClosureParts(function) ? HirOp.Closure : op;
        }

        // The Op at a position: a declaration's at any, another node's only where it is evaluated (Syntax elsewhere).
        private HirOp OpOf(Koto node, HirPosition position)
            => position == HirPosition.Evaluated || node is FunctionKoto { IsAnonymous: false } or PropertyKoto or PropertyAccessorKoto
                ? binding.OpOf(node) : HirOp.Syntax;

        // SPEC 14.5.2 (DECISIONS BU-101, BU-106): whether a jump in a part of a construct (the part's subtree), one the builder builds through
        // its target row (its Op not Error), has the construct as that row (HirTables.PlanOf, written by the jump binding before
        // publication). A failed jump is built as a derived Opaque and reads no target, so it does not count.
        private sealed class TargetWalk(Binding binding) : KotoVisitor
        {
            private Koto? target;
            private bool found;

            internal long Visited { get; private set; }

            public override void Visit(Koto node)
            {
                if (this.found)
                {
                    return;
                }

                this.Visited++;
                if (node is JumpKoto jump && binding.OpOf(jump) != HirOp.Error && ReferenceEquals(HirTables.PlanOf(jump).Plan, this.target))
                {
                    this.found = true;
                    return;
                }

                node.VisitChildren(this);
            }

            internal bool Targeted(Koto part, Koto construct)
            {
                (this.target, this.found) = (construct, false);
                this.Visit(part);
                this.target = null;
                return this.found;
            }
        }
    }
}
