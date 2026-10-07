// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 8.10, 23.3: generic adaptation errors describe the written operation, including in unused definitions.
public class GenericAdaptationDiagnosticTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("Transfer", nameof(DiagnosticCode.TransferRequired_Kd), "value", 3, "@move")]
    [InlineData("Copy", nameof(DiagnosticCode.NonCopyOperand_Kd), "value@copy", 3, "Copy")]
    [InlineData("Mismatch", nameof(DiagnosticCode.TypeMismatch_Kd), "1@s/i64", 2, "i64")]
    public void WrittenAcquisitionFailuresKeepTheirRangeAndCauseInCliAndLsp(string name, string code, string written, int line, string explanation)
    {
        var source = Program(name, valid: false);
        var path = Path.GetFullPath("generic-adaptation.kimi");
        var result = Publish(source, path);
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(code, record.Code);
        Assert.Equal(DiagnosticCategory.Language, record.Category);
        Assert.Equal(DiagnosticSeverity.Error, record.Severity);
        if (name == "Mismatch")
        {
            Assert.Equal("expected s/i64, found i32", record.Label);
        }

        Assert.Equal(written, source.Substring(record.Span!.Value.Start, record.Span.Value.Length));
        var range = new SourceRange(new(line, 11), new(line, 11 + written.Length));
        Assert.Equal(range, record.Display!.Range);

        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains(code, console.Text, StringComparison.Ordinal);
        Assert.Contains(explanation, console.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("<synthetic>", console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(code, sent.Code);
            Assert.Equal(range, sent.Range);
            Assert.Contains(explanation, sent.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("<synthetic>", sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }

        var shifted = Assert.Single(Publish("\n" + source, path).Diagnostics);
        Assert.Equal(record.Code, shifted.Code);
        Assert.Equal(record.Message, shifted.Message);
        Assert.Equal(record.Label, shifted.Label);
        Assert.Equal(new SourceRange(new(line + 1, 11), new(line + 1, 11 + written.Length)), shifted.Display!.Range);
        var independent = Publish(source + "\nfunc unrelated() -> i32 => true", path);
        Assert.Equal(2, independent.Diagnostics.Length);
        Assert.Contains(record, independent.Diagnostics);

        var repaired = MinimalEmissionTest.Analyze(Program(name, valid: true));
        Assert.True(repaired.Binding.Result.IsComplete && repaired.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(repaired, null));
    }

    private static string Program(string name, bool valid)
        => name switch
        {
            "Transfer" => "func box<s/T>(value: T) -> s/T\n    s is object\n    T is ObjectPayload\n    return " + (valid ? "value@move@s" : "value@s") + "\n()",
            "Copy" => "func box<s/T>(value: T) -> s/T\n    s is object\n    T is " + (valid ? "Copy and " : string.Empty) + "ObjectPayload\n    return value@copy@s\n()",
            _ => "func box<s/T>() -> s/i64\n    s is object\n    return " + (valid ? "1@i64@s/i64" : "1@s/i64") + "\n()",
        };

    private static DiagnosticResult Publish(string source, string path)
    {
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        return c.Diagnostics.Finalize(rejected: true);
    }
}
