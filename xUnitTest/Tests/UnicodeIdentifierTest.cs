// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Helper;
using Xunit;

namespace XunitTest;

public class UnicodeIdentifierTest
{
    [Theory]
    [InlineData("Å\u0323", false)]
    [InlineData("Ạ\u030A", true)]
    [InlineData("q\u0301", true)]
    [InlineData("a\u0301", false)]
    [InlineData("à\u0301", true)]
    [InlineData("a\u0301\u0323", false)]
    [InlineData("가", true)]
    [InlineData("가", false)]
    [InlineData("각", false)]
    [InlineData("각", true)]
    [InlineData("\u0344", false)]
    [InlineData("\u1C89", false)]
    [InlineData("\U00031350", true)]
    [InlineData("\U0002EBF0", false)]
    public void UsesPinnedUnicode15AndCanonicalComposition(string text, bool expected)
        => Assert.Equal(expected, IdentifierHelper.IsValidIdentifier(text));

    [Fact]
    public void NormalizesLongNamesWithoutRetainingPooledStorage()
    {
        var prefix = new string('q', 300);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(IdentifierHelper.IsValidIdentifier(prefix + "\u0301"));
            Assert.False(IdentifierHelper.IsValidIdentifier(prefix + "a\u0301"));
        }
    }
}
