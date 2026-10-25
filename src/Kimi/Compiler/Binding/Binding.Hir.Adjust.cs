// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The Adjust column's rule (SPEC 3.5, 10.2, 13.4, 13.5, 15.1.5), a lasting component that the HIR publisher reads.
// - Responsibility: how a consumer takes each child it consumes (Consume; a match's Subject and its Pattern bindings in
//   Binding.Hir.Patterns), and how a child's own facts complete a take (TakeAdjust, BareOf, IsPlace).
// - Inputs: a consumer and its evaluated Op (OpOf), Binding's facts for the consumer (its call plans, receiver operations, conversion,
//   match plan, enum construction) and for the child (its recorded adaptation, erasure, bare acquisition), and the capability the
//   consumer's own use needs (IHirTaker.UseExclusive).
// - Outputs: each take, to an IHirTaker; the HIR publisher writes them as the Adjust column and the plan rows.
// - Invariants: a consumer's takes come in evaluation order, its calls' inputs last and in CallPlan.Of's order; the rule reads no walk
//   state but the capability of the consumer's own use, which only a user index's selection reads (SPEC 4.7.1; DECISIONS BU-8).
// - Prohibitions: the rule writes nothing; no reader keeps a second copy of a take.
// The publisher's reader of the rule (PublisherTaker, over its claims, ExclusiveUse, Derived and column writes) is the Transitional walk's,
// until R7.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // How a consumer takes an operand (SPEC 3.5, 13.5.5, 13.4): acquired by value, used in place (a borrow, a follow, a runtime test,
    // a projection's receiver), or inspected (a built-in operator's operand, through a shared Loan where it is not Copy).
    private enum TakeKind : byte
    {
        Acquired,
        InPlace,
        Inspected,
    }

    // A reader of the Adjust column's rule (Consume): the HIR publisher (PublisherTaker), which writes each take.
    private interface IHirTaker
    {
        // The capability the consumer's own use needs, which selects a user index's indexUniq (SPEC 4.7.1; DECISIONS BU-8).
        bool UseExclusive { get; }

        // The call of the consumer whose inputs follow, by its CallPlan.Of ordinal; -1 after the last.
        void Call(int ordinal);

        // A child taken as the consumer's operation adapts it, acquired, in place or inspected, completed by its own facts (TakeAdjust).
        void Take(Koto? child, HirAdaptation adaptation = HirAdaptation.None, TakeKind kind = TakeKind.Acquired);

        // A call's receiver or argument, a Case payload or a writer, taken by its operation (SPEC 7.3, 10.2).
        void Take(Koto? child, in BoundArgumentOperation operation);

        // A child whose take the consumer fixes: a group's operand, an assignment target, a transfer, a Subject, a Pattern binding.
        void Write(Koto? child, HirAdjust adjust, HirPlace place = default);
    }

    // SPEC 13.6.1: an object upcast to a view (objref, objuniq) of an operand of another Semantics borrows the operand in the view's mode.
    private static HirAdaptation ViewBorrow(Koto node) => node is ConversionKoto { ConversionBinding: ConversionBinding.ObjectUpcast, BoundType: { IsObjectBorrow: true } view } upcast &&
        upcast.Left.BoundType?.Semantics != view.Semantics ? view.Semantics == SemanticsKind.ObjUniq ? HirAdaptation.ExclusiveBorrow : HirAdaptation.SharedBorrow : HirAdaptation.None;

    // A body or operand that supplies its construct's value (SPEC 14.9); none in Discard Context.
    private static Koto? ResultOf(Koto? source) => source is not null && KotoHelper.IsValueContext(source) ? source : null;

    // SPEC 13.7.2, 11.3 (DECISIONS R4-M109): the take of a call input that a later call of its consumer replaces by another Adjust (a
    // compound update's getter before its setter), which the builder still builds as the input's plan-row Adjust; none at the first call.
    private static HirAdjust EarlierTake(int call, HirAdjust taken, HirAdjust later) => call > 0 && taken != later ? taken : default;

    // A group Property: static storage (SPEC 6.6).
    private static bool IsStaticStorage(Koto node) => node.BoundSymbol is { Kind: BindingSymbolKind.Property, Scope.Owner: GroupKoto };

    // Whether a user index's use selects indexUniq (SPEC 4.7.1): the use needs exclusive capability and indexUniq is bound.
    private bool SelectsUniq(Koto index, bool exclusive) => exclusive && this.IndexerCall(index, true) is not null;

    // The children a node's Op consumes (SPEC 3.5, 10.2, 15.1.5): its own operands first, then its calls' receivers and arguments by
    // their operations, in evaluation order; a deeper consumer, such as a setter taking an assignment's value, is taken last.
    private void Consume<T>(Koto node, HirOp op, ref T taker)
        where T : IHirTaker
    {
        switch (node)
        {
            case FunctionKoto function:
                for (var i = 0; i < function.Parameters.Count; i++)
                {
                    taker.Take(function.Parameters[i].DefaultValue);
                }

                taker.Take(ResultOf(op is HirOp.Body or HirOp.Closure ? function.ExpressionBody : null));
                break;
            case PropertyAccessorKoto or CodeBlockKoto or DoKoto:
                taker.Take(ResultOf(node is PropertyAccessorKoto accessor ? accessor.Body
                    : node is DoKoto scoped ? scoped.Body : ((CodeBlockKoto)node).TrailingExpression));
                break;
            case VariableKoto or PropertyKoto:
                taker.Take(node is VariableKoto variable ? variable.InitializerKoto : ((PropertyKoto)node).InitializerKoto);
                break;
            case ParenthesizedKoto or LabeledKoto when op == HirOp.Group: // Its group acquires it, an erasure recorded at it included.
                var grouped = node is LabeledKoto label ? label.Target : ((UnaryKoto)node).Operand;
                taker.Write(grouped, HirTables.ErasedType(grouped) is null ? new(HirAcquire.None, HirAdaptation.None) : new(this.BareOf(grouped), HirAdaptation.Erasure));
                break;
            case ConversionKoto { ConversionBinding: ConversionBinding.Transfer } transfer:
                taker.Write(transfer.Left, new(HirAcquire.Move, HirAdaptation.None));
                break;
            case ConversionKoto or DereferenceKoto or IsKoto: // A borrow, a followed referent and a runtime test take their operand in place.
                var inPlace = op is HirOp.Borrow or HirOp.Deref or HirOp.RuntimeTypeTest ? TakeKind.InPlace : TakeKind.Acquired;
                taker.Take(node is DereferenceKoto dereference ? dereference.Operand : ((BinaryKoto)node).Left, ViewBorrow(node), inPlace);
                break;
            case MemberAccessKoto member when op is HirOp.Field or HirOp.PrimitiveOp: // A stored Field or Tuple element, or a sequence's metadata.
                var operation = this.receiverOperations.TryGetValue(member, out var receiver) ? HirAdjust.Of(receiver) : HirAdaptation.None;
                taker.Take(member.Left, operation, TakeKind.InPlace);
                break;
            case IndexKoto index when op == HirOp.Index:
                taker.Take(index.Left, kind: TakeKind.InPlace);
                taker.Take(index.Right);
                break;
            case BinaryKoto or UnaryKoto when op is HirOp.Assign or HirOp.CompoundAssign:
                taker.Write(node is BinaryKoto target ? target.Left : ((UnaryKoto)node).Operand, new(HirAcquire.None, HirAdaptation.None));
                taker.Take((node as BinaryKoto)?.Right);
                break;
            case BinaryKoto or UnaryKoto or RangeKoto when op is HirOp.PrimitiveOp or HirOp.Logical or HirOp.Range:
                var inspected = op == HirOp.PrimitiveOp ? TakeKind.Inspected : TakeKind.Acquired;
                taker.Take(node is BinaryKoto binary ? binary.Left : node is RangeKoto range ? range.Start : ((UnaryKoto)node).Operand, kind: inspected);
                taker.Take((node as BinaryKoto)?.Right ?? (node as RangeKoto)?.End, kind: inspected);
                break;
            case TupleLiteralKoto or ArrayLiteralKoto:
                foreach (var element in node is TupleLiteralKoto tuple ? tuple.Elements : ((ArrayLiteralKoto)node).Elements)
                {
                    taker.Take(element);
                }

                break;
            case DictionaryLiteralKoto dictionary:
                foreach (var entry in dictionary.Entries)
                {
                    taker.Take(entry.Key);
                    taker.Take(entry.Value);
                }

                break;
            case IfKoto conditional:
                for (var i = 0; i < conditional.Branches.Count; i++)
                {
                    taker.Take(conditional.Branches[i].Condition);
                    taker.Take(ResultOf(conditional.Branches[i].Body));
                }

                taker.Take(ResultOf(conditional.ElseBody));
                break;
            case JumpKoto or WhileKoto or RequireKoto or TestVerificationKoto or DiscardKoto:
                taker.Take(node switch { JumpKoto jump => jump.Expression, WhileKoto loop => loop.Condition, RequireKoto require => require.Condition, DiscardKoto discard => discard.Operand, _ => ((TestVerificationKoto)node).Condition });
                taker.Take((node as TestVerificationKoto)?.Message);
                break;
            case MatchKoto match:
                var plan = BoundMatch.Of(match);
                Subject(match.Expression, plan?.SubjectBorrow, ref taker);
                Bindings(plan, ref taker);
                for (var i = 0; i < match.Arms.Count; i++)
                {
                    taker.Take(match.Arms[i].Guard);
                    taker.Take(ResultOf(match.Arms[i].Body));
                }

                break;
            case ForKoto loop:
                taker.Take(loop.Iterable);
                var decomposition = BoundMatch.Of(loop);
                for (var i = 0; decomposition is null && i < loop.Bindings.Count; i++)
                {
                    taker.Write(loop.Bindings[i], new(HirAcquire.Copy, HirAdaptation.None)); // SPEC 4.6.4: a ResolvedRange loop binds isize positions.
                }

                Bindings(decomposition, ref taker);
                break;
            case MacroKoto { Formatting.Acquisition.BoundCall.ArgumentOperations: [var writer, ..] }:
                taker.Take(writer.Source, writer); // A $tryWrite acquires its writer operand (SPEC 22.10.4).
                break;
        }

        // An enum Case's payloads, then each call of the node: a value call's callee in its receiver mode (SPEC 7.7.3), a reference to the
        // callable passed as it is in that mode. Of a user index's index and indexUniq, a use of the element that needs write or exclusive
        // capability takes indexUniq (SPEC 4.7.1; SelectsUniq), and the other's inputs are not taken.
        if (op == HirOp.Aggregate && this.TryGetEnumConstruction(node, out var construction))
        {
            foreach (ref readonly var payload in construction!.PayloadOperations)
            {
                taker.Take(payload.Source, payload);
            }
        }

        var unselected = node is IndexKoto ? this.IndexerCall(node, !this.SelectsUniq(node, taker.UseExclusive))?.CallStorage : null;
        for (var i = 0; CallPlan.Of(node, i) is { } call; i++)
        {
            taker.Call(i);
            if (ReferenceEquals(call, unselected))
            {
                continue;
            }

            if (call.Kind == CalleeKind.Value && call.ReceiverOperation.Source is null)
            {
                var reference = call.CalleeType is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq } callee ? callee.Semantics : SemanticsKind.Owner;
                var mode = call.ReceiverKind == SemanticsKind.Owner || reference == call.ReceiverKind ? HirAdaptation.None : reference != SemanticsKind.Owner ? HirAdaptation.ReferenceRead
                    : call.ReceiverKind == SemanticsKind.Uniq ? HirAdaptation.ExclusiveBorrow : HirAdaptation.SharedBorrow;
                taker.Take(call.CalleeValue, mode);
            }
            else
            {
                taker.Take(call.Kind == CalleeKind.Value ? call.ReceiverOperation.Source : call.Receiver, call.ReceiverOperation);
            }

            foreach (ref readonly var argument in call.ArgumentOperations)
            {
                taker.Take(argument.Source, argument);
            }
        }

        taker.Call(-1);
    }

    // A consumed child's Adjust: an adaptation Binding recorded at the node itself (at its fixed expected Type, or as a receiver; an erasure to
    // its Function Type, SPEC 7.7.4) wins over its consumer's operation, except Binding's in-place read of a selection (a shared borrow of the
    // child's own stored Type, ElementAccess.IsSynthesizedAccess), which a consumer needing exclusive capability of the child (an exclusive
    // borrow, or an operation whose target needs it: claimed) overrides (SPEC 4.7.1, 7.3). A Place adapted or used in place is not acquired,
    // an adapted value is a Temporary, and an unadapted one taken by value, or erased, is acquired by the bare rule. A built-in operator's
    // operand that is not Copy, a comparison's, is inspected through a shared Loan from its evaluation instead (SPEC 13.4): a Place in
    // place, a value other than a literal in its temporary.
    private HirAdjust TakeAdjust(Koto child, HirAdaptation adaptation, TakeKind kind, bool claimed)
    {
        var recordedWins = this.adaptations.TryGetValue(child, out var recorded) && !(ElementAccess.IsSynthesizedAccess(child) &&
            (adaptation == HirAdaptation.ExclusiveBorrow || claimed));
        adaptation = recordedWins ? HirAdjust.Of(recorded, child.BoundType)
            : HirTables.ErasedType(child) is not null ? HirAdaptation.Erasure : adaptation;
        var acquire = adaptation is not (HirAdaptation.None or HirAdaptation.Erasure) || kind == TakeKind.InPlace
            ? this.IsPlace(child) ? HirAcquire.None : HirAcquire.Temporary : this.BareOf(child);
        if (kind == TakeKind.Inspected && adaptation == HirAdaptation.None && (acquire is HirAcquire.Move or HirAcquire.Reborrow or HirAcquire.ByCase ||
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
        IsStaticStorage(KotoHelper.UnwrapParentheses(child));

    // The publisher as Consume's reader, a struct, so that Consume is specialized for it and calls it directly (the S1 publish A/B: the
    // publisher passed as itself, a class, made every take an interface call and a warm publish about 2% slower).
    private readonly struct PublisherTaker(HirPublisher publisher) : IHirTaker
    {
        public bool UseExclusive => publisher.UseExclusive;

        public void Call(int ordinal) => publisher.Call(ordinal);

        public void Take(Koto? child, HirAdaptation adaptation = HirAdaptation.None, TakeKind kind = TakeKind.Acquired)
            => publisher.Take(child, adaptation, kind);

        public void Take(Koto? child, in BoundArgumentOperation operation) => publisher.Take(child, in operation);

        public void Write(Koto? child, HirAdjust adjust, HirPlace place = default) => publisher.Write(child, adjust, place);
    }

    private sealed partial class HirPublisher
    {
        // The call of the node Consume takes inputs for, by its CallPlan.Of ordinal; -1 outside its calls (Write).
        private int callOrdinal = -1;

        public bool UseExclusive => this.exclusive;

        public void Call(int ordinal) => this.callOrdinal = ordinal;

        // A child an operation takes (a call's receiver or argument, a Case payload, a writer; SPEC 7.3, 10.2), as the operation adapts it and
        // with the capability its target needs (Exclusive).
        public void Take(Koto? child, in BoundArgumentOperation operation)
        {
            if (this.Exclusive(operation.AdaptedType ?? operation.ParameterType) && this.Owns(child))
            {
                this.claims[child!.SyntaxId] = true;
            }

            this.Take(child, HirAdjust.Of(operation));
        }

        // A consumed child, as its consumer's take and its own facts give it (TakeAdjust), with the capability its consumers claimed.
        public void Take(Koto? child, HirAdaptation adaptation = HirAdaptation.None, TakeKind kind = TakeKind.Acquired)
        {
            if (child is not null)
            {
                this.Write(child, binding.TakeAdjust(child, adaptation, kind, claimed: this.Owns(child) && this.claims[child.SyntaxId]));
            }
        }

        // Only the nodes of the module being published; a call shared across modules names nodes of another. An exclusive borrow, and the
        // Reborrow or pair-case acquisition of a Place whose Type needs exclusive capability (SPEC 10.2, 4.7.2, 8.14), is an exclusive use.
        // At a call input the earlier call's take (EarlierTake) is the input's plan-row Adjust, the column keeping the later; at any other
        // call input the row states none, which also clears a row of an earlier pass.
        public void Write(Koto? child, HirAdjust adjust, HirPlace place = default)
        {
            if (this.Owns(child))
            {
                ref var column = ref this.table!.Hir.Adjusts![child!.SyntaxId];
                if (this.callOrdinal >= 0)
                {
                    var earlier = EarlierTake(this.callOrdinal, column, adjust);
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
        // consumer's derived value, taken as it is. An Error assignment target folds too; the builder derives the whole assignment and never
        // reads it.
        private void Derived(Koto node)
        {
            var derived = new HirAdjust(HirAcquire.Temporary, HirAdaptation.None);
            if (HirTables.AdjustOf(node) != default)
            {
                this.Write(node, derived);
            }

            if (this.Owns(node) && HirTables.PlanOf(node).Adjust != default)
            {
                HirTables.PlanFor(node, written: true).Adjust = derived;
            }
        }

        // Whether a node's use needs write or exclusive capability, which selects a user index's indexUniq (SPEC 4.7.1; DECISIONS BU-8): the
        // capability its consumer's operation needs, which Take and Write recorded (claims), or it is updated or exclusively borrowed, or an
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
