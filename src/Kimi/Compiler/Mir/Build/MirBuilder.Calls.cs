// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Call assembly (SPEC 7.3): a node's call, its callee, receiver and arguments in their order, and its Call terminator.
internal sealed partial class MirBuilder
{
    // SPEC 7.3: the parameters a function's signature declares, an accessor's self and value included (TyCtxt.Signature's count; a slot
    // past them asks for no Type).
    private static int Arity(BindingSymbol function)
    {
        _ = TyCtxt.Signature(function, null, int.MaxValue, out var count);
        return count;
    }

    // A node's call (CallPlan.Of; a Property read's getter; a user or view index's selected call, SPEC 4.7.1); a constructor's builds its
    // object in the destination (SPEC 6.3.2); a comparison operator's is Compare's (SPEC 13.4, 13.4.1).
    private void Call(MirPlace destination, Koto node)
    {
        var call = (ExpansionPlan.Of(node) is { } plan ? plan.Second : CallPlan.Of(node))!;
        if (node is not BinaryKoto binary || BinaryOp(binary.Akind) is not (>= MirBinaryOp.Eq and MirBinaryOp relation))
        {
            this.Call(destination, call, node, first: null);
            return;
        }

        this.Compare(destination, call, node, relation);
    }

    // SPEC 7.3, 7.7.3: a call of the function, requirement, virtual slot or callable value Binding selected: a value call's callee in its
    // receiver mode, the receiver, then the arguments in their order (an assignment's secured value first, SPEC 13.7.1), each as its Adjust
    // takes it for its parameter's Type or as the value an expansion evaluated for it (given), each read where it is evaluated (SPEC 12.2:
    // secured before a later argument that builds code or that the operand order puts before it: a named one (SPEC 10.1.1), an unbound
    // reference's receiver supplied after earlier parameters (SPEC 7.3.4), a setter's receiver after its value (SPEC 13.7.1), A3 E13 (2);
    // or in its pending slot), then its omitted defaults (Defaults), passed by parameter slot, the receiver first. A compiler function is its
    // intrinsic where the table gives one (IntrinsicDisposition), naming its library declaration (A3 E14), with the operands it takes beyond
    // the written arguments (Extra), $abort's at its macro (SPEC 22.5.1), and a direct base call the implementation the virtual selection
    // names. A call of a Never function or a diverging intrinsic has no runtime successor and continues for the checks (SPEC 14.10); a call
    // whose operand does not complete enters no callee at run time (SPEC 15.6.7), but for the checks it continues where that operand's code
    // continues, its later operands, defaults and activation checked there (SPEC 14.10.3), each operand as built: a Never form's is the
    // Never value (DECISIONS R4-M62).
    private void Call(MirPlace destination, CallPlan call, Koto node, Koto? first, Koto? given = null, in MirOperand value = default)
    {
        var function = call.Kind == CalleeKind.Value ? null : call.Target;
        var receiver = function is null ? null : call.Receiver;
        var count = function is null ? call.Signature.Components[0].Components.Count : Arity(function);
        var direct = call is { Kind: CalleeKind.Virtual, VirtualIsDirect: true } ? HirTables.Symbol(call.VirtualImplementation!) : function;
        if (call.ArgumentOperations.Length + call.DefaultArguments.Length + (receiver is null ? 0 : 1) != count ||
            (receiver is not null && function!.ReceiverIndex < 0))
        {
            this.Unsupported(node, destination); // A plan whose operations do not cover the parameters (an expansion supplies them).
            return;
        }

        var source = call.ReceiverOperation.Source ?? call.CalleeValue;
        var pending = this.arguments.Count;
        var subst = compilation.Types.SubstOf(call);
        var after = this.leastAfter.Count;
        var last = this.Order(call, function, first, given);
        if (function is null)
        {
            // The callee value, pending while the arguments are evaluated.
            var operand = this.Operand(source, call.ReceiverOperation.AdaptedType, input: true);
            this.arguments.Add(last > -2 ? this.Secure(operand, source) : operand);
        }

        var start = this.arguments.Count;
        var defaults = !call.DefaultArguments.IsEmpty;
        CollectionsMarshal.SetCount(this.arguments, start + count);
        CollectionsMarshal.AsSpan(this.arguments)[start..].Clear(); // No stale operand reads as pending (Transfer).
        if (receiver is not null)
        {
            // A direct base call's receiver is its implementation's (the selection adapted it to the slot's declaring Type).
            var parameter = ReferenceEquals(direct, function) ? call.ReceiverOperation.ParameterType : TyCtxt.Signature(
                direct!,
                subst,
                direct!.ReceiverIndex,
                out _);
            var slot = defaults ? this.Slot(parameter, receiver) : (MirPlace?)null;
            var view = ReferenceEquals(direct, function) ? call.ReceiverOperation.AdaptedType ?? parameter : parameter;
            var operand = call is { Kind: CalleeKind.Virtual, VirtualIsDirect: false } ? this.Dispatched(receiver, view!, slot)
                : this.Operand(receiver, view, input: true, slot);
            operand = slot is null && last > -2 ? this.Secure(operand, receiver) : operand;
            this.arguments[start] = slot is { } place ? this.Pending(place, operand, receiver) : operand;
        }

        for (var pass = first is null ? 1 : 0; pass < 2; pass++)
        {
            for (var i = 0; i < call.ArgumentOperations.Length; i++)
            {
                ref readonly var argument = ref call.ArgumentOperations[i];
                if (ReferenceEquals(argument.Source, first) == (pass == 0))
                {
                    var slot = defaults ? this.Slot(argument.ParameterType, argument.Source!) : (MirPlace?)null;
                    var operand = ReferenceEquals(argument.Source, given) ? value : this.Operand(
                        argument.Source!,
                        argument.AdaptedType ?? argument.ParameterType,
                        input: true,
                        slot);
                    var (order, position) = (pass == 0 ? -1 : i, MirCallee.Position(function, argument.ParameterIndex));
                    operand = slot is null && (order < last || this.leastAfter[after + i] < position) ? this.Secure(operand, argument.Source!) : operand;
                    this.arguments[start + position] = slot is { } place ? this.Pending(place, operand, argument.Source!) : operand;
                }
            }
        }

        CollectionsMarshal.SetCount(this.leastAfter, after);

        if (defaults)
        {
            this.Defaults(call, function!, subst, start, node);
        }

        var disposition = function is null ? default : IntrinsicDisposition.Of(function.CompilerFunction);
        if (this.Extra(disposition) is { } extra)
        {
            this.arguments.Add(extra);
        }

        var declaration = function?.LibraryDeclaration is null ? null : function; // The compiler-only $abort has none (SPEC 17.4).
        var callee = disposition.Kind == DispositionKind.Intrinsic ? MirCallee.Intrinsic(disposition.Id, declaration, subst) : call.Kind switch
        {
            CalleeKind.Requirement => new(MirCalleeKind.Requirement, function, subst, Contract: call.RequirementContract),
            CalleeKind.Virtual when !call.VirtualIsDirect => new(MirCalleeKind.Virtual, function, subst),
            CalleeKind.Value => new(MirCalleeKind.Value, Arg: this.body.Const(new(MirConstKind.TypeToken, call.DeclaredSignature))), // C8.
            _ => new MirCallee(MirCalleeKind.Fn, direct, subst),
        };

        this.Invoke(
            destination,
            callee,
            start,
            node,
            ReferenceEquals(call.ReturnType, BoundType.Never) || MirDescriptors.Of(disposition.Id).Diverges,
            function is null ? this.arguments[pending] : default);
        this.arguments.RemoveRange(pending, start - pending);
    }

