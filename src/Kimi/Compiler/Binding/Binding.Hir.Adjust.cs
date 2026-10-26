// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The Adjust column's rule (SPEC 3.5, 10.2, 13.4, 13.5, 15.1.5), a lasting component that the HIR publisher reads.
// - Responsibility: how a consumer acquires or uses each of its children (AdjustChildren; a match's Subject and its Pattern bindings in
//   Binding.Hir.Patterns), and how a child's own facts complete its use (ChildAdjust, BareOf, IsPlace).
// - Inputs: a consumer and its evaluated Op (OpOf), Binding's facts for the consumer (its call plans, receiver operations, conversion,
//   match plan, enum construction) and for the child (its recorded adaptation, erasure, bare acquisition); and two facts of the record's
//   walk: the capability the consumer's own use needs (IHirUses.UseExclusive), which only a user index's selection reads (SPEC 4.7.1;
//   DECISIONS BU-8), and a child's claim (ChildAdjust's `claimed`): whether this use's operation's target needs exclusive capability of
//   the child (IHirUses.Use(child, in operation), recorded before the rule reads it), or an earlier use of it by the same consumer did,
//   by its operation's target or by the Adjust fixed for it (an exclusive borrow, or a Reborrow or pair-case acquisition of a child whose
//   Type needs it). The record accumulates it per publish (the publisher's claims); it decides whether Binding's recorded in-place
//   shared read of a selection yields (SPEC 4.7.1, 7.3). This use's own claim decides that a part below a stored reference passed as a
//   `uniq/Self` receiver is a Reborrow (HirFormsTest.APartBelowAStoredReferenceIsAPlace).
// - Outputs: each child's use, to an IHirUses; the HIR publisher writes them as the Adjust column and the plan rows.
// - Invariants: a consumer's uses come in evaluation order, its calls' inputs last and in CallPlan.Of's order; the rule reads no other
//   walk state.
// - Prohibitions: the rule writes nothing; no record keeps a second copy of a use's Adjust.
// The publisher's record of the uses (PublisherUses, over its claims, ExclusiveUse, Derived and column writes) is the Transitional walk's,
// until R7 (DECISIONS R4-M142).

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // How a consumer uses an operand (SPEC 3.5, 13.5.5, 13.4): an acquisition by value, a use in place (a borrow, a follow, a runtime test,
    // a projection's receiver), or an inspection (a built-in operator's operand, through a shared Loan where it is not Copy).
    private enum UseKind : byte
    {
        Acquisition,
        InPlace,
        Inspection,
    }

    // The record of the uses the Adjust column's rule gives (AdjustChildren): the HIR publisher's (PublisherUses), which writes each.
    private interface IHirUses
    {
        // The capability the consumer's own use needs, which selects a user index's indexUniq (SPEC 4.7.1; DECISIONS BU-8).
        bool UseExclusive { get; }

        // The call of the consumer whose inputs follow, by its CallPlan.Of ordinal; -1 after the last.
        void Call(int ordinal);

        // A child the consumer's operation adapts, acquired, used in place or inspected, completed by its own facts (ChildAdjust).
        void Use(Koto? child, HirAdaptation adaptation = HirAdaptation.None, UseKind kind = UseKind.Acquisition);

        // A call's receiver or argument, a Case payload or a writer, used by its operation (SPEC 7.3, 10.2).
        void Use(Koto? child, in BoundArgumentOperation operation);

        // A child whose Adjust the consumer fixes: a group's operand, an assignment target, a transfer, a Subject, a Pattern binding.
        void FixAdjust(Koto? child, HirAdjust adjust, HirPlace place = default);
    }

    // SPEC 13.6.1: an object upcast to a view (objref, objuniq) of an operand of another Semantics borrows the operand in the view's mode.
    private static HirAdaptation ViewBorrow(Koto node) => node is ConversionKoto { ConversionBinding: ConversionBinding.ObjectUpcast, BoundType: { IsObjectBorrow: true } view } upcast &&
        upcast.Left.BoundType?.Semantics != view.Semantics ? view.Semantics == SemanticsKind.ObjUniq ? HirAdaptation.ExclusiveBorrow : HirAdaptation.SharedBorrow : HirAdaptation.None;

    // A body or operand that supplies its construct's value (SPEC 14.9); none in Discard Context.
    private static Koto? ResultOf(Koto? source) => source is not null && KotoHelper.IsValueContext(source) ? source : null;

    // SPEC 13.7.2, 11.3 (DECISIONS R4-M109): the Adjust of a call input that a later call of its consumer replaces by another (a compound
    // update's getter before its setter), which the builder still builds as the input's plan-row Adjust; none at the first call.
    private static HirAdjust EarlierAdjust(int call, HirAdjust earlier, HirAdjust later) => call > 0 && earlier != later ? earlier : default;

    // Whether a user index's use selects indexUniq (SPEC 4.7.1): the use needs exclusive capability and indexUniq is bound.
    private bool SelectsUniq(Koto index, bool exclusive) => exclusive && this.IndexerCall(index, exclusive: true) is not null;

    // The children a node's Op acquires or uses (SPEC 3.5, 10.2, 15.1.5): its own operands first, then its calls' receivers and arguments by
    // their operations, in evaluation order; a deeper consumer, such as a setter acquiring an assignment's value, is used last.
    private void AdjustChildren<T>(Koto node, HirOp op, ref T uses)
        where T : IHirUses
    {
        switch (node)
        {
            case FunctionKoto function:
                for (var i = 0; i < function.Parameters.Count; i++)
                {
                    uses.Use(function.Parameters[i].DefaultValue);
                }

                uses.Use(ResultOf(op is HirOp.Body or HirOp.Closure ? function.ExpressionBody : null));
                break;
            case PropertyAccessorKoto or CodeBlockKoto or DoKoto:
                uses.Use(ResultOf(node is PropertyAccessorKoto accessor ? accessor.Body
                    : node is DoKoto scoped ? scoped.Body : ((CodeBlockKoto)node).TrailingExpression));
                break;
            case VariableKoto or PropertyKoto:
                uses.Use(node is VariableKoto variable ? variable.InitializerKoto : ((PropertyKoto)node).InitializerKoto);
                break;
            case ParenthesizedKoto or LabeledKoto when op == HirOp.Group: // Its group acquires it, an erasure recorded at it included.
                var grouped = node is LabeledKoto label ? label.Target : ((UnaryKoto)node).Operand;
                uses.FixAdjust(
                    grouped,
                    HirTables.ErasedType(grouped) is null ? new(HirAcquire.None, HirAdaptation.None) : new(this.BareOf(grouped), HirAdaptation.Erasure));
                break;
            case ConversionKoto { ConversionBinding: ConversionBinding.Transfer } transfer:
                uses.FixAdjust(transfer.Left, new(HirAcquire.Move, HirAdaptation.None));
                break;
            case ConversionKoto or DereferenceKoto or IsKoto: // A borrow, a followed referent and a runtime test use their operand in place.
                uses.Use(
                    node is DereferenceKoto dereference ? dereference.Operand : ((BinaryKoto)node).Left,
                    ViewBorrow(node),
                    op is HirOp.Borrow or HirOp.Deref or HirOp.RuntimeTypeTest ? UseKind.InPlace : UseKind.Acquisition);
                break;
            case MemberAccessKoto member when op is HirOp.Field or HirOp.PrimitiveOp: // A stored Field or Tuple element, or a sequence's metadata.
                var adaptation = this.receiverOperations.TryGetValue(member, out var receiver) ? HirAdjust.Of(receiver) : HirAdaptation.None;
                uses.Use(member.Left, adaptation, UseKind.InPlace);
                break;
            case IndexKoto index when op == HirOp.Index:
                uses.Use(index.Left, kind: UseKind.InPlace);
                uses.Use(index.Right);
                break;
            case BinaryKoto or UnaryKoto when op is HirOp.Assign or HirOp.CompoundAssign:
                uses.FixAdjust(node is BinaryKoto target ? target.Left : ((UnaryKoto)node).Operand, new(HirAcquire.None, HirAdaptation.None));
                uses.Use((node as BinaryKoto)?.Right);
                break;
            case BinaryKoto or UnaryKoto or RangeKoto when op is HirOp.PrimitiveOp or HirOp.Logical or HirOp.Range:
                var use = op == HirOp.PrimitiveOp ? UseKind.Inspection : UseKind.Acquisition;
                uses.Use(node is BinaryKoto binary ? binary.Left : node is RangeKoto range ? range.Start : ((UnaryKoto)node).Operand, kind: use);
                uses.Use((node as BinaryKoto)?.Right ?? (node as RangeKoto)?.End, kind: use);
                break;
            case TupleLiteralKoto or ArrayLiteralKoto:
                foreach (var element in node is TupleLiteralKoto tuple ? tuple.Elements : ((ArrayLiteralKoto)node).Elements)
                {
                    uses.Use(element);
                }

                break;
            case DictionaryLiteralKoto dictionary:
                foreach (var entry in dictionary.Entries)
                {
                    uses.Use(entry.Key);
                    uses.Use(entry.Value);
                }

                break;
            case IfKoto conditional:
                for (var i = 0; i < conditional.Branches.Count; i++)
                {
                    uses.Use(conditional.Branches[i].Condition);
                    uses.Use(ResultOf(conditional.Branches[i].Body));
                }

                uses.Use(ResultOf(conditional.ElseBody));
                break;
            case JumpKoto or WhileKoto or RequireKoto or TestVerificationKoto or DiscardKoto:
                uses.Use(node switch { JumpKoto jump => jump.Expression, WhileKoto loop => loop.Condition, RequireKoto require => require.Condition, DiscardKoto discard => discard.Operand, _ => ((TestVerificationKoto)node).Condition });
                uses.Use((node as TestVerificationKoto)?.Message);
                break;
            case MatchKoto match:
                var plan = BoundMatch.Of(match);
                Subject(match.Expression, plan?.SubjectBorrow, ref uses);
                Bindings(plan, ref uses);
                for (var i = 0; i < match.Arms.Count; i++)
                {
                    uses.Use(match.Arms[i].Guard);
                    uses.Use(ResultOf(match.Arms[i].Body));
                }

                break;
            case ForKoto loop:
                uses.Use(loop.Iterable);
                var decomposition = BoundMatch.Of(loop);
                for (var i = 0; decomposition is null && i < loop.Bindings.Count; i++)
                {
                    uses.FixAdjust(loop.Bindings[i], new(HirAcquire.Copy, HirAdaptation.None)); // SPEC 4.6.4: a ResolvedRange loop binds isize positions.
                }

                Bindings(decomposition, ref uses);
                break;
            case MacroKoto { Formatting.Acquisition.BoundCall.ArgumentOperations: [var writer, ..] }:
                uses.Use(writer.Source, writer); // A $tryWrite acquires its writer operand (SPEC 22.10.4).
                break;
        }

        // An enum Case's payloads, then each call of the node: a value call's callee in its receiver mode (SPEC 7.7.3), a reference to the
        // callable passed as it is in that mode. Of a user index's index and indexUniq, a use of the element that needs write or exclusive
        // capability selects indexUniq (SPEC 4.7.1; SelectsUniq), and the other's inputs are not used.
        if (op == HirOp.Aggregate && this.TryGetEnumConstruction(node, out var construction))
        {
            foreach (ref readonly var payload in construction!.PayloadOperations)
            {
                uses.Use(payload.Source, payload);
            }
        }

        var unselected = node is IndexKoto ? this.IndexerCall(node, !this.SelectsUniq(node, uses.UseExclusive))?.CallStorage : null;
        for (var i = 0; CallPlan.Of(node, i) is { } call; i++)
        {
            uses.Call(i);
            if (ReferenceEquals(call, unselected))
            {
                continue;
            }

            if (call.Kind == CalleeKind.Value && call.ReceiverOperation.Source is null)
            {
                var reference = call.CalleeType is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq } callee ? callee.Semantics : SemanticsKind.Owner;
                var mode = call.ReceiverKind == SemanticsKind.Owner || reference == call.ReceiverKind ? HirAdaptation.None : reference != SemanticsKind.Owner ? HirAdaptation.ReferenceRead
                    : call.ReceiverKind == SemanticsKind.Uniq ? HirAdaptation.ExclusiveBorrow : HirAdaptation.SharedBorrow;
                uses.Use(call.CalleeValue, mode);
            }
            else
            {
                uses.Use(call.Kind == CalleeKind.Value ? call.ReceiverOperation.Source : call.Receiver, call.ReceiverOperation);
            }

            foreach (ref readonly var argument in call.ArgumentOperations)
            {
                uses.Use(argument.Source, argument);
            }
        }

        uses.Call(-1);
    }

    // The Adjust of a child its consumer acquires or uses: an adaptation Binding recorded at the node itself (at its fixed expected Type,
    // or as a receiver; an erasure to its Function Type, SPEC 7.7.4) wins over its consumer's operation, except Binding's in-place read of
    // a selection (a shared borrow of the child's own stored Type, ElementAccess.IsSynthesizedAccess), which this use's exclusive borrow of
    // the child or the child's claim overrides (claimed: this use's operation's target or an earlier use needed exclusive capability of
    // it, as the contract states; SPEC 4.7.1, 7.3). A Place adapted or used in place is not acquired, an adapted value is a Temporary, and
    // an unadapted one acquired by value, or erased, is acquired by the bare rule. A built-in operator's operand that is not Copy, a
    // comparison's, is inspected through a shared Loan from its evaluation instead (SPEC 13.4): a Place in place, a value other than a
    // literal in its temporary.
    private HirAdjust ChildAdjust(Koto child, HirAdaptation adaptation, UseKind kind, bool claimed)
    {
        var recordedWins = this.adaptations.TryGetValue(child, out var recorded) && !(ElementAccess.IsSynthesizedAccess(child) &&
            (adaptation == HirAdaptation.ExclusiveBorrow || claimed));
        adaptation = recordedWins ? HirAdjust.Of(recorded, child.BoundType)
            : HirTables.ErasedType(child) is not null ? HirAdaptation.Erasure : adaptation;
        var acquire = adaptation is not (HirAdaptation.None or HirAdaptation.Erasure) || kind == UseKind.InPlace
            ? this.IsPlace(child) ? HirAcquire.None : HirAcquire.Temporary : this.BareOf(child);
        if (kind == UseKind.Inspection && adaptation == HirAdaptation.None && (acquire is HirAcquire.Move or HirAcquire.Reborrow or HirAcquire.ByCase ||
            (acquire == HirAcquire.Temporary && child is not StringLiteralKoto && child.BoundType is { } type && this.ProveCopy(type, child) != ConstraintProof.Proven)))
        {
            (acquire, adaptation) = (acquire == HirAcquire.Temporary ? acquire : HirAcquire.None, HirAdaptation.SharedBorrow);
        }

        return new(acquire, adaptation);
    }

    // Binding's bare acquisition of a Place (BareAcquire) as it is: a Move there is the explicit Move a Place whose Copy is unproven needs
    // (SPEC 3.5, TransferRequired), and only an Error proof leaves it unset; a value that is not a Place is a Temporary.
    private HirAcquire BareOf(Koto child)
    {
        var place = this.IsPlace(child);
        if (!place || child.BoundType is not { } type)
        {
            return place ? HirAcquire.Unset : HirAcquire.Temporary;
        }

        var bare = this.BareAcquire(type, child, out var proof);
        return bare == HirAcquire.Move && proof == ConstraintProof.Error ? HirAcquire.Unset : bare;
    }

    // A bare Place (SPEC 3.5), a raw Place (SPEC 5.2), a static, or the storage a formatting plan prepared (its buffer, its writer).
    private bool IsPlace(Koto child) => IsBarePlace(child) || ElementAccess.IsRawPlace(child) || child is FormattingKoto { Operation: FormattingOperation.Storage } ||
        IsStatic(KotoHelper.UnwrapParentheses(child).BoundSymbol);

    // The publisher's record of the uses, a struct, so that AdjustChildren is specialized for it and calls it directly (the S1 publish A/B:
    // the publisher passed as itself, a class, made every use an interface call and a warm publish about 2% slower).
    [Transitional("R7", Reason = "R7 replaces the publisher walk with inline Binding writes.")]
    private readonly struct PublisherUses(HirPublisher publisher) : IHirUses
    {
        public bool UseExclusive => publisher.UseExclusive;

        public void Call(int ordinal) => publisher.Call(ordinal);

        public void Use(Koto? child, HirAdaptation adaptation, UseKind kind) => publisher.Use(child, adaptation, kind);

        public void Use(Koto? child, in BoundArgumentOperation operation) => publisher.Use(child, in operation);

        public void FixAdjust(Koto? child, HirAdjust adjust, HirPlace place) => publisher.FixAdjust(child, adjust, place);
    }

    private sealed partial class HirPublisher
    {
        // The call of the node whose inputs AdjustChildren gives, by its CallPlan.Of ordinal; -1 outside its calls (FixAdjust).
        private int callOrdinal = -1;

        internal bool UseExclusive => this.exclusive;

        internal void Call(int ordinal) => this.callOrdinal = ordinal;

        // A child an operation uses (a call's receiver or argument, a Case payload, a writer; SPEC 7.3, 10.2), as the operation adapts it and
        // with the capability its target needs (Exclusive), which it claims.
        internal void Use(Koto? child, in BoundArgumentOperation operation)
        {
            if (this.Exclusive(operation.AdaptedType ?? operation.ParameterType) && this.Owns(child))
            {
                this.claims[child!.SyntaxId] = true;
            }

            this.Use(child, HirAdjust.Of(operation));
        }

        // A child its consumer acquires or uses, as that use and its own facts give it (ChildAdjust), with the capability its consumer claimed.
        internal void Use(Koto? child, HirAdaptation adaptation = HirAdaptation.None, UseKind kind = UseKind.Acquisition)
        {
            if (child is not null)
            {
                this.FixAdjust(child, binding.ChildAdjust(child, adaptation, kind, claimed: this.Owns(child) && this.claims[child.SyntaxId]));
            }
        }

        // Only the nodes of the module being published; a call shared across modules names nodes of another. An exclusive borrow, and the
        // Reborrow or pair-case acquisition of a Place whose Type needs exclusive capability (SPEC 10.2, 4.7.2, 8.14), is an exclusive use.
        // At a call input the earlier call's Adjust (EarlierAdjust) is the input's plan-row Adjust, the column keeping the later; at any other
        // call input the row states none, which also clears a row of an earlier pass.
        internal void FixAdjust(Koto? child, HirAdjust adjust, HirPlace place = default)
        {
            if (this.Owns(child))
            {
                ref var column = ref this.table!.Hir.Adjusts![child!.SyntaxId];
                if (this.callOrdinal >= 0)
                {
                    var earlier = EarlierAdjust(this.callOrdinal, column, adjust);
                    HirTables.PlanFor(child, earlier != default).Adjust = earlier;
                }

                column = adjust;
                this.claims[child.SyntaxId] |= adjust.Adaptation == HirAdaptation.ExclusiveBorrow ||
                    (adjust.Acquire is HirAcquire.Reborrow or HirAcquire.ByCase && this.Exclusive(child.BoundType));
                if (place.Kind != HirPlaceKind.Unset)
                {
                    this.table.Hir.Places![child.SyntaxId] = place;
                }
            }
        }

        // SPEC 23.1.2 (DECISIONS R4-M109, R4-M88 Rule 2): an Error node has no Place and no value of its own to acquire or adapt, so wherever
        // a consumer wrote an acquisition or adaptation for it, in the column or in its plan row (the earlier of two calls), it is that
        // consumer's derived value, used as it is. An Error assignment target folds too; the builder derives the whole assignment and never
        // reads it.
        private void Derived(Koto node)
        {
            var derived = new HirAdjust(HirAcquire.Temporary, HirAdaptation.None);
            if (HirTables.AdjustOf(node) != default)
            {
                this.FixAdjust(node, derived);
            }

            if (this.Owns(node) && HirTables.PlanOf(node).Adjust != default)
            {
                HirTables.PlanFor(node, written: true).Adjust = derived;
            }
        }

        // Whether a node's use needs write or exclusive capability, which selects a user index's indexUniq (SPEC 4.7.1; DECISIONS BU-8): the
        // capability its consumer's operation needs, which Use and FixAdjust recorded (claims), or it is updated or exclusively borrowed, or an
        // exclusive use reaches it as the receiver of a Field or an element, as a group's operand or as the reference, handle or pair layer a
        // follow selects through (SPEC 13.5.5, 15.6.2: past a shared layer only reads), unless that use is of a raw Place, whose capability its
        // path does not decide (SPEC 5.2). The parent is the node's while the node is visited.
        private bool ExclusiveUse(Koto node)
            => (this.Owns(node) && this.claims[node.SyntaxId]) || (this.parentOp == HirOp.Borrow && this.Exclusive(this.parent!.BoundType)) ||
            ((this.parentOp is HirOp.Assign or HirOp.CompoundAssign || (this.exclusive && (this.parentOp is HirOp.Field or HirOp.Index or HirOp.Deref ||
                this.parent is ParenthesizedKoto) && !ElementAccess.IsRawPlace(this.parent!))) && ReferenceEquals(Operand(this.parent), node));

        // Whether an acquisition's target needs exclusive capability of what it takes (SPEC 4.7.1, 13.5.5): an exclusive reference or object
        // view, or a pair layer admitting one, whose exclusive case does.
        private bool Exclusive(BoundType? target) => target is { Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq } ||
            (target is not null && (binding.LayerAdmitted(target) & (SemanticsMask.Uniq | SemanticsMask.ObjUniq)) != 0);

        // Whether a node is of the module being published.
        private bool Owns(Koto? node) => node is not null && ReferenceEquals(node.SyntaxOwner, this.table) && node.SyntaxId < this.table!.Hir.Adjusts!.Length;
    }
}
