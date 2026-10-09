// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class ConstructorInferenceDiagnosticTest
{
    private const string Correlation = "struct C<T>\n    public init(value: (T, i32), code: i32) => ()\n    public init(value: ref/(i64, T), name: string) => ()\nlet x: (i64, i32) = (1, 2)\nlet c = C.init(x, 0)";
    private const string Selection = "struct C<T>\n    public init(x: ref/i32, y: ref/T) => ()\n    public init(x: ref/T, y: ref/T) => ()\nlet n: i32 = 42\nlet r = n@ref\nlet c = C.init(r@ref, n@ref)";

    [Theory]
    [InlineData(Correlation, "UnprovenAcquisitionCorrelation_Kd", "T", false)]
    [InlineData(Correlation, "UnprovenAcquisitionCorrelation_Kd", "T", true)]
    [InlineData(Selection, "ConstructorSelectionChanged_Kd", "C<i32>", false)]
    [InlineData(Selection, "ConstructorSelectionChanged_Kd", "C<i32>", true)]
    public void DistinctFactsSurviveBoundCheckingCliAndLsp(string source, string code, string evidence, bool checkBound)
    {
        var path = Path.GetFullPath("ConstructorInference.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.False(c.Binding.Result.IsComplete);
        if (checkBound)
        {
            Assert.False(c.Binding.CheckBound().IsComplete);
        }

        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(code, record.Code);
        Assert.Equal(DiagnosticCategory.Language, record.Category);
        Assert.Equal(evidence, record.Reason![0].Value);
        Assert.Equal(2, record.Related!.Length);
        Assert.Empty(record.Repairs ?? []);
        Assert.NotNull(record.Note);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains(code, console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(record.Code, sent.Code);
            Assert.Equal(record.Display!.Range, sent.Range);
        }
    }
}
