// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// SPEC 13.5.4.4: E@bits<U> reinterprets the bits between a floating-point Type and a same-width integer or wrapping integer
// Type without a check; a direct literal is converted at compile time.
public class BitConversionTest
{
    [Fact]
    public void BindsEveryAllowedPairInBothDirections()
    {
        const string source = "let f: f32 = 1.5\nlet d: f64 = 1.5\nlet i: i32 = 1\nlet u: u64 = 1\nlet w: Wrapping<u32> = 1\nlet v: Wrapping<i64> = 1\n" +
            "let a = f@bits<i32>\nlet b = f@bits<u32>\nlet c = f@bits<Wrapping<i32>>\nlet e = d@bits<i64>\nlet g = d@bits<u64>\nlet h = d@bits<Wrapping<u64>>\n" +
            "let k = i@bits<f32>\nlet l = u@bits<f64>\nlet m = w@bits<f32>\nlet n = v@bits<f64>";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var variables = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<VariableKoto>().ToDictionary(x => x.NameKoto.IdentifierName, x => x);
        Assert.Same(BoundType.Primitives["i32"], variables["a"].NameKoto.BoundType);
        Assert.Same(BoundType.Primitives["u32"], variables["b"].NameKoto.BoundType);
        Assert.Same(BoundType.WrappingOf(BoundType.Primitives["i32"]), variables["c"].NameKoto.BoundType);
        Assert.Same(BoundType.Primitives["i64"], variables["e"].NameKoto.BoundType);
        Assert.Same(BoundType.Primitives["u64"], variables["g"].NameKoto.BoundType);
        Assert.Same(BoundType.WrappingOf(BoundType.Primitives["u64"]), variables["h"].NameKoto.BoundType);
        Assert.Same(BoundType.F32, variables["k"].NameKoto.BoundType);
        Assert.Same(BoundType.F64, variables["l"].NameKoto.BoundType);
        Assert.Same(BoundType.F32, variables["m"].NameKoto.BoundType);
        Assert.Same(BoundType.F64, variables["n"].NameKoto.BoundType);
        Assert.All(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ConversionKoto>(), x => Assert.Equal(ConversionBinding.Bits, x.ConversionBinding));
    }

