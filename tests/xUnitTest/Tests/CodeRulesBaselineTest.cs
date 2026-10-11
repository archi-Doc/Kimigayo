// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace XunitTest;

public class CodeRulesBaselineTest
{
    [Theory]
    [InlineData("Enforce", 61, null)]
    [InlineData("Enforce", 62, "KR2006")]
    [InlineData("Enforce", 0, "KR9002")]
    [InlineData("Verify", 0, "KR9002")]
    [InlineData("Report", 61, "KR9001")]
    public void MaximumUsesBaselineAndRemovesIneffectiveBounds(string mode, int current, string? expected)
    {
        var diagnostics = new List<Diagnostic>();
        var baseline = Read("KR2006\tT:C\tmember:M:C.M\t61\t" + CodeRulesTestHelper.SourcePath, diagnostics);
        var observations = new List<CodeRuleObservation>();
        if (current > 60)
        {
            observations.Add(Observation("KR2006", "member:M:C.M", current, 60));
        }

        baseline.Complete(observations, mode, diagnostics.Add, TestContext.Current.CancellationToken);
        if (expected is null)
        {
            Assert.Empty(diagnostics);
        }
        else
        {
            var diagnostic = Assert.Single(diagnostics);
            Assert.Equal(expected, diagnostic.Id);
            if (expected == "KR9002")
            {
                Assert.Equal(mode == "Verify" ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning, diagnostic.Severity);
            }
        }
    }

    [Fact]
    public void DuplicateCountsAreMergedAcrossPartialFilesAndReportEveryOccurrence()
    {
        var diagnostics = new List<Diagnostic>();
        const string anchor = "node:0123456789abcdef";
        var baseline = Read("KR3003\tT:C\t" + anchor + "\t1\t" + CodeRulesTestHelper.SourcePath, diagnostics);
        var second = Observation("KR3003", anchor, 1, 0, "src/Kimi/Compiler/Binding/Other.cs");
        var observations = new List<CodeRuleObservation> { second, Observation("KR3003", anchor, 1, 0) };
        baseline.Complete(observations, "Verify", diagnostics.Add, TestContext.Current.CancellationToken);
        Assert.Equal(["KR3003", "KR3003"], diagnostics.Select(x => x.Id));
        Assert.All(diagnostics, x => Assert.Contains("occurrences 2, baseline 1", x.GetMessage(), StringComparison.Ordinal));
        diagnostics.Clear();
        baseline.Complete(observations, "Report", diagnostics.Add, TestContext.Current.CancellationToken);
        Assert.All(diagnostics, x => Assert.Equal("2", x.Properties["value"]));
        Assert.Equal("src/Kimi/Compiler/Binding/Other.cs", diagnostics[0].Properties["path"]);
    }

    [Fact]
    public void MovingTypeBetweenPartialFilesDoesNotHideMirViolation()
    {
        var diagnostics = new List<Diagnostic>();
        var baseline = Read("KR2006\tT:C\tmember:M:C.M\t61\t" + CodeRulesTestHelper.SourcePath, diagnostics);
        baseline.Complete(
            [Observation("KR2006", "member:M:C.M", 61, 60, "src/Kimi/Compiler/Mir/Test.cs")],
            "Enforce",
            diagnostics.Add,
            TestContext.Current.CancellationToken);
        Assert.Equal("KR2006", Assert.Single(diagnostics).Id);
    }

    [Theory]
    [InlineData("KR2006\tT:C\tmember:M:C.M\t0\tsrc/Kimi/C.cs")]
    [InlineData("KR2006\tT:C\tmember:M:C.M\t61\tsrc/Kimi/../C.cs")]
    [InlineData("KR2006\tT:C\tmember:M:C.M\t61\tsrc/Kimi/Compiler/Mir/C.cs")]
    [InlineData("KR1001\tT:C\tnode:0123456789abcdef\t1\tsrc/Kimi/C.cs")]
    [InlineData("KR3003\tT:C\tnode:invalid\t1\tsrc/Kimi/C.cs")]
    [InlineData("KR9999\tT:C\tfile\t1\tsrc/Kimi/C.cs")]
    public void InvalidRowsHaveLocatedDiagnostics(string row)
    {
        var diagnostics = new List<Diagnostic>();
        Read(row, diagnostics);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("KR9003", diagnostic.Id);
        Assert.Equal("CodeRules.baseline.tsv", diagnostic.Location.GetLineSpan().Path);
    }

    [Fact]
    public void DuplicateOrUnsortedKeysAreRejected()
    {
        foreach (var scopes in new[] { new[] { "T:C", "T:C" }, new[] { "T:D", "T:C" } })
        {
            var diagnostics = new List<Diagnostic>();
            Read(string.Join("\n", scopes.Select(x => $"KR2006\t{x}\tmember:M:C.M\t61\t{CodeRulesTestHelper.SourcePath}")), diagnostics);
            Assert.Equal("KR9003", Assert.Single(diagnostics).Id);
        }
    }

    private static CodeRulesBaseline Read(string rows, List<Diagnostic> diagnostics)
        => CodeRulesBaseline.Read(SourceText.From(rows), "CodeRules.baseline.tsv", diagnostics);

    private static CodeRuleObservation Observation(string id, string anchor, int value, int limit, string path = CodeRulesTestHelper.SourcePath)
        => new(new CodeRuleKey(CodeRule.Find(id)!, "T:C", anchor), path, Location.None, value, limit);
}
