// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using System.Text.Json;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 23.4.1: framing, JSON-RPC responses and the lifecycle.
public sealed class LspProtocolTest
{
    [Fact]
    public async Task InitializeAdvertisesOnlyTheImplementedCapabilities()
    {
        await using var client = new LspTestClient();
        var response = await client.RequestAsync("initialize", "{\"capabilities\":{}}");
        var capabilities = response.GetProperty("result").GetProperty("capabilities");
        Assert.Equal("utf-16", capabilities.GetProperty("positionEncoding").GetString());
        Assert.True(capabilities.GetProperty("textDocumentSync").GetProperty("openClose").GetBoolean());
        Assert.Equal(2, capabilities.GetProperty("textDocumentSync").GetProperty("change").GetInt32());
        Assert.Equal(2, capabilities.EnumerateObject().Count());
        Assert.Equal("Kimi Language Server", response.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());
    }

    [Fact]
    public async Task FramesSplitAcrossReadsWithMultibyteBodiesAreRead()
    {
        await using var client = new LspTestClient();
        var body = Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"日本語 \U0001F600\"}}}");
        var frame = Encoding.ASCII.GetBytes($"content-length: {body.Length}\r\nContent-Type: application/vscode-jsonrpc; charset=utf-8\r\n\r\n").Concat(body).ToArray();
        for (var offset = 0; offset < frame.Length; offset += 5)
        {
            await client.WriteAsync(frame.AsMemory(offset, Math.Min(5, frame.Length - offset)));
            await Task.Yield();
        }

        var response = await client.ReceiveAsync(static x => x.TryGetProperty("id", out _));
        Assert.Equal(7, response.GetProperty("id").GetInt32());
        Assert.True(response.TryGetProperty("result", out _));
    }

    [Fact]
    public async Task FrameReaderHonorsContentLengthInBytes()
    {
        var first = LspTestClient.Frame("{\"a\":\"é\U0001F600\"}");
        var second = LspTestClient.Frame("{\"b\":1}");
        using var stream = new MemoryStream([.. first, .. second]);
        var reader = new LspFrameReader(stream);
        var token = TestContext.Current.CancellationToken;
        var one = await reader.ReadAsync(token);
        Assert.Equal("{\"a\":\"é\U0001F600\"}", Encoding.UTF8.GetString(one!.Value.Buffer, 0, one.Value.Length));
        var two = await reader.ReadAsync(token);
        Assert.Equal("{\"b\":1}", Encoding.UTF8.GetString(two!.Value.Buffer, 0, two.Value.Length));
        Assert.Null(await reader.ReadAsync(token));
    }

    [Theory]
    [InlineData("Content-Length: x\r\n\r\n")]
    [InlineData("Content-Length: 99999999999\r\n\r\n")]
    [InlineData("Content-Length: 10\r\n")]
    public async Task MalformedHeadersAreRejected(string header)
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(header));
        var reader = new LspFrameReader(stream);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await reader.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OversizedHeadersAreRejected()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("X-Filler: " + new string('a', LspFrameReader.MaxHeaderBytes) + "\r\n"));
        var reader = new LspFrameReader(stream);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await reader.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShutdownAnswersNullAndExitReturnsZero()
    {
        await using var client = new LspTestClient();
        await client.InitializeAsync();
        var response = await client.RequestAsync("shutdown");
        Assert.Equal(JsonValueKind.Null, response.GetProperty("result").ValueKind);
        Assert.False(response.TryGetProperty("error", out _));

        var after = await client.RequestAsync("custom/after");
        Assert.Equal(-32600, after.GetProperty("error").GetProperty("code").GetInt32());

        await client.NotifyAsync("exit", "null");
        Assert.Equal(0, await client.Server);
    }

    [Fact]
    public async Task ExitWithoutShutdownReturnsOne()
    {
        await using var client = new LspTestClient();
        await client.InitializeAsync();
        await client.NotifyAsync("exit", "null");
        Assert.Equal(1, await client.Server);
    }

    [Fact]
    public async Task EndOfInputActsAsExit()
    {
        await using var client = new LspTestClient();
        await client.InitializeAsync();
        client.CloseInput();
        Assert.Equal(1, await client.Server);
    }

    [Fact]
    public async Task UnknownRequestsFailAndUnknownNotificationsAreIgnored()
    {
        await using var client = new LspTestClient();
        await client.InitializeAsync();
        await client.NotifyAsync("custom/notification", "{}");
        await client.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":\"abc\",\"method\":\"custom/request\"}");
        var response = await client.ReceiveAsync(static x => x.TryGetProperty("id", out _));
        Assert.Equal("abc", response.GetProperty("id").GetString());
        Assert.Equal(-32601, response.GetProperty("error").GetProperty("code").GetInt32());
        Assert.False(response.TryGetProperty("result", out _));
        Assert.Single(client.Received, static x => x.TryGetProperty("id", out _) && x.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task RequestsBeforeInitializeAreRejected()
    {
        await using var client = new LspTestClient();
        var response = await client.RequestAsync("custom/request");
        Assert.Equal(-32002, response.GetProperty("error").GetProperty("code").GetInt32());
        var initialize = await client.RequestAsync("initialize", "{}");
        Assert.True(initialize.TryGetProperty("result", out _));
    }

    [Fact]
    public async Task MalformedBodiesAreParseErrorsAndTheSessionContinues()
    {
        await using var client = new LspTestClient();
        await client.SendAsync("{not json");
        var error = await client.ReceiveAsync(static x => x.TryGetProperty("error", out _));
        Assert.Equal(JsonValueKind.Null, error.GetProperty("id").ValueKind);
        Assert.Equal(-32700, error.GetProperty("error").GetProperty("code").GetInt32());
        var initialize = await client.RequestAsync("initialize", "{}");
        Assert.True(initialize.TryGetProperty("result", out _));
    }

    [Fact]
    public async Task InvalidSettingsAreLoggedAndReplacedByDefaults()
    {
        await using var client = new LspTestClient();
        await client.InitializeAsync("{\"checkQuietPeriodMs\":-1,\"unknownOption\":true,\"debug\":\"yes\"}");
        Assert.Contains(client.Received, static x => LspTestClient.IsLog(x, "checkQuietPeriodMs"));
        Assert.Contains(client.Received, static x => LspTestClient.IsLog(x, "unknownOption"));
        Assert.Contains(client.Received, static x => LspTestClient.IsLog(x, "debug must be a Boolean"));
    }

    [Fact]
    public async Task WatchersAreRegisteredWhenTheClientSupportsDynamicRegistration()
    {
        await using var client = new LspTestClient();
        await client.InitializeAsync(capabilities: "{\"workspace\":{\"didChangeWatchedFiles\":{\"dynamicRegistration\":true}}}");
        var request = await client.ReceiveAsync(static x => x.TryGetProperty("method", out var method) && method.GetString() == "client/registerCapability");
        var registration = request.GetProperty("params").GetProperty("registrations")[0];
        Assert.Equal("workspace/didChangeWatchedFiles", registration.GetProperty("method").GetString());
        var patterns = registration.GetProperty("registerOptions").GetProperty("watchers").EnumerateArray().Select(static x => x.GetProperty("globPattern").GetString()).ToArray();
        Assert.Equal(["**/*.kimi", "**/*.kimiproj", "**/*.kimi.lock.json"], patterns);
    }
}
