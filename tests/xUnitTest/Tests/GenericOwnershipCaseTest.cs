// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 8.15 Semantics cases: the family corpus of <c>tests/diagnostics/pair-cases.json</c>, written from the SPEC. A generic
/// definition is rejected at definition, with owner-only callers, exactly in the cases the SPEC rejects (at the stated operation with
/// the stated Loan holder), and the valid families run natively in every instance. A row's <c>Old</c> states the verdicts and run of the
/// default pipeline, which keeps a rule an adopted SPEC revision replaced until R6-check; those are checked here (DECISIONS R4-M60).</summary>
public class GenericOwnershipCaseTest
{
    // The units of plan C that have landed; a row of a later unit is not judged yet, and a `Limit` row is a located limit.
    private static readonly string[] Landed = ["U3b", "E2", "E5", "U4", "U5"];

    private static readonly Lazy<Dictionary<string, PairCaseRow>> Rows = new(() =>
        JsonSerializer.Deserialize<PairCaseRow[]>(File.ReadAllText(DiagnosticCorpus.RepositoryPath("tests", "diagnostics", "pair-cases.json")))!.ToDictionary(x => x.Name, StringComparer.Ordinal));

    public static TheoryData<string> Judged
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var row in Rows.Value.Values)
            {
                if (Array.IndexOf(Landed, row.Unit) >= 0)
                {
                    data.Add(row.Name);
                }
            }

            return data;
        }
    }

    public static TheoryData<string> Executed
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var row in Rows.Value.Values)
            {
                if (Array.IndexOf(Landed, row.Unit) >= 0 && (row.Old?.Run ?? row.Run) is not null)
                {
                    data.Add(row.Name);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Judged))]
    public void JudgesEveryCaseAtDefinition(string name)
    {
        var row = Rows.Value[name];
        var c = MinimalEmissionTest.Analyze(row.Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(row.Old is null || row.Old.Expected.Keys.All(row.Expected.ContainsKey), $"{name}: an old verdict names no Semantics case of the row.");
        var rejected = false;
        foreach (var (semantics, written) in row.Expected)
        {
            var expected = row.Old is { } old && old.Expected.TryGetValue(semantics, out var kept) ? kept : written;
            if (expected.ValueKind == JsonValueKind.String)
            {
                continue;
            }

            rejected = true;
            var code = expected.GetProperty("Code").GetString();
            var line = expected.GetProperty("Line").GetString()!;
            var loan = expected.GetProperty("Loan").GetString()!;
            Assert.Equal("ComparisonLoanConflict_Kd", code);
            var at = LineOf(row.Source, line);
            Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict && LineAt(row.Source, x.Source.Span.Start) == at &&
                x.LoanSource is { } holder && Text(holder).Contains(loan, StringComparison.Ordinal));
        }

        if (!rejected)
        {
            Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c, null));
        }
    }

    [Theory]
    [MemberData(nameof(Executed))]
    public void ValidFamiliesRunInEveryInstance(string name)
    {
        var row = Rows.Value[name];
        var run = (row.Old?.Run ?? row.Run)!;
        ScalarEmissionTest.EmitFixture("GenericCase" + name.Replace("-", string.Empty, StringComparison.Ordinal), run.Source, run.Stdout);
    }

    private static string Text(Kimi.Compiler.Parsing.Koto node) => node.ToString().Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

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

    private sealed record PairCaseRow(string Name, string Family, string Unit, string[] Spec, string Source, Dictionary<string, JsonElement> Expected, PairCaseRun? Run, PairCaseOld? Old);

    private sealed record PairCaseRun(string Source, string Stdout);

    // The default pipeline's verdicts of the Semantics cases that differ from the SPEC's, and its native run, under the rule replaced by
    // the revision item that `Rule` names as R4-M50's trait does; removed at R6-check.
    private sealed record PairCaseOld(string Rule, Dictionary<string, JsonElement> Expected, PairCaseRun? Run);
}
