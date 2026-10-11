// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Calls (SPEC 7.3).
internal sealed partial class MirBuilder
{
    // A node's call (CallPlan.Of; a Property read's getter; a user or view index's selected call, SPEC 4.6.9); a constructor's builds its
    // object in the destination (SPEC 6.2.3.2).
    private void Call(MirPlace destination, Koto node) => this.Call(destination, ExpansionPlan.Of(node)?.Second ?? CallPlan.Of(node)!, node);

    // SPEC 7.3, 7.6.3: a call of the function, requirement, virtual slot or callable value Binding selected: a value call's callee in its
    // receiver mode, the receiver, then the arguments in their order (an assignment's secured value first, SPEC 13.7.1), each as its Adjust
    // takes it for its parameter's Type or as the value an expansion evaluated for it (given), then its omitted defaults (Defaults), passed by
    // parameter slot, the receiver first. A compiler function is its intrinsic where the table gives one (IntrinsicDisposition; Fail with its
    // fixed code), a direct base call the implementation the virtual selection names, and a place result the reference the call returns. A
    // call of a Never function or a diverging intrinsic has no runtime successor and continues for the checks (SPEC 14.10); a call whose
    // operand does not complete enters no callee (SPEC 15.6.7).
    private void Call(MirPlace destination, CallPlan call, Koto node, Koto? first = null, Koto? given = null, in MirOperand value = default)
    {
        var function = call.Kind == CalleeKind.Value ? null : call.Target;
        var receiver = function is null ? null : call.Receiver;
        var count = function is null ? call.Signature.Components[0].Components.Count : ((FunctionKoto)function.Declaration).Parameters.Count;
        var direct = call is { Kind: CalleeKind.Virtual, VirtualIsDirect: true } ? call.VirtualImplementation is { } implementation ? HirTables.Symbol(implementation) : null : function;
        if (call.ArgumentOperations.Length + call.DefaultArguments.Length + (receiver is null ? 0 : 1) != count || (receiver is not null && function!.ReceiverIndex < 0) ||
            (direct is null && function is not null))
        {
            this.Unsupported(node, destination); // A plan whose operations do not cover the parameters (an expansion supplies them).
            return;
        }

        var (source, pending) = (call.ReceiverOperation.Source ?? call.CalleeValue, this.arguments.Count);
        if (function is null)
        {
            this.arguments.Add(this.Operand(source, call.ReceiverOperation.AdaptedType, true)); // The callee value, pending while the arguments are evaluated.
        }

        var (start, completes, defaults) = (this.arguments.Count, function is not null || Completes(source), !call.DefaultArguments.IsEmpty);
        CollectionsMarshal.SetCount(this.arguments, start + count);
        CollectionsMarshal.AsSpan(this.arguments)[start..].Clear(); // No stale operand reads as pending (Transfer).
        if (receiver is not null)
        {
            var operand = this.Operand(receiver, call.ReceiverOperation.AdaptedType ?? call.ReceiverOperation.ParameterType, true);
            this.arguments[start] = defaults ? this.Pending(operand, call.ReceiverOperation.ParameterType, receiver) : operand;
            completes &= Completes(receiver);
        }

        for (var pass = first is null ? 1 : 0; pass < 2; pass++)
        {
            foreach (ref readonly var argument in call.ArgumentOperations)
            {
                if (ReferenceEquals(argument.Source, first) == (pass == 0))
                {
                    var operand = ReferenceEquals(argument.Source, given) ? value : this.Operand(argument.Source!, argument.AdaptedType ?? argument.ParameterType, true);
                    this.arguments[start + Position(function, argument.ParameterIndex)] = defaults ? this.Pending(operand, argument.ParameterType, argument.Source!) : operand;
                    completes &= Completes(argument.Source!);
                }
            }
        }

        var subst = compilation.Types.SubstOf(call);
        if (!completes || (defaults && !this.Defaults(call, function!, subst, start, node)))
        {
            this.arguments.RemoveRange(pending, this.arguments.Count - pending);
            return;
        }

        var disposition = function is null ? default : IntrinsicDisposition.Of(function.CompilerFunction);
        if (disposition.Id == IntrinsicId.Fail)
        {
            this.arguments.Add(MirOperand.Const(this.body.Const(new(MirConstKind.String, BoundType.String, Value: disposition.Code))));
        }

        var callee = disposition.Kind == DispositionKind.Intrinsic ? MirCallee.Intrinsic(disposition.Id, subst) : call.Kind switch
        {
            CalleeKind.Requirement => new(MirCalleeKind.Requirement, function, subst, Contract: call.RequirementContract),
            CalleeKind.Virtual when !call.VirtualIsDirect => new(MirCalleeKind.Virtual, function, subst),
            CalleeKind.Value => new(MirCalleeKind.Value),
            _ => new MirCallee(MirCalleeKind.Fn, direct, subst),
        };

        var result = call.ResultMode == FunctionResultMode.Value || ReferenceEquals(this.body.TypeOf(destination), call.ReturnType) ? destination : this.Temp(call.ReturnType, node);
        this.Invoke(result, callee, start, node, ReferenceEquals(call.ReturnType, BoundType.Never) || MirDescriptors.Of(disposition.Id).Diverges, function is null ? this.arguments[pending] : default);
        this.arguments.RemoveRange(pending, start - pending);
    }

