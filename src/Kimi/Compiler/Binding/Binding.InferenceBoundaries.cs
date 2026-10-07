// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// SPEC 10.6, 10.8: the inference-boundary record of a selected call: the declaration, the first unsolved slot, the waiting argument whose
// fixed expected call signature holds it, and the Note and Advice formed from the evidence the call lacks.
internal readonly record struct UnboundSlotFact(FunctionKoto Declaration, string Slot, Koto? Argument, string Note, string Advice);

public sealed partial class Binding
{
    // SPEC 10.5, 10.6: the Advice of a function reference that no fixed expected call signature or explicit Type argument binds.
    private const string UnboundReferenceAdvice = "Write explicit Type arguments, as in identity<i32>, or annotate the expected Function Type, as in let f: (i32) -> i32 = identity";

    private const string UnboundReferenceNote = "A function reference without a fixed expected call signature binds its Type parameters only from explicit Type arguments (SPEC 10.5)";

    private Dictionary<Koto, UnboundSlotFact>? unboundSlots;

    // Whether `type` names one of the callee's Type slots in `slots`, directly or as the selector of an `s/U` application.
    private static bool MentionsSlots(BoundType type, Koto function, SlotSet slots, int depth = 0)
    {
        if (slots.IsEmpty || depth > 64)
        {
            return false;
        }

        if (type.Kind is BoundTypeKind.Parameter or BoundTypeKind.SemanticsApplication && type.Symbol is { } symbol &&
            ContainerSlot(function, symbol) is var slot && slots.Contains(slot))
        {
            return true;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (MentionsSlots(type.Components[i], function, slots, depth + 1))
            {
                return true;
            }
        }

        return false;
    }

    // The callee's Type slots that are still unbound.
    private static SlotSet UnboundTypeSlots(FunctionKoto function, BoundType?[] arguments, ref SlotSet slots)
    {
        slots.Clear();
        for (var g = 0; g < CallOwnSlots(function).Count; g++)
        {
            if (CallOwnSlots(function)[g] is GenericParameterKoto && arguments[g] is null)
            {
                slots.Add(g);
            }
        }

        return slots;
    }

    // SPEC 10.8: the required structural slots that no evidence binds, other than an F that a waiting argument binds (SPEC 10.5).
    // An unresolved length remains pending; structural slots have no fixed-width limit.
    private static void UnsolvedSlots(InvocationKoto call, FunctionKoto function, BoundType?[] arguments, BoundLength?[] lengths, int[] mapping, ref SlotSet unsolved, out bool unrepresentable)
    {
        unrepresentable = false;
        unsolved.Clear();
        for (var g = 0; g < CallOwnSlots(function).Count; g++)
        {
            if (CallOwnSlots(function)[g] is LengthParameterKoto)
            {
                unrepresentable |= lengths[g] is null;
                continue;
            }

            if (arguments[g] is not null || WaitingBindsSlot(call, function, mapping, g))
            {
                continue;
            }

            unsolved.Add(g);
        }
    }

    // Whether a waiting argument binds the slot as its F, `ref/F` or `uniq/F` (SPEC 10.5).
    private static bool WaitingBindsSlot(InvocationKoto call, FunctionKoto function, int[] mapping, int slot)
    {
        for (var a = 0; a < call.ArgumentNodes.Count; a++)
        {
            var pattern = function.Parameters[mapping[a]].Type.BoundType!;
            var slotType = pattern is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq } ? pattern.Components[0] : pattern;
            if (IsWaitingCallable(call.ArgumentNodes[a]) && slotType.Kind == BoundTypeKind.Parameter && ContainerSlot(CallSlotOwner(function), slotType.Symbol!) == slot)
            {
                return true;
            }
        }

