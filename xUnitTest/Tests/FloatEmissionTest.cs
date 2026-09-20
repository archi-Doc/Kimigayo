// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class FloatEmissionTest
{
    [Theory]
    [InlineData("f32", "1.00000005960464477539062500000000000000000000000000001", 0x3F800001L)]
    [InlineData("f32", "1.000000059604644775390625", 0x3F800000L)]
    [InlineData("f32", "1.000000178813934326171875", 0x3F800002L)]
    [InlineData("f32", "1e-45", 1L)]
    [InlineData("f32", "7e-46", 0L)]
    [InlineData("f32", "8e-46", 1L)]
    [InlineData("f32", "-7e-46", 0x80000000L)]
    [InlineData("f32", "-0.0", 0x80000000L)]
    [InlineData("f32", "0.0", 0L)]
    [InlineData("f32", "1_2.5_0", 0x41480000L)]
    [InlineData("f32", "3.4028234663852886e38", 0x7F7FFFFFL)]
    [InlineData("f64", "1.00000000000000011102230246251565404236316680908203125", 0x3FF0000000000000L)]
    [InlineData("f64", "1.0000000000000001110223024625156540423631668090820312501", 0x3FF0000000000001L)]
    [InlineData("f64", "4.9406564584124654e-324", 1L)]
    [InlineData("f64", "2e-324", 0L)]
    [InlineData("f64", "3e-324", 1L)]
    [InlineData("f64", "-2e-324", long.MinValue)]
    [InlineData("f64", "-0.0", long.MinValue)]
    [InlineData("f64", "0.0", 0L)]
    [InlineData("f64", "1_2.5_0", 0x4029000000000000L)]
    [InlineData("f64", "1.7976931348623157e308", 0x7FEFFFFFFFFFFFFFL)]
    public void LiteralsUseSelectedPrecisionBitsThroughStorageAndCalls(string type, string literal, long bits)
    {
        var source = $"func echo(x?: {type}) -> {type} => x\nvar value: {type} = {literal}\nlet copy = value\nvalue = 0.0\nvalue = copy\nif echo(value) == {literal} => Console.writeLine(\"ok\")";
        var c = MinimalEmissionTest.Analyze(source);
        var body = c.Ownership.Bodies.Single(x => x.Function.IsGenerated);
        Assert.Contains(body.Values, x => x.Kind == OwnershipValueKind.Constant && x.Constant == bits);
        var ir = Emit("FloatLiteral" + type + literal.Replace('.', 'P').Replace('-', 'M'), source, "ok\n");
        var llvm = type == "f32" ? "float" : "double";
        Assert.Contains("alloca " + llvm + ", align " + (type == "f32" ? 4 : 8), ir);
        Assert.Contains("call " + llvm + " @__kimi_f", ir);
    }

    [Theory]
    [InlineData("f32")]
    [InlineData("f64")]
    public void ArithmeticCompoundAssignmentAndNegation(string type)
    {
        var source = $"func calculate(a?: {type}, b?: {type}) -> {type}\n    var x = a\n    x += b\n    x -= 1.0\n    x *= 2.0\n    x /= 4.0\n    return -x\nif calculate(2.0, 3.0) == -2.0 => Console.writeLine(\"ok\")";
        var ir = Emit("FloatArithmetic" + type, source, "ok\n");
        foreach (var op in new[] { "fadd", "fsub", "fmul", "fdiv", "fneg" })
        {
            Assert.Contains(" = " + op + " ", ir);
        }
    }

    [Theory]
    [InlineData("f32")]
    [InlineData("f64")]
    public void IeeeComparisonsInfinityAndSignedZero(string type)
    {
        var source = $"""
            func equal(a?: {type}, b?: {type}) -> bool => a == b
            func unequal(a?: {type}, b?: {type}) -> bool => a != b
            func less(a?: {type}, b?: {type}) -> bool => a < b
            func lessEqual(a?: {type}, b?: {type}) -> bool => a <= b
            func greater(a?: {type}, b?: {type}) -> bool => a > b
            func greaterEqual(a?: {type}, b?: {type}) -> bool => a >= b
            var zero: {type} = 0.0
            let negative = -zero
            let positiveInfinity = 1.0 / zero
            let negativeInfinity = 1.0 / (+negative)
            let nan = zero / zero
            if equal(zero, negative) and not unequal(zero, negative) and not less(zero, negative) and lessEqual(zero, negative) and not greater(zero, negative) and greaterEqual(zero, negative) => Console.writeLine("zero")
            if not equal(nan, nan) and unequal(nan, nan) and not less(nan, zero) and not lessEqual(nan, zero) and not greater(nan, zero) and not greaterEqual(nan, zero) => Console.writeLine("nan")
            if not equal(zero, nan) and unequal(zero, nan) and not less(zero, nan) and not lessEqual(zero, nan) and not greater(zero, nan) and not greaterEqual(zero, nan) => Console.writeLine("right")
            if less(negativeInfinity, zero) and greater(positiveInfinity, zero) and equal(positiveInfinity, positiveInfinity) and unequal(positiveInfinity, negativeInfinity) => Console.writeLine("infinity")
            """;
        var ir = Emit("FloatIeee" + type, source, "zero\nnan\nright\ninfinity\n");
        foreach (var predicate in new[] { "oeq", "une", "olt", "ole", "ogt", "oge" })
        {
            Assert.Contains("fcmp " + predicate, ir);
        }
    }

    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "FloatRounding32", "var a: f32 = 16777216.0\nvar b: f32 = 1.0\nif (a + b) - a == 0.0 => Console.writeLine(\"ok\")\nvar x: f32 = 4097.0\nif x * x - 16785408.0 == 0.0 => Console.writeLine(\"unfused\")", "ok\nunfused\n" },
        { "FloatRounding64", "var a: f64 = 9007199254740992.0\nvar b: f64 = 1.0\nif (a + b) - a == 0.0 => Console.writeLine(\"ok\")", "ok\n" },
        { "FloatSubnormal32", "var tiny: f32 = 1e-45\nlet twice = tiny + tiny\nif tiny > 0.0 and twice > tiny and twice / 2.0 == tiny => Console.writeLine(\"ok\")", "ok\n" },
        { "FloatSubnormal64", "var tiny: f64 = 5e-324\nlet twice = tiny + tiny\nif tiny > 0.0 and twice > tiny and twice / 2.0 == tiny => Console.writeLine(\"ok\")", "ok\n" },
        { "FloatOverflow32", "var x: f32 = 3.4e38\nlet inf = x * x\nif inf > x and inf - inf != 0.0 => Console.writeLine(\"ok\")", "ok\n" },
        { "FloatOverflow64", "var x: f64 = 1.7e308\nlet inf = x * x\nif inf > x and inf - inf != 0.0 => Console.writeLine(\"ok\")", "ok\n" },
        { "FloatReturnSnapshot", "func f(x?: f32) -> f32\n    var y = x\n    defer => y = 99.0\n    return y\nif f(1.25) == 1.25 => Console.writeLine(\"ok\")", "ok\n" },
        { "FloatIf", "var flag = false\nlet x: f32 = if flag => 1.0 else => -0.0\nif 1.0 / x < 0.0 => Console.writeLine(\"ok\")", "ok\n" },
        { "FloatLoop", "var n = 0\nlet x: f64 = loop\n    n += 1\n    if n < 3 => continue\n    if n == 3 => exit 1.25\n    exit 2.0\nif x == 1.25 => Console.writeLine(\"ok\")", "ok\n" },
        { "FloatDo", "let x: f32 = work: do\n    defer => Console.writeLine(\"cleanup\")\n    exit to work: 1.25\nif x == 1.25 => Console.writeLine(\"ok\")", "cleanup\nok\n" },
        { "FloatMatchGuard", "let x: f32 = 1.25\nlet y = match x\n    let n if n > 1.0 => n\n    _ => 0.0\nif y == x => Console.writeLine(\"ok\")", "ok\n" },
        { "FloatNamedCall", "func first() -> f32\n    Console.writeLine(\"first\")\n    return 1.0\nfunc second() -> f32\n    Console.writeLine(\"second\")\n    return 2.0\nfunc subtract(a?: f32, b?: f32) -> f32\n    defer => Console.writeLine(\"cleanup\")\n    return a - b\nif subtract(b: second(), a: first()) == -1.0 => Console.writeLine(\"ok\")", "second\nfirst\ncleanup\nok\n" },
        { "FloatAggregate", "var a: (f32, f64) = (1.25, -0.0)\nlet b = a\na = b\nlet c: [2 of f32] = [1.0, 2.0]\nlet d = c\nlet e = c\nConsole.writeLine(\"ok\")", "ok\n" },
        { "FloatOwnedAggregate", "let a: (f32, string, f64) = (1.25, \"held\", -0.0)\ndefer => Console.writeLine(\"cleanup\")", "cleanup\n" },
        { "FloatStackCall", "func f(a?: f32, b?: f64, c?: f32, d?: f64, e?: f32, f?: f64, g?: f32, h?: f64) -> f64\n    if a != 1.0 or c != 3.0 or e != 5.0 or g != 7.0 => Console.writeLine(\"bad\")\n    return b + d + f + h\nif f(1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0) == 20.0 => Console.writeLine(\"ok\")", "ok\n" },
        { "FloatMatchReversed", "let x: f32 = 1.25\nlet y = match x\n    _ if false => 0.0\n    let n => n\nif y == x => Console.writeLine(\"ok\")", "ok\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ExistingValueAndCleanupPathsExecuteFloats(string name, string source, string expected)
    {
        var ir = Emit(name, source, expected);
        if (name == "FloatOwnedAggregate")
        {
            StringEmissionTest.WriteAuditedFixture(name, source, ir, expected, "held=1;cleanup=1", order: [1, 0]);
        }
    }

    [Theory]
    [InlineData("var x: f32 = 1.0\nx++")]
    [InlineData("var x: f64 = 1.0\n--x")]
    [InlineData("1.0 % 2.0")]
    [InlineData("1.0 & 2.0")]
    [InlineData("1.0 | 2.0")]
    [InlineData("1.0 ^ 2.0")]
    [InlineData("1.0 << 1")]
    [InlineData("1.0 >> 1")]
    [InlineData("not 1.0")]
    [InlineData("var a: f32 = 1.0\nvar b: f64 = 1.0\na + b")]
    [InlineData("let x: f32 = 3.5e38")]
    [InlineData("let x: f64 = 1.8e308")]
    [InlineData("match 1.0\n    1.0 => ()\n    _ => ()")]
    [InlineData("func unused() => 1.0 % 2.0\n()")]
    [InlineData("if false => 1.0 << 1")]
    [InlineData("let x: f32 = 1.0\nlet z: f64 = 2.0\nlet y = match x\n    _ if true => z\n    let n => n")]
    public void InvalidFloatOperationsFailBeforeWriting(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.NotEmpty(c.Binding.Issues);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Theory]
    [InlineData("bits")]
    [InlineData("precision")]
    [InlineData("integer-type")]
    [InlineData("operator")]
    public void CorruptFloatPlansFailBeforeWriting(string defect)
    {
        var c = MinimalEmissionTest.Analyze("var a: f32 = 1.25\nlet b = a * 2.0\nif b == 2.5 => Console.writeLine(\"ok\")");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies[0];
        var index = body.Values.FindIndex(x => x.Kind == OwnershipValueKind.Constant && x.Constant == 0x3FA00000L);
        Assert.True(index >= 0);
        if (defect == "bits")
        {
            body.Values[index] = body.Values[index] with { Constant = 0x3FA00001L };
        }
        else if (defect is "precision" or "integer-type")
        {
            body.PlaceStorage[body.Operations[index].Place] = body.Places[body.Operations[index].Place] with { Type = defect == "precision" ? BoundType.F64 : BoundType.I32 };
        }
        else
        {
            index = body.Values.FindIndex(x => x.Kind == OwnershipValueKind.Binary && x.Operator == KotoKind.Asterisk);
            body.Values[index] = body.Values[index] with { Operator = KotoKind.Percent };
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        c.Ownership.Analyze();
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }

    [Fact]
    public void WarmFloatAnalysisAndEmissionDoNotAllocate()
    {
        var c = MinimalEmissionTest.Analyze("func f(x?: f32) -> f32 => -(x * 1_2.5)\nvar a: f32 = 1.25\na = if a > 0.0 => f(a) else => a\nlet pair: (f32, f64) = (a, 1.25)");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Ownership.Analyze().IsVerified || !c.Emission.WriteIr(TextWriter.Null, out _))
            {
                throw new InvalidOperationException("Float analysis or emission failed.");
            }
        }));
    }

    [Fact]
    public void RebindingAndSerializationDoNotReuseTheOldDoubleLiteralCache()
    {
        const string Source = "let x: f32 = 1.00000005960464477539062500000000000000000000000000001\nif x == 1.00000011920928955078125 => Console.writeLine(\"ok\")";
        var c = MinimalEmissionTest.Analyze(Source);
        var field = Assert.IsType<FieldKoto>(c.Kotonoha.GeneratedFunction!.Body!.Items[0]);
        Assert.True(Assert.IsType<NumberLiteralKoto>(field.InitializerKoto).TryGetBasicValue(out _));
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Ownership.Analyze();
        Assert.Contains(c.Ownership.Bodies[0].Values, x => x.Kind == OwnershipValueKind.Constant && x.Constant == 0x3F800001L);
        using var original = new StringWriter();
        Assert.True(c.Emission.WriteIr(original, out var error), error);
        var bytes = Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var kotonoha = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(bytes, ref kotonoha);
        Assert.NotNull(kotonoha);
        Assert.Same(restored.Kotonoha, kotonoha);
        kotonoha.OnDeserialized(restored);
        restored.Bind();
        restored.Binding.CheckStartup(OutputKind.Application);
        restored.Ownership.Analyze();
        using var roundTrip = new StringWriter();
        Assert.True(restored.Emission.WriteIr(roundTrip, out error), error);
        Assert.Equal(original.ToString(), roundTrip.ToString());
        ScalarEmissionTest.WriteFixture("FloatDoubleRoundingRoundTrip", roundTrip.ToString(), "ok\n");
    }

    internal static void AssertEmissionSupport(Compilation c, bool expected)
    {
        using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, c.Emission.WriteIr(writer, out var error));
        if (expected)
        {
            Assert.True(c.Binding.Result.IsComplete);
            Assert.True(c.Ownership.Result.IsVerified);
            Assert.Null(error);
            Assert.NotEmpty(writer.ToString());
        }
        else
        {
            Assert.NotNull(error);
            Assert.Empty(writer.ToString());
        }
    }

    private static string Emit(string name, string source, string expected)
    {
        var ir = ScalarEmissionTest.EmitFixture(name, source, expected);
        Assert.Contains("\"denormal-fp-math\"=\"ieee,ieee\"", ir);
        Assert.DoesNotMatch(@"\b(?:fast|nnan|ninf|nsz|arcp|contract|afn|reassoc|strictfp)\b", ir);
        Assert.DoesNotContain("llvm.fma", ir);
        return ir;
    }
}
