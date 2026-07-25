// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Parsing;

public static class LimitedValueHelper
{
    public static LimitedValue Evaluate(Compilation compilation, Koto koto)
    {
        if (koto is BoolLiteralKoto boolLiteralKoto)
        {// true, false
            return new(boolLiteralKoto.Value);
        }
        else if (koto is NumericLiteralKoto numericLiteralKoto)
        {// 1
        }
        else if (koto is StringLiteralKoto stringLiteralKoto)
        {// Text
            return new(stringLiteralKoto.Literal);
        }
        else if (koto is UnresolvedKoto unresolvedKoto)
        {// os value: bool
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
            if (koto is EqualsEqualsKoto)
            {// A == B
                if (left.Kind != right.Kind)
                {
                    koto.AddDiagnostic(Hashed.Kimi.ComparisonTypeMismatch);
                    goto Exit;
                }

                return left.Kind switch
                {
                    LimitedValueKind.Bool => new(left.Bool == right.Bool),
                    LimitedValueKind.I64 => new(left.I64 == right.I64),
                    LimitedValueKind.Double => new(left.Double == right.Double),
                    _ => new(true),
                };
            }
            else if (koto is ExclamationEqualsKoto exclamationEqualsKoto)
            {// A != B
                if (left.Kind != right.Kind)
                {
                    koto.AddDiagnostic(Hashed.Kimi.ComparisonTypeMismatch);
                    goto Exit;
                }

                return left.Kind switch
                {
                    LimitedValueKind.Bool => new(left.Bool != right.Bool),
                    LimitedValueKind.I64 => new(left.I64 != right.I64),
                    LimitedValueKind.Double => new(left.Double != right.Double),
                    _ => new(true),
                };
            }
            else if (koto is AndKoto andKoto)
            {
                if (left.Kind != LimitedValueKind.Bool ||
                    right.Kind != LimitedValueKind.Bool)
                {
                    goto NotSupported;
                }

                return new(left.Bool && right.Bool);
            }
        }

NotSupported:
        koto.AddDiagnostic(Hashed.Kimi.UnsupportedIfAttributeConditionType);

Exit:
        return new(true);
    }
}
