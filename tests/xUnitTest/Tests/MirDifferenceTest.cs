// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>The MIR difference ledger (docs/dev/DECISIONS.md R4-M17): its matcher, its classification table and the requested runs. With
/// <c>KIMI_MIR_DIFF</c> and the mir pipeline, <see cref="RequestedCorpus"/> is tier 1; with <c>KIMI_MIR_LEDGER</c>,
/// <see cref="RequestedLedger"/> aggregates a directory's raw rows. Without the variables both do nothing.</summary>
public sealed class MirDifferenceTest
{
    // R4-M17: the sides match as multisets on code and subject node: a node's second old record of one code stays old, and another code
    // at a matched node is new. R4-M67: a matched MIR problem that names another requirement writes the pair's as old>mir; a MIR record
    // that names none (-) agrees.
    [Fact]
    public void RecordsMatchOnCodeAndSubjectNode()
    {
        var c = CompilationTestHelper.Parse("func f(a: i32) -> i32 => a\n");
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var (a, b) = (nodes[^1], nodes[^2]);
        var (moved, uninitialized, bound) = (DiagnosticCode.MovedPlace_Kd, DiagnosticCode.UninitializedPlace_Kd, DiagnosticCode.UnsatisfiedEffectBound_Kd);
        MirDifferenceLedger.Entry[] old =
        [
            new(moved, "Ownership.PossiblyMovedUse", "A1", a), new(moved, "Ownership.PossiblyMovedUse", "A1", a),
            new(uninitialized, "Ownership.UninitializedUse", "A1", b), new(bound, "Binding.VirtualEffectBound", "A4", b),
            new(DiagnosticCode.Unsupported_Kd, "Ownership.Unsupported", "U", b),
        ];
        MirDifferenceLedger.Entry[] mir =
        [
            new(moved, "Ownership.PossiblyMovedUse", "A1", a), new(uninitialized, "Ownership.UninitializedUse", "A1", a),
            new(bound, "Ownership.VirtualEffectBound", "A4", b), new(DiagnosticCode.Unsupported_Kd, "-", "B", b),
        ];
        var rows = MirDifferenceLedger.Match([.. old], [.. mir]);
        var renamed = old[3] with { Requirement = "Binding.VirtualEffectBound>Ownership.VirtualEffectBound" };
        Assert.Equal([("both", old[0]), ("old", old[1]), ("old", old[2]), ("both", renamed), ("both", old[4]), ("new", mir[1])], rows);
    }

    // R4-M67: a MIR problem is one entry, as MirReport.Add keeps one per code and subject, with its own requirement and its pair's
    // analysis in the compared set; MirReport's disagreement record and a pair outside the set are contract-break rows (B), never a throw.
    [Fact]
    public void MirProblemsTakeTheirPairsAnalysisAndADefectIsARow()
    {
        var c = MirBuildTest.Compile("func f(a: i32) -> i32 => a\n");
        var program = c.BuildMir();
        var subject = program.Bodies[^1].Source;
        var moved = DiagnosticRequirement.Ownership(OwnershipRequirement.PossiblyMovedUse);
        program.Problems.Add(new(DiagnosticCode.MovedPlace_Kd, moved, subject, 1));
        program.Problems.Add(new(DiagnosticCode.MovedPlace_Kd, DiagnosticRequirement.Ownership(OwnershipRequirement.UninitializedUse), subject, 2));
        program.Problems.Add(new(DiagnosticCode.UninitializedPlace_Kd, moved, subject, 1));
        var node = c.SyntaxOf(subject);
        MirDifferenceLedger.Entry[] expected =
        [
            new(DiagnosticCode.MovedPlace_Kd, "Ownership.PossiblyMovedUse", "A1", node),
            new(DiagnosticCode.InternalInvariant_Kd, "Ownership.Internal", "B", node),
            new(DiagnosticCode.UninitializedPlace_Kd, "Ownership.PossiblyMovedUse", "B", node),
        ];
        Assert.Equal(expected, MirDifferenceLedger.Sides(c, program).Mir.TakeLast(3));
    }

    // PLAN10 §7, R4-M67: the requirement each MIR recording rule names lies in the compared set's MIR column, so its problems take their
    // analysis there (MirAnalysisTest's program, one problem of each rule).
    [Fact]
    public void MirProblemsNameRequirementsOfTheComparedSet()
    {
        var problems = MirBuildTest.Compile(MirAnalysisTest.Requirements).BuildMir().Problems.All.ToArray();
        Assert.NotEmpty(problems);
        Assert.All(problems, static x => Assert.True(MirDifferenceLedger.MirAnalysisOf(x.Requirement, x.Code) is not null, $"{x.Code} {x.Requirement}"));
    }

