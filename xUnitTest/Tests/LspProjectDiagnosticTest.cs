// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// SPEC 23.4.2 to 23.4.8 end to end: documents, discovery, units and publication through the real check entry.
public sealed class LspProjectDiagnosticTest : IDisposable
{
    private const string Valid = "public func one() -> i32 => 1\n";
    private const string Broken = "public func one() -> i32 => 1 +\n";

    private readonly string directory = Path.Combine(Path.GetTempPath(), "kimi-lsp-" + Guid.NewGuid().ToString("N"));

    public LspProjectDiagnosticTest() => Directory.CreateDirectory(this.directory);

    public void Dispose() => Directory.Delete(this.directory, true);

    [Fact]
    public async Task ImplicitSourcePublishesErrorsAndClearsThem()
    {
        var path = Path.Combine(this.directory, "Hello.kimi");
        await using var client = new LspTestClient();
        await client.InitializeAsync();
        await client.OpenAsync(path, "::Kimi.Console.writeLine(\"Hello\" +\n");
        var publish = await client.ReceiveAsync(x => LspTestClient.IsPublish(x, LspTestClient.Uri(path)));
        Assert.Equal(1, publish.GetProperty("params").GetProperty("version").GetInt32());
        var diagnostic = publish.GetProperty("params").GetProperty("diagnostics")[0];
        Assert.Equal(1, diagnostic.GetProperty("severity").GetInt32());
        Assert.Equal("kimigayo", diagnostic.GetProperty("source").GetString());
        Assert.EndsWith("_Kd", diagnostic.GetProperty("code").GetString(), StringComparison.Ordinal);

        await client.ChangeAsync(path, 2, LspTestClient.Full("::Kimi.Console.writeLine(\"Hello\")\n"));
        publish = await client.ReceiveAsync(x => LspTestClient.IsPublish(x, LspTestClient.Uri(path)));
        Assert.Equal(2, publish.GetProperty("params").GetProperty("version").GetInt32());
        Assert.Equal(0, publish.GetProperty("params").GetProperty("diagnostics").GetArrayLength());
    }

    [Fact]
    public async Task IncrementalChangesAreCheckedAgainstTheEditedText()
    {
        var project = this.WriteProject("App", ("main.kimi", Valid));
        var main = this.PathOf("App", "main.kimi");
        await using var client = new LspTestClient();
        await client.InitializeAsync();
        await client.OpenAsync(main, Valid);
        await client.ReceiveAsync(x => LspTestClient.IsLog(x, project + " (Product"));

        await client.ChangeAsync(main, 2, LspTestClient.Range(0, 29, 0, 29, " +"));
        var diagnostics = await client.PublishAsync(main);
        Assert.NotEqual(0, diagnostics.GetArrayLength());
        Assert.Equal(1, diagnostics[0].GetProperty("severity").GetInt32());

        await client.ChangeAsync(main, 3, LspTestClient.Range(0, 29, 0, 31, string.Empty));
        Assert.Equal(0, (await client.PublishAsync(main)).GetArrayLength());
    }

    [Fact]
    public async Task DesynchronizedDocumentsBlockTheirChecksUntilResynchronized()
    {
        this.WriteProject("App", ("main.kimi", Valid));
        var main = this.PathOf("App", "main.kimi");
        await using var client = new LspTestClient();
        await client.InitializeAsync();
        await client.OpenAsync(main, Valid);
        await client.ChangeAsync(main, 2, LspTestClient.Range(9, 0, 9, 0, "x"));
        await client.ReceiveAsync(static x => LspTestClient.IsLog(x, "out of sync"));
        var diagnostics = await client.PublishAsync(main);
        Assert.Contains(diagnostics.EnumerateArray(), static x => x.GetProperty("code").GetString() == "DocumentDesynchronized_Kd");

        await client.ChangeAsync(main, 3, LspTestClient.Full(Valid));
        Assert.Equal(0, (await client.PublishAsync(main)).GetArrayLength());
    }

