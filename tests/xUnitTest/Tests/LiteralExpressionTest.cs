// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 12.3.1: a literal-only expression is fitted to candidates, expected Types, comparison operands and branch
/// results as one literal; its operators then evaluate in the fitted Type at run time, and a shift count is typed
/// independently of the shifted operand.</summary>
public class LiteralExpressionTest
{
    private const string Fitting =
        "func widen(value: i64) -> i64 => value\nfunc pick(value: u8) -> u8 => value\n" +
        "func take(value: i32) -> string => \"value\"\nfunc take(value: ref/i64) -> string => \"borrow\"\n" +
        "let x: i64 = 7\n" +
        "require widen(2 * 5) == 10 and widen(-(3) + 1) == -2 else => $abort(\"candidate\")\n" +
        "require pick(200 + 55) == 255 and pick(1 << 7) == 128 else => $abort(\"u8\")\n" +
        "require x == 2 * 5 + -3 and 2 * 5 - 3 == x and (3 << 2) + x == 19 else => $abort(\"comparison\")\n" +
        "let wide: i64 = 1 << 40\nrequire wide == 1099511627776 else => $abort(\"shift\")\n" +
        "let r = if x > 0 => 2 * 3 else => x\nrequire r == 6 else => $abort(\"branch\")\n" +
        "require take(1 + 1) == \"value\" else => $abort(\"class\")\n" +
        "let n = 4 * 4\nlet m: i64 = n@i64 + (1 << 2)\nrequire m == 20 else => $abort(\"default\")\n" +
        "let buffer: [(2 * 3) of u8] = [(2 * 3) of 0]\nrequire buffer.length == 6 else => $abort(\"length\")\n" +
        "Console.writeLine(\"ok\")";

    [Fact]
    public void FitsLiteralOnlyExpressionsAsLiterals()
        => ScalarEmissionTest.EmitFixture("LiteralExpressionFitting", Fitting, "ok\n");

    [Fact]
    public void OperatorsEvaluateInTheFittedTypeAtRunTime()
        => ScalarEmissionTest.EmitFixture(
            "LiteralExpressionOverflow",
            "Console.writeLine(\"before\")\nlet a: u8 = 200 + 100\nConsole.writeLine(\"after\")",
            "before\n",
            1,
            "Hello.kimi:2:13: abort KIMI_E_INT_OVERFLOW: Integer overflow\n");

    [Fact]
    public void ShiftCountIsTypedIndependently()
        => ScalarEmissionTest.EmitFixture(
            "LiteralExpressionShiftCount",
            "Console.writeLine(\"before\")\nlet bad: u8 = 1 << 256\nConsole.writeLine(\"after\")",
            "before\n",
            1,
            "Hello.kimi:2:15: abort KIMI_E_INT_SHIFT_COUNT: Shift count out of range\n");

    [Theory]
    [InlineData("func choose(value: i32) -> () => ()\nfunc choose(value: i64) -> () => ()\nchoose(2 * 5)")]
    [InlineData("let a: u8 = -(1)")]
    [InlineData("let a: u8 = 256 + 0")]
    [InlineData("let a: f64 = 1 + 2")]
    [InlineData("let invalid: [(4 / 0) of u8] = []")]
    [InlineData("let x = 10\nmatch x\n    2 * 5 => ()\n    _ => ()")]
    public void RejectsWhatLiteralFittingDoesNotAdmit(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete && TestDiagnostics.Of(c, "Hello.kimi").Length == 0, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void NumericConversionTypesItsOperandIndependently()
        => ScalarEmissionTest.EmitFixture(
            "LiteralExpressionConversion",
            "Console.writeLine(\"before\")\nlet a = (200 + 100)@u8\nConsole.writeLine(\"after\")",
            "before\n",
            1,
            "Hello.kimi:2:9: abort KIMI_E_INT_CONVERSION: Integer conversion out of range\n");
}