    // R4-M17: per comparison and body, an unmatched old and new row of one code pair into one location difference with the old row's
    // facts, and the rest are missing or new; a comparison with A-owned records on one side only adds an acceptance difference, and
    // matched rows are no difference unless their requirements differ (R4-M67: a requirement difference). R5-M56: each difference names
    // its side, old for missing, mir for new, both for location and requirement, and for acceptance the side holding the records (2 and
    // 4 old, 3 mir). R4-M47: in a Binding-complete compilation a missing row is unsound unless its body keeps an unpaired MIR record (a
    // code choice); a matched record does not exempt it, and no other difference is unsound.
    [Fact]
    public void DifferencesPairByBodyAndCode()
    {
        static string[] Row(string comparison, string side, string code, string body, string form = "IdentifierNameKoto", string section = "complete")
            => [comparison, section, side, side == "new" ? "-" : "A1", code, side == "new" ? "-" : "Ownership.UninitializedUse", "Program.kimi:1+1", form, body, "sha"];
        var differences = MirDifferenceLedger.Differences(
        [
            Row("1", "both", "MovedPlace_Kd", "f"),
            Row("1", "old", "MovedPlace_Kd", "f"),
            Row("1", "new", "MovedPlace_Kd", "f", "InvocationKoto"),
            Row("1", "old", "UninitializedPlace_Kd", "g"),
            Row("1", "new", "UninitializedPlace_Kd", "f"),
            Row("2", "old", "MovedPlace_Kd", "f"),
            Row("3", "new", "MovedPlace_Kd", "f"),
            Row("4", "old", "MovedPlace_Kd", "f", section: "incomplete"),
            Row("5", "old", "MovedPlace_Kd", "f"),
            Row("5", "new", "UninitializedPlace_Kd", "f"),
            Row("6", "both", "MovedPlace_Kd", "f"),
            Row("6", "old", "UninitializedPlace_Kd", "f"),
            ["7", "complete", "both", "A1", "MovedPlace_Kd", "Binding.VirtualEffectBound>Ownership.VirtualEffectBound", "Program.kimi:1+1",
                "IdentifierNameKoto", "f", "sha"],
        ]).Select(static x => (x.Signature, x.Unsound)).ToArray();
        (string, bool)[] expected =
        [
            ("complete\tlocation\tboth\tA1\tMovedPlace_Kd\tOwnership.UninitializedUse\tIdentifierNameKoto", false),
            ("complete\tmissing\told\tA1\tUninitializedPlace_Kd\tOwnership.UninitializedUse\tIdentifierNameKoto", true),
            ("complete\tnew\tmir\t-\tUninitializedPlace_Kd\t-\tIdentifierNameKoto", false),
            ("complete\tacceptance\told\tA1\tMovedPlace_Kd\tOwnership.UninitializedUse\tIdentifierNameKoto", false),
            ("complete\tmissing\told\tA1\tMovedPlace_Kd\tOwnership.UninitializedUse\tIdentifierNameKoto", true),
            ("complete\tacceptance\tmir\t-\tMovedPlace_Kd\t-\tIdentifierNameKoto", false),
            ("complete\tnew\tmir\t-\tMovedPlace_Kd\t-\tIdentifierNameKoto", false),
            ("incomplete\tacceptance\told\tA1\tMovedPlace_Kd\tOwnership.UninitializedUse\tIdentifierNameKoto", false),
            ("incomplete\tmissing\told\tA1\tMovedPlace_Kd\tOwnership.UninitializedUse\tIdentifierNameKoto", false),
            ("complete\tmissing\told\tA1\tMovedPlace_Kd\tOwnership.UninitializedUse\tIdentifierNameKoto", false),
            ("complete\tnew\tmir\t-\tUninitializedPlace_Kd\t-\tIdentifierNameKoto", false),
            ("complete\tmissing\told\tA1\tUninitializedPlace_Kd\tOwnership.UninitializedUse\tIdentifierNameKoto", true),
            ("complete\trequirement\tboth\tA1\tMovedPlace_Kd\tBinding.VirtualEffectBound>Ownership.VirtualEffectBound\tIdentifierNameKoto", false),
        ];
        Assert.Equal(expected, differences);
    }

