// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// docs/dev/DIAGNOSTICS.md §4.4 and §9.2: the syntax corpus. Each case of tests/diagnostics/syntax.json lists every record
// its complete check publishes, written independently of the parser: no cascade, no repeated token and no unexplained
// derived record survives, and a form that no case reaches is not in the vocabulary.
public sealed class SyntaxDiagnosticTest(ITestOutputHelper output)
{
    public static TheoryData<string> CaseNames => [.. DiagnosticCorpus.SyntaxCases.Select(static x => x.Name)];

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void ACasePublishesExactlyItsExpectedRecords(string name)
    {
        var syntaxCase = DiagnosticCorpus.Syntax(name);
        var result = DiagnosticCorpus.Check(syntaxCase.Source);
        var detail = string.Join("\n", result.Diagnostics.Select(Describe));
        output.WriteLine(detail);
        Assert.Equal(CheckOutcome.Completed, result.Outcome);
        Assert.True(syntaxCase.Records.Length == 0 == result.Accepted, detail);
        Assert.True(syntaxCase.Records.Length == result.Diagnostics.Length, detail);
        for (var i = 0; i < syntaxCase.Records.Length; i++)
        {
            var expected = syntaxCase.Records[i];
            var actual = result.Diagnostics[i];
            Assert.Equal(expected.Code, actual.Code);
            Assert.Equal(expected.At is null && expected.After is null ? null : expected.Location.Resolve(syntaxCase.Source), actual.Span);
            if (expected.Form is null)
            {
                continue;
            }

            // A syntax record states its form and, when a token was found, that token, as exact facts (SPEC 23.3.6.2).
            var form = DiagnosticRequirement.SyntaxOf(Enum.Parse<SyntaxForm>(expected.Form));
            Assert.Equal(DiagnosticCategory.Language, actual.Category);
            Assert.Equal(DiagnosticSeverity.Error, actual.Severity);
            Assert.Equal(form.Name, Assert.Single(actual.Reason!, static x => x.Kind == DiagnosticValueKind.Requirement).Value);
            Assert.Equal(expected.Found, actual.Reason!.SingleOrDefault(static x => x.Name == "found").Value);
            Assert.True(DiagnosticRequirements.TryGetPhrase(form, out var phrase));
            Assert.Contains(phrase, actual.Message, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(actual.Label));
            Assert.Equal(DiagnosticRequirements.AdviceOf(form), actual.Advice);
            foreach (var (related, index) in (expected.Related ?? []).Select(static (x, i) => (x, i)))
            {
                Assert.Equal(related.Role, actual.Related![index].Role);
                Assert.Equal(related.Location.Resolve(syntaxCase.Source), actual.Related[index].Span);
            }
        }
    }

    // Every form the vocabulary names is reached by a case, so a form added to the enumeration needs its table row and its case.
    [Fact]
    public void EveryFormHasAPhraseAndACase()
    {
        var reached = DiagnosticCorpus.SyntaxCases.SelectMany(static x => x.Records).Select(static x => x.Form).Where(static x => x is not null).ToHashSet(StringComparer.Ordinal);
        foreach (var form in Enum.GetValues<SyntaxForm>())
        {
            var requirement = DiagnosticRequirement.SyntaxOf(form);
            Assert.Equal(form != SyntaxForm.None, DiagnosticRequirements.TryGetPhrase(requirement, out _));
            Assert.True(DiagnosticRequirements.TryGetDescription(requirement, out _), requirement.Name);
            if (form != SyntaxForm.None)
            {
                Assert.Contains(form.ToString(), reached);
            }
        }

        Assert.Empty(DiagnosticRequirements.Anomalies);
    }

    // SPEC 23.3.6.8 and 23.4.7: both adapters show the form's label at the underline and the form's advice.
    [Theory]
    [InlineData("missing-body")]
    [InlineData("trailing-token")]
    [InlineData("stray-closer")]
    public void CliAndLspShowTheFormsLabelAndAdvice(string name)
    {
        var syntaxCase = DiagnosticCorpus.Syntax(name);
        var path = Path.GetFullPath("syntax-" + name + ".kimi");
        var c = Compilation.CreateForTest();
        c.Kotonoha.AddSource(new SourceDocument(path, syntaxCase.Source));
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        Assert.Contains("^ " + error.Label, console.Text, StringComparison.Ordinal);
        Assert.Contains(error.Advice!, console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Equal(error.Code, sent.Code);
            Assert.Contains(error.Message, sent.Message, StringComparison.Ordinal);
            Assert.Contains(error.Advice!, sent.Message, StringComparison.Ordinal);
        }
    }

    private static string Describe(CheckDiagnostic diagnostic)
        => $"{diagnostic.Code} {diagnostic.Span}: {diagnostic.Message}" + (diagnostic.Reason is null ? string.Empty : " [" + string.Join(", ", diagnostic.Reason.Select(static x => $"{x.Name}={x.Value}")) + "]");
}
