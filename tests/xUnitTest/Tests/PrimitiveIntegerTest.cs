// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 8.4.7.3: a Type parameter proven PrimitiveInteger uses the built-in integer operators, comparisons,
/// conversions, Scalar read, formatting and literals 0 through 127; each instance executes its concrete Type's operations.</summary>
public class PrimitiveIntegerTest
{
    private const string Operations =
        "func sum<T>(values: Slice<T>) -> T\n    T is PrimitiveInteger\n    var total: T = 0\n    for value in values\n        total += value\n    return total\n" +
        "func clamp<T>(value: T, low: T, high: T) -> T\n    T is PrimitiveInteger\n    if value < low => return low\n    if value > high => return high\n    return value\n" +
        "func widen<T>(value: T) -> i64\n    T is PrimitiveInteger\n    return value@i64\n" +
        "func narrow<T>(value: i64) -> T\n    T is PrimitiveInteger\n    return value@T\n" +
        "func mix<T>(value: T) -> T\n    T is PrimitiveInteger\n    var x = value\n    x += 1\n    x = (x << 1) | 1\n    x = x & 127\n    x++\n    return +x % 100\n" +
        "func describe<T>(value: T) -> string\n    T is PrimitiveInteger\n    return \"value \\(value)\"\n" +
        "func same<T>(a: T, b: T) -> bool\n    T is PrimitiveInteger\n    return a == b and not (a != b) and a <= b and a >= b\n" +
        "func largest<T>(a: T, b: T) -> T\n    T is Comparable and Copy\n    return if a < b => b else => a\n" +
        "func forward<T>(a: T, b: T) -> T\n    T is PrimitiveInteger\n    return largest(a, b)\n" +
        "let bytes: [3 of u8] = [1, 2, 3]\nlet wide: [3 of i64] = [100, 200, 300]\nlet big: [2 of i128] = [1, 2]\n" +
        "require sum(bytes[..]) == 6 and sum(wide[..]) == 600 and sum(big[..]) == 3 else => $abort(\"sum\")\n" +
        "require clamp(-5, 0, 10) == 0 and clamp(15@u16, 0, 10) == 10 and clamp(7@isize, 0, 10) == 7 else => $abort(\"clamp\")\n" +
        "require widen(200@u8) == 200 and widen(-3) == -3 and narrow<u8>(255) == 255@u8 else => $abort(\"convert\")\n" +
        "require mix(3@u8) == 10 and mix(3) == 10 and mix(3@u64) == 10 else => $abort(\"mix\")\n" +
        "require describe(7@u8) == \"value 7\" and describe(-2@i16) == \"value -2\" else => $abort(\"describe\")\n" +
        "require same(4@i128, 4) and not same(1@usize, 2) and forward(3@u32, 9) == 9 else => $abort(\"compare\")\n" +
        "Console.writeLine(\"ok\")";

    [Fact]
    public void GenericBodiesUseEachInstanceOperations()
        => ScalarEmissionTest.EmitFixture("PrimitiveIntegerOperations", Operations, "ok\n");

    [Fact]
    public void NarrowingAnInstanceChecksItsRange()
        => ScalarEmissionTest.EmitFixture(
            "PrimitiveIntegerNarrowAbort",
            "func narrow<T>(value: i64) -> T\n    T is PrimitiveInteger\n    return value@T\nConsole.writeLine(\"before\")\nlet x = narrow<u8>(256)\nConsole.writeLine(\"after\")",
            "before\n",
            1,
            "Hello.kimi:3:12: abort KIMI_E_INT_CONVERSION: Integer conversion out of range\n");

    [Theory]
    [InlineData("func negate<T>(value: T) -> T\n    T is PrimitiveInteger\n    return -value")]
    [InlineData("func big<T>() -> T\n    T is PrimitiveInteger\n    return 128")]
    [InlineData("func negative<T>() -> T\n    T is PrimitiveInteger\n    return -1")]
    [InlineData("func fraction<T>() -> T\n    T is PrimitiveInteger\n    return 1.5")]
    [InlineData("func twice<T>(value: T) -> T\n    return value + value")]
    [InlineData("func twice<T>(value: T) -> T\n    T is i8 or u8\n    return value + value")]
    [InlineData("func keep<T>(value: T) -> T\n    T is PrimitiveInteger\n    return value\nlet x = keep(1.5)")]
    [InlineData("func keep<T>(value: T) -> T\n    T is PrimitiveInteger\n    return value\nlet x = keep(true)")]
    [InlineData("func keep<T>(value: T) -> T\n    T is PrimitiveInteger\n    return value\nlet n = 1\nlet x = keep(n@ref)")]
    [InlineData("struct Number\n    Self is PrimitiveInteger\n    public let value: i32 = 0")]
    public void RejectsWhatTheRequirementDoesNotProve(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void WideDivisionIsDiagnosedForTheInstance()
    {
        // SPEC 8.4.7.3: the generic body is valid; the initial profile refuses its 128-bit instance before optimization.
        var c = MinimalEmissionTest.Analyze("func half<T>(value: T) -> T\n    T is PrimitiveInteger\n    return value / 2\nlet a = half(10)\nlet b = half(10@i128)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out var error));
        Assert.Contains("Generic instance", error, StringComparison.Ordinal);
    }

    [Fact]
    public void RecognizesTheIntrinsicIdentity()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(KimiDeclarationState.Validated, c.Library.GetDeclarationState(KimiDeclarationId.PrimitiveInteger));
        Assert.Equal(IntrinsicKind.PrimitiveInteger, c.Library.PrimitiveInteger.Intrinsic);
    }
}
