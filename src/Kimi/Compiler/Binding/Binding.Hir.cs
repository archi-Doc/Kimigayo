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

        // By SyntaxId: whether a child's consumer needs exclusive capability of it, recorded where the consumer uses it (Use, FixAdjust) and
        // read by ChildAdjust's claimed and ExclusiveUse.
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
            var unresolvedOperation = node.BindingState != BindingState.Resolved && op == HirOp.Error && !Rejected(node) && Operation(binding.KindOp(node));
            if (this.parentOp == HirOp.Error && position == HirPosition.Evaluated)
            {
                this.FixAdjust(node, new(HirAcquire.None, HirAdaptation.None)); // A kept operand of an Error (Keeps), used in place.
            }
            else if (op == HirOp.Error)
            {
                this.Derived(node); // Any other consumer uses it as its derived value (DECISIONS R4-M109).
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

                    // SPEC 14.2.2 (DECISIONS R4-M145): a Place in Discard Context is acquired by bare acquisition, its plan row's Adjust
                    // (Discarded). No consumer takes such a Place but the group around it (Grouped's IsGroup), so only a Place with no
                    // Adjust or a group's operand is asked for its context (a warm publish measured about 1.2% slower asking every Place).
                    if (places[node.SyntaxId].Kind != HirPlaceKind.Value && (HirTables.AdjustOf(node).Acquire == HirAcquire.Unset || IsGroup(node.Parent)) &&
                        !KotoHelper.IsValueContext(node))
                    {
                        var discarded = binding.Discarded(node);
                        HirTables.PlanFor(node, discarded != default).Adjust = discarded;
                    }
                }
            }

            Fold(node, op);
            if (parts is not (HirOp.Unset or HirOp.Error) &&
                (parts != HirOp.Syntax || (node is FunctionKoto function && PartsPublished(function) && (position & HirPosition.Replaced) == 0)))
            {
                // The selection by which AdjustChildren uses a user index's inputs, recorded for its ExpansionPlan (SPEC 4.7.1).
                if (node is IndexKoto)
                {
                    var uniq = binding.SelectsUniq(node, this.exclusive);
                    HirTables.PlanFor(node, uniq).Mode = uniq ? (byte)1 : (byte)0;
                }

                var uses = new PublisherUses(this);
                binding.AdjustChildren(node, parts, ref uses);
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
                    return IsStatic(symbol) ? HirRes.Static : HirRes.Field;
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
                HirRes.Field => HirPlaceKind.Field, // An accessor's storage (Binding.StorageProjection): the implicit self's Field, or static storage (MirBuilder.Places).
                _ => HirPlaceKind.Value,
            },
            HirOp.Field => HirPlaceKind.Field,
            HirOp.Deref => binding.PairAdmitted(node) == SemanticsMask.None ? HirPlaceKind.Deref : HirPlaceKind.PairLayer,
            HirOp.Index => ElementAccess.IsSlicing(node) ? HirPlaceKind.Value : ElementAccess.StaticSelector((IndexKoto)node) < 0 ? HirPlaceKind.Index : HirPlaceKind.ConstIndex,
            HirOp.Call => ElementAccess.IsUserIndex(node) ? HirPlaceKind.CallResult : HirPlaceKind.Value,
            HirOp.Group when node is ParenthesizedKoto && KotoHelper.UnwrapParentheses(node) is var inner => this.KindOf(inner, binding.OpOf(inner), this.ResOf(inner)),
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
