// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Language;
using Xunit;

namespace XunitTest;

public sealed class TokenHelperScanNumberLiteralTest
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("abc", 0)]
    [InlineData("_123", 0)]
    [InlineData(".123", 0)]
    [InlineData("+123", 0)]
    [InlineData("-123", 0)]
    public void ScanNumberLiteral_NotStartedWithDigit_ReturnsFalseAndZeroLength(
        string text,
        int expectedLength)
    {
        var result = TokenHelper.ScanNumberLiteral(text, out var length);

        Assert.False(result);
        Assert.Equal(expectedLength, length);
    }

    [Theory]
    [InlineData("0", 1)]
    [InlineData("1", 1)]
    [InlineData("123", 3)]
    [InlineData("123_456", 7)]
    [InlineData("1_2_3", 5)]
    [InlineData("123 ", 3)]
    [InlineData("123+", 3)]
    [InlineData("123.", 3)]
    [InlineData("123..456", 3)]
    [InlineData("123.foo", 3)]
    public void ScanNumberLiteral_DecimalInteger_ReturnsTrue(
        string text,
        int expectedLength)
    {
        var result = TokenHelper.ScanNumberLiteral(text, out var length);

        Assert.True(result);
        Assert.Equal(expectedLength, length);
    }

    [Theory]
    [InlineData("0u8", 3)]
    [InlineData("0i8", 3)]
    [InlineData("123u16", 6)]
    [InlineData("123i16", 6)]
    [InlineData("123u32", 6)]
    [InlineData("123i32", 6)]
    [InlineData("123u64", 6)]
    [InlineData("123i64", 6)]
    [InlineData("123u128", 7)]
    [InlineData("123i128", 7)]
    [InlineData("123usize", 8)]
    [InlineData("123isize", 8)]
    [InlineData("123u8+", 5)]
    public void ScanNumberLiteral_IntegerSuffix_ReturnsTrue(
        string text,
        int expectedLength)
    {
        var result = TokenHelper.ScanNumberLiteral(text, out var length);

        Assert.True(result);
        Assert.Equal(expectedLength, length);
    }

    [Theory]
    [InlineData("1.0", 3)]
    [InlineData("1.23", 4)]
    [InlineData("1_000.5_0", 9)]
    [InlineData("1.0f32", 6)]
    [InlineData("1.0f64", 6)]
    [InlineData("1.0+", 3)]
    [InlineData("1.0;", 3)]
    public void ScanNumberLiteral_Fraction_ReturnsTrue(
        string text,
        int expectedLength)
    {
        var result = TokenHelper.ScanNumberLiteral(text, out var length);

        Assert.True(result);
        Assert.Equal(expectedLength, length);
    }

    [Theory]
    [InlineData("1e0", 3)]
    [InlineData("1e10", 4)]
    [InlineData("1E10", 4)]
    [InlineData("1e+10", 5)]
    [InlineData("1e-10", 5)]
    [InlineData("1_2e3_4", 7)]
    [InlineData("1.2e3", 5)]
    [InlineData("1.2e+3", 6)]
    [InlineData("1e10f32", 7)]
    [InlineData("1e10f64", 7)]
    public void ScanNumberLiteral_Exponent_ReturnsTrue(
        string text,
        int expectedLength)
    {
        var result = TokenHelper.ScanNumberLiteral(text, out var length);

        Assert.True(result);
        Assert.Equal(expectedLength, length);
    }

    [Theory]
    [InlineData("0b0", 3)]
    [InlineData("0b1", 3)]
    [InlineData("0b1010", 6)]
    [InlineData("0B1010", 6)]
    [InlineData("0b1010u8", 8)]
    [InlineData("0b1010i32", 9)]
    [InlineData("0b1010usize", 11)]
    [InlineData("0b1010+", 6)]
    public void ScanNumberLiteral_BinaryInteger_ReturnsTrue(
        string text,
        int expectedLength)
    {
        var result = TokenHelper.ScanNumberLiteral(text, out var length);

        Assert.True(result);
        Assert.Equal(expectedLength, length);
    }

    [Theory]
    [InlineData("0o0", 3)]
    [InlineData("0o7", 3)]
    [InlineData("0o755", 5)]
    [InlineData("0O755", 5)]
    [InlineData("0o755u16", 8)]
    [InlineData("0o755+", 5)]
    public void ScanNumberLiteral_OctalInteger_ReturnsTrue(
        string text,
        int expectedLength)
    {
        var result = TokenHelper.ScanNumberLiteral(text, out var length);

        Assert.True(result);
        Assert.Equal(expectedLength, length);
    }

    [Theory]
    [InlineData("0x0", 3)]
    [InlineData("0x9", 3)]
    [InlineData("0xa", 3)]
    [InlineData("0xA", 3)]
    [InlineData("0xdead_beef", 11)]
    [InlineData("0XDEAD_BEEF", 11)]
    [InlineData("0xFFu8", 6)]
    [InlineData("0xFFi64", 7)]
    [InlineData("0xFF+", 4)]
    public void ScanNumberLiteral_HexInteger_ReturnsTrue(
        string text,
        int expectedLength)
    {
        var result = TokenHelper.ScanNumberLiteral(text, out var length);

        Assert.True(result);
        Assert.Equal(expectedLength, length);
    }

    [Theory]
    [InlineData("0x", 2)]
    [InlineData("0X", 2)]
    [InlineData("0b", 2)]
    [InlineData("0B", 2)]
    [InlineData("0o", 2)]
    [InlineData("0O", 2)]
    [InlineData("1e", 2)]
    [InlineData("1E", 2)]
    [InlineData("1e+", 3)]
    [InlineData("1e-", 3)]
    public void ScanNumberLiteral_IncompleteNumber_ReturnsFalseAndConsumesMalformedLiteral(
        string text,
        int expectedLength)
    {
        var result = TokenHelper.ScanNumberLiteral(text, out var length);

        Assert.False(result);
        Assert.Equal(expectedLength, length);
    }

    [Theory]
    [InlineData("123abc", 6)]
    [InlineData("123_u8x", 7)]
    [InlineData("1u8x", 4)]
    [InlineData("1usizeX", 7)]
    [InlineData("1.0u8", 5)]
    [InlineData("1.0f16", 6)]
    [InlineData("1.0f32x", 7)]
    [InlineData("1e10u8", 6)]
    [InlineData("0x1g", 4)]
    [InlineData("0b01u8x", 7)]
    [InlineData("0o77usizeX", 10)]
    public void ScanNumberLiteral_InvalidSuffixOrIdentifierContinuation_ReturnsFalseAndConsumesMalformedLiteral(
        string text,
        int expectedLength)
    {
        var result = TokenHelper.ScanNumberLiteral(text, out var length);

        Assert.False(result);
        Assert.Equal(expectedLength, length);
    }

    [Theory]
    [InlineData("0b2", 3)]
    [InlineData("0o8", 3)]
    [InlineData("0xg", 3)]
    [InlineData("0b2u8", 5)]
    [InlineData("0o8usize", 8)]
    [InlineData("0xg123", 6)]
    public void ScanNumberLiteral_BasedIntegerWithoutValidDigit_ReturnsFalseAndConsumesMalformedLiteral(
    string text,
    int expectedLength)
    {
        var result = TokenHelper.ScanNumberLiteral(text, out var length);

        Assert.False(result);
        Assert.Equal(expectedLength, length);
    }
}
