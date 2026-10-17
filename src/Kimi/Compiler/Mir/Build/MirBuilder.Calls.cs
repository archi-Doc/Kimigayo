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
        var call = (ExpansionPlan.Of(node) is { } plan ? plan.Second : CallPlan.Of(node))!;
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
    // takes it for its parameter's Type or as the value an expansion evaluated for it (given), each read where it is evaluated (SPEC 12.2:
    // secured before a later argument that builds code, or in its pending slot), then its omitted defaults (Defaults), passed by parameter
    // slot, the receiver first. A compiler function is its intrinsic where the table gives one (IntrinsicDisposition), naming its library
    // declaration (A3 E14), with the operands it takes beyond the written arguments (Extra), and a direct base call the implementation the
    // virtual selection names. A call of a Never function or a diverging intrinsic has no runtime successor and continues for the checks
    // (SPEC 14.10); a call whose operand does not complete enters no callee at run time (SPEC 15.6.7), but for the checks it continues where
    // that operand's code continues, its later operands, defaults and activation checked there (SPEC 14.10.3), each operand as built: a
    // Never form's is the Never value (DECISIONS R4-M62).
    private void Call(MirPlace destination, CallPlan call, Koto node, Koto? first, Koto? given = null, in MirOperand value = default)
    {
        var function = call.Kind == CalleeKind.Value ? null : call.Target;
        var receiver = function is null ? null : call.Receiver;
        var count = function is null ? call.Signature.Components[0].Components.Count : ((FunctionKoto)function.Declaration).Parameters.Count;
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
        var last = -2; // SPEC 12.2: the last argument that builds code, in evaluation order: the receiver -2, first -1, the others in order.
        for (var i = 0; i < call.ArgumentOperations.Length; i++)
        {
            var argument = call.ArgumentOperations[i].Source!;
            if (!ReferenceEquals(argument, given) && Builds(argument))
            {
                last = ReferenceEquals(argument, first) ? Math.Max(last, -1) : i;
            }
        }

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
            var operand = this.Operand(
                receiver,
                ReferenceEquals(direct, function) ? call.ReceiverOperation.AdaptedType ?? parameter : parameter,
                input: true,
                slot);
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
                    var order = pass == 0 ? -1 : i;
                    operand = slot is null && order < last ? this.Secure(operand, argument.Source!) : operand;
                    this.arguments[start + MirCallee.Position(function, argument.ParameterIndex)] = slot is { } place ? this.Pending(
                        place,
                        operand,
                        argument.Source!) : operand;
                }
            }
        }

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

    // The operand an intrinsic takes beyond a written call's arguments (its row's arity; DECISIONS R4-M40): Fail's fixed code (SPEC 22.5.2)
    // and Raw.slice's backing, Unit, since nothing backs a written raw slice (SPEC 5.6: its storage is the caller's obligation); none for
    // another callee.
    private MirOperand? Extra(in IntrinsicDisposition disposition) => disposition.Id switch
    {
        IntrinsicId.Fail => this.Code(disposition.Code!),
        IntrinsicId.RawSlice => this.UnitOperand(),
        _ => null,
    };

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
        var callee = disposition.Kind == DispositionKind.Intrinsic
            ? MirCallee.Intrinsic(disposition.Id, function, subst)
            : new(MirCalleeKind.Fn, function, subst);
        this.Invoke(destination, callee, start, node);
    }

    // SPEC 17.4: a block that fails with a fixed SPEC 22.5.2 code (an expansion's check), without a successor.
    private void Fail(int block, string code, Koto node)
    {
        var start = this.body.AddOperand(this.Code(code));
        this.body.Terminate(block, MirTerminator.Call(MirCallee.Intrinsic(IntrinsicId.Fail), start, 1, this.Unwritten(node), -1, this.Info(node)));
    }

    // A SPEC 22.5.2 failure code, the string constant Fail takes.
    private MirOperand Code(string code) => MirOperand.Const(this.body.Const(new(MirConstKind.String, BoundType.String, Value: code)));

    // The destination of a diverging call on a path that ends with it (a failed check, $require's abort): a Never temp the call never writes
    // (MirUses), without storage, whose markers the statement's other paths would end without beginning (SPEC 3.6).
    private MirPlace Unwritten(Koto node) => new(this.body.AddLocal(new(BoundType.Never, MirLocalKind.Temp, this.Info(node, local: true))));

    // SPEC 13.6.4: a runtime Type test is the intrinsic typeTest of a shared view of the handle's payload and the target Type's token, and
    // `is not` is its negation, the Not of its result (as a `!=` call's); the Never value has no payload to view, so it tests nothing.
    private void TypeTest(MirPlace destination, IsKoto test)
    {
        var handle = HirTables.AdjustOf(test.Left).Acquire == HirAcquire.Temporary ? this.Value(test.Left) : this.Place(test.Left);
        if (!this.IsNever(handle))
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
