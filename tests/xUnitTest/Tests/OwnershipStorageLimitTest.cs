// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

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

    // R6-check H5 (DECISIONS R5-M185, H0b-3): the MIR decides acceptance, so OwnershipStorage's logical limit is the old analysis's alone,
    // read through its legacy entry until H10b, and publishes nothing; the MIR's arenas have their own limit (R4-M15a, R4-M132;
    // MirPublicationTest). The published records are the MIR's: an independent error stays at its use, and generation follows the verdict.
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
        var previous = OwnershipStorage.ByteLimit;
        try
        {
            // Repeat after full-size preparation: retained capacity must never bypass the logical limit.
            OwnershipStorage.ByteLimit = 4096;
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