    [Theory]
    [InlineData("1.5@bits<u32>", "u32", 0x3FC00000L)]
    [InlineData("-1.5@bits<i32>", "i32", -1077936128L)] // The sign-extended payload of 0xBFC00000.
    [InlineData("1.5@bits<u64>", "u64", 0x3FF8000000000000L)]
    [InlineData("-0.0@bits<Wrapping<i64>>", "Wrapping<i64>", long.MinValue)]
    [InlineData("0xFF80_0000@bits<f32>", "f32", 0xFF800000L)] // Negative infinity: f32 bits are stored zero-extended.
    [InlineData("-1@bits<f32>", "f32", 0xFFFFFFFFL)] // All ones after wrapping to u32: a NaN pattern.
    [InlineData("0x3FF8_0000_0000_0000@bits<f64>", "f64", 0x3FF8000000000000L)]
    [InlineData("(-1)@bits<f64>", "f64", -1L)]
    public void DirectLiteralsAreConvertedAtCompileTime(string expression, string type, long payload)
    {
        var c = MinimalEmissionTest.Analyze($"let w = {expression}");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var conversion = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ConversionKoto>());
        var expected = type.StartsWith("Wrapping<", StringComparison.Ordinal) ? BoundType.WrappingOf(BoundType.Primitives[type[9..^1]]) : BoundType.Primitives[type];
        Assert.Same(expected, conversion.BoundType);
        Assert.Equal(ConversionBinding.Bits, conversion.ConversionBinding);
        Assert.Equal((Int128)payload, Assert.NotNull(conversion.FoldedConstant));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
    }

    [Fact]
    public void BitConversionsExecuteAndPreserveEveryPattern()
    {
        const string source = "let raw = 1.5@f32@bits<u32>\nlet back = raw@bits<f32>\nlet quiet = 0x7FC0_0001@u32@bits<f32>\nlet payload = quiet@bits<u32>\n" +
            "let negativeInfinity = 0xFF80_0000@bits<f32>\nlet negativeZero = -0.0@bits<u64>\nlet d: f64 = -2.5\nlet dbits = d@bits<i64>\nlet dback = dbits@bits<f64>\n" +
            "let w: Wrapping<u32> = 0x4000_0000\nlet two = w@bits<f32>\nlet subnormal = 1@u32@bits<f32>\nlet subnormalBits = subnormal@bits<Wrapping<u32>>\n" +
            "let signaling = 0x7F80_0001@u32@bits<f32>\nlet signalingBits = signaling@bits<u32>\nlet literalBits = 1.5@bits<u32>\n" +
            "if raw == 0x3FC0_0000 and back == 1.5 and quiet != quiet and payload == 0x7FC0_0001 and negativeInfinity < -3.0e38 and negativeZero == 0x8000_0000_0000_0000 and dbits == -4610560118520545280 and dback == -2.5 " +
            "and two == 2.0 and subnormal > 0.0 and subnormal < 1.0e-44 and subnormalBits == 1 and signalingBits == 0x7F80_0001 and literalBits == raw => Console.writeLine(\"ok\")";
        var ir = ScalarEmissionTest.EmitFixture("BitConversions", source, "ok\n");
        Assert.Contains(" = bitcast float ", ir);
        Assert.Contains(" = bitcast i32 ", ir);
        Assert.Contains(" = bitcast double ", ir);
        Assert.Contains(" = bitcast i64 ", ir);
    }

    [Fact]
    public void BitConversionsEmitNoChecks()
    {
        var ir = ScalarEmissionTest.EmitFixture("BitInstructions", "var f: f32 = 1.5\nlet a = f@bits<u32>\nvar u: u64 = 1\nlet b = u@bits<f64>\nConsole.writeLine(\"ok\")", "ok\n");
        var start = ir.IndexOf("define internal void @__kimi_entry_body", StringComparison.Ordinal);
        var body = ir[start..ir.IndexOf("\n}\n", start, StringComparison.Ordinal)];
        Assert.Contains(" = bitcast float ", body);
        Assert.Contains(" = bitcast i64 ", body);
        Assert.DoesNotContain("icmp", body);
        Assert.DoesNotContain("fcmp", body);
        Assert.DoesNotContain("br i1", body);
    }

    [Theory]
    [InlineData("let x: u32 = 1\nlet y = x@bits<u64>", "InvalidBitConversion_Kd")] // Two integers use @wrap.
    [InlineData("let x: u32 = 1\nlet y = x@bits<i32>", "InvalidBitConversion_Kd")]
    [InlineData("let f: f32 = 1.5\nlet y = f@bits<f64>", "InvalidBitConversion_Kd")] // Two floating-point Types.
    [InlineData("let f: f32 = 1.5\nlet y = f@bits<u64>", "InvalidBitConversion_Kd")] // Width mismatch.
    [InlineData("let d: f64 = 1.5\nlet y = d@bits<u32>", "InvalidBitConversion_Kd")]
    [InlineData("let x: i16 = 1\nlet y = x@bits<f32>", "InvalidBitConversion_Kd")]
    [InlineData("let x: isize = 1\nlet y = x@bits<f64>", "InvalidBitConversion_Kd")] // Platform-dependent width.
    [InlineData("let d: f64 = 1.5\nlet y = d@bits<usize>", "InvalidBitConversion_Kd")]
    [InlineData("let x: i128 = 1\nlet y = x@bits<f64>", "InvalidBitConversion_Kd")]
    [InlineData("let y = true@bits<f32>", "InvalidBitConversion_Kd")]
    [InlineData("let y = 1.5@bits<f32>", "InvalidBitConversion_Kd")] // A literal of the target's own kind.
    [InlineData("let y = 1@bits<u32>", "InvalidBitConversion_Kd")]
    [InlineData("let y = 1.5@bits<isize>", "InvalidBitConversion_Kd")]
    [InlineData("func f<T>(x: T) -> f32\n    T is PrimitiveInteger\n    return x@bits<f32>", "GenericBitConversion_Kd")]
    [InlineData("func f<T>(x: f32) -> Wrapping<T>\n    T is PrimitiveInteger\n    return x@bits<Wrapping<T>>", "GenericBitConversion_Kd")]
    [InlineData("func f<T>(x: f32) -> T\n    T is PrimitiveInteger\n    return 1@bits<T>", "GenericBitConversion_Kd")]
    public void InvalidFormsAreRejectedWithTheirExplanation(string source, string code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal(code, error.Code);
        Assert.NotNull(error.Advice);
    }

    [Fact]
    public void FixedTypesInsideAGenericBodyAreAccepted()
    {
        var c = MinimalEmissionTest.Analyze("func f<T>(x: T, f: f32) -> u32\n    T is PrimitiveInteger\n    return f@bits<u32>\nlet r = f(1@u8, 1.5)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }
}
