// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;
using static XunitTest.ParseTestHelper;

namespace XunitTest;

public class LayoutRecoveryTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("(\n        value\n    )")]
    [InlineData("get(\n        value\n    )")]
    [InlineData("[\n        value\n    ]")]
    [InlineData("get(\n        (\n            value\n        )\n    )")]
    [InlineData("(\n        value\n    ) // comment\n\n    // another comment")]
    public void OuterAlignedCloserKeepsTheFollowingChain(string expression)
    {
        var tree = Parse("func f()\n    let n = " + expression + "\n        .first()\n        .second()\n    next()\nlast()");
        AssertValid(tree);
        var top = tree.GeneratedFunction!.Body!.Items;
        Assert.Equal(2, top.Count);
        var function = Assert.IsType<FunctionKoto>(top[0]);
        Assert.Equal(2, function.Body!.Items.Count);
        var field = Assert.IsType<FieldKoto>(function.Body.Items[0]);
        Assert.Equal("second", Assert.IsType<MemberAccessKoto>(Assert.IsType<InvocationKoto>(field.InitializerKoto).Method).Accessor.ToString());
        Assert.Equal("next()", function.Body.Items[1].ToString());
        Assert.Equal("last()", top[1].ToString());
        VerifyParents(tree.RootKoto);
    }

    [Theory]
    [InlineData("let n = (if ready\n    1\n)")]
    [InlineData("let n = (if ready\n    1\nelse\n    2\n)")]
    [InlineData("let n = consume(if ready\n    1\nelse\n    2\n)")]
    public void HeaderDelimiterEndsWithItsBody(string source)
    {
        var tree = Parse(source + "\nlet next = 3");
        AssertValid(tree);
        Assert.Equal(2, tree.GeneratedFunction!.Body!.Items.Count);
        VerifyParents(tree.RootKoto);
    }

    [Theory]
    [InlineData("let n = (if ready\n    1\nlet next = 3")]
    [InlineData("let n = consume(if ready\n    1\nlet next = 3")]
    public void MissingSharedHeaderCloserDoesNotSwallowNextStatement(string source)
    {
        var tree = Parse(source);
        var error = Assert.Single(TestDiagnostics.Of(tree));
        Assert.Equal("MissingSyntax_Kd", error.Code);
        Assert.Equal(2, tree.GeneratedFunction!.Body!.Items.Count);
        Assert.Equal("next", Assert.IsType<FieldKoto>(tree.GeneratedFunction.Body.Items[1]).NameKoto.IdentifierName);
    }

    [Theory]
    [InlineData("let n = (\n        1\n)\nlet next = 3", "IndentationLevelMismatch_Kd")]
    [InlineData("let n = (\n     1\n)\nlet next = 3", "InvalidIndentation_Kd")]
    public void InvalidGroupingIndentationDoesNotInventABody(string source, string code)
    {
        var tree = Parse(source);
        Assert.Equal(code, Assert.Single(TestDiagnostics.Of(tree)).Code);
        Assert.Equal(2, tree.GeneratedFunction!.Body!.Items.Count);
        VerifyParents(tree.RootKoto);
    }

    [Fact]
    public void ChainedOuterClosersExecuteInsideTheirOriginalBody()
        => ScalarEmissionTest.EmitFixture("LayoutOuterCloserChain", "func count() -> i32\n    let values = [\n        7, 9\n    ]\n    let n = (\n        values\n    )\n        .length\n    return n@i32\nrequire count() == 2 else => $abort(\"chain\")", string.Empty);

    [Theory]
    [InlineData("        ", "IndentationLevelMismatch_Kd")]
    [InlineData("     ", "InvalidIndentation_Kd")]
    public void InvalidIndentationKeepsItsLocationAndIndependentErrors(string indent, string code)
    {
        var path = Path.GetFullPath("Layout.kimi");
        var c = MinimalEmissionTest.Analyze("let n = (\n" + indent + "1\n)\nlet wrong: i32 = true", path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        Assert.Equal([code, "TypeMismatch_Kd"], result.Diagnostics.Select(static x => x.Code));
        var error = result.Diagnostics[0];
        Assert.Equal(new SourceSpan(10, indent.Length), error.Span);
        Assert.Equal(new SourceRange(new(1, 0), new(1, indent.Length)), error.Display!.Range);
        Assert.Equal(DiagnosticCategory.Language, error.Category);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("Layout.kimi:2:1", console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var capability in new[] { false, true })
        {
            var sent = WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, capability)[identity];
            Assert.Equal(result.Diagnostics.Select(static x => x.Display!.Range!.Value), sent.Select(static x => x.Range));
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void LayoutContextsReuseTokenStorage()
    {
        var c = Compilation.CreateForTest();
        var diagnostics = c.Diagnostics.GetOrAddCollection("layout");
        var source = new SourceDocument("Layout.kimi", "func f()\n    let n = (if ready\n        [1, 2]\n    else\n        [3, 4]\n    )\n        .length\n    return n\nf()");
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            var tokenizer = new Tokenizer(diagnostics, source);
            try
            {
                tokenizer.ReadAll();
                var bodies = 0;
                foreach (var token in tokenizer.Tokens)
                {
                    bodies += token.Kind == TokenKind.StartBlock ? 1 : token.Kind == TokenKind.EndBlock ? -1 : 0;
                    valid &= !token.IsMissing && bodies >= 0;
                }

                valid &= bodies == 0;
            }
            finally
            {
                tokenizer.Dispose();
            }
        }));
        Assert.True(valid);
        Assert.Empty(TestDiagnostics.Of(c));
    }
}
