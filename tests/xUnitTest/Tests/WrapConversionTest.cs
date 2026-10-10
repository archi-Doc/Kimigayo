// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// SPEC 13.5.4.3: E@wrap<U> wraps an integer or wrapping integer value to U without a check; a direct literal is wrapped at
// compile time, and a generic U takes the generic literals.
public class WrapConversionTest
{
    [Fact]
    public void BindsBetweenIntegerAndWrappingTypes()
    {
        const string source = "let x: u64 = 1\nlet a = x@wrap<u32>\nlet b = x@wrap<Wrapping<u8>>\nlet w: Wrapping<i16> = 1\nlet c = w@wrap<u64>\nlet d = w@wrap<Wrapping<i16>>\nlet e = x@wrap<u64>\nlet f = x@wrap<i128>";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var variables = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<VariableKoto>().ToDictionary(x => x.NameKoto.IdentifierName, x => x);
        Assert.Same(BoundType.Primitives["u32"], variables["a"].NameKoto.TypeOf());
        Assert.Same(BoundType.WrappingOf(BoundType.Primitives["u8"]), variables["b"].NameKoto.TypeOf());
        Assert.Same(BoundType.Primitives["u64"], variables["c"].NameKoto.TypeOf());
        Assert.Same(BoundType.WrappingOf(BoundType.Primitives["i16"]), variables["d"].NameKoto.TypeOf());
        Assert.Same(BoundType.Primitives["u64"], variables["e"].NameKoto.TypeOf());
        Assert.Same(BoundType.Primitives["i128"], variables["f"].NameKoto.TypeOf());
        Assert.All(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ConversionKoto>(), x => Assert.Equal(ConversionBinding.Wrap, x.ConversionBinding));
    }

