// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 23.2: diagnostics owned by one check, their immutable publication and output locations.
// Uses the shared project/input fixture; test identities and check behavior remain unchanged.
public sealed partial class CheckServiceTest
{
    [Fact]
    public void CompilationsOfOneServiceKeepSeparateDiagnostics()
    {
        var kimigayo = new Kimigayo(new CapturingConsole());
        var first = new Compilation(kimigayo, new Project(kimigayo));
        var second = new Compilation(kimigayo, new Project(kimigayo));
        first.Kotonoha.AddSource(new SourceDocument("main.kimi", Broken));
        second.Kotonoha.AddSource(new SourceDocument("main.kimi", Valid));

        Assert.NotEmpty(TestDiagnostics.Of(first, "main.kimi"));
        Assert.Empty(TestDiagnostics.Of(second, "main.kimi"));
        Assert.NotSame(first.Diagnostics, second.Diagnostics);
    }

    [Fact]
    public void SilentServiceRendersNothingButKeepsTheErrorState()
    {
        var kimigayo = Kimigayo.CreateSilent();
        var compilation = new Compilation(kimigayo, new Project(kimigayo));
        compilation.Kotonoha.AddSource(new SourceDocument("main.kimi", Broken));

        Assert.True(compilation.Diagnostics.HasSyntaxErrors(compilation.Kotonoha));
        Assert.True(compilation.Diagnostics.HasErrors);
    }

    [Fact]
    public async Task CheckReportsTheCommandDiagnosticsWithRanges()
    {
        var project = this.WriteProject("App", ("main.kimi", Broken));
        var console = new CapturingConsole();
        Assert.True(Project.TryCreate(new Kimigayo(console), null, project, out var loaded));
        Assert.False(await loaded.Check(TestContext.Current.CancellationToken));

        var output = this.Run(project);
        Assert.Equal(CheckOutcome.Completed, output.Outcome);
        Assert.False(output.Accepted);
        var error = output.Diagnostics.First(static x => x.Severity == DiagnosticSeverity.Error);
        Assert.Contains(error.Code, console.Output);
        Assert.Equal(SourceIdentity.FromPath(this.PathOf("App", "main.kimi")), Location(output, error));
        Assert.NotNull(error.Span);
    }

    [Fact]
    public void RepeatedChecksDoNotLeakDiagnostics()
    {
        var project = this.WriteProject("App", ("main.kimi", Broken));
        Assert.Contains(this.Run(project).Diagnostics, static x => x.Severity == DiagnosticSeverity.Error);

        File.WriteAllText(this.PathOf("App", "main.kimi"), Valid);
        var output = this.Run(project);
        Assert.True(output.Accepted, string.Join("; ", output.Diagnostics.Select(x => x.Message)));
        Assert.DoesNotContain(output.Diagnostics, static x => x.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [MemberData(nameof(ContextualLabelTest.DiagnosticCases), MemberType = typeof(ContextualLabelTest))]
    public async Task ContextualSyntaxExplainsTheExactRangeInCliAndCheck(string source, string valid, string code, string token)
    {
        const string Prefix = "func sample()\n    ";
        var project = this.WriteProject("Labels", ("main.kimi", Prefix + source));
        var console = new CapturingConsole();
        Assert.True(Project.TryCreate(new Kimigayo(console), null, project, out var loaded));
        Assert.False(await loaded.Check(TestContext.Current.CancellationToken));
        var output = this.Run(project);
        Assert.False(output.Accepted);
        var error = Assert.Single(output.Diagnostics, x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(code, error.Code);
        var start = token.Length == 0 ? source.Length : source.LastIndexOf(token, StringComparison.Ordinal);
        Assert.Equal(new SourceSpan(Prefix.Length + start, token.Length), error.Span);
        Assert.Contains(error.Message, console.Output);
        Assert.Contains(error.Label!, console.Output);
        Assert.Contains($"main.kimi:2:{start + 5}", console.Output);
        File.WriteAllText(this.PathOf("Labels", "main.kimi"), Prefix + valid);
        Assert.True(this.Run(project).Accepted);
    }

    // SPEC 23.2.2: a check that an input or its configuration blocks before the entry runs publishes one Input Error without a range.
    [Fact]
    public void ABlockedOutputPublishesOneInputErrorAtItsInput()
    {
        var project = SourceIdentity.FromPath(this.PathOf("Missing", "Missing.kimiproj"));
        var output = CheckOutput.Blocked(DiagnosticCode.ProjectLoadFailed_Kd, project, "The project file is missing.");
        Assert.Equal(CheckOutcome.Blocked, output.Outcome);
        Assert.False(output.Accepted);
        Assert.Equal(TestPresence.Unknown, output.Presence);
        var failure = Assert.Single(output.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.ProjectLoadFailed_Kd), failure.Code);
        Assert.Equal(DiagnosticSeverity.Error, failure.Severity);
        Assert.Equal(DiagnosticCategory.Input, failure.Category);
        Assert.Equal(project, Location(output, failure));
        Assert.Null(failure.Span);
        Assert.Equal("The project file is missing.", failure.Note);

        var unlocated = Assert.Single(CheckOutput.Blocked(DiagnosticCode.TargetSelectionRequired_Kd, default).Diagnostics);
        Assert.Equal(-1, unlocated.Source);
        Assert.Null(unlocated.Note);
    }
}
