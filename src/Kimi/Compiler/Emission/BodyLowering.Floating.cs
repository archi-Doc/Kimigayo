// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private bool LowerFloating(OwnershipBody body, EmissionFunction function, int id, BoundType type, BoundType operandType, out string? failure)
    {
        failure = null;
        var value = body.Values[id];
        var op = value.Operator switch
        {
            KotoKind.Plus => "fadd",
            KotoKind.Minus => "fsub",
            KotoKind.Asterisk => "fmul",
            KotoKind.Slash => "fdiv",
            KotoKind.PrefixMinus => "fneg",
            KotoKind.EqualsEquals => "oeq",
            KotoKind.ExclamationEquals => "une",
            KotoKind.LessThan => "olt",
            KotoKind.LessThanEquals => "ole",
            KotoKind.GreaterThan => "ogt",
            KotoKind.GreaterThanEquals => "oge",
            _ => null,
        };
        var unary = value.Operator == KotoKind.PrefixMinus;
        var comparison = op is "oeq" or "une" or "olt" or "ole" or "ogt" or "oge";
        if (op is null || value.Kind != (unary ? OwnershipValueKind.Unary : OwnershipValueKind.Binary) || value.Count != (unary ? 1 : 2) ||
            !ReferenceEquals(type, comparison ? BoundType.Boolean : operandType) ||
            (!unary && !ReferenceEquals(operandType, ValueType(body, Input(body, id, 1)))) || this.checks[id] != ArithmeticCheckKind.None)
        {
            return Fail("Unsupported or inconsistent floating-point operation.", out failure);
        }

        var representation = WindowsLowering.GetValue(operandType)!;
        var left = this.PhysicalOperand(body, Input(body, id, 0));
        if (unary)
        {
            function.AddScalar(EmissionOpcode.Scalar, id, [left], representation.ComputationType, op, representation: representation);
        }
        else
        {
            function.AddScalar(EmissionOpcode.Scalar, id, [left, this.PhysicalOperand(body, Input(body, id, 1))], representation.ComputationType, op, comparison: comparison, representation: representation);
        }

        return true;
    }
}
