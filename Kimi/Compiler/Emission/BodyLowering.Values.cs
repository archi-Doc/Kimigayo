// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private static BoundType? ValueType(OwnershipBody body, int id)
    {
        var place = ValuePlace(body.Operations[id]);
        return place >= 0 ? body.Places[place].Type : null;
    }

    private static int ValuePlace(OwnershipOperation operation) => operation.Kind is OwnershipOperationKind.Consume or OwnershipOperationKind.AcquirePattern or OwnershipOperationKind.Borrow or OwnershipOperationKind.WriteElement ||
        (operation.Kind == OwnershipOperationKind.Read && operation.Input >= 0) ? operation.Input : operation.Place;

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
                OwnershipValueKind.None or OwnershipValueKind.Constant or OwnershipValueKind.Parameter or OwnershipValueKind.Call or OwnershipValueKind.StringComparison or OwnershipValueKind.Borrow or OwnershipValueKind.Element => 0,
                OwnershipValueKind.Alias or OwnershipValueKind.Unary or OwnershipValueKind.Convert => 1,
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
            if (operation.Kind == OwnershipOperationKind.WriteElement &&
                ((uint)operation.Place >= (uint)body.Places.Count || (uint)operation.Input >= (uint)body.Places.Count))
            {
                return false;
            }

            var scalar = IsScalar(ValueType(body, id)!);
            if (value.Kind == OwnershipValueKind.Convert && !scalar)
            {
                return false;
            }

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
                if (producer is not (OwnershipOperationKind.Read or OwnershipOperationKind.Consume or OwnershipOperationKind.Produce or OwnershipOperationKind.Call or OwnershipOperationKind.InitializeSubject or OwnershipOperationKind.AcquirePattern) && body.Values[input].Kind != OwnershipValueKind.Phi)
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

            if (value.Kind == OwnershipValueKind.Convert &&
                (operation.Kind != OwnershipOperationKind.Produce ||
                operation.Source is not Parsing.ConversionKoto conversion ||
                !ReferenceEquals(ValueType(body, id), conversion.BoundType) ||
                !ReferenceEquals(ValueType(body, Input(body, id, 0)), conversion.Left.BoundType) ||
                !ValidScalarConversion(conversion.ConversionBinding, ValueType(body, Input(body, id, 0)), ValueType(body, id))))
            {
                return false;
            }

            if (value.Kind == OwnershipValueKind.Parameter &&
                (operation.Kind != OwnershipOperationKind.Produce || body.Places[operation.Place].Kind != OwnershipPlaceKind.Parameter ||
                value.Constant < 0 || value.Constant >= body.Function.Parameters.Count ||
                !ReferenceEquals(operation.Source, body.Function.Parameters[(int)value.Constant].Type) ||
                !ReferenceEquals(ValueType(body, id), body.Function.Parameters[(int)value.Constant].Type.BoundType)))
            {
                return false;
            }

            if (value.Kind == OwnershipValueKind.Constant)
            {
                var type = ValueType(body, id);
                if (FloatingTypes.Supports(type) || FloatingTypes.Supports(operation.Source.BoundType))
                {
                    if (!ReferenceEquals(type, operation.Source.BoundType) ||
                        !FloatingTypes.TryLiteral(operation.Source, out var bits) || bits != value.Constant)
                    {
                        return false;
                    }

                    continue;
                }

                if (ReferenceEquals(type, BoundType.Char) || operation.Source is Parsing.CharLiteralKoto)
                {
                    if (!ReferenceEquals(type, BoundType.Char) || !ReferenceEquals(operation.Source.BoundType, BoundType.Char) ||
                        operation.Source is not Parsing.CharLiteralKoto { Value: { } character } ||
                        !ScalarTypes.IsCharacterValue(value.Constant) || value.Constant != character.Value)
                    {
                        return false;
                    }

                    continue;
                }

                var width = ScalarTypes.Width(type);
                if (ReferenceEquals(type, BoundType.Boolean) ? value.Constant < 0 || value.Constant > 1 : width == 0 || ScalarTypes.Normalize(value.Constant, width) != value.Constant)
                {
                    return false;
                }

                if (width == 128)
                {
                    var source = operation.Source;
                    var number = source is Parsing.PrefixMinusKoto or Parsing.PrefixPlusKoto
                        ? ((Parsing.UnaryKoto)source).Operand as Parsing.NumberLiteralKoto : source as Parsing.NumberLiteralKoto;
                    if (number is not null)
                    {
                        if (!ReferenceEquals(type, source.BoundType) || !number.TryGetIntegerMagnitude(out var magnitude) ||
                            !ScalarTypes.TryLiteral(type, magnitude, source is Parsing.PrefixMinusKoto, 64, out var bits) || bits != value.Constant)
                        {
                            return false;
                        }
                    }
                    else if (value.Constant != 1 || source.Akind is not (Parsing.KotoKind.PrefixPlusPlus or Parsing.KotoKind.PrefixMinusMinus or Parsing.KotoKind.PostfixIncrement or Parsing.KotoKind.PostfixDecrement))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static bool ValidScalarConversion(ConversionBinding binding, BoundType? source, BoundType? target)
        => binding == ConversionBinding.Integer ? ScalarTypes.Width(source) != 0 && ScalarTypes.Width(target) != 0 :
            binding == ConversionBinding.Floating && FloatingTypes.Supports(source) && FloatingTypes.Supports(target) &&
            (ReferenceEquals(source, target) || ReferenceEquals(source, BoundType.F32));
}
