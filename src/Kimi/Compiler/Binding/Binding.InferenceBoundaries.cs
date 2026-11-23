// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // The callee's own Type slot a part of a Type names itself, -1 for none: directly, as the selector of an `s/U` application or
    // adaptation, or as the target projection of a pair slot (the one owner of the own-slot kinds).
    private static int OwnSlot(BoundType type, Koto function)
        => type.Kind is BoundTypeKind.Parameter or BoundTypeKind.SemanticsApplication or BoundTypeKind.SemanticsAdaptation or BoundTypeKind.TargetProjection &&
            type.Symbol is { } symbol ? ContainerSlot(function, symbol) : -1;

    // Whether `type` names one of the callee's slots in `slots`: a Type slot directly, as the selector of an `s/U` application or as the
    // target projection of a pair slot, or a length slot in a fixed array's length.
    private static bool MentionsSlots(BoundType type, Koto function, SlotSet slots)
    {
        if (slots.IsEmpty)
        {
            return false;
        }

        if (OwnSlot(type, function) is >= 0 and var slot && slots.Contains(slot))
        {
            return true;
        }

        if (type.LengthExpression is { } length && MentionsLength(length, function, slots))
        {
            return true;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (MentionsSlots(type.Components[i], function, slots))
            {
                return true;
            }
        }

        return false;
    }

    // Whether a length names one of the callee's length slots in `slots`.
    private static bool MentionsLength(BoundLength length, Koto function, SlotSet slots)
        => length.Parameter is { } parameter ? ContainerSlot(function, parameter) is >= 0 and var slot && slots.Contains(slot)
            : (length.Left is { } left && MentionsLength(left, function, slots)) || (length.Right is { } right && MentionsLength(right, function, slots));

    // The callee's Type and length slots that are still unbound (SPEC 10.8: a slot is either).
    private static SlotSet UnboundSlots(FunctionKoto function, ReadOnlySpan<BoundType?> arguments, ReadOnlySpan<BoundLength?> lengths, ref SlotSet slots)
    {
        slots.Clear();
        for (var g = 0; g < CallOwnSlots(function).Count; g++)
        {
            if (CallOwnSlots(function)[g] is LengthParameterKoto ? lengths[g] is null : arguments[g] is null)
            {
                slots.Add(g);
            }
        }

        return slots;
    }

    // SPEC 10.5, 10.8: an unbound slot's record (FailUnboundSlots at a call, FailUnfixedReference at a reference): the slot's Type parameter
    // (role 0), shown as the declaration with its name as the evidence, and the waiting argument whose fixed expected call signature holds
    // it (role 1).
    private void ReportUnboundTypeArgument(Koto node, DiagnosticCollection? store, int index, in BindingProblem problem)
    {
        var facts = this.report.Facts(index);
        var slot = (TypeKoto)this.compilation.SyntaxOf(facts[0].Site);
        (string Role, Koto At, string? Label)[] related = facts is [_, { Kind: ProblemFactKind.Related, Value: 1 } argument, ..]
            ? [("declaration", slot, null), ("argument", this.compilation.SyntaxOf(argument.Site), null)]
            : [("declaration", slot, null)];
        node.ReportIn(store, problem.Requirement, problem.Code, evidence: [slot.Identifier], related: related, condition: problem.Condition);
    }

    // SPEC 10.8: the required structural and length slots that no evidence binds, other than an F that a waiting argument binds (SPEC
    // 10.5).
    private void UnsolvedSlots(InvocationKoto call, FunctionKoto function, ReadOnlySpan<BoundType?> arguments, ReadOnlySpan<BoundLength?> lengths, ReadOnlySpan<int> mapping, ref SlotSet unsolved)
    {
        UnboundSlots(function, arguments, lengths, ref unsolved);
        var waiting = new WaitingSlots(ref unsolved, CallSlotOwner(function));
        for (var a = 0; a < call.ArgumentNodes.Count; a++)
        {
            if (function.Parameters[mapping[a]].Type.BoundType is { } pattern)
            {
                this.TakeWaiting(call.ArgumentNodes[a], pattern, CallSlotOwner(function), waiting);
            }
        }
    }

    // Whether the waiting parts of an argument bind every slot of its parameter Type that no evidence binds (SPEC 10.5, 10.8), so the
    // position is completed with them after selection instead of left open. `rest` is scratch.
    private bool WaitingPartsBind(Koto part, BoundType pattern, FunctionKoto function, SlotSet unbound, ref SlotSet rest)
    {
        rest.Clear();
        for (var g = 0; g < CallOwnSlots(function).Count; g++)
        {
            if (unbound.Contains(g))
            {
                rest.Add(g);
            }
        }

        this.TakeWaiting(part, pattern, CallSlotOwner(function), new WaitingSlots(ref rest, CallSlotOwner(function)));
        return !MentionsSlots(pattern, CallSlotOwner(function), rest);
    }

    // The fixed expected call signature of a parameter (SPEC 10.5): the Callable signature of its F, `ref/F` or `uniq/F`, or its common
    // Function Type; null otherwise.
    private BoundType? ExpectedCallSignature(FunctionKoto function, BoundType pattern)
    {
        if (CallableSlotType(pattern, CallSlotOwner(function)) is { } slotType)
        {
            return this.TryCallable(slotType, this.ConstraintScope(function), out var required, out _) ? required : null;
        }

        return pattern.Kind == BoundTypeKind.Function ? pattern : null;
    }

    // An open position stays open only while its parameter Type still holds an unsolved slot; a later literal default may have closed it,
    // and then the position was never fitted, so the candidate stays pending.
    private bool OpenPositionsHold(in SlotSolution solution, SlotSet open, SlotSet openSignatures, SlotSet unsolved)
    {
        var call = solution.Call;
        var function = solution.Function;
        for (var i = 0; i < call.ArgumentNodes.Count; i++)
        {
            if (!open.Contains(i) && !openSignatures.Contains(i))
            {
                continue;
            }

            var pattern = function.Parameters[solution.Mapping[i]].Type.BoundType!;
            var signature = open.Contains(i) ? pattern : this.ExpectedCallSignature(function, pattern);
            if (signature is null || !MentionsSlots(signature, CallSlotOwner(function), unsolved) ||
                solution.Type(signature) is not null)
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 23.1.2, 10.8 (Unbound slots; DECISIONS U1.6h-1): the unsolved slots at whose position a part's own binding failed
    // (PartFailed: a nested call the defaults resolved alone awaits no more, EndAwait) rest on that part and leave `unsolved`, by
    // the positions of [start, end) (a join's sources, an aggregate's elements: ChildPositions); the slots left are the call's own
    // boundary. Returns such a part, null when there is none; `scratch` is empty before and after.
    private Koto? RestSlots(int start, BoundType pattern, FunctionKoto selected, ref SlotSet unsolved, ref SlotSet scratch)
    {
        var end = this.positionParts.Count;
        if (!MentionsSlots(pattern, CallSlotOwner(selected), unsolved))
        {
            return null;
        }

        for (var i = start; i < end; i++)
        {
            if (!this.PartFailed(this.positionParts[i]))
            {
                continue;
            }

            for (var g = 0; g < CallSlotCount(selected); g++)
            {
                scratch.Add(g);
                if (unsolved.Contains(g) && MentionsSlots(pattern, CallSlotOwner(selected), scratch))
                {
                    unsolved.Remove(g);
                }

                scratch.Remove(g);
            }

            return this.positionParts[i];
        }

        // A whole that a bare slot takes keeps the slot at each of its places (SlotSolution.DefaultChildren likewise).
        Koto? failed = null;
        for (var i = 0; i < this.ChildPositions(start, end); i++)
        {
            var child = this.positionParts.Count;
            var element = this.AddChildPosition(start, end, i, pattern) ?? pattern;
            failed = this.RestSlots(child, element, selected, ref unsolved, ref scratch) ?? failed;
            this.ClosePosition(child);
        }

        return failed;
    }

    // SPEC 10.6, 10.8: the selected candidate leaves a required structural slot that neither explicit Type arguments nor evidence bind. The
    // call is UnboundTypeArgument_Kd, whose record relates the first such slot in declaration order (its Type parameter, role 0) and the
    // waiting argument whose fixed expected call signature holds it (role 1); the checks that need the slot are derived. An anonymous
    // argument whose fixed expected call signature has closed parameter Types is still checked with them, its result inferred from its
    // body, so that its independent problems stay visible; one with open parameter Types, and every other argument that the open slot
    // leaves without a Type, rests on the call. Where every unsolved slot stands at a position with a part whose own
    // binding failed (RestSlots), the call rests on such a part instead (SPEC 23.1.2; DECISIONS U1.6h-1): its record is that part's.
    // Otherwise the record names the first slot that does not rest (`boundary`): a slot at a failed part's position is no independent fact.
    // The record is the selection's (RecordSelection), so a call that already failed keeps its first failure.
    private BoundType? FailUnboundSlots(in SlotSolution solution, ReadOnlySpan<BoundArgumentOperation> operations)
    {
        var (call, selected, scope, self, declaringType) = (solution.Call, solution.Function, solution.Scope, solution.Self, solution.DeclaringType);
        var slots = solution.Types;
        var lengths = solution.Lengths;
        var mapping = solution.Mapping;
        var firstSlot = this.RentSlotSet(CallSlotCount(selected));
        var boundary = this.RentSlotSet(CallSlotCount(selected));
        try
        {
            this.UnsolvedSlots(call, selected, slots, lengths, mapping, ref boundary);
            Koto? failed = null;
            for (var i = 0; i < call.ArgumentNodes.Count; i++)
            {
                var position = this.OpenPosition(call.ArgumentNodes[i]);
                failed = this.RestSlots(position, selected.Parameters[mapping[i]].Type.BoundType!, selected, ref boundary, ref firstSlot) ?? failed;
                this.ClosePosition(position);
            }

            if (failed is not null && boundary.IsEmpty)
            {
                return this.CompleteDependent(call, failed);
            }

            var first = boundary.First();
            firstSlot.Add(first);
            Koto? waiting = null;
            var contract = this.activeRequirementContract;
            this.activeRequirementContract = null; // The arguments are expressions of the caller's context.
            try
            {
                for (var i = 0; i < call.ArgumentNodes.Count; i++)
                {
                    var argument = call.ArgumentNodes[i];
                    var inner = KotoHelper.UnwrapParentheses(argument);
                    var pattern = selected.Parameters[mapping[i]].Type.BoundType!;
                    var kind = this.KindOf(inner);
                    var signature = this.IsWaitingCallable(inner) ? this.ExpectedCallSignature(selected, pattern) : null;
                    var holds = signature is { Kind: BoundTypeKind.Function, Components.Count: 2 } && MentionsSlots(signature, CallSlotOwner(selected), firstSlot);
                    if (holds && waiting is null)
                    {
                        waiting = argument;
                    }

                    if (kind == PartKind.Function && this.AwaitsCall(inner) && inner is FunctionKoto closure)
                    {
                        var parameters = signature is { Kind: BoundTypeKind.Function, Components.Count: 2 } ? this.CallType(signature.Components[0], selected, slots, scope, self, solution.Origins, solution.Inputs, declaringType, lengths) : null;
                        var result = parameters is null ? null : this.CallType(signature!.Components[1], selected, slots, scope, self, solution.Origins, solution.Inputs, declaringType, lengths);
                        if (parameters is not null)
                        {
                            // SPEC 10.5: the closed parts of S guide the body; its inferred result is never evidence for the slot.
                            this.BindClosureArgument(argument, closure, scope, this.types.Intern(BoundTypeKind.Function, null, SemanticsKind.Owner, [parameters, result ?? BoundType.Unit], binder: signature!.Binder), new(result is null, 0));
                        }
                        else
                        {
                            this.MarkOmittedHeaders(closure, call);
                        }

                        continue;
                    }

                    if (kind != PartKind.Reference && argument.BoundType is null && operations[i].ParameterType is null)
                    {
                        // SPEC 23.1.2: an argument at an open position, such as `.None` at Option<T>, has no Type without the slot.
                        this.CompleteDependent(argument, call);
                    }
                }
            }
            finally
            {
                this.activeRequirementContract = contract;
            }

            this.candidateFacts.Add(new(ProblemFactKind.Related, this.FactSite(CallOwnSlots(selected)[first]), 0));
            if (waiting is not null)
            {
                this.candidateFacts.Add(new(ProblemFactKind.Related, this.FactSite(waiting), 1));
            }

            return this.RecordSelection(call, BindingFailure.UnboundTypeArgument);
        }
        finally
        {
            this.ReturnSlotSet(boundary);
            this.ReturnSlotSet(firstSlot);
        }
    }

    // SPEC 23.1.2: omitted header Types need the selection's expectation; they rest on the call without the body being checked, and an
    // independent written-Type error stays its own record.
    private void MarkOmittedHeaders(FunctionKoto closure, Koto cause)
    {
        for (var i = 0; i < closure.Parameters.Count; i++)
        {
            var parameter = closure.Parameters[i];
            if (parameter.Type is SyntaxFormKoto { Akind: KotoKind.InferredType } inferred)
            {
                this.CompleteDependent(inferred, cause);
            }
        }
    }
}
