// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Verification;
using Xunit;

namespace XunitTest;

public class OwnershipStorageLimitTest(ITestOutputHelper output)
{
    [Fact]
    public void ConcreteInstanceLimitsRemainResourceFailuresAndRecover()
    {
        var c = MinimalEmissionTest.Analyze("func identity<T>(value: T) -> T => value@move\nidentity((20, 22))");
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var initial), initial);
        var previous = OwnershipStorage.ByteLimit;
        try
        {
            // Reuse the fully prepared instance; retained arrays must not bypass a later request's limit.
            OwnershipStorage.ByteLimit = 8;
            using var writer = new StringWriter();
            Assert.False(c.Emission.WriteIr(writer, out var failure));
            Assert.Empty(writer.ToString());
            Assert.True(c.Emission.FailureIsResourceLimit, failure);
            Assert.Contains("ownership", failure!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("8", failure, StringComparison.Ordinal);
            Assert.True(c.Ownership.Result.IsVerified);
            Assert.Empty(c.Ownership.Issues);
        }
        finally
        {
            OwnershipStorage.ByteLimit = previous;
        }

        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var recovered), recovered);
        Assert.False(c.Emission.FailureIsResourceLimit);
        Assert.Empty(c.Ownership.Issues);
    }

    [Fact]
    public void OversizedProductsReportResourcesBeforeAllocationOrOverflow()
    {
        var failure = Assert.Throws<OwnershipStorageLimitException>(() => OwnershipStorage.Cells(65536, 65536, 2, "borrow dependencies"));
        Assert.Equal(1L << 30, failure.RequiredBytes);
        Assert.Equal(OwnershipStorage.DefaultByteLimit, failure.LimitBytes);
        Assert.Equal("borrow dependencies", failure.Table);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LimitsPreservePublicEvidenceAndIndependentErrors(bool independent)
    {
        var source = VerificationWorkloads.InspectionLoans(32, true) +
            (independent ? "\nfunc invalid()\n    let value = 1\n    let moved = value@move\n    let invalid = value" : string.Empty);
        var path = Path.GetFullPath("ownership-storage.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var previous = OwnershipStorage.ByteLimit;
        try
        {
            // Repeat after full-size preparation: retained capacity must never bypass the logical limit.
            OwnershipStorage.ByteLimit = 4096;
            Assert.False(c.Ownership.Analyze().IsVerified);
            var issue = Assert.Single(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.StorageLimit);
            Assert.True(issue.RequiredBytes > 4096);
            Assert.Equal(4096, issue.LimitBytes);
            Assert.Contains("func check()", issue.Source.ToString(), StringComparison.Ordinal);
            Assert.False(c.Emission.Validate(out _));
            c.Ownership.ReportDiagnostics();
            c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
            var result = c.Diagnostics.Finalize();
            var error = Assert.Single(result.Diagnostics, static x => x.Code == "OwnershipStorageLimit_Kd");
            Assert.Equal(DiagnosticSeverity.Error, error.Severity);
            Assert.Equal(DiagnosticCategory.Resource, error.Category);
            Assert.Contains(error.Reason!, static x => x.Name == "limitBytes" && x.Value == "4096");
            Assert.Contains(error.Reason!, x => x.Name == "requiredBytes" && x.Value == issue.RequiredBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.Contains(error.Reason!, x => x.Name == "table" && x.Value == issue.StorageTable);
            Assert.Equal("check()", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
            Assert.DoesNotContain(result.Diagnostics, static x => x.Category == DiagnosticCategory.Internal);
            Assert.Equal(independent, result.Diagnostics.Any(static x => x.Code == "MovedPlace_Kd"));

            var console = new DiagnosticContractTest.DiagnosticConsole();
            new Kimigayo(console).Render(result, string.Empty);
            Assert.Contains("4096", console.Text, StringComparison.Ordinal);
            output.WriteLine(console.Text);
            var identity = SourceIdentity.FromPath(path);
            var sent = WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, true)[identity];
            Assert.Contains(sent, x => x.Range == error.Display!.Range && x.Message.Contains("4096", StringComparison.Ordinal));

            Assert.False(c.Ownership.Analyze().IsVerified);
            Assert.Equal(issue, Assert.Single(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.StorageLimit));
        }
        finally
        {
            OwnershipStorage.ByteLimit = previous;
        }

        Assert.Equal(!independent, c.Ownership.Analyze().IsVerified);
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.StorageLimit);
    }
}
