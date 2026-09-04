// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Arc.Unit;
using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public class SourceDocumentAndDiagnosticTest
{
    [Fact]
    public void GetsLinesWithoutLineBreaks()
    {
        var sourceDocument = new SourceDocument("test.kimi", "first\r\nsecond\nthird\rfourth\n");

        Assert.Equal(5, sourceDocument.LineCount);
        Assert.Equal("first", sourceDocument.GetLineSpan(0).ToString());
        Assert.Equal("second", sourceDocument.GetLineSpan(1).ToString());
        Assert.Equal("third", sourceDocument.GetLineSpan(2).ToString());
        Assert.Equal("fourth", sourceDocument.GetLineSpan(3).ToString());
        Assert.Equal(string.Empty, sourceDocument.GetLineSpan(4).ToString());
        Assert.Equal(new[] { 0, 7, 14, 20, 27 }, sourceDocument.LineStarts.ToArray());
        Assert.Equal(new SourcePosition(2, 2), sourceDocument.GetPosition(16));
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("a", 1)]
    [InlineData("\n", 2)]
    [InlineData("\r", 2)]
    [InlineData("\r\n", 2)]
    [InlineData("\n\r", 3)] // Two separate terminators, not a pair.
    [InlineData("x\n\r\ny", 3)]
    public void MapsEveryOffsetOfAnEdgeCaseDocument(string sourceText, int expectedLineCount)
    {
        var sourceDocument = new SourceDocument("test.kimi", sourceText);
        Assert.Equal(expectedLineCount, sourceDocument.LineCount);

        var lineStarts = sourceDocument.LineStarts.ToArray();
        for (var offset = 0; offset <= sourceText.Length; offset++)
        {
            var position = sourceDocument.GetPosition(offset);

            // The reported line is the last one that starts at or before the offset.
            var expectedLine = 0;
            while (expectedLine + 1 < lineStarts.Length && lineStarts[expectedLine + 1] <= offset)
            {
                expectedLine++;
            }

            Assert.Equal(expectedLine, position.Line);
            Assert.Equal(offset - lineStarts[expectedLine], position.Character);
        }
    }

    [Fact]
    public void SourceRangeMatchesTheIndividualPositions()
    {
        var sourceText = "first\r\nsecond\nthird\rfourth\n";
        var sourceDocument = new SourceDocument("test.kimi", sourceText);

        for (var start = 0; start <= sourceText.Length; start++)
        {
            for (var end = start; end <= sourceText.Length; end++)
            {
                var range = sourceDocument.GetSourceRange(SourceSpan.FromBounds(start, end));

                Assert.Equal(sourceDocument.GetPosition(start), range.Start);
                Assert.Equal(sourceDocument.GetPosition(end), range.End);
            }
        }
    }

    [Fact]
    public void OffsetRoundTripsThroughEveryPositionOnEveryLine()
    {
        var sourceText = "first\r\nsecond\nthird\rfourth\n";
        var sourceDocument = new SourceDocument("test.kimi", sourceText);

        for (var line = 0; line < sourceDocument.LineCount; line++)
        {
            var lineStart = sourceDocument.LineStarts[line];
            var lineLength = sourceDocument.GetLineSpan(line).Length;
            for (var character = 0; character <= lineLength; character++)
            {
                var position = new SourcePosition(line, character);
                Assert.Equal(lineStart + character, sourceDocument.GetOffset(position));
            }

            // A character past the end of the line is rejected.
            Assert.Throws<ArgumentOutOfRangeException>(
                () => sourceDocument.GetOffset(new SourcePosition(line, lineLength + 1)));
        }

        Assert.Throws<ArgumentOutOfRangeException>(
            () => sourceDocument.GetOffset(new SourcePosition(sourceDocument.LineCount, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => sourceDocument.GetPosition(sourceText.Length + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => sourceDocument.GetPosition(-1));
    }

    [Fact]
    public void ParserDiagnosticReferencesSourceDocument()
    {
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        var sourceDocument = new SourceDocument("test.kimi", "var x = 1 +");

        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, sourceDocument);

        var diagnostics = kotonoha.DiagnosticCollection.GetArray();
        Assert.NotEmpty(diagnostics);
        Assert.All(diagnostics, x => Assert.Same(sourceDocument, x.SourceDocument));
    }

    [Fact]
    public void ReportsSourceLineAndCaret()
    {
        var console = new TestConsoleService();
        var kimigayo = new Kimigayo(console);
        var sourceDocument = new SourceDocument("test.kimi", "let x = 1\r\nvar value = bad\n");
        var entry = new DiagnosticEntry("Test_Kd", DiagnosticSeverity.Error, "Bad token");
        var range = sourceDocument.GetTextSpan(new SourceRange(new(1, 12), new(1, 15)));
        var diagnostic = new Diagnostic(range, entry, sourceDocument);

        kimigayo.ReportDiagnostic(sourceDocument.Path, diagnostic);

        Assert.Equal(
            "Bad token : Test_Kd\n" +
            " --> test.kimi:2:13\n" +
            "  |\n" +
            "2 | var value = bad\n" +
            "  |             ^^^\n" +
            "  |\n" +
            "\n",
            console.Output);
    }

    [Fact]
    public void ReportsCaretsAcrossMultipleLines()
    {
        var console = new TestConsoleService();
        var kimigayo = new Kimigayo(console);
        var sourceDocument = new SourceDocument("test.kimi", "abc\ndefg\nhij");
        var entry = new DiagnosticEntry("Test_Kd", DiagnosticSeverity.Error, "Bad range");
        var range = sourceDocument.GetTextSpan(new SourceRange(new(0, 1), new(2, 2)));
        var diagnostic = new Diagnostic(range, entry, sourceDocument);

        kimigayo.ReportDiagnostic(sourceDocument.Path, diagnostic);

        Assert.Contains("1 | abc\n  |  ^^\n", console.Output);
        Assert.Contains("2 | defg\n  | ^^^^\n", console.Output);
        Assert.Contains("3 | hij\n  | ^^\n", console.Output);
    }

    private sealed class TestConsoleService : IConsoleService
    {
        private readonly StringBuilder output = new();

        public string Output => this.output.ToString();

        public bool KeyAvailable => false;

        public bool EnableColor { get; set; }

        public void Write(string? message, ConsoleColor color = ConsoleColor.Gray)
            => this.output.Append(message);

        public void Write(ReadOnlySpan<char> message, ConsoleColor color = ConsoleColor.Gray)
            => this.output.Append(message);

        public void WriteLine(string? message, ConsoleColor color = ConsoleColor.Gray)
            => this.output.Append(message).Append('\n');

        public void WriteLine(ReadOnlySpan<char> message, ConsoleColor color = ConsoleColor.Gray)
            => this.output.Append(message).Append('\n');

        public Task<InputResult> ReadLine(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public ConsoleKeyInfo ReadKey(bool intercept)
            => throw new NotSupportedException();
    }
}
