// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class IntegerEmissionTest
{
    private const string Overflow = "KIMI_E_INT_OVERFLOW: Integer overflow";
    private const string DivisionZero = "KIMI_E_INT_DIV_ZERO: Integer division or remainder by zero";
    private const string ShiftCount = "KIMI_E_INT_SHIFT_COUNT: Shift count out of range";
    private const string Mixed = "func small(x: i8, y: u8, z: i16, w: u16, a: u64, b: isize) -> u64\n    if x == -128 and y == 200 and z == -32768 and w == 65535 and b == -1 => return a\n    return 0\npublic func main()\n    var n: u8 = 7\n    var u: u64 = 1\n    var s: i16 = -128\n    defer => n = 0\n    let x = if true => u << n else => u\n    if x == 128 and (s >> n) == -1 and small(-128, 200, -32768, 65535, 18446744073709551615, -1) == 18446744073709551615\n        writeLine(\"ok\")";

    public static TheoryData<string, int, bool, string, string> Types => new()
    {
        { "i8", 8, true, "-128", "127" }, { "u8", 8, false, "0", "255" },
        { "i16", 16, true, "-32768", "32767" }, { "u16", 16, false, "0", "65535" },
        { "i32", 32, true, "-2147483648", "2147483647" }, { "u32", 32, false, "0", "4294967295" },
        { "i64", 64, true, "-9223372036854775808", "9223372036854775807" }, { "u64", 64, false, "0", "18446744073709551615" },
        { "isize", 64, true, "-9223372036854775808", "9223372036854775807" }, { "usize", 64, false, "0", "18446744073709551615" },
    };

    [Theory]
    [MemberData(nameof(Types))]
    public void EveryIntegerWidthPreservesValuesAcrossOperationsAndCalls(string type, int width, bool signed, string minimum, string maximum)
    {
        var source = $"func id(x: {type}) -> {type} => x\nfunc snapshot(x: {type}) -> {type}\n    var y = x\n    defer => y = 0\n    return y\n" +
            $"var x: {type} = 9\nvar y: {type} = 2\nlet q = x / y\nlet r = x % y\nx += 3\nx -= 2\nx *= 2\nx /= 2\nx %= 7\nx |= 4\nx &= 6\nx ^= 3\nx <<= 1\nx >>= 1\nlet old = x++\n++x\nlet current = --x\nx--\n" +
            $"let low: {type} = {minimum}\nlet high: {type} = {maximum}\nlet top: {type} = 1 << {width - 1}\nvar c = true\nlet phi = if c => id(high) else => id(low)\n" +
            $"if q == 4 and r == 1 and x == 5 and old == 5 and current == 6 and low < high and high > low and high >= high and low <= low and high / 1 == high and high % 1 == 0 and low / 1 == low and low % 1 == 0 and (top >> {width - 1}) == {(signed ? "-1" : "1")} and {(signed ? "top == low" : "top > 1 and top < high")} and snapshot(high) == high and phi == high\n    writeLine(\"ok\")";
        ScalarEmissionTest.EmitFixture("IntegerValues" + type, source, "ok\n");
    }

    [Theory]
    [MemberData(nameof(Types))]
    public void CheckedBoundariesAbortBeforeWriting(string type, int width, bool signed, string minimum, string maximum)
    {
        _ = width;
        var failures = new (string Name, string Initial, string Expression, string Reason)[]
        {
            ("Add", maximum, "x += 1", Overflow), ("Subtract", minimum, "x -= 1", Overflow),
            ("Multiply", maximum, "x *= 2", Overflow), ("Increment", maximum, "x++", Overflow),
            ("Decrement", minimum, "x--", Overflow), ("DivideZero", "1", "x /= 0", DivisionZero),
            ("RemainderZero", "1", "x %= 0", DivisionZero),
        };
        foreach (var failure in failures)
        {
            Abort("IntegerFail" + type + failure.Name, $"var x: {type} = {failure.Initial}\n{failure.Expression}\nwriteLine(\"bad\")", failure.Reason, 2, 1);
        }

        if (signed)
        {
            Abort("IntegerFail" + type + "Negate", $"var x: {type} = {minimum}\n-x", Overflow, 2, 1);
            Abort("IntegerFail" + type + "DivideMinimum", $"var x: {type} = {minimum}\nx /= -1", Overflow, 2, 1);
            Abort("IntegerFail" + type + "RemainderMinimum", $"var x: {type} = {minimum}\nx %= -1", Overflow, 2, 1);
        }
    }

    [Theory]
    [InlineData("IntegerMixedAbi", Mixed, "ok\n")]
    [InlineData("IntegerShiftNarrowCount", "var x: u64 = 1\nvar n: i8 = 63\nif (x << n) == 9223372036854775808 => writeLine(\"ok\")", "ok\n")]
    [InlineData("IntegerShiftWideCount", "var x: u8 = 128\nvar n: u64 = 7\nlet y = if true => x >> n else => x << n\nif y == 1 => writeLine(\"ok\")", "ok\n")]
    [InlineData("IntegerSignedShift", "var x: i8 = -128\nvar n: u64 = 7\nif (x >> n) == -1 => writeLine(\"ok\")", "ok\n")]
    [InlineData("IntegerUnsignedHighDivision", "var x: u64 = 18446744073709551615\nvar y: u64 = 9223372036854775808\nif x / y == 1 and x % y == 9223372036854775807 and y / 2 == 4611686018427387904 => writeLine(\"ok\")", "ok\n")]
    [InlineData("IntegerSignedDivisionSigns", "var x: i64 = -7\nvar y: i64 = -3\nif x / y == 2 and x % y == -1 => writeLine(\"ok\")", "ok\n")]
    [InlineData("IntegerSkippedChecks", "if false\n    let x: u8 = 255 + 1\nvar x: u64 = 1\nvar n: i8 = -1\nif true or (x << n) == 0 => writeLine(\"ok\")", "ok\n")]
    [InlineData("IntegerLiteralUnsignedZeroNegation", "let x: u8 = -0\nif x == 0 => writeLine(\"ok\")", "ok\n")]
    public void MixedWidthsAndSourceOrderExecute(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture(name, source, stdout);

    [Theory]
    [InlineData("u8", "u64", "256")]
    [InlineData("u8", "u64", "18446744073709551615")]
    [InlineData("u64", "i8", "-1")]
    [InlineData("u64", "u8", "64")]
    [InlineData("i16", "i64", "-9223372036854775808")]
    [InlineData("i32", "u64", "4294967296")]
    public void InvalidCountsAreCheckedBeforeConversion(string type, string countType, string count)
        => Abort($"IntegerShiftFail{type}{countType}{count}", $"var x: {type} = 1\nlet n: {countType} = {count}\nx <<= n", ShiftCount, 3, 1);

    [Theory]
    [InlineData("IntegerLiteralOverflow", "let x: u8 = 255 + 1", Overflow, 1, 13)]
    [InlineData("IntegerUnsignedUnderflow", "var x: u64 = 1\n0 - x", Overflow, 2, 1)]
    [InlineData("IntegerBeforeCleanup", "func f() -> u8\n    defer => writeLine(\"bad\")\n    return 255 + 1\nf()", Overflow, 3, 12)]
    [InlineData("IntegerDuringCleanup", "defer => writeLine(\"bad\")\ndefer\n    var x: i8 = -128\n    x /= -1", Overflow, 4, 5)]
    public void LiteralAndDeferredChecksRetainRuntimeFailure(string name, string source, string reason, int line, int column)
        => Abort(name, source, reason, line, column);

    [Theory]
    [InlineData("var x: u8 = 0\n-x", false)]
    [InlineData("var x: u64 = 1\n-x", false)]
    [InlineData("var x: u8 = 1\nx << -1", false)]
    [InlineData("var x: i8 = 1\nx << 200", false)]
    [InlineData("let x: u64 = 18446744073709551616", false)]
    [InlineData("let x: i64 = -9223372036854775809", false)]
    [InlineData("let x: i128 = 1", true)]
    [InlineData("if false\n    let x: u128 = 1", true)]
    [InlineData("func unused(x: i128) -> i128 => x\n()", true)]
    [InlineData("func unused() -> ()\n    let x: i128 = 1\n()", true)]
    [InlineData("let x = 'a'", true)]
    public void StaticErrorsAndUnsupportedWidthsNeverWriteIr(string source, bool bound)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(bound, c.Binding.Result.IsComplete);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Theory]
    [InlineData("i8", 128L)]
    [InlineData("u8", 200L)] // Valid mathematical u8, but not the canonical signed-extended payload (-56).
    [InlineData("u8", 300L)]
    [InlineData("i16", -32769L)]
    [InlineData("u16", 65536L)]
    [InlineData("i32", 2147483648L)]
    [InlineData("u32", 4294967296L)]
    public void NoncanonicalConstantPlansAreRejectedBeforeLlvmCanTruncate(string type, long bits)
    {
        var c = MinimalEmissionTest.Analyze($"let x: {type} = 1");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies[0];
        var id = body.Values.FindIndex(x => x.Kind == OwnershipValueKind.Constant);
        body.Values[id] = body.Values[id] with { Constant = bits };
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        c.Ownership.Analyze();
        Assert.True(c.Emission.Validate(out error), error);
    }

    [Fact]
    public void MixedAbiUsesNarrowSsaAndCanonicalConstants()
    {
        var ir = ScalarEmissionTest.EmitFixture("IntegerAbiStructure", Mixed, "ok\n");
        Assert.Contains("i8 -128, i8 -56, i16 -32768, i16 -1, i64 -1, i64 -1)", ir);
        Assert.DoesNotContain("signext", ir);
        Assert.DoesNotContain("zeroext", ir);
        var c = MinimalEmissionTest.Analyze(Mixed);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        for (var i = 0; i < c.Ownership.Bodies.Count; i++)
        {
            if (c.Ownership.Bodies[i].Function.Name == "small")
            {
                var function = Enumerable.Range(0, module.FunctionCount).Select(module.GetFunction).First(x => x.Abi.Parameters.Length == 6);
                Assert.Empty(function.Slots);
            }
        }
    }

    [Fact]
    public void ShiftConversionFollowsCheckAndPrecedesThePhiPredecessorEnd()
    {
        var c = MinimalEmissionTest.Analyze("var x: u8 = 1\nvar n: u64 = 7\nlet y = if true => x << n else => x >> n");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var f = module.GetFunction(0);
        var shifts = f.Instructions.Where(x => x.Check == ArithmeticCheckKind.Shift).ToArray();
        Assert.Equal(2, shifts.Length);
        using var writer = new StringWriter();
        module.WriteIr(writer);
        var ir = writer.ToString();
        foreach (var shift in shifts)
        {
            Assert.Contains($"%invalid{shift.Operation} = icmp uge i64 ", ir);
            Assert.Contains($"b{shift.Place}:\n  %shift{shift.Operation} = trunc i64 ", ir);
            Assert.Contains($"%v{shift.Operation} = {shift.ScalarOperator} i8 ", ir);
        }

        var inputs = f.GetOperands(Assert.Single(f.Instructions, x => x.Opcode == EmissionOpcode.Phi));
        for (var i = 0; i < inputs.Length; i += 2)
        {
            var value = inputs[i].Value;
            Assert.Equal(Assert.Single(shifts, x => x.Operation == value).Place, inputs[i + 1].Value);
        }
    }

    [Fact]
    public void WarmMixedIntegerAnalysisAndWritingAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Mixed);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var valid = true;
        for (var i = 0; i < 128; i++)
        {
            valid &= c.Ownership.Analyze().IsVerified;
            valid &= c.Emission.WriteIr(TextWriter.Null, out _);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(valid);
    }

    [Fact]
    public void SameLlvmWidthDoesNotPermitMixedLanguageTypesInPhi()
    {
        var c = MinimalEmissionTest.Analyze("let u: u8 = 1\nlet y: i8 = if true => 1 else => 2");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies[0];
        var phi = body.Values.FindIndex(x => x.Kind == OwnershipValueKind.Phi);
        var unsigned = Enumerable.Range(0, body.Values.Count).First(i => body.Values[i].Kind == OwnershipValueKind.Constant && body.Places[body.Operations[i].Place].Type == BoundType.Primitives["u8"]);
        var index = body.Values[phi].Start;
        body.PhiInputs[index] = body.PhiInputs[index] with { Value = unsigned };
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void PointerWidthMustMatchTheEmissionProfile()
    {
        var c = MinimalEmissionTest.Analyze("let n: usize = 1");
        Assert.True(c.Emission.Validate(out var error), error);
        var property = typeof(Compilation).GetProperty(nameof(Compilation.IrTarget))!;
        var original = c.IrTarget;
        try
        {
            property.SetValue(c, original with { PointerWidth = 32 });
            using var writer = new StringWriter();
            Assert.False(c.Emission.WriteIr(writer, out _));
            Assert.Empty(writer.ToString());
        }
        finally
        {
            property.SetValue(c, original);
        }

        Assert.True(c.Emission.Validate(out error), error);
    }

    private static void Abort(string name, string source, string reason, int line, int column)
        => ScalarEmissionTest.EmitFixture(name, source, string.Empty, 1, $"Hello.kimi:{line}:{column}: abort {reason}\n");
}
