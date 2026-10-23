// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns publication of HIR facts from the completed Binding pass.
// Input: that pass's decisions and syntax slots. Output: Op, Res, Completion, Adjust and Place columns (the jump binding writes the
// jump target rows).
// Publication is versioned and reused; it decides nothing beyond Binding's existing facts.
// Must not resolve again; R7 replaces the publisher walk with inline writes.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The HIR publisher (plan R2b): the Op, Res, Completion, Adjust and PlaceShape columns of every module, read from the facts
// the latest pass keeps; it decides nothing Binding has not decided. Tests call it in R2b and the MIR builder from R3; R7 writes the columns inline.
public sealed partial class Binding
{
    [Transitional("R7", Reason = "R7 replaces the publisher walk with inline Binding writes.")]
    private HirPublisher? hirPublisher;
    private ulong hirVersion;

    // Where a node stands: evaluated, inside a Pattern (its match plan describes it), covered by its parent (the callee of a selected
    // call or construction, the call shape of a formatting macro, the name Type arguments apply to), or inside an Error, whose value the
    // builder replaces (DECISIONS R5-M63 keeps a closure definition's parts and a failed compound Place's resolved operands, R4-M88 an
    // unresolved operation's operands).
    [Flags]
    [Transitional("R7", Reason = "R7 replaces the publisher walk with inline Binding writes.")]
    private enum HirPosition : byte
    {
        None = 0,
        Evaluated = 1,
        Pattern = 2,
        Covered = 4,
        Replaced = 8,
    }

    // SPEC 22.10.5: whether a formatting write writes a literal text (a text segment, or a string literal written as a value), whose bytes
    // are known and which needs no Type argument; the one classification the published formatting facts (HasFacts) and the MIR builder's
    // capacity estimate read (DECISIONS R5-M79).
    internal static bool WritesText(CallPlan write) => write.ArgumentOperations[1].Source is StringLiteralKoto;

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
    [Transitional("R7", Reason = "R7 replaces the publisher walk with inline Binding writes.")]
    private sealed partial class HirPublisher(Binding binding) : KotoVisitor
    {
        // The parameter defaults the walk is inside: the function and the parameter's slot.
        private (Koto Function, int Slot)[] defaults = new (Koto, int)[4];
        private int defaultCount;
        private Koto? parent;
        private HirOp parentOp;
        private HirPosition position;
        private bool exclusive;
        private bool unresolvedOperation; // The parent is an operation Binding left unresolved (Keeps).

        // The exclusive capability each consumed child's consumer needs, recorded where the consumer takes it (ExclusiveUse), by SyntaxId.
        private bool[] claims = [];

        // Binding's structural completion with its Never evidence, one per body.
        private StructuralCompletion? completion;
        private SyntaxTable? table;

        public override void Visit(Koto node)
        {
            var (parent, parentOp, outer, defaults, exclusive, operation) =
                (this.parent, this.parentOp, this.position, this.defaultCount, this.exclusive, this.unresolvedOperation);
            var position = this.PositionOf(node);
            var op = this.OpOf(node, position);
            var parts = this.Parts(node, op, position);
            var unresolvedOperation = node.BindingState != BindingState.Resolved && op == HirOp.Error && !Rejected(node) && Operation(this.KindOp(node));
            if (this.parentOp == HirOp.Error && position == HirPosition.Evaluated)
            {
                this.Write(node, new(HirAcquire.None, HirAdaptation.None)); // A kept operand of an Error (Keeps), used in place.
            }
            else if (op == HirOp.Error)
            {
                this.Derived(node); // Any other consumer takes it as its derived value (DECISIONS R4-M109).
            }

            if (op == HirOp.Body)
            {
                this.completion?.Clear();
            }

            this.exclusive = this.ExclusiveUse(node);
            if (node.SyntaxOwner is { Hir.Ops: { } ops, Hir.Res: { } res, Hir.Completions: { } completions, Hir.Places: { } places } && node.SyntaxId < ops.Length)
            {
                // An Error Binding resolved without rejecting it lacks the facts its Op guarantees (HasFacts): a broken HIR contract.
                if (op == HirOp.Error && node.BindingState == BindingState.Resolved && !Rejected(node))
                {
                    node.SyntaxOwner.Hir.Broken!.Add(node);
                }

                ops[node.SyntaxId] = op;
                res[node.SyntaxId] = this.ResOf(node);
                completions[node.SyntaxId] = op is HirOp.Unset or HirOp.Syntax or HirOp.Body ? HirCompletion.Unset
                    : (this.completion ??= new(binding.ResultNeverEvidence)).CanComplete(node) ? HirCompletion.Completes : HirCompletion.Diverges;
                if (completions[node.SyntaxId] != HirCompletion.Unset)
                {
                    places[node.SyntaxId] = new(this.KindOf(node, op, res[node.SyntaxId]), this.Layers(node));
                }
            }

            Fold(node, op);
            if (parts is not (HirOp.Unset or HirOp.Error) &&
                (parts != HirOp.Syntax || (node is FunctionKoto function && PartsPublished(function) && (position & HirPosition.Replaced) == 0)))
            {
                this.Consume(node, parts);
            }

            (this.parent, this.parentOp, this.position, this.unresolvedOperation) = (node, parts, parts == op ? position : HirPosition.None, unresolvedOperation);
            node.VisitChildren(this);
            if (node is ForKoto { Iteration: { } iteration } && op == HirOp.For)
            {
                this.Visit(iteration.Receiver); // The step's receiver, the iterator local the loop declares (SPEC 14.6.2).
            }

            (this.parent, this.parentOp, this.position, this.defaultCount, this.exclusive, this.unresolvedOperation) =
                (parent, parentOp, outer, defaults, exclusive, operation);
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

            if (this.claims.Length < hir.States.Length)
            {
                this.claims = new bool[hir.States.Length];
            }

            Array.Clear(this.claims, 0, hir.States.Length);
            (hir.Defaults ??= []).Clear();
            (hir.Broken ??= []).Clear();
            this.table = table;
            this.completion?.Clear();
            (this.parent, this.position, this.exclusive, this.unresolvedOperation) = (null, HirPosition.None, false, false);
            this.Visit(root);
        }

