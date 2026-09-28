// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

/// <summary>An in-process LSP client over pipes: it frames requests and reads the server's frames in order.</summary>
internal sealed class LspTestClient : IAsyncDisposable
{
    private static readonly PipeOptions Unbounded = new(pauseWriterThreshold: 0, resumeWriterThreshold: 0);

    private readonly Pipe toServer = new(Unbounded);
    private readonly Pipe fromServer = new(Unbounded);
    private readonly LspFrameReader reader;
    private readonly CancellationTokenSource timeout;
    private int nextId;

    public LspTestClient(Func<long>? clock = null, Action<LspSession>? configure = null)
    {
        this.timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        this.timeout.CancelAfter(TimeSpan.FromSeconds(120));
        this.Server = LspServer.Run(this.toServer.Reader.AsStream(), this.fromServer.Writer.AsStream(), clock ?? (static () => Environment.TickCount64), configure, this.timeout.Token);
        this.reader = new(this.fromServer.Reader.AsStream());
    }

    /// <summary>Gets the server task, whose result is the exit code.</summary>
    public Task<int> Server { get; }

    /// <summary>Gets every frame read so far, in order.</summary>
    public List<JsonElement> Received { get; } = [];

    public CancellationToken Token => this.timeout.Token;

    public static byte[] Frame(string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        return [.. Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"), .. body];
    }

    public static string Uri(string path) => new Uri(path).AbsoluteUri;

    public static bool IsPublish(JsonElement message, string uri)
        => message.TryGetProperty("method", out var method) && method.GetString() == "textDocument/publishDiagnostics" &&
            string.Equals(message.GetProperty("params").GetProperty("uri").GetString(), uri, StringComparison.OrdinalIgnoreCase);

    public static bool IsLog(JsonElement message, string text)
        => message.TryGetProperty("method", out var method) && method.GetString() == "window/logMessage" &&
            message.GetProperty("params").GetProperty("message").GetString()!.Contains(text, StringComparison.Ordinal);

    public static string Full(string text) => $"{{\"text\":{JsonSerializer.Serialize(text)}}}";

    public static string Range(int startLine, int startCharacter, int endLine, int endCharacter, string text)
        => $"{{\"range\":{{\"start\":{{\"line\":{startLine},\"character\":{startCharacter}}},\"end\":{{\"line\":{endLine},\"character\":{endCharacter}}}}},\"text\":{JsonSerializer.Serialize(text)}}}";

    public async Task WriteAsync(ReadOnlyMemory<byte> bytes)
    {
        await this.toServer.Writer.WriteAsync(bytes, this.Token);
    }

    public Task SendAsync(string json) => this.WriteAsync(Frame(json));

    public Task NotifyAsync(string method, string parameters)
        => this.SendAsync($"{{\"jsonrpc\":\"2.0\",\"method\":\"{method}\",\"params\":{parameters}}}");

    public async Task<JsonElement> RequestAsync(string method, string parameters = "null")
    {
        var id = ++this.nextId;
        await this.SendAsync($"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"{method}\",\"params\":{parameters}}}");
        return await this.ReceiveAsync(x => x.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.Number && value.GetInt32() == id);
    }

    /// <summary>Initializes the session; the quiet period defaults to zero so each event checks at once.</summary>
    /// <param name="options">The <c>initializationOptions</c> JSON.</param>
    /// <param name="capabilities">The client capabilities JSON.</param>
    /// <returns>The initialize response.</returns>
    public async Task<JsonElement> InitializeAsync(string options = "{\"checkQuietPeriodMs\":0}", string capabilities = "{}")
    {
        var response = await this.RequestAsync("initialize", $"{{\"capabilities\":{capabilities},\"initializationOptions\":{options}}}");
        await this.NotifyAsync("initialized", "{}");
        return response;
    }

    public Task OpenAsync(string path, string text, int version = 1)
        => this.NotifyAsync("textDocument/didOpen", $"{{\"textDocument\":{{\"uri\":\"{Uri(path)}\",\"languageId\":\"kimi\",\"version\":{version},\"text\":{JsonSerializer.Serialize(text)}}}}}");

    public Task ChangeAsync(string path, int version, params string[] changes)
        => this.NotifyAsync("textDocument/didChange", $"{{\"textDocument\":{{\"uri\":\"{Uri(path)}\",\"version\":{version}}},\"contentChanges\":[{string.Join(',', changes)}]}}");

    public Task CloseAsync(string path)
        => this.NotifyAsync("textDocument/didClose", $"{{\"textDocument\":{{\"uri\":\"{Uri(path)}\"}}}}");

    /// <summary>Reads frames until one satisfies the predicate.</summary>
    /// <param name="predicate">The condition.</param>
    /// <returns>The matching frame.</returns>
    public async Task<JsonElement> ReceiveAsync(Func<JsonElement, bool> predicate)
    {
        while (true)
        {
            var frame = await this.reader.ReadAsync(this.Token) ?? throw new EndOfStreamException("The server output ended.");
            JsonElement message;
            try
            {
                using var document = JsonDocument.Parse(frame.Buffer.AsMemory(0, frame.Length));
                message = document.RootElement.Clone();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(frame.Buffer);
            }

            this.Received.Add(message);
            if (predicate(message))
            {
                return message;
            }
        }
    }

    /// <summary>Reads until the next diagnostics notification for a file and returns its diagnostics.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The diagnostics array.</returns>
    public async Task<JsonElement> PublishAsync(string path)
    {
        var uri = Uri(path);
        return (await this.ReceiveAsync(x => IsPublish(x, uri))).GetProperty("params").GetProperty("diagnostics");
    }

    public void CloseInput() => this.toServer.Writer.Complete();

    public void Cancel() => this.timeout.Cancel();

    public async ValueTask DisposeAsync()
    {
        this.toServer.Writer.Complete();
        try
        {
            await this.Server;
        }
        catch (OperationCanceledException)
        {
        }

        this.fromServer.Reader.Complete();
        this.timeout.Dispose();
    }
}
