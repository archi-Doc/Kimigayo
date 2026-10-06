// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>Written holes are inference requests, not unresolved identifier names.</summary>
public class ArrayAnnotationDiagnosticTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("let values: [_ of _] = []", "_", "no element Type", "[]")]
    [InlineData("let values: [_ of [_ of i32]] = []", "_", "no fixed-array length", "[]")]
    [InlineData("let values: [_ of [_ of _]] = [[1], [2, 3]]", "[2, 3]", "length 2 but earlier initializer evidence established 1", "_")]
    [InlineData("let dynamic = [1, 2]\nlet values: [_ of i32] = dynamic@move", "dynamic@move", "initializer has Array<i32>", "[_ of i32]")]
    public void HoleFailuresKeepTheirCauseLocationAndContextInPublicOutputs(string source, string text, string reason, string relatedText)
    {
        var path = Path.GetFullPath("ArrayAnnotationDiagnostic.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        c.Binding.ReportDiagnostics();
        var diagnostic = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(nameof(DiagnosticCode.ArrayAnnotationInference_Kd), diagnostic.Code);
        Assert.Equal(text, diagnostic.Text);
        Assert.Contains(reason, diagnostic.Label, StringComparison.Ordinal);
        var document = c.Diagnostics.FindDocument(path)!;
        c.Diagnostics.AddInput(document, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCategory.Language, record.Category);
        Assert.Contains(record.Reason!, x => x.Name == "reason" && x.Value.Contains(reason, StringComparison.Ordinal));
        Assert.Contains(record.Related!, x => x.Span is { } span && source.Substring(span.Start, span.Length) == relatedText);

        var check = new CheckOutput(CheckOutcome.Completed, false, TestPresence.No, result);
        DiagnosticOutputTestHelper.Single(check, reason, output);
    }

    [Theory]
    [InlineData("let values: [_ of i32] = missing()", "UnresolvedBinding_Kd")]
    [InlineData("let values: [_ of _] = []\nlet wrong: i32 = true", "ArrayAnnotationInference_Kd,TypeMismatch_Kd")]
    public void FailedEvidenceAndIndependentErrorsAreNotMisreported(string source, string codes)
    {
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        Assert.Equal(codes.Split(',').Order(StringComparer.Ordinal), TestDiagnostics.Of(c).Where(static x => x.Severity == DiagnosticSeverity.Error).Select(static x => x.Code).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("let values: [_ of i32] = []")]
    [InlineData("let values: [_ of [2 of i32]] = []")]
    [InlineData("let values: [_ of [_ of _]] = [[1, 2], [3, 4]]")]
    public void SufficientConsistentEvidenceIsAccepted(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.DoesNotContain(TestDiagnostics.Of(c), static x => x.Severity == DiagnosticSeverity.Error);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmFailureRecordingAllocatesNothing()
    {
        var c = CompilationTestHelper.ParseSuccess("let values: [_ of [_ of _]] = [[1], [2, 3]]");
        for (var i = 0; i < 4; i++)
        {
            Assert.False(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
    }
}
