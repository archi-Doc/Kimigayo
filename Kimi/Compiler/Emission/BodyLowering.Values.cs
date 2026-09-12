// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private static BoundType? ValueType(OwnershipBody body, int id)
    {
        var operation = body.Operations[id];
        var place = operation.Kind == OwnershipOperationKind.Consume ? operation.Input : operation.Place;
        return place >= 0 ? body.Places[place].Type : body.Values[id].Kind == OwnershipValueKind.Phi ? BoundType.Boolean : null;
    }

    private static bool ValidateValues(OwnershipBody body)
    {
        if (body.Values.Count != body.Operations.Count)
        {
            return false;
        }

        for (var id = 0; id < body.Values.Count; id++)
        {
            var value = body.Values[id];
            var expected = value.Kind switch
            {
                OwnershipValueKind.None or OwnershipValueKind.Constant => 0,
                OwnershipValueKind.Alias or OwnershipValueKind.Unary => 1,
                OwnershipValueKind.Binary or OwnershipValueKind.Phi => 2,
                _ => -1,
            };
            if (value.Count != expected || value.Start < 0 || value.Start > body.ValueOperands.Count - value.Count)
            {
                return false;
            }

            // Non-scalar operations retain ownership-only Place flow until their lowering is implemented.
            var operation = body.Operations[id];
            var scalar = IsScalar(ValueType(body, id)!);
            if (!scalar && operation.Kind != OwnershipOperationKind.Branch)
            {
                continue;
            }

            for (var n = 0; n < value.Count; n++)
            {
                var input = Input(body, id, n);
                if ((uint)input >= (uint)body.Values.Count || (value.Kind != OwnershipValueKind.Phi && input >= id) || !IsScalar(ValueType(body, input)!))
                {
                    return false;
                }

                var producer = body.Operations[input].Kind;
                if (producer is not (OwnershipOperationKind.Read or OwnershipOperationKind.Consume or OwnershipOperationKind.Produce) && body.Values[input].Kind != OwnershipValueKind.Phi)
                {
                    return false;
                }
            }

            if (value.Kind == OwnershipValueKind.Constant && (ReferenceEquals(ValueType(body, id), BoundType.Boolean) ? value.Constant is < 0 or > 1 : value.Constant is < int.MinValue or > int.MaxValue))
            {
                return false;
            }
        }

        return true;
    }
}
