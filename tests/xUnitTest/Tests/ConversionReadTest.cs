// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// SPEC 3.5.3, 13.5.2: the operand of a numeric, wrapping or bit conversion, or of an Identity Acquisition to a read Type, is a
// read position: safe reference layers ending in a read Type supply its value. Borrow, follow, copy, move and object targets
// keep the reference.
public class ConversionReadTest
{
    [Theory]
    [InlineData("let x: u8 = 1\nlet r = x@ref\nlet w = r@u32", "u32", "Integer")]
    [InlineData("let x: u8 = 1\nlet r = x@ref\nlet w = r@u8", "u8", "Integer")] // The same-Type @ is the no-op conversion.
    [InlineData("let x: u8 = 1\nlet r = x@ref\nlet w = r@owner/u8", "u8", "Identity")] // An explicit owning target is Identity Acquisition.
    [InlineData("let x: u8 = 1\nlet r = x@ref\nlet w = r@Wrapping<u8>", "Wrapping<u8>", "Integer")]
    [InlineData("let x: u8 = 1\nlet r = x@ref\nlet w = r@wrap<Wrapping<u32>>", "Wrapping<u32>", "Wrap")]
    [InlineData("let x: u8 = 1\nlet r = x@ref\nlet w = r@wrap<i8>", "i8", "Wrap")]
    [InlineData("let f: f32 = 1.5\nlet r = f@ref\nlet w = r@bits<u32>", "u32", "Bits")]
    [InlineData("let n: u32 = 1\nlet r = n@ref\nlet w = r@bits<f32>", "f32", "Bits")]
    [InlineData("let f: f32 = 1.5\nlet r = f@ref\nlet w = r@f64", "f64", "Floating")]
    [InlineData("let d: f64 = 1.5\nlet r = d@ref\nlet w = r@i32", "i32", "Numeric")]
    [InlineData("var x: u8 = 1\nlet u = x@uniq\nlet w = u@i32", "i32", "Integer")] // An exclusive layer.
    [InlineData("let x: i32 = 1\nlet r = x@ref\nlet rr = r@ref\nlet w = rr@i64", "i64", "Integer")] // Two layers.
    [InlineData("let w: Wrapping<u16> = 1\nlet r = w@ref\nlet v = r@u16", "u16", "Integer")]
    [InlineData("func f(bytes: Slice<u8>) -> u32\n    var h: u32 = 0\n    for b in bytes\n        h += b@u32\n    return h\nlet a: [2 of u8] = [1, 2]\nlet w = f(a[..])", "u32", "Integer")]
    public void ReferenceOperandsSupplyTheirValue(string source, string type, string binding)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var conversion = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ConversionKoto>().Last();
        var expected = type.StartsWith("Wrapping<", StringComparison.Ordinal) ? BoundType.WrappingOf(BoundType.Primitives[type[9..^1]]) : BoundType.Primitives[type];
        Assert.Same(expected, conversion.BoundType);
        Assert.Equal(binding, conversion.ConversionBinding.ToString());
        Assert.True(c.Binding.ReadsReferent(conversion.Left));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
    }

    [Fact]
    public void GenericReferenceOperandsAreReadUnderTheirConstraint()
    {
        const string source = "func total<T>(values: Slice<T>) -> T\n    T is PrimitiveInteger\n    var sum: Wrapping<T> = 0\n    for value in values\n        sum += value@Wrapping<T>\n    return sum@T\n" +
            "func widen<T>(value: ref/T) -> i64\n    T is PrimitiveInteger\n    return value@i64\n" +
            "let bytes: [3 of u8] = [250, 10, 1]\nlet n: u8 = 7\nif total(bytes[..]) == 5 and widen(n) == 7 => Console.writeLine(\"ok\")";
        var ir = ScalarEmissionTest.EmitFixture("ConversionReadGeneric", source, "ok\n");
        Assert.Contains("load i8, ptr", ir);
    }

    [Fact]
    public void ReadsExecuteThroughEveryLayerAndConversion()
    {
        const string source = "func hash(bytes: Slice<u8>) -> u32\n    var h: Wrapping<u32> = 0x811C_9DC5\n    for b in bytes\n        h = (h ^ b@wrap<Wrapping<u32>>) * 0x0100_0193\n    return h@u32\n" +
            "func widen(value: ref/u8) -> i64 => value@i64\nfunc same(value: ref/i32) -> i32 => value@i32\nfunc bits(value: ref/f32) -> u32 => value@bits<u32>\n" +
            "let text = \"Kimigayo\"\nlet x: u8 = 200\nvar y: i32 = -5\nlet f: f32 = 1.5\nlet s: i16 = -3\nlet inner = s@ref\nlet outer = inner@ref\n" +
            "if hash(Text.utf8(text).bytes()) == 1093368299 and widen(x) == 200 and same(y) == -5 and bits(f) == 0x3FC0_0000 and outer@i32 == -3 => Console.writeLine(\"ok\")";
        ScalarEmissionTest.EmitFixture("ConversionReadLayers", source, "ok\n");
    }

    [Theory]
    [InlineData("let x: u8 = 1\nlet r = x@ref\nlet c = r@copy")] // Copies the reference.
    [InlineData("let x: u8 = 1\nlet r = x@ref\nlet c = r@ref")] // Reborrows.
    [InlineData("let x: u8 = 1\nlet r = x@ref\nlet c = r@follow")] // Selects the referent Place explicitly.
    public void ReferenceTargetsKeepTheReference(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var conversion = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ConversionKoto>().Last();
        Assert.False(c.Binding.ReadsReferent(conversion.Left));
    }

    [Fact]
    public void ACopyReferentThatIsNotAReadTypeIsNotExtracted()
    {
        var c = MinimalEmissionTest.Analyze("struct Point\n    Self is Copy\n    public let x: i32\n    public init(x: i32) => self.x = x\nlet p = Point.init(1)\nlet r = p@ref\nlet q = r@Point");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node is ConversionKoto);
    }
}
