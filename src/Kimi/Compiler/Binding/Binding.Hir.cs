// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns publication of HIR facts from the Binding pass: a Final pass's Check publishes between its failure producers and its counting.
// Input: that pass's decisions and syntax slots. Output: Op, Completion, Adjust and Place columns (the jump binding writes the jump
// target rows) and the plan rows' published facts, an explicit discard's in-place destruction among them (DestroysInPlace).
// Publication is versioned and reused; it decides nothing beyond Binding's existing facts.
// The walk is a lasting component: it holds what a node's parent gives it (the parent, its Op, the position, the capability its use needs
// with a by-case need's decider, whether it is an unresolved operation) and passes those to the rows' rules, which read no walk state.
// Must not resolve again.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The HIR publisher (plan R2b): the Op, Completion, Adjust and PlaceShape columns of every module, read from the facts the latest pass
// keeps; it decides nothing Binding has not decided.
public sealed partial class Binding
{
    private HirPublisher? hirPublisher;
    private ulong hirVersion;

    // The library module's publication version (HirTables.LibraryVersion; PLAN G949 Stage 2 U3): Frozen once the compilation's library is
    // frozen, the same in every compilation; 0 for a library not frozen (the freeze compilation's before its freeze, a refused one).
    internal ulong LibraryVersion => this.Library.Frozen ? Frozen : 0;

    // The nodes the publisher's jump walk visited (Keeps, DECISIONS BU-106): a scaling check's work measure.
    internal long KeptTargetWork => this.hirPublisher?.TargetWork ?? 0;

    // SPEC 22.10.5: whether a formatting write writes a literal text (a text segment, or a string literal written as a value), whose bytes
    // are known and which needs no Type argument; the one classification the published formatting facts (HasFacts) and the MIR builder's
    // interpolation terms read (DECISIONS R5-M79).
    internal static bool WritesText(CallPlan write) => write.ArgumentOperations[1].Source is StringLiteralKoto;

    // Returns the pass the published columns of the program pass's modules describe (0: none), by which the MIR builder caches its
    // program. A Final pass published them itself (Check, DECISIONS BU-81); another finished pass publishes here, once. The library's
    // module is published by its own pass, which is Final in every Bind (Run).
    internal ulong PublishHir()
    {
        if (this.running || this.Result == default || this.hirVersion == this.storageVersion)
        {
            return this.hirVersion;
        }

        this.PublishPass();
        return this.hirVersion;
    }

    // The columns of the pass's modules, in their order, from the pass's facts. RB1 (PLAN G949 S1-r1b; review-s1r1 Re-check 3; C-379
    // F1): a pass that holds the library module publishes it at its end, since the program pass empties the per-pass tables the
    // publisher reads for the library's nodes; its version is the library's (LibraryVersion: Frozen once frozen), never
    // hirVersion, which PublishHir compares for the program pass's modules. So the library pass, Final in every Bind, leaves a
    // Provisional program pass's modules to PublishHir.
    private void PublishPass()
    {
        if (this.pass.Library is null)
        {
            this.hirVersion = this.storageVersion;
        }

        var publisher = this.hirPublisher ??= new(this);
        for (var i = 0; i < this.pass.Count; i++)
        {
            publisher.Publish(this.pass[i].Syntax, this.pass[i].RootKoto);
        }
    }

    // One walk of the tree through its child slots; each node's position follows from the slot its parent holds it in.
    private sealed partial class HirPublisher(Binding binding) : KotoVisitor
    {
        private Koto? parent;
        private HirOp parentOp;
        private HirPosition position;
        private UseNeed need;
        private bool unresolvedOperation; // The parent is an operation Binding left unresolved (Keeps).

        // Binding's structural completion with its Never evidence, one per body.
        private StructuralCompletion? completion;
        private SyntaxTable? table;
        private RowUses uses; // The record of the uses of the module being published.