    // R4-M47 (plan §8): a difference blocks the R4 exit when it is unsound, lies in a library body, or lies in a Binding-complete program
    // that holds a milestone's whole text; it stays open unless its signature is classified (a), (c) or (d). The library row's new
    // signature, on side mir and naming its requirement (R4-M67, R5-M56), is the tracked (d) row a1-c3.
    [Fact]
    public void BlockingRowsAreUnsoundOrInTheLibraryOrAMilestone()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "kimi-mir-ledger-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "sources"));
            File.WriteAllText(Path.Combine(directory, "sources", "milestone.kimi"), "// Program.kimi\n" + DiagnosticCorpus.Milestone(1) + "\n");
            File.WriteAllText(Path.Combine(directory, "sources", "other.kimi"), "// Program.kimi\nlet x = 1\n");
            static string Row(string comparison, string side, string location, string sha, string form = "UnitLiteralKoto", string section = "complete")
                => $"{comparison}\t{section}\t{side}\tA1\tUninitializedPlace_Kd\tOwnership.UninitializedUse\t{location}\t{form}\tProgram.kimi:0\t{sha}";
            File.WriteAllLines(
                Path.Combine(directory, "raw-1.tsv"),
                [
                    Row("1", "old", "Program.kimi:1+1", "other"),
                    Row("2", "new", "Program.kimi:1+1", "milestone"),
                    Row("3", "new", "Kimi/Array.kimi:1+1", "other", "IdentifierNameKoto"),
                    Row("4", "new", "Program.kimi:1+1", "other"),
                    Row("5", "new", "Program.kimi:1+1", "milestone", section: "incomplete"),
                ]);
            MirDifferenceLedger.Aggregate(directory);
            var blocking = File.ReadAllLines(Path.Combine(directory, "ledger.tsv")).Skip(1).Select(static x => x.Split('\t'))
                .ToDictionary(static x => $"{x[0]} {x[1]} {x[2]} {x[5]} {x[6]}", static x => (x[7], x[8]));
            var expected = new Dictionary<string, (string, string)>
            {
                ["complete missing old Ownership.UninitializedUse UnitLiteralKoto"] = ("1", "1"),
                ["complete acceptance old Ownership.UninitializedUse UnitLiteralKoto"] = ("1", "0"),
                ["complete acceptance mir Ownership.UninitializedUse UnitLiteralKoto"] = ("2", "1"),
                ["complete new mir Ownership.UninitializedUse UnitLiteralKoto"] = ("2", "1"),
                ["complete new mir Ownership.UninitializedUse IdentifierNameKoto"] = ("1", "1"),
                ["complete acceptance mir Ownership.UninitializedUse IdentifierNameKoto"] = ("1", "1"),
                ["incomplete new mir Ownership.UninitializedUse UnitLiteralKoto"] = ("1", "0"),
                ["incomplete acceptance mir Ownership.UninitializedUse UnitLiteralKoto"] = ("1", "0"),
            };
            Assert.Equal(expected, blocking);
            Assert.Contains("- Open blocking rows: 4 of 5 (an old rejection the MIR lacks 1, else a library body 1, else a milestone 2)\n", File.ReadAllText(Path.Combine(directory, "summary.md")));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    // R4-M4: the compared set names its owning analysis; pass-through records have none.
    [Theory]
    [InlineData("Ownership", (int)OwnershipRequirement.PossiblyMovedUse, DiagnosticCode.MovedPlace_Kd, "A1")]
    [InlineData("Ownership", (int)OwnershipRequirement.TransferRequired, DiagnosticCode.TransferRequired_Kd, null)]
    [InlineData("Ownership", (int)OwnershipRequirement.Unsupported, DiagnosticCode.Unsupported_Kd, "U")]
    [InlineData("ControlFlow", 0, DiagnosticCode.RequireFallthrough_Kd, "A3")]
    [InlineData("Binding", (int)BindingFailure.None, DiagnosticCode.FunctionFallthrough_Kd, null)]
    [InlineData("Binding", (int)BindingFailure.OriginRelation, DiagnosticCode.UnsatisfiedOriginRelation_Kd, "A5")]
    [InlineData("Binding", (int)BindingFailure.IncompatibleImplementation, DiagnosticCode.IncompatibleContractImplementation_Kd, "A4")]
    [InlineData("Binding", (int)BindingFailure.Unsupported, DiagnosticCode.Unsupported_Kd, "U")]
    public void TheComparedSetNamesItsAnalysis(string partition, int kind, DiagnosticCode code, string? analysis)
        => Assert.Equal(analysis, MirDifferenceLedger.AnalysisOf(new(Enum.Parse<DiagnosticPartition>(partition), (ushort)kind), code));

