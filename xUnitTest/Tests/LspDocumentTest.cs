// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 23.4.2: document text synchronization with UTF-16 positions and the source line-break rule.
public sealed class LspDocumentTest
{
    [Fact]
    public void ChangesApplyInOrderAtUtf16Positions()
    {
        using var document = new TextDocument("a\U0001F600b\nline2");
        Assert.True(document.TryApply(new(0, 3), new(0, 3), "X"));
        Assert.True(document.TryApply(new(1, 0), new(1, 4), "L"));
        Assert.True(document.TryApply(new(0, 0), new(0, 1), string.Empty));
        Assert.Equal("\U0001F600Xb\nL2", document.ToString());
        Assert.Equal(2, document.LineCount);
    }

    [Theory]
    [InlineData("a\r\nb", "a\r\nXb")]
    [InlineData("a\rb", "a\rXb")]
    [InlineData("a\nb", "a\nXb")]
    public void LineBreaksFollowTheSourceRule(string text, string expected)
    {
        using var document = new TextDocument(text);
        Assert.True(document.TryApply(new(1, 0), new(1, 0), "X"));
        Assert.Equal(expected, document.ToString());
    }

    [Fact]
    public void CharacterBeyondTheLineEndIsClampedBeforeTheLineBreak()
    {
        using var document = new TextDocument("ab\r\ncd");
        Assert.True(document.TryApply(new(0, 99), new(0, 99), "X"));
        Assert.True(document.TryApply(new(1, 99), new(1, 99), "Y"));
        Assert.Equal("abX\r\ncdY", document.ToString());
    }

    [Fact]
    public void AnEditJoiningCrAndLfFormsOneLineBreak()
    {
        using var document = new TextDocument("a\rX\nb");
        Assert.Equal(3, document.LineCount);
        Assert.True(document.TryApply(new(1, 0), new(1, 1), string.Empty));
        Assert.Equal("a\r\nb", document.ToString());
        Assert.Equal(2, document.LineCount);
    }

    [Fact]
    public void InapplicableChangesLeaveTheTextUnchanged()
    {
        using var document = new TextDocument("one\ntwo");
        Assert.False(document.TryApply(new(2, 0), new(2, 0), "X"));
        Assert.False(document.TryApply(new(1, 2), new(1, 1), "X"));
        Assert.False(document.TryApply(new(0, 1), new(0, 0), "X"));
        Assert.Equal("one\ntwo", document.ToString());
    }

    [Fact]
    public void LargeEditsGrowTheBufferAndReplaceResetsIt()
    {
        using var document = new TextDocument(string.Empty);
        var line = new string('x', 1000) + "\n";
        for (var i = 0; i < 20; i++)
        {
            Assert.True(document.TryApply(new(i, 0), new(i, 0), line));
        }

        Assert.Equal(21, document.LineCount);
        Assert.Equal(20 * line.Length, document.Length);
        var text = document.ToString();
        Assert.Same(text, document.ToString());

        document.Replace("short");
        Assert.Equal("short", document.ToString());
        Assert.Equal(1, document.LineCount);
    }

    [Fact]
    public void IncrementalLineStartsMatchAFullRebuild()
    {
        var random = new Random(23);
        var alphabet = new[] { 'a', 'b', '\r', '\n' };
        using var document = new TextDocument("a\r\nb\rc\n");
        for (var step = 0; step < 5000; step++)
        {
            var starts = document.LineStarts;
            var startLine = random.Next(starts.Length);
            var endLine = random.Next(startLine, starts.Length);
            var startCharacter = random.Next(4);
            var endCharacter = endLine == startLine ? random.Next(startCharacter, 5) : random.Next(5);
            var inserted = new char[random.Next(4)];
            for (var i = 0; i < inserted.Length; i++)
            {
                inserted[i] = alphabet[random.Next(alphabet.Length)];
            }

            if (document.TryApply(new(startLine, startCharacter), new(endLine, endCharacter), inserted) && document.Length > 200)
            {
                document.Replace(document.ToString()[..50]);
            }

            using var rebuilt = new TextDocument(document.ToString());
            Assert.Equal(rebuilt.LineStarts.ToArray(), document.LineStarts.ToArray());
        }
    }

    [Fact]
    public void ReplacementsSpanningLinesKeepLaterLines()
    {
        using var document = new TextDocument("first\nsecond\nthird\n");
        Assert.True(document.TryApply(new(0, 2), new(2, 3), "-"));
        Assert.Equal("fi-rd\n", document.ToString());
        Assert.True(document.TryApply(new SourcePosition(1, 0), new SourcePosition(1, 0), "tail"));
        Assert.Equal("fi-rd\ntail", document.ToString());
    }
}
