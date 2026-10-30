// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class GenericGenerationDiagnosticTest(ITestOutputHelper output)
{
    private const string WideDivision = "func half<T>(value: T) -> T\n    T is PrimitiveInteger\n    return value / 2\nlet result = half(10@i128)";

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void InstanceFailuresAreLocatedOnly(bool remainder, bool forwarded)
    {
        var source = WideDivision.Replace("/", remainder ? "%" : "/", StringComparison.Ordinal);
        if (forwarded)
        {
            source = source.Replace("let result = half(10@i128)", "func forward<T>(value: T) -> T\n    T is PrimitiveInteger\n    return half(value)\nlet result = forward(10@i128)", StringComparison.Ordinal);
        }

        var path = Path.GetFullPath("generic-generation.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));

        // Check, then generation, as a build runs them: check reports the instance's operation (K13a), on every pipeline (DECISIONS R5-M156).
        Assert.Equal("Unsupported_Kd " + (remainder ? "value % 2" : "value / 2"), MinimalEmissionTest.Problems(c));
        c.ReportAnalysis();
        using var writer = new StringWriter();
        Assert.False(c.Generate(writer));
        Assert.Empty(writer.ToString());
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(DiagnosticPartition.Input, DiagnosticPartition.Emission, rejected: true);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal("Unsupported_Kd", error.Code);
        Assert.Equal(DiagnosticCategory.Unsupported, error.Category);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal(remainder ? "value % 2" : "value / 2", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Equal((null, null, null, null), (error.Reason, error.Note, error.Related, error.Repairs));
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains(error.Message, console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var relatedSupport in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, relatedSupport)[identity]);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Contains(error.Message, sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    [Fact]
    public void SuccessfulInstancesKeepTheExistingAllocationFreePath()
    {
        var source = WideDivision.Replace("10@i128", "10@i64", StringComparison.Ordinal) + "\nrequire result == 5 else => $abort(\"result\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
        ScalarEmissionTest.EmitFixture("GenericGenerationDiagnosticValid", source, string.Empty);
    }

    [Fact]
    public void IndependentUnsupportedOperationsRetainSeparateLocations()
    {
        var c = MinimalEmissionTest.Analyze(WideDivision.Replace("    return value / 2", "    let first = value / 2\n    return value % 2", StringComparison.Ordinal));
        Assert.Equal("Unsupported_Kd value / 2 | Unsupported_Kd value % 2", MinimalEmissionTest.Problems(c)); // At check (DECISIONS R5-M156).
        c.ReportAnalysis();
        Assert.False(c.Generate(TextWriter.Null));
        var errors = TestDiagnostics.Of(c);
        Assert.Equal(2, errors.Length);
        Assert.All(errors, x => Assert.Equal("Unsupported_Kd", x.Code));
        Assert.NotEqual(errors[0].Span, errors[1].Span);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmSuccessfulInstancePreparationAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(WideDivision.Replace("10@i128", "10@i64", StringComparison.Ordinal));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null)));
        Assert.True(valid);
    }

    [Fact]
    public async Task GeneratePublishesAnUnsupportedOperationInsteadOfAnInternalFailure()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kimi-generation-diagnostic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var console = new DiagnosticContractTest.DiagnosticConsole();
            var project = new Project(new Kimigayo(console)) { Name = "diagnostic", Directory = directory };
            project.AddSource("main.kimi", WideDivision);
            Assert.False(await project.Generate(TestContext.Current.CancellationToken));
            output.WriteLine(console.Text);
            Assert.Contains("Unsupported_Kd", console.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("GenerationFailed_Kd", console.Text, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFiles(directory, "*.ll", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
