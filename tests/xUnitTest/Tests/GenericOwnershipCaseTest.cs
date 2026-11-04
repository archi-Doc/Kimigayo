// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 8.15 Semantics cases: the family corpus of <c>tests/diagnostics/pair-cases.json</c>, written from the SPEC. A generic
/// definition is rejected at definition, with owner-only callers, exactly in the cases the SPEC rejects (at the stated operation with
/// the stated Loan holder), and the valid families run natively in every instance.</summary>
public class GenericOwnershipCaseTest
{
    // The units of plan C that have landed; a row of a later unit is not judged yet, and a `Limit` row is a located limit.
    private static readonly string[] Landed = ["U3b", "E2", "E5", "U4", "U5"];

    private static readonly Lazy<Dictionary<string, PairCaseRow>> Rows = new(() =>
        JsonSerializer.Deserialize<PairCaseRow[]>(File.ReadAllText(DiagnosticCorpus.RepositoryPath("tests", "diagnostics", "pair-cases.json")))!.ToDictionary(x => x.Name, StringComparer.Ordinal));

    public static TheoryData<string> Judged => Names(static _ => true);

    public static TheoryData<string> Executed => Names(static x => x.Run is not null && x.Mir is null);

    public static TheoryData<string> Refused => Names(static x => x.Run is not null && x.Mir is not null);

    // The MIR decides (DECISIONS R5-M185): a rejected case is its published record at the stated operation, whose loan location is a use
    // of the stated Loan holder. A row's `Mir` states the MIR's verdicts where they differ from the SPEC's.
    [Theory]
    [MemberData(nameof(Judged))]
    public void JudgesEveryCaseAtDefinition(string name)
    {
        var row = Rows.Value[name];
        var c = MinimalEmissionTest.Analyze(row.Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        c.ReportAnalysis();
        var records = c.Diagnostics.Finalize(DiagnosticPartition.Input, DiagnosticPartition.Emission).Diagnostics;
        var rejected = false;
        foreach (var expected in (row.Mir?.Expected ?? row.Expected).Values)
        {
            if (expected.ValueKind == JsonValueKind.String)
            {
                continue;
            }

            rejected = true;
            var (at, holder) = Locate(row.Source, expected);
            Assert.Contains(records, x => x.Code == nameof(DiagnosticCode.ComparisonLoanConflict_Kd) && x.Span is { } span &&
                LineAt(row.Source, span.Start) == at && (x.Related ?? []).Any(r => r.Role == "loan" && r.Span is { } use && Leading(row.Source[use.Start..]) == holder));
        }

        if (!rejected)
        {
            Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        }
    }

    [Theory]
    [MemberData(nameof(Executed))]
    public void ValidFamiliesRunInEveryInstance(string name)
    {
        var row = Rows.Value[name];
        var run = row.Run!;
        ScalarEmissionTest.EmitFixture("GenericCase" + name.Replace("-", string.Empty, StringComparison.Ordinal), run.Source, run.Stdout);
    }

    // H5's (e) rows (the owner, 2026-11-03, 「推奨どおり」, board O-1): A4-box-ok and A4-field-ok are valid in every case (SPEC 8.15), and A5
    // over-approximates their uniq case. Their run, which ValidFamiliesRunInEveryInstance generated and ran before the flip, is rejected at
    // each rejected case of the row's `Mir`, at the operation's exact span, and generation writes nothing. PLAN G147 (8) restores the
    // acceptance, removes the `Mir` members and returns the rows to ValidFamiliesRunInEveryInstance.
    [Theory]
    [MemberData(nameof(Refused))]
    public void RunsTheMirRejectsGenerateNothing(string name)
    {
        var row = Rows.Value[name];
        var c = MinimalEmissionTest.Analyze(row.Run!.Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var mir = row.Mir!;
        Assert.False(c.Accepts, mir.Note);
        MirTargetSide.Check(c, [.. mir.Expected.Values.Where(static x => x.ValueKind != JsonValueKind.String).Select(static x =>
            (Enum.Parse<DiagnosticCode>(x.GetProperty("Code").GetString()!), new SyntaxLocation(x.GetProperty("Line").GetString())))]);
        Assert.False(c.Generate(TextWriter.Null));
    }

    // The names of the landed rows a theory selects.
    private static TheoryData<string> Names(Func<PairCaseRow, bool> selected)
    {
        var data = new TheoryData<string>();
        foreach (var row in Rows.Value.Values)
        {
            if (Array.IndexOf(Landed, row.Unit) >= 0 && selected(row))
            {
                data.Add(row.Name);
            }
        }

        return data;
    }

    // A rejection's zero-based operation line and Loan holder, the name that the `let` or `var` line holding the Loan text declares.
    private static (int Line, string Holder) Locate(string source, JsonElement rejection)
    {
        Assert.Equal("ComparisonLoanConflict_Kd", rejection.GetProperty("Code").GetString());
        var line = LineOf(source, rejection.GetProperty("Line").GetString()!);
        var loan = LineOf(source, rejection.GetProperty("Loan").GetString()!);
        var declaration = loan < 0 ? string.Empty : source.Split('\n')[loan].TrimStart();
        Assert.True(line >= 0 && declaration.Length > 4 && declaration[..4] is "let " or "var ", rejection.GetRawText());
        return (line, Leading(declaration[4..]));
    }

    // The identifier a text begins with.
    private static string Leading(string text) => new([.. text.TakeWhile(static x => char.IsLetterOrDigit(x) || x == '_')]);

    // The zero-based line of the first source line containing the text.
    private static int LineOf(string source, string text)
    {
        var lines = source.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(text, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int LineAt(string source, int offset)
    {
        var line = 0;
        for (var i = 0; i < offset && i < source.Length; i++)
        {
            line += source[i] == '\n' ? 1 : 0;
        }

        return line;
    }

    private sealed record PairCaseRow(
        string Name, string Family, string Unit, string[] Spec, string Source, Dictionary<string, JsonElement> Expected, PairCaseRun? Run, PairCaseMir? Mir);

    private sealed record PairCaseRun(string Source, string Stdout);

    // The MIR's verdicts per case, in Expected's form, and the reason they differ from the SPEC's.
    private sealed record PairCaseMir(Dictionary<string, JsonElement> Expected, string Note);
}
