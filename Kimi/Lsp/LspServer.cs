// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Buffers.Text;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arc.Unit;

namespace Kimigayo.Lsp;

public class LspServer
{
    #region FieldAndProperty

    private readonly Stream input;
    private readonly Stream output;
    private readonly SemaphoreSlim writeLock;
    private readonly byte[] contentHeader;
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly Dictionary<string, TomlDocumentState> documents = new(StringComparer.Ordinal);
    private bool shutdownRequested;

    #endregion

    public LspServer()
    {
        this.input = Console.OpenStandardInput();
        this.output = Console.OpenStandardOutput();
        this.writeLock = new(1, 1);
        this.contentHeader = Encoding.UTF8.GetBytes("content-length: ");
    }

    public async Task Run(CancellationToken cancellationToken)
    {
        var buffer = new byte[32];
        int length = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            // 'Content-Length: '
            var r = await ReadLine().ConfigureAwait(false);
            if (r.TextLength < this.contentHeader.Length)
            {
                break;
            }

            for (var i = 0; i < r.TextLength; i++)
            {// To lower
                if ((uint)(buffer[i] - 'A') <= 'Z' - 'A')
                {
                    buffer[i] += 0x20;
                }
            }

            if (!buffer.AsSpan(0, this.contentHeader.Length).SequenceEqual(this.contentHeader))
            {// Not 'Content-Length'
                break;
            }

            Utf8Parser.TryParse(buffer.AsSpan(this.contentHeader.Length, r.TextLength - this.contentHeader.Length), out int contentLength, out _);

            do
            {// Skip 'Content-Type: '
                MoveBuffer(r.LineLength);
                r = await ReadLine().ConfigureAwait(false);
            }
            while (r.TextLength > 0);

            var payload = ArrayPool<byte>.Shared.Rent(contentLength);
            var remaining = contentLength;
            try
            {
                MoveBuffer(r.LineLength);
                if (length > 0)
                {
                    var size = Math.Min(length, remaining);
                    buffer.AsSpan(0, size).CopyTo(payload);
                    remaining -= size;
                    MoveBuffer(size);
                }

                await this.input.ReadExactlyAsync(payload.AsMemory(contentLength - remaining, remaining), cancellationToken).ConfigureAwait(false);

                var span = payload.AsSpan(0, contentLength);
                File.AppendAllBytes("C:\\App\\lsp.txt", span);//

                var message = JsonSerializer.Deserialize<LspMessage>(span, this.jsonOptions);
                if (message is null)
                {
                    break;
                }

                await this.HandleMessage(message).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        void MoveBuffer(int nextPosition)
        {
            if (length > nextPosition)
            {
                buffer.AsSpan(nextPosition, length - nextPosition).CopyTo(buffer);
            }

            length -= nextPosition;
        }

        async Task<(int TextLength, int LineLength)> ReadLine()
        {
            while (true)
            {
                var span = buffer.AsSpan(0, length);
                var idx = span.IndexOf(LspHelper.Lf);
                if (idx >= 0)
                {
                    int textLength;
                    int lineLength;

                    if (idx > 0 && span[idx - 1] == LspHelper.Cr)
                    {// CrLf
                        textLength = idx - 1;
                        lineLength = idx + 1;
                    }
                    else
                    {// Lf
                        textLength = idx;
                        lineLength = idx + 1;
                    }

                    return (textLength, lineLength);
                }

                if (length >= buffer.Length)
                {
                    return default;
                }
                else
                {
                    var read = await this.input.ReadAsync(buffer.AsMemory(length, buffer.Length - length)).ConfigureAwait(false);
                    if (read == 0)
                    {
                        return default;
                    }

                    length += read;
                }
            }
        }
    }

    private async Task HandleMessage(LspMessage message)
    {
        switch (message.Method)
        {
            case "initialize":
                await this.HandleInitializeAsync(message.Id).ConfigureAwait(false);
                break;

            case "initialized":
                break;

            case "shutdown":
                this.shutdownRequested = true;
                await this.SendResponseAsync(message.Id, null).ConfigureAwait(false);
                break;

            case "exit":
                Environment.Exit(this.shutdownRequested ? 0 : 1);
                break;

            case "textDocument/didOpen":
                await this.HandleDidOpenAsync(message.Params).ConfigureAwait(false);
                break;

            case "textDocument/didChange":
                await this.HandleDidChangeAsync(message.Params).ConfigureAwait(false);
                break;

            case "textDocument/didClose":
                await this.HandleDidCloseAsync(message.Params).ConfigureAwait(false);
                break;

            default:
                if (message.Id is not null)
                {
                    await this.SendErrorAsync(
                        message.Id,
                        -32601,
                        $"Method not found: {message.Method}").ConfigureAwait(false);
                }

                break;
        }
    }

    private async Task HandleInitializeAsync(JsonElement? id)
    {
        var response = new InitializeResult
        {
            Capabilities = new ServerCapabilities
            {
                TextDocumentSync = new TextDocumentSyncOptions
                {
                    OpenClose = true,

                    // 2 = Incremental
                    Change = 2,
                },
            },
            ServerInfo = new ServerInfo
            {
                Name = "Kimi Language Server",
                Version = "0.0.1",
            },
        };

        await this.SendResponseAsync(id, response).ConfigureAwait(false);
    }

    private async Task HandleDidOpenAsync(JsonElement? parametersElement)
    {
        if (parametersElement is null)
        {
            return;
        }

        var parameters = parametersElement.Value.Deserialize<DidOpenTextDocumentParams>(this.jsonOptions);
        if (parameters?.TextDocument is null)
        {
            return;
        }

        var doc = parameters.TextDocument;

        var state = new TomlDocumentState(doc.Uri);
        state.Open(doc.Text ?? string.Empty, doc.Version);

        this.documents[doc.Uri] = state;

        await this.PublishDiagnosticsAsync(state).ConfigureAwait(false);
    }

    private async Task HandleDidChangeAsync(JsonElement? parametersElement)
    {
        if (parametersElement is null)
        {
            return;
        }

        var parameters = parametersElement.Value.Deserialize<DidChangeTextDocumentParams>(this.jsonOptions);
        if (parameters?.TextDocument is null)
        {
            return;
        }

        var uri = parameters.TextDocument.Uri;
        var version = parameters.TextDocument.Version;

        if (!this.documents.TryGetValue(uri, out var state))
        {
            return;
        }

        foreach (var change in parameters.ContentChanges)
        {
            if (change.Range is null)
            {
                state.Open(change.Text ?? string.Empty, version);
                continue;
            }

            var textChange = new TextChange(
                change.Range.Start.Line,
                change.Range.Start.Character,
                change.Range.End.Line,
                change.Range.End.Character,
                change.Text ?? string.Empty);

            state.ApplyChange(textChange, version);
        }

        await this.PublishDiagnosticsAsync(state).ConfigureAwait(false);
    }

    private async Task HandleDidCloseAsync(JsonElement? parametersElement)
    {
        if (parametersElement is null)
        {
            return;
        }

        var parameters = parametersElement.Value.Deserialize<DidCloseTextDocumentParams>(this.jsonOptions);
        if (parameters?.TextDocument is null)
        {
            return;
        }

        var uri = parameters.TextDocument.Uri;

        this.documents.Remove(uri);

        await this.PublishDiagnosticsAsync(uri, null, []).ConfigureAwait(false);
    }

    private async Task PublishDiagnosticsAsync(TomlDocumentState state)
    {
        var diagnostics = SimpleTomlLinter.Lint(state.Lines);
        await this.PublishDiagnosticsAsync(state.Uri, state.Version, diagnostics).ConfigureAwait(false);
    }

    private async Task PublishDiagnosticsAsync(
        string uri,
        int? version,
        IReadOnlyList<TomlDiagnostic> diagnostics)
    {
        var lspDiagnostics = new List<Diagnostic>(diagnostics.Count);

        foreach (var diagnostic in diagnostics)
        {
            lspDiagnostics.Add(new Diagnostic
            {
                Range = new Range
                {
                    Start = new Position
                    {
                        Line = diagnostic.Line,
                        Character = diagnostic.Character,
                    },
                    End = new Position
                    {
                        Line = diagnostic.Line,
                        Character = diagnostic.Character + Math.Max(1, diagnostic.Length),
                    },
                },
                Severity = LspHelper.ToLspSeverity(diagnostic.Severity),
                Source = "simple-toml-lsp",
                Message = diagnostic.Message,
            });
        }

        var parameters = new PublishDiagnosticsParams
        {
            Uri = uri,
            Version = version,
            Diagnostics = lspDiagnostics,
        };

        await this.SendNotificationAsync("textDocument/publishDiagnostics", parameters).ConfigureAwait(false);
    }

    private async Task SendResponseAsync(JsonElement? id, object? result)
    {
        var response = new JsonRpcResponse
        {
            Id = id,
            Result = result,
        };

        await this.SendJsonAsync(response).ConfigureAwait(false);
    }

    private async Task SendNotificationAsync(string method, object? parameters)
    {
        var notification = new JsonRpcNotification
        {
            Method = method,
            Params = parameters,
        };

        await this.SendJsonAsync(notification).ConfigureAwait(false);
    }

    private async Task SendErrorAsync(JsonElement? id, int code, string message)
    {
        var response = new JsonRpcResponse
        {
            Id = id,
            Error = new JsonRpcError
            {
                Code = code,
                Message = message,
            },
        };

        await this.SendJsonAsync(response).ConfigureAwait(false);
    }

    private async Task SendJsonAsync<T>(T value)
    {
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(value, this.jsonOptions);
        var headerBytes = Encoding.ASCII.GetBytes($"Content-Length: {jsonBytes.Length}\r\n\r\n");

        await this.writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await this.output.WriteAsync(headerBytes).ConfigureAwait(false);
            await this.output.WriteAsync(jsonBytes).ConfigureAwait(false);
            await this.output.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            this.writeLock.Release();
        }
    }
}
