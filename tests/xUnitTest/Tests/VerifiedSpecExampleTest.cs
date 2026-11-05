// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// These regressions read the normative example itself, so a stale copied test cannot mask drift. The SPEC examples of
// tests/diagnostics/spec-cases.json publish exactly the Error records the SPEC gives them (plan §8 of the Complete Polonius A5 Plan,
// DECISIONS R4-M45), or the MIR's recorded difference (plan §9.2): since R6-check H5 the MIR decides every verdict (DECISIONS R5-M185).
public class VerifiedSpecExampleTest
{
    /// <summary>The deciders a SPEC example names: Binding, the analyses A1–A4 and the A5 families F1–F10 (plan §6.1) but F6, which
    /// the adopted proposal Abstract Calls Reach by Paths retired.</summary>
    internal static readonly string[] Families = ["Binding", "A1", "A3", "A4", "F1", "F2", "F3", "F4", "F5", "F7", "F8", "F9", "F10"];

    private const string Document = "Hello.kimi";

    public static TheoryData<string> Variants => [.. DiagnosticCorpus.SpecCases.SelectMany(static x => x.Variants.Select((_, i) => $"{x.Name}/{i}"))];

    [Fact]
    public void ComparisonInspectionExampleRuns()
    {
        var example = DiagnosticCorpus.SpecCases.Single(static x => x.Name == "13.4-comparison-inspection");
        ScalarEmissionTest.EmitFixture("SpecExampleComparisonInspection", DiagnosticCorpus.SpecExample(example.Section, example.Anchor), "a\n");
    }

    // Every case has a unique name, one variant as written, known deciders and a record, variant or omission for each error verdict of its
    // block; every record has a code of Error severity, and every recorded difference a class, sections the SPEC has and a note.
    [Fact]
    public void TheExampleTableIsWellFormed()
    {
        var cases = DiagnosticCorpus.SpecCases;
        Assert.NotEmpty(cases);
        Assert.Equal(cases.Count, cases.Select(static x => x.Name).Distinct(StringComparer.Ordinal).Count());
        foreach (var example in cases)
        {
            Assert.Contains(example.Family, Families);
            AssertVerdictsCovered(example, Assert.Single(example.Variants, static x => x.Uncomment is null).Expected);
            foreach (var variant in example.Variants)
            {
                Assert.True(variant.Family is null || Families.Contains(variant.Family), example.Name);
                if (variant.Mir is { } difference)
                {
                    Assert.Contains(difference.Category, (string[])["a", "b", "c", "d", "e"]);
                    Assert.All(difference.Clause.Split(' '), x => Assert.Contains(x, DiagnosticCorpus.SpecSections));
                    Assert.NotEqual(string.Empty, difference.Note.Trim());
                }

                Assert.All(variant.Expected.Concat(variant.Mir?.Records ?? []), x => Assert.True(
                    Enum.TryParse<DiagnosticCode>(x.Code, out var code) && DiagnosticEntries.TryGet(code, out var entry) && entry.Severity == DiagnosticSeverity.Error, $"{example.Name}: {x.Code}"));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void ExamplesReportTheirExpectedCodes(string name)
    {
        var example = DiagnosticCorpus.SpecCases.Single(x => x.Name == name[..name.LastIndexOf('/')]);
        var variant = example.Variants[int.Parse(name[(name.LastIndexOf('/') + 1)..], System.Globalization.CultureInfo.InvariantCulture)];
        var source = DiagnosticCorpus.Source(example, variant);
        var c = Bind(source);
        c.Analyze();
        c.Binding.ReportDiagnostics();
        c.ReportAnalysis();
        AssertRecords(source, variant.Mir?.Records ?? variant.Expected, TestDiagnostics.Of(c));
    }

    // Binds a variant's source as the Hello.kimi of an Application, whose startup has no issue.
    internal static Compilation Bind(string source)
    {
        var c = CompilationTestHelper.ParseSuccess(source, Document);
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.Empty(c.Binding.StartupIssues);
        return c;
    }

    // An error verdict (`// Error` on a line, or on an explanation line after it) of an active line needs a record of the block as written on
    // that line, and of a commented line a variant that uncomments it or an omission.
    private static void AssertVerdictsCovered(SpecCase example, SpecRecord[] written)
    {
        var lines = DiagnosticCorpus.SpecExample(example.Section, example.Anchor).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Contains("// Error", StringComparison.Ordinal))
            {
                continue;
            }

            var at = i;
            while (at > 0 && lines[at].TrimStart().StartsWith("// Error", StringComparison.Ordinal))
            {
                at--;
            }

            var line = lines[at].Trim();
            var covered = line.StartsWith("// ", StringComparison.Ordinal)
                ? example.Variants.Any(x => x.Uncomment is { } text && line[3..].StartsWith(text, StringComparison.Ordinal)) || (example.Omitted?.Keys.Any(x => line[3..].StartsWith(x, StringComparison.Ordinal)) ?? false)
                : written.Any(x => line.Contains(x.Line, StringComparison.Ordinal));
            Assert.True(covered, $"{example.Name}: the verdict of '{line}' has no record, variant or omission.");
        }
    }

    // Compares the published Error records with the expected ones as "code line: text" rows, a record's line being the one holding its
    // primary location; a dependent record may be absent.
    private static void AssertRecords(string source, SpecRecord[] expected, IEnumerable<TestDiagnostic> records)
    {
        var lines = source.Split('\n');
        var observed = records.Where(static x => x.Severity == DiagnosticSeverity.Error).Select(x =>
            x.Path == Document ? Row(x.Code, x.Span.Start) : $"{x.Code} {x.Path}:{x.Span.Start}")
            .Order(StringComparer.Ordinal).ToList();
        var required = new List<string>();
        foreach (var record in expected)
        {
            var row = Row(record.Code, new SyntaxLocation(record.Line).Resolve(source).Start);
            if (record.Dependent)
            {
                observed.Remove(row);
            }
            else
            {
                required.Add(row);
            }
        }

        required.Sort(StringComparer.Ordinal);
        Assert.True(required.SequenceEqual(observed), $"expected\n  {string.Join("\n  ", required)}\nobserved\n  {string.Join("\n  ", observed)}");

        string Row(string code, int offset)
        {
            var line = source.AsSpan(0, offset).Count('\n');
            return $"{code} {line + 1}: {lines[line].Trim()}";
        }
    }
}
