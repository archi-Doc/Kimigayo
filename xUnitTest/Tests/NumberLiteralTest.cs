// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using Kimi.Compiler.Helper;
using Xunit;

namespace XunitTest;

public class NumberLiteralHelperTest
{
    [Theory]
    [InlineData("0", "0")]
    [InlineData("1", "1")]
    [InlineData("9", "9")]
    [InlineData("10", "10")]
    [InlineData("123456789", "123456789")]
    [InlineData("1_234_567_890", "1234567890")]
    [InlineData("1__2__3__", "123")]
    [InlineData("18446744073709551615", "18446744073709551615")]
    [InlineData("18446744073709551616", "18446744073709551616")]
    public void ParseNumberLiteral_DecimalInteger_ReturnsExpectedValue(
        string literal,
        string expectedText)
    {
        var expected = Int128.Parse(expectedText);

        var result = NumberLiteralHelper.ParseNumberLiteral(literal, out var value);

        Assert.Equal(NumberLiteralParseResult.I128, result);
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("0b", "0")]
    [InlineData("0B", "0")]
    [InlineData("0b_", "0")]
    [InlineData("0b____", "0")]
    [InlineData("0b0", "0")]
    [InlineData("0b1", "1")]
    [InlineData("0b1010", "10")]
    [InlineData("0B1010", "10")]
    [InlineData("0b1_010_101", "85")]
    [InlineData("0b1__0__1__", "5")]
    public void ParseNumberLiteral_BinaryInteger_ReturnsExpectedValue(
        string literal,
        string expectedText)
    {
        var expected = Int128.Parse(expectedText);

        var result = NumberLiteralHelper.ParseNumberLiteral(literal, out var value);

        Assert.Equal(NumberLiteralParseResult.I128, result);
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("0o", "0")]
    [InlineData("0O", "0")]
    [InlineData("0o_", "0")]
    [InlineData("0o____", "0")]
    [InlineData("0o0", "0")]
    [InlineData("0o7", "7")]
    [InlineData("0o10", "8")]
    [InlineData("0o777", "511")]
    [InlineData("0O1_234_567", "342391")]
    [InlineData("0o1__0__0__", "64")]
    public void ParseNumberLiteral_OctalInteger_ReturnsExpectedValue(
        string literal,
        string expectedText)
    {
        var expected = Int128.Parse(expectedText);

        var result = NumberLiteralHelper.ParseNumberLiteral(literal, out var value);

        Assert.Equal(NumberLiteralParseResult.I128, result);
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("0x", "0")]
    [InlineData("0X", "0")]
    [InlineData("0x_", "0")]
    [InlineData("0x____", "0")]
    [InlineData("0x0", "0")]
    [InlineData("0xF", "15")]
    [InlineData("0xf", "15")]
    [InlineData("0x10", "16")]
    [InlineData("0xFF", "255")]
    [InlineData("0XCAFE", "51966")]
    [InlineData("0xCA_FE", "51966")]
    [InlineData("0x1__0__0__", "256")]
    public void ParseNumberLiteral_HexInteger_ReturnsExpectedValue(
        string literal,
        string expectedText)
    {
        var expected = Int128.Parse(expectedText);

        var result = NumberLiteralHelper.ParseNumberLiteral(literal, out var value);

        Assert.Equal(NumberLiteralParseResult.I128, result);
        Assert.Equal(expected, value);
    }

    [Fact]
    public void ParseNumberLiteral_DecimalUInt128Max_ReturnsAllBitsSet()
    {
        const string literal = "340282366920938463463374607431768211455";

        var result = NumberLiteralHelper.ParseNumberLiteral(literal, out var value);

        Assert.Equal(NumberLiteralParseResult.I128, result);
        Assert.Equal((Int128)(-1), value);
        Assert.Equal(UInt128.MaxValue, unchecked((UInt128)value));
    }

    [Fact]
    public void ParseNumberLiteral_DecimalAboveUInt128Max_ReturnsInvalid()
    {
        const string literal = "340282366920938463463374607431768211456";

        var result = NumberLiteralHelper.ParseNumberLiteral(literal, out var value);

        Assert.Equal(NumberLiteralParseResult.Invalid, result);
        Assert.Equal(default, value);
    }

