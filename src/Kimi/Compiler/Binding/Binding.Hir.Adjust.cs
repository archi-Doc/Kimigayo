// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The Adjust column's rule (SPEC 3.5, 10.2, 13.4, 13.5, 15.1.5), a lasting component that the HIR publisher reads.
// - Responsibility: how a consumer acquires or uses each of its children (AdjustChildren; a match's Subject and its Pattern bindings in
//   Binding.Hir.Patterns), how a child's own facts complete its use (ChildAdjust, BareOf, IsPlace), how a Place in Discard Context
//   is acquired (Discarded), and what a use of a Place needs of it (UseOf over the rows' takes, Take), which the HIR publisher's claim and
//   ClosureEffects read.
// - Inputs: a consumer and its evaluated Op (OpOf), Binding's facts for the consumer (its call plans, receiver operations, conversion,
//   match plan, enum construction) and for the child (its recorded adaptation, erasure, bare acquisition); and two facts of the record's
//   walk: the capability the consumer's own use needs (AdjustChildren's `exclusive`), which only a user index's selection reads (SPEC
//   4.7.1; DECISIONS BU-8), and a child's claim (ChildAdjust's `claimed`): whether a use of the child that stands needed exclusive
//   capability of it, by the Adjust that use gave (its operation's target, which the row states, HirAdjust.Exclusive, an exclusive
//   borrow, or a Reborrow or pair-case acquisition of a child whose Type needs it): the rule's Claimed, over the two rows every record
//   keeps (DECISIONS R7-M8; a `$tryWrite` storage's plan row has no defined value and no path below it, PLAN G103 (ii)). A take a later
//   take replaces with no plan row is no use: the operation that replaced it states what the use needs (SPEC 13.7.2,
//   15.1.6: a for's Subject before its entry call, a compound assignment's right side before its operator). The claim decides whether
//   Binding's recorded in-place shared read of a selection yields (SPEC 4.7.1, 7.3). This use's operation's target decides as such a
//   claim does: a part below a stored reference passed as a `uniq/Self` receiver is a Reborrow
//   (HirFormsTest.APartBelowAStoredReferenceIsAPlace). Every record takes an operation's use by ChildAdjust(child, in operation). A use
//   in place forms no reference and states no mode: its consumer's use decides its capability.
// - Outputs: each child's use, to an IHirUses; the HIR publisher writes them as the Adjust column and the plan rows, a discarded
//   Place's as its plan row's (PlanRow.Adjust); ClosureEffects finds one capture use's (Binding.ClosureReceivers).
// - Invariants: a consumer's uses come in evaluation order, its calls' inputs last and in CallPlan.Of's order; the rule reads no other
//   walk state.
// - Prohibitions: the rule writes nothing; no record keeps a second copy of a use's Adjust.
// The publisher's record of the uses (RowUses) writes the rows (DECISIONS R4-M142). A take's claim is read from the rows alone, before
// ExclusiveUse's read of the node: its consumer's (the column), an earlier call's or its container's discard (the plan row; PLAN G426).

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // What a use of a Place needs of it, by strength (SPEC 4.7.1, 7.7.3, 8.14): read capability; exclusive capability only in the admitted
    // cases where its acquisition Reborrows; exclusive capability; or its value, moved out. A Binding representation (UseOf), no column
    // (the owner's O-40 (a)).
    private enum PlaceUse : byte
    {
        Shared,
        ByCase,
        Exclusive,
        Consuming,
    }

    // How a consumer uses an operand (SPEC 3.5, 13.5.5, 13.4): an acquisition by value, a use in place (a borrow, a follow, a runtime test,
    // a projection's receiver), or an inspection (a built-in operator's operand, through a shared Loan where it is not Copy).
    private enum UseKind : byte
    {
        Acquisition,
        InPlace,
        Inspection,
    }

    // The record of the uses the Adjust column's rule gives (AdjustChildren): the HIR publisher's (RowUses), which writes each, and
    // ClosureEffects' record (TakeRecord), which keeps every child's once per consumer and selection.
    private interface IHirUses
    {
        // The call of the consumer whose inputs follow, by its CallPlan.Of ordinal; -1 after the last.
        void Call(int ordinal);

        // A child the consumer's operation adapts, acquired, used in place or inspected, completed by its own facts (ChildAdjust).
        void Use(Koto? child, HirAdaptation adaptation = HirAdaptation.None, UseKind kind = UseKind.Acquisition);

        // A call's receiver or argument, a Case payload or a writer, used by its operation (SPEC 7.3, 10.2).
        void Use(Koto? child, in BoundArgumentOperation operation);

        // A child whose Adjust the consumer fixes: a group's operand, an assignment target, a transfer, a Subject, a Pattern binding.
        void FixAdjust(Koto? child, HirAdjust adjust, HirPlace place = default);
    }

    // Whether an acquisition's target needs exclusive capability of what it takes (SPEC 4.7.1, 13.5.5): an exclusive reference or object
    // view, or a pair layer admitting one, whose exclusive case does.
    internal bool Exclusive(BoundType? target) => target is { Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq } ||
        (target is not null && (this.LayerAdmitted(target) & (SemanticsMask.Uniq | SemanticsMask.ObjUniq)) != 0);

    // SPEC 13.6.1: an object upcast to a view (objref, objuniq) of an operand of another Semantics borrows the operand in the view's mode.
    private static HirAdaptation ViewBorrow(Koto node) => node is ConversionKoto { ConversionBinding: ConversionBinding.ObjectUpcast, BoundType: { IsObjectBorrow: true } view } upcast &&
        upcast.Left.BoundType?.Semantics != view.Semantics ? view.Semantics == SemanticsKind.ObjUniq ? HirAdaptation.ExclusiveBorrow : HirAdaptation.SharedBorrow : HirAdaptation.None;

    // A body or operand that supplies its construct's value (SPEC 14.9); none in Discard Context.
    private static Koto? ResultOf(Koto? source) => source is not null && IsValueContext(source) ? source : null;

    // SPEC 13.7.2, 11.3 (DECISIONS R4-M109): the Adjust of a call input that a later call of its consumer replaces by another (a compound
    // update's getter before its setter), which the builder still builds as the input's plan-row Adjust; none at the first call.
    private static HirAdjust EarlierAdjust(int call, HirAdjust earlier, HirAdjust later) => call > 0 && earlier != later ? earlier : default;

    // The operand a parent projects, updates, groups or borrows.
    private static Koto? Operand(Koto? parent) => (parent as BinaryKoto)?.Left ?? (parent as UnaryKoto)?.Operand;

    // Whether a user index's use selects indexUniq (SPEC 4.7.1): the use needs exclusive capability and indexUniq is bound.
    private bool SelectsUniq(Koto index, bool exclusive) => exclusive && this.IndexerCall(index, exclusive: true) is not null;

    // The children a node's Op acquires or uses (SPEC 3.5, 10.2, 15.1.5): its own operands first, then its calls' receivers and arguments by
    // their operations, in evaluation order; a deeper consumer, such as a setter acquiring an assignment's value, is used last. Exclusive
    // is the capability the consumer's own use needs, which selects a user index's indexUniq (SPEC 4.7.1; DECISIONS BU-8).
    private void AdjustChildren<T>(Koto node, HirOp op, bool exclusive, ref T uses)
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
            case ConversionKoto or DereferenceKoto or IsKoto: // A borrow, a followed referent, a runtime test and a case adaptation (SPEC 8.14, 13.5.3:
                // its Adapt reads a Place in place and a value through a Temporary; R6-check Q14 (b)) use their operand in place.
                var inPlace = op is HirOp.Borrow or HirOp.Deref or HirOp.RuntimeTypeTest ||
                    node is ConversionKoto { ConversionBinding: ConversionBinding.CaseAdaptation };
                uses.Use(
                    node is DereferenceKoto dereference ? dereference.Operand : ((BinaryKoto)node).Left,
                    ViewBorrow(node),
                    inPlace ? UseKind.InPlace : UseKind.Acquisition);
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

        var unselected = node is IndexKoto ? this.IndexerCall(node, !this.SelectsUniq(node, exclusive))?.CallStorage : null;
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
    // or as a receiver) wins over its consumer's operation, except Binding's in-place read of a selection (a shared borrow of the child's
    // own stored Type, ElementAccess.IsSynthesizedAccess), which this use's exclusive borrow of the child or the child's claim overrides
    // (claimed: this use's operation's target or an earlier use needed exclusive capability of it, as the contract states; SPEC 4.7.1,
    // 7.3). An erasure to its Function Type (SPEC 7.7.4) is the node's own record, its ErasedType: that column, never a consumer's
    // operation, owns where an erasure is applied (DECISIONS BU-83). A Place adapted or used in place is not acquired, an adapted value is
    // a Temporary, and an unadapted one acquired by value, or erased, is acquired by the bare rule. A built-in operator's operand that is
    // not Copy, a comparison's, is inspected through a shared Loan from its evaluation instead (SPEC 13.4): a Place in place, a value
    // other than a literal in its temporary.
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

    // The Adjust of a child an operation takes (a call's receiver or argument, a Case payload, a writer; SPEC 7.3, 10.2), the one route of
    // such a use: its need is its target's, whatever its adaptation (DECISIONS BU-8), which decides as an earlier use's claim does and which
    // the row states. The target decides also where a recorded read gives the adaptation: a recorded Type names what the layers reach,
    // which a shared receiver through exclusive layers does not take (SPEC 15.6.2).
    private HirAdjust ChildAdjust(Koto child, in BoundArgumentOperation operation, bool claimed)
    {
        var exclusive = this.Exclusive(operation.AdaptedType ?? operation.ParameterType);
        return this.ChildAdjust(child, HirAdjust.Of(operation), UseKind.Acquisition, claimed || exclusive) with { ExclusiveMode = exclusive };
    }

    // SPEC 14.2.2, 3.5 (DECISIONS R4-M145): a Place in Discard Context is acquired by bare acquisition, as at a value position, and
    // discarded; no operation of its container takes it, so no claim completes it, while its own Adjust, the publisher's discard row, claims
    // as a fixed one does (Claimed).
    private HirAdjust Discarded(Koto place) => this.ChildAdjust(place, HirAdaptation.None, UseKind.Acquisition, claimed: false);

    // Binding's bare acquisition of a Place (BareAcquire) as it is: a Move there is the explicit Move a Place whose Copy is unproven needs
    // (SPEC 3.5, TransferRequired); a Place without a Type, or whose Move rests on an Error Copy proof, failed (SPEC 23.1.2; DECISIONS
    // BU-103); a value that is not a Place is a Temporary.
    private HirAcquire BareOf(Koto child)
    {
        if (!this.IsPlace(child))
        {
            return HirAcquire.Temporary;
        }

        return child.BoundType is { } type && this.BareAcquire(type, child, out var proof) is var bare &&
            !(bare == HirAcquire.Move && proof == ConstraintProof.Error) ? bare : HirAcquire.Failed;
    }

    // A bare Place (SPEC 3.5; a raw Place and its parts included, SPEC 3.4.1, 5.2), a static, or the storage a formatting plan prepared
    // (its buffer, its writer).
    private bool IsPlace(Koto child) => IsBarePlace(child) || child is FormattingKoto { Operation: FormattingOperation.Storage } ||
        IsStatic(KotoHelper.UnwrapParentheses(child).BoundSymbol);

    // What a take needs of what it takes, a Place storing the Type, acquired at the node (SPEC 10.2, 4.7.2, 8.14, 7.7.2): a pair-case
    // acquisition that Reborrows in some case (BareReborrows, the owner case's target included), ByCase, tested first, so that a pair
    // target's exclusive mode stays by case; its operation's target or an exclusive borrow (HirAdjust.Exclusive) or the Reborrow of a Type
    // that needs exclusive capability, Exclusive; a Move, Consuming; any other, Shared. A child's take is acquired at the child; a nested
    // Closure's capture entry (BoundCapture.Adjust) at the nested Closure.
    private PlaceUse Take(BoundType? type, Koto node, HirAdjust adjust) =>
        adjust.Acquire == HirAcquire.ByCase && type is not null && this.BareReborrows(type, node) ? PlaceUse.ByCase
        : adjust.Exclusive || (adjust.Acquire == HirAcquire.Reborrow && this.Exclusive(type)) ? PlaceUse.Exclusive
        : adjust.Acquire == HirAcquire.Move ? PlaceUse.Consuming : PlaceUse.Shared;

    // Whether a child's takes so far claim it: its last row (the column's) or the row beside it (PlanRow.Adjust: an earlier call's, or its
    // container's discard), the two rows every record of the uses keeps (DECISIONS R7-M8); a `$tryWrite` storage's plan row has no defined
    // value and no path below it (PLAN G103 (ii)).
    private bool Claimed(Koto child, HirAdjust last, HirAdjust earlier) =>
        this.Take(child.BoundType, child, last) is PlaceUse.ByCase or PlaceUse.Exclusive ||
        this.Take(child.BoundType, child, earlier) is PlaceUse.ByCase or PlaceUse.Exclusive;

    // The one rule of what a use of a Place needs of it (SPEC 4.7.1, 7.7.3, 8.14, 13.5.6, 15.1.5, 15.1.6): the strongest of its takes that
    // stand (the two rows, Take) and its parent's own operation on it, which no row states: an explicit adaptation that borrows it
    // exclusively in some admitted case (BorrowsExclusively: an exclusive borrow, an object view upcast, a case adaptation), an assignment
    // or update target, or an Exclusive-mode `for` or `match` Subject. The HIR publisher reads it for indexUniq (ExclusiveUse), ClosureEffects
    // for a Closure's receiver (DECISIONS R7-Y2); neither decides a use's need itself.
    private PlaceUse UseOf(Koto child, HirAdjust last, HirAdjust earlier, Koto? parent, HirOp parentOp)
    {
        var own = parent switch
        {
            ConversionKoto conversion => ReferenceEquals(conversion.Left, child) && this.BorrowsExclusively(conversion),
            ForKoto loop => ReferenceEquals(loop.Iterable, child) && loop.Mode == SubjectMode.Exclusive,
            MatchKoto match => ReferenceEquals(match.Expression, child) && BoundMatch.Of(match) is { Mode: SubjectMode.Exclusive },
            _ => parentOp is HirOp.Assign or HirOp.CompoundAssign && ReferenceEquals(Operand(parent), child),
        };

        // An own operation never moves its operand (an assignment target, a borrow's operand and a Subject are not moved), so Consuming
        // and an own exclusive operation never meet.
        var use = (PlaceUse)Math.Max((byte)this.Take(child.BoundType, child, last), (byte)this.Take(child.BoundType, child, earlier));
        return own && use < PlaceUse.Exclusive ? PlaceUse.Exclusive : use;
    }

    // The publisher's record of the uses of one module's nodes (a call shared across modules names nodes of another), a struct, so that
    // AdjustChildren is specialized for it and calls it directly (the S1 publish A/B: the publisher passed as itself, a class, made every
    // use an interface call and a warm publish about 2% slower).
    private struct RowUses(Binding binding, SyntaxTable table) : IHirUses
    {
        // The call of the node whose inputs AdjustChildren gives, by its CallPlan.Of ordinal; -1 outside its calls (FixAdjust).
        private int callOrdinal = -1;

        public void Call(int ordinal) => this.callOrdinal = ordinal;

        // A child an operation uses (a call's receiver or argument, a Case payload, a writer; SPEC 7.3, 10.2), as the operation adapts it.
        public readonly void Use(Koto? child, in BoundArgumentOperation operation)
        {
            if (child is not null)
            {
                this.FixAdjust(child, binding.ChildAdjust(child, operation, this.Claimed(child)), default);
            }
        }

        // A child its consumer acquires or uses, as that use and its own facts give it (ChildAdjust), with the capability its takes claimed.
        public readonly void Use(Koto? child, HirAdaptation adaptation, UseKind kind)
        {
            if (child is not null)
            {
                this.FixAdjust(child, binding.ChildAdjust(child, adaptation, kind, this.Claimed(child)), default);
            }
        }

        // Only the nodes of the module being published. At a call input the earlier call's Adjust (EarlierAdjust) is the input's plan-row
        // Adjust, the column keeping the later; at any other call input the row states none.
        public readonly void FixAdjust(Koto? child, HirAdjust adjust, HirPlace place)
        {
            if (this.Owns(child))
            {
                ref var column = ref table.Hir.Adjusts![child!.SyntaxId];
                if (this.callOrdinal >= 0)
                {
                    var earlier = EarlierAdjust(this.callOrdinal, column, adjust);
                    HirTables.PlanFor(child, earlier != default).Adjust = earlier;
                }

                column = adjust;
                if (place.Kind != HirPlaceKind.Unset)
                {
                    table.Hir.Places![child.SyntaxId] = place;
                }
            }
        }

        // Whether a node's takes claim it (Claimed), of the rows this record wrote.
        internal readonly bool Claimed(Koto node) => this.Owns(node) && binding.Claimed(node, HirTables.AdjustOf(node), HirTables.PlanOf(node).Adjust);

        // What a node's use needs (Binding.UseOf), of the rows this record wrote and its parent's operation.
        internal readonly PlaceUse UseOf(Koto node, Koto? parent, HirOp parentOp) => this.Owns(node)
            ? binding.UseOf(node, HirTables.AdjustOf(node), HirTables.PlanOf(node).Adjust, parent, parentOp)
            : binding.UseOf(node, default, default, parent, parentOp);

        // Whether a node is of the module being published.
        internal readonly bool Owns(Koto? node) => node is not null && ReferenceEquals(node.SyntaxOwner, table) && node.SyntaxId < table.Hir.Adjusts!.Length;
    }

    private sealed partial class HirPublisher
    {
        // SPEC 23.1.2 (DECISIONS R4-M109, R4-M88 Rule 2): an Error node has no Place and no value of its own to acquire or adapt, so wherever
        // a consumer wrote an acquisition or adaptation for it, in the column or in its plan row (the earlier of two calls), it is that
        // consumer's derived value, used as it is. An Error assignment target folds too; the builder derives the whole assignment and never
        // reads it.
        private void Derived(Koto node)
        {
            var derived = new HirAdjust(HirAcquire.Temporary, HirAdaptation.None);
            if (HirTables.AdjustOf(node) != default)
            {
                this.uses.FixAdjust(node, derived, default);
            }

            if (this.uses.Owns(node) && HirTables.PlanOf(node).Adjust != default)
            {
                HirTables.PlanFor(node, written: true).Adjust = derived;
            }
        }

        // Whether a node's use needs write or exclusive capability, which selects a user index's indexUniq (SPEC 4.7.1; DECISIONS BU-8): the
        // one place-use rule over the rows its takes wrote (UseOf, in some admitted case or in every one), or an exclusive use (the
        // parent's) reaches it as the receiver of a Field or an element, as a group's operand or as the reference, handle or pair layer a
        // follow selects through (SPEC 13.5.5, 15.6.2: past a shared layer only reads), unless that use is of a raw Place, whose capability
        // its path does not decide (SPEC 5.2).
        private bool ExclusiveUse(Koto node, Koto? parent, HirOp parentOp, bool exclusive)
            => this.uses.UseOf(node, parent, parentOp) is PlaceUse.ByCase or PlaceUse.Exclusive ||
            (exclusive && (parentOp is HirOp.Field or HirOp.Index or HirOp.Deref || parent is ParenthesizedKoto) && !ElementAccess.IsRawRooted(parent!) &&
                ReferenceEquals(Operand(parent), node));
    }
}
