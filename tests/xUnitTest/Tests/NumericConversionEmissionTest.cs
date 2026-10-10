// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Numerics;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class NumericConversionEmissionTest
{
    public static IEnumerable<object[]> Pairs()
    {
        foreach (var floating in new[] { "f32", "f64" })
        {
            foreach (var integer in new[] { "i8", "u8", "i16", "u16", "i32", "u32", "i64", "u64", "isize", "usize" })
            {
                yield return [floating, integer];
            }
        }
    }

    [Theory]
    [InlineData("Narrow", "let x: f64 = 1.25\nif x@f32 == 1.25 => Console.writeLine(\"ok\")")]
    [InlineData("FromSigned", "let x: i64 = -42\nif x@f32 == -42.0 => Console.writeLine(\"ok\")")]
    [InlineData("FromUnsigned", "let x: u64 = 18446744073709551615\nif x@f64 == 18446744073709551616.0 => Console.writeLine(\"ok\")")]
    [InlineData("Literal", "if 5000000000@f64 == 5000000000.0 => Console.writeLine(\"ok\")")]
    [InlineData("ToSigned", "let x: f64 = 3.9\nif x@i32 == 3 => Console.writeLine(\"ok\")")]
    [InlineData("ToUnsigned", "let x: f32 = -0.9\nif x@u8 == 0 => Console.writeLine(\"ok\")")]
    public void NumericConversionsExecute(string name, string source)
        => ScalarEmissionTest.EmitFixture("NumericConvert" + name, source, "ok\n");

    [Theory]
    [MemberData(nameof(Pairs))]
    public void EveryPairChecksTruncationAtAdjacentSourceValues(string floating, string integer)
    {
        var signed = integer[0] == 'i';
        var width = ScalarTypes.Width(BoundType.Primitives[integer], 64);
        var upper = BigInteger.One << (width - (signed ? 1 : 0));
        var minimum = signed ? -upper : BigInteger.Zero;
        var maximum = upper - 1;
        double Round(double x) => floating == "f32" ? (float)x : x;
        double Previous(double x) => floating == "f32" ? float.BitDecrement((float)x) : double.BitDecrement(x);
        double Next(double x) => floating == "f32" ? float.BitIncrement((float)x) : double.BitIncrement(x);
        var low = Round((double)minimum);
        var high = Round((double)upper);
        double[] samples = [0.0, -0.0, 0.9, -0.9, 3.9, -3.9, low, Previous(low), Next(low), low - 0.75, low - 1.0, high, Previous(high), Next(high), high - 0.25];
        var header = $"func convert(x: {floating}) -> {integer} => x@{integer}\n";
        var stem = "NumericConvertBoundary" + floating + integer;
        var positive = new List<string>();
        var seen = new HashSet<long>();
        for (var i = 0; i < samples.Length; i++)
        {
            var sample = Round(samples[i]);
            if (!seen.Add(BitConverter.DoubleToInt64Bits(sample)))
            {
                continue;
            }

            var truncated = new BigInteger(sample);
            var argument = FloatText(sample);
            if (truncated >= minimum && truncated <= maximum)
            {
                positive.Add($"convert({argument}) == {truncated.ToString(CultureInfo.InvariantCulture)}");
            }
            else
            {
                var error = $"Hello.kimi:1:{header.IndexOf("x@", StringComparison.Ordinal) + 1}: abort KIMI_E_INT_CONVERSION: Integer conversion out of range\n";
                ScalarEmissionTest.EmitFixture(stem + "Reject" + i, header + $"defer => Console.writeLine(\"bad\")\nconvert({argument})", string.Empty, 1, error);
            }
        }

        Assert.NotEmpty(positive);
        ScalarEmissionTest.EmitFixture(stem + "Accept", header + "if " + string.Join(" and ", positive) + " => Console.writeLine(\"ok\")", "ok\n");
        foreach (var (name, expression) in new[] { ("NaN", "z / z"), ("PositiveInfinity", "one / z"), ("NegativeInfinity", "-one / z") })
        {
            var error = $"Hello.kimi:1:{header.IndexOf("x@", StringComparison.Ordinal) + 1}: abort KIMI_E_INT_CONVERSION: Integer conversion out of range\n";
            ScalarEmissionTest.EmitFixture(stem + name, header + $"let z: {floating} = 0.0\nlet one: {floating} = 1.0\nconvert({expression})", string.Empty, 1, error);
        }
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void IntegerExtremaRoundIntoBothFloatingFormats(string floating, string integer)
    {
        var signed = integer[0] == 'i';
        var width = ScalarTypes.Width(BoundType.Primitives[integer], 64);
        var limit = BigInteger.One << (width - (signed ? 1 : 0));
        var minimum = signed ? -limit : BigInteger.Zero;
        var maximum = limit - 1;
        string Expected(BigInteger value) => FloatText(floating == "f32"
            ? float.Parse(value.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)
            : double.Parse(value.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));
        var source = $"func convert(x: {integer}) -> {floating} => x@{floating}\nif convert({minimum}) == {Expected(minimum)} and convert({maximum}) == {Expected(maximum)} and convert(0) == 0.0 => Console.writeLine(\"ok\")";
        ScalarEmissionTest.EmitFixture("NumericConvertInteger" + floating + integer, source, "ok\n");
    }

    [Theory]
    [InlineData("NarrowSpecial", "func narrow(x: f64) -> f32 => x@f32\nlet z = 0.0\nlet nan = narrow(z / z)\nif nan != nan and narrow(1.0 / z) > 0.0 and narrow(-1.0 / z) < 0.0 and 1.0 / narrow(-0.0) < 0.0 => Console.writeLine(\"ok\")")]
    [InlineData("NarrowUnderflow", "func narrow(x: f64) -> f32 => x@f32\nif narrow(1e-45) > 0.0 and narrow(-7e-46) == 0.0 and 1.0 / narrow(-7e-46) < 0.0 => Console.writeLine(\"ok\")")]
    [InlineData("NarrowTies", "func narrow(x: f64) -> f32 => x@f32\nif narrow(1.000000059604644775390625) == 1.0 and narrow(1.000000178813934326171875) == 1.0000002384185791015625 => Console.writeLine(\"ok\")")]
    [InlineData("IntegerLiteralBits", "if 0x20000000000001@f64 == 9007199254740992.0 and (-0)@f32 == 0.0 and 1.0 / (-0)@f32 > 0.0 and 18_014_399_583_223_809@f32 == 18014400656965632.0 => Console.writeLine(\"ok\")")]
    [InlineData("LiteralFraction", "if 3.9@i32 == 3 and -3.9@i32 == -3 => Console.writeLine(\"ok\")")]
    [InlineData("LiteralExact", "if 9007199254740993.0@i64 == 9007199254740993 and 1e10@i64 == 10000000000 and 1e38@u128 == 100000000000000000000000000000000000000 and 255.999@u8 == 255 and -0.9@u8 == 0 and 3.9@i128 == 3 and (-3.9)@i8 == -3 and 1.5e-3@u16 == 0 and 2.5e0@usize == 2 and 3.9@i32@f64 == 3.0 => Console.writeLine(\"ok\")")]
    [InlineData("RoundingChain", "let x: f64 = 16777217.0\nif x@f32@f64 == 16777216.0 and x@f32@i32 == 16777216 => Console.writeLine(\"ok\")")]
    [InlineData("CheckedPhi", "func convert(x: f64, flag: bool) -> i32\n    var result: i32 = 0\n    defer => result = 0\n    result = if flag => x@f32@i32 else => (-x)@i32\n    return result\nif convert(1.9, true) == 1 and convert(1.9, false) == -1 => Console.writeLine(\"ok\")")]
    public void RoundingAndConsumersPreserveSemantics(string name, string source)
        => ScalarEmissionTest.EmitFixture("NumericConvert" + name, source, "ok\n");

    [Theory]
    [InlineData("340282356779733661637539395458142568448@f32")]
    [InlineData("(-340282356779733661637539395458142568448)@f32")]
    [InlineData("func unused() => 340282366920938463463374607431768211455@f32\n()")]
    public void ExactIntegerLiteralOverflowIsStatic(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.FailureOf() == BindingFailure.InvalidLiteral);
        Assert.False(c.Emission.Validate(out _));
    }

    // SPEC 13.5.4.2: a direct floating-point literal converted to an integer Type is truncated toward zero from its exact decimal
    // value at compile time; the folded payload is the target's sign-extended bit pattern.
    [Theory]
    [InlineData("3.9@i32", "i32", "3")]
    [InlineData("-3.9@i32", "i32", "-3")]
    [InlineData("(-3.9)@i8", "i8", "-3")]
    [InlineData("+0.5@u8", "u8", "0")]
    [InlineData("-0.9@u8", "u8", "0")] // Truncation toward zero reaches zero, which an unsigned Type holds.
    [InlineData("9007199254740993.0@i64", "i64", "9007199254740993")] // Not representable in f64.
    [InlineData("1e10@i64", "i64", "10000000000")]
    [InlineData("12_3.4_5e1@u16", "u16", "1234")]
    [InlineData("0.000001e7@u8", "u8", "10")]
    [InlineData("1e38@u128", "u128", "100000000000000000000000000000000000000")]
    [InlineData("340282366920938463463374607431768211455.9@u128", "u128", "-1")]
    [InlineData("-170141183460469231731687303715884105728.5@i128", "i128", "-170141183460469231731687303715884105728")]
    [InlineData("18446744073709551615.5@u64", "u64", "-1")]
    [InlineData("255.999@u8", "u8", "-1")]
    [InlineData("0.0e999999999999999999999@u8", "u8", "0")] // A saturated exponent of a zero mantissa.
    [InlineData("1e-999999999999999999999@i32", "i32", "0")]
    [InlineData("1.5@isize", "isize", "1")]
    [InlineData("2.5E+1@usize", "usize", "25")]
    public void FloatingLiteralsTruncateExactlyAtCompileTime(string expression, string type, string payload)
    {
        var c = MinimalEmissionTest.Analyze($"let w = {expression}");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var conversion = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ConversionKoto>());
        Assert.Same(BoundType.Primitives[type], conversion.TypeOf());
        Assert.Same(BoundType.Primitives[type], Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<NumberLiteralKoto>()).TypeOf());
        Assert.Equal(ConversionBinding.Literal, conversion.ConversionBinding);
        Assert.Equal(Int128.Parse(payload, CultureInfo.InvariantCulture), Assert.NotNull(conversion.FoldedConstant));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
    }

    [Theory]
    [InlineData("256.0@u8", "its truncated value is outside the range of u8")]
    [InlineData("1e10@i32", "its truncated value is outside the range of i32")]
    [InlineData("-0.5e1@u8", "its truncated value is outside the range of u8")]
    [InlineData("(-129.0)@i8", "its truncated value is outside the range of i8")]
    [InlineData("9.3e18@i64", "its truncated value is outside the range of i64")]
    [InlineData("1e39@u128", "its truncated value is outside the range of u128")]
    [InlineData("340282366920938463463374607431768211456.0@u128", "its truncated value is outside the range of u128")]
    [InlineData("1e99999999999999999999@i32", "its truncated value is outside the range of i32")]
    [InlineData("256@u8", "its value is outside the range of u8")]
    [InlineData("((-129))@i8", "its value is outside the range of i8")]
    [InlineData("-1@Wrapping<u32>", "its value is outside the range of Wrapping<u32>")]
    public void DirectLiteralsOutsideTheRangeAreStaticAndExplained(string source, string note)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.FailureOf() == BindingFailure.InvalidLiteral);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal("InvalidNumericLiteral_Kd", error.Code);
        Assert.Contains(note, error.Note);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void FoldedLiteralsEmitNoConversion()
    {
        var ir = ScalarEmissionTest.EmitFixture("NumericConvertLiteralFolded", "var a = 3.9@i32\nvar b = -1e3@i64\nvar c = 1e38@u128\nif a == 3 and b == -1000 and c > 0 => Console.writeLine(\"ok\")", "ok\n");
        var start = ir.IndexOf("define internal void @__kimi_entry_body", StringComparison.Ordinal);
        var body = ir[start..ir.IndexOf("\n}\n", start, StringComparison.Ordinal)];
        Assert.DoesNotContain("fptosi", body);
        Assert.DoesNotContain("fptoui", body);
        Assert.DoesNotContain("fcmp", body);
    }

    [Theory]
    [InlineData("let x: f32 = 1")]
    [InlineData("func f(x: f64) => ()\nf(1)")]
    [InlineData("(5000000000 + 1)@f64")]
    [InlineData("-(5000000000)@f64")]
    public void ExplicitFittingDoesNotBroadenImplicitOrGeneralExpressionFitting(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void NarrowingChecksTheRoundingThresholdOnBothSides()
    {
        const double Threshold = 3.40282356779733661637539395458142568448e38;
        const string Header = "func narrow(x: f64) -> f32 => x@f32\n";
        var valid = FloatText(double.BitDecrement(Threshold));
        ScalarEmissionTest.EmitFixture("NumericConvertNarrowMaximum", Header + $"if narrow({valid}) == 3.4028234663852886e38 and narrow(-{valid}) == -3.4028234663852886e38 => Console.writeLine(\"ok\")", "ok\n");
        double[] invalid = [Threshold, -Threshold, double.BitIncrement(Threshold), -double.BitIncrement(Threshold), double.MaxValue, -double.MaxValue];
        for (var i = 0; i < invalid.Length; i++)
        {
            ScalarEmissionTest.EmitFixture("NumericConvertNarrowOverflow" + i, Header + $"defer => Console.writeLine(\"bad\")\nnarrow({FloatText(invalid[i])})", string.Empty, 1, "Hello.kimi:1:31: abort KIMI_E_FLOAT_CONVERSION: Floating conversion out of range\n");
        }
    }

    [Theory]
    [InlineData("Narrow", "f64", "f32", "1.25")]
    [InlineData("ToInteger", "f32", "i32", "1.25")]
    [InlineData("FromInteger", "u64", "f32", "16777217")]
    public void ConversionPlansAreRevalidatedAndSurviveReload(string name, string sourceType, string targetType, string value)
    {
        var c = MinimalEmissionTest.Analyze($"let x: {sourceType} = {value}\nlet y = x@{targetType}\nConsole.writeLine(\"ok\")");
        using var original = new StringWriter();
        Assert.True(c.Emission.WriteIr(original, out var error), error);
        var body = c.Ownership.Bodies[0];
        var id = body.Values.FindIndex(x => x.Kind == OwnershipValueKind.Convert);
        var conversion = Assert.IsType<ConversionKoto>(body.Operations[id].Source);
        conversion.ConversionBinding = ConversionBinding.Integer;
        using var failed = new StringWriter();
        Assert.False(c.Emission.WriteIr(failed, out _));
        Assert.Empty(failed.ToString());
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Ownership.Analyze();
        using var rebound = new StringWriter();
        Assert.True(c.Emission.WriteIr(rebound, out error), error);
        Assert.Equal(original.ToString(), rebound.ToString());
        var restored = CompilationTestHelper.Reload(c);
        restored.Bind();
        restored.Binding.CheckStartup(OutputKind.Application);
        restored.Ownership.Analyze();
        using var writer = new StringWriter();
        Assert.True(restored.Emission.WriteIr(writer, out error), error);
        Assert.Equal(original.ToString(), writer.ToString());
        ScalarEmissionTest.WriteFixture("NumericConvertReload" + name, writer.ToString(), "ok\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmNumericAnalysisAndWritingAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("func convert(x: f64) -> u64 => x@f32@u64\nlet x = 5000000000@f64\nlet y = convert(x)@f32\nif y > 0.0 => Console.writeLine(\"ok\")");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Ownership.Analyze().IsVerified || !c.Emission.WriteIr(TextWriter.Null, out _))
            {
                throw new InvalidOperationException("Numeric conversion analysis or writing failed.");
            }
        }));
    }

    private static string FloatText(double value)
    {
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.Contains('.') || text.Contains('E') ? text : text + ".0";
    }
}
