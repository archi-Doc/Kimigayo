// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>The MIR census (docs/dev/DECISIONS.md R4-M12, R4-M74 OQ1; A5 step 2 plan PD22) over four sets, each loaded as its own tests
/// load it: the Kimi library, the milestone programs, the <c>tests/mir</c> goldens and the variants of
/// <c>tests/diagnostics/spec-cases.json</c> the SPEC accepts. In any program (R4-M12): no validator finding, which would leave it
/// unanalyzed, no problem outside the census pairs (<see cref="Pairs"/>, a recording rule's defect, B) and no A5 problem (the analysis
/// the pairs give a problem's requirement and code, R4-M4, R4-M67: A1's storage limit shares A5's pair and counts, A5's located
/// Unsupported_Kd shares A4's and does not). In a Binding-complete program (R4-M47): no construction record, whose Opaque value would leave it partly
/// analyzed, and no problem of any MIR analysis (A1–A5, merged by A6's report). A program Binding rejects may have other records and
/// problems that failure causes, whose report SPEC 23.1.2 leaves implementation-defined: the test output lists them, not asserted. With
/// <c>KIMI_MIR_PROBLEMS</c> naming a directory, each set writes <c>census-&lt;set&gt;.tsv</c> there: per record, finding or problem
/// its program, the program's section (complete or incomplete, <see cref="BindingComplete"/>), analysis (<see cref="MirAnalysisOf"/>; B
/// for a record, a finding or a defect), code and subject. The class stays in the normal suite while its tests take under 60 s together in a Session's results.</summary>
public sealed class MirCensusTest(ITestOutputHelper output)
{
    // R4-M4, R4-M67 (R6-check H10a D6): the requirement and code of each MIR problem a recording rule makes, with the analysis that owns
    // it; the MIR names the Ownership requirement of each decision the old path reported under Binding (DIAGNOSTICS rule 1). They are the
    // consistency set of PLAN10 §7: a MIR problem outside it is a recording rule's defect (B), as is MirReport.Add's disagreement record
    // (Ownership.Internal).
    private static readonly (DiagnosticRequirement Requirement, DiagnosticCode Code, string Analysis)[] Pairs =
    [
        (Ownership(OwnershipRequirement.UninitializedUse), DiagnosticCode.UninitializedPlace_Kd, "A1"),
        (Ownership(OwnershipRequirement.PossiblyMovedUse), DiagnosticCode.MovedPlace_Kd, "A1"),
        (Ownership(OwnershipRequirement.ReassignedLet), DiagnosticCode.ReassignedLet_Kd, "A1"),
        (Ownership(OwnershipRequirement.StaticMovePathRequired), DiagnosticCode.StaticMovePathRequired_Kd, "A1"),
        (Ownership(OwnershipRequirement.DefaultArgumentMove), DiagnosticCode.DefaultArgumentMove_Kd, "A1"),
        (DiagnosticRequirement.ControlFlow, DiagnosticCode.RequireFallthrough_Kd, "A3"),
        (Ownership(OwnershipRequirement.EffectBound), DiagnosticCode.IncompatibleContractImplementation_Kd, "A4"),
        (Ownership(OwnershipRequirement.CallableEffectBound), DiagnosticCode.UnsatisfiedEffectBound_Kd, "A4"),
        (Ownership(OwnershipRequirement.VirtualEffectBound), DiagnosticCode.UnsatisfiedEffectBound_Kd, "A4"),
        (Ownership(OwnershipRequirement.ComparisonLoanConflict), DiagnosticCode.ComparisonLoanConflict_Kd, "A5"),
        (Ownership(OwnershipRequirement.ComparisonLoanConflict), DiagnosticCode.CallReservationConflict_Kd, "A5"),
        (Ownership(OwnershipRequirement.ComparisonLoanConflict), DiagnosticCode.CallActivationConflict_Kd, "A5"),
        (Ownership(OwnershipRequirement.ComparisonLoanConflict), DiagnosticCode.PlacementActivationConflict_Kd, "A5"),
        (Ownership(OwnershipRequirement.DefaultArgumentAccess), DiagnosticCode.DefaultArgumentAccess_Kd, "A5"),
        (Ownership(OwnershipRequirement.DefaultArgumentBorrow), DiagnosticCode.DefaultArgumentBorrow_Kd, "A5"),
        (Ownership(OwnershipRequirement.UnprovenOrigin), DiagnosticCode.UnsatisfiedOriginRelation_Kd, "A5"),
        (Ownership(OwnershipRequirement.UnprovenOrigin), DiagnosticCode.UnprovenOriginRelation_Kd, "A5"),
        (Ownership(OwnershipRequirement.StorageLimit), DiagnosticCode.OwnershipStorageLimit_Kd, "A5"),
        (Ownership(OwnershipRequirement.OwnStepDeparture), DiagnosticCode.OwnStepDeparture_Kd, "A5"),
        (Ownership(OwnershipRequirement.Unsupported), DiagnosticCode.Unsupported_Kd, "U"),
        (Ownership(OwnershipRequirement.DepthLimit), DiagnosticCode.OwnershipDepthLimit_Kd, "U"),
    ];

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

