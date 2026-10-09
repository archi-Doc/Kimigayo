// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Kimi;
using Kimi.Checking;
using Kimi.Command;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 23.3.6.8: `kimi check --Format json` checks one unit through the shared check entry and writes the kimi.check/2 document
// to standard output alone; its records equal the text form's, its inputs carry the hash of the bytes that were read, and a
// project that cannot be loaded is a Blocked document.
public sealed class CheckJsonOutputTest : IDisposable
{
    private const string Broken = "struct Resource\n    public var value: i32\n    public init(value: i32) => self.value = value\n    drop => ()\nfunc consume(value: Resource) => ()\npublic func main()\n    let resource = Resource.init(1)\n    consume(resource)\n";

    private readonly string directory = Path.Combine(Path.GetTempPath(), "kimi-check-json-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(this.directory))
        {
            Directory.Delete(this.directory, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectoryReadFailuresProduceBlockedDocuments(bool accessDenied)
    {
        Directory.CreateDirectory(this.directory);
        var failure = accessDenied ? (Exception)new UnauthorizedAccessException("Directory access denied.") : new IOException("Directory disappeared.");
        var document = CheckJsonOutput.Create(new KimiOptions(), [this.directory], TestContext.Current.CancellationToken, new FailingDirectoryInput(failure));
        AssertBlockedInput(document, this.directory, failure.Message);
    }

    [Fact]
    public void InvalidPathsProduceBlockedDocuments()
    {
        var document = CheckJsonOutput.Create(new KimiOptions(), ["invalid\0.kimiproj"], TestContext.Current.CancellationToken);
        AssertBlockedInput(document, string.Empty, null);
    }

    [Fact]
    public void CancellationDoesNotBecomeAnInputFailure()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => CheckJsonOutput.Create(new KimiOptions(), [], cancellation.Token));
        Directory.CreateDirectory(this.directory);
        Assert.Throws<OperationCanceledException>(() => CheckJsonOutput.Create(new KimiOptions(), [this.directory], TestContext.Current.CancellationToken, new FailingDirectoryInput(new OperationCanceledException())));
    }

    private static void AssertBlockedInput(CheckDocument document, string path, string? reason)
    {
        Assert.Equal(CheckOutcome.Blocked, document.Outcome);
        Assert.False(document.Accepted);
        Assert.Equal(path, document.Unit.Project);
        var record = Assert.Single(document.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.ProjectLoadFailed_Kd), record.Code);
        var json = JsonSerializer.Serialize(document, DiagnosticJsonContext.Default.CheckDocument);
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal("Blocked", parsed.RootElement.GetProperty("outcome").GetString());
        if (reason is not null)
        {
            Assert.Contains(reason, json, StringComparison.Ordinal);
        }
    }

    private sealed class FailingDirectoryInput(Exception failure) : CheckInputSource
    {
        public override byte[] ReadAllBytes(string path) => throw new InvalidOperationException("No file should be read.");

        public override SourceContent ReadSource(string path) => throw new InvalidOperationException("No source should be read.");

        public override string[] GetFiles(string directory, string pattern) => throw failure;
    }

