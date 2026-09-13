// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private static BoundType? ValueType(OwnershipBody body, int id)
    {
        var place = ValuePlace(body.Operations[id]);
        return place >= 0 ? body.Places[place].Type : null;
    }

    private static int ValuePlace(OwnershipOperation operation) => operation.Kind == OwnershipOperationKind.Consume ? operation.Input : operation.Place;

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
                OwnershipValueKind.None or OwnershipValueKind.Constant or OwnershipValueKind.Parameter or OwnershipValueKind.Call => 0,
                OwnershipValueKind.Alias or OwnershipValueKind.Unary => 1,
                OwnershipValueKind.Binary => 2,
                OwnershipValueKind.Phi => value.Count,
                _ => -1,
            };
            var length = value.Kind == OwnershipValueKind.Phi ? body.PhiInputs.Count : body.ValueOperands.Count;
            if (value.Count < 0 || value.Count != expected || value.Start < 0 || value.Start > length - value.Count)
            {
                return false;
            }

            // Non-scalar operations retain ownership-only Place flow until their lowering is implemented.
            var operation = body.Operations[id];
            var scalar = IsScalar(ValueType(body, id)!);
            if (scalar && operation.Kind == OwnershipOperationKind.Branch && value.Kind != OwnershipValueKind.Phi)
            {
                return false;
            }

            if (value.Kind == OwnershipValueKind.Phi && (!scalar || operation.Kind != OwnershipOperationKind.Branch ||
                !ReferenceEquals(ValueType(body, id), operation.Source.BoundType)))
            {
                return false;
            }

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
                if (producer is not (OwnershipOperationKind.Read or OwnershipOperationKind.Consume or OwnershipOperationKind.Produce or OwnershipOperationKind.Call) && body.Values[input].Kind != OwnershipValueKind.Phi)
                {
                    return false;
                }

                if (value.Kind == OwnershipValueKind.Phi && (body.Values[input].Kind == OwnershipValueKind.Alias || !ReferenceEquals(ValueType(body, id), ValueType(body, input))))
                {
                    return false;
                }

                if (value.Kind == OwnershipValueKind.Alias && !ReferenceEquals(operation.Kind == OwnershipOperationKind.Branch ? BoundType.Boolean : ValueType(body, id), ValueType(body, input)))
                {
                    return false;
                }
            }

            if (value.Kind == OwnershipValueKind.Call && operation.Kind != OwnershipOperationKind.Call)
            {
                return false;
            }

            if (value.Kind == OwnershipValueKind.Parameter &&
                (operation.Kind != OwnershipOperationKind.Produce || body.Places[operation.Place].Kind != OwnershipPlaceKind.Parameter ||
                (ulong)value.Constant >= (ulong)body.Function.Parameters.Count ||
                !ReferenceEquals(operation.Source, body.Function.Parameters[(int)value.Constant].Type) ||
                !ReferenceEquals(ValueType(body, id), body.Function.Parameters[(int)value.Constant].Type.BoundType)))
            {
                return false;
            }

            if (value.Kind == OwnershipValueKind.Constant)
            {
                var type = ValueType(body, id);
                var width = ScalarTypes.Width(type);
                if (ReferenceEquals(type, BoundType.Boolean) ? value.Constant is < 0 or > 1 : width == 0 || ScalarTypes.Normalize(value.Constant, width) != value.Constant)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