        return false;
    }

    private static void ReportUnboundSlots(Koto node, UnboundSlotFact fact, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        (string Role, Koto At, string? Label)[] related = fact.Argument is { } argument
            ? [("declaration", fact.Declaration, null), ("argument", argument, null)]
            : [("declaration", fact.Declaration, null)];
        node.Report(requirement, code, evidence: [fact.Slot], related: related, note: fact.Note, advice: fact.Advice);
    }

    // The fixed expected call signature of a parameter (SPEC 10.5): the Callable signature of its F, `ref/F` or `uniq/F`, or its common
    // Function Type; null otherwise.
    private BoundType? ExpectedCallSignature(FunctionKoto function, BoundType pattern)
    {
        var slotType = pattern is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } ? pattern.Components[0] : pattern;
        if (slotType.Kind == BoundTypeKind.Parameter && ContainerSlot(CallSlotOwner(function), slotType.Symbol!) >= 0)
        {
            return this.TryCallable(slotType, this.ConstraintScope(function), out var required, out _) ? required : null;
        }

        return pattern.Kind == BoundTypeKind.Function ? pattern : null;
    }

    // An open position stays open only while its parameter Type still holds an unsolved slot; a later literal default may have closed it,
    // and then the position was never fitted, so the candidate stays pending.
    private bool OpenPositionsHold(InvocationKoto call, FunctionKoto function, BoundType?[] arguments, int[] mapping, SlotSet open, SlotSet openSignatures, SlotSet unsolved, BindingScope scope, BoundType? self, BoundOrigin[] origins, BoundOrigin[] inputs, BoundType? declaringType, BoundLength?[] lengths)
    {
        for (var i = 0; i < call.ArgumentNodes.Count; i++)
        {
            if (!open.Contains(i) && !openSignatures.Contains(i))
            {
                continue;
            }

            var pattern = function.Parameters[mapping[i]].Type.BoundType!;
            var signature = open.Contains(i) ? pattern : this.ExpectedCallSignature(function, pattern);
            if (signature is null || !MentionsSlots(signature, CallSlotOwner(function), unsolved) ||
                this.CallType(signature, function, arguments, scope, self, origins, inputs, declaringType, lengths) is not null)
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 10.6, 10.8: the selected candidate leaves a required structural slot that neither explicit Type arguments nor evidence bind. The
    // call is UnboundTypeArgument_Kd, whose Reason names the first such slot in declaration order; the checks that need the slot are
    // derived. An anonymous argument whose fixed expected call signature has closed parameter Types is still checked with them, its result
    // inferred from its body, so that its independent problems stay visible; one with open parameter Types, and every other argument that
    // the open slot leaves without a Type, rests on the call.
    private BoundType? FailUnboundSlots(InvocationKoto call, FunctionKoto selected, BindingScope scope, BoundType?[] slots, BoundLength?[] lengths, int[] mapping, ReadOnlySpan<BoundArgumentOperation> operations, BoundType? expected, BoundType? self, BoundOrigin[] origins, BoundOrigin[] inputs, BoundType? declaringType)
    {
        var unsolved = this.RentSlotSet(CallSlotCount(selected));
        UnsolvedSlots(call, selected, slots, lengths, mapping, ref unsolved, out _);
        var firstSlot = this.RentSlotSet(CallSlotCount(selected));
        try
        {
            var first = unsolved.First();
            firstSlot.Add(first);
            var name = CallOwnSlots(selected)[first].Identifier;
            Koto? waiting = null;
            string? headerParts = null;
            string? reference = null;
            var overloads = false;
            var contract = this.activeRequirementContract;
            this.activeRequirementContract = null; // The arguments are expressions of the caller's context.
            try
            {
                for (var i = 0; i < call.ArgumentNodes.Count; i++)
                {
                    var argument = call.ArgumentNodes[i];
                    var inner = KotoHelper.UnwrapParentheses(argument);
                    var pattern = selected.Parameters[mapping[i]].Type.BoundType!;
                    var signature = IsWaitingCallable(inner) ? this.ExpectedCallSignature(selected, pattern) : null;
                    var holds = signature is { Kind: BoundTypeKind.Function, Components.Count: 2 } && MentionsSlots(signature, CallSlotOwner(selected), firstSlot);
                    if (holds && waiting is null)
                    {
                        waiting = argument;
                    }

                    if (inner is FunctionKoto { IsAnonymous: true, BoundType: null } closure)
                    {
                        var parameters = signature is { Kind: BoundTypeKind.Function, Components.Count: 2 } ? this.CallType(signature.Components[0], selected, slots, scope, self, origins, inputs, declaringType, lengths) : null;
                        var result = parameters is null ? null : this.CallType(signature!.Components[1], selected, slots, scope, self, origins, inputs, declaringType, lengths);
                        if (holds && ReferenceEquals(waiting, argument))
                        {
                            var inParameters = MentionsSlots(signature!.Components[0], CallSlotOwner(selected), firstSlot);
                            var inResult = MentionsSlots(signature.Components[1], CallSlotOwner(selected), firstSlot);
                            headerParts = inParameters && inResult ? "parameter and result Types" : inParameters ? "parameter Types" : "result Type";
                        }

                        if (parameters is not null)
                        {
                            // SPEC 10.5: the closed parts of S guide the body; its inferred result is never evidence for the slot.
                            this.BindClosureArgument(argument, closure, scope, this.InternType(BoundTypeKind.Function, null, SemanticsKind.Owner, [parameters, result ?? BoundType.Unit], resultMode: signature!.ResultMode), openResult: result is null);
                        }
                        else
                        {
                            this.MarkOmittedHeaders(closure, call);
                        }

                        continue;
                    }

                    if (IsWaitingFunctionReference(inner))
                    {
                        if (holds && ReferenceEquals(waiting, argument))
                        {
                            var group = inner.BoundSymbol!;
                            overloads = group.Next is not null;
                            for (var candidate = group; candidate is not null && reference is null; candidate = candidate.Next)
                            {
                                if (candidate.Declaration is FunctionKoto { GenericArguments.Count: > 0 } generic)
                                {
                                    reference = $"{group.Name}<{string.Join(", ", Enumerable.Repeat("Type", generic.GenericArguments.Count))}>";
                                }
                            }
                        }

                        continue;
                    }

                    if (argument.BoundType is null && operations[i].ParameterType is null)
                    {
                        // SPEC 23.3.6.4: an argument at an open position, such as `.None` at Option<T>, has no Type without the slot.
                        this.CompleteDependent(argument, call);
                    }
                }
            }
            finally
            {
                this.activeRequirementContract = contract;
            }

            var clauses = new List<string>(3);
            if (waiting is not null)
            {
                if (headerParts is not null)
                {
                    clauses.Add($"write the anonymous function's {headerParts}");
                }
                else if (reference is not null)
                {
                    clauses.Add($"write explicit Type arguments for the function reference, as in {reference}");
                }
                else if (overloads)
                {
                    clauses.Add("bind the function reference to a local whose Function Type is written");
                }
            }
            else if (NameableSlots(selected, slots, unsolved))
            {
                clauses.Add(selected.IsConstructor ? "write explicit Type arguments for the construction target" : $"write explicit Type arguments for {selected.Name}");
            }

            if (expected is null && selected.BoundSymbol?.Type is { } resultPattern && MentionsSlots(resultPattern, CallSlotOwner(selected), firstSlot))
            {
                clauses.Add("annotate the Type of the call's result");
            }

            if (clauses.Count == 0)
            {
                clauses.Add($"give an argument a Type that binds {name}, such as through a local whose Type is written");
            }

            var advice = string.Join(", or ", clauses);
            advice = char.ToUpperInvariant(advice[0]) + advice[1..];
            var others = new List<string>();
            for (var g = first + 1; g < CallOwnSlots(selected).Count; g++)
            {
                if (unsolved.Contains(g))
                {
                    others.Add(CallOwnSlots(selected)[g].Identifier);
                }
            }

            var also = others.Count == 0 ? string.Empty : $"; {string.Join(" and ", others)} {(others.Count == 1 ? "is" : "are")} also unbound";
            var note = waiting is not null
                ? $"No explicit Type argument or evidence binds {name}; it appears in the fixed expected call signature of a waiting argument, which is never evidence for an outer slot{also} (SPEC 10.5, 10.8)"
                : $"No explicit Type argument or evidence binds {name}{also} (SPEC 10.8)";
            return this.FailExplained(ref this.unboundSlots, call, BindingFailure.UnboundTypeArgument, new UnboundSlotFact(selected, name, waiting, note, advice), true);

            // Explicit Type arguments are a complete list (SPEC 8.1): every slot must be writable, so a slot that a waiting argument binds or
            // that holds a Closure or Function Item Type leaves none to write.
            static bool NameableSlots(FunctionKoto function, BoundType?[] slots, SlotSet unsolved)
            {
                for (var g = 0; g < CallOwnSlots(function).Count; g++)
                {
                    if (CallOwnSlots(function)[g] is LengthParameterKoto)
                    {
                        continue;
                    }

                    if (slots[g] is { } bound ? Unnameable(bound, 0) : !unsolved.Contains(g))
                    {
                        return false;
                    }
                }

                return true;
            }

            static bool Unnameable(BoundType type, int depth)
            {
                if (type.Kind is BoundTypeKind.Closure or BoundTypeKind.FunctionItem)
                {
                    return true;
                }

                for (var i = 0; i < type.Components.Count && depth < 64; i++)
                {
                    if (Unnameable(type.Components[i], depth + 1))
                    {
                        return true;
                    }
                }

                return false;
            }
        }
        finally
        {
            this.ReturnSlotSet(firstSlot);
            this.ReturnSlotSet(unsolved);
        }
    }

    // SPEC 23.3.6.4: omitted header Types need the selection's expectation; they rest on the call without the body being checked, and an
    // independent written-Type error stays its own record.
    private void MarkOmittedHeaders(FunctionKoto closure, Koto cause)
    {
        foreach (var parameter in closure.Parameters)
        {
            if (parameter.Type is SyntaxFormKoto { Akind: KotoKind.InferredType } inferred)
            {
                this.CompleteDependent(inferred, cause);
            }
        }
    }
}
