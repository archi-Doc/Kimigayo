// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;

namespace Kimi.Compiler.Parsing;

public static class LimitedValueHelper
{
    public delegate LimitedValue LimitedValueHandler(Compilation compilation, Koto koto);

    private static readonly LimitedValueHandler?[] HandlerTable = CreateHandlerTable();

    static LimitedValueHelper()
    {
        HandlerTable[(int)KotoKind.BoolLiteral] = (compilation, koto)
            =>
        {
            return new(((BoolLiteralKoto)koto).Value);
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LimitedValue Evaluate(Compilation compilation, Koto koto)
    {
        var kind = (uint)koto.Akind;
        if (kind < (uint)HandlerTable.Length && HandlerTable[kind] is { } handler)
        {
            return handler(compilation, koto);
        }

        return AddNotSupportedDiagnostic(koto);
    }

    private static LimitedValueHandler?[] CreateHandlerTable()
    {//
        var table = new LimitedValueHandler?[Koto.MaxKind];

        table[(int)KotoKind.BoolLiteral] = EvaluateBoolLiteral;
        table[(int)KotoKind.NumberLiteral] = EvaluateNumberLiteral;
        table[(int)KotoKind.StringLiteral] = EvaluateStringLiteral;
        table[(int)KotoKind.IdentifierName] = EvaluateIdentifierName;
        table[(int)KotoKind.Parenthesized] = EvaluateParenthesized;

        table[(int)KotoKind.Not] = EvaluateNot;
        table[(int)KotoKind.PrefixPlus] = EvaluatePrefixPlus;
        table[(int)KotoKind.PrefixMinus] = EvaluatePrefixMinus;

        table[(int)KotoKind.Asterisk] = EvaluateAsterisk;
        table[(int)KotoKind.Slash] = EvaluateSlash;
        table[(int)KotoKind.Percent] = EvaluatePercent;
        table[(int)KotoKind.Plus] = EvaluatePlus;
        table[(int)KotoKind.Minus] = EvaluateMinus;

        table[(int)KotoKind.LessThan] = EvaluateLessThan;
        table[(int)KotoKind.LessThanEquals] = EvaluateLessThanEquals;
        table[(int)KotoKind.GreaterThan] = EvaluateGreaterThan;
        table[(int)KotoKind.GreaterThanEquals] = EvaluateGreaterThanEquals;
        table[(int)KotoKind.EqualsEquals] = EvaluateEqualsEquals;
        table[(int)KotoKind.ExclamationEquals] = EvaluateExclamationEquals;

        table[(int)KotoKind.And] = EvaluateAnd;
        table[(int)KotoKind.Or] = EvaluateOr;

        return table;
    }

    private static LimitedValue EvaluateBoolLiteral(Compilation compilation, Koto koto)
    {// true, false
        return new(((BoolLiteralKoto)koto).Value);
    }

    private static LimitedValue EvaluateNumberLiteral(Compilation compilation, Koto koto)
    {// long or double
        var literal = (NumberLiteralKoto)koto;
        if (literal.TryGetLimitedValue(out var value))
        {
            return value;
        }

        return AddNotSupportedDiagnostic(koto);
    }

    private static LimitedValue EvaluateStringLiteral(Compilation compilation, Koto koto)
    {// StringLiteral
        return new(((StringLiteralKoto)koto).Literal);
    }

    private static LimitedValue EvaluateIdentifierName(Compilation compilation, Koto koto)
    {// IdentifierName
        if (compilation.TryResolveValue((IdentifierNameKoto)koto, out var value))
        {
            return value;
        }

        return AddNotSupportedDiagnostic(koto);
    }

    private static LimitedValue EvaluateParenthesized(Compilation compilation, Koto koto)
    {// (A)
        return Evaluate(compilation, ((ParenthesizedKoto)koto).Operand);
    }

    private static LimitedValue EvaluateNot(Compilation compilation, Koto koto)
    {// not A
        var operand = Evaluate(compilation, ((NotKoto)koto).Operand);
        if (operand.Kind == LimitedValueKind.Bool)
        {
            return new(!operand.Bool);
        }

        return AddNotSupportedDiagnostic(koto);
    }

    private static LimitedValue EvaluatePrefixPlus(Compilation compilation, Koto koto)
    {// +A
        var operand = Evaluate(compilation, ((PrefixPlusKoto)koto).Operand);
        if (operand.Kind is LimitedValueKind.I64 or LimitedValueKind.Double)
        {
            return operand;
        }

        return AddNotSupportedDiagnostic(koto);
    }

    private static LimitedValue EvaluatePrefixMinus(Compilation compilation, Koto koto)
    {// -A
        var operand = Evaluate(compilation, ((PrefixMinusKoto)koto).Operand);

        return operand.Kind switch
        {
            LimitedValueKind.I64 => new(-operand.I64),
            LimitedValueKind.Double => new(-operand.Double),
            _ => AddNotSupportedDiagnostic(koto),
        };
    }

    private static LimitedValue EvaluateAsterisk(Compilation compilation, Koto koto)
    {// A * B
        if (!TryEvaluateBinaryOperands(compilation, (BinaryKoto)koto, out var left, out var right))
        {
            return new(true);
        }

        return left.Kind switch
        {
            LimitedValueKind.I64 => new(left.I64 * right.I64),
            LimitedValueKind.Double => new(left.Double * right.Double),
            _ => AddNotSupportedDiagnostic(koto),
        };
    }

    private static LimitedValue EvaluateSlash(Compilation compilation, Koto koto)
    {// A / B
        if (!TryEvaluateBinaryOperands(compilation, (BinaryKoto)koto, out var left, out var right))
        {
            return new(true);
        }

        return left.Kind switch
        {
            LimitedValueKind.I64 => new(left.I64 / right.I64),
            LimitedValueKind.Double => new(left.Double / right.Double),
            _ => AddNotSupportedDiagnostic(koto),
        };
    }

    private static LimitedValue EvaluatePercent(Compilation compilation, Koto koto)
    {// A % B
        if (!TryEvaluateBinaryOperands(compilation, (BinaryKoto)koto, out var left, out var right))
        {
            return new(true);
        }

        if (left.Kind == LimitedValueKind.I64)
        {
            return new(left.I64 % right.I64);
        }

        return AddNotSupportedDiagnostic(koto);
    }

    private static LimitedValue EvaluatePlus(Compilation compilation, Koto koto)
    {// A + B
        if (!TryEvaluateBinaryOperands(compilation, (BinaryKoto)koto, out var left, out var right))
        {
            return new(true);
        }

        return left.Kind switch
        {
            LimitedValueKind.I64 => new(left.I64 + right.I64),
            LimitedValueKind.Double => new(left.Double + right.Double),
            _ => AddNotSupportedDiagnostic(koto),
        };
    }

    private static LimitedValue EvaluateMinus(Compilation compilation, Koto koto)
    {// A - B
        if (!TryEvaluateBinaryOperands(compilation, (BinaryKoto)koto, out var left, out var right))
        {
            return new(true);
        }

        return left.Kind switch
        {
            LimitedValueKind.I64 => new(left.I64 - right.I64),
            LimitedValueKind.Double => new(left.Double - right.Double),
            _ => AddNotSupportedDiagnostic(koto),
        };
    }

    private static LimitedValue EvaluateLessThan(Compilation compilation, Koto koto)
    {// A < B
        if (!TryEvaluateBinaryOperands(compilation, (BinaryKoto)koto, out var left, out var right))
        {
            return new(true);
        }

        return left.Kind switch
        {
            LimitedValueKind.I64 => new(left.I64 < right.I64),
            LimitedValueKind.Double => new(left.Double < right.Double),
            _ => AddNotSupportedDiagnostic(koto),
        };
    }

    private static LimitedValue EvaluateLessThanEquals(Compilation compilation, Koto koto)
    {// A <= B
        if (!TryEvaluateBinaryOperands(compilation, (BinaryKoto)koto, out var left, out var right))
        {
            return new(true);
        }

        return left.Kind switch
        {
            LimitedValueKind.I64 => new(left.I64 <= right.I64),
            LimitedValueKind.Double => new(left.Double <= right.Double),
            _ => AddNotSupportedDiagnostic(koto),
        };
    }

    private static LimitedValue EvaluateGreaterThan(Compilation compilation, Koto koto)
    {// A > B
        if (!TryEvaluateBinaryOperands(compilation, (BinaryKoto)koto, out var left, out var right))
        {
            return new(true);
        }

        return left.Kind switch
        {
            LimitedValueKind.I64 => new(left.I64 > right.I64),
            LimitedValueKind.Double => new(left.Double > right.Double),
            _ => AddNotSupportedDiagnostic(koto),
        };
    }

    private static LimitedValue EvaluateGreaterThanEquals(Compilation compilation, Koto koto)
    {// A >= B
        if (!TryEvaluateBinaryOperands(compilation, (BinaryKoto)koto, out var left, out var right))
        {
            return new(true);
        }

        return left.Kind switch
        {
            LimitedValueKind.I64 => new(left.I64 >= right.I64),
            LimitedValueKind.Double => new(left.Double >= right.Double),
            _ => AddNotSupportedDiagnostic(koto),
        };
    }

    private static LimitedValue EvaluateEqualsEquals(Compilation compilation, Koto koto)
    {// A == B
        if (!TryEvaluateBinaryOperands(compilation, (BinaryKoto)koto, out var left, out var right))
        {
            return new(true);
        }

        return left.Kind switch
        {
            LimitedValueKind.Bool => new(left.Bool == right.Bool),
            LimitedValueKind.I64 => new(left.I64 == right.I64),
            LimitedValueKind.Double => new(left.Double == right.Double),
            LimitedValueKind.Text => new(string.Equals(left.Text, right.Text, StringComparison.OrdinalIgnoreCase)),
            _ => AddNotSupportedDiagnostic(koto),
        };
    }

    private static LimitedValue EvaluateExclamationEquals(Compilation compilation, Koto koto)
    {// A != B
        if (!TryEvaluateBinaryOperands(compilation, (BinaryKoto)koto, out var left, out var right))
        {
            return new(true);
        }

        return left.Kind switch
        {
            LimitedValueKind.Bool => new(left.Bool != right.Bool),
            LimitedValueKind.I64 => new(left.I64 != right.I64),
            LimitedValueKind.Double => new(left.Double != right.Double),
            LimitedValueKind.Text => new(!string.Equals(left.Text, right.Text, StringComparison.OrdinalIgnoreCase)),
            _ => AddNotSupportedDiagnostic(koto),
        };
    }

    private static LimitedValue EvaluateAnd(Compilation compilation, Koto koto)
    {// A and B
        if (!TryEvaluateBinaryOperands(compilation, (BinaryKoto)koto, out var left, out var right))
        {
            return new(true);
        }

        if (left.Kind == LimitedValueKind.Bool)
        {
            return new(left.Bool && right.Bool);
        }

        return AddNotSupportedDiagnostic(koto);
    }

    private static LimitedValue EvaluateOr(Compilation compilation, Koto koto)
    {// A or B
        if (!TryEvaluateBinaryOperands(compilation, (BinaryKoto)koto, out var left, out var right))
        {
            return new(true);
        }

        if (left.Kind == LimitedValueKind.Bool)
        {
            return new(left.Bool || right.Bool);
        }

        return AddNotSupportedDiagnostic(koto);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryEvaluateBinaryOperands(Compilation compilation, BinaryKoto koto, out LimitedValue left, out LimitedValue right)
    {
        left = Evaluate(compilation, koto.Left);
        right = Evaluate(compilation, koto.Right);
        if (left.Kind == right.Kind)
        {
            return true;
        }

        koto.AddDiagnostic(Hashed.Kimi.TypeMismatch);
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static LimitedValue AddNotSupportedDiagnostic(Koto koto)
    {
        koto.AddDiagnostic(Hashed.Kimi.UnsupportedIfAttributeConditionType);
        return new(true);
    }

    public static LimitedValue Evaluate2(Compilation compilation, Koto koto)
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
            return Evaluate2(compilation, parenthesizedKoto.Operand);
        }
        else if (koto is NotKoto notKoto)
        {// not A
            var op = Evaluate2(compilation, notKoto.Operand);
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
            var op = Evaluate2(compilation, prefixPlusKoto.Operand);
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
            var op = Evaluate2(compilation, prefixMinusKoto.Operand);
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
            var left = Evaluate2(compilation, binaryKoto.Left);
            var right = Evaluate2(compilation, binaryKoto.Right);
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
}
