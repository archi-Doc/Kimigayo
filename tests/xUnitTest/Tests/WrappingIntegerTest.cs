// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// SPEC 3.1.1.1, 13.3, 13.5.4.1: Wrapping<T> is the interned wrapping integer Scalar of T, with T's representation and
// operators, wrapping arithmetic, conversions only to and from T, and no position, length, shift-count or member role.
public class WrappingIntegerTest
{
    private const string DivisionZero = "KIMI_E_INT_DIV_ZERO: Integer division or remainder by zero";
    private const string ShiftCount = "KIMI_E_INT_SHIFT_COUNT: Shift count out of range";

    public static TheoryData<string> Integers => ["i8", "u8", "i16", "u16", "i32", "u32", "i64", "u64", "i128", "u128", "isize", "usize"];

    // The native profile excludes 128-bit division and remainder; the other widths exercise every operator.
    public static TheoryData<string, int, bool, string, string> Executable => new()
    {
        { "i8", 8, true, "-128", "127" }, { "u8", 8, false, "0", "255" },
        { "i16", 16, true, "-32768", "32767" }, { "u16", 16, false, "0", "65535" },
        { "i32", 32, true, "-2147483648", "2147483647" }, { "u32", 32, false, "0", "4294967295" },
        { "i64", 64, true, "-9223372036854775808", "9223372036854775807" }, { "u64", 64, false, "0", "18446744073709551615" },
        { "isize", 64, true, "-9223372036854775808", "9223372036854775807" }, { "usize", 64, false, "0", "18446744073709551615" },
    };

