// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// SPEC 10.5, 10.5.1, 10.5.4 (DECISIONS BU-2): the waiting-binds-F rule. A waiting callable at a position F, `ref/F` or `uniq/F`, F a slot
// of the slot owner, binds F: F takes no evidence from it, and it is completed after the other evidence with the call signature S of F's
// Callable Constraint, or without S when F has none. A position is an argument's or a payload's, or that of a Tuple or array literal
// element within one. TakeWaiting is the one walk that finds such callables, through the forms CallablePart defines, which also decide
// where an anonymous function among a call's join results waits for its call (CallableTarget; DECISIONS BU-43), and CallableSlotType
// the one test of F; they read a pattern and a slot owner only, so the queries of a call (UnsolvedSlots, WaitingPartsBind) and of a Case
// construction (UnsolvedPayloadSlots) and the completions over a SlotSolution share them. Whether such an F is fixed with no outer
// expectation, so that a nested call's or construction's result is typed without one, is FixesWithoutExpectation, read through
// OpenWaitingSlots by both (DECISIONS BU-2, D4). The part path through the same forms is here too: PartCore, down to the part a form
// delivers, the one key of a part's recorded kind (Binding.ArgumentKinds.cs); PartParent and PartHolder, up toward its argument, the
// walk by which a part finds its argument probe (UnderArgumentProbe) and the result target its call completes it at (CallableTarget).
public sealed partial class Binding
{
    // A consumer of TakeWaiting: it takes each waiting callable at its position and slot F, and stops the walk by returning false.
    private interface IWaitingTaker
    {
        bool Take(Koto part, BoundType position, BoundType slot);
    }