        // The operand a parent projects, updates, groups or borrows.
        private static Koto? Operand(Koto? parent) => (parent as BinaryKoto)?.Left ?? (parent as UnaryKoto)?.Operand;

        // A definition's parts (body, defaults, initializer, accessor bodies) are published only when Binding resolved the definition: those
        // of an unresolved one, such as a definition beyond the case bound (Binding.CaseLimits), are not verified.
        private static bool PartsPublished(Koto definition) => definition.BindingState == BindingState.Resolved;

        // Whether the lexer, the parser or Binding rejected a node: a recovery, a #switch that selected no arm (SPEC 19.3) or a failure.
        private static bool Rejected(Koto node) => IsRecovery(node, out _) || node is CompileTimeSwitchKoto || node.BindingFailure != BindingFailure.None;

        // SPEC 3.4.3, 13.5.5: the form of a compound Place: a Field, an Index, a Deref (`*p`, `p@follow`) or a group (such as a list item the
        // parser recovered, a parameter default or a call's last argument).
        private static bool CompoundPlace(Koto node) => node is MemberAccessKoto or IndexKoto or DereferenceKoto or ParenthesizedKoto ||
            (node is ConversionKoto conversion && ConversionTargetSyntax(conversion) is TypeSemanticsKoto target && target.Names(Constants.FollowOperation));

        // The constant Binding decided that a node's own plan row keeps (Folded): a number literal's value as Binding fits it (SPEC 3.1,
        // 13.5.4), its floating-point Type's bits, else the integer it names; a fixed-array element's literal position, beside its
        // PlaceShape (SPEC 15.1.3); a Tuple element's position (SPEC 3.2), the integer its Field names.
        private static void Fold(Koto node, HirOp op)
        {
            if (op == HirOp.Literal && KotoHelper.SignedNumber(node, out var negative) is { } number)
            {
                var value = node.BoundType is { IsFloatingPoint: true } type ? ScalarTypes.TryFloatLiteral(number, type, negative, out var bits) ? (Int128)bits : null
                    : number.TryGetIntegerMagnitude(out var magnitude) ? unchecked((Int128)(negative ? 0 - magnitude : magnitude)) : (Int128?)null;
                HirTables.PlanFor(node, value is not null).Folded = value;
            }
            else if (node is IndexKoto index && HirTables.PlaceOf(node).Kind == HirPlaceKind.ConstIndex)
            {
                HirTables.PlanFor(node, written: true).Folded = ElementAccess.StaticSelector(index);
            }
            else if (op == HirOp.Field && node is MemberAccessKoto { Right: NumberLiteralKoto element } && element.TryGetIntegerMagnitude(out var position))
            {
                HirTables.PlanFor(node, written: true).Folded = unchecked((Int128)position);
            }
        }