    [Fact]
    public async Task IgnoredDocumentsAndUnopenedChangesDoNothing()
    {
        var path = Path.Combine(this.directory, "notes.txt");
        await using var client = new LspTestClient();
        await client.InitializeAsync();
        await client.NotifyAsync("textDocument/didOpen", "{\"textDocument\":{\"uri\":\"untitled:Untitled-1\",\"languageId\":\"kimi\",\"version\":1,\"text\":\"x +\"}}");
        await client.OpenAsync(path, "x +");
        await client.ChangeAsync(Path.Combine(this.directory, "Closed.kimi"), 2, LspTestClient.Full("x +"));
        await client.ReceiveAsync(static x => LspTestClient.IsLog(x, "not open"));
        var response = await client.RequestAsync("shutdown");
        Assert.Equal(JsonValueKind.Null, response.GetProperty("result").ValueKind);
        Assert.DoesNotContain(client.Received, static x => x.TryGetProperty("method", out var method) && method.GetString() == "textDocument/publishDiagnostics");
    }

    [Fact]
    public async Task ProjectChecksReportToEveryCheckedSourceAndFollowWatchedChanges()
    {
        var project = this.WriteProject("App", ("main.kimi", Valid));
        var other = this.PathOf("App", "other.kimi");
        File.WriteAllText(other, "public func two() -> i32 => 2 +\n");
        await using var client = new LspTestClient();
        await client.InitializeAsync();
        await client.OpenAsync(this.PathOf("App", "main.kimi"), Valid);
        Assert.NotEqual(0, (await client.PublishAsync(other)).GetArrayLength());
        Assert.Contains(client.Received, x => LspTestClient.IsLog(x, $"{project} (Product {WindowsProfile.Target})"));

        File.WriteAllText(other, "public func two() -> i32 => 2\n");
        await client.NotifyAsync("workspace/didChangeWatchedFiles", $"{{\"changes\":[{{\"uri\":\"{LspTestClient.Uri(other)}\",\"type\":2}}]}}");
        Assert.Equal(0, (await client.PublishAsync(other)).GetArrayLength());
    }

    [Fact]
    public async Task UnsavedNewFilesJoinTheirProject()
    {
        var project = this.WriteProject("App", ("main.kimi", Valid));
        var added = this.PathOf("App", "Added.kimi");
        await using var client = new LspTestClient();
        await client.InitializeAsync();
        await client.OpenAsync(added, Broken.Replace("one", "added", StringComparison.Ordinal));
        Assert.NotEqual(0, (await client.PublishAsync(added)).GetArrayLength());
        Assert.Contains(client.Received, x => LspTestClient.IsLog(x, project + " (Product"));
        Assert.DoesNotContain(client.Received, x => LspTestClient.IsLog(x, added + " (Product"));
    }

    [Fact]
    public async Task SelectedProjectsReportLoadFailuresWithoutOpenDocuments()
    {
        var folder = this.PathOf("Bad");
        Directory.CreateDirectory(folder);
        var project = Path.Combine(folder, "Bad.kimiproj");
        File.WriteAllText(project, "OutputKind=\"Unknown\" Targets={");
        await using var client = new LspTestClient();
        await client.InitializeAsync($"{{\"checkQuietPeriodMs\":0,\"selectedProjects\":[{JsonSerializer.Serialize(project)}]}}");
        var diagnostics = await client.PublishAsync(project);
        Assert.Equal("ProjectLoadFailed_Kd", diagnostics[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task ProjectsWithTestsGetATestUnit()
    {
        var project = this.WriteProject("App", ("main.kimi", Valid + "#Test\nfunc check()\n    $expect(true)\n"));
        await using var client = new LspTestClient();
        await client.InitializeAsync();
        await client.OpenAsync(this.PathOf("App", "main.kimi"), Valid + "#Test\nfunc check()\n    $expect(true)\n");
        await client.ReceiveAsync(x => LspTestClient.IsLog(x, $"{project} (Test {WindowsProfile.Target}): Completed"));
    }

    private string PathOf(params string[] parts) => Path.Combine([this.directory, .. parts]);

    private string WriteProject(string name, (string File, string Text) source, string settings = "")
    {
        var folder = Path.Combine(this.directory, name);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name + ".kimiproj");
        File.WriteAllText(path, $"OutputKind=\"Library\" Targets={{\"{WindowsProfile.Target}\"}} Dependencies={{}} {settings}");
        File.WriteAllText(Path.Combine(folder, source.File), source.Text);
        return path;
    }
}
