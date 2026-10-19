// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Intrinsic calls and the compiler functions the builder serves (SPEC 7.3, 17.4, 13.6.4): an intrinsic's operands beyond a written
// call's arguments, a synthesized call of a served compiler function, a fixed-code failure and its destination, and a runtime Type test.
internal sealed partial class MirBuilder
{
    // The operand an intrinsic takes beyond a written call's arguments (its row's arity; DECISIONS R4-M40): Fail's fixed code (SPEC 22.5.2)
    // and Raw.slice's backing, Unit, since nothing backs a written raw slice (SPEC 5.6: its storage is the caller's obligation); none for
    // another callee.
    private MirOperand? Extra(in IntrinsicDisposition disposition) => disposition.Id switch
    {
        IntrinsicId.Fail => this.Code(disposition.Code!),
        IntrinsicId.RawSlice => this.UnitOperand(),
        _ => null,
    };

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
