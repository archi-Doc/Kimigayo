// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class ArrayTypeRecoveryTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("let values: [::width of i32] = source")]
    [InlineData("let values = [::width of 1]")]
    public void RootQualifiedLengthsUseTheNameGrammar(string source)
        => ParseTestHelper.VerifyParents(ParseTestHelper.ParseSuccess(source).RootKoto);

    [Theory]
    [InlineData("[i32]", "ExpectedSyntax_Kd", "i32", "Syntax.ArrayLength")]
    [InlineData("[-1 of u8]", "MisplacedSyntax_Kd", "-1", "Syntax.CompoundArrayLength")]
    [InlineData("[Width * Height of u8]", "MisplacedSyntax_Kd", "Width * Height", "Syntax.CompoundArrayLength")]
    [InlineData("[2 i32]", "ExpectedSyntax_Kd", "i32", "Syntax.OfKeyword")]
    [InlineData("[2 of ]", "ExpectedSyntax_Kd", "]", "Syntax.Type")]
    public void RejectedLengthGrammarKeepsOneCauseAndTheFollowingDeclaration(string type, string code, string marked, string requirement)
    {
        var source = "let Width = 2\nlet Height = 3\nlet n: " + type + "\nlet wrong: i32 = true";
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        var errors = c.Diagnostics.Finalize(rejected: true).Diagnostics;
        output.WriteLine(string.Join("\n", errors.Select(static x => x.Code + " " + x.Span + " " + x.Message)));
        Assert.Equal([code, "TypeMismatch_Kd"], errors.Select(static x => x.Code));
        Assert.Equal(new SourceSpan(source.IndexOf(marked, "let Width = 2\nlet Height = 3\nlet n: ".Length, StringComparison.Ordinal), marked.Length), errors[0].Span);
        Assert.Contains(errors[0].Reason!, x => x.Kind == DiagnosticValueKind.Requirement && x.Value == requirement);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
        ParseTestHelper.VerifyParents(c.Kotonoha.RootKoto);
        Assert.Equal(4, c.Kotonoha.GeneratedFunction!.Body!.Items.Count);
        c.Bind();
        c.Binding.ReportDiagnostics();
        Assert.Equal(errors, c.Diagnostics.Finalize(rejected: true).Diagnostics);
    }

    [Theory]
    [InlineData("[-1 of Missing]")]
    [InlineData("[Width * Height of Missing]")]
    public void ARecoveredLengthRetainsTheIndependentElementTypeCheck(string type)
    {
        var source = "let Width = 2\nlet Height = 3\nlet n: " + type;
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        var errors = c.Diagnostics.Finalize(rejected: true).Diagnostics;
        output.WriteLine(string.Join("\n", errors.Select(static x => x.Code + " " + x.Span + " " + x.Message)));
        Assert.Equal(["MisplacedSyntax_Kd", "UnresolvedBinding_Kd"], errors.Select(static x => x.Code));
        Assert.Equal(new SourceSpan(source.IndexOf("Missing", StringComparison.Ordinal), "Missing".Length), errors[1].Span);
    }

    [Fact]
    public void ParenthesizedLengthGrammarExecutes()
    {
        const string Source = """
            let Width = 2
            let Height = 3
            let n: [(Width * Height) of u8] = [6 of 7]
            Console.writeLine("\(n.length)")
            Console.writeLine("\(n[5])")
            """;
        ScalarEmissionTest.EmitFixture("ArrayTypeParenthesizedLength", Source, "6\n7\n");
    }

    [Fact]
    public void CliAndLspExplainTheLengthWithoutHidingTheElementType()
    {
        const string Source = "let n: [-1 of Missing]\nlet wrong: i32 = true";
        var path = Path.GetFullPath("array-recovery.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        Assert.Equal(["MisplacedSyntax_Kd", "UnresolvedBinding_Kd", "TypeMismatch_Kd"], result.Diagnostics.Select(static x => x.Code));
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity];
            Assert.Equal(result.Diagnostics.Select(static x => x.Display!.Range!.Value), sent.Select(static x => x.Range));
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }
}
