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
        var range = new SourceRange(new(1, 12), new(1, 15));
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
        var range = new SourceRange(new(0, 1), new(2, 2));
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
