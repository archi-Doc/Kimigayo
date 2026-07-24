// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;

namespace Kimi.Compiler.Parsing;

public enum LimitedValueKind
{
    Bool,
    I32,
    Single,

}

[StructLayout(LayoutKind.Explicit)]
public readonly struct LimitedValue
{
    [FieldOffset(0)]
    public readonly LimitedValueKind Kind;

    [FieldOffset(4)]
    public readonly bool Bool;

    [FieldOffset(4)]
    public readonly int I32;

    [FieldOffset(4)]
    public readonly float Single;

    public LimitedValue(bool value)
    {
        this.Kind = LimitedValueKind.Bool;
        this.Bool = value;
    }

    public LimitedValue(int value)
    {
        this.Kind = LimitedValueKind.I32;
        this.I32 = value;
    }

    public LimitedValue(float value)
    {
        this.Kind = LimitedValueKind.Single;
        this.Single = value;
    }
}

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
        else if (koto is UnresolvedKoto unresolvedKoto)
        {// os value: bool
        }
        else if (koto is ParenthesizedKoto parenthesizedKoto)
        {// (A)
            var op = Evaluate(compilation, parenthesizedKoto.Operand);
            return op;
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
            if (op.Kind == LimitedValueKind.I32)
            {
                return new(op.I32);
            }
            else if (op.Kind == LimitedValueKind.Single)
            {
                return new(op.Single);
            }
            else
            {
                goto NotSupported;
            }
        }
        else if (koto is PrefixMinusKoto prefixMinusKoto)
        {// -A
            var op = Evaluate(compilation, prefixMinusKoto.Operand);
            if (op.Kind == LimitedValueKind.I32)
            {
                return new(-op.I32);
            }
            else if (op.Kind == LimitedValueKind.Single)
            {
                return new(-op.Single);
            }
            else
            {
                goto NotSupported;
            }
        }
        else if (koto is BinaryKoto binaryKoto)
        {
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
                    LimitedValueKind.I32 => new(left.I32 == right.I32),
                    LimitedValueKind.Single => new(left.Single == right.Single),
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
                    LimitedValueKind.I32 => new(left.I32 != right.I32),
                    LimitedValueKind.Single => new(left.Single != right.Single),
                    _ => new(true),
                };
            }
        }

NotSupported:
        koto.AddDiagnostic(Hashed.Kimi.UnsupportedIfAttributeConditionType);

Exit:
        return new(true);
    }
}
