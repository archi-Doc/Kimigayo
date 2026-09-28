// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private bool LowerRuntimeTypeTest(OwnershipBody body, EmissionFunction function, int id, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        if (operation.Kind != OwnershipOperationKind.Produce || body.Values[id].Count != 1 ||
            operation.Source is not IsKoto { BoundRuntimeTest: { SharedType: { } shared } plan } source ||
            !ReferenceEquals(ValueType(body, id), BoundType.Boolean) ||
            this.ObjectRuntimeTypes?.GetValueOrDefault(plan.TargetType) is not > 0)
        {
            return Fail("Runtime Type test requires its finalized identity and shared operand plan.", out failure);
        }

        var input = Input(body, id, 0);
        if ((uint)input >= (uint)id || !ReferenceEquals(ValueType(body, input), shared) ||
            body.Operations[input].Kind != OwnershipOperationKind.Borrow ||
            !ReferenceEquals(body.Operations[input].Source, source.Left) ||
            (body.IsReachable(id) && !this.Dominates(input, id)))
        {
            return Fail("Runtime Type test requires one evaluated, verified shared object borrow.", out failure);
        }

        function.AddScalar(EmissionOpcode.ObjectTypeTest, id, [this.PhysicalOperand(body, input), new(EmissionOperandKind.Integer, this.ObjectRuntimeTypes![plan.TargetType])], op: source.IsNegated ? "not" : null);
        return true;
    }
}
