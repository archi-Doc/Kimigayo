// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// SPEC 23.4.8: the language server advertises code actions only to a client that supports code action literals and versioned
// document changes, answers textDocument/codeAction from the diagnostics it last sent for the URI, and returns a repair
// candidate as a quick fix only while the result that sent it is valid.
public sealed class LspCodeActionTest : IDisposable
{
    private const string Capabilities = "{\"textDocument\":{\"codeAction\":{\"codeActionLiteralSupport\":{\"codeActionKind\":{\"valueSet\":[\"quickfix\"]}}}},\"workspace\":{\"workspaceEdit\":{\"documentChanges\":true}}}";
    private const string Source = "struct Resource\n    public var value: i32\n    public init(value: i32) => self.value = value\n    drop => ()\nfunc consume(value: Resource) => ()\npublic func main()\n    let resource = Resource.init(1)\n    consume(resource)\n";

    private readonly string directory = Path.Combine(Path.GetTempPath(), "kimi-code-action-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(this.directory))
        {
            Directory.Delete(this.directory, true);
        }
    }

    [Theory]
    [InlineData(Capabilities, true)]
    [InlineData("{\"textDocument\":{\"codeAction\":{\"codeActionLiteralSupport\":{}}}}", false)]
    [InlineData("{\"workspace\":{\"workspaceEdit\":{\"documentChanges\":true}}}", false)]
    [InlineData("{}", false)]
    public async Task InitializeAdvertisesCodeActionsOnlyWithBothClientFeatures(string capabilities, bool advertised)
    {
        await using var client = new LspTestClient();
        var response = await client.InitializeAsync(capabilities: capabilities);
        var serverCapabilities = response.GetProperty("result").GetProperty("capabilities");
        if (advertised)
        {
            Assert.Equal("quickfix", Assert.Single(serverCapabilities.GetProperty("codeActionProvider").GetProperty("codeActionKinds").EnumerateArray()).GetString());
        }
        else
        {
            Assert.False(serverCapabilities.TryGetProperty("codeActionProvider", out _));
        }
    }

    [Fact]
    public async Task AQuickFixIsReturnedForTheDiagnosticRangeWhileTheResultIsValid()
    {
        var main = this.WriteProject("App", "main.kimi", Source);
        await using var client = new LspTestClient();
        await client.InitializeAsync("{\"checkQuietPeriodMs\":1000}", Capabilities);
        await client.OpenAsync(main, Source);
        var diagnostics = await client.PublishAsync(main);
        var diagnostic = Assert.Single(diagnostics.EnumerateArray());
        Assert.Equal("TransferRequired_Kd", diagnostic.GetProperty("code").GetString());
        Assert.Equal(7, diagnostic.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
        Assert.Equal(12, diagnostic.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());

        // A cursor inside the diagnostic's range, a range intersecting it, and the whole line match; another line does not.
        var actions = await this.Request(client, main, 7, 15, 7, 15);
        var action = Assert.Single(actions.EnumerateArray());
        Assert.Equal("quickfix", action.GetProperty("kind").GetString());
        Assert.Equal("Append @move to transfer resource to consume; requires the edited operation and every later use of resource satisfy the initialization, Loan and lifetime conditions", action.GetProperty("title").GetString());
        Assert.Equal("TransferRequired_Kd", Assert.Single(action.GetProperty("diagnostics").EnumerateArray()).GetProperty("code").GetString());
        var change = Assert.Single(action.GetProperty("edit").GetProperty("documentChanges").EnumerateArray());
        Assert.Equal(LspTestClient.Uri(main), change.GetProperty("textDocument").GetProperty("uri").GetString());
        Assert.Equal(1, change.GetProperty("textDocument").GetProperty("version").GetInt32());
        var edit = Assert.Single(change.GetProperty("edits").EnumerateArray());
        Assert.Equal("@move", edit.GetProperty("newText").GetString());
        Assert.Equal(20, edit.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
        Assert.Equal(20, edit.GetProperty("range").GetProperty("end").GetProperty("character").GetInt32());
        Assert.Equal(1, (await this.Request(client, main, 7, 0, 8, 0)).GetArrayLength());
        Assert.Equal(1, (await this.Request(client, main, 7, 20, 7, 20)).GetArrayLength());
        Assert.Equal(0, (await this.Request(client, main, 6, 0, 6, 10)).GetArrayLength());
        Assert.Equal(0, (await this.Request(client, main, 7, 15, 7, 15, "\"only\":[\"refactor\"]")).GetArrayLength());
        Assert.Equal(1, (await this.Request(client, main, 7, 15, 7, 15, "\"only\":[\"quickfix\"]")).GetArrayLength());

        // A change marks the input: nothing is returned until the next adoption, which restores the candidates with the new version.
        await client.ChangeAsync(main, 2, LspTestClient.Full(Source));
        Assert.Equal(0, (await this.Request(client, main, 7, 15, 7, 15)).GetArrayLength());
        Assert.Equal(1, (await client.PublishAsync(main)).GetArrayLength());
        actions = await this.Request(client, main, 7, 15, 7, 15);
        Assert.Equal(2, Assert.Single(actions.EnumerateArray()).GetProperty("edit").GetProperty("documentChanges")[0].GetProperty("textDocument").GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task WithoutClientSupportTheRequestReturnsNothing()
    {
        var main = this.WriteProject("App", "main.kimi", Source);
        await using var client = new LspTestClient();
        await client.InitializeAsync();
        await client.OpenAsync(main, Source);
        Assert.Equal(1, (await client.PublishAsync(main)).GetArrayLength());
        Assert.Equal(0, (await this.Request(client, main, 7, 15, 7, 15)).GetArrayLength());
    }

    private async Task<JsonElement> Request(LspTestClient client, string path, int startLine, int startCharacter, int endLine, int endCharacter, string context = "")
    {
        var range = $"{{\"start\":{{\"line\":{startLine},\"character\":{startCharacter}}},\"end\":{{\"line\":{endLine},\"character\":{endCharacter}}}}}";
        var response = await client.RequestAsync("textDocument/codeAction", $"{{\"textDocument\":{{\"uri\":\"{LspTestClient.Uri(path)}\"}},\"range\":{range},\"context\":{{\"diagnostics\":[]{(context.Length == 0 ? string.Empty : "," + context)}}}}}");
        return response.GetProperty("result");
    }

    private string WriteProject(string name, string file, string text)
    {
        var folder = Path.Combine(this.directory, name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name + ".kimiproj"), $"OutputKind=\"Application\" Targets={{\"{WindowsProfile.Target}\"}}");
        var path = Path.Combine(folder, file);
        File.WriteAllText(path, text);
        return path;
    }
}
