// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public sealed class DuplicateDiagnosticTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("let x = 1\nlet x = 2\nlet x = 3", "x", "x")]
    [InlineData("func f(x: i32) -> i32 => x\nfunc f(y: i32) -> i32 => y\nfunc f(z: i32) -> i32 => z", "f", "f(y: i32) -> i32")]
    public void LaterDeclarationsReferToTheFirst(string source, string name, string secondText)
    {
        var c = Analyze(source);
        var result = c.Diagnostics.Finalize(rejected: true);
        var errors = result.Diagnostics.Where(static x => x.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Equal(2, errors.Length);
        Assert.All(errors, error =>
        {
            Assert.Equal(nameof(DiagnosticCode.DuplicateBinding_Kd), error.Code);
            Assert.Equal(DiagnosticCategory.Language, error.Category);
            Assert.Equal(name, Assert.Single(error.Reason!).Value);
            var first = Assert.Single(error.Related!);
            Assert.Equal("declaration", first.Role);
            Assert.Equal(0, first.Range!.Value.Start.Line);
        });
        Assert.Equal([1, 2], errors.Select(static x => x.Display!.Range!.Value.Start.Line));
        Assert.Equal(secondText, source.Substring(errors[0].Span!.Value.Start, errors[0].Span!.Value.Length));
        c.Bind();
        c.Binding.ReportDiagnostics();
        Assert.Equal(result, c.Diagnostics.Finalize(rejected: true));
    }

    [Fact]
    public void TheFirstDeclarationFollowsConsumedSourceOrder()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("z-first.kimi", "group G\n    public func x() => ()"));
        c.Kotonoha.AddSource(new SourceDocument("a-second.kimi", "group G\n    public func x() => ()"));
        Assert.False(c.Bind().IsComplete);
        c.Binding.ReportDiagnostics();
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(["z-first.kimi", "a-second.kimi"], result.Sources.Select(static x => x.Path));
        Assert.Equal(1, error.Source);
        Assert.Equal(new SourceSpan(24, 3), error.Span);
        Assert.Equal(0, Assert.Single(error.Related!).Source);
    }

    [Theory]
    [InlineData("let x = 1\nlet y = 2\nlet z = 3")]
    [InlineData("func f(x: i32) -> i32 => x\nfunc f(x: bool) -> bool => x")]
    public void DistinctDeclarationsRemainValid(string source)
    {
        var c = Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Empty(c.Diagnostics.Finalize().Diagnostics);
    }

    [Fact]
    public void IndependentErrorsAndBlankLinesPreserveDuplicateEvidence()
    {
        const string Duplicates = "let x = 1\nlet x = 2";
        foreach (var suffix in new[] { string.Empty, "\nmissing()" })
        {
            var c = Analyze("\n" + Duplicates + suffix);
            var result = c.Diagnostics.Finalize(rejected: true);
            var error = Assert.Single(result.Diagnostics, static x => x.Code == nameof(DiagnosticCode.DuplicateBinding_Kd));
            Assert.Equal(2, error.Display!.Range!.Value.Start.Line);
            Assert.Equal(1, Assert.Single(error.Related!).Range!.Value.Start.Line);
            Assert.DoesNotContain(result.Diagnostics, static x => x.Code == nameof(DiagnosticCode.PrerequisiteUnavailable_Kd));
            if (suffix.Length != 0)
            {
                Assert.Contains(result.Diagnostics, static x => x.Code == nameof(DiagnosticCode.UnresolvedBinding_Kd));
            }
        }
    }

    [Theory]
    [InlineData("alias A => Kimi\nalias A => Kimi.Console\nalias A => Kimi.Text", "A", 0)]
    [InlineData("contract C\nstruct S<T>\n    Self is C when T is Copy\n    Self is C when T is Owned\n    Self is C when T is Copy", "C", 2)]
    [InlineData("func f<T>(value: ref/T) -> i32 => 1\nspecialize func f<i32>(value: ref/i32) -> i32 => 2\nspecialize func f<i32>(value: ref/i32) -> i32 => 3\nspecialize func f<i32>(value: ref/i32) -> i32 => 4", "f", 1)]
    public void PairwiseRecordersShareTheSameNormalization(string source, string name, int firstLine)
    {
        var c = Analyze(source);
        var result = c.Diagnostics.Finalize(rejected: true);
        var duplicates = result.Diagnostics.Where(static x => x.Code == nameof(DiagnosticCode.DuplicateBinding_Kd)).ToArray();
        Assert.Equal(2, duplicates.Length);
        Assert.All(duplicates, error =>
        {
            Assert.Equal(name, Assert.Single(error.Reason!).Value);
            Assert.Equal(firstLine, Assert.Single(error.Related!).Range!.Value.Start.Line);
        });
        Assert.DoesNotContain(result.Diagnostics, static x => x.Code is nameof(DiagnosticCode.CheckFaulted_Kd) or nameof(DiagnosticCode.PrerequisiteUnavailable_Kd));
    }

    [Fact]
    public void RootRuntimeDeclarationsRemainSourceLocal()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        foreach (var path in new[] { "first.kimi", "second.kimi" })
        {
            c.Kotonoha.AddSource(new SourceDocument(path, "let x = 1\npublic func f() => ()"));
        }

        Assert.True(c.Bind().IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.Empty(c.Diagnostics.Finalize().Diagnostics);
    }

    [Fact]
    public void CliAndLspIdentifyTheLaterAndFirstDeclarations()
    {
        var path = Path.GetFullPath("DuplicateDiagnostic.kimi");
        var c = MinimalEmissionTest.Analyze("let x = 1\nlet x = 2", path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("DuplicateDiagnostic.kimi:2:5", console.Text, StringComparison.Ordinal);
        Assert.Contains("DuplicateDiagnostic.kimi:1:5", console.Text, StringComparison.Ordinal);
        Assert.Contains("'x' is declared again", console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var capability in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, capability)[identity]);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Contains("'x' is declared again", sent.Message, StringComparison.Ordinal);
            if (capability)
            {
                Assert.Equal(new SourceRange(new(0, 4), new(0, 5)), Assert.Single(sent.RelatedInformation!).Location.Range);
            }
            else
            {
                Assert.Contains("DuplicateDiagnostic.kimi:1:5: first declaration", sent.Message, StringComparison.Ordinal);
            }

            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    [TestClass(DisableParallelization = true)]
    [Trait("Purpose", "Allocation")]
    public sealed class AllocationTests
    {
        [Fact]
        public void WarmDuplicateChecksReuseTheirGroups()
        {
            var c = CompilationTestHelper.ParseSuccess("func f(x: i32) -> i32 => x\nfunc f(y: i32) -> i32 => y\nfunc f(z: i32) -> i32 => z");
            for (var i = 0; i < 4; i++)
            {
                Assert.False(c.Bind().IsComplete);
            }

            Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
        }
    }

    private static Compilation Analyze(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        return c;
    }
}