    [Fact]
    public void ParseNumberLiteral_DecimalUInt128MaxWithSeparators_ReturnsAllBitsSet()
    {
        const string literal =
            "340_282_366_920_938_463_463_374_607_431_768_211_455";

        var result = NumberLiteralHelper.ParseNumberLiteral(literal, out var value);

        Assert.Equal(NumberLiteralParseResult.I128, result);
        Assert.Equal((Int128)(-1), value);
        Assert.Equal(UInt128.MaxValue, unchecked((UInt128)value));
    }

    [Fact]
    public void ParseNumberLiteral_BinaryUInt128Max_ReturnsAllBitsSet()
    {
        var literal = "0b" + new string('1', 128);

        var result = NumberLiteralHelper.ParseNumberLiteral(literal, out var value);

        Assert.Equal(NumberLiteralParseResult.I128, result);
        Assert.Equal((Int128)(-1), value);
        Assert.Equal(UInt128.MaxValue, unchecked((UInt128)value));
    }

    [Fact]
    public void ParseNumberLiteral_BinaryUInt128MaxWithSeparators_ReturnsAllBitsSet()
    {
        var literal = "0b" + Separate(new string('1', 128), 8);

        var result = NumberLiteralHelper.ParseNumberLiteral(literal, out var value);

        Assert.Equal(NumberLiteralParseResult.I128, result);
        Assert.Equal((Int128)(-1), value);
        Assert.Equal(UInt128.MaxValue, unchecked((UInt128)value));
    }

    [Fact]
    public void ParseNumberLiteral_BinaryAboveUInt128Max_ReturnsInvalid()
    {
        var literal = "0b1" + new string('0', 128);

        var result = NumberLiteralHelper.ParseNumberLiteral(literal, out var value);

        Assert.Equal(NumberLiteralParseResult.Invalid, result);
        Assert.Equal(default, value);
    }

    [Fact]
    public void ParseNumberLiteral_OctalUInt128Max_ReturnsAllBitsSet()
    {
        var literal = "0o" + ToBaseString(UInt128.MaxValue, 8);

        var result = NumberLiteralHelper.ParseNumberLiteral(literal, out var value);

        Assert.Equal(NumberLiteralParseResult.I128, result);
        Assert.Equal((Int128)(-1), value);
        Assert.Equal(UInt128.MaxValue, unchecked((UInt128)value));
    }

    [Fact]
    public void ParseNumberLiteral_OctalUInt128MaxWithSeparators_ReturnsAllBitsSet()
    {
        var digits = ToBaseString(UInt128.MaxValue, 8);
        var literal = "0o" + Separate(digits, 3);

        var result = NumberLiteralHelper.ParseNumberLiteral(literal, out var value);

        Assert.Equal(NumberLiteralParseResult.I128, result);
        Assert.Equal((Int128)(-1), value);
        Assert.Equal(UInt128.MaxValue, unchecked((UInt128)value));
    }

    [Fact]
    public void ParseNumberLiteral_OctalAboveUInt128Max_ReturnsInvalid()
    {
        var literal = "0o1" + new string('0', 43);

        var result = NumberLiteralHelper.ParseNumberLiteral(literal, out var value);

        Assert.Equal(NumberLiteralParseResult.Invalid, result);
        Assert.Equal(default, value);
    }

    [Fact]
    public void ParseNumberLiteral_HexUInt128Max_ReturnsAllBitsSet()
    {
        const string literal = "0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF";

        var result = NumberLiteralHelper.ParseNumberLiteral(literal, out var value);

        Assert.Equal(NumberLiteralParseResult.I128, result);
        Assert.Equal((Int128)(-1), value);
        Assert.Equal(UInt128.MaxValue, unchecked((UInt128)value));
    }

    [Fact]
    public void ParseNumberLiteral_HexUInt128MaxWithSeparators_ReturnsAllBitsSet()
    {
        const string literal =
            "0xFFFF_FFFF_FFFF_FFFF_FFFF_FFFF_FFFF_FFFF";

        var result = NumberLiteralHelper.ParseNumberLiteral(literal, out var value);

        Assert.Equal(NumberLiteralParseResult.I128, result);
        Assert.Equal((Int128)(-1), value);
        Assert.Equal(UInt128.MaxValue, unchecked((UInt128)value));
    }

    [Fact]
    public void ParseNumberLiteral_HexAboveUInt128Max_ReturnsInvalid()
    {
        const string literal = "0x100000000000000000000000000000000";

        var result = NumberLiteralHelper.ParseNumberLiteral(literal, out var value);

        Assert.Equal(NumberLiteralParseResult.Invalid, result);
        Assert.Equal(default, value);
    }

