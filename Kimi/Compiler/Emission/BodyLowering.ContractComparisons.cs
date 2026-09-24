// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private bool LowerContractComparison(OwnershipBody body, EmissionFunction function, int id, out string? failure)
    {
        failure = null;
        var value = body.Values[id];
        var equality = value.Operator is KotoKind.EqualsEquals or KotoKind.ExclamationEquals;
        var input = value.Count == 1 ? Input(body, id, 0) : -1;
        if (input < 0 || body.Operations[id].Source is not BinaryKoto { ComparisonCall: { } call } binary ||
            binary.Akind != value.Operator || !ReferenceEquals(ValueType(body, id), BoundType.Boolean) ||
            !ReferenceEquals(ValueType(body, input), equality ? BoundType.Boolean : BoundType.I32))
        {
            return Fail("Contract comparison requires its verified call result and Boolean result.", out failure);
        }

        var predicate = value.Operator switch
        {
            KotoKind.EqualsEquals => "ne", KotoKind.ExclamationEquals => "eq",
            KotoKind.LessThan => "slt", KotoKind.LessThanEquals => "sle",
            KotoKind.GreaterThan => "sgt", KotoKind.GreaterThanEquals => "sge",
            _ => null,
        };
        if (predicate is null || !ReferenceEquals(SignatureType(this, call.BoundType), ValueType(body, input)) ||
            body.Operations[Definition(body, input)] is not { Kind: OwnershipOperationKind.Call } producer || !ReferenceEquals(producer.Source, call))
        {
            return Fail("Contract comparison has an inconsistent requirement result.", out failure);
        }

        var opcode = !equality && call.BoundCall?.TupleOperator == true ? EmissionOpcode.TupleRelation : EmissionOpcode.Scalar;
        function.AddScalar(opcode, id, [this.PhysicalOperand(body, input), new(EmissionOperandKind.Integer, 0)], equality ? "i1" : "i32", predicate, comparison: true);
        return true;
    }
}
