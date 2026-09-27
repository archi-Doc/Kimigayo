// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Arc.Unit;
using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
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

        Assert.NotEmpty(first.Kimigayo.GetOrAddDiagnosticCollection("main.kimi").GetArray());
        Assert.Empty(second.Kimigayo.GetOrAddDiagnosticCollection("main.kimi").GetArray());
        Assert.Empty(kimigayo.GetOrAddDiagnosticCollection("main.kimi").GetArray());
    }

    [Fact]
    public void SilentServiceRendersNothingButKeepsTheErrorState()
    {
        var kimigayo = Kimigayo.CreateSilent();
        var compilation = new Compilation(kimigayo, new Project(kimigayo));
        compilation.Kotonoha.AddSource(new SourceDocument("main.kimi", Broken));

        Assert.True(compilation.Kotonoha.HasSourceErrors);
        Assert.True(compilation.Kimigayo.GetOrAddDiagnosticCollection("main.kimi").HasErrors);
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
        Assert.Equal(SourceIdentity.FromPath(this.PathOf("App", "main.kimi")), error.Location);
        Assert.NotNull(error.Range);
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

    [Fact]
    public void TextOnlyPreparationFailuresBecomeBlockedDiagnostics()
    {
        var project = this.WriteProject("App", settings: "Targets={}");
        var output = this.Run(project);

        Assert.Equal(CheckOutcome.Blocked, output.Outcome);
        var failure = Assert.Single(output.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.ProjectPreparationFailed_Kd), failure.Code);
        Assert.Contains("At least one compilation target", failure.Message);
        Assert.Equal(SourceIdentity.FromPath(project), failure.Location);
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
        Assert.Equal(SourceIdentity.FromPath(this.PathOf("App", "b.kimi")), encoding.Location);
        Assert.Null(encoding.Range);
        var syntax = output.Diagnostics.First(x => x.Location == SourceIdentity.FromPath(this.PathOf("App", "a.kimi")));
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
        var failure = Assert.Single(output.Diagnostics, static x => x.Code == nameof(DiagnosticCode.GenerationFailed_Kd));
        Assert.Equal(SourceIdentity.FromPath(this.PathOf("App", "main.kimi")), failure.Location);
        Assert.True(this.Run(project).Accepted);
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
        Assert.Contains("Conflicting inputs for leaf@1", failure.Message);
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

        Assert.Contains(output.Diagnostics, x => x.Location == SourceIdentity.FromPath(main) && x.Severity == DiagnosticSeverity.Error);
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
        Assert.Equal(SourceIdentity.FromPath(main), encoding.Location);
        Assert.NotNull(encoding.Range);
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
        Assert.True(output.Accepted, string.Join("; ", output.Diagnostics.Select(x => $"{x.Location}{x.Range}: {x.Message}")));
    }

    [Fact]
    public void TestModeRejectsAnUntestableTarget()
    {
        var project = this.WriteProject("App", ("main.kimi", Valid), settings: "Targets={\"x86_64-unknown-linux-gnu\"}");
        var output = this.Run(project, CheckMode.Test, "x86_64-unknown-linux-gnu");

        Assert.Equal(CheckOutcome.Blocked, output.Outcome);
        Assert.Contains(output.Diagnostics, static x => x.Message.Contains("Windows x64", StringComparison.Ordinal));
    }

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
