// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi.Compiler;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public sealed class LspHoverTest : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "kimi-hover-" + Guid.NewGuid().ToString("N"));

    public LspHoverTest() => Directory.CreateDirectory(this.directory);

    public void Dispose() => Directory.Delete(this.directory, true);

    [Fact]
    public async Task CompilerTypesPropertiesFunctionsAndLinksReachMarkdownResponses()
    {
        const string Text = "/// A [guide](guide.md#usage).\nstruct Sample\n    /// The count.\n    public var count: i32 = 0\n/// Returns the input.\nfunc value(x: i32) -> i32 => x\n";
        var path = this.Write(Text);
        await using var client = new LspTestClient();
        await client.InitializeAsync(capabilities: "{\"textDocument\":{\"hover\":{\"contentFormat\":[\"html\",\"markdown\",\"plaintext\"]}}}");
        await client.OpenAsync(path, Text);
        var type = await WaitFor(client, path, 1, 7, "A ");
        Assert.Equal("markdown", type.GetProperty("contents").GetProperty("kind").GetString());
        var html = Markdig.Markdown.ToHtml(Body(type));
        Assert.Contains("struct Sample", html);
        Assert.Contains("Copy: No", html);
        Assert.Contains("href=\"" + new Uri(Path.Combine(this.directory, "guide.md")).AbsoluteUri + "#usage\"", html);
        var property = Markdig.Markdown.ToHtml(Body(await Request(client, path, 3, 15)));
        Assert.Contains("The count.", property);
        Assert.Contains("var count: i32", property);
        Assert.DoesNotContain("= 0", property);
        var function = Markdig.Markdown.ToHtml(Body(await Request(client, path, 5, 6)));
        Assert.Contains("Returns the input.", function);
        Assert.Contains("func value", function);
        Assert.DoesNotContain("=&gt; x", function);
        Assert.Equal(JsonValueKind.Null, (await Request(client, path, 0, 6)).ValueKind);
    }

    [Fact]
    public async Task HoverUsesTheDiagnosticQuietPeriodAndUpdatesWithUnchangedEmptyDiagnostics()
    {
        const string Text = "/// Original description.\nstruct Sample\n";
        var path = this.Write(Text);
        long now = 0;
        await using var client = new LspTestClient(clock: () => Interlocked.Read(ref now));
        await client.InitializeAsync("{\"checkQuietPeriodMs\":1000}");
        await client.OpenAsync(path, Text);
        Assert.Equal(JsonValueKind.Null, (await Request(client, path, 1, 8)).ValueKind);
        Interlocked.Exchange(ref now, 1000);
        await client.RequestAsync("custom/wake");
        var original = Body(await WaitFor(client, path, 1, 8, "Original description."));
        await client.ChangeAsync(path, 2, LspTestClient.Range(0, 4, 0, 12, "Updated"));
        Assert.Equal(HoverRenderer.PreviousNotice + "\n\n" + original, Body(await Request(client, path, 1, 8)));
        Interlocked.Exchange(ref now, 1999);
        Assert.StartsWith(HoverRenderer.PreviousNotice, Body(await Request(client, path, 1, 8)));
        Interlocked.Exchange(ref now, 2000);
        await client.RequestAsync("custom/wake");
        var updated = Body(await WaitFor(client, path, 1, 8, "Updated description."));
        Assert.DoesNotContain(HoverRenderer.PreviousNotice, updated);
        Assert.DoesNotContain(client.Received, x => LspTestClient.IsPublish(x, LspTestClient.Uri(path)));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"textDocument\":{\"hover\":{\"contentFormat\":[]}}}")]
    [InlineData("{\"textDocument\":{\"hover\":{\"contentFormat\":[\"html\"]}}}")]
    [InlineData("{\"textDocument\":{\"hover\":{\"contentFormat\":[\"plaintext\",\"markdown\"]}}}")]
    public async Task AbsentUnsupportedAndPreferredPlaintextFormatsUsePlaintext(string capabilities)
    {
        const string Text = "/// Description.\nstruct Sample\n";
        var path = this.Write(Text);
        await using var client = new LspTestClient();
        await client.InitializeAsync(capabilities: capabilities);
        await client.OpenAsync(path, Text);
        var result = await WaitFor(client, path, 1, 8, "Description.");
        Assert.Equal("plaintext", result.GetProperty("contents").GetProperty("kind").GetString());
        Assert.DoesNotContain("```", Body(result));
    }

    [Theory]
    [InlineData("markdown")]
    [InlineData("plaintext")]
    public async Task VariableCompositionAndOperationDescriptionsReachExactProtocolRanges(string format)
    {
        const string Text = "/// The counter.\nstruct Counter\nfunc use(counter: ref/Counter)\n    _ = counter@copy\n";
        var path = this.Write(Text);
        await using var client = new LspTestClient();
        await client.InitializeAsync(capabilities: "{\"textDocument\":{\"hover\":{\"contentFormat\":[\"" + format + "\"]}}}");
        await client.OpenAsync(path, Text);
        var parameter = await WaitFor(client, path, 2, 10, "counter: ref/Counter");
        var body = format == "markdown" ? Markdig.Markdown.ToHtml(Body(parameter)) : Body(parameter);
        Assert.Contains("counter: ref/Counter during ", body);
        Assert.Contains("Core: Counter", body);
        Assert.Contains("The counter.", body);
        Assert.Equal(format, parameter.GetProperty("contents").GetProperty("kind").GetString());
        var at = await Request(client, path, 3, 15);
        var name = await Request(client, path, 3, 16);
        Assert.Equal(Body(at), Body(name));
        Assert.Contains("Copies the operand", Body(at));
        Assert.Equal(15, at.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
        Assert.Equal(16, at.GetProperty("range").GetProperty("end").GetProperty("character").GetInt32());
        Assert.Equal(16, name.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
        Assert.Equal(20, name.GetProperty("range").GetProperty("end").GetProperty("character").GetInt32());
    }

    private static string Body(JsonElement result) => result.GetProperty("contents").GetProperty("value").GetString()!;

    private static async Task<JsonElement> Request(LspTestClient client, string path, int line, int character)
    {
        var response = await client.RequestAsync("textDocument/hover", $"{{\"textDocument\":{{\"uri\":\"{LspTestClient.Uri(path)}\"}},\"position\":{{\"line\":{line},\"character\":{character}}}}}");
        return response.GetProperty("result");
    }

    private static async Task<JsonElement> WaitFor(LspTestClient client, string path, int line, int character, string expected)
    {
        JsonElement result = default;
        for (var i = 0; i < 500; i++)
        {
            result = await Request(client, path, line, character);
            if (result.ValueKind != JsonValueKind.Null && Body(result).Contains(expected, StringComparison.Ordinal))
            {
                return result;
            }

            await Task.Delay(20, client.Token);
        }

        Assert.Fail("Expected Hover containing " + expected + "; last response: " + result);
        return result;
    }

    private string Write(string text)
    {
        File.WriteAllText(Path.Combine(this.directory, "App.kimiproj"), $"OutputKind=\"Library\" Targets={{\"{WindowsProfile.Target}\"}}");
        var path = Path.Combine(this.directory, "main.kimi");
        File.WriteAllText(path, text);
        return path;
    }
}
