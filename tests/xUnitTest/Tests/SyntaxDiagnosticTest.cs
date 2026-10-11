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

            // SPEC 23.3.6.9: the candidates the record offers, as kinds and located edits; a record without an expectation offers none.
            var repairs = actual.Repairs ?? [];
            Assert.Equal((expected.Repairs ?? []).Length, repairs.Length);
            for (var j = 0; j < repairs.Length; j++)
            {
                Assert.Equal(expected.Repairs![j].Kind, repairs[j].Kind);
                Assert.Equal(expected.Repairs[j].Edits.Length, repairs[j].Edits.Length);
                for (var k = 0; k < repairs[j].Edits.Length; k++)
                {
                    Assert.Equal(expected.Repairs[j].Edits[k].Location.Resolve(syntaxCase.Source), repairs[j].Edits[k].Span);
                    Assert.Equal(expected.Repairs[j].Edits[k].Text, repairs[j].Edits[k].Text);
                }
            }

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

    // SPEC 23.3.6.8 and 23.4.7: the command shows the form's label at the underline, and both language-server placements carry its
    // range, code and message.
    [Theory]
    [InlineData("missing-body")]
    [InlineData("trailing-token")]
    [InlineData("stray-closer")]
    [InlineData("require-and-symbol")]
    [InlineData("or-symbol")]
    [InlineData("while-stray-token-body")]
    [InlineData("virtual-modifier")]
    [InlineData("override-modifier")]
    public void CliAndLspShowTheFormsLabel(string name)
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
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Equal(error.Code, sent.Code);
            Assert.Contains(error.Message, sent.Message, StringComparison.Ordinal);
        }
    }

    // SPEC 23.3.6.8, 23.3.6.9: the command renders a syntax record's candidate with the edit that supplies its separators.
    [Fact]
    public void CliShowsTheReplacementCandidate()
    {
        var check = DiagnosticCorpus.Check(DiagnosticCorpus.Syntax("and-symbol-adjacent").Source);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(new DiagnosticResult(check.Diagnostics, check.Sources), string.Empty);
        output.WriteLine(console.Text);
        Assert.Contains("Repair: Replace '&&' with 'and'\n = ", console.Text, StringComparison.Ordinal);
        Assert.Contains("Program.kimi:3:14: replace '&&' with ' and '\n", console.Text, StringComparison.Ordinal);
    }

    // DIAGNOSTICS.md §4.3: a fallthrough or a discarded tail reached only through a body the parser supplied rests on the body's
    // syntax Error, so the console and the language server show the syntax record alone; one that written syntax reaches stays,
    // and the result it leaves undelivered rests on the fallthrough (§2 rule 1).
    [Theory]
    [InlineData("guessed-then-body", "MissingSyntax_Kd")]
    [InlineData("guessed-discarded-tail", "MissingSyntax_Kd")]
    [InlineData("written-branch-falls-through", "FunctionFallthrough_Kd,MissingSyntax_Kd,DiscardedValue_Kd")]
    [InlineData("written-fallthrough", "FunctionFallthrough_Kd,DiscardedValue_Kd")]
    public void CliAndLspShowTheRecordsOfSuppliedBodies(string name, string codes)
    {
        var check = DiagnosticCorpus.Check(DiagnosticCorpus.Syntax(name).Source);
        Assert.Equal(codes.Split(','), check.Diagnostics.Select(static x => x.Code));
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(new DiagnosticResult(check.Diagnostics, check.Sources), string.Empty);
        output.WriteLine(console.Text);
        foreach (var record in check.Diagnostics)
        {
            Assert.Contains(record.Code, console.Text, StringComparison.Ordinal);
            Assert.Contains(record.Label ?? record.Message, console.Text, StringComparison.Ordinal);
        }

        var identity = SourceIdentity.FromPath(check.Sources[check.Diagnostics[0].Source].Path!);
        foreach (var related in new[] { false, true })
        {
            var sent = WorkspaceCheck.Place(check, [identity], identity, related)[identity];
            Assert.Equal(check.Diagnostics.Select(static x => x.Code), sent.Select(static x => x.Code));
            Assert.Equal(check.Diagnostics.Select(static x => x.Display!.Range), sent.Select(static x => (SourceRange?)x.Range));
        }
    }

    // A body, a require failure body and a match end at their last written item (DIAGNOSTICS.md §4.4): the console underlines no
    // blank or comment line after them, and the language server's range ends at the end of that item's line.
    [Theory]
    [InlineData("fallthrough-before-blank-lines")]
    [InlineData("fallthrough-before-crlf-blank-lines")]
    [InlineData("fallthrough-before-indented-comment")]
    [InlineData("fallthrough-at-source-end")]
    [InlineData("require-fallthrough-before-blank-line")]
    [InlineData("accessor-fallthrough-before-blank-lines")]
    [InlineData("match-before-indented-comment")]
    public void CliAndLspEndARecordAtTheLastWrittenItem(string name)
    {
        var source = DiagnosticCorpus.Syntax(name).Source;
        var check = DiagnosticCorpus.Check(source);
        Assert.NotEmpty(check.Diagnostics);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(new DiagnosticResult(check.Diagnostics, check.Sources), string.Empty);
        output.WriteLine(console.Text);
        var lines = source.ReplaceLineEndings("\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            // An excerpt line starts with its line number and a bar; no record of these cases is located at a blank or comment line.
            if (lines[i].TrimStart() is "" or ['/', '/', ..])
            {
                Assert.DoesNotMatch($@"(?m)^\s*{i + 1} \|", console.Text);
            }
        }

        var identity = SourceIdentity.FromPath(check.Sources[check.Diagnostics[0].Source].Path!);
        foreach (var related in new[] { false, true })
        {
            foreach (var sent in WorkspaceCheck.Place(check, [identity], identity, related)[identity])
            {
                Assert.Equal(lines[sent.Range.End.Line].TrimEnd().Length, sent.Range.End.Character);
            }
        }
    }

    private static string Describe(CheckDiagnostic diagnostic)
        => $"{diagnostic.Code} {diagnostic.Span}: {diagnostic.Message}" + (diagnostic.Reason is null ? string.Empty : " [" + string.Join(", ", diagnostic.Reason.Select(static x => $"{x.Name}={x.Value}")) + "]");
}
