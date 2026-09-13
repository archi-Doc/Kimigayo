// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private readonly List<int> placeValues = new();

    private bool TryScalarLiteral(Koto source, out long value)
    {
        var negative = source is PrefixMinusKoto;
        var number = source is PrefixMinusKoto or PrefixPlusKoto ? ((UnaryKoto)source).Operand as NumberLiteralKoto : source as NumberLiteralKoto;
        if (number is { IsInteger: true } && number.TryGetIntegerMagnitude(out var magnitude))
        {
            return ScalarTypes.TryLiteral(source.BoundType, magnitude, negative, this.compilation.PointerWidth, out value);
        }

        value = 0;
        return false;
    }

    private int Value(int place) => place < 0 ? -1 : this.placeValues[place];

    private void SetValue(int operation, OwnershipValueKind kind, ReadOnlySpan<int> inputs, KotoKind op = default, long constant = 0)
    {
        var start = this.body.ValueOperands.Count;
        this.body.ValueOperands.AddRange(inputs);
        this.body.Values[operation] = new(kind, start, inputs.Length, constant, op);
    }

    private void RecordValue(int id, OwnershipOperationKind kind, Koto source, int place, int input)
    {
        this.body.Values.Add(default);
        if (kind is OwnershipOperationKind.Read or OwnershipOperationKind.Consume && place >= 0 &&
            this.body.Places[place].Kind == OwnershipPlaceKind.Parameter && ScalarResult(this.body.Places[place].Type))
        {
            this.SetValue(id, OwnershipValueKind.Alias, [this.Value(place)]);
        }

        if (kind == OwnershipOperationKind.Write && input >= 0)
        {
            this.SetValue(id, OwnershipValueKind.Alias, [this.Value(input)]);
        }

        if (kind == OwnershipOperationKind.CallEntry && place >= 0 && ScalarResult(this.body.Places[place].Type))
        {
            this.SetValue(id, OwnershipValueKind.Alias, [this.Value(place)]);
        }

        if (kind is OwnershipOperationKind.Read or OwnershipOperationKind.Produce or OwnershipOperationKind.Consume)
        {
            var destination = kind == OwnershipOperationKind.Consume ? input : place;
            if (destination >= 0)
            {
                this.placeValues[destination] = id;
            }
        }

        if (kind == OwnershipOperationKind.Produce)
        {
            if (source is BoolLiteralKoto boolean)
            {
                this.SetValue(id, OwnershipValueKind.Constant, [], constant: boolean.Value ? 1 : 0);
            }
            else if (this.TryScalarLiteral(source, out var value))
            {
                this.SetValue(id, OwnershipValueKind.Constant, [], constant: value);
            }
        }
    }

    private int UnaryValue(UnaryKoto unary)
    {
        // Binding fits a directly signed literal once, including each signed minimum.
        if (unary is PrefixMinusKoto or PrefixPlusKoto && unary.Operand is NumberLiteralKoto)
        {
            return this.Temporary(unary);
        }

        var input = this.Value(this.Expression(unary.Operand, PlaceUseKind.Read));
        if (unary.Akind is KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus or KotoKind.PostfixIncrement or KotoKind.PostfixDecrement)
        {
            var one = this.Temporary(unary);
            this.SetValue(this.Value(one), OwnershipValueKind.Constant, [], constant: 1);
            var updated = this.Temporary(unary);
            this.SetValue(this.Value(updated), OwnershipValueKind.Binary, [input, this.Value(one)], unary.Akind is KotoKind.PrefixPlusPlus or KotoKind.PostfixIncrement ? KotoKind.Plus : KotoKind.Minus);
            this.Emit(OwnershipOperationKind.Write, unary, this.Local(KotoHelper.UnwrapParentheses(unary.Operand)), updated);
            var result = this.Temporary(unary);
            this.SetValue(this.Value(result), OwnershipValueKind.Alias, [unary.Akind is KotoKind.PostfixIncrement or KotoKind.PostfixDecrement ? input : this.Value(updated)]);
            return result;
        }

        var output = this.Temporary(unary);
        this.SetValue(this.Value(output), OwnershipValueKind.Unary, [input], unary.Akind);
        return output;
    }
}
