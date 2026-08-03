// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Xunit;

namespace XunitTest;

public class TokenHelperNumberLiteralTest
{
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("123")]
    [InlineData("1_2")]
    [InlineData("1__2")]
    [InlineData("1__")]
    [InlineData("123____")]
    [InlineData("1_1.23")]
    [InlineData("1.0_12_")]
    [InlineData("1_1.23e2_123")]
    [InlineData("1e0")]
    [InlineData("1e+10")]
    [InlineData("1e-10")]
    [InlineData("1e1__0")]
    [InlineData("1e10____")]
    [InlineData("1.25e+2_000")]
    public void ScanNumberLiteral_ValidDecimalLiteral(
        string text)
    {
        var result = TokenHelper.ScanNumberLiteral(
            text.AsSpan(),
            out var length);

        Assert.True(result);
        Assert.Equal(text.Length, length);
    }

    [Theory]
    [InlineData("0b")]
    [InlineData("0b_")]
    [InlineData("0b____")]
    [InlineData("0b0")]
    [InlineData("0b_1010")]
    [InlineData("0b____1010____")]
    [InlineData("0B_1010")]
    [InlineData("0o")]
    [InlineData("0o_")]
    [InlineData("0o____")]
    [InlineData("0o755")]
    [InlineData("0o_755")]
    [InlineData("0O_755")]
    [InlineData("0x")]
    [InlineData("0x_")]
    [InlineData("0x____")]
    [InlineData("0x1200AABB")]
    [InlineData("0x_1200_AABB__")]
    [InlineData("0X_dead_BEEF____")]
    public void ScanNumberLiteral_ValidBasedInteger(
        string text)
    {
        var result = TokenHelper.ScanNumberLiteral(
            text.AsSpan(),
            out var length);

        Assert.True(result);
        Assert.Equal(text.Length, length);
    }

    [Theory]
    [InlineData("1e")]
    [InlineData("1E")]
    [InlineData("1e+")]
    [InlineData("1e-")]
    [InlineData("1e_2")]
    [InlineData("1E_2")]
    [InlineData("1e+_2")]
    [InlineData("1e-_2")]
    [InlineData("123abc")]
    [InlineData("123i128")]
    [InlineData("1.0f64")]
    [InlineData("0b2")]
    [InlineData("0b102")]
    [InlineData("0b_102")]
    [InlineData("0o8")]
    [InlineData("0o789")]
    [InlineData("0xG")]
    [InlineData("0x12G")]
    [InlineData("0x1i128")]
    public void ScanNumberLiteral_InvalidLiteral(
        string text)
    {
        var result = TokenHelper.ScanNumberLiteral(
            text.AsSpan(),
            out var length);

        Assert.False(result);
        Assert.Equal(text.Length, length);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("_", 0)]
    [InlineData("_1", 0)]
    [InlineData(".1", 0)]
    [InlineData("abc", 0)]
    [InlineData("+1", 0)]
    [InlineData("-1", 0)]
    public void ScanNumberLiteral_DoesNotStartWithDigit(
        string text,
        int expectedLength)
    {
        var result = TokenHelper.ScanNumberLiteral(
            text.AsSpan(),
            out var length);

        Assert.False(result);
        Assert.Equal(expectedLength, length);
    }

    [Theory]
    [InlineData("1.", 1)]
    [InlineData("1._2", 1)]
    [InlineData("1.foo", 1)]
    [InlineData("123..456", 3)]
    [InlineData("123.abc", 3)]
    [InlineData("0x.", 2)]
    [InlineData("0x.foo", 2)]
    public void ScanNumberLiteral_StopsBeforeFollowingToken(
        string text,
        int expectedLength)
    {
        var result = TokenHelper.ScanNumberLiteral(
            text.AsSpan(),
            out var length);

        Assert.True(result);
        Assert.Equal(expectedLength, length);
    }

    [Theory]
    [InlineData("1.0", 3)]
    [InlineData("1.0_12_", 7)]
    [InlineData("1.0.member", 3)]
    [InlineData("1e2+", 3)]
    [InlineData("1e2-3", 3)]
    [InlineData("0b1010+1", 6)]
    [InlineData("0o755/2", 5)]
    [InlineData("0xFF.A", 4)]
    public void ScanNumberLiteral_ConsumesOnlyNumericLiteral(
        string text,
        int expectedLength)
    {
        var result = TokenHelper.ScanNumberLiteral(
            text.AsSpan(),
            out var length);

        Assert.True(result);
        Assert.Equal(expectedLength, length);
    }
}