    // R4-M4: every code an ownership failure reports is compared, except TransferRequired's and Internal's. R4-M67: each MIR pair of the
    // compared set names its row's analysis, and PLAN10 §7's consistency holds: an Ownership requirement with a code the old analysis
    // gives that failure (OwnershipIssue.Code), or ControlFlow with the require fallthrough; a pair outside it, such as MirReport's
    // disagreement record, has none.
    [Fact]
    public void TheComparedSetCoversEachOwnershipCode()
    {
        var images = new HashSet<(DiagnosticRequirement, DiagnosticCode)>();
        foreach (var (failure, code) in PipelineSwitchTest.OwnershipCodes())
        {
            var passes = failure is OwnershipRequirement.TransferRequired or OwnershipRequirement.Internal;
            Assert.True(passes == (MirDifferenceLedger.AnalysisOf(DiagnosticRequirement.Ownership(failure), code) is null), $"{failure} {code}");
            images.Add((DiagnosticRequirement.Ownership(failure), code));
        }

        images.Add((DiagnosticRequirement.ControlFlow, DiagnosticCode.RequireFallthrough_Kd));
        foreach (var (_, code, analysis, mir) in MirDifferenceLedger.Compared)
        {
            if (mir is { } requirement)
            {
                Assert.True(images.Contains((requirement, code)), $"{requirement.Name} {code}");
                Assert.Equal(analysis, MirDifferenceLedger.MirAnalysisOf(requirement, code));
            }
        }

        var (transfer, defect) = (OwnershipRequirement.TransferRequired, OwnershipRequirement.Internal);
        Assert.Null(MirDifferenceLedger.MirAnalysisOf(DiagnosticRequirement.Ownership(transfer), DiagnosticCode.TransferRequired_Kd));
        Assert.Null(MirDifferenceLedger.MirAnalysisOf(DiagnosticRequirement.Ownership(defect), DiagnosticCode.InternalInvariant_Kd));
    }

    // R5-M56: a classification row matches a signature's side by equality or *; a row of side mir does not match a both signature.
    [Theory]
    [InlineData("mir", "mir", true)]
    [InlineData("mir", "both", false)]
    [InlineData("old", "both", false)]
    [InlineData("both", "both", true)]
    [InlineData("*", "old", true)]
    public void ARowMatchesItsSideOrStar(string row, string signature, bool matches)
        => Assert.Equal(matches, MirDifferenceLedger.Matches(
            ["id", "complete", "*", row, "A5", "*", "*", "*", "b", "-", "ref", "note"],
            $"complete\tlocation\t{signature}\tA5\tComparisonLoanConflict_Kd\tOwnership.ComparisonLoanConflict\tIdentifierNameKoto"));

    // R4-M16, R4-M46: every classification row has a unique id, known fields or * (a requirement difference's old>mir; R5-M56's side),
    // a valid form pattern, a category a–e, a clause of SPEC sections or -, and a reference; an (a), (c) or (d) row, which plan §8 lets
    // the R4 exit pass, names its clause.
    [Fact]
    public void TheClassificationTableIsWellFormed()
    {
        var table = MirDifferenceLedger.Table;
        Assert.NotEmpty(table);
        Assert.Equal(table.Length, table.Select(static x => x[0]).Distinct(StringComparer.Ordinal).Count());
        var requirements = DiagnosticRequirements.All.Select(static x => x.Name).Append("-").ToHashSet(StringComparer.Ordinal);
        foreach (var row in table)
        {
            Assert.Equal(12, row.Length);
            Assert.Contains(row[1], (string[])["*", "complete", "incomplete"]);
            Assert.Contains(row[2], (string[])["*", "missing", "new", "location", "acceptance", "requirement"]);
            Assert.Contains(row[3], (string[])["*", "old", "mir", "both"]);
            Assert.Contains(row[4], (string[])["*", "A1", "A3", "A4", "A5", "U", "B", "-"]);
            Assert.True(row[5] == "*" || Enum.GetNames<DiagnosticCode>().Contains(row[5]), row[0]);
            Assert.True(row[6] == "*" || row[6].Split('>').All(requirements.Contains), row[0]);
            Assert.True(row[7] == "*" || Record.Exception(() => new Regex($"^(?:{row[7]})$")) is null, row[0]);
            Assert.Contains(row[8], (string[])["a", "b", "c", "d", "e"]);
            Assert.True(row[9] == "-" ? row[8] is "b" or "e" : row[9].Split(' ').All(DiagnosticCorpus.SpecSections.Contains), row[0]);
            Assert.NotEqual(string.Empty, row[10].Trim());
        }
    }

    // Tier 1 (R4-M17): with KIMI_MIR_DIFF under the mir pipeline, checks the diagnostic corpus and every kimi block of the specification
    // through the shared check entry, which the ledger observes.
    [Fact]
    public void RequestedCorpus()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KIMI_MIR_DIFF")) || Compilation.DefaultPipeline != CompilerPipeline.Mir)
        {
            return;
        }

        string[] names = [.. DiagnosticSnapshotTest.CaseNames(), .. DiagnosticCorpus.SpecBlocks.Select(static x => "spec/" + x.Name)];
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "kimi-mir-diff-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            Parallel.For(0, names.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, i => DiagnosticSnapshotTest.Take(names[i], directory));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    // With KIMI_MIR_LEDGER naming a directory of raw rows, writes its ledger.tsv and summary.md.
    [Fact]
    public void RequestedLedger()
    {
        if (Environment.GetEnvironmentVariable("KIMI_MIR_LEDGER") is { Length: > 0 } directory)
        {
            MirDifferenceLedger.Aggregate(directory);
        }
    }
}
