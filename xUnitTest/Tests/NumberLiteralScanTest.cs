// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Xunit;

namespace XunitTest;

public class NumberLiteralScanTest
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("abc", 0)]
    [InlineData("_123", 0)]
    [InlineData("+123", 0)]
    [InlineData("-123", 0)]
    [InlineData(".123", 0)]
    public void DoesNotStartWithDigit_ReturnsFalse(
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
    [InlineData("000123", 6)]
    [InlineData("123_", 4)]
    [InlineData("123__", 5)]
    [InlineData("1_2_3", 5)]
    [InlineData("1__2___3", 8)]
    [InlineData("123+456", 3)]
    [InlineData("123.abc", 3)]
    [InlineData("123)", 3)]
    [InlineData("123 ", 3)]
    public void DecimalInteger_ReturnsTrue(
        string text,
        int expectedLength)
    {
        var result = TokenHelper.ScanNumberLiteral(text, out var length);

        Assert.True(result);
        Assert.Equal(expectedLength, length);
    }

    [Theory]
    [InlineData("0b", 2)]
    [InlineData("0B", 2)]
    [InlineData("0b_", 3)]
    [InlineData("0b____", 6)]
    [InlineData("0b0", 3)]
    [InlineData("0b1", 3)]
    [InlineData("0b1010", 6)]
    [InlineData("0B1010", 6)]
    [InlineData("0b_1010", 7)]
    [InlineData("0b__1010__", 10)]
    [InlineData("0b1010+1", 6)]
    public void BinaryInteger_ReturnsTrue(
        string text,
        int expectedLength)
    {
        var result = TokenHelper.ScanNumberLiteral(text, out var length);

        Assert.True(result);
        Assert.Equal(expectedLength, length);
    }

    [Theory]
    [InlineData("0o", 2)]
    [InlineData("0O", 2)]
    [InlineData("0o_", 3)]
    [InlineData("0o____", 6)]
    [InlineData("0o0", 3)]
    [InlineData("0o7", 3)]
    [InlineData("0o755", 5)]
    [InlineData("0O755", 5)]
    [InlineData("0o_755", 6)]
    [InlineData("0o__7_5_5__", 11)]
    [InlineData("0o755+1", 5)]
    public void OctalInteger_ReturnsTrue(
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
    [InlineData("0x_", 3)]
    [InlineData("0x____", 6)]
    [InlineData("0x0", 3)]
    [InlineData("0x9", 3)]
    [InlineData("0xa", 3)]
    [InlineData("0xF", 3)]
    [InlineData("0x0123456789abcdef", 18)]
    [InlineData("0X0123456789ABCDEF", 18)]
    [InlineData("0x_FF", 5)]
    [InlineData("0x__AA_BB__", 11)]
    [InlineData("0xFF+1", 4)]
    public void HexadecimalInteger_ReturnsTrue(
        string text,
        int expectedLength)
    {
        var result = TokenHelper.ScanNumberLiteral(text, out var length);

        Assert.True(result);
        Assert.Equal(expectedLength, length);
    }

    [Theory]
    [InlineData("0.0", 3)]
    [InlineData("1.0", 3)]
    [InlineData("123.456", 7)]
    [InlineData("1.2_", 4)]
    [InlineData("1.2__", 5)]
    [InlineData("1.2_3", 5)]
    [InlineData("1___.2___", 9)]
    [InlineData("1_.2", 4)]
    [InlineData("123.456+1", 7)]
    [InlineData("123.456)", 7)]
    public void DecimalFraction_ReturnsTrue(
        string text,
        int expectedLength)
    {
        var result = TokenHelper.ScanNumberLiteral(text, out var length);

        Assert.True(result);
        Assert.Equal(expectedLength, length);
    }

    [Theory]
    [InlineData("1e0", 3)]
    [InlineData("1E0", 3)]
    [InlineData("1e10", 4)]
    [InlineData("1E10", 4)]
    [InlineData("1e+10", 5)]
    [InlineData("1e-10", 5)]
    [InlineData("1E+10", 5)]
    [InlineData("1E-10", 5)]
    [InlineData("1e1_", 4)]
    [InlineData("1e1__", 5)]
    [InlineData("1e1_2_3", 7)]
    [InlineData("1__e10", 6)]
    [InlineData("1.25e10", 7)]
    [InlineData("1.25E+10", 8)]
    [InlineData("1_2.3__e-4__", 12)]
    [InlineData("1e10+2", 4)]
    public void DecimalExponent_ReturnsTrue(
        string text,
        int expectedLength)
    {
        var result = TokenHelper.ScanNumberLiteral(text, out var length);

        Assert.True(result);
        Assert.Equal(expectedLength, length);
    }

    [Theory]
    [InlineData("1.", 1)]
    [InlineData("1.+2", 1)]
    [InlineData("1._2", 1)]
    [InlineData("123.foo", 3)]
    public void DotWithoutFollowingDigit_IsNotPartOfLiteral(
        string text,
        int expectedLength)
    {
        var result = TokenHelper.ScanNumberLiteral(text, out var length);

        Assert.True(result);
        Assert.Equal(expectedLength, length);
    }

    [Theory]
    [InlineData("0b2", 3)]
    [InlineData("0b102", 5)]
    [InlineData("0b12abc", 7)]
    [InlineData("0bfoo", 5)]
    [InlineData("0b2+1", 3)]
    [InlineData("0B102", 5)]
    public void InvalidBinaryInteger_ReturnsFalse(
        string text,
        int expectedLength)
    {
        var result = TokenHelper.ScanNumberLiteral(text, out var length);

        Assert.False(result);
        Assert.Equal(expectedLength, length);
    }

    [Theory]
    [InlineData("0o8", 3)]
    [InlineData("0o789", 5)]
    [InlineData("0o8abc", 6)]
    [InlineData("0og", 3)]
    [InlineData("0o8+1", 3)]
    [InlineData("0O789", 5)]
    public void InvalidOctalInteger_ReturnsFalse(
        string text,
        int expectedLength)
    {
        var result = TokenHelper.ScanNumberLiteral(text, out var length);

        Assert.False(result);
        Assert.Equal(expectedLength, length);
    }

    [Theory]
    [InlineData("0xg", 3)]
    [InlineData("0xG", 3)]
    [InlineData("0x1g", 4)]
    [InlineData("0xFFxyz", 7)]
    [InlineData("0xg+1", 3)]
    [InlineData("0XFFG", 5)]
    public void InvalidHexadecimalInteger_ReturnsFalse(
        string text,
        int expectedLength)
    {
        var result = TokenHelper.ScanNumberLiteral(text, out var length);

        Assert.False(result);
        Assert.Equal(expectedLength, length);
    }

    [Theory]
    [InlineData("1e", 2)]
    [InlineData("1E", 2)]
    [InlineData("1e+", 3)]
    [InlineData("1e-", 3)]
    [InlineData("1e_2", 4)]
    [InlineData("1e__2", 5)]
    [InlineData("1e+_2", 5)]
    [InlineData("1e-_2", 5)]
    [InlineData("1eabc", 5)]
    [InlineData("1e+abc", 6)]
    [InlineData("1.0e", 4)]
    [InlineData("1.0e+", 5)]
    [InlineData("1.0e_2", 6)]
    public void InvalidExponent_ReturnsFalse(
        string text,
        int expectedLength)
    {
        var result = TokenHelper.ScanNumberLiteral(text, out var length);

        Assert.False(result);
        Assert.Equal(expectedLength, length);
    }

    [Theory]
    [InlineData("123abc", 6)]
    [InlineData("123i128", 7)]
    [InlineData("123f64", 6)]
    [InlineData("123_abc", 7)]
    [InlineData("1.0abc", 6)]
    [InlineData("1e10abc", 7)]
    [InlineData("0b101i128", 9)]
    [InlineData("0o755u64", 8)]
    [InlineData("0xFFu128", 8)]
    [InlineData("123日本語", 6)]
    public void IdentifierContinuation_ReturnsFalse(
        string text,
        int expectedLength)
    {
        var result = TokenHelper.ScanNumberLiteral(text, out var length);

        Assert.False(result);
        Assert.Equal(expectedLength, length);
    }

    [Theory]
    [InlineData("0b101.1", 5)]
    [InlineData("0o755.0", 5)]
    [InlineData("0xFF.0", 4)]
    public void BasedIntegerStopsOrFailsAtUnsupportedFloatSyntax(
        string text,
        int expectedLength)
    {
        var result = TokenHelper.ScanNumberLiteral(text, out var length);

        if (text[expectedLength] == '.')
        {
            Assert.True(result);
        }
        else
        {
            Assert.False(result);
        }

        Assert.Equal(expectedLength, length);
    }
}