        // The jumps a rejected labeled do's subtree holds (Keeps), one walk reused across nodes and passes.
        private TargetWalk? targets;

        // The nodes the jump walk visited (TargetWalk), which a scaling check reads; kept across passes.
        internal long TargetWork => this.targets?.Visited ?? 0;

        public override void Visit(Koto node)
        {
            var (parent, parentOp, outer, need, operation) = (this.parent, this.parentOp, this.position, this.need, this.unresolvedOperation);
            var position = this.PositionOf(node, parent, parentOp, outer, operation);
            var op = this.OpOf(node, position);
            var parts = this.Parts(node, op, position);
            var unresolvedOperation = node.BindingState != BindingState.Resolved && op == HirOp.Error && !Rejected(node) && Operation(binding.KindOp(node));
            var claimed = this.uses.Claimed(node); // The takes' claim, which the rows of a kept or an Error node replace.
            if (parentOp == HirOp.Error && position == HirPosition.Evaluated)
            {
                this.uses.FixAdjust(node, new(HirAcquire.None, HirAdaptation.None), default); // A kept operand of an Error (Keeps), used in place.
            }
            else if (op == HirOp.Error)
            {
                this.Derived(node); // Any other consumer uses it as its derived value (DECISIONS R4-M109).
            }

            if (op == HirOp.Body)
            {
                this.completion?.Clear();
            }

            if (node.SyntaxOwner is { Hir.Ops: { } ops, Hir.Completions: { } completions, Hir.Places: { } places } && node.SyntaxId < ops.Length)
            {
                // An Error Binding resolved without rejecting it lacks the facts its Op guarantees (HasFacts): a broken HIR contract.
                if (op == HirOp.Error && node.BindingState == BindingState.Resolved && !Rejected(node))
                {
                    node.SyntaxOwner.Hir.Broken!.Add(node);
                }

                ops[node.SyntaxId] = op;
                completions[node.SyntaxId] = op is HirOp.Unset or HirOp.Syntax or HirOp.Body ? HirCompletion.Unset
                    : (this.completion ??= new(binding, binding.ResultNeverEvidence)).CanComplete(node) ? HirCompletion.Completes : HirCompletion.Diverges;
                BoundType? discardedType = null;
                if (completions[node.SyntaxId] != HirCompletion.Unset)
                {
                    places[node.SyntaxId] = new(this.KindOf(node, op), this.Layers(node, parent, parentOp));
                    if (places[node.SyntaxId].Kind != HirPlaceKind.Value)
                    {
                        discardedType = this.DiscardRow(node);
                    }
                }

                // Every node a publish reaches gets its discarded Type, null where it is none, so no row keeps an earlier pass's (BU-98).
                HirTables.PlanFor(node, discardedType is not null).DiscardedType = discardedType;
                if (node is DiscardKoto discard)
                {
                    var inPlace = binding.DestroysInPlace(discard); // SPEC 14.2.4: a raw Place's Take destroyed in place (HirTables.DestroysInPlace).
                    HirTables.PlanFor(node, inPlace).Flag = inPlace;
                }
            }

            // Every take of the node has written its row: its consumer's, then its container's discard row (DECISIONS BU-8).
            this.need = this.NeedOf(node, parent, parentOp, claimed, need);
            this.Fold(node, op);
            if (parts is not (HirOp.Unset or HirOp.Error) &&
                (parts != HirOp.Syntax || (node is FunctionKoto function && PartsPublished(function) && (position & HirPosition.Replaced) == 0)))
            {
                // The selection by which AdjustChildren uses a user index's inputs, recorded for its ExpansionPlan (SPEC 4.7.1, 8.14), and a
                // selection by case with its decider, read through HirTables.IndexCaseDecider (R7 plan Z23).
                if (node is IndexKoto)
                {
                    var mode = binding.SelectionMode(node, this.need.Use);
                    ref var row = ref HirTables.PlanFor(node, mode != 0);
                    (row.Mode, row.Plan) = (mode, mode == 2 ? this.need.Decider : null);
                }

                binding.AdjustChildren(node, parts, this.need.Use, ref this.uses);
            }

            (this.parent, this.parentOp, this.position, this.unresolvedOperation) = (node, parts, parts == op ? position : HirPosition.None, unresolvedOperation);
            node.VisitChildren(this);
            if (node is ForKoto { Iteration: { } iteration } && op == HirOp.For)
            {
                this.Visit(iteration.Receiver); // The step's receiver, the iterator local the loop declares (SPEC 14.6.2).
            }

            (this.parent, this.parentOp, this.position, this.need, this.unresolvedOperation) = (parent, parentOp, outer, need, operation);
        }

