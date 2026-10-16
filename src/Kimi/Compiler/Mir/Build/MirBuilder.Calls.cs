// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Calls (SPEC 7.3).
internal sealed partial class MirBuilder
{
    // A node's call (CallPlan.Of; a Property read's getter; a user or view index's selected call, SPEC 4.7.1); a constructor's builds its
    // object in the destination (SPEC 6.3.2). SPEC 13.4.1: a comparison operator's Contract call gives equals' bool, negated for !=, or
    // compare's i32 compared with zero.
    private void Call(MirPlace destination, Koto node)
    {
        var call = ExpansionPlan.Of(node)?.Second ?? CallPlan.Of(node)!;
        if (node is not BinaryKoto binary || BinaryOp(binary.Akind) is not (> MirBinaryOp.Eq and MirBinaryOp relation))
        {
            this.Call(destination, call, node, first: null);
            return;
        }

        var result = this.Temp(call.ReturnType, node);
        this.Call(result, call, node, first: null);
        var start = this.body.AddOperand(MirOperand.Move(result));
        if (relation != MirBinaryOp.Ne)
        {
            this.body.AddOperand(MirOperand.Const(this.body.Const(new(MirConstKind.Integer, call.ReturnType))));
        }

        this.Set(
            destination,
            relation == MirBinaryOp.Ne ? MirRvalue.Unary(MirUnaryOp.Not, start, proven: true) : MirRvalue.Binary(relation, start, proven: true),
            node);
    }

    // SPEC 7.3, 7.7.3: a call of the function, requirement, virtual slot or callable value Binding selected: a value call's callee in its
    // receiver mode, the receiver, then the arguments in their order (an assignment's secured value first, SPEC 13.7.1), each as its Adjust
    // takes it for its parameter's Type or as the value an expansion evaluated for it (given), then its omitted defaults (Defaults), passed by
    // parameter slot, the receiver first. A compiler function is its intrinsic where the table gives one (IntrinsicDisposition; Fail with its
    // fixed code), and a direct base call the implementation the virtual selection names. A
    // call of a Never function or a diverging intrinsic has no runtime successor and continues for the checks (SPEC 14.10); a call whose
    // operand does not complete enters no callee (SPEC 15.6.7).
    private void Call(MirPlace destination, CallPlan call, Koto node, Koto? first, Koto? given = null, in MirOperand value = default)
    {
        var function = call.Kind == CalleeKind.Value ? null : call.Target;
        var receiver = function is null ? null : call.Receiver;
        var count = function is null ? call.Signature.Components[0].Components.Count : ((FunctionKoto)function.Declaration).Parameters.Count;
        var direct = function;
        if (call is { Kind: CalleeKind.Virtual, VirtualIsDirect: true })
        {
            direct = call.VirtualImplementation is { } implementation ? HirTables.Symbol(implementation) : null;
        }

        if (call.ArgumentOperations.Length + call.DefaultArguments.Length + (receiver is null ? 0 : 1) != count ||
            (receiver is not null && function!.ReceiverIndex < 0) ||
            (direct is null && function is not null))
        {
            this.Unsupported(node, destination); // A plan whose operations do not cover the parameters (an expansion supplies them).
            return;
        }

        var source = call.ReceiverOperation.Source ?? call.CalleeValue;
        var pending = this.arguments.Count;
        var subst = compilation.Types.SubstOf(call);
        if (function is null)
        {
            // The callee value, pending while the arguments are evaluated.
            this.arguments.Add(this.Operand(source, call.ReceiverOperation.AdaptedType, input: true));
        }

        var start = this.arguments.Count;
        var completes = function is not null || Completes(source);
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
            var operand = this.Operand(
                receiver,
                ReferenceEquals(direct, function) ? call.ReceiverOperation.AdaptedType ?? parameter : parameter,
                input: true,
                slot);
            this.arguments[start] = slot is { } place ? this.Pending(place, operand, receiver) : operand;
            completes &= Completes(receiver);
        }

