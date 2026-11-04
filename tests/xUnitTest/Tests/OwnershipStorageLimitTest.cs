// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Verification;
using Xunit;

namespace XunitTest;

public class OwnershipStorageLimitTest
{
    [Fact]
    public void OversizedProductsReportResourcesBeforeAllocationOrOverflow()
    {
        var failure = Assert.Throws<OwnershipStorageLimitException>(() => OwnershipStorage.Cells(65536, 65536, 2, "borrow dependencies"));
        Assert.Equal(1L << 30, failure.RequiredBytes);
        Assert.Equal(OwnershipStorage.DefaultByteLimit, failure.LimitBytes);
        Assert.Equal("borrow dependencies", failure.Table);
    }

    // R6-check H10a D10 (restored coverage; T-04 N3): the MIR's own arena limit publishes end to end. MirAnalysisTest's A5 program, 2,000
    // reference locals live across 2,000 branches, fits A1's arena but not A5's Loans table, so A5 records one OwnershipStorageLimit_Kd at
    // `func f` with its arguments and stops there (its Loan conflict at `x = 2` is not judged); an independent error elsewhere publishes
    // beside it, and the program is refused and generates nothing (R4-M15a, R4-M132, R5-M185).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheMirsArenaLimitPublishesBesideIndependentErrors(bool independent)
    {
        var text = new StringBuilder("func inspect(value: ref/i32) => ()\nfunc pair<s/T>(p: s/T) -> ()\n    s is owner or uniq\n    let v = p@move\n")
            .Append("func f(flag: bool) -> i32\n    var x = 1\n    var y = 2\n    let r = x@ref\n");
        foreach (var part in new[] { "    var r{0} = x@ref\n", "    if flag => r{0} = y@ref\n", "    inspect(r{0})\n" })
        {
            for (var n = 0; n < 2_000; n++)
            {
                text.AppendFormat(CultureInfo.InvariantCulture, part, n);
            }
        }

        text.Append("    x = 2\n    inspect(r)\n    return x\n")
            .Append(independent ? "func invalid()\n    let value = 1\n    let moved = value@move\n    let invalid = value\n" : string.Empty);
        var source = text.Append("public func main() => ()\n").ToString();
        var path = Path.GetFullPath("ownership-storage-mir.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.False(c.Accepts);
        using var module = new StringWriter();
        Assert.False(c.Generate(module));
        Assert.Empty(module.ToString());

        c.ReportAnalysis();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var limit = (nameof(DiagnosticCode.OwnershipStorageLimit_Kd), source.IndexOf("f(flag: bool)", StringComparison.Ordinal)); // At its name.
        var moved = (nameof(DiagnosticCode.MovedPlace_Kd), source.LastIndexOf("value", StringComparison.Ordinal));
        Assert.Equal(independent ? [moved, limit] : [limit], result.Diagnostics.Select(static x => (x.Code, x.Span!.Value.Start)).Order());
        var reason = Assert.Single(result.Diagnostics, static x => x.Code == nameof(DiagnosticCode.OwnershipStorageLimit_Kd)).Reason!;
        Assert.Equal(["requiredBytes", "limitBytes", "table"], reason.Select(static x => x.Name));
        Assert.Equal(("67108864", "Loans"), (reason[1].Value, reason[2].Value)); // 64 MiB, MirDataflow's ByteLimit.
        Assert.True(long.Parse(reason[0].Value!, CultureInfo.InvariantCulture) > 64L * 1024 * 1024, reason[0].Value);
    }

    // R6-check H5 (DECISIONS R5-M185, H0b-3): the MIR decides acceptance, so OwnershipStorage's logical limit is the old analysis's alone,
    // read through its legacy entry until H10b, and publishes nothing; the MIR's arenas have their own limit
    // (TheMirsArenaLimitPublishesBesideIndependentErrors). The published records are the MIR's: an independent error stays at its use, and
    // generation follows the verdict.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheOldLimitPublishesNothingBesideIndependentErrors(bool independent)
    {
        var source = VerificationWorkloads.InspectionLoans(32, true) +
            (independent ? "\nfunc invalid()\n    let value = 1\n    let moved = value@move\n    let invalid = value" : string.Empty);
        var path = Path.GetFullPath("ownership-storage.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        c.AnalyzeLegacyOwnership(); // The old analysis's full-size preparation.
        var previous = OwnershipStorage.ByteLimit;
        try
        {
            // Repeat after full-size preparation: retained capacity must never bypass the logical limit. The legacy entry runs once per
            // version, so each repeat invalidates it first.
            OwnershipStorage.ByteLimit = 4096;
            c.InvalidateOwnership();
            Assert.Equal(!independent, c.Analyze().IsVerified);
            var issue = Assert.Single(c.AnalyzeLegacyOwnership().Issues, static x => x.Failure == OwnershipRequirement.StorageLimit);
            Assert.True(issue.RequiredBytes > 4096);
            Assert.Equal(4096, issue.LimitBytes);
            Assert.Contains("func check()", issue.Source.ToString(), StringComparison.Ordinal);

            using var module = new StringWriter();
            Assert.Equal(!independent, c.Generate(module));
            Assert.Equal(independent, module.ToString().Length == 0);
            c.ReportAnalysis();
            c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
            var result = c.Diagnostics.Finalize();
            Assert.DoesNotContain(result.Diagnostics, static x => x.Code == "OwnershipStorageLimit_Kd");
            Assert.DoesNotContain(result.Diagnostics, static x => x.Category == DiagnosticCategory.Internal);
            var moved = (nameof(DiagnosticCode.MovedPlace_Kd), source.LastIndexOf("value", StringComparison.Ordinal), "value".Length);
            Assert.Equal(independent ? [moved] : [], result.Diagnostics.Select(static x => (x.Code, x.Span!.Value.Start, x.Span.Value.Length)));

            c.InvalidateOwnership();
            Assert.Equal(!independent, c.Analyze().IsVerified);
            Assert.Equal(issue, Assert.Single(c.AnalyzeLegacyOwnership().Issues, static x => x.Failure == OwnershipRequirement.StorageLimit));
        }
        finally
        {
            OwnershipStorage.ByteLimit = previous;
        }

        Assert.Equal(!independent, c.Analyze().IsVerified);
        Assert.DoesNotContain(c.Problems, static x => x.Code == DiagnosticCode.OwnershipStorageLimit_Kd);
    }
}
