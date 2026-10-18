// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// A comparison operator's call (SPEC 13.4.1).
internal sealed partial class MirBuilder
{
    // SPEC 13.4.1: the Contract call of a comparison operator other than == (whose call gives equals' bool), then equals' bool negated
    // for !=, else compare's i32 compared with zero by the relation.
    private void Compare(MirPlace destination, CallPlan call, Koto node, MirBinaryOp relation)
    {
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
}
