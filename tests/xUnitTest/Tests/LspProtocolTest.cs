// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 23.4.1: framing, JSON-RPC responses and the lifecycle.
public sealed class LspProtocolTest
{
    // The start of a JSON \u escape, spelled so that no source escape is involved.
    private const string Escape = "\\" + "u";

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
        ArrayPool<byte>.Shared.Return(one.Value.Buffer);
        ArrayPool<byte>.Shared.Return(two.Value.Buffer);
        Assert.Null(await reader.ReadAsync(token));
    }

    [Theory]
    [InlineData("Content-Length: x\r\n\r\n")]
    [InlineData("Content-Length: 99999999999\r\n\r\n")]
    [InlineData("Content-Length: 10\r\n")]
    [InlineData("Content-Length: 0\r\nContent-Length: 1\r\n\r\nx")]
    [InlineData("Content-Length: +1\r\n\r\nx")]
    [InlineData("Content-Length: 1")]
    [InlineData("Content-Type: application/json\r\n")]
    [InlineData("Content-Type: application/json\r\n\r\n")]
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
    public async Task HeaderBoundAppliesToAllLinesTogether()
    {
        var header = string.Concat(Enumerable.Repeat("X: value\r\n", LspFrameReader.MaxHeaderBytes / 10)) + "Content-Length: 0\r\n\r\n";
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(header));
        var reader = new LspFrameReader(stream);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await reader.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SenderFlushFinishesWhenTheOutputIsClosed()
    {
        using var stream = new MemoryStream();
        stream.Dispose();
        var sender = new LspSender(stream);
        sender.Error(null, -32600, "test");
        await sender.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await sender.DrainAsync();
        await sender.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheSenderWritesEachFrameOnceAndFlushesWhenTheQueueRunsEmpty()
    {
        var token = TestContext.Current.CancellationToken;
        var stream = new GatedStream();
        var sender = new LspSender(stream);
        for (var i = 0; i < 100; i++)
        {
            sender.Error(new RequestId(i, null), -32601, "Method not found: " + i);
        }

        stream.Open();
        await sender.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5), token);
        Assert.Equal(100, stream.Writes);
        Assert.Equal(1, stream.Flushes);

        var reader = new LspFrameReader(new MemoryStream(stream.ToArray()));
        for (var i = 0; i < 100; i++)
        {
            var frame = (await reader.ReadAsync(token))!.Value;
            using var document = JsonDocument.Parse(frame.Buffer.AsMemory(0, frame.Length));
            ArrayPool<byte>.Shared.Return(frame.Buffer);
            Assert.Equal(i, document.RootElement.GetProperty("id").GetInt32());
            Assert.Equal("Method not found: " + i, document.RootElement.GetProperty("error").GetProperty("message").GetString());
        }

        Assert.Null(await reader.ReadAsync(token));
        await sender.DrainAsync();
    }

    [Fact]
    public async Task AValueThatCannotBeSerializedIsDroppedAndOutputContinues()
    {
        var token = TestContext.Current.CancellationToken;
        using var stream = new MemoryStream();
        var sender = new LspSender(stream);
        var options = new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver(), Converters = { new UnwritableConverter() } };
        sender.Notify("test/unwritable", new Unwritable(), (JsonTypeInfo<Unwritable>)options.GetTypeInfo(typeof(Unwritable)));
        sender.Error(null, -32600, "after");
        await sender.DrainAsync();

        var reader = new LspFrameReader(new MemoryStream(stream.ToArray()));
        var frame = (await reader.ReadAsync(token))!.Value;
        using var document = JsonDocument.Parse(frame.Buffer.AsMemory(0, frame.Length));
        ArrayPool<byte>.Shared.Return(frame.Buffer);
        Assert.Equal("after", document.RootElement.GetProperty("error").GetProperty("message").GetString());
        Assert.Null(await reader.ReadAsync(token));
    }

    [Fact]
    public async Task ExternalCancellationCancelsTheRunningCheck()
    {
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = new LspTestClient(configure: session => session.Runner = (_, _, _, _, token) =>
        {
            entered.SetResult(token);
            try
            {
                Task.Delay(Timeout.Infinite, token).GetAwaiter().GetResult();
                return new(CheckOutcome.Completed, true, TestPresence.No, []);
            }
            finally
            {
                finished.SetResult();
            }
        });
        await client.InitializeAsync($"{{\"checkQuietPeriodMs\":0,\"target\":\"{WindowsProfile.Target}\"}}");
        await client.OpenAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "Cancel.kimi"), "let x = 1");
        var token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        client.Cancel();
        Assert.Equal(1, await client.Server.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.True(token.IsCancellationRequested);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
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

    [Theory]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("{}")]
    [InlineData("{\"jsonrpc\":\"1.0\",\"id\":1,\"method\":\"initialize\"}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":true,\"method\":\"initialize\"}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"method\":42}")]
    public async Task InvalidMessagesAreNotParseErrors(string json)
    {
        await using var client = new LspTestClient();
        await client.SendAsync(json);
        var error = await client.ReceiveAsync(static x => x.TryGetProperty("error", out _));
        Assert.Equal(-32600, error.GetProperty("error").GetProperty("code").GetInt32());
        Assert.True((await client.RequestAsync("initialize", "{}")).TryGetProperty("result", out _));
    }

    [Theory]
    [InlineData("textDocument/didChange", "{\"contentChanges\":null}")]
    [InlineData("textDocument/didChange", "{\"contentChanges\":[null]}")]
    [InlineData("textDocument/didChange", "{\"contentChanges\":[{\"text\":null}]}")]
    [InlineData("workspace/didChangeWatchedFiles", "{\"changes\":null}")]
    [InlineData("workspace/didChangeWatchedFiles", "{\"changes\":[null]}")]
    public async Task NullChangesAreLoggedWithoutEndingTheSession(string method, string parameters)
    {
        await using var client = new LspTestClient();
        await client.InitializeAsync();
        await client.NotifyAsync(method, parameters);
        await client.ReceiveAsync(static x => LspTestClient.IsLog(x, "Invalid params"));
        Assert.Equal(JsonValueKind.Null, (await client.RequestAsync("shutdown")).GetProperty("result").ValueKind);
    }

    [Fact]
    public async Task LifecycleNotificationsNeverReceiveResponses()
    {
        await using var client = new LspTestClient();
        await client.NotifyAsync("initialize", "{}");
        Assert.True((await client.InitializeAsync()).TryGetProperty("result", out _));
        await client.NotifyAsync("shutdown", "null");
        var response = await client.RequestAsync("custom/request");
        Assert.Equal(-32601, response.GetProperty("error").GetProperty("code").GetInt32());
        Assert.DoesNotContain(client.Received, static x => x.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Null);
    }

    [Theory]
    [InlineData("initialized")]
    [InlineData("textDocument/didOpen")]
    [InlineData("textDocument/didChange")]
    [InlineData("textDocument/didClose")]
    [InlineData("workspace/didChangeWatchedFiles")]
    [InlineData("exit")]
    public async Task NotificationMethodsRejectRequestIds(string method)
    {
        await using var client = new LspTestClient();
        await client.InitializeAsync();
        var response = await client.RequestAsync(method, "{}");
        Assert.Equal(-32600, response.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(JsonValueKind.Null, (await client.RequestAsync("shutdown")).GetProperty("result").ValueKind);
    }

    [Fact]
    public async Task UnechoableRequestIdsAreInvalidRequestsAndOutputContinues()
    {
        // A string ID holding an escaped lone surrogate cannot be written back; answering it once stopped all output.
        await using var client = new LspTestClient();
        await client.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":\"" + Escape + "d800\",\"method\":\"initialize\",\"params\":{}}");
        var error = await client.ReceiveAsync(static x => x.TryGetProperty("error", out _));
        Assert.Equal(JsonValueKind.Null, error.GetProperty("id").ValueKind);
        Assert.Equal(-32600, error.GetProperty("error").GetProperty("code").GetInt32());
        Assert.True((await client.RequestAsync("initialize", "{}")).TryGetProperty("result", out _));
    }

    [Fact]
    public async Task AnUnexpectedInputFailureEndsTheSession()
    {
        using var output = new MemoryStream();
        var token = TestContext.Current.CancellationToken;
        Assert.Equal(1, await LspServer.Run(new FailingStream(), output, static () => Environment.TickCount64, null, token).WaitAsync(TimeSpan.FromSeconds(5), token));
    }

    [Fact]
    public void MessagesAreReadInOnePassWhateverTheMemberOrder()
    {
        var message = Assert.IsType<LspMessage>(LspMessageReader.Parse("{\"params\":{\"textDocument\":{\"uri\":\"file:///a.kimi\",\"languageId\":\"kimi\",\"version\":3,\"text\":\"a\\n\"}},\"method\":\"textDocument/didOpen\",\"jsonrpc\":\"2.0\"}"u8));
        Assert.Same(LspMethods.DidOpen, message.Method);
        Assert.Null(message.Id);
        Assert.Equal("a\n", Assert.IsType<DidOpenTextDocumentParams>(message.Params).TextDocument.Text);

        var response = Assert.IsType<LspMessage>(LspMessageReader.Parse("{\"jsonrpc\":\"2.0\",\"id\":\"r\",\"result\":[1,{}],\"error\":{\"code\":1}}"u8));
        Assert.Null(response.Method);
        Assert.Equal(new RequestId(0, "r"), response.Id);
        Assert.Equal("{\"code\":1}", response.Error);

        var invalid = Assert.IsType<LspMessage>(LspMessageReader.Parse("{\"jsonrpc\":\"2.0\",\"method\":\"initialize\",\"id\":2,\"params\":{\"capabilities\":5}}"u8));
        Assert.Null(invalid.Params);
        Assert.NotNull(invalid.ParamsError);
    }

    [Fact]
    public void DocumentTextKeepsLoneSurrogatesAndEveryEscape()
    {
        var text = "\"a" + "\\/b\\\"\\\\\\t\\r\\b\\f" + Escape + "d800c" + Escape + "00e9" + Escape + "D83D" + Escape + "DE00\"";
        var json = "{\"jsonrpc\":\"2.0\",\"method\":\"textDocument/didChange\",\"params\":{\"textDocument\":{\"uri\":\"file:///a.kimi\",\"version\":2},\"contentChanges\":[{\"text\":" + text + "}]}}";
        var message = Assert.IsType<LspMessage>(LspMessageReader.Parse(Encoding.UTF8.GetBytes(json)));
        var change = Assert.Single(Assert.IsType<DidChangeTextDocumentParams>(message.Params).ContentChanges);
        Assert.Equal("a/b\"\\\t\r\b\f" + (char)0xD800 + "c" + (char)0xE9 + char.ConvertFromUtf32(0x1F600), change.Text);
    }

    [Fact]
    public async Task InvalidSelectedProjectPathsAreLogged()
    {
        await using var client = new LspTestClient();
        var path = Path.Combine(Path.GetTempPath(), "invalid\0.kimiproj");
        await client.InitializeAsync($"{{\"selectedProjects\":[{JsonSerializer.Serialize(path)}]}}");
        Assert.Contains(client.Received, static x => LspTestClient.IsLog(x, "selectedProjects entries"));
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
        await client.NotifyAsync("initialized", "{}");
        await client.RequestAsync("custom/barrier");
        Assert.Single(client.Received, static x => x.TryGetProperty("method", out var method) && method.GetString() == "client/registerCapability");
    }

    private sealed class Unwritable
    {
    }

    private sealed class UnwritableConverter : JsonConverter<Unwritable>
    {
        public override Unwritable Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, Unwritable value, JsonSerializerOptions options) => throw new InvalidOperationException("The value cannot be written.");
    }

    // Holds every write until it is opened, so the sender's queue fills first; counts writes and flushes.
    private sealed class GatedStream : Stream
    {
        private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly MemoryStream written = new();

        public int Writes { get; private set; }

        public int Flushes { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public void Open() => this.gate.TrySetResult();

        public byte[] ToArray() => this.written.ToArray();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await this.gate.Task;
            this.Writes++;
            this.written.Write(buffer.Span);
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            this.Flushes++;
            return Task.CompletedTask;
        }

        public override void Flush() => this.Flushes++;

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class FailingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("The input failed.");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw new InvalidOperationException("The input failed.");

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
