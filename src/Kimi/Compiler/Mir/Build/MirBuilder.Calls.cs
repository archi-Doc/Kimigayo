// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Calls (SPEC 7.3).
internal sealed partial class MirBuilder
{
    // A node's call (CallPlan.Of; a Property read's getter). Not built yet: a constructor's (U13), omitted defaults (U14) and the calls of
    // an expansion (U12).
    private void Call(MirPlace destination, Koto node)
    {
        var call = CallPlan.Of(node)!;
        if (ExpansionPlan.Of(node) is not null || !call.DefaultArguments.IsEmpty || (call.Kind != CalleeKind.Value && CallPlan.Parameter(call.Target, -1) is not null))
        {
            this.Unsupported(node, destination);
            return;
        }

        this.Call(destination, call, node);
    }

    // SPEC 7.3, 7.6.3: a call of the function, requirement, virtual slot or callable value Binding selected: a value call's callee in its
    // receiver mode, the receiver, then the arguments in their order (an assignment's secured value first, SPEC 13.7.1), each as its Adjust
    // takes it for its parameter's Type, passed by parameter slot, the receiver first. A compiler function is its intrinsic where the table
    // gives one (IntrinsicDisposition; Fail with its fixed code), a direct base call the implementation the virtual selection names, and a
    // place result the reference the call returns. A call of a Never function or a diverging intrinsic has no runtime successor and
    // continues for the checks (SPEC 14.10).
    private void Call(MirPlace destination, CallPlan call, Koto node, Koto? first = null)
    {
        var function = call.Kind == CalleeKind.Value ? null : call.Target;
        var receiver = function is null ? null : call.Receiver;
        var count = function is null ? call.Signature.Components[0].Components.Count : ((FunctionKoto)function.Declaration).Parameters.Count;
        var direct = call is { Kind: CalleeKind.Virtual, VirtualIsDirect: true } ? call.VirtualImplementation is { } implementation ? HirTables.Symbol(implementation) : null : function;
        if (call.ArgumentOperations.Length + (receiver is null ? 0 : 1) != count || (receiver is not null && function!.ReceiverIndex < 0) || (direct is null && function is not null))
        {
            this.Unsupported(node, destination); // An expansion supplies the other arguments (U12).
            return;
        }

        var value = function is null ? this.Operand(call.ReceiverOperation.Source ?? call.CalleeValue, null, true) : default;
        var start = this.arguments.Count;
        CollectionsMarshal.SetCount(this.arguments, start + count);
        if (receiver is not null)
        {
            this.arguments[start] = this.Operand(receiver, call.ReceiverOperation.AdaptedType ?? call.ReceiverOperation.ParameterType, true);
        }

        for (var pass = first is null ? 1 : 0; pass < 2; pass++)
        {
            foreach (ref readonly var argument in call.ArgumentOperations)
            {
                if (ReferenceEquals(argument.Source, first) == (pass == 0))
                {
                    this.arguments[start + Position(function, argument.ParameterIndex)] = this.Operand(argument.Source!, argument.AdaptedType ?? argument.ParameterType, true);
                }
            }
        }

        var disposition = function is null ? default : IntrinsicDisposition.Of(function.CompilerFunction);
        if (disposition.Id == IntrinsicId.Fail)
        {
            this.arguments.Add(MirOperand.Const(this.body.Const(new(MirConstKind.String, BoundType.String, Value: disposition.Code))));
        }

        var operands = this.Flush(start);
        var subst = compilation.Types.SubstOf(call);
        var callee = disposition.Kind == DispositionKind.Intrinsic ? MirCallee.Intrinsic(disposition.Id, subst) : call.Kind switch
        {
            CalleeKind.Requirement => new(MirCalleeKind.Requirement, function, subst, Contract: call.RequirementContract),
            CalleeKind.Virtual when ReferenceEquals(direct, function) => new(MirCalleeKind.Virtual, function, subst),
            CalleeKind.Value => new(MirCalleeKind.Value),
            _ => new MirCallee(MirCalleeKind.Fn, direct, subst),
        };

        var result = call.ResultMode == FunctionResultMode.Value || ReferenceEquals(this.body.TypeOf(destination), call.ReturnType) ? destination : this.Temp(call.ReturnType, node);
        var diverges = ReferenceEquals(call.ReturnType, BoundType.Never) || MirDescriptors.Of(disposition.Id).Diverges;
        var next = this.body.AddBlock();
        this.body.Terminate(this.current, MirTerminator.Call(callee, operands, this.body.Operands.Length - operands, result, diverges ? -1 : next, this.Info(node), diverges ? next : -1, value));
        this.current = next;
    }
}
