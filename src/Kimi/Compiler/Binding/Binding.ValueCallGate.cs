// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The old analysis's gate input (SPEC 15.6.4, 15.8.2; DECISIONS R4-M126 §2.13, R4-M152, R4-M159; removed with the old analysis at
// R6-check).
// - Responsibility: the calls the old analysis does not analyze, kept as a fact once Binding binds them: R1's condition, the call whose
//   result depends on its receiver, and F1's, the call that reads a maker's images (ReadsMakerImages; R4-M159, pending the owner's
//   approval of the wider D5 exception):
//   ResultFixedForCall, whether a value call's result that names none of the callee's per-call inputs is fixed for the call (each Origin
//   static, carried only by Copy captures of a Copy result, or, carried by no capture, a fixed Origin of a function enclosing the call:
//   FixedInBody, FixedInBodyOrigin); RecordReceiverDependence, the fact on the call's plan, overwritten by each binding of the call, and
//   the pass's list of the calls that set it (ReceiverDependentCalls), which OwnershipAnalysis's gate re-reads through each listed
//   call's current plan (F4). The list is the pass's that last appended to it (receiverDependentPass, a pass stamp), so it needs no
//   reset; a plan's flag says the list holds its call (CallPlan.ReceiverDependentListed), so a call is listed once per pass in O(1),
//   each flag cleared when the next pass starts the list afresh. The list is created with the first such call, and it keeps the last
//   listing pass's calls, and so their syntax, until a later pass lists one (no pass reset; transitional with the gate).
// - Inputs: the result Type, the callee expression (its Closure Type and that Type's definition's captures, BoundClosure.Of) and the
//   call.
// - Outputs: the answer, read by ValueCallResult; the fact and the list, read by the gate only.
// - Invariants: a meet is fixed when each operand is; a Non-Copy carrier is never fixed; a call is listed once per pass, in the order its
//   bindings first set the fact; an empty list costs nothing.
// - Prohibitions: no record, failure or substitution; no reader but ValueCallResult and the gate.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // The calls whose binding set ReceiverDependentResult in the pass receiverDependentPass (OwnershipAnalysis's gate re-reads each
    // one's current plan); null until the first such call.
    [Transitional("R6-check", Reason = "The old analysis's gate (DECISIONS R4-M152) goes with the old analysis.")]
    private List<InvocationKoto>? receiverDependentCalls;

    // The pass (storageVersion) whose calls receiverDependentCalls lists.
    [Transitional("R6-check", Reason = "The old analysis's gate (DECISIONS R4-M152) goes with the old analysis.")]
    private ulong receiverDependentPass;

    // This pass's receiver-dependent calls: none when the list is an earlier pass's.
    [Transitional("R6-check", Reason = "The old analysis's gate (DECISIONS R4-M152) goes with the old analysis.")]
    internal ReadOnlySpan<InvocationKoto> ReceiverDependentCalls
        => this.receiverDependentPass == this.storageVersion ? CollectionsMarshal.AsSpan(this.receiverDependentCalls) : default;

    // A result whose Origins are static or fixed Origins of a function that encloses the call: the signature's own written contract,
    // which the callee's body was checked against.
    private static bool FixedInBody(BoundType type, Koto use)
    {
        if (!FixedOrigin(type.Origin, use))
        {
            return false;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (!FixedOrigin(type.OriginArguments[i], use))
            {
                return false;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (!FixedInBody(type.Components[i], use))
            {
                return false;
            }
        }

        return true;

        static bool FixedOrigin(BoundOrigin? origin, Koto use)
        {
            if (origin is null || origin.Kind == OriginKind.Static)
            {
                return true;
            }

            if (origin.Kind == OriginKind.Intersection)
            {
                for (var i = 0; i < origin.Operands.Count; i++)
                {
                    if (!FixedOrigin(origin.Operands[i], use))
                    {
                        return false;
                    }
                }

                return true;
            }

            return FixedInBodyOrigin(origin, use);
        }
    }

    // A fixed Origin of a named function that encloses the use: its input or a parameter Origin of its signature.
    private static bool FixedInBodyOrigin(BoundOrigin origin, Koto use)
        => IsLocalRegion(origin) || (origin.Kind is OriginKind.Projection or OriginKind.Anchor && origin.Binder is not FunctionKoto) ||
            (origin.Kind is OriginKind.Input or OriginKind.Parameter && origin.Binder is FunctionKoto { IsAnonymous: false } owner && IsWithin(use, owner));

    // F1's condition (DECISIONS R4-M152, R4-M159): the call reads its maker's images, its callee's definition naming, as an Origin argument,
    // an atom of a Closure that does not enclose the call; the old analysis has no reading of those images (SupportsType).
    [Transitional("R6-check", Reason = "The old analysis's gate (DECISIONS R4-M152) goes with the old analysis.")]
    private static bool ReadsMakerImages(FunctionKoto closure, Koto call)
    {
        if (closure.ClosureDefinition?.EnvironmentType is not { } definition)
        {
            return false;
        }

        for (var i = 0; i < definition.OriginArguments.Count; i++)
        {
            if ((CallAtomsOf(definition.OriginArguments[i], closure, call) & CallAtoms.Foreign) != 0)
            {
                return true;
            }
        }

        return false;
    }

    // The gate's condition at a value call's binding: the fact on its plan, overwritten by each binding (a consultation's and a candidate
    // trial's included), and the call listed once per pass when the fact holds, the pass's first listing starting the list afresh.
    [Transitional("R6-check", Reason = "The old analysis's gate (DECISIONS R4-M152) goes with the old analysis.")]
    private void RecordReceiverDependence(InvocationKoto call, bool dependent)
    {
        var plan = call.CallStorage ??= new();
        plan.ReceiverDependentResult = dependent;
        if (!dependent)
        {
            return;
        }

        var calls = this.receiverDependentCalls ??= new();
        if (this.receiverDependentPass != this.storageVersion)
        {
            foreach (var listed in calls)
            {
                if (listed.CallStorage is { } old)
                {
                    old.ReceiverDependentListed = false; // An earlier pass's listing ends.
                }
            }

            calls.Clear();
            this.receiverDependentPass = this.storageVersion;
        }

        if (!plan.ReceiverDependentListed)
        {
            plan.ReceiverDependentListed = true;
            calls.Add(call);
        }
    }

    // SPEC 15.6.4, 15.8.2: a value call whose result names none of the callee's per-call inputs keeps that result as written when
    // it is independent of the call: each Origin is static, carried only by Copy captures of a Copy result (a Copy of a captured
    // shared reference keeps its external Origin), or, carried by no capture, a fixed Origin of a function enclosing the call. A Non-Copy
    // carrier would make the result depend on the call receiver, and equal lifetime names never prove which capture supplied the result.
    private bool ResultFixedForCall(BoundType result, Koto receiver, Koto use)
    {
        var type = receiver.BoundType!;
        type = type.Kind == BoundTypeKind.Semantics ? type.Components[0] : type;
        if (type.Kind != BoundTypeKind.Closure)
        {
            return FixedInBody(result, use);
        }

        if (BoundClosure.Of(type) is not { } closure)
        {
            return false;
        }

        var copy = this.ProveCopy(result, receiver) == ConstraintProof.Proven;
        return FixedType(result);

        bool FixedType(BoundType part)
        {
            if (!FixedOrigin(part.Origin))
            {
                return false;
            }

            for (var i = 0; i < part.OriginArguments.Count; i++)
            {
                if (!FixedOrigin(part.OriginArguments[i]))
                {
                    return false;
                }
            }

            for (var i = 0; i < part.Components.Count; i++)
            {
                if (!FixedType(part.Components[i]))
                {
                    return false;
                }
            }

            return true;
        }

        bool FixedOrigin(BoundOrigin? origin)
        {
            if (origin is null || origin.Kind == OriginKind.Static)
            {
                return true;
            }

            if (origin.Kind == OriginKind.Intersection)
            {
                for (var i = 0; i < origin.Operands.Count; i++)
                {
                    if (!FixedOrigin(origin.Operands[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            var carried = false;
            for (var i = 0; i < closure.Captures.Count; i++)
            {
                var captured = closure.Captures[i].Environment.Type!;
                if (ContainsOrigin(captured, origin))
                {
                    if (this.ProveCopy(captured, receiver) != ConstraintProof.Proven)
                    {
                        return false;
                    }

                    carried = true;
                }
            }

            return carried ? copy : FixedInBodyOrigin(origin, use);
        }
    }
}
