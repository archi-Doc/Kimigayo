// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Arc.Unit;
using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 23.3: the shared check entry, compilation-owned diagnostics and immutable check output.
public sealed class CheckServiceTest : IDisposable
{
    private const string Valid = "public func one() -> i32 => 1\n";
    private const string Broken = "public func one() -> i32 => 1 +\n";

    private readonly string directory = Path.Combine(Path.GetTempPath(), "kimi-check-" + Guid.NewGuid().ToString("N"));

    public CheckServiceTest() => Directory.CreateDirectory(this.directory);

    public void Dispose() => Directory.Delete(this.directory, true);

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
        Assert.Contains("Advice: " + error.Advice, console.Output);
        Assert.Contains($"main.kimi:2:{start + 5}", console.Output);
        File.WriteAllText(this.PathOf("Labels", "main.kimi"), Prefix + valid);
        Assert.True(this.Run(project).Accepted);
    }

    [Fact]
    public void TextOnlyPreparationFailuresBecomeBlockedDiagnostics()
    {
        var project = this.WriteProject("App", settings: "Targets={}");
        var output = this.Run(project);

        Assert.Equal(CheckOutcome.Blocked, output.Outcome);
        var failure = Assert.Single(output.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.ProjectPreparationFailed_Kd), failure.Code);
        Assert.Contains("At least one compilation target", failure.Note);
        Assert.Equal(SourceIdentity.FromPath(project), Location(output, failure));
    }

    // SPEC 23.3.1, 23.3.3: an unsupported target is an Input problem of its own, Blocked in the check entry and reported by the
    // command, never an unexplained rejection.
    [Fact]
    public async Task AnUnsupportedTargetIsAnInputProblem()
    {
        var project = this.WriteProject("App", ("main.kimi", Valid), settings: "Targets={\"unknown-none-elf\"}");
        var output = this.Run(project, target: "unknown-none-elf");
        Assert.Equal(CheckOutcome.Blocked, output.Outcome);
        var failure = Assert.Single(output.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.UnsupportedTarget_Kd), failure.Code);
        Assert.Equal(DiagnosticCategory.Input, failure.Category);

        var console = new CapturingConsole();
        Assert.True(Project.TryCreate(new Kimigayo(console), null, project, out var loaded));
        loaded.KimiOptions = new Kimi.Command.KimiOptions { Target = "unknown-none-elf" };
        Assert.False(await loaded.Check(TestContext.Current.CancellationToken));
        Assert.Contains(nameof(DiagnosticCode.UnsupportedTarget_Kd), console.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(DiagnosticCode.CheckFaulted_Kd), console.Output, StringComparison.Ordinal);
    }

    // SPEC 23.3.3: an invalid test configuration is not established, so test preparation is Blocked before the front end.
    [Fact]
    public void AnInvalidTestProjectIdBlocksTestPreparation()
    {
        var project = this.WriteProject("App", ("main.kimi", Valid), settings: "TestProjectId=\" \"");
        var output = this.Run(project, CheckMode.Test);
        Assert.Equal(CheckOutcome.Blocked, output.Outcome);
        var failure = Assert.Single(output.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.ProjectPreparationFailed_Kd), failure.Code);
        Assert.Contains("TestProjectId", failure.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EarlierSyntaxErrorsSurviveALaterEncodingFailure()
    {
        var project = this.WriteProject("App", ("a.kimi", Broken));
        File.WriteAllBytes(this.PathOf("App", "b.kimi"), [0x66, 0xff, 0x0a]);
        var console = new CapturingConsole();
        Assert.True(Project.TryCreate(new Kimigayo(console), null, project, out var loaded));
        Assert.False(await loaded.Check(TestContext.Current.CancellationToken));

        var output = this.Run(project);
        Assert.Equal(CheckOutcome.Blocked, output.Outcome);
        var encoding = Assert.Single(output.Diagnostics, static x => x.Code == nameof(DiagnosticCode.InvalidSourceEncoding_Kd));
        Assert.Equal(SourceIdentity.FromPath(this.PathOf("App", "b.kimi")), Location(output, encoding));
        Assert.Null(encoding.Span);
        var syntax = output.Diagnostics.First(x => Location(output, x) == SourceIdentity.FromPath(this.PathOf("App", "a.kimi")));
        Assert.Contains(syntax.Code, console.Output);
        Assert.True(console.Output.IndexOf(syntax.Code, StringComparison.Ordinal) < console.Output.IndexOf(encoding.Code, StringComparison.Ordinal));
    }

    [Fact]
    public void UnreadableSourceIsBlockedUntilItCanBeRead()
    {
        var project = this.WriteProject("App", ("main.kimi", Valid));
        CheckOutput output;
        using (new FileStream(this.PathOf("App", "main.kimi"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            output = this.Run(project);
        }

        Assert.Equal(CheckOutcome.Blocked, output.Outcome);
        var failure = Assert.Single(output.Diagnostics, static x => x.Code == nameof(DiagnosticCode.SourceReadFailed_Kd));
        Assert.Equal(SourceIdentity.FromPath(this.PathOf("App", "main.kimi")), Location(output, failure));
        Assert.True(this.Run(project).Accepted);
    }

    [Fact]
    public void EveryFailedSourceReadIsExplainedAtItsOwnInput()
    {
        var project = this.WriteProject("ReadFailures", ("a.kimi", Valid));
        var first = this.PathOf("ReadFailures", "a.kimi");
        var second = this.PathOf("ReadFailures", "b.kimi");
        File.WriteAllText(second, "public func two() -> i32 => 2\n");
        using (new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.None))
        using (new FileStream(second, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var output = this.Run(project);
            Assert.Equal(CheckOutcome.Blocked, output.Outcome);
            Assert.False(output.Accepted);
            Assert.Equal([first, second], output.Diagnostics.Select(x => output.Sources[x.Source].Path));
            Assert.All(output.Diagnostics, static x =>
            {
                Assert.Equal(nameof(DiagnosticCode.SourceReadFailed_Kd), x.Code);
                Assert.Equal(DiagnosticCategory.Input, x.Category);
                Assert.Null(x.Span);
                Assert.NotEmpty(x.Note!);
            });
        }

        Assert.True(this.Run(project).Accepted);
    }

    [Fact]
    public async Task EncodingFailuresPreserveEveryConsumedInputsOwnExplanation()
    {
        var project = this.WriteProject("EncodingFailures", ("a.kimi", Valid));
        File.WriteAllBytes(this.PathOf("EncodingFailures", "a.kimi"), [0xff]);
        File.WriteAllText(this.PathOf("EncodingFailures", "b.kimi"), Broken);
        File.WriteAllBytes(this.PathOf("EncodingFailures", "c.kimi"), [0xfe]);
        var output = this.Run(project);
        Assert.Equal(CheckOutcome.Blocked, output.Outcome);
        Assert.Equal(["a.kimi", "c.kimi"], output.Diagnostics.Where(static x => x.Code == nameof(DiagnosticCode.InvalidSourceEncoding_Kd)).Select(x => Path.GetFileName(output.Sources[x.Source].Path)));
        Assert.Contains(output.Diagnostics, x => Path.GetFileName(output.Sources[x.Source].Path) == "b.kimi" && x.Category == DiagnosticCategory.Language);
        Assert.DoesNotContain(output.Diagnostics, static x => x.Code is nameof(DiagnosticCode.CheckFaulted_Kd) or nameof(DiagnosticCode.ProjectPreparationFailed_Kd));
        var console = new CapturingConsole();
        Assert.True(Project.TryCreate(new Kimigayo(console), null, project, out var loaded));
        Assert.False(await loaded.Check(TestContext.Current.CancellationToken));
        Assert.Equal(2, console.Output.Split(nameof(DiagnosticCode.InvalidSourceEncoding_Kd), StringSplitOptions.None).Length - 1);
        Assert.Contains("a.kimi", console.Output, StringComparison.Ordinal);
        Assert.Contains("b.kimi", console.Output, StringComparison.Ordinal);
        Assert.Contains("c.kimi", console.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TestSourceFailuresAreBlockedInputProblems(bool encoding)
    {
        var project = this.WriteProject("TestInput", ("main.kimi", Valid), settings: "TestSources={\"tests/one.kimi\"}");
        Directory.CreateDirectory(this.PathOf("TestInput", "tests"));
        var path = this.PathOf("TestInput", "tests", "one.kimi");
        if (encoding)
        {
            File.WriteAllBytes(path, [0xff]);
        }

        var output = this.Run(project, CheckMode.Test);
        Assert.Equal(CheckOutcome.Blocked, output.Outcome);
        Assert.False(output.Accepted);
        var failure = Assert.Single(output.Diagnostics);
        Assert.Equal(encoding ? nameof(DiagnosticCode.InvalidSourceEncoding_Kd) : nameof(DiagnosticCode.SourceReadFailed_Kd), failure.Code);
        Assert.Equal(DiagnosticCategory.Input, failure.Category);
        Assert.Equal(path, output.Sources[failure.Source].Path);
        Assert.Null(failure.Span);
        var identity = SourceIdentity.FromPath(path);
        var sent = Assert.Single(WorkspaceCheck.Place(output, [], SourceIdentity.FromPath(project))[identity]);
        Assert.Equal(failure.Code, sent.Code);
        Assert.Equal(default, sent.Range);
        Assert.Contains(failure.Message, sent.Message, StringComparison.Ordinal);
        File.WriteAllText(path, "#Test\nfunc check()\n    $expect(true)\n");
        Assert.True(this.Run(project, CheckMode.Test).Accepted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFailedProductInputDoesNotSwallowPendingOrCancelledTestReads(bool cancelled)
    {
        var project = this.WriteProject("PendingTest", ("main.kimi", Valid), settings: "TestSources={\"pending.kimi\"}");
        File.WriteAllBytes(this.PathOf("PendingTest", "main.kimi"), [0xff]);
        var path = this.PathOf("PendingTest", "pending.kimi");
        Exception exception = cancelled ? new OperationCanceledException() : new PendingInputException(path);
        var inputs = new FailingInputSource(path, exception);
        var thrown = Record.Exception(() => this.Run(project, CheckMode.Test, inputs: inputs));
        Assert.Same(exception, thrown);
    }

    [Fact]
    public async Task ConflictingDependencyInputsUnderALockReportTheCommandMessage()
    {
        this.WriteLibrary("LeafA", "leaf", "public func a() -> i32 => 1\n");
        this.WriteLibrary("LeafB", "leaf", "public func b() -> i32 => 2\n");
        var project = this.WriteProject(
            "App",
            ("main.kimi", Valid),
            dependencies: "A={PackageId=\"leaf\" PackageVersion=\"1\" Project=\"../LeafA/LeafA.kimiproj\"} B={PackageId=\"leaf\" PackageVersion=\"1\" Project=\"../LeafB/LeafB.kimiproj\"}");
        DependencyLock.Update(DependencyLock.PathForProject(project), DependencyResolver.Resolve(project, WindowsProfile.Target, Compilation.CurrentLanguageVersion, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        var console = new CapturingConsole();
        Assert.True(Project.TryCreate(new Kimigayo(console), null, project, out var loaded));
        Assert.False(await loaded.Check(TestContext.Current.CancellationToken));

        var output = this.Run(project);
        Assert.Equal(CheckOutcome.Blocked, output.Outcome);
        var failure = Assert.Single(output.Diagnostics);
        Assert.Contains("Conflicting inputs for leaf@1", failure.Note);
        Assert.Contains("Conflicting inputs for leaf@1", console.Output);
    }

    [Fact]
    public void OverlaysReplaceDiskContentAndExistence()
    {
        var project = this.WriteProject("App", ("main.kimi", Valid));
        var main = this.PathOf("App", "main.kimi");
        var added = this.PathOf("App", "added.kimi");
        var inputs = new OverlayInputSource(new() { [main] = Broken, [added] = "public func two() -> i32 => 2\n" });
        var output = this.Run(project, inputs: inputs);

        Assert.Contains(output.Diagnostics, x => Location(output, x) == SourceIdentity.FromPath(main) && x.Severity == DiagnosticSeverity.Error);
        Assert.Contains(added, inputs.Listed);
        Assert.True(this.Run(project).Accepted);
    }

    [Fact]
    public void UnencodableOverlayTextIsAnEncodingDiagnostic()
    {
        var project = this.WriteProject("App", ("main.kimi", Valid));
        var main = this.PathOf("App", "main.kimi");
        var output = this.Run(project, inputs: new OverlayInputSource(new() { [main] = "// \ud800\n" + Valid }));

        Assert.False(output.Accepted);
        var encoding = Assert.Single(output.Diagnostics, static x => x.Code == nameof(DiagnosticCode.InvalidSourceEncoding_Kd));
        Assert.Equal(SourceIdentity.FromPath(main), Location(output, encoding));
        Assert.NotNull(encoding.Span);
    }

    [Theory]
    [InlineData(Valid, TestPresence.No)]
    [InlineData("#Test\nfunc check()\n    $expect(true)\n", TestPresence.Yes)]
    [InlineData("#Test\nfunc check()\n    Missing.call()\n", TestPresence.Yes)]
    [InlineData(Broken, TestPresence.Unknown)]
    public void ProductChecksReportTestPresence(string source, TestPresence presence)
    {
        var project = this.WriteProject("App", ("main.kimi", source));
        Assert.Equal(presence, this.Run(project).Presence);
    }

    [Fact]
    public void TestModeChecksTestSources()
    {
        var project = this.WriteProject("App", ("main.kimi", Valid), settings: "TestSources={\"tests/one.kimi\"}");
        Directory.CreateDirectory(this.PathOf("App", "tests"));
        File.WriteAllText(this.PathOf("App", "tests", "one.kimi"), "#Test\nfunc returnsOne()\n    $expect(true)\n");

        var output = this.Run(project, CheckMode.Test);
        Assert.Equal(CheckOutcome.Completed, output.Outcome);
        Assert.True(output.Accepted, string.Join("; ", output.Diagnostics.Select(x => $"{x.Source}{x.Span}: {x.Message}")));
    }

    [Fact]
    public void TestModeRejectsAnUntestableTarget()
    {
        var project = this.WriteProject("App", ("main.kimi", Valid), settings: "Targets={\"x86_64-unknown-linux-gnu\"}");
        var output = this.Run(project, CheckMode.Test, "x86_64-unknown-linux-gnu");

        Assert.Equal(CheckOutcome.Blocked, output.Outcome);
        Assert.Contains(output.Diagnostics, static x => x.Note?.Contains("Windows x64", StringComparison.Ordinal) == true);
    }

    // SPEC 23.3.3: a check that an input or its configuration blocks before the entry runs publishes one Input Error without a range.
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

    // SPEC 23.3.1: the scan is syntactic, so a marker on a local function is found; test preparation then explains the invalid definition.
    [Fact]
    public void ATestMarkerOnALocalFunctionIsFoundAndThenRejected()
    {
        const string Source = "public func outer() -> ()\n    #Test\n    func inner() -> () => ()\n    inner()\n";
        var project = this.WriteProject("Local", ("main.kimi", Source));
        Assert.Equal(TestPresence.Yes, this.Run(project).Presence);
        var output = this.Run(project, CheckMode.Test);
        Assert.Equal(CheckOutcome.Completed, output.Outcome);
        Assert.False(output.Accepted);
        Assert.Contains(output.Diagnostics, static x => x.Code == nameof(DiagnosticCode.InvalidTestDefinition_Kd));
    }

    private static SourceIdentity Location(CheckOutput output, CheckDiagnostic diagnostic)
        => diagnostic.Source < 0 ? default : SourceIdentity.FromPath(output.Sources[diagnostic.Source].Path);

    private CheckOutput Run(string projectPath, CheckMode mode = CheckMode.Product, string target = "", CheckInputSource? inputs = null)
    {
        inputs ??= CheckInputSource.Disk;
        Assert.True(Project.TryCreate(Kimigayo.CreateSilent(), null, projectPath, inputs, out var project, out var failure), failure);
        return CheckService.Run(project, target.Length == 0 ? WindowsProfile.Target : target, mode, false, inputs, TestContext.Current.CancellationToken);
    }

    private string PathOf(params string[] parts)
        => Path.Combine([this.directory, .. parts]);

    private string WriteProject(string name, (string File, string Text)? source = null, string settings = "", string dependencies = "")
    {
        var folder = Path.Combine(this.directory, name);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name + ".kimiproj");
        var targets = settings.Contains("Targets=", StringComparison.Ordinal) ? string.Empty : $"Targets={{\"{WindowsProfile.Target}\"}} ";
        File.WriteAllText(path, $"OutputKind=\"Library\" {targets}Dependencies={{{dependencies}}} {settings}");
        if (source is var (file, text))
        {
            File.WriteAllText(Path.Combine(folder, file), text);
        }

        return path;
    }

    private void WriteLibrary(string name, string id, string source)
    {
        var folder = Path.Combine(this.directory, name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name + ".kimiproj"), $"OutputKind=\"Library\" PackageId=\"{id}\" PackageVersion=\"1\" Targets={{\"{WindowsProfile.Target}\"}}");
        File.WriteAllText(Path.Combine(folder, "main.kimi"), source);
    }

    private sealed class OverlayInputSource(Dictionary<string, string> overlays) : CheckInputSource
    {
        public List<string> Listed { get; } = [];

        public override byte[] ReadAllBytes(string path)
            => overlays.TryGetValue(path, out var text) ? Encoding.UTF8.GetBytes(text) : Disk.ReadAllBytes(path);

        public override SourceContent ReadSource(string path)
            => overlays.TryGetValue(path, out var text) ? SourceContent.FromText(text, false) : Disk.ReadSource(path);

        public override string[] GetFiles(string directory, string pattern)
        {
            var files = new SortedSet<string>(Disk.GetFiles(directory, pattern), StringComparer.Ordinal);
            foreach (var path in overlays.Keys)
            {
                if (SourceIdentity.PathComparer.Equals(Path.GetDirectoryName(path), directory) && path.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase))
                {
                    files.Add(path);
                }
            }

            this.Listed.AddRange(files);
            return files.ToArray();
        }
    }

    private sealed class FailingInputSource(string failing, Exception failure) : CheckInputSource
    {
        public override byte[] ReadAllBytes(string path) => Disk.ReadAllBytes(path);

        public override SourceContent ReadSource(string path) => SourceIdentity.PathComparer.Equals(path, failing) ? throw failure : Disk.ReadSource(path);

        public override string[] GetFiles(string directory, string pattern) => Disk.GetFiles(directory, pattern);
    }

    private sealed class CapturingConsole : IConsoleService
    {
        private readonly StringBuilder output = new();

        public string Output => this.output.ToString();

        public bool KeyAvailable => false;

        public bool EnableColor { get; set; }

        public void Write(string? message, ConsoleColor color = ConsoleColor.Gray) => this.output.Append(message);

        public void Write(ReadOnlySpan<char> message, ConsoleColor color = ConsoleColor.Gray) => this.output.Append(message);

        public void WriteLine(string? message, ConsoleColor color = ConsoleColor.Gray) => this.output.Append(message).Append('\n');

        public void WriteLine(ReadOnlySpan<char> message, ConsoleColor color = ConsoleColor.Gray) => this.output.Append(message).Append('\n');

        public Task<InputResult> ReadLineAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public ConsoleKeyInfo ReadKey(bool intercept) => throw new NotSupportedException();
    }
}