    // The slot F of a position `F`, `ref/F` or `uniq/F` of the owner; null for any other position.
    private static BoundType? CallableSlotType(BoundType pattern, Koto owner)
    {
        var slot = pattern is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components: [var target] }
            ? target : pattern;
        return slot.Kind == BoundTypeKind.Parameter && ContainerSlot(owner, slot.Symbol!) >= 0 ? slot : null;
    }

    // SPEC 10.5.1, 12.4.3 (DECISIONS BU-43; PLAN G924 rule A, BU-43 C1 deleted): the parts of an aggregate at whose own positions its call
    // completes a waiting callable in it, each classified as an argument there: a Tuple or array literal's elements, a Dictionary
    // literal's keys and values; -1 for any other form. The one definition of the forms TakeWaiting, the wait (CallableTarget) and the
    // bound-since test pass: CallablePart gives part `index` and the child position it is at (ExpectedChild's index).
    private static int CallablePartCount(Koto node) => node switch
    {
        TupleLiteralKoto tuple => tuple.Elements.Count,
        ArrayLiteralKoto array => array.Elements.Count,
        DictionaryLiteralKoto dictionary => dictionary.Entries.Count * 2,
        _ => -1,
    };

    private static Koto CallablePart(Koto node, int index, out int child)
    {
        child = node is DictionaryLiteralKoto ? index % 2 : index;
        return node switch
        {
            TupleLiteralKoto tuple => tuple.Elements[index],
            ArrayLiteralKoto array => array.Elements[index],
            DictionaryLiteralKoto dictionary => child == 0 ? dictionary.Entries[index / 2].Key : dictionary.Entries[index / 2].Value,
            _ => throw new InvalidOperationException("A callable part belongs to a Tuple, array or Dictionary literal (InternalInvariant)."),
        };
    }

    // The result target at whose position its call completes a waiting callable: the first result target on the part path (PartParent).
    private static Koto? CallableTarget(Koto node)
    {
        var source = node;
        while (PartHolder(source) is { } holder)
        {
            source = holder;
        }

        return ResultTarget(source);
    }

    // The part path, down (SPEC 10.5.1, 14.4, 14.9.1): the part a form delivers, through parentheses, a label and an expression body
    // (DeliveredOperand), the one key of a part's recorded kind.
    private static Koto PartCore(Koto node)
    {
        while (DeliveredOperand(node) is { } operand)
        {
            node = operand;
        }

        return node;
    }

    // The part path, up: the next node from a part toward its argument, the form that delivers it or the Tuple or array literal holding
    // it (PartHolder), else the result target it is a source of (ResultTarget: a selection body, a transfer's operand, a `do` body).
    private static Koto? PartParent(Koto node) => PartHolder(node) ?? ResultTarget(node);

    // The form delivering the part or the aggregate whose part it is (CallablePartCount), or null.
    private static Koto? PartHolder(Koto node)
        => node.Parent is { } parent && (ReferenceEquals(DeliveredOperand(parent), node) || CallablePartCount(parent) >= 0) ? parent : null;

    // Each waiting callable of the part at an F of the owner, in source order: the part itself, a part of its aggregate at that part's
    // position (CallablePart, ExpectedChild), or a result of a control-flow part its call classifies at the part's own position
    // (ComposedJoin; DECISIONS BU-43). False when the taker stops.
    private bool TakeWaiting<T>(Koto part, BoundType pattern, Koto owner, in T taker)
        where T : IWaitingTaker, allows ref struct
    {
        if (this.IsWaitingCallable(part))
        {
            return CallableSlotType(pattern, owner) is not { } slot || taker.Take(part, pattern, slot);
        }

        if (this.ComposedJoin(KotoHelper.UnwrapParentheses(part)) is { } join)
        {
            var start = this.positionParts.Count;
            this.AddResults(join);
            var taken = true;
            for (var i = start; taken && i < this.positionParts.Count; i++)
            {
                taken = this.TakeWaiting(this.positionParts[i], pattern, owner, taker);
            }

            this.ClosePosition(start);
            return taken;
        }

        var aggregate = KotoHelper.UnwrapParentheses(part);
        for (int i = 0, count = CallablePartCount(aggregate); i < count; i++)
        {
            var element = CallablePart(aggregate, i, out var child);
            if (ExpectedChild(aggregate, child, pattern) is { } position && !this.TakeWaiting(element, position, owner, taker))
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 10.5.1, 10.5.2-10.5.4 (D4; owner decision 2026-10-18): whether a waiting callable fixes its slot F with no expectation from
    // outside its callee. It does when it needs no S: a function reference that does not need S (SPEC 10.5.3), or an anonymous function
    // whose header writes every parameter Type and its result Type; or when F's Callable Constraint gives S, the callee's own (SPEC
    // 10.5.2). Otherwise the outer expectation is what can fix F, so a nested callee whose result holds it waits for that expectation.
    private bool FixesWithoutExpectation(Koto callable, BoundType slot, Koto binder)
    {
        if (this.TryCallable(slot, this.ConstraintScope(binder), out _, out _))
        {
            return true;
        }

        if (KotoHelper.UnwrapParentheses(callable) is not FunctionKoto closure)
        {
            return IndependentFunctionItem(callable.BoundSymbol!, this.UnboundMemberReference(callable), this.ReferenceDeclaringType(callable));
        }

        for (var i = 0; i < closure.Parameters.Count; i++)
        {
            if (closure.Parameters[i].Type is SyntaxFormKoto { Akind: KotoKind.InferredType })
            {
                return false;
            }
        }

        return closure.ReturnType is not null;
    }

    // Removes from a slot set each slot F that a waiting callable binds.
    private readonly ref struct WaitingSlots : IWaitingTaker
    {
        private readonly ref SlotSet slots;
        private readonly Koto owner;

        internal WaitingSlots(ref SlotSet slots, Koto owner)
        {
            this.slots = ref slots;
            this.owner = owner;
        }

        public bool Take(Koto part, BoundType position, BoundType slot)
        {
            this.slots.Remove(ContainerSlot(this.owner, slot.Symbol!));
            return true;
        }
    }

    // Adds to a slot set each slot F of the solution that no evidence binds and that its waiting callable does not fix with no outer
    // expectation (FixesWithoutExpectation).
    private readonly ref struct OpenWaitingSlots : IWaitingTaker
    {
        private readonly ref SlotSet slots;
        private readonly SlotSolution solution;

        internal OpenWaitingSlots(ref SlotSet slots, in SlotSolution solution)
        {
            this.slots = ref slots;
            this.solution = solution;
        }

        public bool Take(Koto part, BoundType position, BoundType slot)
        {
            var index = ContainerSlot(this.solution.SlotOwner, slot.Symbol!);
            if (this.solution.Types[index] is null && !this.solution.Binding.FixesWithoutExpectation(part, slot, this.solution.Binder))
            {
                this.slots.Add(index);
            }

            return true;
        }
    }

    private ref partial struct SlotSolution : IWaitingTaker
    {
        // Completes a waiting callable at F with S, F's Callable signature under the solution, or without S when F has none.
        public readonly bool Take(Koto part, BoundType position, BoundType slot)
        {
            var binding = this.Binding;
            var signature = binding.TryCallable(slot, binding.ConstraintScope(this.Binder), out var required, out _) ? this.Type(required) : null;
            return binding.CompleteWaitingPart(part, position, this.SlotOwner, signature, this.Scope) is not null;
        }

        // The slot F of a parameter, element or payload position of the solution; null for any other position.
        internal readonly BoundType? CallableSlot(BoundType pattern) => CallableSlotType(pattern, this.SlotOwner);
    }
}