        // The position of a child: a declaration evaluates its body, defaults and initializer, an expression its operands, and
        // nothing evaluates Types, names it declares or selects, attributes, Origin clauses or a fill length; an Error replaces its
        // children but those it keeps (Keeps).
        private HirPosition PositionOf(Koto node)
        {
            if ((this.parentOp == HirOp.Error && !this.Keeps(node)) || (this.position & HirPosition.Replaced) != 0)
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
                        if (ReferenceEquals(node, function.Parameters[i].DefaultValue) && PartsPublished(function))
                        {
                            if (this.defaultCount == this.defaults.Length)
                            {
                                Array.Resize(ref this.defaults, this.defaultCount * 2);
                            }

                            this.defaults[this.defaultCount++] = (function, i);
                            this.table!.Hir.Defaults!.Add(new(function, i));
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

        // SPEC 23.1.2: the children an Error keeps at their positions, evaluated in place; the others stay replaced. The parent is the child's.
        // An operation Binding left unresolved (DECISIONS R4-M88) keeps every operand its form evaluates (one Binding resolved, one it did
        // not, an Error), except a construct that has no Type (Construct), also in parentheses or labeled: no destination Type holds a
        // kept operand, so it stays replaced, its parts' problems lost (PLAN G121's kept-construct design). A compound Place Binding
        // rejected or did not resolve, not a broken contract, keeps each operand Binding resolved as a value or a Place, an Op with its facts
        // (not a Type or group name, Syntax; DECISIONS R5-M63).
        private bool Keeps(Koto child)
        {
            if (this.unresolvedOperation)
            {
                var op = this.OpOf(child, HirPosition.Evaluated);
                var inner = KotoHelper.UnwrapParentheses(child);
                inner = inner is LabeledKoto label ? label.Target : inner;
                return op != HirOp.Syntax && !(Construct(ReferenceEquals(inner, child) ? op : this.OpOf(inner, HirPosition.Evaluated)) &&
                    HirTables.Meaning(inner) is null && HirTables.Meaning(child) is null);
            }

            return child.BindingState == BindingState.Resolved && CompoundPlace(this.parent!) &&
                (Rejected(this.parent!) || this.parent!.BindingState != BindingState.Resolved) &&
                this.OpOf(child, HirPosition.Evaluated) is not (HirOp.Unset or HirOp.Syntax or HirOp.Error);
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

            if (Rejected(node))
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

            return binding.HasFacts(node, op) ? op : HirOp.Error;
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
            IndexKoto index => (binding.IndexerCall(index, false) ?? binding.IndexerCall(index, true) ?? binding.ViewRangeCall(index)) is null ? HirOp.Index : HirOp.Call,
            RangeKoto or FromEndIndexKoto => CallPlan.Of(node) is null ? HirOp.Range : HirOp.Call,
            InvocationKoto => CallPlan.Of(node) is null && node.BoundSymbol?.Kind == BindingSymbolKind.EnumCase ? HirOp.Aggregate : HirOp.Call,
            MemberAccessKoto { Right: NumberLiteralKoto } => HirOp.Field,
            MemberAccessKoto or IdentifierNameKoto or GenericsKoto or SyntaxFormKoto => this.NameOp(node),
            UnaryKoto { Akind: KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus or KotoKind.PostfixIncrement or KotoKind.PostfixDecrement } or
                BinaryKoto { Akind: > KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals } => HirOp.CompoundAssign,
            BinaryKoto { Akind: KotoKind.Equals } => HirOp.Assign,
            BinaryKoto { Akind: KotoKind.And or KotoKind.Or } => HirOp.Logical,
            UnaryKoto or BinaryKoto { Akind: not (KotoKind.OriginRelation or KotoKind.Is) } => CallPlan.Of(node) is null ? HirOp.PrimitiveOp : HirOp.Call,
            _ => HirOp.Unset,
        };

        // A published declaration whose body Binding bound builds it, a virtual body no instance applies included (checked, never generated:
        // virtual-dispatch 3, 6.3); an accessor's body is a part of its Property.
        private bool HasBody(Koto declaration) => PartsPublished(declaration) && declaration switch
        {
            FunctionKoto function => ((Koto?)function.Body ?? function.ExpressionBody) is { BindingState: not BindingState.Unvisited },
            PropertyAccessorKoto accessor => PartsPublished(accessor.Parent!) && accessor.Body is { BindingState: not BindingState.Unvisited },
            _ => ((PropertyKoto)declaration).InitializerKoto is { BindingState: not BindingState.Unvisited },
        };

        // A name evaluates what its symbol is; a Property with a custom accessor is its accessor calls.
        private HirOp NameOp(Koto node) => node.BoundSymbol?.Kind switch
        {
            null => node is MemberAccessKoto { BindingState: BindingState.Resolved } ? HirOp.PrimitiveOp : HirOp.Name, // The metadata of a sequence.
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

        // SPEC 3.4.3, 3.5, 4.7.1, 13.5.5, 15.1.3: the Place a node designates, as its Op, Res and Binding's element, follow and call facts
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
            HirOp.Call => ElementAccess.IsUserIndex(node) ? HirPlaceKind.CallResult : HirPlaceKind.Value,
            HirOp.Group when node is ParenthesizedKoto && KotoHelper.UnwrapParentheses(node) is var inner => this.KindOf(inner, this.OpOf(inner, HirPosition.Evaluated), this.ResOf(inner)),
            _ => HirPlaceKind.Value,
        };

        // The layers a node's consumer follows before it projects or borrows the node (SPEC 3.4.3, 13.5.5): every reference, object
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
    }
}