    [Fact]
    public void TheDocumentHoldsTheUnitItsSourcesAndTheRecords()
    {
        var project = this.WriteProject("App", "main.kimi", Broken);
        var document = CheckJsonOutput.Create(new KimiOptions(), [project], TestContext.Current.CancellationToken);
        Assert.Equal(CheckDocument.SchemaName, document.Schema);
        Assert.Equal(Compilation.CompilerVersion, document.Compiler);
        Assert.Equal(new CheckUnitDescription(project, WindowsProfile.Target, CheckMode.Product, false), document.Unit);
        Assert.Equal(CheckOutcome.Completed, document.Outcome);
        Assert.False(document.Accepted);
        Assert.Equal(TestPresence.No, document.TestPresence);

        // Every published input carries the hash of the bytes that were read; the project file is read but named by no record (SPEC 23.3.6.3).
        Assert.Equal([Path.Combine(Path.GetDirectoryName(project)!, "main.kimi")], document.Sources.Select(static x => x.Path));
        Assert.All(document.Sources, x => Assert.Equal(Hash(x.Path), x.Sha256));
        Assert.All(document.Sources, static x => Assert.True(x.IsInput));

        // The entry is shared, so the records are those the check service publishes for the same inputs.
        Assert.True(Project.TryCreate(Kimigayo.CreateSilent(), null, project, CheckInputSource.Disk, out var loaded, out var failure), failure);
        var expected = CheckService.Run(loaded, WindowsProfile.Target, CheckMode.Product, false, CheckInputSource.Disk, TestContext.Current.CancellationToken);
        Assert.Equal(expected.Diagnostics, document.Diagnostics);
        var record = Assert.Single(document.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.TransferRequired_Kd), record.Code);
        Assert.NotNull(record.Repairs);
    }

    [Fact]
    public void TheJsonFormFollowsTheSchemaAndRoundTrips()
    {
        var project = this.WriteProject("App", "main.kimi", Broken);
        var document = CheckJsonOutput.Create(new KimiOptions(), [project], TestContext.Current.CancellationToken);
        var json = JsonSerializer.Serialize(document, DiagnosticJsonContext.Default.CheckDocument);
        Assert.StartsWith("{\"schema\":\"kimi.check/2\",\"compiler\":\"", json, StringComparison.Ordinal);
        Assert.Contains("\"unit\":{\"project\":", json, StringComparison.Ordinal);
        Assert.Contains("\"mode\":\"Product\",\"debug\":false}", json, StringComparison.Ordinal);
        Assert.Contains("\"outcome\":\"Completed\",\"accepted\":false,\"testPresence\":\"No\",\"sources\":[{\"path\":", json, StringComparison.Ordinal);
        Assert.Contains("\"isInput\":true,\"sha256\":\"", json, StringComparison.Ordinal);
        Assert.Contains("\"diagnostics\":[{\"code\":\"TransferRequired_Kd\",\"severity\":\"Error\",\"category\":\"Language\",\"message\":", json, StringComparison.Ordinal);
        Assert.Contains("\"repairs\":[{\"kind\":\"Repair.Transfer\",", json, StringComparison.Ordinal);
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal(["schema", "compiler", "unit", "outcome", "accepted", "testPresence", "sources", "diagnostics"], parsed.RootElement.EnumerateObject().Select(static x => x.Name));
        Assert.Equal(document, JsonSerializer.Deserialize(json, DiagnosticJsonContext.Default.CheckDocument));
    }

    // SPEC 23.3.3: a project that cannot be found or loaded, and a directory without exactly one project, are Blocked documents.
    [Theory]
    [InlineData("missing", "App.kimiproj", "ProjectLoadFailed_Kd")]
    [InlineData("empty", "", "ProjectLoadFailed_Kd")]
    public void AnInputThatCannotBeCheckedIsBlocked(string folder, string file, string code)
    {
        var path = Path.Combine(this.directory, folder);
        Directory.CreateDirectory(path);
        var input = file.Length == 0 ? path : Path.Combine(path, file);
        var document = CheckJsonOutput.Create(new KimiOptions(), [input], TestContext.Current.CancellationToken);
        Assert.Equal(CheckOutcome.Blocked, document.Outcome);
        Assert.False(document.Accepted);
        var record = Assert.Single(document.Diagnostics);
        Assert.Equal(code, record.Code);
        Assert.All(document.Sources, static x => Assert.Null(x.Sha256));
    }

    // Standard output carries the document alone; the exit code follows acceptance as in the text form.
    [Fact]
    public async Task TheCommandWritesOnlyTheDocumentToStandardOutput()
    {
        var project = this.WriteProject("App", "main.kimi", Broken);
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Kimi.exe" : "Kimi"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = this.directory,
        };
        start.ArgumentList.Add("check");
        start.ArgumentList.Add(project);
        start.ArgumentList.Add("--Format");
        start.ArgumentList.Add("json");
        using var process = Process.Start(start)!;
        var stdout = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, process.ExitCode);
        using var parsed = JsonDocument.Parse(stdout);
        Assert.Equal(CheckDocument.SchemaName, parsed.RootElement.GetProperty("schema").GetString());
        Assert.Equal("Completed", parsed.RootElement.GetProperty("outcome").GetString());
        Assert.DoesNotContain("TransferRequired_Kd", stderr, StringComparison.Ordinal);
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private string WriteProject(string name, string file, string text)
    {
        var folder = Path.Combine(this.directory, name);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name + ".kimiproj");
        File.WriteAllText(path, $"OutputKind=\"Application\" Targets={{\"{WindowsProfile.Target}\"}}");
        File.WriteAllText(Path.Combine(folder, file), text);
        return path;
    }
}
