// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// A Closure's call receiver (SPEC 7.7.3, 7.7.2): the minimum receiver a call of the Closure needs, classified from the uses of its
// captures in its bound body.
// - Responsibility: ClosureEffects, one reusable visitor: a capture transferred out of the environment, or an outer environment
//   binding a nested Closure's entry moves, makes the call Consuming; a Place reached from a capture (the capture itself or a Place
//   its path reaches through NextPathStep's steps: a part of it, a captured referent, a raw pointer's referent) whose consuming operation
//   needs it exclusively (one predicate, NeedsExclusively: a write or update, an exclusive Borrow or object view upcast in some
//   admitted case, an exclusive argument or method or accessor receiver, an Exclusive-mode `for` or `match` Subject, an Exclusive
//   value call, an exclusive Reborrow at an expected Type or bare), or a nested entry that Reborrows or exclusively
//   borrows an outer environment binding, makes it Exclusive; every other use borrows (Shared). An acquisition of the binding itself
//   by value without an operator (ConsumedByValue: as the Adjust rule takes it from its consumer, a discarded Place included, or a
//   nested bare entry) that Reborrows in some admitted cases makes it Exclusive in exactly those (SPEC 8.14): the capture is marked
//   (BoundCapture.BareReborrows); every other use counts in every case (CaseFreeReceiver). A use of such a capture that no consumer
//   takes is published (Unlisted) where it could leave a case Shared. A use a failure explains (SPEC 23.1.2: its own, or its
//   consumer's failure or dependency outside a bare acquisition position) neither marks its capture nor is published.
// - Inputs: the Closure's bound body (each node's bound symbol, Type, call, value call and conversion), the adaptations Binding
//   recorded, a nested Closure's published Storage, a loop's and a match's Subject mode; the Adjust rule (AdjustChildren, Discarded,
//   Covers, OpOf), IsBareAcquisitionPosition, HasFailureCauses, BareReborrows, BorrowsExclusively and ProveCopy.
// - Outputs: BoundClosure's receiver facts: Receiver, the strongest over every case, which every reader of a Closure's call
//   receiver reads (value calls, candidate selection and Callable proofs, effects, the MIR builder and the old analysis);
//   CaseFreeReceiver and the captures' BareReborrows, from which a view of one case reads that case's receiver; Classified;
//   ExclusiveUse and ExclusiveReborrow, the Exclusive cause a write record names (Binding.Diagnostics.cs); Unlisted, which
//   BindConcreteClosure fails as the rule's net, a located Unsupported_Kd (the owner, 2026-10-25; DECISIONS R4-M145).
// - Invariants: Classify runs once per binding of a Closure that bound its body, right after it, from the facts BindClosure and
//   BindConcreteClosure reset (Shared, no mark, no cause, not Classified); only this binding's captures are classified; the
//   case-free receiver only strengthens, Receiver is derived from the facts once at the end and the first exclusive use while the
//   call is not Consuming is kept; a nested function's body is never visited, so one Closure is classified at a time.
// - Prohibitions: no record or failure; no other component classifies a Closure's receiver.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private ClosureEffects? closureEffects;

    // One reusable visitor classifies each closure's receiver from the uses of its captures (SPEC 7.7.3); it never visits a
    // nested function's body, so one closure is classified at a time.
    private sealed class ClosureEffects(Binding binding) : KotoVisitor
    {
        private FunctionKoto function = null!;
        private BoundClosure plan = null!;

        // The uses of a capture that Reborrows in some admitted case that no consumer takes (ConsumedByValue is null), with the capture's
        // index; after Classify, only those the receiver facts leave at Shared in a case (DECISIONS R4-M145).
        public List<(Koto Use, int Capture)> Unlisted { get; } = new();

        public void Classify(FunctionKoto function, BoundClosure plan)
        {
            this.function = function;
            this.plan = plan;
            this.Unlisted.Clear();
            if ((function.Body as Koto ?? function.ExpressionBody) is { } body)
            {
                this.Visit(body);
            }

            // SPEC 7.7.3: the strongest requirement over every admitted case.
            var receiver = plan.CaseFreeReceiver;
            for (var i = 0; receiver == SemanticsKind.Ref && i < plan.Storage.Count; i++)
            {
                receiver = plan.Storage[i].BareReborrows ? SemanticsKind.Uniq : receiver;
            }

            plan.Receiver = receiver;
            plan.Classified = true;

            // The rule's net (the owner, 2026-10-25): an untaken use matters only where its capture is unmarked and the case-free
            // receiver Shared, so a case may be Shared where SPEC 7.7.3 needs Exclusive.
            var kept = 0;
            for (var i = 0; i < this.Unlisted.Count; i++)
            {
                if (plan.CaseFreeReceiver == SemanticsKind.Ref && !plan.Storage[this.Unlisted[i].Capture].BareReborrows)
                {
                    this.Unlisted[kept++] = this.Unlisted[i];
                }
            }

            this.Unlisted.RemoveRange(kept, this.Unlisted.Count - kept);
        }

        public override void Visit(Koto node)
        {
            if (node is FunctionKoto nested)
            {
                if (nested.ClosureDefinition is { } child)
                {
                    this.VisitEntries(nested, child);
                }

                return;
            }

            if (node.BoundSymbol is { Kind: BindingSymbolKind.Capture } symbol && this.CaptureOf(symbol) is >= 0 and var at)
            {
                var use = node;
                while (use.Parent is ParenthesizedKoto ||
                    (use.Parent is IndexKoto index && ReferenceEquals(index.Left, use) && use.BoundType?.Kind == BoundTypeKind.FixedArray) ||
                    (use.Parent is MemberAccessKoto part && ReferenceEquals(part.Left, use) &&
                        (use.BoundType?.Kind == BoundTypeKind.Tuple || (AdtDef.IsStruct(use.BoundType) && part.BoundSymbol?.Property?.IsStored == true && !IsGetterResult(part)))))
                {
                    // Reading or updating a captured stored part obtains that part's authority, not a whole-value Move: the
                    // Consuming test reads the stored part a use takes.
                    use = use.Parent;
                }

                var need = this.UsesExclusively(node);
                if (need.Exclusive)
                {
                    if (need.ByCase)
                    {
                        this.plan.Storage[at] = this.plan.Storage[at] with { BareReborrows = true };
                    }
                    else if (this.plan.CaseFreeReceiver != SemanticsKind.Owner)
                    {
                        this.plan.CaseFreeReceiver = SemanticsKind.Uniq;
                    }

                    if (this.plan.CaseFreeReceiver != SemanticsKind.Owner && this.plan.ExclusiveUse is null)
                    {
                        this.plan.ExclusiveUse = node;
                        this.plan.ExclusiveReborrow = binding.adaptations.TryGetValue(use, out var adaptation) && adaptation.Kind == ExpectedAdaptationKind.Reborrow;
                    }
                }
                else if (use.Parent is ConversionKoto { ConversionBinding: ConversionBinding.Transfer } ||
                    (use.Parent is InvocationKoto { BoundValueCall: { ReceiverKind: SemanticsKind.Owner } called } && ReferenceEquals(called.CalleeValue, use)) ||
                    (this.ReceiverOperation(use) is { Kind: ArgumentOperationKind.Value } && use.BoundType is { } receiverType &&
                        binding.ProveCopy(receiverType, this.function) == ConstraintProof.Refuted))
                {
                    // SPEC 7.7.3, 13.5.3: transferring a capture out of the environment makes the call Consuming. A bare Non-Copy
                    // Place is never moved, so every other use, such as `items.length`, a shared element read, a bare `for` or
                    // `match` Subject or a borrowed argument, only borrows the capture; ownership rejects a Move out of a capture
                    // in a Shared or Exclusive call. A Copy payload Field read lends the captured handle; it does not consume that
                    // handle (SPEC 3.4.3).
                    this.plan.CaseFreeReceiver = SemanticsKind.Owner;
                }

                if (need.ByValue is null)
                {
                    this.Unlisted.Add((node, at)); // No consumer takes this use, so no rule decides whether it Reborrows by case.
                }
            }

            node.VisitChildren(this);
        }

        // The operation by which a call takes the Place as an argument, or null.
        private static BoundArgumentOperation? ArgumentOf(ReadOnlySpan<BoundArgumentOperation> arguments, Koto place)
        {
            foreach (ref readonly var argument in arguments)
            {
                if (ReferenceEquals(argument.Source, place))
                {
                    return argument;
                }
            }

            return null;
        }

        // Whether an operation lends its source to an exclusive parameter (SPEC 15.1.5): a Borrow, Reborrow, base borrow or payload
        // projection in an exclusive mode.
        private static bool LendsExclusively(in BoundArgumentOperation operation)
            => operation.Kind is ArgumentOperationKind.Borrow or ArgumentOperationKind.Reborrow or ArgumentOperationKind.BaseBorrow or ArgumentOperationKind.PayloadProjection &&
                operation.ParameterType is { } parameter && IsExclusive(parameter.Semantics);

        // SPEC 7.7.2, 7.7.3: a nested Closure's entries of this binding's captures: moving an outer environment value is Consuming;
        // Reborrowing it or borrowing its slot exclusively needs exclusive access to the outer environment, in the cases where it Reborrows
        // for a bare entry.
        private void VisitEntries(FunctionKoto nested, BoundClosure child)
        {
            // Indexed over the storage list: enumerating the read-only interface boxes its enumerator on every pass.
            for (var i = 0; i < child.Storage.Count; i++)
            {
                var capture = child.Storage[i];
                var acquisition = capture.Environment.CaptureAcquisition;
                if (this.CaptureOf(capture.Source) is not (>= 0 and var outer))
                {
                    continue;
                }

                if (acquisition == CaptureAcquisition.Move)
                {
                    this.plan.CaseFreeReceiver = SemanticsKind.Owner;
                    continue;
                }

                if (acquisition is not (CaptureAcquisition.Reborrow or CaptureAcquisition.ExclusiveSlotBorrow) &&
                    !(acquisition == CaptureAcquisition.Bare && capture.Source.Type is { } captured && binding.BareReborrows(captured, nested)))
                {
                    continue;
                }

                if (acquisition == CaptureAcquisition.Bare)
                {
                    this.plan.Storage[outer] = this.plan.Storage[outer] with { BareReborrows = true };
                }
                else if (this.plan.CaseFreeReceiver != SemanticsKind.Owner)
                {
                    this.plan.CaseFreeReceiver = SemanticsKind.Uniq;
                }

                if (this.plan.CaseFreeReceiver != SemanticsKind.Owner)
                {
                    this.plan.ExclusiveUse ??= nested;
                }
            }
        }

        // SPEC 7.7.3, 7.7.2: whether the body uses a Place reached from a capture (the capture itself or a Place its path reaches, one
        // NextPathStep at a time: a part of it through Fields or elements, a captured referent through a follow, a raw pointer's
        // referent, `*p` as `p[0]`, SPEC 5.2) exclusively. Such a body mutates the environment or a captured referent, so the call is
        // Exclusive, by case only where that use is an acquisition of the capture itself by value that Reborrows in some admitted cases,
        // which the capture's case Type tells. Such a Reborrow of a Place reached through the capture takes that Place's cases, which
        // the capture's do not tell, so it counts in every case: the strongest receiver (PLAN G13). The need's ByValue is the capture's
        // own (NeedsExclusively).
        private Need UsesExclusively(Koto capture)
        {
            bool? byValue = false;
            for (var place = capture; ; place = place.Parent!)
            {
                var need = this.NeedsExclusively(place);
                byValue = ReferenceEquals(place, capture) ? need.ByValue : byValue;
                if (need.Exclusive)
                {
                    return new(true, need.ByCase && ReferenceEquals(KotoHelper.UnwrapParentheses(place), capture), byValue);
                }

                _ = NextPathStep(place.Parent!, out var next);
                if (!(IsGroup(place.Parent) || ReferenceEquals(next, place)))
                {
                    return new(false, false, byValue);
                }
            }
        }

        // Whether the operation that consumes a Place needs it exclusively (SPEC 7.7.3, 3.5.1): it writes or updates the Place,
        // borrows it exclusively (an explicit adaptation as Binding.BorrowsExclusively decides it, SPEC 13.5.6), lends it to an
        // exclusive parameter as an argument or a method or accessor receiver, takes it as a `for` or `match` Subject in Exclusive mode
        // (SPEC 14.6.2, 15.1.6), calls it as an Exclusive callee, or acquires it by a Reborrow in an exclusive mode, at an expected Type
        // or bare; by case when only an acquisition by value of a pair layer that Reborrows in some admitted cases needs it (SPEC 8.14),
        // which ConsumedByValue decides for such a Place.
        private Need NeedsExclusively(Koto place)
        {
            var exclusive = place.Parent switch
            {
                UnaryKoto update => update.Akind is KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus or KotoKind.PostfixIncrement or KotoKind.PostfixDecrement,
                ConversionKoto conversion => ReferenceEquals(conversion.Left, place) && binding.BorrowsExclusively(conversion),
                ForKoto loop => ReferenceEquals(loop.Iterable, place) && loop.Mode == SubjectMode.Exclusive,
                MatchKoto match => ReferenceEquals(match.Expression, place) && BoundMatch.Of(match) is { Mode: SubjectMode.Exclusive },
                InvocationKoto { BoundValueCall: { } valueCall } => ReferenceEquals(valueCall.CalleeValue, place) && valueCall.ReceiverKind == SemanticsKind.Uniq,
                InvocationKoto { BoundCall: { } call } => ArgumentOf(call.ArgumentOperations, place) is { } argument && LendsExclusively(argument),
                MemberAccessKoto => this.ReceiverOperation(place) is { } receiver && LendsExclusively(receiver),
                BinaryKoto assignment => assignment.Akind is >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals && ReferenceEquals(assignment.Left, place),
                _ => false,
            };
            if (exclusive || (binding.adaptations.TryGetValue(place, out var adaptation) && adaptation.Kind == ExpectedAdaptationKind.Reborrow && IsExclusive(adaptation.Type.Semantics)))
            {
                return new(true, false, false);
            }

            // SPEC 3.5, 8.14: a Place acquired by value without an operator (ConsumedByValue) is acquired as each case's Type is.
            var byValue = place.BoundType is { } bare && binding.BareReborrows(bare, place) ? this.ConsumedByValue(place) : false;
            return new(byValue == true, byValue == true, byValue);
        }

        // SPEC 3.5, 8.14, 14.2.2, 23.1.2 (DECISIONS R4-M145): whether a Place is acquired by value (the bare rule) as the Adjust rule takes
        // it, its groups taken through: false where its own failure explains it; in Discard Context, as its container discards it
        // (Binding.Discarded, the plan-row Adjust the MIR builder reads); else as its consumer takes it, the nearest ancestor its parent
        // does not cover (Binding.Covers), the Closure under classification taken as a Closure. A consumer whose failure or dependency
        // explains its inputs (HasFailureCauses) decides only a position that acquires by bare acquisition by itself where no adaptation was
        // recorded (IsBareAcquisitionPosition); another runs the rule with the finder (ConsumedTakes) under both capabilities of its own use:
        // null where it takes no such child (the net) or a user index's two selections take it differently (SPEC 4.7.1).
        private bool? ConsumedByValue(Koto place)
        {
            var target = Grouped(place);
            if (Rejected(place))
            {
                return false;
            }

            if (!KotoHelper.IsValueContext(target))
            {
                return ConsumedTakes.ByValue(binding.Discarded(place));
            }

            var consumer = target.Parent!;
            while (consumer.Parent is { } outer && Covers(outer, consumer))
            {
                consumer = outer;
            }

            var closure = ReferenceEquals(consumer, this.function);
            if (!closure && binding.HasFailureCauses(consumer))
            {
                return IsBareAcquisitionPosition(target) && !binding.adaptations.ContainsKey(target);
            }

            var op = closure ? HirOp.Closure : binding.OpOf(consumer);
            if (op is HirOp.Unset or HirOp.Error or HirOp.Syntax)
            {
                return null; // The HIR publisher's walk takes no child of such a consumer.
            }

            var shared = new ConsumedTakes(binding, target, exclusive: false);
            binding.AdjustChildren(consumer, op, ref shared);
            var exclusive = new ConsumedTakes(binding, target, exclusive: true);
            binding.AdjustChildren(consumer, op, ref exclusive);
            return shared.Result == exclusive.Result ? shared.Result : null;
        }

        // The operation by which the method or accessor call whose receiver a Place is takes it (SPEC 7.3.3, 11.3), or null.
        private BoundArgumentOperation? ReceiverOperation(Koto place)
        {
            var call = place.Parent is MemberAccessKoto { Parent: InvocationKoto { BoundCall: { } selected } } member &&
                ReferenceEquals(member.Left, place) && ReferenceEquals(selected.Receiver, place) ? selected :
                place.Parent is MemberAccessKoto property && ReferenceEquals(property.Left, place)
                    ? (binding.PropertyCall(property, PropertyAccessorKind.Set) ?? binding.PropertyCall(property, PropertyAccessorKind.Get))?.BoundCall : null;
            if (call is null || call.ReceiverOperation.Source is not null)
            {
                return call?.ReceiverOperation;
            }

            // Accessor calls pass their receiver as an explicit argument; ordinary methods retain it separately.
            foreach (var argument in call.ArgumentOperations)
            {
                if (ReferenceEquals(argument.Source, place))
                {
                    return argument;
                }
            }

            return call.ReceiverOperation;
        }

        // The index of the capture of this binding whose environment binding a use names, or -1 (SPEC 7.7.3: the resolved operations of
        // the body this binding bound).
        private int CaptureOf(BindingSymbol environment)
        {
            for (var i = 0; i < this.plan.Storage.Count; i++)
            {
                if (ReferenceEquals(this.plan.Storage[i].Environment, environment))
                {
                    return i;
                }
            }

            return -1;
        }

        // What a use of a Place needs of the environment (SPEC 7.7.3, 8.14): exclusive access, or only in the admitted cases where its
        // acquisition by value Reborrows (ByCase); and whether the Place is acquired by value (ByValue, ConsumedByValue: null where no
        // consumer takes it, false where its Type Reborrows in no case or its operation needs it exclusively in every case).
        private readonly record struct Need(bool Exclusive, bool ByCase, bool? ByValue);

        // One child's takes by its consumer, as the Adjust rule gives them and the HIR publisher writes them: the last, the column's, and
        // the earlier call's that a later one replaces by another (EarlierAdjust, the plan row's), each completed by the claim the
        // consumer's uses of the child made so far (Claims, R4-M142's `claimed`), under a given capability of the consumer's own use.
        private struct ConsumedTakes(Binding binding, Koto child, bool exclusive) : IHirUses
        {
            private HirAdjust last;
            private HirAdjust earlier;
            private int call = -1;
            private bool claimed;
            private bool found;

            // Whether either take acquires the child by value; null where the consumer takes no such child.
            public readonly bool? Result => this.found ? ByValue(this.last) || ByValue(this.earlier) : null;

            public readonly bool UseExclusive => exclusive;

            // An acquisition by the bare rule (SPEC 3.5): Copied, moved, Reborrowed or by case, erased or not.
            public static bool ByValue(HirAdjust adjust) => adjust is
            {
                Adaptation: HirAdaptation.None or HirAdaptation.Erasure,
                Acquire: HirAcquire.Copy or HirAcquire.Move or HirAcquire.Reborrow or HirAcquire.ByCase,
            };

            public void Call(int ordinal) => this.call = ordinal;

            public void Use(Koto? taken, HirAdaptation adaptation, UseKind kind)
            {
                if (ReferenceEquals(taken, child))
                {
                    this.FixAdjust(taken, binding.ChildAdjust(child, adaptation, kind, this.claimed), default);
                }
            }

            public void Use(Koto? taken, in BoundArgumentOperation operation)
            {
                if (ReferenceEquals(taken, child))
                {
                    this.claimed |= binding.Claims(operation);
                    this.Use(taken, HirAdjust.Of(operation), UseKind.Acquisition);
                }
            }

            public void FixAdjust(Koto? taken, HirAdjust adjust, HirPlace place)
            {
                if (ReferenceEquals(taken, child))
                {
                    this.earlier = this.call >= 0 ? EarlierAdjust(this.call, this.last, adjust) : this.earlier;
                    (this.last, this.found) = (adjust, true);
                    this.claimed |= binding.Claims(child, adjust);
                }
            }
        }
    }
}
