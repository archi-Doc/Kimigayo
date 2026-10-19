// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>The MIR census (docs/dev/DECISIONS.md R4-M12, R4-M74 OQ1; A5 step 2 plan PD22) over four sets, each loaded as its own tests
/// load it: the Kimi library, the milestone programs, the <c>tests/mir</c> goldens and the variants of
/// <c>tests/diagnostics/spec-cases.json</c> the SPEC accepts. In any program (R4-M12): no validator finding, which would leave it
/// unanalyzed, no problem outside the ledger's MIR pairs (a recording rule's defect, B) and no A5 problem (the analysis the ledger gives
/// a problem's requirement and code, R4-M4, R4-M67: A1's storage limit shares A5's pair and counts, A5's located Unsupported_Kd shares
/// A4's and does not). In a Binding-complete program (R4-M47): no construction record, whose Opaque value would leave it partly
/// analyzed, and no problem of any MIR analysis (A1–A5, merged by A6's report). A program Binding rejects may have other records and
/// problems that failure causes, whose report SPEC 23.1.2 leaves implementation-defined: the test output lists them, not asserted. With
/// <c>KIMI_MIR_PROBLEMS</c> naming a directory, each set writes <c>census-&lt;set&gt;.tsv</c> there: per record, finding or problem
/// its program, the program's section (the ledger's: complete or incomplete), analysis (the ledger's; B for a record, a finding or a
/// defect), code and subject. The class stays in the normal suite while its tests take under 60 s together in a Session's results.</summary>
public sealed class MirCensusTest(ITestOutputHelper output)
{
    // R4-M12, V17: the library bodies have no problems; the library case binds an empty program.
    [Fact]
    public void TheLibraryHasNoProblems()
        => this.Census("library", Corpus(static x => x == "library"));

    // R4-M12: nor have the milestone programs Binding accepts, and the others have no A5 problem.
    [Fact]
    public void MilestonesHaveNoProblems()
        => this.Census("milestones", Corpus(static x => x.StartsWith("milestone/", StringComparison.Ordinal)));

    // Nor have the MIR goldens' programs, as the milestones.
    [Fact]
    public void GoldensHaveNoProblems()
        => this.Census("goldens", MirDumpTest.Programs.Select(static x => (x.Data, MirDumpTest.Compile(x.Data))));

    // Nor has a SPEC example variant without an Error record, as the milestones.
    [Fact]
    public void AcceptedSpecVariantsHaveNoProblems()
        => this.Census("spec", DiagnosticCorpus.SpecCases.SelectMany(static x => x.Variants.Select((y, i) => (Name: $"{x.Name}/{i}", Example: x, Variant: y)))
            .Where(static x => x.Variant.Expected.Length == 0)
            .Select(static x => (x.Name, VerifiedSpecExampleTest.Bind(DiagnosticCorpus.Source(x.Example, x.Variant)))));

    // The selected cases of the semantic snapshot corpus, bound as their snapshots are.
    private static IEnumerable<(string Name, Compilation Compilation)> Corpus(Func<string, bool> selected)
        => SemanticSnapshotTest.CaseNames().Where(selected).Select(static x => (x, SemanticSnapshotTest.Bind(x).Compilation));

    // Builds and analyzes each program as it comes, writes the set's rows when asked, lists the programs Binding rejects with their rows
    // not asserted, and requires a Binding-complete program and no asserted row: a finding, a defect or an A5 problem in any program,
    // any record or problem in a Binding-complete one.
    private void Census(string set, IEnumerable<(string Name, Compilation Compilation)> programs)
    {
        var (rows, rejected, count) = (new List<(string Text, bool Asserted)>(), new List<string>(), 0);
        foreach (var (name, c) in programs)
        {
            count++;
            var complete = MirDifferenceLedger.BindingComplete(c);
            if (!complete)
            {
                rejected.Add(name);
            }

            var program = c.BuildMir();
            foreach (var record in program.Records)
            {
                rows.Add(Row(name, complete, "B", record.Code, c.SyntaxOf(record.Subject), complete));
            }

            foreach (var finding in program.Findings)
            {
                rows.Add(Row(name, complete, "B", DiagnosticCode.InternalInvariant_Kd, c.SyntaxOf(finding.Subject), true));
            }

            foreach (var problem in program.Problems.All)
            {
                var analysis = MirDifferenceLedger.MirAnalysisOf(problem.Requirement, problem.Code) ?? "B";
                rows.Add(Row(name, complete, analysis, problem.Code, c.SyntaxOf(problem.Subject), complete || analysis is "A5" or "B"));
            }
        }

        if (Environment.GetEnvironmentVariable("KIMI_MIR_PROBLEMS") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllLines(Path.Combine(directory, $"census-{set}.tsv"), ["program\tsection\tanalysis\tcode\tsubject", .. rows.Select(static x => x.Text)], new UTF8Encoding(false));
        }

        output.WriteLine($"{set}: {count - rejected.Count} Binding-complete programs; Binding rejects {string.Join(", ", rejected.DefaultIfEmpty("none"))}, " +
            $"rows not asserted:\n{string.Join('\n', rows.Where(static x => !x.Asserted).Select(static x => x.Text).DefaultIfEmpty("none"))}");
        var asserted = rows.Where(static x => x.Asserted).Select(static x => x.Text).ToList();
        Assert.True(rejected.Count < count, $"{set}: no Binding-complete program");
        Assert.True(asserted.Count == 0, $"{set}: {asserted.Count} rows in {count} programs:\n{string.Join('\n', asserted.Take(40))}");

        static (string Text, bool Asserted) Row(string name, bool complete, string analysis, DiagnosticCode code, Koto node, bool asserted)
            => ($"{name}\t{(complete ? "complete" : "incomplete")}\t{analysis}\t{code}\t{LegacyRecords.Location(node)} {node.ToString().Split('\n')[0].Trim()}", asserted);
    }
}
