// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public sealed class OriginSyntaxDiagnosticTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("View<i32>{}", "{}", "OriginBindingSetName_Kd")]
    [InlineData("View<i32>{static}", "{static}", "OriginBindingSetName_Kd")]
    [InlineData("View<i32>{_}", "{_}", "OriginBindingSetName_Kd")]
    [InlineData("View<i32>{a.source}", "{a.source}", "OriginBindingSetName_Kd")]
    [InlineData("View<i32>{a and b}", "{a and b}", "OriginBindingSetName_Kd")]
    [InlineData("View<i32>{a, b}", "{a, b}", "OriginBindingSetName_Kd")]
    [InlineData("View<i32>{a => static}", "{a => static}", "OriginBindingSetName_Kd")]
    [InlineData("View<i32>{a, b => static}", "{a, b => static}", "OriginBindingSetName_Kd")]
    [InlineData("View<i32>{a => static, b}", "{a => static, b}", "OriginBindingSetName_Kd")]
    [InlineData("(ref/i32){a}", "{a}", "OriginBindingSetTarget_Kd")]
    [InlineData("[2 of i32]{a}", "{a}", "OriginBindingSetTarget_Kd")]
    [InlineData("ref{a}/i32", "{a}", "LegacyBorrowOrigin_Kd")]
    [InlineData("s{a}/i32", "{a}", "LegacyBorrowOrigin_Kd")]
    public void ExplainsBindingSetSuffixAtTheSuffix(string type, string marked, string code)
    {
        var source = $"func f(x: {type}) => ()";
        var error = Assert.Single(Parse(source).Diagnostics.Finalize(rejected: true).Diagnostics);
        Assert.Equal(code, error.Code);
        Assert.Equal(new SourceSpan(source.IndexOf(marked, StringComparison.Ordinal), marked.Length), error.Span);
        Assert.Equal(DiagnosticCategory.Language, error.Category);
        Assert.False(string.IsNullOrWhiteSpace(error.Label));
        Assert.False(string.IsNullOrWhiteSpace(error.Advice));
    }

    [Theory]
    [InlineData("View<i32>{set}")]
    [InlineData("View<i32>{set,}")]
    [InlineData("(View<i32>{set})")]
    [InlineData("ref/View<i32>{set} during a")]
    public void BindingSetsStillNameOneOccurrence(string type)
        => Assert.Empty(Parse($"func f(x: {type}) => ()").Diagnostics.Finalize().Diagnostics);

    [Fact]
    public void BindingSetRecoveryKeepsIndependentProblems()
    {
        const string source = "func f(x: (ref/i32){static}, y: ref{a}/i32, z: ref/i32 during b?) => ()\nlet n = (1";
        var errors = Parse(source).Diagnostics.Finalize(rejected: true).Diagnostics;
        Assert.Equal(["OriginBindingSetName_Kd", "OriginBindingSetTarget_Kd", "LegacyBorrowOrigin_Kd", "BorrowOriginSuffixOrder_Kd", "MissingSyntax_Kd"], errors.Select(static x => x.Code));
    }

    [Fact]
    public void LegacyBindingSetRecoveryKeepsIndependentBindingErrors()
    {
        const string source = "func f(x: ref{a}/i32) => ()\nlet n: i32 = true";
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        var result = c.Diagnostics.Finalize(rejected: true);
        Assert.Equal(["LegacyBorrowOrigin_Kd", "TypeMismatch_Kd"], result.Diagnostics.Select(static x => x.Code));
        Assert.False(c.Emission.Validate(out _));
        c.Bind();
        c.Binding.ReportDiagnostics();
        Assert.Equal(result, c.Diagnostics.Finalize(rejected: true));
    }

    [Theory]
    [InlineData("func f(x: ref/i32 during a, y: ref/i32 during b)\n    origin a b\n    ()", "b\n", "OriginRelationOperator_Kd")]
    [InlineData("func f(x: ref/i32 during a, y: ref/i32 during b)\n    origin a > b\n    ()", "> b", "OriginRelationOperator_Kd")]
    [InlineData("func f(x: ref/i32 during a, y: ref/i32 during b)\n    origin a = b\n    ()", "= b", "OriginRelationOperator_Kd")]
    [InlineData("func f(x: ref/i32 during a, y: ref/i32 during b)\n    origin a != b\n    ()", "!= b", "OriginRelationOperator_Kd")]
    [InlineData("func f(x: ref/i32 during a, y: ref/i32 during b)\n    origin a < b\n    ()", "< b", "OriginRelationOperator_Kd")]
    [InlineData("func f(x: ref/i32 during a, y: ref/i32 during b)\n    origin a <= b\n    ()", "<= b", "OriginRelationOperator_Kd")]
    [InlineData("func f(x: ref/i32 during a, y: ref/i32 during b)\n    origin a >= b\n    ()", ">= b", "OriginRelationOperator_Kd")]
    [InlineData("func f(x: ref/i32)\n    var pending: ref/i32\n        let y = 1", "let y", "AttachedOriginRelation_Kd")]
    [InlineData("func f {a}(x: ref/i32 during a) => ()", "{a}", "CallableOriginList_Kd")]
    [InlineData("struct S\n    init {a}(x: ref/i32 during a) => ()", "{a}", "CallableOriginList_Kd")]
    [InlineData("struct S {a : b, b}", ":", "OriginSchemaRelation_Kd")]
    [InlineData("func f(x: ref/i32 during a and b) => ()", "and", "BorrowOriginIntersection_Kd")]
    [InlineData("func f(x: ref/i32 during a and b and c) => ()", "and", "BorrowOriginIntersection_Kd")]
    public void ExplainsOriginGrammarWithoutDelimiterCascades(string source, string marked, string code)
    {
        var error = Assert.Single(Parse(source).Diagnostics.Finalize(rejected: true).Diagnostics);
        Assert.Equal(code, error.Code);
        Assert.Equal(source.IndexOf(marked, StringComparison.Ordinal), error.Span!.Value.Start);
        Assert.Equal(DiagnosticCategory.Language, error.Category);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.False(string.IsNullOrWhiteSpace(error.Label));
        Assert.False(string.IsNullOrWhiteSpace(error.Advice));
        Assert.DoesNotContain("Unexpected token", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("func f(x: ref/i32 during a, y: ref/i32 during b)\n    origin a == b\n    ()")]
    [InlineData("func f(x: ref/i32 during a, y: ref/i32 during b)\n    origin a outlives b\n    ()")]
    [InlineData("struct S {a, b}\n    origin a outlives b")]
    [InlineData("struct S {a}\n    let x: ref/i32 during a\n        origin a == a")]
    [InlineData("func f<T, U>(x: T)\n    T is ref/U during a and Copy\n    ()")]
    public void PreservesRelationAndRequirementGrammar(string source)
        => Assert.Empty(Parse(source).Diagnostics.Finalize().Diagnostics);

    [Fact]
    public void IntersectionRecoveryRetainsLaterIndependentErrors()
    {
        const string source = "func f(x: ref/i32 during a and b, y: ref/i32 during c?) => ()\nlet n = (1";
        var errors = Parse(source).Diagnostics.Finalize(rejected: true).Diagnostics;
        Assert.Equal(["BorrowOriginIntersection_Kd", "BorrowOriginSuffixOrder_Kd", "MissingSyntax_Kd"], errors.Select(static x => x.Code));
    }

    // SPEC 3.3.6 and 15.3.1 fix annotation attachment and order before semantic lookup.
    [Theory]
    [InlineData("ref/i32 from a", "from", "BorrowOriginKeyword_Kd")]
    [InlineData("owner/ref/i32 during a", "during", "BorrowOriginSemantics_Kd")]
    [InlineData("raw/ref/i32 during a", "during", "BorrowOriginSemantics_Kd")]
    [InlineData("(ref/i32)? during a", "during", "BorrowOriginTarget_Kd")]
    [InlineData("(ref/i32 during a) during b", "during b", "BorrowOriginTarget_Kd")]
    [InlineData("View<i32>{v} during a", "during", "BorrowOriginBindingSet_Kd")]
    [InlineData("ref/i32 during a?", "?", "BorrowOriginSuffixOrder_Kd")]
    [InlineData("ref/i32 during a during b", "during b", "DuplicateBorrowOrigin_Kd")]
    public void ExplainsAnnotationFailureAtItsSyntax(string type, string marked, string code)
    {
        var source = $"func f(x: {type}) => ()";
        var error = Assert.Single(Parse(source).Diagnostics.Finalize(rejected: true).Diagnostics);
        Assert.Equal(code, error.Code);
        Assert.Equal(DiagnosticCategory.Language, error.Category);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal(new SourceSpan(source.IndexOf(marked, StringComparison.Ordinal), marked.StartsWith("during", StringComparison.Ordinal) ? 6 : marked.Length), error.Span);
        Assert.DoesNotContain("Unexpected token", error.Message, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(error.Label));
        Assert.True(error.Repairs is not null || !string.IsNullOrWhiteSpace(error.Advice), "a repair candidate or Advice explains the repair");
        Assert.Null(error.Reason);
    }

    // SPEC 3.3.6, 23.3.6.9: the former keyword offers 'during' as a repair candidate, and no Advice repeats the edit.
    [Fact]
    public void TheFormerKeywordOffersDuring()
    {
        const string source = "func f(x: ref/i32 from a) => ()";
        var error = Assert.Single(Parse(source).Diagnostics.Finalize(rejected: true).Diagnostics);
        Assert.Equal("BorrowOriginKeyword_Kd", error.Code);
        Assert.Null(error.Advice);
        var repair = Assert.Single(error.Repairs!);
        Assert.Equal("Repair.ReplaceToken", repair.Kind);
        Assert.Equal("Replace 'from' with 'during'", repair.Title);
        Assert.Empty(repair.Verified);
        Assert.Empty(repair.Required);
        Assert.Equal(new RepairEdit(0, new(18, 4), new SourceRange(new(0, 18), new(0, 22)), "during", "from"), Assert.Single(repair.Edits));
    }

    [Theory]
    [InlineData("ref/i32 during a")]
    [InlineData("ref/i32? during a")]
    [InlineData("(ref/i32 during a)?")]
    [InlineData("ref/i32 during (a and b)")]
    [InlineData("raw/(ref/i32 during a)")]
    [InlineData("ref/(uniq/i32 during b) during a")]
    [InlineData("View<i32>{v}")]
    [InlineData("View<i32> during a")]
    public void AcceptsTheCorrespondingTypeSyntax(string type)
        => Assert.Empty(Parse($"func f(x: {type}) => ()").Diagnostics.Finalize().Diagnostics);

    [Fact]
    public void RepeatedAnnotationsAndIndependentErrorsKeepTheirLocations()
    {
        const string source = "\nfunc f(x: ref/i32 during a during b?) => ()\nlet n = (1";
        var errors = Parse(source).Diagnostics.Finalize(rejected: true).Diagnostics;
        Assert.Equal(["DuplicateBorrowOrigin_Kd", "BorrowOriginSuffixOrder_Kd", "MissingSyntax_Kd"], errors.Select(static x => x.Code));
        Assert.Equal(new SourceSpan(source.IndexOf("during b", StringComparison.Ordinal), 6), errors[0].Span);
        Assert.Equal(new SourceSpan(source.IndexOf('?'), 1), errors[1].Span);
    }

    [Fact]
    public void BindingKeepsTheFirstAnnotationAndIndependentTypeFailure()
    {
        const string source = "func f(x: ref/i32 during a during b) -> ref/i32 during a => x\nlet n: i32 = true";
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        var result = c.Diagnostics.Finalize(rejected: true);
        Assert.Equal(["DuplicateBorrowOrigin_Kd", "TypeMismatch_Kd"], result.Diagnostics.Select(static x => x.Code));
        Assert.False(c.Emission.Validate(out _));
        c.Bind();
        c.Binding.ReportDiagnostics();
        Assert.Equal(result, c.Diagnostics.Finalize(rejected: true));
    }

    [Theory]
    [InlineData("func f(x: ref/i32 during a?) => ()", "An optional suffix cannot follow an Origin annotation in the same Type", "optional suffix follows the annotation")]
    [InlineData("func f(x: ref/i32 during a and b) => ()", "An Origin intersection after 'during' requires parentheses", "intersection is outside parentheses")]
    [InlineData("func f(x: View<i32>{a, b}) => ()", "A Type binding-set suffix contains one new set Name, not Origin values, lists or mappings", "expected one binding-set Name in braces")]
    public void CliAndLspExplainAnnotationOrder(string source, string message, string label)
    {
        var path = Path.GetFullPath("origin-syntax.kimi");
        var c = Parse(source, path);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(message, error.Message);
        Assert.Equal(label, error.Label);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("^ " + label, console.Text, StringComparison.Ordinal);
        Assert.Contains(error.Advice!, console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Equal(error.Code, sent.Code);
            Assert.Contains(error.Message, sent.Message, StringComparison.Ordinal);
            Assert.Contains(error.Advice!, sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    private static Compilation Parse(string source, string path = "origin-syntax.kimi")
    {
        var c = Compilation.CreateForTest();
        c.Kotonoha.AddSource(new SourceDocument(path, source));
        return c;
    }
}
