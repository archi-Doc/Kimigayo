// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Parsing;

public static class LimitedValueHelper
{
    public delegate LimitedValue LimitedValueHandler(Compilation compilation, Koto koto);

    private static readonly LimitedValueHandler?[] HandlerTable = new LimitedValueHandler[Koto.MaxKind];

    static LimitedValueHelper()
    {
        HandlerTable[(int)KotoKind.BoolLiteral] = (compilation, koto)
            =>
        {
            return new(((BoolLiteralKoto)koto).Value);
        };
    }

    public static LimitedValue Evaluate2(Compilation compilation, Koto koto)
    {
        if (HandlerTable[(int)koto.Akind] is { } handler)
        {
            return handler(compilation, koto);
        }
        else
        {
            return new(true);
        }
    }

    public static LimitedValue Evaluate(Compilation compilation, Koto koto)
    {
        if (koto is BoolLiteralKoto boolLiteralKoto)
        {// true, false
            return new(boolLiteralKoto.Value);
        }
        else if (koto is NumberLiteralKoto numericLiteralKoto)
        {// long or double
            if (numericLiteralKoto.TryGetLimitedValue(out var lv))
            {
                return lv;
            }
            else
            {
                goto NotSupported;
            }
        }
        else if (koto is StringLiteralKoto stringLiteralKoto)
        {// Text
            return new(stringLiteralKoto.Literal);
        }
        else if (koto is IdentifierNameKoto unresolvedKoto)
        {// os value: bool
            if (compilation.TryResolveValue(unresolvedKoto, out var lv))
            {
                return lv;
            }
            else
            {
                goto NotSupported;
            }
        }
        else if (koto is ParenthesizedKoto parenthesizedKoto)
        {// (A)
            return Evaluate(compilation, parenthesizedKoto.Operand);
        }
        else if (koto is NotKoto notKoto)
        {// not A
            var op = Evaluate(compilation, notKoto.Operand);
            if (op.Kind == LimitedValueKind.Bool)
            {
                return new(op.Bool);
            }
            else
            {
                goto NotSupported;
            }
        }
        else if (koto is PrefixPlusKoto prefixPlusKoto)
        {// +A
            var op = Evaluate(compilation, prefixPlusKoto.Operand);
            if (op.Kind == LimitedValueKind.I64)
            {
                return new(op.I64);
            }
            else if (op.Kind == LimitedValueKind.Double)
            {
                return new(op.Double);
            }
            else
            {
                goto NotSupported;
            }
        }
        else if (koto is PrefixMinusKoto prefixMinusKoto)
        {// -A
            var op = Evaluate(compilation, prefixMinusKoto.Operand);
            if (op.Kind == LimitedValueKind.I64)
            {
                return new(-op.I64);
            }
            else if (op.Kind == LimitedValueKind.Double)
            {
                return new(-op.Double);
            }
            else
            {
                goto NotSupported;
            }
        }
        else if (koto is BinaryKoto binaryKoto)
        {// Binary operation
            var left = Evaluate(compilation, binaryKoto.Left);
            var right = Evaluate(compilation, binaryKoto.Right);
            if (left.Kind != right.Kind)
            {
                koto.AddDiagnostic(Hashed.Kimi.TypeMismatch);
                goto Exit;
            }

            if (koto is AsteriskKoto)
            {// A * B
                if (left.Kind == LimitedValueKind.I64)
                {
                    return new(left.I64 * right.I64);
                }
                else if (left.Kind == LimitedValueKind.Double)
                {
                    return new(left.Double * right.Double);
                }
            }
            else if (koto is SlashKoto)
            {// A / B
                if (left.Kind == LimitedValueKind.I64)
                {
                    return new(left.I64 / right.I64);
                }
                else if (left.Kind == LimitedValueKind.Double)
                {
                    return new(left.Double / right.Double);
                }
            }
            else if (koto is PercentKoto)
            {// A % B
                if (left.Kind == LimitedValueKind.I64)
                {
                    return new(left.I64 % right.I64);
                }
            }
            else if (koto is PlusKoto)
            {// A + B
                if (left.Kind == LimitedValueKind.I64)
                {
                    return new(left.I64 + right.I64);
                }
                else if (left.Kind == LimitedValueKind.Double)
                {
                    return new(left.Double + right.Double);
                }
            }
            else if (koto is MinusKoto)
            {// A - B
                if (left.Kind == LimitedValueKind.I64)
                {
                    return new(left.I64 - right.I64);
                }
                else if (left.Kind == LimitedValueKind.Double)
                {
                    return new(left.Double - right.Double);
                }
            }
            else if (koto is LessThanKoto)
            {// A < B
                if (left.Kind == LimitedValueKind.I64)
                {
                    return new(left.I64 < right.I64);
                }
                else if (left.Kind == LimitedValueKind.Double)
                {
                    return new(left.Double < right.Double);
                }
            }
            else if (koto is LessThanEqualsKoto)
            {// A <= B
                if (left.Kind == LimitedValueKind.I64)
                {
                    return new(left.I64 <= right.I64);
                }
                else if (left.Kind == LimitedValueKind.Double)
                {
                    return new(left.Double <= right.Double);
                }
            }
            else if (koto is GreaterThanKoto)
            {// A > B
                if (left.Kind == LimitedValueKind.I64)
                {
                    return new(left.I64 > right.I64);
                }
                else if (left.Kind == LimitedValueKind.Double)
                {
                    return new(left.Double > right.Double);
                }
            }
            else if (koto is GreaterThanEqualsKoto)
            {// A >= B
                if (left.Kind == LimitedValueKind.I64)
                {
                    return new(left.I64 >= right.I64);
                }
                else if (left.Kind == LimitedValueKind.Double)
                {
                    return new(left.Double >= right.Double);
                }
            }
            else if (koto is EqualsEqualsKoto)
            {// A == B
                return left.Kind switch
                {
                    LimitedValueKind.Bool => new(left.Bool == right.Bool),
                    LimitedValueKind.I64 => new(left.I64 == right.I64),
                    LimitedValueKind.Double => new(left.Double == right.Double),
                    LimitedValueKind.Text => new(string.Equals(left.Text, right.Text, StringComparison.OrdinalIgnoreCase)),
                    _ => new(true),
                };
            }
            else if (koto is ExclamationEqualsKoto exclamationEqualsKoto)
            {// A != B
                return left.Kind switch
                {
                    LimitedValueKind.Bool => new(left.Bool != right.Bool),
                    LimitedValueKind.I64 => new(left.I64 != right.I64),
                    LimitedValueKind.Double => new(left.Double != right.Double),
                    LimitedValueKind.Text => new(!string.Equals(left.Text, right.Text, StringComparison.OrdinalIgnoreCase)),
                    _ => new(true),
                };
            }
            else if (koto is AndKoto andKoto)
            {// A and B
                if (left.Kind != LimitedValueKind.Bool ||
                    right.Kind != LimitedValueKind.Bool)
                {
                    goto NotSupported;
                }

                return new(left.Bool && right.Bool);
            }
            else if (koto is OrKoto orKoto)
            {// A or B
                if (left.Kind != LimitedValueKind.Bool ||
                    right.Kind != LimitedValueKind.Bool)
                {
                    goto NotSupported;
                }

                return new(left.Bool || right.Bool);
            }
        }

NotSupported:
        koto.AddDiagnostic(Hashed.Kimi.UnsupportedIfAttributeConditionType);

Exit:
        return new(true);
    }

    private static LimitedValue AddNotSupportedDiagnostic(Koto koto)
    {
        koto.AddDiagnostic(Hashed.Kimi.UnsupportedIfAttributeConditionType);
        return new(true);
    }
}
