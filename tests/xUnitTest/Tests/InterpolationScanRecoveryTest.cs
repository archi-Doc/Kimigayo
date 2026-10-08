// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Helper;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class InterpolationScanRecoveryTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("\"\\('x)", "'", "MissingCharLiteralEnd_Kd")]
    [InlineData("\"\\(\"inner", "\"inner", "MissingStringLiteralEnd_Kd")]
    [InlineData("\"\\(\"\"\"inner", "\"\"\"", "MissingStringLiteralEnd_Kd")]
    [InlineData("\"\\(\n    'x)", "'", "MissingCharLiteralEnd_Kd")]
    [InlineData("\"\\(\n    \"inner", "\"inner", "MissingStringLiteralEnd_Kd")]
    [InlineData("\"\\(\"\\('x)", "'", "MissingCharLiteralEnd_Kd")]
    public void FailedNestedTokensKeepTheirOwnOpeningAndTheNextStatement(string literal, string opening, string code)
    {
        foreach (var newline in new[] { "\n", "\r\n", "\r" })
        {
            var source = "let text = " + literal.Replace("\n", newline, StringComparison.Ordinal) + newline + "let wrong: i32 = true";
            var c = Analyze(source);
            var records = c.Diagnostics.Finalize(rejected: true).Diagnostics;
            output.WriteLine(string.Join("\n", records.Select(static x => x.Code + " " + x.Span + " " + x.Message)));
            Assert.Equal([code, "TypeMismatch_Kd"], records.Select(static x => x.Code));
            Assert.Equal(new SourceSpan(source.IndexOf(opening, StringComparison.Ordinal), opening == "\"inner" ? 1 : opening.Length), records[0].Span);
            Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
            ParseTestHelper.VerifyParents(c.Kotonoha.RootKoto);
            c.Bind();
            c.Binding.ReportDiagnostics();
            Assert.Equal(records, c.Diagnostics.Finalize(rejected: true).Diagnostics);
        }
    }

    [Fact]
    public void AnUnterminatedInterpolationCommentKeepsItsLexicalCause()
    {
        const string Source = "let wrong: i32 = true\nlet text = \"\\(1 /* tail";
        var records = Analyze(Source).Diagnostics.Finalize(rejected: true).Diagnostics;
        Assert.Equal(["TypeMismatch_Kd", "MissingBlockCommentEnd_Kd"], records.Select(static x => x.Code));
        Assert.Equal(new SourceSpan(Source.IndexOf("/*", StringComparison.Ordinal), 2), records[1].Span);
    }

    [Theory]
    [InlineData("\"\\(1")]
    [InlineData("\"\\(1 // tail")]
    public void AnUnclosedInterpolationNamesItsOwnDelimiter(string literal)
    {
        var source = "let text = " + literal;
        var record = Assert.Single(Analyze(source).Diagnostics.Finalize(rejected: true).Diagnostics);
        Assert.Equal("MissingSyntax_Kd", record.Code);
        Assert.Equal(new SourceSpan(source.Length, 0), record.Span);
        Assert.Contains(record.Reason!, static x => x.Kind == DiagnosticValueKind.Requirement && x.Value == "Syntax.CloseParenthesis");
        Assert.Equal(new SourceSpan(source.IndexOf('('), 1), Assert.Single(record.Related ?? [], static x => x.Role == "opening delimiter").Span);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(128)]
    public void InterpolationDepthCountsEachInterpolationOnce(int depth)
    {
        var source = Nest(depth);
        Assert.Equal(ScanStringLiteralResult.Interpolation, StringLiteralHelper.ScanStringLiteral(source, out var quotes, out var length));
        Assert.Equal(1, quotes);
        Assert.Equal(source.Length, length);
    }

    [Fact]
    public void ExcessiveNestingIsAResourceFailureAtTheExceededDepth()
    {
        const string Prefix = "let text = ";
        var source = Prefix + Nest(129);
        var record = Assert.Single(Analyze(source).Diagnostics.Finalize(rejected: true).Diagnostics);
        Assert.Equal("InterpolationNestingLimit_Kd", record.Code);
        Assert.Equal(DiagnosticCategory.Resource, record.Category);
        Assert.Equal(new SourceSpan(Prefix.Length + (128 * 3) + 1, 2), record.Span);
        Assert.Contains("128", record.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AResourceStopKeepsEarlierErrorsAndOtherSourcesIndependent()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("deep.kimi", "let wrong: i32 = true\nlet text = " + Nest(129)));
        c.Kotonoha.AddSource(new SourceDocument("next.kimi", "let other: i32 = true"));
        c.Bind();
        c.Binding.ReportDiagnostics();
        var records = c.Diagnostics.Finalize(rejected: true).Diagnostics;
        Assert.Equal(["TypeMismatch_Kd", "InterpolationNestingLimit_Kd", "TypeMismatch_Kd"], records.Select(static x => x.Code));
        Assert.Equal([0, 0, 1], records.Select(static x => x.Source));
    }

    [Fact]
    public void ClosedNestedTokensExecute()
    {
        const string Source = """"
            Console.writeLine("\("inner \(42)")")
            Console.writeLine("\('A') \(/* note */ 2)")
            Console.writeLine("\("""raw""")")
            """";
        ScalarEmissionTest.EmitFixture("InterpolationScanRecovery", Source, "inner 42\nA 2\nraw\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void NestedStringScanningReusesTokenStorageWithoutAllocations()
    {
        var c = Compilation.CreateForTest();
        var diagnostics = c.Diagnostics.GetOrAddCollection("interpolation-scan");
        var source = new SourceDocument("nested.kimi", "let text = " + Nest(16));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            var tokenizer = new Tokenizer(diagnostics, source);
            try
            {
                tokenizer.ReadAll();
                var literals = 0;
                foreach (var token in tokenizer.Tokens)
                {
                    valid &= token.Kind != TokenKind.Invalid && !token.IsMissing;
                    literals += token.Kind == TokenKind.InterpolatedStringLiteral ? 1 : 0;
                }

                valid &= literals == 1;
            }
            finally
            {
                tokenizer.Dispose();
            }
        }));
        Assert.True(valid);
        Assert.Empty(TestDiagnostics.Of(c));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CliAndLspExplainTheActualFailedNestedForm(int kind)
    {
        var resource = kind == 2;
        var literal = kind switch { 0 => "\"\\(\n    'x)", 1 => "\"\\(1", _ => Nest(129) };
        var source = "let text = " + literal;
        var path = Path.GetFullPath("interpolation-scan.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(kind switch { 0 => "MissingCharLiteralEnd_Kd", 1 => "MissingSyntax_Kd", _ => "InterpolationNestingLimit_Kd" }, record.Code);
        Assert.Equal(resource ? DiagnosticCategory.Resource : DiagnosticCategory.Language, record.Category);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(record.Display!.Range!.Value, sent.Range);
            Assert.Equal(record.Code, sent.Code);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    private static string Nest(int depth)
        => string.Concat(Enumerable.Repeat("\"\\(", depth)) + "1" + string.Concat(Enumerable.Repeat(")\"", depth));

    private static Compilation Analyze(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        return c;
    }
}
