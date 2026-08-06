// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class StringLiteralHelperTest
{
    public static TheoryData<string, ScanStringLiteralResult, int, int> ValidScanData
        => new()
        {
            { "\"\"", ScanStringLiteralResult.String, 1, 2 },
            { "\"Text\"", ScanStringLiteralResult.String, 1, 6 },
            { "\"Text\"Remaining", ScanStringLiteralResult.String, 1, 6 },
            { "\"A\\\"B\"", ScanStringLiteralResult.String, 1, 6 },
            { "\"A\\\\B\"", ScanStringLiteralResult.String, 1, 6 },
            { "\"\"\"Text\"\"\"", ScanStringLiteralResult.String, 3, 10 },
            { "\"\"\"Text\"\"\"Remaining", ScanStringLiteralResult.String, 3, 10 },
            { "\"\"\"\"Text\"\"\"\"", ScanStringLiteralResult.String, 4, 12 },

            // Empty raw strings with three and four quotes per delimiter.
            { "\"\"\"\"\"\"", ScanStringLiteralResult.Invalid, 6, 6 },
            { "\"\"\"\"\"\"\"\"", ScanStringLiteralResult.Invalid, 8, 8 },
            { "\"\"\"\nText\n\"\"\"", ScanStringLiteralResult.MultilineString, 3, 12 },
            { "\"\"\"\r\nText\r\n\"\"\"", ScanStringLiteralResult.MultilineString, 3, 14 },
        };

    [Theory]
    [MemberData(nameof(ValidScanData))]
    public void ScanStringLiteral_ValidLiteral_ReturnsExpectedResult(
        string text,
        ScanStringLiteralResult expectedResult,
        int expectedQuoteCount,
        int expectedLength)
    {
        var result = StringLiteralHelper.ScanStringLiteral(text, out var quoteCount, out var length);

        Assert.Equal(expectedResult, result);
        Assert.Equal(expectedQuoteCount, quoteCount);
        Assert.Equal(expectedLength, length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Text")]
    [InlineData("123")]
    [InlineData("'Text'")]
    public void ScanStringLiteral_NonStringLiteral_ReturnsNone(string text)
    {
        var result = StringLiteralHelper.ScanStringLiteral(text, out var quoteCount, out var length);

        Assert.Equal(ScanStringLiteralResult.None, result);
        Assert.Equal(0, quoteCount);
        Assert.Equal(0, length);
    }

    [Theory]
    [InlineData("\"", 1, 1)]
    [InlineData("\"Text", 1, 1)]
    [InlineData("\"\"\"", 3, 3)]
    [InlineData("\"\"\"Text", 3, 3)]
    public void ScanStringLiteral_UnterminatedLiteral_ReturnsInvalid(
        string text,
        int expectedQuoteCount,
        int expectedLength)
    {
        var result = StringLiteralHelper.ScanStringLiteral(text, out var quoteCount, out var length);

        Assert.Equal(ScanStringLiteralResult.Invalid, result);
        Assert.Equal(expectedQuoteCount, quoteCount);
        Assert.Equal(expectedLength, length);
    }

    [Fact]
    public void ScanStringLiteral_EscapedStringContainingPhysicalLineBreak_IsValid()
    {
        const string Text = "\"Line1\\r\\nLine2\\nLine3\\r\\nLine4\"";

        var result = StringLiteralHelper.ScanStringLiteral(Text, out var quoteCount, out var length);

        Assert.True(result is ScanStringLiteralResult.String or ScanStringLiteralResult.MultilineString);
        Assert.Equal(1, quoteCount);
        Assert.Equal(Text.Length, length);
    }
}

public class KotoHelperStringLiteralTest
{
    [Fact]
    public void GetStringLiteralValue_EmptyString_ReturnsEmpty()
    {
        var result = StringLiteralHelper.GetStringLiteralValue(string.Empty);

        Assert.Same(string.Empty, result);
    }

    [Fact]
    public void GetStringLiteralValue_StringWithoutEscape_ReturnsOriginalInstance()
    {
        var source = new string("Text without escapes".ToCharArray());

        var result = StringLiteralHelper.GetStringLiteralValue(source);

        Assert.Same(source, result);
    }

    [Theory]
    [InlineData(@"\0", "\0")]
    [InlineData(@"\\", "\\")]
    [InlineData(@"\e", "\u001b")]
    [InlineData(@"\t", "\t")]
    [InlineData(@"\n", "\n")]
    [InlineData("\\\"", "\"")]
    [InlineData(@"\'", "'")]
    public void GetStringLiteralValue_SimpleEscape_ReturnsDecodedValue(string source, string expected)
    {
        var result = StringLiteralHelper.GetStringLiteralValue(source);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetStringLiteralValue_MultipleEscapes_ReturnsDecodedValue()
    {
        const string Source = @"A\0B\\C\eD\tE\nF\""G\'H";
        const string Expected = "A\0B\\C\u001bD\tE\nF\"G'H";

        var result = StringLiteralHelper.GetStringLiteralValue(Source);

        Assert.Equal(Expected, result);
    }

    [Fact]
    public void GetStringLiteralValue_PhysicalLineBreaks_ArePreserved()
    {
        const string Source = "Line1\r\nLine2\nLine3\rLine4";

        var result = StringLiteralHelper.GetStringLiteralValue(Source);

        Assert.Same(Source, result);
    }

    [Theory]
    [InlineData(@"\u(0)", "\0")]
    [InlineData(@"\u(0000)", "\0")]
    [InlineData(@"\u(41)", "A")]
    [InlineData(@"\u(0041)", "A")]
    [InlineData(@"\u(3042)", "あ")]
    [InlineData(@"\u(1F600)", "\U0001F600")]
    public void GetStringLiteralValue_UnicodeEscape_ReturnsDecodedValue(string source, string expected)
    {
        var result = StringLiteralHelper.GetStringLiteralValue(source);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetStringLiteralValue_MaximumUnicodeScalar_ReturnsSurrogatePair()
    {
        const string Source = @"A\u(10FFFF)B";
        var expected = "A" + char.ConvertFromUtf32(0x10FFFF) + "B";

        var result = StringLiteralHelper.GetStringLiteralValue(Source);

        Assert.Equal(expected, result);
        Assert.Equal(4, result.Length);
    }

    [Theory]
    [InlineData(@"\x", "k")]
    [InlineData(@"\q", "k")]
    [InlineData(@"\u", "k")]
    [InlineData(@"\u()", "k")]
    [InlineData(@"\u(12X4)", "k")]
    [InlineData(@"\u(D800)", "k")]
    [InlineData(@"\u(DFFF)", "k")]
    [InlineData(@"\u(110000)", "k")]
    [InlineData(@"\u(1234567)", "k")]
    [InlineData(@"\u(1234ABCD)", "k")]
    public void GetStringLiteralValue_InvalidEscape_ReturnsFallbackCharacter(string source, string expected)
    {
        var result = StringLiteralHelper.GetStringLiteralValue(source);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetStringLiteralValue_TrailingBackslash_ReturnsFallbackCharacter()
    {
        const string Source = "Text\\";

        var result = StringLiteralHelper.GetStringLiteralValue(Source);

        Assert.Equal("Textk", result);
    }

    [Fact]
    public void GetStringLiteralValue_MultipleInvalidEscapes_ReplacesEachEscape()
    {
        const string Source = @"A\qB\u()C\xD";

        var result = StringLiteralHelper.GetStringLiteralValue(Source);

        Assert.Equal("AkBkCkD", result);
    }

    [Theory]
    [InlineData("\"\"\"Text\"\"\"", "Text")]
    [InlineData("\"\"\"\"Text\"\"\"\"", "Text")]
    [InlineData("\"\"\"\"\"\"", "")]
    [InlineData("\"\"\"\"\"\"\"\"", "")]
    [InlineData("\"\"\"A\"\"B\"\"\"", "A\"\"B")]
    public void GetStringLiteralValue_RawString_RemovesDelimiters(string source, string expected)
    {
        var result = StringLiteralHelper.GetStringLiteralValue(source);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetStringLiteralValue_RawString_DoesNotProcessEscapes()
    {
        const string Source = "\"\"\"A\\nB\\u(0041)C\\\\D\"\"\"";
        const string Expected = @"A\nB\u(0041)C\\D";

        var result = StringLiteralHelper.GetStringLiteralValue(Source);

        Assert.Equal(Expected, result);
    }

    [Fact]
    public void GetStringLiteralValue_MultilineRawString_PreservesLineBreaks()
    {
        const string Source = "\"\"\"\r\nLine1\nLine2\r\"\"\"";
        const string Expected = "\r\nLine1\nLine2\r";

        var result = StringLiteralHelper.GetStringLiteralValue(Source);

        Assert.Equal(Expected, result);
    }
}

public class StringLiteralIntegrationTest
{
    [Theory]
    [InlineData("\"Text\"", "Text")]
    [InlineData("\"A\\nB\"", "A\nB")]
    [InlineData("\"\\u(3042)\"", "あ")]
    [InlineData("\"\\u(1F600)\"", "\U0001F600")]
    [InlineData("\"\"\"Text\"\"\"", "Text")]
    [InlineData("\"\"\"A\\nB\"\"\"", @"A\nB")]
    [InlineData("\"\"\"\nText\n\"\"\"", "\nText\n")]
    public void ScanAndGetValue_ValidLiteral_ReturnsExpectedValue(string literal, string expected)
    {
        var result = StringLiteralHelper.ScanStringLiteral(literal, out var quoteCount, out var length);

        Assert.True(result is ScanStringLiteralResult.String or ScanStringLiteralResult.MultilineString);
        Assert.Equal(literal.Length, length);

        var valueSource = quoteCount == 1
            ? literal.Substring(1, length - 2)
            : literal.Substring(0, length);

        var value = StringLiteralHelper.GetStringLiteralValue(valueSource);

        Assert.Equal(expected, value);
    }
}
