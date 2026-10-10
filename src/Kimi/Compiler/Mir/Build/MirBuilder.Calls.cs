// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Calls (SPEC 7.3).
internal sealed partial class MirBuilder
{
    // A call of the function Binding selected: its arguments evaluated in order, each as its Adjust takes it, then passed by parameter
    // slot; a call of a Never function has no runtime successor and continues for the checks (SPEC 14.10). Not built yet: Requirement,
    // Virtual, Value and intrinsic callees and receivers (U8), constructors (U13), omitted defaults (U14) and the further calls of a node,
    // which its expansion orders (U12).
    private void Call(MirPlace destination, Koto node)
    {
        var call = CallPlan.Of(node)!;
        var function = call.Kind == CalleeKind.Function && IntrinsicDisposition.Of(call.Target.CompilerFunction).Kind == DispositionKind.Body ? call.Target : null;
        if (function is null || call.Receiver is not null || function.ReceiverIndex >= 0 || CallPlan.Parameter(function, -1) is not null ||
            !call.DefaultArguments.IsEmpty || CallPlan.Of(node, 1) is not null)
        {
            this.Unsupported(node, destination);
            return;
        }

        var operations = call.ArgumentOperations;
        var start = this.arguments.Count;
        CollectionsMarshal.SetCount(this.arguments, start + operations.Length);
        foreach (ref readonly var argument in operations)
        {
            this.arguments[start + argument.ParameterIndex] = this.Operand(argument.Source!);
        }

        var first = this.body.Operands.Length;
        for (var i = 0; i < operations.Length; i++)
        {
            this.body.AddOperand(this.arguments[start + i]);
        }

        CollectionsMarshal.SetCount(this.arguments, start);
        var next = this.body.AddBlock();
        var diverges = ReferenceEquals(call.ReturnType, BoundType.Never);
        var callee = new MirCallee(MirCalleeKind.Fn, function, compilation.Types.SubstOf(call));
        this.body.Terminate(this.current, MirTerminator.Call(callee, first, operations.Length, destination, diverges ? -1 : next, this.Info(node), diverges ? next : -1));
        this.current = next;
    }
}