    [Theory]
    [MemberData(nameof(Integers))]
    public void EveryWrappingTypeIsTheInternedScalarOfItsArgument(string integer)
    {
        var source = $"let a: Wrapping<{integer}> = 1\nlet b: Wrapping<{integer}> = a + 1\nlet c = a@{integer}\nlet d: {integer} = 2\nlet e = d@Wrapping<{integer}>\n" +
            "let eq = a == b\nlet lt = a < b\nlet r = a@ref\nlet f: Wrapping<" + integer + "> = r\nlet g = a & b\nlet h = a << 1\nlet n = -a\nlet m: Wrapping<" + integer + "> = -(1)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var variables = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<VariableKoto>().ToDictionary(x => x.NameKoto.IdentifierName, x => x);
        var primitive = BoundType.Primitives[integer];
        var wrapping = BoundType.WrappingOf(primitive);
        var a = variables["a"];
        Assert.Same(wrapping, a.NameKoto.BoundType);
        Assert.Equal($"Wrapping<{integer}>", wrapping.Name);
        Assert.Equal(BoundTypeKind.Primitive, wrapping.Kind);
        Assert.True(wrapping.IsWrappingInteger);
        Assert.False(wrapping.IsInteger);
        Assert.True(wrapping.HasIntegerArithmetic);
        Assert.True(wrapping.IsNumeric);
        Assert.False(wrapping.IsUnsignedInteger);
        Assert.Same(primitive, wrapping.Underlying);
        Assert.Equal(ScalarTypes.Width(primitive), ScalarTypes.Width(wrapping));
        Assert.Equal(ScalarTypes.Signed(primitive), ScalarTypes.Signed(wrapping));
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(wrapping, a));
        Assert.Equal(ConstraintProof.Refuted, c.Binding.ProvePrimitiveInteger(wrapping, a));
        Assert.Same(wrapping, variables["b"].NameKoto.BoundType);
        Assert.Same(primitive, variables["c"].NameKoto.BoundType);
        Assert.Same(wrapping, variables["e"].NameKoto.BoundType);
        Assert.Same(BoundType.Boolean, variables["eq"].NameKoto.BoundType);
        Assert.Same(BoundType.Boolean, variables["lt"].NameKoto.BoundType);
        Assert.Same(wrapping, variables["f"].NameKoto.BoundType); // Read through ref/Wrapping<T>.
        Assert.Same(wrapping, variables["g"].NameKoto.BoundType);
        Assert.Same(wrapping, variables["h"].NameKoto.BoundType);
        Assert.Same(wrapping, variables["n"].NameKoto.BoundType); // Unary minus exists for unsigned arguments too.
        Assert.Same(wrapping, variables["m"].NameKoto.BoundType);
        Assert.Same(WindowsLowering.GetValue(primitive), WindowsLowering.GetValue(wrapping));
    }

    [Theory]
    [InlineData("let w: Wrapping<isize> = 1\nlet v: [3 of i32] = [1, 2, 3]\nlet x = v[w]")] // Not a Position.
    [InlineData("let w: Wrapping<i32> = 1\nlet r = 0..w")] // Not a range boundary.
    [InlineData("let w: Wrapping<i32> = 1\nlet p = ^w")] // Not a from-end operand.
    [InlineData("let w: Wrapping<isize> = 3\nlet a: [w of u8]")] // Not a length.
    [InlineData("let w: Wrapping<u8> = 1\nlet n: u8 = 1\nlet s = n << w")] // Not a shift count.
    [InlineData("let w: Wrapping<u32> = 1\nlet n: u32 = 1\nlet s = w + n")] // Distinct Types never mix.
    [InlineData("let w: Wrapping<u32> = 1\nlet n: u32 = 1\nlet s = w == n")]
    [InlineData("let w: Wrapping<u32> = 1\nlet v: Wrapping<u64> = 1\nlet s = w + v")]
    [InlineData("let x: u64 = 1\nlet w = x@Wrapping<u32>")] // Only U enters Wrapping<U>.
    [InlineData("let x: i32 = 1\nlet w = x@Wrapping<u32>")]
    [InlineData("let w: Wrapping<u32> = 1\nlet x = w@u64")] // Only U leaves Wrapping<U>.
    [InlineData("let w: Wrapping<u32> = 1\nlet f = w@f64")]
    [InlineData("let f: f64 = 1.0\nlet w = f@Wrapping<u32>")]
    [InlineData("let w: Wrapping<u32> = 1\nlet v = w@Wrapping<u64>")]
    [InlineData("let w: Wrapping<f32> = 1")] // The argument must be an integer Type.
    [InlineData("let w: Wrapping<Wrapping<u8>> = 1")]
    [InlineData("let w: Wrapping<u32> = -1")] // Literals fit by the argument's range.
    [InlineData("let w: Wrapping<u8> = 256")]
    [InlineData("let w: Wrapping<i8> = 128")]
    [InlineData("let w: Wrapping<u8> = 1\nlet m = w.offset")] // No members.
    [InlineData("let w = Wrapping<u8>.init(1)")] // No constructor.
    [InlineData("let w: Wrapping<u8> = 1\nlet b: bool = w")]
    [InlineData("func f(x: Wrapping<i32>) -> () => ()\nf(1@i32)")] // No implicit conversion.
    public void InvalidFormsAreRejected(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData("let x: u64 = 1\nlet w = x@Wrapping<u32>", "u64", "Wrapping<u32>")]
    [InlineData("let w: Wrapping<u32> = 1\nlet x = w@i64", "Wrapping<u32>", "i64")]
    [InlineData("let w: Wrapping<u32> = 1\nlet x = w@f32", "Wrapping<u32>", "f32")]
    public void ConversionAcrossTheArgumentExplainsTheSameArgumentRule(string source, string actual, string expected)
    {
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal(nameof(Kimi.DiagnosticCode.TypeMismatch_Kd), error.Code);
        Assert.Equal($"expected {expected}, found {actual}", error.Label);
        Assert.Equal(Binding.WrappingConversionNote, error.Note);
    }

    [Theory]
    [MemberData(nameof(Executable))]
    public void ArithmeticWrapsAndOtherOperatorsFollowTheArgument(string type, int width, bool signed, string minimum, string maximum)
    {
        _ = width;
        var source = $"var a: Wrapping<{type}> = {maximum}\na += 1\nvar b: Wrapping<{type}> = {minimum}\nb -= 1\nvar c: Wrapping<{type}> = {maximum}\nc *= 2\n" +
            $"let d: Wrapping<{type}> = {minimum}\nlet e = -d\nvar f: Wrapping<{type}> = {maximum}\nlet g = f++\nlet h = --f\nlet lowest: Wrapping<{type}> = {maximum}\nlet doubledMax = lowest + lowest\n" +
            $"if a == {minimum} and b == {maximum} and c == doubledMax and e == {(signed ? minimum : "0")} and g == {maximum} and f == {maximum} and h == {maximum} => Console.writeLine(\"ok\")\n" +
            $"let x: Wrapping<{type}> = 7\nvar y: Wrapping<{type}> = 2\n" +
            $"if x / y == 3 and x % y == 1 and (x << 1) == 14 and (x >> 1) == 3 and (x & y) == 2 and (x | y) == 7 and (x ^ y) == 5 and x > y and y < x and x >= x and y <= y and x != y and x@{type} == 7 and 7@{type}@Wrapping<{type}> == x => Console.writeLine(\"ok\")\n" +
            (signed
                ? $"var m: Wrapping<{type}> = {minimum}\nvar n: Wrapping<{type}> = -1\nif m / n == {minimum} and m % n == 0 and m / -1 == {minimum} and m % -1 == 0 and -m == {minimum} and (m / 1) == {minimum} and 7 / n == -7 => Console.writeLine(\"ok\")\n"
                : $"var m: Wrapping<{type}> = {maximum}\nvar n: Wrapping<{type}> = 1\nif m / n == {maximum} and m % n == 0 and -n == {maximum} and (m + n) == 0 => Console.writeLine(\"ok\")\n");
        ScalarEmissionTest.EmitFixture("WrappingValues" + type, source, "ok\nok\nok\n");
    }

    // a wraps to the minimum, b to the maximum; the shift of b halves the maximum by the argument's signedness.
    [Theory]
    [InlineData("i128", "-170141183460469231731687303715884105728", "170141183460469231731687303715884105727", "85070591730234615865843651857942052863")]
    [InlineData("u128", "0", "340282366920938463463374607431768211455", "170141183460469231731687303715884105727")]
    public void WideWrappingArithmeticExecutes(string type, string minimum, string maximum, string halfMaximum)
    {
        var source = $"var a: Wrapping<{type}> = {maximum}\na += 1\nvar b: Wrapping<{type}> = {minimum}\nb -= 1\nvar c: Wrapping<{type}> = {maximum}\nc *= 2\nlet twice: Wrapping<{type}> = {maximum}\n" +
            $"let d = -a\nif a == {minimum} and b == {maximum} and c == twice + twice and d == {(type[0] == 'i' ? minimum : "0")} and (b >> 1) == {halfMaximum} and (a >> 1) == {(type[0] == 'i' ? "-85070591730234615865843651857942052864" : "0")} => Console.writeLine(\"ok\")";
        ScalarEmissionTest.EmitFixture("WrappingValues" + type, source, "ok\n");
    }

    [Theory]
    [MemberData(nameof(Executable))]
    public void UndefinedInputsStillAbort(string type, int width, bool signed, string minimum, string maximum)
    {
        _ = signed;
        _ = minimum;
        _ = maximum;
        ScalarEmissionTest.EmitFixture("WrappingFail" + type + "DivideZero", $"var x: Wrapping<{type}> = 1\nx /= 0\nConsole.writeLine(\"bad\")", string.Empty, 1, $"Hello.kimi:2:1: abort {DivisionZero}\n");
        ScalarEmissionTest.EmitFixture("WrappingFail" + type + "RemainderZero", $"var x: Wrapping<{type}> = 1\nvar d: Wrapping<{type}> = 0\nx %= d\nConsole.writeLine(\"bad\")", string.Empty, 1, $"Hello.kimi:3:1: abort {DivisionZero}\n");
        ScalarEmissionTest.EmitFixture("WrappingFail" + type + "ShiftCount", $"var x: Wrapping<{type}> = 1\nvar n: i32 = {width}\nx <<= n\nConsole.writeLine(\"bad\")", string.Empty, 1, $"Hello.kimi:3:1: abort {ShiftCount}\n");
    }

    [Fact]
    public void WrappingArithmeticEmitsPlainInstructions()
    {
        var ir = ScalarEmissionTest.EmitFixture("WrappingInstructions", "var a: Wrapping<i32> = 2147483647\nvar b: Wrapping<i32> = 2\nlet s = a + b\nlet p = a * b\nlet n = -a\nlet q = a / b\nlet r = a % b\nif s == -2147483647 and p == -2 and n == -2147483647 and q == 1073741823 and r == 1 => Console.writeLine(\"ok\")", "ok\n");
        Assert.Contains(" = add i32 ", ir);
        Assert.Contains(" = mul i32 ", ir);
        Assert.Contains(" = sub i32 0, ", ir);
        Assert.Contains(" = sdiv i32 ", ir);
        Assert.Contains(" = srem i32 ", ir);
        Assert.Contains("%safe", ir);
        Assert.Contains("%neg", ir);
        Assert.DoesNotContain("call { i32, i1 } @llvm.", ir); // The prelude declares the intrinsics; no body calls one.
        Assert.DoesNotContain(" nsw ", ir);
        Assert.DoesNotContain(" nuw ", ir);
        Assert.DoesNotContain("%minimum", ir); // No minimum / -1 Abort check: that quotient wraps.
    }

    [Fact]
    public void WrappingValuesFormatAndStoreLikeTheirArgument()
    {
        const string source = "struct Pair<T>\n    T is PrimitiveInteger\n    public let value: Wrapping<T>\n    public let tag: u8\n    public init(value: Wrapping<T>, tag: u8)\n        self.value = value\n        self.tag = tag\n" +
            "func twice(x: Wrapping<u16>) -> Wrapping<u16> => x * 2\n" +
            "let p = Pair<u8>.init(250, 1)\nlet q = p.value + 10\nvar a: [2 of Wrapping<u8>] = [255, 1]\na[0] += 1\na[1]++\nlet x: Wrapping<u8> = 255\nlet y: Wrapping<i8> = -1\n" +
            "if q == 4 and p.tag == 1 and a[0] == 0 and a[1] == 2 and twice(40000) == 14464 => Console.writeLine(\"\\(x) \\(y)\")";
        var ir = ScalarEmissionTest.EmitFixture("WrappingStorage", source, "255 -1\n");
        Assert.Contains("i16 @", ir); // twice passes and returns the i16 representation of u16.
    }

    // SPEC 13.5.4.2: a direct literal fits Wrapping<U> by the range of U; a failure is a compile-time error.
    [Fact]
    public void DirectLiteralsFitTheArgumentRange()
    {
        var c = MinimalEmissionTest.Analyze("let w = 5@Wrapping<u32>\nlet s = -5@Wrapping<i8>\nlet g = (-128)@Wrapping<i8>");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var variables = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<VariableKoto>().ToDictionary(x => x.NameKoto.IdentifierName, x => x);
        Assert.Same(BoundType.WrappingOf(BoundType.Primitives["u32"]), variables["w"].NameKoto.BoundType);
        Assert.Same(BoundType.WrappingOf(BoundType.Primitives["i8"]), variables["s"].NameKoto.BoundType);
        Assert.Same(BoundType.WrappingOf(BoundType.Primitives["i8"]), variables["g"].NameKoto.BoundType);

        // -(1) is an ordinary negation typed as i32 before the conversion, which is then the same-argument error.
        var negated = MinimalEmissionTest.Analyze("let m = -(1)@Wrapping<u32>");
        Assert.False(negated.Binding.Result.IsComplete);
        Assert.Contains(negated.Binding.Issues, x => x.Code == Kimi.DiagnosticCode.TypeMismatch_Kd);
        foreach (var invalid in new[] { "let w = 300@Wrapping<u8>", "let w = -1@Wrapping<u32>", "let w = 128@Wrapping<i8>" })
        {
            var rejected = MinimalEmissionTest.Analyze(invalid);
            Assert.False(rejected.Binding.Result.IsComplete);
            Assert.Contains(rejected.Binding.Issues, x => x.Code == Kimi.DiagnosticCode.InvalidNumericLiteral_Kd);
        }
    }

    // SPEC 14.8.1, 12.3.4: literal Patterns and Dictionary literal keys fit a wrapping Type by its argument's range.
    [Fact]
    public void LiteralPatternsAndDictionaryKeysUseTheArgumentRange()
    {
        const string source = "let w: Wrapping<u32> = 0xFFFF_FFFF\nlet r = match w\n    0xFFFF_FFFF => 1\n    0 => 2\n    _ => 0\n" +
            "let d: Dictionary<Wrapping<u8>, i32> = [1: 10, 255: 20]\nlet s: Wrapping<i8> = -1\nlet t = match s\n    -1 => 3\n    _ => 0\n" +
            "if r == 1 and d[255] == 20 and t == 3 => Console.writeLine(\"ok\")";
        ScalarEmissionTest.EmitFixture("WrappingPatternsAndKeys", source, "ok\n");

        var duplicate = MinimalEmissionTest.Analyze("let d: Dictionary<Wrapping<u8>, i32> = [1: 10, 1: 20]");
        Assert.False(duplicate.Binding.Result.IsComplete);
        Assert.Contains(duplicate.Binding.Issues, x => x.Code == Kimi.DiagnosticCode.DuplicateDictionaryKey_Kd);
        foreach (var invalid in new[] { "let w: Wrapping<u32> = 1\nlet r = match w\n    -1 => 1\n    _ => 0", "let w: Wrapping<u8> = 1\nlet r = match w\n    256 => 1\n    _ => 0" })
        {
            Assert.False(MinimalEmissionTest.Analyze(invalid).Binding.Result.IsComplete);
        }
    }

    // SPEC 8.4.7.3, 10.2.1: a generic body uses the wrapping operators, unary minus, generic literals, the conversions to
    // and from T and the value read; each instance uses its wrapping Scalar.
    [Fact]
    public void GenericBodiesUseTheWrappingScalarOfEachInstance()
    {
        const string source = "func scramble<T>(value: Wrapping<T>, key: Wrapping<T>) -> Wrapping<T>\n    T is PrimitiveInteger\n    return -(value ^ key) * 31\n" +
            "func halve<T>(value: ref/Wrapping<T>) -> Wrapping<T>\n    T is PrimitiveInteger\n    return value >> 1\n" +
            "func same<T>(a: Wrapping<T>, b: Wrapping<T>) -> bool\n    T is PrimitiveInteger\n    return a == b and not (a < b) and a <= b\n" +
            "func enter<T>(value: T) -> Wrapping<T>\n    T is PrimitiveInteger\n    var w = value@Wrapping<T>\n    w += 1\n    w++\n    return w - 1\n" +
            "func leave<T>(value: Wrapping<T>) -> T\n    T is PrimitiveInteger\n    let literal: Wrapping<T> = 127\n    let negated: Wrapping<T> = -(1)\n    if value == literal and negated == -(1) => return value@T\n    return (value / 1)@T\n" +
            "let a: Wrapping<u8> = 200\nlet b: Wrapping<i32> = -2147483648\n" +
            "if scramble(a, 3) == 107 and halve(a@ref) == 100 and same(a, a) and enter(255@u8) == 0 and leave(a) == 200 and scramble(b, 0) == -2147483648 and halve(b@ref) == -1073741824 and enter(2147483647) == -2147483648 => Console.writeLine(\"\\(scramble(a, 3)) \\(halve(b@ref))\")";
        ScalarEmissionTest.EmitFixture("WrappingGeneric", source, "107 -1073741824\n");
    }

    [Theory]
    [InlineData("func f<T>(x: Wrapping<T>) -> T\n    T is PrimitiveInteger\n    return x")] // No implicit conversion.
    [InlineData("func f<T>(x: T) -> Wrapping<T>\n    T is PrimitiveInteger\n    return x")]
    [InlineData("func f<T>(x: Wrapping<T>) -> Wrapping<T> => x")] // Formation needs the proof.
    [InlineData("func f<T>(x: Wrapping<T>, n: Wrapping<T>) -> Wrapping<T>\n    T is PrimitiveInteger\n    return x << n")] // Never a shift count.
    [InlineData("func f<T>(x: Wrapping<T>) -> Wrapping<T>\n    T is PrimitiveInteger\n    let v: [3 of i32] = [1, 2, 3]\n    return x + v[x]")] // Never a position.
    [InlineData("func f<T>(x: Wrapping<T>) -> Wrapping<T>\n    T is PrimitiveInteger\n    return x + 128")] // Generic literals stop at 127.
    [InlineData("func f<T>(x: Wrapping<T>) -> Wrapping<T>\n    T is PrimitiveInteger\n    return x + -1")] // A signed literal cannot fit every instance.
    [InlineData("func f<T>(x: Wrapping<T>) -> Wrapping<u8>\n    T is PrimitiveInteger\n    return x@Wrapping<u8>")]
    public void InvalidGenericFormsAreRejected(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
    }
}