    [Theory]
    [InlineData("-1@wrap<u64>", "u64", -1)] // All ones, stored as the sign-extended payload.
    [InlineData("0x1_0000_0005@wrap<u32>", "u32", 5)]
    [InlineData("(-1)@wrap<u8>", "u8", -1)] // 255
    [InlineData("300@wrap<i8>", "i8", 44)]
    [InlineData("128@wrap<i8>", "i8", -128)]
    [InlineData("+7@wrap<Wrapping<u16>>", "Wrapping<u16>", 7)]
    [InlineData("340282366920938463463374607431768211455@wrap<i128>", "i128", -1)]
    [InlineData("-170141183460469231731687303715884105728@wrap<u128>", "u128", long.MinValue)] // Checked below by payload bits.
    public void DirectLiteralsAreWrappedAtCompileTime(string expression, string type, long payload)
    {
        var c = MinimalEmissionTest.Analyze($"let w = {expression}");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var conversion = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ConversionKoto>());
        var expected = type.StartsWith("Wrapping<", StringComparison.Ordinal) ? BoundType.WrappingOf(BoundType.Primitives[type[9..^1]]) : BoundType.Primitives[type];
        Assert.Same(expected, conversion.TypeOf());
        Assert.Equal(ConversionBinding.Wrap, conversion.ConversionBinding);
        var folded = Assert.NotNull(conversion.FoldedConstant);
        if (type == "u128")
        {
            Assert.Equal(Int128.MinValue, folded); // -2^127 is the bit pattern 2^127, read as u128.
        }
        else
        {
            Assert.Equal((Int128)payload, folded);
        }

        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
    }

    [Fact]
    public void GenericTargetsTakeTheGenericLiterals()
    {
        var c = MinimalEmissionTest.Analyze("func f<T>(x: T) -> Wrapping<T>\n    T is PrimitiveInteger\n    return 127@wrap<Wrapping<T>> + x@wrap<Wrapping<T>>\nlet r = f(1@u8)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(MinimalEmissionTest.Analyze("func f<T>(x: T) -> Wrapping<T>\n    T is PrimitiveInteger\n    return 128@wrap<Wrapping<T>>").Binding.Result.IsComplete);
    }

    [Fact]
    public void WrappingConversionsExecute()
    {
        const string source = "let a = 300@i32@wrap<u8>\nlet b = 200@u8@wrap<i8>\nlet c = 0xFFFF_FFFF@u32@wrap<i32>\nlet d = -1@i32@wrap<u64>\nlet e = -1@i32@wrap<u32>@u64\n" +
            "let seed: u64 = 0x1_0000_0007\nlet w = seed@wrap<Wrapping<u32>>\nlet big: u64 = 0x1234_5678_9abc_def0\nlet low = big@wrap<u32>\n" +
            "let m = -1@wrap<u64>\nlet n = 0x1_0000_0005@wrap<u32>\nlet p = (200 + 100)@wrap<u8>\nlet q: i8 = -1\nlet r = q@wrap<i128>\nlet s: u8 = 200\nlet t = s@wrap<u128>\nlet u = r@wrap<u8>\n" +
            "func fold<T>(value: T) -> u64\n    T is PrimitiveInteger\n    return value@wrap<u64>\n" +
            "func zigzag(value: i64) -> u64 => ((value << 1) ^ (value >> 63))@wrap<u64>\n" +
            "let v: Wrapping<u16> = 65535\nlet x = v@wrap<Wrapping<i16>>\nlet y = v@wrap<u16>\nvar z: Wrapping<u8> = 255\nz += 1\nlet wide = z@wrap<i64>\n" +
            "if a == 44 and b == -56 and c == -1 and d == 18446744073709551615 and e == 4294967295 and w == 7 and low == 0x9abc_def0 and m == 18446744073709551615 and n == 5 and p == 44 and r == -1 and t == 200 and u == 255 " +
            "and fold(-1@i8) == 18446744073709551615 and fold(255@u8) == 255 and fold(-1@i128) == 18446744073709551615 and zigzag(-1) == 1 and zigzag(2) == 4 and x == -1 and y == 65535 and v@wrap<Wrapping<u16>> == v and wide == 0 => Console.writeLine(\"ok\")";
        var ir = ScalarEmissionTest.EmitFixture("WrapConversions", source, "ok\n");
        Assert.Contains(" = trunc i32 ", ir);
        Assert.Contains(" = sext i8 ", ir);
        Assert.Contains(" = zext i8 ", ir);
        Assert.Contains(" = trunc i128 ", ir);
    }

    [Fact]
    public void WrappingConversionsEmitNoChecks()
    {
        var ir = ScalarEmissionTest.EmitFixture("WrapInstructions", "var x: u64 = 5\nlet a = x@wrap<u32>\nvar b: i8 = -1\nlet c = b@wrap<u64>\nvar d: u8 = 1\nlet e = d@wrap<u64>\nlet f = x@wrap<i64>\nlet g = 7@wrap<u8>\nConsole.writeLine(\"ok\")", "ok\n");
        var start = ir.IndexOf("define internal void @__kimi_entry_body", StringComparison.Ordinal);
        var body = ir[start..ir.IndexOf("\n}\n", start, StringComparison.Ordinal)];
        Assert.Contains(" = trunc i64 ", body);
        Assert.Contains(" = sext i8 ", body);
        Assert.Contains(" = zext i8 ", body);
        Assert.DoesNotContain("icmp", body);
        Assert.DoesNotContain("br i1", body);
    }

    [Theory]
    [InlineData("let f = 1.5@wrap<u32>")] // Floating-point operands have no wrapping conversion.
    [InlineData("let f: f64 = 1.5\nlet w = f@wrap<u32>")]
    [InlineData("let w: Wrapping<u32> = 1\nlet f = w@wrap<f64>")]
    [InlineData("let x: u32 = 1\nlet y = x@wrap")] // The Type argument is mandatory.
    [InlineData("let x: u32 = 1\nlet y = x@wrap<ref/u32>")]
    [InlineData("let x: u32 = 1\nlet y = x@wrap<u32, u8>")]
    [InlineData("let x: u32 = 1\nlet y = x@wrap<bool>")]
    [InlineData("let y = true@wrap<u8>")]
    [InlineData("let y = 'a'@wrap<u32>")]
    [InlineData("let y = \"x\"@wrap<u8>")]
    [InlineData("let x: isize = 1\nlet p: raw/u8 = null\nlet y = p@wrap<usize>")]
    [InlineData("let r = 0..3\nlet y = r@wrap<u8>")]
    public void InvalidFormsAreRejected(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void FloatingOperandExplainsTheOperation()
    {
        var c = MinimalEmissionTest.Analyze("let f: f32 = 1.5\nlet w = f@wrap<u32>");
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal(nameof(Kimi.DiagnosticCode.InvalidWrapConversion_Kd), error.Code);
        Assert.Equal("f@wrap<u32>", error.Text);
    }
}
