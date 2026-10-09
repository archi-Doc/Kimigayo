// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi;
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
        await client.InitializeAsync($"{{\"checkQuietPeriodMs\":0,\"target\":\"{WindowsProfile.Target}\"}}");
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

    [Theory]
    [MemberData(nameof(ContextualLabelTest.DiagnosticCases), MemberType = typeof(ContextualLabelTest))]
    public async Task ContextualSyntaxPublishesItsRangeAndClearsAfterRepair(string source, string valid, string code, string token)
    {
        var path = Path.Combine(this.directory, "Labels.kimi");
        await using var client = new LspTestClient();
        await client.InitializeAsync($"{{\"checkQuietPeriodMs\":0,\"target\":\"{WindowsProfile.Target}\"}}");
        await client.OpenAsync(path, source);
        var diagnostics = await client.PublishAsync(path);
        var error = Assert.Single(diagnostics.EnumerateArray(), x => x.GetProperty("severity").GetInt32() == 1);
        Assert.Equal(code, error.GetProperty("code").GetString());
        var start = token.Length == 0 ? source.Length : source.LastIndexOf(token, StringComparison.Ordinal);
        var range = error.GetProperty("range");
        Assert.Equal(0, range.GetProperty("start").GetProperty("line").GetInt32());
        Assert.Equal(start, range.GetProperty("start").GetProperty("character").GetInt32());
        Assert.Equal(start + token.Length, range.GetProperty("end").GetProperty("character").GetInt32());
        await client.ChangeAsync(path, 2, LspTestClient.Full(valid));
        Assert.DoesNotContain((await client.PublishAsync(path)).EnumerateArray(), x => x.GetProperty("severity").GetInt32() == 1);
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
    public async Task LoneSurrogatesInDocumentTextAreEncodingDiagnostics()
    {
        // JSON escapes a lone surrogate; the change applies and the source reports it instead of dropping the change.
        this.WriteProject("App", ("main.kimi", Valid));
        var main = this.PathOf("App", "main.kimi");
        await using var client = new LspTestClient();
        await client.InitializeAsync();
        await client.OpenAsync(main, Valid);
        await client.ChangeAsync(main, 2, "{\"range\":{\"start\":{\"line\":0,\"character\":0},\"end\":{\"line\":0,\"character\":0}},\"text\":\"// \\" + "ud800\\n\"}");
        var diagnostics = await client.PublishAsync(main);
        Assert.Contains(diagnostics.EnumerateArray(), static x => x.GetProperty("code").GetString() == "InvalidSourceEncoding_Kd");

        await client.ChangeAsync(main, 3, LspTestClient.Range(0, 0, 1, 0, string.Empty));
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

    [Fact]
    public async Task SeveralTargetsWithoutTheHostAskForASelection()
    {
        var project = this.WriteProject("App", ("main.kimi", Valid), "Targets={\"x86_64-unknown-linux-gnu\", \"aarch64-unknown-linux-gnu\"}");
        await using var client = new LspTestClient();
        await client.InitializeAsync();
        await client.OpenAsync(this.PathOf("App", "main.kimi"), Valid);
        var diagnostics = await client.PublishAsync(project);
        Assert.Equal("TargetSelectionRequired_Kd", diagnostics[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task SessionTargetsSelectTheProductUnits()
    {
        var project = this.WriteProject("App", ("main.kimi", Valid), $"Targets={{\"{WindowsProfile.Target}\", \"x86_64-unknown-linux-gnu\"}}");
        await using (var client = new LspTestClient())
        {
            await client.InitializeAsync("{\"checkQuietPeriodMs\":0,\"allTargets\":true}");
            await client.OpenAsync(this.PathOf("App", "main.kimi"), Valid);
            await client.ReceiveAsync(x => LspTestClient.IsLog(x, $"{project} (Product {WindowsProfile.Target}): Completed"));
            await client.ReceiveAsync(x => LspTestClient.IsLog(x, $"{project} (Product x86_64-unknown-linux-gnu): Completed"));
        }

        await using (var client = new LspTestClient())
        {
            await client.InitializeAsync("{\"checkQuietPeriodMs\":0,\"target\":\"x86_64-unknown-linux-gnu\"}");
            await client.OpenAsync(this.PathOf("App", "main.kimi"), Broken);
            Assert.NotEqual(0, (await client.PublishAsync(this.PathOf("App", "main.kimi"))).GetArrayLength());
            Assert.Contains(client.Received, x => LspTestClient.IsLog(x, $"{project} (Product x86_64-unknown-linux-gnu)"));
            Assert.DoesNotContain(client.Received, x => LspTestClient.IsLog(x, $"{project} (Product {WindowsProfile.Target})"));
        }
    }

    [Fact]
    public async Task DependencyDiagnosticsAreReportedByTheirConsumers()
    {
        var library = this.WriteProject("Lib", ("main.kimi", "public func two() -> i32 => 2 +\n"), "PackageId=\"lib\" PackageVersion=\"1\"");
        var project = this.WriteProject("App", ("main.kimi", Valid), string.Empty, "Lib={PackageId=\"lib\" PackageVersion=\"1\" Project=\"../Lib/Lib.kimiproj\"}");
        var resolution = DependencyResolver.Resolve(project, WindowsProfile.Target, Compilation.CurrentLanguageVersion, TestContext.Current.CancellationToken);
        DependencyLock.Update(DependencyLock.PathForProject(project), resolution, TestContext.Current.CancellationToken); // A check never restores.
        await using var client = new LspTestClient();
        await client.InitializeAsync();
        await client.OpenAsync(this.PathOf("App", "main.kimi"), Valid);
        Assert.NotEqual(0, (await client.PublishAsync(this.PathOf("Lib", "main.kimi"))).GetArrayLength());
        Assert.Contains(client.Received, x => LspTestClient.IsLog(x, $"{project} (Product"));
        Assert.DoesNotContain(client.Received, x => LspTestClient.IsLog(x, $"{library} (Product"));
    }

    private string PathOf(params string[] parts) => Path.Combine([this.directory, .. parts]);

    private string WriteProject(string name, (string File, string Text) source, string settings = "", string dependencies = "")
    {
        var folder = Path.Combine(this.directory, name);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name + ".kimiproj");
        var targets = settings.Contains("Targets=", StringComparison.Ordinal) ? string.Empty : $"Targets={{\"{WindowsProfile.Target}\"}} ";
        File.WriteAllText(path, $"OutputKind=\"Library\" {targets}Dependencies={{{dependencies}}} {settings}");
        File.WriteAllText(Path.Combine(folder, source.File), source.Text);
        return path;
    }
}
