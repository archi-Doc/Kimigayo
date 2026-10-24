// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// A Closure's call receiver (SPEC 7.7.3, 7.7.2): the minimum receiver a call of the Closure needs, classified from the uses of its
// captures in its bound body.
// - Responsibility: ClosureEffects, one reusable visitor: a capture transferred out of the environment, or an outer environment
//   binding a nested Closure's entry moves, makes the call Consuming; a capture or a Place reached from it through Fields, elements
//   or a follow that is written, incremented, Reborrowed or borrowed exclusively or used as an exclusive receiver or argument, a bare
//   acquisition of a capture that Reborrows, or a nested entry that Reborrows or exclusively borrows an outer environment binding
//   makes it Exclusive; every other use borrows (Shared).
// - Inputs: the Closure's bound body (each node's bound symbol, Type, call, value call and conversion), the adaptations Binding
//   recorded, a nested Closure's published Storage; BareReborrows, PropertyCall and ProveCopy.
// - Outputs: BoundClosure.Receiver, which every reader of a Closure's call receiver reads (value calls, candidate selection and
//   Callable proofs, effects, the MIR builder, the old analysis and Emission); ExclusiveUse and ExclusiveReborrow, the Exclusive
//   cause a write record names (Binding.Diagnostics.cs).
// - Invariants: Classify runs at most once per binding of a Closure, after its body is bound, from the facts BindConcreteClosure
//   resets (Shared, no cause); the receiver only strengthens and the first exclusive use is kept; a nested function's body is never
//   visited, so one Closure is classified at a time.
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

        public void Classify(FunctionKoto function, BoundClosure plan)
        {
            this.function = function;
            this.plan = plan;
            (function.Body as Koto ?? function.ExpressionBody)?.VisitChildren(this);
            if (function.ExpressionBody is { } bodyExpression)
            {
                this.Visit(bodyExpression);
            }
        }

        public override void Visit(Koto node)
        {
            if (node is FunctionKoto nested)
            {
                if (nested.BoundClosure is { } child)
                {
                    // Indexed over the storage list: enumerating the read-only interface boxes its enumerator on every pass.
                    for (var i = 0; i < child.Storage.Count; i++)
                    {
                        var capture = child.Storage[i];
                        if (!ReferenceEquals(capture.Source.Declaration, this.function))
                        {
                            continue;
                        }

                        // SPEC 7.7.2, 7.7.3: moving an outer environment value is Consuming; Reborrowing it or borrowing its slot
                        // exclusively needs exclusive access to the outer environment.
                        if (capture.Environment.CaptureAcquisition == CaptureAcquisition.Move)
                        {
                            this.plan.Receiver = SemanticsKind.Owner;
                        }
                        else if ((capture.Environment.CaptureAcquisition is CaptureAcquisition.Reborrow or CaptureAcquisition.ExclusiveSlotBorrow ||
                            (capture.Environment.CaptureAcquisition == CaptureAcquisition.Bare && capture.Source.Type is { } captured && binding.BareReborrows(captured, nested))) &&
                            this.plan.Receiver != SemanticsKind.Owner)
                        {
                            this.plan.Receiver = SemanticsKind.Uniq;
                            this.plan.ExclusiveUse ??= nested;
                        }
                    }
                }

                return;
            }

            if (node.BoundSymbol is { Kind: BindingSymbolKind.Capture } symbol && ReferenceEquals(symbol.Declaration, this.function))
            {
                var use = node;
                while (use.Parent is ParenthesizedKoto ||
                    (use.Parent is IndexKoto index && ReferenceEquals(index.Left, use) && use.BoundType?.Kind == BoundTypeKind.FixedArray) ||
                    (use.Parent is MemberAccessKoto part && ReferenceEquals(part.Left, use) &&
                        (use.BoundType?.Kind == BoundTypeKind.Tuple || (AdtDef.IsStruct(use.BoundType) && part.BoundSymbol?.Property?.IsStored == true && !IsGetterResult(part)))))
                {
                    // Reading or updating a captured stored part obtains that part's authority, not a whole-value Move.
                    use = use.Parent;
                }

                var valueCall = use.Parent as InvocationKoto;
                var called = valueCall?.BoundValueCall;
                var receiver = called is not null && ReferenceEquals(called.CalleeValue, use);
                var memberCall = use.Parent is MemberAccessKoto { Parent: InvocationKoto { BoundCall: { } selected } } member &&
                    ReferenceEquals(member.Left, use) && ReferenceEquals(selected.Receiver, use) ? selected :
                    use.Parent is MemberAccessKoto property && ReferenceEquals(property.Left, use)
                        ? (binding.PropertyCall(property, PropertyAccessorKind.Set) ?? binding.PropertyCall(property, PropertyAccessorKind.Get))?.BoundCall : null;
                var memberOperation = memberCall?.ReceiverOperation ?? default;
                if (memberCall is not null && memberOperation.Source is null)
                {
                    // Accessor calls pass their receiver as an explicit argument; ordinary methods retain it separately.
                    foreach (var argument in memberCall.ArgumentOperations)
                    {
                        if (ReferenceEquals(argument.Source, use))
                        {
                            memberOperation = argument;
                            break;
                        }
                    }
                }

                var memberBorrow = memberOperation.Kind is ArgumentOperationKind.Borrow or ArgumentOperationKind.Reborrow or ArgumentOperationKind.PayloadProjection;
                // A Copy payload Field read lends the captured handle; it does not consume that handle (SPEC 3.4.3).
                if ((use.Parent is BinaryKoto assignment && assignment.Akind is >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals && ReferenceEquals(assignment.Left, use)) ||
                    use.Parent is UnaryKoto { Akind: KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus or KotoKind.PostfixIncrement or KotoKind.PostfixDecrement } ||
                    (receiver && called!.ReceiverKind == SemanticsKind.Uniq) || (memberBorrow && memberOperation.ParameterType?.Semantics == SemanticsKind.Uniq) ||
                    this.UsesReferentExclusively(use) ||
                    (ReferenceEquals(use, node) && IsBareAcquisitionPosition(node) && node.BoundType is { } bare && binding.BareReborrows(bare, node)))
                {
                    if (this.plan.Receiver != SemanticsKind.Owner)
                    {
                        this.plan.Receiver = SemanticsKind.Uniq;
                        if (this.plan.ExclusiveUse is null)
                        {
                            this.plan.ExclusiveUse = node;
                            this.plan.ExclusiveReborrow = binding.adaptations.TryGetValue(use, out var adaptation) && adaptation.Kind == ExpectedAdaptationKind.Reborrow;
                        }
                    }
                }
                else if (use.Parent is ConversionKoto { ConversionBinding: ConversionBinding.Transfer } ||
                    (receiver && called!.ReceiverKind == SemanticsKind.Owner) || (memberCall is not null && memberOperation.Kind == ArgumentOperationKind.Value && !memberBorrow &&
                        use.BoundType is { } receiverType && binding.ProveCopy(receiverType, this.function) == ConstraintProof.Refuted))
                {
                    // SPEC 7.7.3, 13.5.3: transferring a capture out of the environment makes the call Consuming. A bare Non-Copy
                    // Place is never moved, so every other use, such as `items.length`, a shared element read, a bare `for` or
                    // `match` Subject or a borrowed argument, only borrows the capture; ownership rejects a Move out of a capture
                    // in a Shared or Exclusive call.
                    this.plan.Receiver = SemanticsKind.Owner;
                }
            }

            node.VisitChildren(this);
        }

        // SPEC 7.7.3: a capture used in a way that needs it, or the referent of a captured exclusive reference, exclusively:
        // Reborrowed exclusively (bare, at an expected uniq Type or as a uniq argument), or reached through Fields, elements or a
        // follow to a Place that is written, incremented, borrowed exclusively or used as an exclusive receiver. Such a body
        // mutates the environment or a captured referent, so the call is Exclusive.
        private bool UsesReferentExclusively(Koto use)
        {
            if (binding.adaptations.TryGetValue(use, out var adaptation) && adaptation.Kind == ExpectedAdaptationKind.Reborrow && adaptation.Type.Semantics == SemanticsKind.Uniq)
            {
                return true;
            }

            if (use.Parent is InvocationKoto { BoundCall: { } call })
            {
                foreach (var argument in call.ArgumentOperations)
                {
                    if (ReferenceEquals(argument.Source, use) && argument.Kind is ArgumentOperationKind.Reborrow or ArgumentOperationKind.Borrow &&
                        argument.ParameterType?.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq)
                    {
                        return true;
                    }
                }
            }

            var target = use;
            while (target.Parent is ParenthesizedKoto ||
                (target.Parent is ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PayloadFollow or ConversionBinding.PairFollow } selected && ReferenceEquals(selected.Left, target)) ||
                (target.Parent is MemberAccessKoto member && ReferenceEquals(member.Left, target)) ||
                (target.Parent is IndexKoto index && ReferenceEquals(index.Left, target)))
            {
                target = target.Parent;
            }

            if (ReferenceEquals(target, use))
            {
                return false;
            }

            return (target.Parent is BinaryKoto write && write.Akind is >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals && ReferenceEquals(write.Left, target)) ||
                target.Parent is UnaryKoto { Akind: KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus or KotoKind.PostfixIncrement or KotoKind.PostfixDecrement } ||
                target.Parent is ConversionKoto { ConversionBinding: ConversionBinding.Borrow, BoundType.Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq } ||
                (target.Parent is MemberAccessKoto { Parent: InvocationKoto { BoundCall: { } selectedCall } } receiverAccess && ReferenceEquals(receiverAccess.Left, target) &&
                    ReferenceEquals(selectedCall.Receiver, target) && selectedCall.ReceiverOperation.ParameterType?.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq) ||
                (target.Parent is InvocationKoto { BoundValueCall: { ReceiverKind: SemanticsKind.Uniq } valueCall } && ReferenceEquals(valueCall.CalleeValue, target));
        }
    }
}
