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
//   by value without an operator (at a value position its consumer takes it by value, Binding.AcquiredByValue's list, or a nested bare
//   entry) that Reborrows in some admitted cases makes it Exclusive in exactly those (SPEC 8.14): the capture is marked
//   (BoundCapture.BareReborrows); every other use counts in every case (CaseFreeReceiver). A use of such a capture whose consumer the
//   list does not decide is published (Unlisted) where it could leave a case Shared.
// - Inputs: the Closure's bound body (each node's bound symbol, Type, call, value call and conversion), the adaptations Binding
//   recorded, a nested Closure's published Storage, a loop's and a match's Subject mode; AcquiredByValue, ReceiverOperation,
//   BareReborrows, BorrowsExclusively and ProveCopy.
// - Outputs: BoundClosure's receiver facts: Receiver, the strongest over every case, which every reader of a Closure's call
//   receiver reads (value calls, candidate selection and Callable proofs, effects, the MIR builder, the old analysis and Emission);
//   CaseFreeReceiver and the captures' BareReborrows, from which a view of one case reads that case's receiver; Classified;
//   ExclusiveUse and ExclusiveReborrow, the Exclusive cause a write record names (Binding.Diagnostics.cs); Unlisted, which
//   BindConcreteClosure fails as the list's net, a located Unsupported_Kd (the owner, 2026-10-25; DECISIONS R4-M128).
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

        // The uses of a capture that Reborrows in some admitted case whose consumer's acquisition AcquiredByValue's list does not decide,
        // with the capture's index; after Classify, only those the receiver facts leave at Shared in a case (DECISIONS R4-M128).
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

            // The list's net (the owner, 2026-10-25): an unlisted use matters only where its capture is unmarked and the case-free
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
                if (nested.BoundClosure is { } child)
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

                if (this.UsesExclusively(node, out var byCase))
                {
                    if (byCase)
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
                    (binding.ReceiverOperation(use) is { Kind: ArgumentOperationKind.Value } && use.BoundType is { } receiverType &&
                        binding.ProveCopy(receiverType, this.function) == ConstraintProof.Refuted))
                {
                    // SPEC 7.7.3, 13.5.3: transferring a capture out of the environment makes the call Consuming. A bare Non-Copy
                    // Place is never moved, so every other use, such as `items.length`, a shared element read, a bare `for` or
                    // `match` Subject or a borrowed argument, only borrows the capture; ownership rejects a Move out of a capture
                    // in a Shared or Exclusive call. A Copy payload Field read lends the captured handle; it does not consume that
                    // handle (SPEC 3.4.3).
                    this.plan.CaseFreeReceiver = SemanticsKind.Owner;
                }

                if (node.BoundType is { } captured && binding.BareReborrows(captured, node) && binding.AcquiredByValue(node) is null && !FailedConsumer(node))
                {
                    this.Unlisted.Add((node, at)); // The list does not decide whether this use Reborrows by case.
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

        // Whether the consumer of a use's value (past its parentheses) failed: its own record stands, and the net adds none (SPEC 23.1.2).
        private static bool FailedConsumer(Koto use)
        {
            while (use.Parent is ParenthesizedKoto parenthesized)
            {
                use = parenthesized;
            }

            return use.Parent is { BindingFailure: not BindingFailure.None };
        }

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
        // Exclusive; `byCase` when that use is an acquisition of the capture itself by value that Reborrows in some admitted cases,
        // which the capture's case Type tells. Such a Reborrow of a Place reached through the capture takes that Place's cases, which
        // the capture's do not tell, so it counts in every case: the strongest receiver (PLAN G13).
        private bool UsesExclusively(Koto capture, out bool byCase)
        {
            for (var place = capture; ; place = place.Parent!)
            {
                if (this.NeedsExclusively(place, out byCase))
                {
                    byCase &= ReferenceEquals(KotoHelper.UnwrapParentheses(place), capture);
                    return true;
                }

                _ = NextPathStep(place.Parent!, out var next);
                if (!(place.Parent is ParenthesizedKoto || ReferenceEquals(next, place)))
                {
                    return false;
                }
            }
        }

        // Whether the operation that consumes a Place needs it exclusively (SPEC 7.7.3, 3.5.1): it writes or updates the Place,
        // borrows it exclusively (an explicit adaptation as Binding.BorrowsExclusively decides it, SPEC 13.5.6), lends it to an
        // exclusive parameter as an argument or a method or accessor receiver, takes it as a `for` or `match` Subject in Exclusive mode
        // (SPEC 14.6.2, 15.1.6), calls it as an Exclusive callee, or acquires it by a Reborrow in an exclusive mode, at an expected Type
        // or bare; `byCase` when only an acquisition by value of a pair layer that Reborrows in some admitted cases needs it (SPEC 8.14).
        private bool NeedsExclusively(Koto place, out bool byCase)
        {
            var exclusive = place.Parent switch
            {
                UnaryKoto update => update.Akind is KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus or KotoKind.PostfixIncrement or KotoKind.PostfixDecrement,
                ConversionKoto conversion => ReferenceEquals(conversion.Left, place) && binding.BorrowsExclusively(conversion),
                ForKoto loop => ReferenceEquals(loop.Iterable, place) && loop.Mode == SubjectMode.Exclusive,
                MatchKoto match => ReferenceEquals(match.Expression, place) && BoundMatch.Of(match) is { Mode: SubjectMode.Exclusive },
                InvocationKoto { BoundValueCall: { } valueCall } => ReferenceEquals(valueCall.CalleeValue, place) && valueCall.ReceiverKind == SemanticsKind.Uniq,
                InvocationKoto { BoundCall: { } call } => ArgumentOf(call.ArgumentOperations, place) is { } argument && LendsExclusively(argument),
                MemberAccessKoto => binding.ReceiverOperation(place) is { } receiver && LendsExclusively(receiver),
                BinaryKoto assignment => assignment.Akind is >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals && ReferenceEquals(assignment.Left, place),
                _ => false,
            };
            if (exclusive || (binding.adaptations.TryGetValue(place, out var adaptation) && adaptation.Kind == ExpectedAdaptationKind.Reborrow && IsExclusive(adaptation.Type.Semantics)))
            {
                byCase = false;
                return true;
            }

            // SPEC 3.5, 8.14: a Place its consumer acquires by value without an operator (Binding.AcquiredByValue) is acquired as each
            // case's Type is.
            byCase = binding.AcquiredByValue(place) == true && place.BoundType is { } bare && binding.BareReborrows(bare, place);
            return byCase;
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
    }
}
