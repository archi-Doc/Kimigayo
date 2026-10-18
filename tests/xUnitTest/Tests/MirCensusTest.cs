// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>The MIR census (docs/dev/DECISIONS.md R4-M12, R4-M74 OQ1; A5 step 2 plan PD22): no problem of any MIR analysis (A1–A5,
/// merged by A6's report) and no validator finding, which would leave a program unanalyzed, in the Binding-complete programs (R4-M47)
/// of four sets: the Kimi library, the milestone programs, the <c>tests/mir</c> goldens and the variants of
/// <c>tests/diagnostics/spec-cases.json</c> the SPEC accepts, each loaded as its own tests load it. A program Binding rejects is
/// outside the census, since that failure may cause its problems, whose report SPEC 23.1.2 leaves implementation-defined; the test
/// output lists such programs. With <c>KIMI_MIR_PROBLEMS</c>
/// naming a directory, each set writes <c>census-&lt;set&gt;.tsv</c> there: per problem or finding its program, analysis (the
/// ledger's, R4-M4; B for a finding), code and subject. The class stays in the normal suite while its tests take under 60 s together
/// in a Session's results.</summary>
public sealed class MirCensusTest(ITestOutputHelper output)
{
    // R4-M12, V17: the library bodies have no problems; the library case binds an empty program.
    [Fact]
    public void TheLibraryHasNoProblems()
        => this.Census("library", Corpus(static x => x == "library"));

    // R4-M12: nor have the milestone programs.
    [Fact]
    public void MilestonesHaveNoProblems()
        => this.Census("milestones", Corpus(static x => x != "library"));

    // Nor have the MIR goldens' programs.
    [Fact]
    public void GoldensHaveNoProblems()
        => this.Census("goldens", MirDumpTest.Programs.Select(static x => (x.Data, MirDumpTest.Compile(x.Data))));

    // Nor has a SPEC example variant without an Error record.
    [Fact]
    public void AcceptedSpecVariantsHaveNoProblems()
        => this.Census("spec", DiagnosticCorpus.SpecCases.SelectMany(static x => x.Variants.Select((y, i) => (Name: $"{x.Name}/{i}", Example: x, Variant: y)))
            .Where(static x => x.Variant.Expected.Length == 0)
            .Select(static x => (x.Name, VerifiedSpecExampleTest.Bind(DiagnosticCorpus.Source(x.Example, x.Variant)))));

    // The library and milestone cases of the HIR and MIR build corpus, bound as their snapshots are.
    private static IEnumerable<(string Name, Compilation Compilation)> Corpus(Func<string, bool> selected)
        => HirColumnsTest.Corpus.Select(static x => x.Data).Where(selected).Select(static x => (x, SemanticSnapshotTest.Bind(x).Compilation));

    // Builds and analyzes each Binding-complete program as it comes, writes the set's rows when asked, lists the other programs and
    // requires some Binding-complete program and no row.
    private void Census(string set, IEnumerable<(string Name, Compilation Compilation)> programs)
    {
        var (rows, outside, complete) = (new List<string>(), new List<string>(), 0);
        foreach (var (name, c) in programs)
        {
            if (!MirDifferenceLedger.BindingComplete(c))
            {
                outside.Add(name);
                continue;
            }

            complete++;
            var program = c.BuildMir();
            rows.AddRange(program.Findings.Select(x => Row(name, "B", DiagnosticCode.InternalInvariant_Kd, c.SyntaxOf(x.Subject))));
            foreach (var problem in program.Problems.All)
            {
                rows.Add(Row(name, MirDifferenceLedger.AnalysisOf(problem.Code), problem.Code, c.SyntaxOf(problem.Subject)));
            }
        }

        if (Environment.GetEnvironmentVariable("KIMI_MIR_PROBLEMS") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllLines(Path.Combine(directory, $"census-{set}.tsv"), ["program\tanalysis\tcode\tsubject", .. rows], new UTF8Encoding(false));
        }

        output.WriteLine($"{set}: {complete} Binding-complete programs; outside: {string.Join(", ", outside.DefaultIfEmpty("none"))}");
        Assert.True(complete > 0, $"{set}: no Binding-complete program");
        Assert.True(rows.Count == 0, $"{set}: {rows.Count} rows in {complete} Binding-complete programs:\n{string.Join('\n', rows.Take(40))}");

        static string Row(string name, string analysis, DiagnosticCode code, Koto node)
            => $"{name}\t{analysis}\t{code}\t{LegacyRecords.Location(node)} {node.ToString().Split('\n')[0].Trim()}";
    }
}
