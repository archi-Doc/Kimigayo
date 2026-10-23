// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The HIR publisher's Adjust column: how each child its parent's Op consumes is acquired or adapted (SPEC 3.5, 10.2, 15.1.5); a match's
// Subject and its Pattern bindings are Binding.Hir.Patterns'.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private sealed partial class HirPublisher
    {
        // The call of the node Consume takes inputs for, by its CallPlan.Of ordinal; -1 outside its calls (Write).
        private int callOrdinal = -1;

        // SPEC 13.6.1: an object upcast to a view (objref, objuniq) of an operand of another Semantics borrows the operand in the view's mode.
        private static HirAdaptation ViewBorrow(Koto node) => node is ConversionKoto { ConversionBinding: ConversionBinding.ObjectUpcast, BoundType: { IsObjectBorrow: true } view } upcast &&
            upcast.Left.BoundType?.Semantics != view.Semantics ? view.Semantics == SemanticsKind.ObjUniq ? HirAdaptation.ExclusiveBorrow : HirAdaptation.SharedBorrow : HirAdaptation.None;

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
                case ParenthesizedKoto or MacroKoto or LabeledKoto when op == HirOp.Group: // Its group acquires it, an erasure recorded at it included.
                    var grouped = node is LabeledKoto label ? label.Target : ((UnaryKoto)node).Operand;
                    this.Write(grouped, HirTables.ErasedType(grouped) is null ? new(HirAcquire.None, HirAdaptation.None) : new(this.Bare(grouped), HirAdaptation.Erasure));
                    break;
                case ConversionKoto { ConversionBinding: ConversionBinding.Transfer } transfer:
                    this.Write(transfer.Left, new(HirAcquire.Move, HirAdaptation.None));
                    break;
                case ConversionKoto or DereferenceKoto or IsKoto: // A borrow, a followed referent and a runtime test take their operand in place.
                    this.Take(node is DereferenceKoto dereference ? dereference.Operand : ((BinaryKoto)node).Left, ViewBorrow(node), op is HirOp.Borrow or HirOp.Deref or HirOp.RuntimeTypeTest);
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
                        this.Write(loop.Bindings[i], new(HirAcquire.Copy, HirAdaptation.None)); // SPEC 4.6.4: a ResolvedRange loop binds isize positions.
                    }

                    this.Bindings(decomposition);
                    break;
                case MacroKoto { Formatting.Acquisition.BoundCall.ArgumentOperations: [var writer, ..] }:
                    this.Take(writer.Source, writer); // A $tryWrite acquires its writer operand (SPEC 22.10.4).
                    break;
            }

            // An enum Case's payloads, then each call of the node: a value call's callee in its receiver mode (SPEC 7.7.3), a reference to the
            // callable passed as it is in that mode. Of a user index's index and indexUniq, a use of the element that needs write or exclusive
            // capability (ExclusiveUse) takes indexUniq (SPEC 4.7.1), which its plan row records for its ExpansionPlan.
            if (op == HirOp.Aggregate && binding.TryGetEnumConstruction(node, out var construction))
            {
                foreach (ref readonly var payload in construction!.PayloadOperations)
                {
                    this.Take(payload.Source, payload);
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
                this.callOrdinal = i;
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
                    this.Take(call.Kind == CalleeKind.Value ? call.ReceiverOperation.Source : call.Receiver, call.ReceiverOperation);
                }

                foreach (ref readonly var argument in call.ArgumentOperations)
                {
                    this.Take(argument.Source, argument);
                }
            }

            this.callOrdinal = -1;
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

        // A body or operand that supplies its construct's value (SPEC 14.9).
        private void Result(Koto? source) => this.Take(source is not null && KotoHelper.IsValueContext(source) ? source : null);

        // A child an operation takes (a call's receiver or argument, a Case payload, a writer; SPEC 7.3, 10.2), as the operation adapts it and
        // with the capability its target needs (Exclusive).
        private void Take(Koto? child, in BoundArgumentOperation operation)
        {
            if (this.Exclusive(operation.AdaptedType ?? operation.ParameterType) && this.Owns(child))
            {
                this.claims[child!.SyntaxId] = true;
            }

            this.Take(child, HirAdjust.Of(operation));
        }

        // A consumed child: an adaptation Binding recorded at the node itself (at its fixed expected Type, or as a receiver; an erasure to its
        // Function Type, SPEC 7.7.4) wins over its consumer's operation, except Binding's in-place read of a selection (a shared borrow of the
        // child's own stored Type, ElementAccess.IsSynthesizedAccess), which a consumer needing exclusive capability of the child (an exclusive
        // borrow, or an operation whose target needs it) overrides (SPEC 4.7.1, 7.3). A Place adapted or used in place is not acquired, an
        // adapted value is a Temporary, and an unadapted one taken by value, or erased, is acquired by the bare rule. A built-in operator's
        // operand that is not Copy, a comparison's, is inspected through a shared Loan from its evaluation instead (SPEC 13.4): a Place in
        // place, a value other than a literal in its temporary.
        private void Take(Koto? child, HirAdaptation adaptation = HirAdaptation.None, bool inPlace = false, bool inspect = false)
        {
            if (child is not null)
            {
                var recordedWins = binding.adaptations.TryGetValue(child, out var recorded) && !(ElementAccess.IsSynthesizedAccess(child) &&
                    (adaptation == HirAdaptation.ExclusiveBorrow || (this.Owns(child) && this.claims[child.SyntaxId])));
                adaptation = recordedWins ? HirAdjust.Of(recorded, child.BoundType)
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

        // A bare Place (SPEC 3.5), a raw Place (SPEC 5.2), a static, or the storage a formatting plan prepared (its buffer, its writer).
        private bool IsPlace(Koto child) => IsBarePlace(child) || ElementAccess.IsRawPlace(child) || child is FormattingKoto { Operation: FormattingOperation.Storage } ||
            this.ResOf(KotoHelper.UnwrapParentheses(child)) == HirRes.Static;

        // Only the nodes of the module being published; a call shared across modules names nodes of another. An exclusive borrow, and the
        // Reborrow or pair-case acquisition of a Place whose Type needs exclusive capability (SPEC 10.2, 4.7.2, 8.14), is an exclusive use.
        // SPEC 13.7.2, 11.3 (DECISIONS R4-M109): where a later call of the consumer takes an input by another Adjust than the earlier one did
        // (a compound update's setter after its getter), the earlier one is the input's plan-row Adjust, the column keeping the later;
        // at any other call input the row states none, which also clears a row of an earlier pass.
        private void Write(Koto? node, HirAdjust adjust, HirPlace place = default)
        {
            if (this.Owns(node))
            {
                ref var column = ref this.table!.Hir.Adjusts![node!.SyntaxId];
                if (this.callOrdinal >= 0)
                {
                    var earlier = this.callOrdinal > 0 && column != adjust ? column : default;
                    HirTables.PlanFor(node, earlier != default).Adjust = earlier;
                }

                column = adjust;
                this.claims[node.SyntaxId] |= adjust.Adaptation == HirAdaptation.ExclusiveBorrow ||
                    (adjust.Acquire is HirAcquire.Reborrow or HirAcquire.ByCase && this.Exclusive(node.BoundType));
                if (place.Kind != HirPlaceKind.Unset)
                {
                    this.table.Hir.Places![node.SyntaxId] = place;
                }
            }
        }

        // Whether a node is of the module being published.
        private bool Owns(Koto? node) => node is not null && ReferenceEquals(node.SyntaxOwner, this.table) && node.SyntaxId < this.table!.Hir.Adjusts!.Length;
    }
}