        for (var pass = first is null ? 1 : 0; pass < 2; pass++)
        {
            foreach (ref readonly var argument in call.ArgumentOperations)
            {
                if (ReferenceEquals(argument.Source, first) == (pass == 0))
                {
                    var slot = defaults ? this.Slot(argument.ParameterType, argument.Source!) : (MirPlace?)null;
                    var operand = ReferenceEquals(argument.Source, given) ? value : this.Operand(
                        argument.Source!,
                        argument.AdaptedType ?? argument.ParameterType,
                        input: true,
                        slot);
                    this.arguments[start + MirCallee.Position(function, argument.ParameterIndex)] = slot is { } place ? this.Pending(
                        place,
                        operand,
                        argument.Source!) : operand;
                    completes &= Completes(argument.Source!);
                }
            }
        }

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

        this.Invoke(
            destination,
            callee,
            start,
            node,
            ReferenceEquals(call.ReturnType, BoundType.Never) || MirDescriptors.Of(disposition.Id).Diverges,
            function is null ? this.arguments[pending] : default);
        this.arguments.RemoveRange(pending, start - pending);
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

    // A call the builder synthesizes of the compiler function serving a built-in form (R3 decision 5) on the operands pushed since start,
    // under the substitution of its declaring Type's instance and its Type arguments.
    private void Serve(MirPlace destination, CompilerFunctionKind kind, BoundType? declaring, ReadOnlySpan<BoundType?> types, int start, Koto node)
    {
        var function = compilation.Library.Function(kind)!;
        var disposition = IntrinsicDisposition.Of(kind);
        var subst = compilation.Types.Synthesized(function, declaring, types);
        var callee = disposition.Kind == DispositionKind.Intrinsic ? MirCallee.Intrinsic(disposition.Id, subst) : new(MirCalleeKind.Fn, function, subst);
        this.Invoke(destination, callee, start, node);
    }

    // SPEC 17.4: a block that fails with a fixed SPEC 22.5.2 code (an expansion's check), without a successor.
    private void Fail(int block, string code, Koto node)
    {
        var start = this.body.AddOperand(MirOperand.Const(this.body.Const(new(MirConstKind.String, BoundType.String, Value: code))));
        this.body.Terminate(block, MirTerminator.Call(MirCallee.Intrinsic(IntrinsicId.Fail), start, 1, this.Unwritten(node), -1, this.Info(node)));
    }

    // The destination of a diverging call on a path that ends with it (a failed check, $require's abort): a Never temp the call never writes
    // (MirUses), without storage, whose markers the statement's other paths would end without beginning (SPEC 3.6).
    private MirPlace Unwritten(Koto node) => new(this.body.AddLocal(new(BoundType.Never, MirLocalKind.Temp, this.Info(node, local: true))));

    // SPEC 13.6.4: a runtime Type test is the intrinsic typeTest of a shared view of the handle's payload and the target Type's token, and
    // `is not` is its negation, the Not of its result (as a `!=` call's); an operand that does not complete tests nothing.
    private void TypeTest(MirPlace destination, IsKoto test)
    {
        var handle = HirTables.AdjustOf(test.Left).Acquire == HirAcquire.Temporary ? this.Value(test.Left) : this.Place(test.Left);
        if (Completes(test.Left))
        {
            var start = this.arguments.Count;
            this.arguments.Add(this.Borrow(SemanticsKind.ObjRef, this.body.Project(handle, MirProjectionKind.Deref, 0), test));
            this.arguments.Add(MirOperand.Const(this.body.Const(new(MirConstKind.TypeToken, TypeOf(test.Right)))));
            var result = test.IsNegated ? this.Temp(BoundType.Boolean, test) : destination;
            this.Invoke(result, MirCallee.Intrinsic(IntrinsicId.TypeTest), start, test);
            if (test.IsNegated)
            {
                this.Set(destination, MirRvalue.Unary(MirUnaryOp.Not, this.body.AddOperand(MirOperand.Move(result)), proven: true), test);
            }
        }
    }
}
