// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Helper;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

public static class BasicValueHelper
{
    public delegate BasicValue BasicValueHandler(Compilation compilation, Koto koto);

    private static readonly BasicValueHandler?[] HandlerTable = CreateHandlerTable();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BasicValue Evaluate(Compilation compilation, Koto koto)
    {
        var kind = (uint)koto.Akind;
        if (kind < (uint)HandlerTable.Length && HandlerTable[kind] is { } handler)
        {
            return handler(compilation, koto);
        }

        return AddNotSupportedDiagnostic(koto);
    }

    private static BasicValueHandler?[] CreateHandlerTable()
    {
        var table = new BasicValueHandler?[Koto.MaxKind];

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

    private static BasicValue EvaluateBoolLiteral(Compilation compilation, Koto koto)
    {// true, false
        return new(((BoolLiteralKoto)koto).Value);
    }

    private static BasicValue EvaluateNumberLiteral(Compilation compilation, Koto koto)
    {// long or double
        var literal = (NumberLiteralKoto)koto;
        if (literal.TryGetBasicValue(out var value))
        {
            return value;
        }

        return AddNotSupportedDiagnostic(koto);
    }

    private static BasicValue EvaluateStringLiteral(Compilation compilation, Koto koto)
    {// StringLiteral
        return new(((StringLiteralKoto)koto).Literal);
    }

    private static BasicValue EvaluateIdentifierName(Compilation compilation, Koto koto)
    {// IdentifierName
        if (compilation.TryResolveValue((IdentifierNameKoto)koto, out var value))
        {
            return value;
        }

        return AddNotSupportedDiagnostic(koto);
    }

    private static BasicValue EvaluateParenthesized(Compilation compilation, Koto koto)
    {// (A)
        return Evaluate(compilation, ((ParenthesizedKoto)koto).Operand);
    }

    private static BasicValue EvaluateNot(Compilation compilation, Koto koto)
    {// not A
        var operand = Evaluate(compilation, ((NotKoto)koto).Operand);
        if (operand.Kind == BasicValueKind.Invalid)
        {// Invalid value already has its diagnostic.
            return default;
        }

        if (operand.Kind == BasicValueKind.Bool)
        {
            return new(!operand.Bool);
        }

        return AddNotSupportedDiagnostic(koto);
    }

    private static BasicValue EvaluatePrefixPlus(Compilation compilation, Koto koto)
    {// +A
        var operand = Evaluate(compilation, ((PrefixPlusKoto)koto).Operand);
        if (operand.Kind == BasicValueKind.Invalid)
        {// Invalid value already has its diagnostic.
            return default;
        }

        if (operand.Kind is BasicValueKind.I64 or BasicValueKind.F64)
        {
            return operand;
        }

        return AddNotSupportedDiagnostic(koto);
    }

    private static BasicValue EvaluatePrefixMinus(Compilation compilation, Koto koto)
    {// -A
        var operand = Evaluate(compilation, ((PrefixMinusKoto)koto).Operand);
        if (operand.Kind == BasicValueKind.Invalid)
        {// Invalid value already has its diagnostic.
            return default;
        }

        if (operand.Kind == BasicValueKind.I64)
        {
            if (operand.I64 == long.MinValue)
            {
                return AddIntegerOverflowDiagnostic(koto);
            }

            return new(-operand.I64);
        }
        else if (operand.Kind == BasicValueKind.F64)
        {
            return new(-operand.F64);
        }

        return AddNotSupportedDiagnostic(koto);
    }

    private static BasicValue EvaluateAsterisk(Compilation compilation, Koto koto)
    {// A * B
        if (!TryEvaluateBinaryOperands(compilation, (BinaryKoto)koto, out var left, out var right))
        {
            return default;
        }

        if (left.Kind == BasicValueKind.I64)
        {
            var result = (Int128)left.I64 * right.I64;
            if (NumberLiteralHelper.IsInt64(result))
            {
                return new((long)result);
            }
            else
            {
                return AddIntegerOverflowDiagnostic(koto);
            }
        }
        else if (left.Kind == BasicValueKind.F64)
        {
            return new(left.F64 * right.F64);
        }

        return AddNotSupportedDiagnostic(koto);
    }

    private static BasicValue EvaluateSlash(Compilation compilation, Koto koto)
    {// A / B
        if (!TryEvaluateBinaryOperands(compilation, (BinaryKoto)koto, out var left, out var right))
        {
            return default;
        }

        if (left.Kind == BasicValueKind.I64)
        {
            if (right.I64 == 0)
            {
                return AddDivisionByZeroDiagnostic(koto);
            }
            else if (left.I64 == long.MinValue && right.I64 == -1)
            {
                return AddIntegerOverflowDiagnostic(koto);
            }

            return new(left.I64 / right.I64);
        }

        if (left.Kind == BasicValueKind.F64)
        {
            return new(left.F64 / right.F64);
        }

        return AddNotSupportedDiagnostic(koto);
    }

    private static BasicValue EvaluatePercent(Compilation compilation, Koto koto)
    {// A % B
        if (!TryEvaluateBinaryOperands(compilation, (BinaryKoto)koto, out var left, out var right))
        {
            return default;
        }

        if (left.Kind == BasicValueKind.I64)
        {
            if (right.I64 == 0)
            {
                return AddDivisionByZeroDiagnostic(koto);
            }

            if (right.I64 == -1)
            {
                return new(0L);
            }

            return new(left.I64 % right.I64);
        }

        return AddNotSupportedDiagnostic(koto);
    }

    private static BasicValue EvaluatePlus(Compilation compilation, Koto koto)
    {// A + B
        if (!TryEvaluateBinaryOperands(compilation, (BinaryKoto)koto, out var left, out var right))
        {
            return default;
        }

        if (left.Kind == BasicValueKind.I64)
        {
            var result = (Int128)left.I64 + right.I64;
            if (NumberLiteralHelper.IsInt64(result))
            {
                return new((long)result);
            }
            else
            {
                return AddIntegerOverflowDiagnostic(koto);
            }
        }
        else if (left.Kind == BasicValueKind.F64)
        {
            return new(left.F64 + right.F64);
        }

        return AddNotSupportedDiagnostic(koto);
    }

    private static BasicValue EvaluateMinus(Compilation compilation, Koto koto)
    {// A - B
        if (!TryEvaluateBinaryOperands(compilation, (BinaryKoto)koto, out var left, out var right))
        {
            return default;
        }

        if (left.Kind == BasicValueKind.I64)
        {
            var result = (Int128)left.I64 - right.I64;
            if (NumberLiteralHelper.IsInt64(result))
            {
                return new((long)result);
            }
            else
            {
                return AddIntegerOverflowDiagnostic(koto);
            }
        }
        else if (left.Kind == BasicValueKind.F64)
        {
            return new(left.F64 - right.F64);
        }

        return AddNotSupportedDiagnostic(koto);
    }

    private static BasicValue EvaluateLessThan(Compilation compilation, Koto koto)
    {// A < B
        if (!TryEvaluateBinaryOperands(compilation, (BinaryKoto)koto, out var left, out var right))
        {
            return default;
        }

        return left.Kind switch
        {
            BasicValueKind.I64 => new(left.I64 < right.I64),
            BasicValueKind.F64 => new(left.F64 < right.F64),
            _ => AddNotSupportedDiagnostic(koto),
        };
    }

    private static BasicValue EvaluateLessThanEquals(Compilation compilation, Koto koto)
    {// A <= B
        if (!TryEvaluateBinaryOperands(compilation, (BinaryKoto)koto, out var left, out var right))
        {
            return default;
        }

        return left.Kind switch
        {
            BasicValueKind.I64 => new(left.I64 <= right.I64),
            BasicValueKind.F64 => new(left.F64 <= right.F64),
            _ => AddNotSupportedDiagnostic(koto),
        };
    }

    private static BasicValue EvaluateGreaterThan(Compilation compilation, Koto koto)
    {// A > B
        if (!TryEvaluateBinaryOperands(compilation, (BinaryKoto)koto, out var left, out var right))
        {
            return default;
        }

        return left.Kind switch
        {
            BasicValueKind.I64 => new(left.I64 > right.I64),
            BasicValueKind.F64 => new(left.F64 > right.F64),
            _ => AddNotSupportedDiagnostic(koto),
        };
    }

    private static BasicValue EvaluateGreaterThanEquals(Compilation compilation, Koto koto)
    {// A >= B
        if (!TryEvaluateBinaryOperands(compilation, (BinaryKoto)koto, out var left, out var right))
        {
            return default;
        }

        return left.Kind switch
        {
            BasicValueKind.I64 => new(left.I64 >= right.I64),
            BasicValueKind.F64 => new(left.F64 >= right.F64),
            _ => AddNotSupportedDiagnostic(koto),
        };
    }

    private static BasicValue EvaluateEqualsEquals(Compilation compilation, Koto koto)
    {// A == B
        if (!TryEvaluateBinaryOperands(compilation, (BinaryKoto)koto, out var left, out var right))
        {
            return default;
        }

        return left.Kind switch
        {
            BasicValueKind.Bool => new(left.Bool == right.Bool),
            BasicValueKind.I64 => new(left.I64 == right.I64),
            BasicValueKind.F64 => new(left.F64 == right.F64),
            BasicValueKind.String => new(string.Equals(left.String, right.String, StringComparison.OrdinalIgnoreCase)),
            _ => AddNotSupportedDiagnostic(koto),
        };
    }

    private static BasicValue EvaluateExclamationEquals(Compilation compilation, Koto koto)
    {// A != B
        if (!TryEvaluateBinaryOperands(compilation, (BinaryKoto)koto, out var left, out var right))
        {
            return default;
        }

        return left.Kind switch
        {
            BasicValueKind.Bool => new(left.Bool != right.Bool),
            BasicValueKind.I64 => new(left.I64 != right.I64),
            BasicValueKind.F64 => new(left.F64 != right.F64),
            BasicValueKind.String => new(!string.Equals(left.String, right.String, StringComparison.OrdinalIgnoreCase)),
            _ => AddNotSupportedDiagnostic(koto),
        };
    }

    private static BasicValue EvaluateAnd(Compilation compilation, Koto koto)
    {// A and B
        var binary = (BinaryKoto)koto;
        var left = Evaluate(compilation, binary.Left);
        if (left.Kind == BasicValueKind.Invalid)
        {
            return default;
        }

        if (left.Kind != BasicValueKind.Bool)
        {
            return AddNotSupportedDiagnostic(koto);
        }

        if (!left.Bool)
        {
            return new(false);
        }

        var right = Evaluate(compilation, binary.Right);
        if (right.Kind == BasicValueKind.Invalid)
        {
            return default;
        }

        if (right.Kind != BasicValueKind.Bool)
        {
            return AddNotSupportedDiagnostic(koto);
        }

        return new(right.Bool);
    }

    private static BasicValue EvaluateOr(Compilation compilation, Koto koto)
    {// A or B
        var binary = (BinaryKoto)koto;
        var left = Evaluate(compilation, binary.Left);
        if (left.Kind == BasicValueKind.Invalid)
        {
            return default;
        }

        if (left.Kind != BasicValueKind.Bool)
        {
            return AddNotSupportedDiagnostic(koto);
        }

        if (left.Bool)
        {
            return new(true);
        }

        var right = Evaluate(compilation, binary.Right);
        if (right.Kind == BasicValueKind.Invalid)
        {
            return default;
        }

        if (right.Kind != BasicValueKind.Bool)
        {
            return AddNotSupportedDiagnostic(koto);
        }

        return new(right.Bool);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryEvaluateBinaryOperands(Compilation compilation, BinaryKoto koto, out BasicValue left, out BasicValue right)
    {
        left = Evaluate(compilation, koto.Left);
        right = Evaluate(compilation, koto.Right);

        if (left.Kind == BasicValueKind.Invalid || right.Kind == BasicValueKind.Invalid)
        {
            return false;
        }

        if (left.Kind == right.Kind)
        {
            return true;
        }

        koto.AddDiagnostic(KimiDiagnostic.TypeMismatch_Kd);
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static BasicValue AddIntegerOverflowDiagnostic(Koto koto)
    {
        koto.AddDiagnostic(KimiDiagnostic.IntegerOverflow_Kd);
        return default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static BasicValue AddDivisionByZeroDiagnostic(Koto koto)
    {
        koto.AddDiagnostic(KimiDiagnostic.DivisionByZero_Kd);
        return default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static BasicValue AddNotSupportedDiagnostic(Koto koto)
    {
        koto.AddDiagnostic(KimiDiagnostic.UnsupportedIfAttributeConditionType_Kd);
        return default;
    }
}
