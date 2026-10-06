// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class FunctionItemDiagnosticTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DifferentDeclarationsKeepTheirQualifiedNamesInPublicDiagnostics(bool nested)
    {
        var declarations = "group A\n    public func inc(value: i32) -> i32 => value + 1\ngroup B\n    public func inc(value: i32) -> i32 => value - 1\n";
        var prefix = string.Empty;
        if (nested)
        {
            declarations = "group Outer\n" + string.Join('\n', declarations.TrimEnd('\n').Split('\n').Select(static line => "    " + line.Replace("group ", "public group ", StringComparison.Ordinal))) + "\n";
            prefix = "Outer.";
        }

        var actual = prefix + "B.inc";
        var expected = prefix + "A.inc";
        var source = declarations + "var f = " + expected + "\nf = " + actual;
        var path = Path.GetFullPath("item-identities.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.TypeMismatch_Kd), error.Code);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal(DiagnosticCategory.Language, error.Category);
        Assert.Equal(actual, source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Equal(["function item " + actual, "function item " + expected], error.Reason!.Select(static x => x.Value));
        Assert.Equal("expected function item " + expected + ", found function item " + actual, error.Label);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains(error.Label!, console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(error.Code, sent.Code);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Contains("function item " + actual, sent.Message, StringComparison.Ordinal);
            Assert.Contains("function item " + expected, sent.Message, StringComparison.Ordinal);
        }

        ScalarEmissionTest.EmitFixture("FunctionItemNames" + nested, declarations + "var f = " + expected + "\nf = " + expected + "\nrequire f(41) == 42 else => $abort(\"same item\")", string.Empty);
    }
}