    // SPEC 12.2: the last argument of a call that builds code, in evaluation order (the receiver -2, first -1, the others in order), and,
    // appended to leastAfter by argument, the least position of the arguments evaluated after it.
    private int Order(CallPlan call, BindingSymbol? function, Koto? first, Koto? given)
    {
        var (last, after, least, earliest) = (-2, this.leastAfter.Count, int.MaxValue, -1);
        CollectionsMarshal.SetCount(this.leastAfter, after + call.ArgumentOperations.Length);
        for (var i = call.ArgumentOperations.Length - 1; i >= 0; i--)
        {
            ref readonly var argument = ref call.ArgumentOperations[i];
            if (ReferenceEquals(argument.Source, first))
            {
                earliest = i;
                continue;
            }

            (this.leastAfter[after + i], least) = (least, Math.Min(least, MirCallee.Position(function, argument.ParameterIndex)));
            last = last == -2 && !ReferenceEquals(argument.Source, given) && Builds(argument.Source!) ? i : last;
        }

        if (earliest >= 0)
        {
            this.leastAfter[after + earliest] = least;
            last = last == -2 && !ReferenceEquals(first, given) && Builds(first!) ? -1 : last;
        }

        return last;
    }

    // SPEC 6.4.5: a virtual receiver's borrow covers the whole payload, so the receiver is borrowed as the view of its payload's own Type
    // and that view upcast to the slot's (SPEC 13.6.1), the Loan on the payload rather than on its base subobject (DECISIONS R5-M49).
    private MirOperand Dispatched(Koto receiver, BoundType view, MirPlace? slot)
    {
        var payload = TypeOf(receiver);
        while (TyCtxt.Layer(payload, out var below, out _) != HirPlaceKind.Value && below is not null)
        {
            payload = below;
        }

        var whole = compilation.Types.Reference(view.Semantics, payload, view.Origin);
        if (view.Components is not [var referent] || MirTypes.Agree(payload, referent))
        {
            return this.Operand(receiver, view, input: true, slot);
        }

        var borrowed = this.body.AddOperand(this.Operand(receiver, whole, input: true));
        return this.Temporary(view, MirRvalue.Cast(MirCastKind.Upcast, borrowed, view) with { Proven = true }, receiver, slot);
    }

    // A Call terminator of a callee on the operands pushed since start, into the destination; one that diverges continues for the checks
    // only (SPEC 14.10).
    private void Invoke(MirPlace destination, in MirCallee callee, int start, Koto node, bool diverges = false, in MirOperand value = default)
    {
        var operands = this.Flush(start);
        var next = this.body.AddBlock();
        this.body.Terminate(
            this.current,
            MirTerminator.Call(
            callee,
            operands,
            this.body.Operands.Length - operands,
            destination,
            diverges ? -1 : next,
            this.Info(node)) with { CheckTarget = diverges ? next : -1, Operand = value });
        this.current = next;
    }
}