    // Nor has a SPEC example variant without an Error record on MIR (its MIR expectation, as VerifiedSpecExampleTest reads it), as the
    // milestones.
    [Fact]
    public void AcceptedSpecVariantsHaveNoProblems()
        => this.Census("spec", DiagnosticCorpus.SpecCases.SelectMany(static x => x.Variants.Select((y, i) => (Name: $"{x.Name}/{i}", Example: x, Variant: y)))
            .Where(static x => (x.Variant.Mir?.Records ?? x.Variant.Expected).Length == 0)
            .Select(static x => (x.Name, VerifiedSpecExampleTest.Bind(DiagnosticCorpus.Source(x.Example, x.Variant)))));

    // PLAN10 §7, R4-M67: the requirement each MIR recording rule names lies in the census pairs, so its problems take their analysis
    // there (MirAnalysisTest's program, one problem of each rule).
    [Fact]
    public void MirProblemsNameRequirementsOfTheCensusPairs()
    {
        var problems = MirBuildTest.Compile(MirAnalysisTest.Requirements).BuildMir().Problems.All.ToArray().Where(static x => !x.IsRecord).ToArray();
        Assert.NotEmpty(problems);
        Assert.All(problems, static x => Assert.True(MirAnalysisOf(x.Requirement, x.Code) is not null, $"{x.Code} {x.Requirement}"));
    }

    // R4-M67: a MIR problem is one entry, as MirReport.Add keeps one per code, subject and body, with its own requirement and its pair's
    // analysis; MirReport's disagreement record and a pair outside the census pairs get none, a defect row (B), never a throw.
    [Fact]
    public void MirProblemsTakeTheirPairsAnalysisAndADefectIsARow()
    {
        var c = MirBuildTest.Compile("func f(a: i32) -> i32 => a\n");
        var program = c.BuildMir();
        var (subject, body) = (program.Bodies[^1].Source, program.Bodies[^1].Ordinal);
        var moved = DiagnosticRequirement.Ownership(OwnershipRequirement.PossiblyMovedUse);
        program.Problems.Add(new(DiagnosticCode.MovedPlace_Kd, moved, subject, body, 1));
        program.Problems.Add(new(DiagnosticCode.MovedPlace_Kd, DiagnosticRequirement.Ownership(OwnershipRequirement.UninitializedUse), subject, body, 2));
        program.Problems.Add(new(DiagnosticCode.UninitializedPlace_Kd, moved, subject, body, 1));
        var node = c.SyntaxOf(subject);
        (DiagnosticCode, string, string, Koto)[] expected =
        [
            (DiagnosticCode.MovedPlace_Kd, "Ownership.PossiblyMovedUse", "A1", node),
            (DiagnosticCode.InternalInvariant_Kd, "Ownership.Internal", "B", node),
            (DiagnosticCode.UninitializedPlace_Kd, "Ownership.PossiblyMovedUse", "B", node),
        ];
        Assert.Equal(expected, program.Problems.All.ToArray().TakeLast(3).Select(x => (x.Code, x.Requirement.Name, MirAnalysisOf(x.Requirement, x.Code) ?? "B", c.SyntaxOf(x.Subject))));
    }

    // R4-M67: each census pair names one analysis, and pairs outside them, such as a Binding failure's or MirReport's disagreement
    // record, have none.
    [Fact]
    public void EachCensusPairNamesOneAnalysis()
    {
        Assert.Equal(Pairs.Length, Pairs.Select(static x => (x.Requirement, x.Code)).Distinct().Count());
        Assert.Null(MirAnalysisOf(DiagnosticRequirement.Binding(BindingFailure.TransferRequired), DiagnosticCode.TransferRequired_Kd));
        Assert.Null(MirAnalysisOf(DiagnosticRequirement.Ownership(OwnershipRequirement.Internal), DiagnosticCode.InternalInvariant_Kd));
    }