    // A Call terminator of a callee on the operands pushed since start, into the destination; one that diverges continues for the checks
    // only (SPEC 14.10).
    private void Invoke(MirPlace destination, in MirCallee callee, int start, Koto node, bool diverges = false, in MirOperand value = default)
    {
        var (operands, next) = (this.Flush(start), this.body.AddBlock());
        this.body.Terminate(this.current, MirTerminator.Call(callee, operands, this.body.Operands.Length - operands, destination, diverges ? -1 : next, this.Info(node), diverges ? next : -1, value));
        this.current = next;
    }

    // A call the builder synthesizes of the compiler function serving a built-in form (R3 decision 5) on the operands pushed since start,
    // under the substitution of its declaring Type's instance and its Type arguments.
    private void Serve(MirPlace destination, CompilerFunctionKind kind, BoundType? declaring, ReadOnlySpan<BoundType?> types, int start, Koto node)
    {
        var (function, disposition) = (compilation.Library.Function(kind)!, IntrinsicDisposition.Of(kind));
        var subst = compilation.Types.Synthesized(function, declaring, types);
        this.Invoke(destination, disposition.Kind == DispositionKind.Intrinsic ? MirCallee.Intrinsic(disposition.Id, subst) : new(MirCalleeKind.Fn, function, subst), start, node);
    }

    // SPEC 17.3: a block that fails with a fixed SPEC 22.5.4 code (an expansion's check), without a successor.
    private void Fail(int block, string code, Koto node)
    {
        var start = this.body.AddOperand(MirOperand.Const(this.body.Const(new(MirConstKind.String, BoundType.String, Value: code))));
        this.body.Terminate(block, MirTerminator.Call(MirCallee.Intrinsic(IntrinsicId.Fail), start, 1, this.Unwritten(node), -1, this.Info(node)));
    }

    // The destination of a diverging call on a path that ends with it (a failed check, $require's abort): a Never temp the call never writes
    // (MirUses), without storage, whose markers the statement's other paths would end without beginning (SPEC 3.6.2).
    private MirPlace Unwritten(Koto node) => new(this.body.AddLocal(new(BoundType.Never, MirLocalKind.Temp, this.Info(node, true))));

    // SPEC 13.6.1: a runtime Type test is the intrinsic typeTest of a shared view of the handle's payload and the target Type's token; an
    // operand that does not complete tests nothing.
    private void TypeTest(MirPlace destination, IsKoto test)
    {
        var handle = HirTables.AdjustOf(test.Left).Acquire == HirAcquire.Temporary ? this.Value(test.Left) : this.Place(test.Left);
        if (Completes(test.Left))
        {
            var start = this.arguments.Count;
            this.arguments.Add(this.Borrow(SemanticsKind.ObjRef, this.body.Project(handle, MirProjectionKind.Deref, 0), test));
            this.arguments.Add(MirOperand.Const(this.body.Const(new(MirConstKind.TypeToken, TypeOf(test.Right)))));
            this.Invoke(destination, MirCallee.Intrinsic(IntrinsicId.TypeTest), start, test);
        }
    }
}
