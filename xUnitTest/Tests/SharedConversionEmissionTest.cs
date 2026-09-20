// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class SharedConversionEmissionTest
{
    [Theory]
    [InlineData("SignedWiden", "i8", "i64", "-128")]
    [InlineData("UnsignedWiden", "u8", "i16", "255")]
    [InlineData("Narrow", "i16", "u8", "255")]
    [InlineData("EqualWidth", "u64", "i64", "9223372036854775807")]
    [InlineData("Signedness", "i64", "u64", "42")]
    [InlineData("SameRepresentation", "isize", "i64", "-7")]
    public void ExecutesValidConversions(string name, string sourceType, string targetType, string value)
    {
        var source = $"func convert<T>(value?: ref/T, n?: {sourceType}) -> {targetType} => n@{targetType}\nlet v = true\nrequire convert(v, {value}) == {value} else => $abort(\"conversion\")";
        ScalarEmissionTest.EmitFixture("SharedConversion" + name, source, string.Empty);
    }

    [Theory]
    [InlineData("Negative", "i64", "u8", "-1")]
    [InlineData("Large", "i64", "u8", "256")]
    [InlineData("EqualWidthOverflow", "u64", "i64", "18446744073709551615")]
    [InlineData("WidenNegative", "i8", "u16", "-1")]
    public void InvalidConversionsAbort(string name, string sourceType, string targetType, string value)
    {
        var header = $"func convert<T>(value?: ref/T, n?: {sourceType}) -> {targetType} => n@{targetType}";
        var source = header + $"\nlet v = true\nlet result = convert(v, {value})";
        var column = header.IndexOf("n@", StringComparison.Ordinal) + 1;
        ScalarEmissionTest.EmitFixture("SharedConversion" + name, source, string.Empty, 1, $"Hello.kimi:1:{column}: abort KIMI_E_INT_CONVERSION: Integer conversion out of range\n");
    }

    [Fact]
    public void ConditionalConversionsPreserveChecksAndResultArrivals()
        => ScalarEmissionTest.EmitFixture("SharedConversionPhi", "func convert<T>(value?: ref/T, flag?: bool, n?: i64) -> u8 => if flag => n@u8 else => 0\nlet v = true\nrequire convert(v, true, 255) == 255 and convert(v, false, -1) == 0 else => $abort(\"result\")", string.Empty);
}