        internal void Publish(SyntaxTable table, Koto root)
        {
            table.EnsureWritable(nameof(HirPublisher)); // The one entry of the publisher's direct column writes.
            ref var hir = ref table.Hir;
            if (hir.Ops?.Length != hir.States.Length)
            {
                (hir.Ops, hir.Completions, hir.Adjusts, hir.Places) = (new HirOp[hir.States.Length], new HirCompletion[hir.States.Length], new HirAdjust[hir.States.Length], new HirPlace[hir.States.Length]);
            }
            else
            {
                Array.Clear(hir.Ops);
                Array.Clear(hir.Completions!);
                Array.Clear(hir.Adjusts!);
                Array.Clear(hir.Places!);
            }

            // A plan row's Adjust is a take of this publish, as the column's is, so a claim (Claimed) reads no earlier pass's row.
            for (var i = 1; i <= hir.PlanCount; i++)
            {
                hir.Plans[i].Adjust = default;
            }

            (hir.Defaults ??= []).Clear();
            (hir.Broken ??= []).Clear();
            (this.table, this.uses) = (table, new(binding, table));
            this.completion?.Clear();
            (this.parent, this.position, this.need, this.unresolvedOperation) = (null, HirPosition.None, default, false);
            this.Visit(root);
        }

        // The constant Binding decided that a node's own plan row keeps (Folded): a number literal's value as Binding fits it (SPEC 3.1,
        // 13.5.4), its floating-point Type's bits, else its integer value as its Type's payload (ScalarTypes.Normalize, DECISIONS BU-133;
        // a literal its Type does not hold, the operand of a folded wrap conversion or the number under a direct literal's sign, keeps
        // its value modulo 2^N, and a generic literal its value); a fixed-array element's literal position, beside its PlaceShape
        // (SPEC 15.1.3); a Tuple element's position (SPEC 3.2), the integer its Field names.
        private void Fold(Koto node, HirOp op)
        {
            if (op == HirOp.Literal && KotoHelper.SignedNumber(node, out var negative) is { } number)
            {
                var value = node.BoundType is { IsFloatingPoint: true } type ? ScalarTypes.TryFloatLiteral(number, type, negative, out var bits) ? (Int128)bits : null
                    : number.TryGetIntegerMagnitude(out var magnitude) ? (Int128?)ScalarTypes.Normalize(unchecked((Int128)(negative ? 0 - magnitude : magnitude)), node.BoundType?.Width ?? 0)
                    : null;
                HirTables.PlanFor(node, value is not null).Folded = value;
            }
            else if (node is IndexKoto index && HirTables.PlaceOf(node).Kind == HirPlaceKind.ConstIndex)
            {
                HirTables.PlanFor(node, written: true).Folded = ElementAccess.StaticSelector(binding, index);
            }
            else if (op == HirOp.Field && node is MemberAccessKoto { Right: NumberLiteralKoto element } && element.TryGetIntegerMagnitude(out var position))
            {
                HirTables.PlanFor(node, written: true).Folded = unchecked((Int128)position);
            }
        }

