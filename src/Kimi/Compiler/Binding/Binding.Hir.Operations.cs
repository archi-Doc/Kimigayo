// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The evaluated Op classification and what a node's parent covers, part of the Adjust column's rule (Binding.Hir.Adjust), and the HIR
// publisher's evaluation positions.
// - The rule (lasting): a node's evaluated Op (OpOf: Body, Syntax, Error or the Op its form and Binding's facts give, published only with
//   the facts it guarantees, HasFacts) and Covers, whether a parent covers a child. Inputs: syntax slots and completed Binding facts; it
//   classifies only existing Binding decisions.
// - The publisher's positions: each child's position (PositionOf), the parts an Error or a closure keeps (Keeps, Parts) and the Op at a
//   position, from the inputs the walk passes; the publisher owns the walk, retained storage and column writes.
// Must not resolve syntax again or change the facts each Op guarantees (Binding.HasFacts).

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // Where a node stands: evaluated, inside a Pattern (its match plan describes it), covered by its parent (Covers), or inside an Error,
    // whose value the builder replaces (DECISIONS R5-M63 keeps a closure definition's parts and a failed compound Place's resolved
    // operands, R4-M88 an unresolved operation's operands, BU-106 a rejected do's resolved parts).
    [Flags]
    private enum HirPosition : byte
    {
        None = 0,
        Evaluated = 1,
        Pattern = 2,
        Covered = 4,
        Replaced = 8,
    }

    // A definition's parts (body, defaults, initializer, accessor bodies) are published only when Binding resolved the definition: those
    // of an unresolved one, such as a definition beyond the case bound (Binding.CaseLimits), are not verified.
    private static bool PartsPublished(Koto definition) => definition.BindingState == BindingState.Resolved;

    // Whether the lexer, the parser or Binding rejected a node: a recovery, a #switch that selected no arm (SPEC 19.3), a failure, a
    // literal the lexer could not read, which keeps its Type and lacks only its value (its published absence, CharLiteralKoto.Value), or
    // the value of an admitted misfit (AdmittedMisfit), whose fit Binding rejected: the write places a value derived from that failure.
    private static bool Rejected(Koto node) => IsRecovery(node, out _) || node is CompileTimeSwitchKoto or CharLiteralKoto { Value: null } ||
        node.BindingFailure != BindingFailure.None || (node.Parent is BinaryKoto write && ReferenceEquals(write.Right, node) && AdmittedMisfit(write));

    // Whether a parent covers a child, which it does not evaluate as a value: the callee of a selected call or construction (a value call's
    // callee is evaluated unless its receiver operation uses what the callee reaches), any child of a macro (its call shape), the name
    // Type arguments apply to.
    private static bool Covers(Koto parent, Koto child) => parent switch
    {
        InvocationKoto call => ReferenceEquals(child, call.Method) && (CallPlan.Of(call) is not { Kind: CalleeKind.Value } plan ||
            (plan.ReceiverOperation.Source is { } source && !ReferenceEquals(source, child))),
        MacroKoto => true,
        GenericsKoto generics => ReferenceEquals(child, generics.Identifier),
        _ => false,
    };

    // A published declaration whose body Binding bound builds it, a virtual body no instance applies included (checked, never generated:
    // virtual-dispatch 3, 6.3); an accessor's body is a part of its Property.
    private static bool HasBody(Koto declaration) => PartsPublished(declaration) && declaration switch
    {
        FunctionKoto function => ((Koto?)function.Body ?? function.ExpressionBody) is { BindingState: not BindingState.Unvisited },
        PropertyAccessorKoto accessor => PartsPublished(accessor.Parent!) && accessor.Body is { BindingState: not BindingState.Unvisited },
        _ => ((PropertyKoto)declaration).InitializerKoto is { BindingState: not BindingState.Unvisited },
    };

    // A name evaluates what its symbol is; a Property with a custom accessor is its accessor calls.
    private static HirOp NameOp(Koto node) => node.BoundSymbol?.Kind switch
    {
        null => node is MemberAccessKoto { BindingState: BindingState.Resolved } ? HirOp.PrimitiveOp : HirOp.Name, // The metadata of a sequence.
        BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Capture or BindingSymbolKind.PatternCandidate or
            BindingSymbolKind.Storage or BindingSymbolKind.LengthParameter or BindingSymbolKind.Function => HirOp.Name,
        BindingSymbolKind.Property => CallPlan.Of(node) is not null ? HirOp.Call : IsStatic(node.BoundSymbol) ? HirOp.Name : HirOp.Field,
        BindingSymbolKind.EnumCase => HirOp.Aggregate,
        _ => HirOp.Syntax,
    };

    // A node's evaluated Op: a declaration's Body or Syntax; Syntax for a form nothing evaluates (a Type, a name it declares, an attribute,
    // an Origin clause or effect bound); Error for a node the lexer, the parser or Binding rejected but an admitted misfit, which places
    // (AdmittedMisfit, SPEC 13.7.1, 23.1.2: its value is the Error); else its form's Op, published only with the facts it guarantees (HasFacts).
    private HirOp OpOf(Koto node)
    {
        if (node is FunctionKoto { IsAnonymous: false } or PropertyKoto or PropertyAccessorKoto)
        {
            return HasBody(node) ? HirOp.Body : HirOp.Syntax;
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
        UnaryKoto when KotoHelper.SignedNumber(node, out _) is not null && CallPlan.Of(node) is null => HirOp.Literal, // Binding fits a signed number once.
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
        RangeKoto or FromEndIndexKoto => CallPlan.Of(node) is null ? HirOp.Range : HirOp.Call,
        InvocationKoto => CallPlan.Of(node) is null && node.BoundSymbol?.Kind == BindingSymbolKind.EnumCase ? HirOp.Aggregate : HirOp.Call,
        MemberAccessKoto { Right: NumberLiteralKoto } => HirOp.Field,
        MemberAccessKoto or IdentifierNameKoto or GenericsKoto or SyntaxFormKoto => NameOp(node),
        UnaryKoto { Akind: KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus or KotoKind.PostfixIncrement or KotoKind.PostfixDecrement } or
            BinaryKoto { Akind: > KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals } => HirOp.CompoundAssign,
        BinaryKoto { Akind: KotoKind.Equals } => HirOp.Assign,
        BinaryKoto { Akind: KotoKind.And or KotoKind.Or } => HirOp.Logical,
        UnaryKoto or BinaryKoto { Akind: not (KotoKind.OriginRelation or KotoKind.Is) } => CallPlan.Of(node) is null ? HirOp.PrimitiveOp : HirOp.Call,
        _ => HirOp.Unset,
    };

    private sealed partial class HirPublisher
    {
        // SPEC 3.4.3, 13.5.5: the form of a compound Place: a Field, an Index, a Deref (`*p`, `p@follow`) or a group (such as a list item the
        // parser recovered, a parameter default or a call's last argument).
        private static bool CompoundPlace(Koto node) => node is MemberAccessKoto or IndexKoto or DereferenceKoto or ParenthesizedKoto ||
            (node is ConversionKoto conversion && ConversionTargetSyntax(conversion) is TypeSemanticsKoto target && target.Names(Constants.FollowOperation));

        // The position of a child: a declaration evaluates its body, defaults and initializer, an expression its operands, and
        // nothing evaluates Types, names it declares or selects, attributes, Origin clauses or a fill length; an Error replaces its
        // children but those it keeps (Keeps).
        private HirPosition PositionOf(Koto node, Koto? parent, HirOp parentOp, HirPosition position, bool unresolvedOperation)
        {
            if ((parentOp == HirOp.Error && !this.Keeps(node, parent!, unresolvedOperation)) || (position & HirPosition.Replaced) != 0)
            {
                return HirPosition.Replaced;
            }

            position &= ~HirPosition.Covered;
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
                        if (ReferenceEquals(node, function.Parameters[i].DefaultValue) && PartsPublished(function))
                        {
                            this.table!.Hir.Defaults!.Add(new(function, i));
                            return HirPosition.Evaluated;
                        }
                    }

                    return HirPosition.None;
                case VariableKoto variable:
                    return ReferenceEquals(node, variable.InitializerKoto) && parentOp is HirOp.Body or HirOp.Let ? HirPosition.Evaluated : HirPosition.None;
                case MatchKoto:
                    return node.SelectionRole == SelectionRole.Pattern ? HirPosition.Pattern : position;
                case ForKoto loop:
                    return ReferenceEquals(node, loop.Iterable) || ReferenceEquals(node, loop.Body) || ReferenceEquals(node, loop.Iteration?.Receiver) ? position : HirPosition.None;
                case InvocationKoto or MacroKoto:
                    return Covers(parent, node) ? position | HirPosition.Covered : position;
                case GenericsKoto:
                    return Covers(parent, node) ? position | HirPosition.Covered : HirPosition.None;
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

        // SPEC 23.1.2: the children an Error keeps at their positions, evaluated in place; the others stay replaced. The parent is the child's.
        // An operation Binding left unresolved (DECISIONS R4-M88) keeps every operand its form evaluates (one Binding resolved, one it did
        // not, an Error), except a construct that has no Type (Construct), also in parentheses or labeled: no destination Type holds a
        // kept operand, so it stays replaced, its parts' problems lost (PLAN G121's kept-construct design). A compound Place Binding
        // rejected or did not resolve, not a broken contract, keeps each operand Binding resolved as a value or a Place, an Op with its facts
        // (not a Type or group name, Syntax; DECISIONS R5-M63). A rejected expression that runs its one body once (RunsItsBodyOnce: a do)
        // keeps each part Binding resolved with its Op's facts, unless a jump the builder builds goes to it (DECISIONS BU-106). A kept part
        // holds no jump published without failure whose target row is the Error: the builder resolves such a row against the targets it
        // opened, and an Error opens none (its InternalInvariant_Kd).
        private bool Keeps(Koto child, Koto parent, bool unresolvedOperation)
        {
            if (Rejected(parent) && RunsItsBodyOnce(binding.KindOp(parent)))
            {
                // The label test bounds the walk's cost only: a do is a jump's target row only as a labeled target (SPEC 14.3).
                return child.BindingState == BindingState.Resolved && binding.OpOf(child) is not (HirOp.Unset or HirOp.Syntax or HirOp.Error) &&
                    !(parent.Parent is LabeledKoto && (this.targets ??= new(binding)).Targeted(parent));
            }

            if (unresolvedOperation)
            {
                var op = binding.OpOf(child);
                var inner = KotoHelper.UnwrapParentheses(child);
                inner = inner is LabeledKoto label ? label.Target : inner;
                return op != HirOp.Syntax && !(Construct(ReferenceEquals(inner, child) ? op : binding.OpOf(inner)) &&
                    HirTables.Meaning(inner) is null && HirTables.Meaning(child) is null);
            }

            return child.BindingState == BindingState.Resolved && CompoundPlace(parent) &&
                (Rejected(parent) || parent.BindingState != BindingState.Resolved) &&
                binding.OpOf(child) is not (HirOp.Unset or HirOp.Syntax or HirOp.Error);
        }

        // The Op a node's parts are published under (DECISIONS R5-M63): a closure Binding bound in this pass, at an evaluated or a replaced
        // position, whose definition it completed keeps its parts as a Closure whatever its creation is (built, failed, or part of a
        // replaced parent); another node's parts are its Op's.
        private HirOp Parts(Koto node, HirOp op, HirPosition position)
        {
            var bound = (position & (HirPosition.Evaluated | HirPosition.Replaced)) != 0 &&
                (node.BindingState == BindingState.Resolved || node.BindingFailure != BindingFailure.None);
            return node is FunctionKoto { IsAnonymous: true } && bound && binding.HasFacts(node, HirOp.Closure) ? HirOp.Closure : op;
        }

        // The Op at a position: a declaration's at any, another node's only where it is evaluated (Syntax elsewhere).
        private HirOp OpOf(Koto node, HirPosition position)
            => position == HirPosition.Evaluated || node is FunctionKoto { IsAnonymous: false } or PropertyKoto or PropertyAccessorKoto
                ? binding.OpOf(node) : HirOp.Syntax;

        // SPEC 14.5.2 (DECISIONS BU-101, BU-106): whether a jump in a construct's subtree, one the builder builds through its target row (its
        // Op not Error), has the construct as that row (HirTables.PlanOf, written by the jump binding before publication). A failed jump is
        // built as a derived Opaque and reads no target, so it does not count.
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

            internal bool Targeted(Koto construct)
            {
                (this.target, this.found) = (construct, false);
                construct.VisitChildren(this);
                this.target = null;
                return this.found;
            }
        }
    }
}