    [Theory]
    [InlineData("0.0", 0.0)]
    [InlineData("1.0", 1.0)]
    [InlineData("1.", 1.0)]
    [InlineData(".5", 0.5)]
    [InlineData("1.25", 1.25)]
    [InlineData("1_234.5_678", 1234.5678)]
    [InlineData("1e0", 1.0)]
    [InlineData("1e3", 1000.0)]
    [InlineData("1E3", 1000.0)]
    [InlineData("1e-3", 0.001)]
    [InlineData("1e+3", 1000.0)]
    [InlineData("1_2.5e1", 125.0)]
    [InlineData("1.25e2_", 125.0)]
    [InlineData("1.0__0", 1.0)]
    public void ParseNumberLiteral_Float_ReturnsExpectedBits(
        string literal,
        double expected)
    {
        var result = NumberLiteralHelper.ParseNumberLiteral(literal, out var value);

        Assert.Equal(NumberLiteralParseResult.F64, result);

        var actualBits = unchecked((ulong)value);
        var expectedBits = BitConverter.DoubleToUInt64Bits(expected);

        Assert.Equal(expectedBits, actualBits);
    }

    [Theory]
    [InlineData("-0.0")]
    [InlineData("-0e0")]
    public void ParseNumberLiteral_NegativeZeroFloat_PreservesSignBit(string literal)
    {
        var result = NumberLiteralHelper.ParseNumberLiteral(literal, out var value);

        Assert.Equal(NumberLiteralParseResult.F64, result);
        Assert.Equal(
            BitConverter.DoubleToUInt64Bits(-0.0),
            unchecked((ulong)value));
    }

    [Fact]
    public void ParseNumberLiteral_DoubleMaxValue_ReturnsExpectedBits()
    {
        const string literal = "1.7976931348623157e308";

        var result = NumberLiteralHelper.ParseNumberLiteral(literal, out var value);

        Assert.Equal(NumberLiteralParseResult.F64, result);
        Assert.Equal(
            BitConverter.DoubleToUInt64Bits(double.MaxValue),
            unchecked((ulong)value));
    }

    [Theory]
    [InlineData("1e309")]
    [InlineData("-1e309")]
    [InlineData("9.9e9999")]
    public void ParseNumberLiteral_InfiniteFloat_ReturnsInvalid(string literal)
    {
        var result = NumberLiteralHelper.ParseNumberLiteral(literal, out var value);

        Assert.Equal(NumberLiteralParseResult.Invalid, result);
        Assert.Equal(default, value);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("1e")]
    [InlineData("1e+")]
    [InlineData("1e-")]
    [InlineData("1.2.3")]
    [InlineData("1e2e3")]
    public void ParseNumberLiteral_InvalidFloat_ReturnsInvalid(string literal)
    {
        var result = NumberLiteralHelper.ParseNumberLiteral(literal, out var value);

        Assert.Equal(NumberLiteralParseResult.Invalid, result);
        Assert.Equal(default, value);
    }

    private static string Separate(string digits, int groupSize)
    {
        var separatorCount = (digits.Length - 1) / groupSize;
        return string.Create(
            digits.Length + separatorCount,
            (digits, groupSize),
            static (destination, state) =>
            {
                var sourceIndex = 0;
                var destinationIndex = 0;
                var firstGroupLength = state.digits.Length % state.groupSize;

                if (firstGroupLength == 0)
                {
                    firstGroupLength = state.groupSize;
                }

                while (sourceIndex < state.digits.Length)
                {
                    var currentGroupLength = sourceIndex == 0 ?
                        firstGroupLength :
                        state.groupSize;

                    state.digits.AsSpan(sourceIndex, currentGroupLength)
                        .CopyTo(destination[destinationIndex..]);

                    sourceIndex += currentGroupLength;
                    destinationIndex += currentGroupLength;

                    if (sourceIndex < state.digits.Length)
                    {
                        destination[destinationIndex++] = '_';
                    }
                }
            });
    }

    private static string ToBaseString(UInt128 value, uint radix)
    {
        Span<char> buffer = stackalloc char[128];
        var index = buffer.Length;

        do
        {
            var digit = (uint)(value % radix);
            value /= radix;
            buffer[--index] = digit < 10 ?
                (char)('0' + digit) :
                (char)('A' + digit - 10);
        }
        while (value != 0);

        return new string(buffer[index..]);
    }
}