        // SPEC 14.2.2, 14.2.4 (DECISIONS R4-M145, BU-98): a discarded Place's plan row. A Place in Discard Context is acquired by bare
        // acquisition, its row's Adjust (Discarded), a take that claims as a consumer's does (Claimed; PLAN G426), so a user index whose
        // discarded element is Reborrowed selects indexUniq; the Place a container or an explicit discard takes (Discards, outside
        // any group) holds the value its acquisition supplies, its row's DiscardedType (SuppliedType), which no consumer states. No
        // consumer takes such a Place but the group around it (Grouped's IsGroup) or the explicit discard, so only a Place with no Adjust,
        // a group's operand or a discard's operand is asked for its context, the builder's reach, not every Place. Returns the discarded
        // Type, which Visit writes, null for a Place no container or explicit discard takes.
        private BoundType? DiscardRow(Koto node)
        {
            var unset = HirTables.AdjustOf(node).Acquire == HirAcquire.Unset;
            var grouped = IsGroup(node.Parent);
            if ((unset || grouped) && !IsValueContext(node))
            {
                var discarded = binding.Discarded(node);
                HirTables.PlanFor(node, discarded != default).Adjust = discarded;
            }

            return !grouped && (unset || node.Parent is DiscardKoto) && Discards(node) ? binding.SuppliedType(node) : null;
        }

        // SPEC 3.4.3, 3.5, 4.7.1, 13.5.5, 15.1.3: the Place a node designates, as its Op, Res and Binding's element, follow and call facts
        // give it; a parenthesized Place is its operand's.
        private HirPlaceKind KindOf(Koto node, HirOp op) => op switch
        {
            HirOp.Name => HirTables.ResOf(node) switch
            {
                HirRes.Local or HirRes.Param or HirRes.PreparedParam or HirRes.Capture => HirPlaceKind.Local,
                HirRes.Static => HirPlaceKind.Static,
                HirRes.Field => HirPlaceKind.Field, // An accessor's storage (Binding.StorageProjection): the implicit self's Field, or static storage (MirBuilder.Places).
                _ => HirPlaceKind.Value,
            },
            HirOp.Field => HirPlaceKind.Field,
            HirOp.Deref => binding.PairAdmitted(node) == SemanticsMask.None ? HirPlaceKind.Deref : HirPlaceKind.PairLayer,
            HirOp.Index => ElementAccess.IsSlicing(binding, node) ? HirPlaceKind.Value : ElementAccess.StaticSelector(binding, (IndexKoto)node) < 0 ? HirPlaceKind.Index : HirPlaceKind.ConstIndex,
            HirOp.Call => ElementAccess.IsUserIndex(binding, node) ? HirPlaceKind.CallResult : HirPlaceKind.Value,
            HirOp.Group when node is ParenthesizedKoto && KotoHelper.UnwrapParentheses(node) is var inner => this.KindOf(inner, binding.OpOf(inner)),
            _ => HirPlaceKind.Value,
        };

        // The layers a node's consumer follows before it projects or borrows the node (SPEC 3.4.3, 13.5.5): every reference, object
        // handle and pair layer of the receiver of a stored Field, a Tuple element, a sequence's metadata or a built-in element, and the pair
        // layers Binding follows at a receiver or Subject down to their terminal target.
        private ushort Layers(Koto node, Koto? parent, HirOp parentOp)
        {
            var projected = (parentOp is HirOp.Field or HirOp.Index || (parentOp == HirOp.PrimitiveOp && parent is MemberAccessKoto)) && ReferenceEquals(Operand(parent), node);
            var terminal = projected || !TryPairLayer(node.BoundType, out _, out _) || binding.ImplicitPairAdmitted(node) == SemanticsMask.None ? null : binding.PairTerminalOf(node);
            var type = node.BoundType;
            ushort layers = 0;
            while ((projected || terminal is not null) && !ReferenceEquals(type, terminal) && TyCtxt.Layer(type, out type, out _) != HirPlaceKind.Value)
            {
                layers++;
            }

            return layers;
        }
    }
}
