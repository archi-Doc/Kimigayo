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
    // at a matched node is new.
    [Fact]
    public void RecordsMatchOnCodeAndSubjectNode()
    {
        var c = CompilationTestHelper.Parse("func f(a: i32) -> i32 => a\n");
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var (a, b) = (nodes[^1], nodes[^2]);
        MirDifferenceLedger.Entry[] old = [new(DiagnosticCode.MovedPlace_Kd, "Ownership.PossiblyMovedUse", "A1", a), new(DiagnosticCode.MovedPlace_Kd, "Ownership.PossiblyMovedUse", "A1", a),
            new(DiagnosticCode.UninitializedPlace_Kd, "Ownership.UninitializedUse", "A1", b)];
        MirDifferenceLedger.Entry[] mir = [new(DiagnosticCode.MovedPlace_Kd, "-", "-", a), new(DiagnosticCode.UninitializedPlace_Kd, "-", "-", a)];
        var rows = MirDifferenceLedger.Match([.. old], [.. mir]);
        Assert.Equal([("both", old[0]), ("old", old[1]), ("old", old[2]), ("new", mir[1])], rows);
    }

    // R4-M17: per comparison and body, an unmatched old and new row of one code pair into one location difference with the old row's
    // facts, and the rest are missing or new; a comparison with A-owned records on one side only adds an acceptance difference, and
    // matched rows are no difference.
    [Fact]
    public void DifferencesPairByBodyAndCode()
    {
        static string[] Row(string comparison, string side, string code, string body, string form = "IdentifierNameKoto")
            => [comparison, "complete", side, side == "new" ? "-" : "A1", code, side == "new" ? "-" : "Ownership.UninitializedUse", "Program.kimi:1+1", form, body, "sha"];
        var differences = MirDifferenceLedger.Differences(
        [
            Row("1", "both", "MovedPlace_Kd", "f"),
            Row("1", "old", "MovedPlace_Kd", "f"),
            Row("1", "new", "MovedPlace_Kd", "f", "InvocationKoto"),
            Row("1", "old", "UninitializedPlace_Kd", "g"),
            Row("1", "new", "UninitializedPlace_Kd", "f"),
            Row("2", "old", "MovedPlace_Kd", "f"),
            Row("3", "new", "MovedPlace_Kd", "f"),
        ]).Select(static x => x.Signature).ToArray();
        string[] expected =
        [
            "complete\tlocation\tA1\tMovedPlace_Kd\tOwnership.UninitializedUse\tIdentifierNameKoto",
            "complete\tmissing\tA1\tUninitializedPlace_Kd\tOwnership.UninitializedUse\tIdentifierNameKoto",
            "complete\tnew\t-\tUninitializedPlace_Kd\t-\tIdentifierNameKoto",
            "complete\tacceptance\tA1\tMovedPlace_Kd\tOwnership.UninitializedUse\tIdentifierNameKoto",
            "complete\tmissing\tA1\tMovedPlace_Kd\tOwnership.UninitializedUse\tIdentifierNameKoto",
            "complete\tacceptance\t-\tMovedPlace_Kd\t-\tIdentifierNameKoto",
            "complete\tnew\t-\tMovedPlace_Kd\t-\tIdentifierNameKoto",
        ];
        Assert.Equal(expected, differences);
    }

    // R4-M4: the compared set names its owning analysis; pass-through records have none.
    [Theory]
    [InlineData("Ownership", (int)OwnershipFailure.PossiblyMovedUse, DiagnosticCode.MovedPlace_Kd, "A1")]
    [InlineData("Ownership", (int)OwnershipFailure.TransferRequired, DiagnosticCode.TransferRequired_Kd, null)]
    [InlineData("Ownership", (int)OwnershipFailure.Unsupported, DiagnosticCode.Unsupported_Kd, "U")]
    [InlineData("ControlFlow", 0, DiagnosticCode.RequireFallthrough_Kd, "A3")]
    [InlineData("ControlFlow", 0, DiagnosticCode.FunctionFallthrough_Kd, null)]
    [InlineData("Binding", (int)BindingFailure.OriginRelation, DiagnosticCode.UnsatisfiedOriginRelation_Kd, "A5")]
    [InlineData("Binding", (int)BindingFailure.IncompatibleImplementation, DiagnosticCode.IncompatibleContractImplementation_Kd, "A4")]
    [InlineData("Binding", (int)BindingFailure.Unsupported, DiagnosticCode.Unsupported_Kd, "U")]
    public void TheComparedSetNamesItsAnalysis(string partition, int kind, DiagnosticCode code, string? analysis)
        => Assert.Equal(analysis, MirDifferenceLedger.AnalysisOf(new(Enum.Parse<DiagnosticPartition>(partition), (ushort)kind), code));

    // R4-M4: every code an ownership failure reports is compared, except TransferRequired's and Internal's, and each compared
    // code names one analysis, which a MIR problem of that code takes.
    [Fact]
    public void TheComparedSetCoversEachOwnershipCode()
    {
        foreach (var (failure, code) in PipelineSwitchTest.OwnershipCodes())
        {
            var passes = failure is OwnershipFailure.TransferRequired or OwnershipFailure.Internal;
            Assert.True(passes == (MirDifferenceLedger.AnalysisOf(DiagnosticRequirement.Ownership(failure), code) is null), $"{failure} {code}");
        }

        Assert.All(MirDifferenceLedger.Compared, static x => Assert.Equal(x.Analysis, MirDifferenceLedger.AnalysisOf(x.Code)));
        Assert.Throws<InvalidOperationException>(static () => MirDifferenceLedger.AnalysisOf(DiagnosticCode.TransferRequired_Kd));
    }

    // R4-M16, R4-M46: every classification row has a unique id, known fields or *, a valid form pattern, a category a–e, a clause of SPEC
    // sections or -, and a reference; an (a), (c) or (d) row, which plan §8 lets the R4 exit pass, names its clause.
    [Fact]
    public void TheClassificationTableIsWellFormed()
    {
        var table = MirDifferenceLedger.Table;
        Assert.NotEmpty(table);
        Assert.Equal(table.Length, table.Select(static x => x[0]).Distinct(StringComparer.Ordinal).Count());
        var requirements = DiagnosticRequirements.All.Select(static x => x.Name).Append("-").ToHashSet(StringComparer.Ordinal);
        foreach (var row in table)
        {
            Assert.Equal(11, row.Length);
            Assert.Contains(row[1], (string[])["*", "complete", "incomplete"]);
            Assert.Contains(row[2], (string[])["*", "missing", "new", "location", "acceptance"]);
            Assert.Contains(row[3], (string[])["*", "A1", "A3", "A4", "A5", "U", "B", "-"]);
            Assert.True(row[4] == "*" || Enum.GetNames<DiagnosticCode>().Contains(row[4]), row[0]);
            Assert.True(row[5] == "*" || requirements.Contains(row[5]), row[0]);
            Assert.True(row[6] == "*" || Record.Exception(() => new Regex($"^(?:{row[6]})$")) is null, row[0]);
            Assert.Contains(row[7], (string[])["a", "b", "c", "d", "e"]);
            Assert.True(row[8] == "-" ? row[7] is "b" or "e" : row[8].Split(' ').All(DiagnosticCorpus.SpecSections.Contains), row[0]);
            Assert.NotEqual(string.Empty, row[9].Trim());
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
