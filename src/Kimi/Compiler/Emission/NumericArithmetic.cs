// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// One numeric instruction/check policy for source operators and intrinsic Contract witnesses.
internal static class NumericArithmetic
{
    internal static ArithmeticCheckKind Check(KotoKind operation, BoundType type)
        => type.IsFloatingPoint ? ArithmeticCheckKind.None : operation switch
        {
            KotoKind.Slash when ScalarTypes.Signed(type) => type.IsWrappingInteger ? ArithmeticCheckKind.WrappingDivision : ArithmeticCheckKind.Division,
            KotoKind.Slash or KotoKind.Percent => ArithmeticCheckKind.DivisionZero,
            KotoKind.Plus or KotoKind.Minus or KotoKind.Asterisk or KotoKind.PrefixMinus => type.IsWrappingInteger ? ArithmeticCheckKind.None : ArithmeticCheckKind.Overflow,
            _ => ArithmeticCheckKind.None,
        };

    internal static string? Instruction(KotoKind operation, BoundType type)
        => type.IsFloatingPoint ? operation switch
        {
            KotoKind.Plus => "fadd",
            KotoKind.Minus => "fsub",
            KotoKind.Asterisk => "fmul",
            KotoKind.Slash => "fdiv",
            KotoKind.PrefixMinus => "fneg",
            _ => null,
        } : operation switch
        {
            KotoKind.Plus => type.IsWrappingInteger ? "add" : ScalarTypes.Signed(type) ? "sadd" : "uadd",
            KotoKind.Minus or KotoKind.PrefixMinus => type.IsWrappingInteger ? "sub" : ScalarTypes.Signed(type) ? "ssub" : "usub",
            KotoKind.Asterisk => type.IsWrappingInteger ? "mul" : ScalarTypes.Signed(type) ? "smul" : "umul",
            KotoKind.Slash => ScalarTypes.Signed(type) ? "sdiv" : "udiv",
            KotoKind.Percent => ScalarTypes.Signed(type) ? "srem" : "urem",
            _ => null,
        };
}