    // Tests read a requirement the census pairs map to several codes through one helper each (CompilationTestHelper): its codes are
    // exactly that requirement's codes here, four of a Comparison Loan conflict and two of an unproven Origin (DECISIONS BU-85: an
    // unjudged non-relation is Internal).
    [Fact]
    public void TheMultiCodeHelpersAreTheCensusImages()
    {
        var (conflicts, origins) = (Images(OwnershipRequirement.ComparisonLoanConflict), Images(OwnershipRequirement.UnprovenOrigin));
        Assert.Equal((4, 2), (conflicts.Length, origins.Length));
        Assert.All(Enum.GetValues<DiagnosticCode>(), x => Assert.Equal((conflicts.Contains(x), origins.Contains(x)), (x.IsLoanConflict(), x.IsUnprovenOrigin())));

        static DiagnosticCode[] Images(OwnershipRequirement requirement)
            => [.. Pairs.Where(x => x.Requirement == Ownership(requirement)).Select(static x => x.Code)];
    }

    /// <summary>Gets whether a compilation is Binding-complete (R4-M47): Binding accepted its program, a complete result and no published
    /// Error in Binding's partition (its lints included), which leaves it complete (R5-M31).</summary>
    /// <param name="compilation">The bound compilation.</param>
    /// <returns>Whether it is Binding-complete.</returns>
    internal static bool BindingComplete(Compilation compilation)
    {
        compilation.Binding.ReportDiagnostics();
        return compilation.Binding.Result.IsComplete && !compilation.Diagnostics.HasErrorsIn(DiagnosticPartition.Binding);
    }

    /// <summary>Gets the analysis that owns a MIR problem: its pair's in <see cref="Pairs"/> (R4-M67), where each pair names one analysis;
    /// null outside it, a recording rule's defect.</summary>
    /// <param name="requirement">The requirement the problem names.</param>
    /// <param name="code">The problem's code.</param>
    /// <returns>The analysis, or null.</returns>
    internal static string? MirAnalysisOf(DiagnosticRequirement requirement, DiagnosticCode code)
        => Array.Find(Pairs, x => x.Requirement == requirement && x.Code == code).Analysis;

    // The selected cases of the semantic snapshot corpus, bound as their snapshots are.
    private static IEnumerable<(string Name, Compilation Compilation)> Corpus(Func<string, bool> selected)
        => SemanticSnapshotTest.CaseNames().Where(selected).Select(static x => (x, SemanticSnapshotTest.Bind(x).Compilation));

    private static DiagnosticRequirement Ownership(OwnershipRequirement requirement) => DiagnosticRequirement.Ownership(requirement);

    // Builds and analyzes each program as it comes, writes the set's rows when asked, lists the programs Binding rejects with their rows
    // not asserted, and requires a Binding-complete program and no asserted row: a finding, a defect or an A5 problem in any program,
    // any record or problem in a Binding-complete one.
    private void Census(string set, IEnumerable<(string Name, Compilation Compilation)> programs)
    {
        var (rows, rejected, count) = (new List<(string Text, bool Asserted)>(), new List<string>(), 0);
        foreach (var (name, c) in programs)
        {
            count++;
            var complete = BindingComplete(c);
            if (!complete)
            {
                rejected.Add(name);
            }

            var program = c.BuildMir();
            foreach (var record in program.Records(MirRecordClass.Construction))
            {
                rows.Add(Row(name, complete, "B", record.Code, c.SyntaxOf(record.Subject), complete));
            }

            foreach (var finding in program.Findings)
            {
                rows.Add(Row(name, complete, "B", DiagnosticCode.InternalInvariant_Kd, c.SyntaxOf(finding.Subject), true));
            }

            foreach (var problem in program.Problems.All)
            {
                if (problem.IsRecord)
                {
                    continue; // Its construction records are above; its collection records are no census row.
                }

                var analysis = MirAnalysisOf(problem.Requirement, problem.Code) ?? "B";
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
            => ($"{name}\t{(complete ? "complete" : "incomplete")}\t{analysis}\t{code}\t{CompilationTestHelper.Location(node)} {node.ToString().Split('\n')[0].Trim()}", asserted);
    }
}
