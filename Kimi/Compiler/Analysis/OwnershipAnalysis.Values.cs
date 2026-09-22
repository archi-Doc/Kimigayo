// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private readonly List<int> placeValues = new();

    private bool TryScalarLiteral(Koto source, out Int128 value)
    {
        if (FloatingTypes.Supports(source.BoundType))
        {
            var success = FloatingTypes.TryLiteral(source, out var bits);
            value = bits;
            return success;
        }

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

    private void SetValue(int operation, OwnershipValueKind kind, ReadOnlySpan<int> inputs, KotoKind op = default, Int128 constant = default)
    {
        var start = this.body.ValueOperands.Count;
        this.body.ValueOperands.AddRange(inputs);
        this.body.Values[operation] = new(kind, start, inputs.Length, op, constant);
    }

    private void RecordValue(int id, OwnershipOperationKind kind, Koto source, int place, int input)
    {
        this.body.Values.Add(default);
        if (kind == OwnershipOperationKind.Read && input >= 0 && ReferenceTypes.IsString(this.body.Places[input].Type))
        {
            this.placeValues[input] = id;
            this.SetValue(id, OwnershipValueKind.Borrow, []);
            return;
        }

        if (kind is OwnershipOperationKind.InitializeSubject or OwnershipOperationKind.AcquirePattern && place >= 0 && ScalarResult(this.body.Places[place].Type))
        {
            var sourcePlace = kind == OwnershipOperationKind.InitializeSubject ? input : place;
            var destination = kind == OwnershipOperationKind.InitializeSubject ? place : input;
            if (this.Value(sourcePlace) >= 0)
            {
                this.SetValue(id, OwnershipValueKind.Alias, [this.Value(sourcePlace)]);
            }

            this.placeValues[destination] = id;
        }

        if (kind is OwnershipOperationKind.Read or OwnershipOperationKind.Consume && place >= 0 &&
            this.body.Places[place].Kind == OwnershipPlaceKind.Parameter && (ScalarResult(this.body.Places[place].Type) || ReferenceTypes.IsString(this.body.Places[place].Type)))
        {
            // Parameters are immutable incoming SSA values. A read in another arm need not dominate this read.
            var parameter = this.Value(place);
            while (parameter >= 0 && this.body.Values[parameter].Kind == OwnershipValueKind.Alias)
            {
                parameter = this.body.ValueOperands[this.body.Values[parameter].Start];
            }

            this.SetValue(id, OwnershipValueKind.Alias, [parameter]);
        }

        var preparedDefaultPlace = this.defaultFunction is not null && place >= 0 && this.body.Places[place].Kind != OwnershipPlaceKind.Local;
        if (preparedDefaultPlace && kind is OwnershipOperationKind.Read or OwnershipOperationKind.Consume &&
            place >= 0 && ScalarResult(this.body.Places[place].Type))
        {
            // Prepared arguments are immutable acquired SSA values, including literal
            // temporaries and earlier defaults. They are not loadable caller locals.
            this.SetValue(id, OwnershipValueKind.Alias, [this.Value(place)]);
        }

        if (kind is OwnershipOperationKind.Write or OwnershipOperationKind.PayloadPlacement && input >= 0)
        {
            this.SetValue(id, OwnershipValueKind.Alias, [this.Value(input)]);
        }

        if (kind == OwnershipOperationKind.CallEntry && place >= 0 && (ScalarResult(this.body.Places[place].Type) || ReferenceTypes.IsString(this.body.Places[place].Type)))
        {
            this.SetValue(id, OwnershipValueKind.Alias, [this.Value(place)]);
        }

        if (kind is OwnershipOperationKind.Read or OwnershipOperationKind.Produce or OwnershipOperationKind.Consume)
        {
            var destination = kind == OwnershipOperationKind.Consume ? input : place;
            if (destination >= 0 && !(preparedDefaultPlace && kind == OwnershipOperationKind.Read))
            {
                this.placeValues[destination] = id;
            }
        }

        if (kind == OwnershipOperationKind.Borrow && input >= 0)
        {
            this.placeValues[input] = id;
            this.SetValue(id, OwnershipValueKind.Borrow, []);
        }

        if (kind == OwnershipOperationKind.Produce)
        {
            if (source is NullLiteralKoto)
            {
                // SPEC 5.1: the null address; lowering gives pointer constants their own operand form.
                this.SetValue(id, OwnershipValueKind.Constant, [], constant: 0);
            }
            else if (source is BoolLiteralKoto boolean)
            {
                this.SetValue(id, OwnershipValueKind.Constant, [], constant: boolean.Value ? 1 : 0);
            }
            else if (source is CharLiteralKoto { Value: { } scalar } && ReferenceEquals(source.BoundType, BoundType.Char))
            {
                this.SetValue(id, OwnershipValueKind.Constant, [], constant: scalar.Value);
            }
            else if (this.TryScalarLiteral(source, out var value))
            {
                this.SetValue(id, OwnershipValueKind.Constant, [], constant: value);
            }
        }
    }

    private int ComputeUpdate(Koto source, BoundType? type, int previous, int right, KotoKind operation)
    {
        var updated = this.Place(source, type, OwnershipPlaceKind.Temporary, true);
        this.Emit(OwnershipOperationKind.Produce, source, updated);
        this.RegisterTemporary(updated);
        this.SetValue(this.Value(updated), OwnershipValueKind.Binary, [previous, right], operation);
        return updated;
    }

    private int IncrementOne(Koto source)
    {
        var one = this.Temporary(source);
        var value = this.Value(one);
        this.SetValue(value, OwnershipValueKind.Constant, [], constant: 1);
        return value;
    }

    private int UpdateResult(Koto source, int previous, int updated)
    {
        var result = this.Temporary(source);
        if (source is UnaryKoto)
        {
            this.SetValue(this.Value(result), OwnershipValueKind.Alias, [source.Akind is KotoKind.PostfixIncrement or KotoKind.PostfixDecrement ? previous : this.Value(updated)]);
        }

        return result;
    }

    private int UnaryValue(UnaryKoto unary)
    {
        // Binding fits a directly signed literal once, including each signed minimum.
        if (unary is PrefixMinusKoto or PrefixPlusKoto && unary.Operand is NumberLiteralKoto)
        {
            return this.Temporary(unary);
        }

        if (ElementAccess.UpdateOperator(unary.Akind) != KotoKind.Invalid &&
            IsPointerPlace(KotoHelper.UnwrapParentheses(unary.Operand)))
        {
            return this.UpdatePointer(unary, KotoHelper.UnwrapParentheses(unary.Operand));
        }

        if (ElementAccess.UpdateOperator(unary.Akind) != KotoKind.Invalid &&
            KotoHelper.UnwrapParentheses(unary.Operand) is MemberAccessKoto field && ElementAccess.BorrowedPathRoot(field) is not null)
        {
            return this.UpdateBorrowedField(unary, field);
        }

        if (ElementAccess.UpdateOperator(unary.Akind) != KotoKind.Invalid &&
            KotoHelper.UnwrapParentheses(unary.Operand) is BinaryKoto element && ElementAccess.IsSyntax(element) && !this.SpecialField(element))
        {
            return this.UpdateElement(unary, element);
        }

        var input = this.Value(this.Expression(unary.Operand, PlaceUseKind.Read));
        if (input < 0)
        {
            return -1;
        }

        if (unary.Akind is KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus or KotoKind.PostfixIncrement or KotoKind.PostfixDecrement)
        {
            var updated = this.ComputeUpdate(unary, unary.BoundType, input, this.IncrementOne(unary), ElementAccess.UpdateOperator(unary.Akind));
            this.Emit(OwnershipOperationKind.Write, unary, this.Local(KotoHelper.UnwrapParentheses(unary.Operand)), updated);
            return this.UpdateResult(unary, input, updated);
        }

        var output = this.Temporary(unary);
        if (unary.Akind == KotoKind.PrefixPlus && FloatingTypes.Supports(unary.BoundType))
        {
            this.SetValue(this.Value(output), OwnershipValueKind.Alias, [input]);
            return output;
        }

        this.SetValue(this.Value(output), OwnershipValueKind.Unary, [input], unary.Akind);
        return output;
    }

    private int ConversionValue(ConversionKoto conversion)
    {
        var identity = conversion.ConversionBinding == ConversionBinding.Identity;
        var input = this.Expression(conversion.Left, identity ? PlaceUseKind.Consume : PlaceUseKind.Read);
        if (conversion.ConversionBinding == ConversionBinding.None)
        {
            this.Unsupported(conversion);
            return -1;
        }

        if (input < 0 || conversion.ConversionBinding == ConversionBinding.Abrupt)
        {
            return -1;
        }

        if (conversion.ConversionBinding == ConversionBinding.Literal)
        {
            return input;
        }

        if (identity)
        {
            // The ordinary acquisition above already secured the value. Retain its
            // semantic designation for validation without a second runtime transfer.
            (this.body.Identities ??= new()).Add(new(conversion, input));
            return input;
        }

        var output = this.Temporary(conversion);
        this.SetValue(this.Value(output), OwnershipValueKind.Convert, [this.Value(input)]);
        return output;
    }
}
