// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The HIR publisher's Adjust column: how each child its parent's Op consumes is acquired or adapted (SPEC 3.5, 10.2, 15.1.5).

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private sealed partial class HirPublisher
    {
        // SPEC 13.5.7: an object upcast to a view (objref, objuniq) of an operand of another Semantics borrows the operand in the view's mode.
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
                        this.Write(loop.Bindings[i], new(HirAcquire.Copy, HirAdaptation.None)); // SPEC 4.6.3.4: a ResolvedRange loop binds isize positions.
                    }

                    this.Bindings(decomposition);
                    break;
                case MacroKoto { Formatting.Acquisition.BoundCall.ArgumentOperations: [var writer, ..] }:
                    this.Take(writer.Source, HirAdjust.Of(writer)); // A $tryWrite acquires its writer operand (utf8-formatting 5.3).
                    break;
            }

            // An enum Case's payloads, then each call of the node: a value call's callee in its receiver mode (SPEC 7.6.3), a reference to the
            // callable passed as it is in that mode. Of a user index's index and indexUniq, an update or an exclusive borrow of the element
            // takes indexUniq (SPEC 4.6.9), which its plan row records for its ExpansionPlan.
            if (op == HirOp.Aggregate && binding.TryGetEnumConstruction(node, out var construction))
            {
                foreach (ref readonly var payload in construction!.PayloadOperations)
                {
                    this.Take(payload.Source, HirAdjust.Of(payload));
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
                    this.Take(call.Kind == CalleeKind.Value ? call.ReceiverOperation.Source : call.Receiver, HirAdjust.Of(call.ReceiverOperation));
                }

                foreach (ref readonly var argument in call.ArgumentOperations)
                {
                    this.Take(argument.Source, HirAdjust.Of(argument));
                }
            }
        }

        // A body or operand that supplies its construct's value (SPEC 14.9).
        private void Result(Koto? source) => this.Take(source is not null && KotoHelper.IsValueContext(source) ? source : null);

        // A consumed child: an adaptation Binding recorded at the node itself (at its fixed expected Type, or as a receiver; an erasure to its
        // Function Type, SPEC 7.6.4) wins over its consumer's operation. A Place adapted or used in place is not acquired, an adapted value is
        // a Temporary, and an unadapted one taken by value, or erased, is acquired by the bare rule. A built-in operator's operand that is not
        // Copy, a comparison's, is inspected through a shared Loan from its evaluation instead (SPEC 13.4): a Place in place, a value other
        // than a literal in its temporary.
        private void Take(Koto? child, HirAdaptation adaptation = HirAdaptation.None, bool inPlace = false, bool inspect = false)
        {
            if (child is not null)
            {
                adaptation = binding.adaptations.TryGetValue(child, out var recorded) ? HirAdjust.Of(recorded, child.BoundType)
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

        // Whether a node's consumer updates it or borrows it exclusively, directly or through the projections of a Place path, where the old
        // path selects a user index's indexUniq (SPEC 4.6.9); the parent is the node's while the node is visited.
        private bool ExclusiveUse(Koto node) => HirTables.AdjustOf(node).Adaptation == HirAdaptation.ExclusiveBorrow ||
            (this.parentOp == HirOp.Borrow && this.parent!.BoundType?.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq) ||
            ((this.parentOp is HirOp.Assign or HirOp.CompoundAssign || (this.exclusive && (this.parentOp is HirOp.Field or HirOp.Index || this.parent is ParenthesizedKoto))) &&
                ReferenceEquals(Operand(this.parent), node));

        // A bare Place (SPEC 3.5), a raw Place (SPEC 5.2), a static, or the storage a formatting plan prepared (its buffer, its writer).
        private bool IsPlace(Koto child) => IsBarePlace(child) || ElementAccess.IsRawPlace(child) || child is FormattingKoto { Operation: FormattingOperation.Storage } ||
            this.ResOf(KotoHelper.UnwrapParentheses(child)) == HirRes.Static;

        // SPEC 15.1.6: a Subject is acquired as written, but a bare Place is borrowed in place: its stored exclusive reference Reborrowed, a
        // stored exclusive reference reached through a shared layer read as one shared reference, and any other Place borrowed in its mode.
        private void Subject(Koto subject, BoundType? borrow)
        {
            if (borrow is null)
            {
                this.Take(subject);
                return;
            }

            var mode = borrow.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq ? HirAdaptation.ExclusiveBorrow
                : subject.BoundType is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq } ? HirAdaptation.ReferenceRead : HirAdaptation.SharedBorrow;
            this.Write(subject, ReferenceEquals(borrow, subject.BoundType) ? new(HirAcquire.Reborrow, HirAdaptation.None) : new(HirAcquire.None, mode));
        }

        // A Pattern binding its plan borrows, below a reference layer, designates the payload of its Case position or an element of its Tuple
        // position (SPEC 15.1.6); a binding of the whole Subject acquires it.
        private void Bindings(BoundMatch? plan)
        {
            for (var i = 0; plan is { IsCurrent: true } && i < plan.Positions.Count; i++)
            {
                if (plan.Positions[i] is { Kind: BoundPatternKind.Binding } position && HirAdjust.Of(position.Acquisition, position.AccessMode) is var adjust)
                {
                    var place = adjust.Acquire != HirAcquire.None || position.Parent < 0 ? HirPlaceKind.Unset
                        : plan.Positions[position.Parent].Kind == BoundPatternKind.Case ? HirPlaceKind.Downcast : HirPlaceKind.Field;
                    this.Write(position.Source, adjust, place);
                }
            }
        }

        // Only the nodes of the module being published; a call shared across modules names nodes of another.
        private void Write(Koto? node, HirAdjust adjust, HirPlaceKind place = HirPlaceKind.Unset)
        {
            if (node is not null && ReferenceEquals(node.SyntaxOwner, this.table) && this.table!.Hir.Adjusts is { } adjusts && node.SyntaxId < adjusts.Length)
            {
                adjusts[node.SyntaxId] = adjust;
                if (place != HirPlaceKind.Unset)
                {
                    this.table.Hir.Places![node.SyntaxId] = new(place, 0);
                }
            }
        }
    }
}
