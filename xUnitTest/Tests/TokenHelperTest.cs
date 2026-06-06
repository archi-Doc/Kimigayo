// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimigayo.Language;
using Xunit;

namespace xUnitTest;

public sealed class TokenHelperTest
{
    [Theory]
    [InlineData("0", true, 1)]
    [InlineData("123", true, 3)]
    [InlineData("123u32", true, 6)]
    [InlineData("1_000", true, 5)]
    [InlineData("1.0", true, 3)]
    [InlineData("1.", true, 2)]
    [InlineData("1.23f32", true, 7)]
    [InlineData("1.23f64", true, 7)]
    [InlineData("1e10", true, 4)]
    [InlineData("1e+10", true, 5)]
    [InlineData("1e-10", true, 5)]
    [InlineData("1e1_0", true, 5)]
    [InlineData("0b1010", true, 6)]
    [InlineData("0b_1010", true, 7)] // allowed by your policy
    [InlineData("0o755", true, 5)]
    [InlineData("0o_755", true, 6)] // allowed by your policy
    [InlineData("0xff", true, 4)]
    [InlineData("0x_ff", true, 5)] // allowed by your policy
    [InlineData("0xff_u64", true, 8)]
    [InlineData("0x_ff_u64", true, 9)]
    [InlineData("1..2", true, 1)]
    [InlineData("1.foo", true, 1)]
    [InlineData("-123", false, 0)]
    [InlineData("", false, 0)]
    [InlineData("abc", false, 0)]
    [InlineData("0b", false, 0)]
    [InlineData("0b_", false, 0)]
    [InlineData("0x", false, 0)]
    [InlineData("0x_", false, 0)]
    [InlineData("1e", false, 0)]
    [InlineData("1e+", false, 0)]
    [InlineData("1e-", false, 0)]
    [InlineData("1e_2", true, 4)] // current implementation allows this
    [InlineData("123abc", false, 0)]
    [InlineData("1.0abc", false, 0)]
    [InlineData("1.0u32", false, 0)]
    [InlineData("123f32", false, 0)]
    [InlineData("0xg", false, 0)]
    [InlineData("0b2", false, 0)]
    public void ScanNumberLiteral_ReturnsExpectedLength(string input, bool expectedResult, int expectedLength)
    {
        var result = TokenHelper.ScanNumberLiteral(input.AsSpan(), out var length);

        Assert.Equal(expectedResult, result);
        Assert.Equal(expectedLength, length);
    }
}
