// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kimigayo.Lsp;

internal static class LspServer
{
    private const byte Cr = (byte)'\r';
    private const byte Lf = (byte)'\n';

    private static readonly Stream Input;
    private static readonly Stream Output;
    private static readonly SemaphoreSlim WriteLock;
    private static readonly byte[] ContentHeader;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private static readonly Dictionary<string, TomlDocumentState> Documents = new(StringComparer.Ordinal);
    private static bool shutdownRequested;

    static LspServer()
    {
        Input = Console.OpenStandardInput();
        Output = Console.OpenStandardOutput();
        WriteLock = new(1, 1);
        ContentHeader = Encoding.UTF8.GetBytes("content-length: ");
    }

    public static async Task Run(CancellationToken cancellationToken)
    {
        var header = new byte[32];
        while (!cancellationToken.IsCancellationRequested)
        {
            // Read 'Content-Length'
            var read = await Input.ReadAsync(header.AsMemory(0, ContentHeader.Length), cancellationToken);
            if (read < ContentHeader.Length)
            {
                break;
            }

            for (var i = 0; i < header.Length; i++)
            {// To lower
                if ((uint)(header[i] - 'A') <= 'Z' - 'A')
                {
                    header[i] += 0x20;
                }
            }

            if (!header.AsSpan(0, ContentHeader.Length).SequenceEqual(ContentHeader))
            {// Not 'Content-Length'
                break;
            }

            read = 0;
            var firstLine = true;
            int contentLength = 0;
            while (true)
            {
                var b = (byte)Input.ReadByte();
                if (b == Cr ||
                    b == Lf)
                {
                    if (b == Cr)
                    {
                        Input.ReadByte(); // Lf
                    }

                    if (firstLine)
                    {
                        firstLine = false;
                        Utf8Parser.TryParse(header.AsSpan(0, read), out contentLength, out _);
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                    if (firstLine && read < header.Length)
                    {
                        header[read++] = b;
                    }
                }
            }

            var buffer = ArrayPool<byte>.Shared.Rent(contentLength);
            try
            {
                await Input.ReadExactlyAsync(buffer.AsMemory(0, contentLength), cancellationToken).ConfigureAwait(false);

                {
                    if (contentLength + 1 < buffer.Length)
                    {
                        buffer[contentLength++] = Lf;
                    }

                    File.AppendAllBytes("C:\\App\\lsp.txt", buffer.AsSpan(0, contentLength));
                }

                var message = JsonSerializer.Deserialize<LspMessage>(buffer.AsSpan(0, contentLength), JsonOptions);
                if (message is null)
                {
                    break;
                }

                await HandleMessage(message).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            /*var read = await Input.ReadAsync(buffer, cancellationToken);
            var ros = new ReadOnlySequence<byte>();
            var st = Encoding.UTF8.GetString(buffer, 0, read);

            var message = await ReadMessage(Input).ConfigureAwait(false);
            if (message is null)
            {
                return;
            }*/
        }
    }

    private static async Task HandleMessage(LspMessage message)
    {
        switch (message.Method)
        {
            case "initialize":
                await HandleInitializeAsync(message.Id).ConfigureAwait(false);
                break;

            case "initialized":
                break;

            case "shutdown":
                shutdownRequested = true;
                await SendResponseAsync(message.Id, null).ConfigureAwait(false);
                break;

            case "exit":
                Environment.Exit(shutdownRequested ? 0 : 1);
                break;

            case "textDocument/didOpen":
                await HandleDidOpenAsync(message.Params).ConfigureAwait(false);
                break;

            case "textDocument/didChange":
                await HandleDidChangeAsync(message.Params).ConfigureAwait(false);
                break;

            case "textDocument/didClose":
                await HandleDidCloseAsync(message.Params).ConfigureAwait(false);
                break;

            default:
                if (message.Id is not null)
                {
                    await SendErrorAsync(
                        message.Id,
                        -32601,
                        $"Method not found: {message.Method}").ConfigureAwait(false);
                }

                break;
        }
    }

    private static async Task HandleInitializeAsync(JsonElement? id)
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
                Name = "Simple TOML Language Server",
                Version = "0.0.1",
            },
        };

        await SendResponseAsync(id, response).ConfigureAwait(false);
    }

    private static async Task HandleDidOpenAsync(JsonElement? parametersElement)
    {
        if (parametersElement is null)
        {
            return;
        }

        var parameters = parametersElement.Value.Deserialize<DidOpenTextDocumentParams>(JsonOptions);
        if (parameters?.TextDocument is null)
        {
            return;
        }

        var doc = parameters.TextDocument;

        var state = new TomlDocumentState(doc.Uri);
        state.Open(doc.Text ?? string.Empty, doc.Version);

        Documents[doc.Uri] = state;

        await PublishDiagnosticsAsync(state).ConfigureAwait(false);
    }

    private static async Task HandleDidChangeAsync(JsonElement? parametersElement)
    {
        if (parametersElement is null)
        {
            return;
        }

        var parameters = parametersElement.Value.Deserialize<DidChangeTextDocumentParams>(JsonOptions);
        if (parameters?.TextDocument is null)
        {
            return;
        }

        var uri = parameters.TextDocument.Uri;
        var version = parameters.TextDocument.Version;

        if (!Documents.TryGetValue(uri, out var state))
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

        await PublishDiagnosticsAsync(state).ConfigureAwait(false);
    }

    private static async Task HandleDidCloseAsync(JsonElement? parametersElement)
    {
        if (parametersElement is null)
        {
            return;
        }

        var parameters = parametersElement.Value.Deserialize<DidCloseTextDocumentParams>(JsonOptions);
        if (parameters?.TextDocument is null)
        {
            return;
        }

        var uri = parameters.TextDocument.Uri;

        Documents.Remove(uri);

        await PublishDiagnosticsAsync(uri, null, []).ConfigureAwait(false);
    }

    private static async Task PublishDiagnosticsAsync(TomlDocumentState state)
    {
        var diagnostics = SimpleTomlLinter.Lint(state.Lines);
        await PublishDiagnosticsAsync(state.Uri, state.Version, diagnostics).ConfigureAwait(false);
    }

    private static async Task PublishDiagnosticsAsync(
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
                Severity = ToLspSeverity(diagnostic.Severity),
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

        await SendNotificationAsync("textDocument/publishDiagnostics", parameters).ConfigureAwait(false);
    }

    private static int ToLspSeverity(string severity)
        => severity switch
        {
            "error" => 1,
            "warning" => 2,
            "info" => 3,
            "hint" => 4,
            _ => 1,
        };

    private static async Task SendResponseAsync(JsonElement? id, object? result)
    {
        var response = new JsonRpcResponse
        {
            Id = id,
            Result = result,
        };

        await SendJsonAsync(response).ConfigureAwait(false);
    }

    private static async Task SendNotificationAsync(string method, object? parameters)
    {
        var notification = new JsonRpcNotification
        {
            Method = method,
            Params = parameters,
        };

        await SendJsonAsync(notification).ConfigureAwait(false);
    }

    private static async Task SendErrorAsync(JsonElement? id, int code, string message)
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

        await SendJsonAsync(response).ConfigureAwait(false);
    }

    private static async Task SendJsonAsync<T>(T value)
    {
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        var headerBytes = Encoding.ASCII.GetBytes($"Content-Length: {jsonBytes.Length}\r\n\r\n");

        await WriteLock.WaitAsync().ConfigureAwait(false);

        try
        {
            await Output.WriteAsync(headerBytes).ConfigureAwait(false);
            await Output.WriteAsync(jsonBytes).ConfigureAwait(false);
            await Output.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            WriteLock.Release();
        }
    }

    private static async Task<string?> ReadMessageAsync(Stream input)
    {
        var contentLength = -1;

        while (true)
        {
            var line = await ReadAsciiLineAsync(input).ConfigureAwait(false);

            if (line is null)
            {
                return null;
            }

            if (line.Length == 0)
            {
                break;
            }

            const string contentLengthPrefix = "Content-Length:";

            if (line.StartsWith(contentLengthPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var value = line[contentLengthPrefix.Length..].Trim();
                contentLength = int.Parse(value);
            }
        }

        if (contentLength < 0)
        {
            return null;
        }

        var buffer = new byte[contentLength];
        var read = 0;

        while (read < contentLength)
        {
            var n = await input.ReadAsync(buffer.AsMemory(read, contentLength - read)).ConfigureAwait(false);

            if (n == 0)
            {
                return null;
            }

            read += n;
        }

        return Encoding.UTF8.GetString(buffer);
    }

    private static async Task<string?> ReadMessage(Stream input)
    {
        var contentLength = -1;

        while (true)
        {
            var line = await ReadAsciiLineAsync(input).ConfigureAwait(false);

            if (line is null)
            {
                return null;
            }

            if (line.Length == 0)
            {
                break;
            }

            const string contentLengthPrefix = "Content-Length:";
            if (line.StartsWith(contentLengthPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var value = line[contentLengthPrefix.Length..].Trim();
                contentLength = int.Parse(value);
            }
        }

        if (contentLength < 0)
        {
            return null;
        }

        var buffer = new byte[contentLength];
        var read = 0;

        while (read < contentLength)
        {
            var n = await input.ReadAsync(buffer.AsMemory(read, contentLength - read)).ConfigureAwait(false);
            if (n == 0)
            {
                return null;
            }

            read += n;
        }

        return Encoding.UTF8.GetString(buffer);
    }

    private static async Task<string?> ReadAsciiLineAsync(Stream input)
    {
        var bytes = new List<byte>(64);

        while (true)
        {
            var value = input.ReadByte();

            if (value < 0)
            {
                return bytes.Count == 0 ? null : Encoding.ASCII.GetString(bytes.ToArray());
            }

            if (value == '\r')
            {
                var next = input.ReadByte();
                if (next == '\n')
                {
                    break;
                }

                if (next >= 0)
                {
                    bytes.Add((byte)value);
                    bytes.Add((byte)next);
                }

                continue;
            }

            if (value == '\n')
            {
                break;
            }

            bytes.Add((byte)value);
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return Encoding.ASCII.GetString(bytes.ToArray());
    }
}

internal sealed class TomlDocumentState
{
    private readonly List<string> lines = new();

    public TomlDocumentState(string uri)
    {
        this.Uri = uri;
    }

    public string Uri { get; }

    public int Version { get; private set; }

    public IReadOnlyList<string> Lines => this.lines;

    public void Open(string text, int version)
    {
        this.lines.Clear();
        this.lines.AddRange(SplitLines(text));
        this.Version = version;
    }

    public void ApplyChange(TextChange change, int version)
    {
        if (this.lines.Count == 0)
        {
            this.lines.Add(string.Empty);
        }

        var startLine = Clamp(change.StartLine, 0, this.lines.Count - 1);
        var endLine = Clamp(change.EndLine, 0, this.lines.Count - 1);

        var startCharacter = Clamp(change.StartCharacter, 0, this.lines[startLine].Length);
        var endCharacter = Clamp(change.EndCharacter, 0, this.lines[endLine].Length);

        var prefix = this.lines[startLine][..startCharacter];
        var suffix = this.lines[endLine][endCharacter..];

        var replacementLines = SplitLines(change.Text);

        if (replacementLines.Length == 1)
        {
            this.lines[startLine] = prefix + replacementLines[0] + suffix;

            if (endLine > startLine)
            {
                this.lines.RemoveRange(startLine + 1, endLine - startLine);
            }
        }
        else
        {
            replacementLines[0] = prefix + replacementLines[0];
            replacementLines[^1] += suffix;

            this.lines[startLine] = replacementLines[0];

            if (endLine > startLine)
            {
                this.lines.RemoveRange(startLine + 1, endLine - startLine);
            }

            this.lines.InsertRange(startLine + 1, replacementLines.AsSpan(1).ToArray());
        }

        this.Version = version;
    }

    private static string[] SplitLines(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    private static int Clamp(int value, int min, int max)
        => Math.Min(Math.Max(value, min), max);
}

internal sealed record TextChange(int StartLine, int StartCharacter, int EndLine, int EndCharacter, string Text);

internal sealed record TomlDiagnostic(int Line, int Character, int Length, string Severity, string Message);

internal static class SimpleTomlLinter
{
    public static List<TomlDiagnostic> Lint(IReadOnlyList<string> lines)
    {
        var diagnostics = new List<TomlDiagnostic>();
        var keysBySection = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        var section = string.Empty;
        keysBySection[section] = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (trimmed.StartsWith('['))
            {
                if (!TryParseTableHeader(trimmed, out var tableName, out var isArrayTable))
                {
                    diagnostics.Add(new TomlDiagnostic(
                        i,
                        FirstNonWhiteSpace(line),
                        Math.Max(1, line.Length - FirstNonWhiteSpace(line)),
                        "error",
                        "Invalid table header syntax."));

                    continue;
                }

                section = tableName;

                if (isArrayTable)
                {
                    keysBySection[section] = new HashSet<string>(StringComparer.Ordinal);
                    continue;
                }

                if (!keysBySection.TryAdd(section, new HashSet<string>(StringComparer.Ordinal)))
                {
                    diagnostics.Add(new TomlDiagnostic(
                        i,
                        Math.Max(0, line.IndexOf('[', StringComparison.Ordinal)),
                        Math.Max(1, line.Length),
                        "warning",
                        $"Table '{section}' is already defined."));
                }

                continue;
            }

            var equalIndex = IndexOfUnquotedEqual(line);
            if (equalIndex < 0)
            {
                diagnostics.Add(new TomlDiagnostic(
                    i,
                    FirstNonWhiteSpace(line),
                    Math.Max(1, line.Length - FirstNonWhiteSpace(line)),
                    "error",
                    "Expected a key-value pair. TOML entries must use 'key = value'."));

                continue;
            }

            var keyPart = line[..equalIndex].Trim();

            if (keyPart.Length == 0)
            {
                diagnostics.Add(new TomlDiagnostic(
                    i,
                    equalIndex,
                    1,
                    "error",
                    "Missing key before '='."));

                continue;
            }

            if (!IsValidBareOrDottedKey(keyPart))
            {
                diagnostics.Add(new TomlDiagnostic(
                    i,
                    FirstNonWhiteSpace(line),
                    keyPart.Length,
                    "error",
                    $"Invalid key '{keyPart}'. This simple linter supports bare keys and dotted keys."));
            }

            var valuePart = RemoveComment(line[(equalIndex + 1)..]).Trim();

            if (valuePart.Length == 0)
            {
                diagnostics.Add(new TomlDiagnostic(
                    i,
                    equalIndex,
                    1,
                    "error",
                    "Missing value after '='."));

                continue;
            }

            if (!HasBalancedQuotes(valuePart))
            {
                diagnostics.Add(new TomlDiagnostic(
                    i,
                    equalIndex + 1,
                    Math.Max(1, line.Length - equalIndex - 1),
                    "error",
                    "String literal is not closed."));
            }

            if (!HasBalancedBrackets(valuePart))
            {
                diagnostics.Add(new TomlDiagnostic(
                    i,
                    equalIndex + 1,
                    Math.Max(1, line.Length - equalIndex - 1),
                    "error",
                    "Array or inline table brackets are not balanced."));
            }

            if (!keysBySection.TryGetValue(section, out var keys))
            {
                keys = new HashSet<string>(StringComparer.Ordinal);
                keysBySection[section] = keys;
            }

            if (!keys.Add(keyPart))
            {
                diagnostics.Add(new TomlDiagnostic(
                    i,
                    Math.Max(0, line.IndexOf(keyPart, StringComparison.Ordinal)),
                    Math.Max(1, keyPart.Length),
                    "error",
                    $"Duplicate key '{keyPart}' in the current table."));
            }
        }

        return diagnostics;
    }

    private static bool TryParseTableHeader(string trimmed, out string name, out bool isArrayTable)
    {
        name = string.Empty;
        isArrayTable = false;

        var body = trimmed;

        var commentIndex = IndexOfUnquotedHash(body);
        if (commentIndex >= 0)
        {
            body = body[..commentIndex].TrimEnd();
        }

        if (body.StartsWith("[[", StringComparison.Ordinal) &&
            body.EndsWith("]]", StringComparison.Ordinal))
        {
            isArrayTable = true;
            name = body[2..^2].Trim();
            return IsValidBareOrDottedKey(name);
        }

        if (body.StartsWith('[') && body.EndsWith(']'))
        {
            name = body[1..^1].Trim();
            return IsValidBareOrDottedKey(name);
        }

        return false;
    }

    private static bool IsValidBareOrDottedKey(string key)
    {
        var parts = key.Split('.', StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (!IsBareKey(part))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsBareKey(string key)
    {
        if (key.Length == 0)
        {
            return false;
        }

        foreach (var c in key)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static int IndexOfUnquotedEqual(string line)
    {
        var inString = false;
        var escaped = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (c == '#' && !inString)
            {
                return -1;
            }

            if (c == '=' && !inString)
            {
                return i;
            }
        }

        return -1;
    }

    private static int IndexOfUnquotedHash(string line)
    {
        var inString = false;
        var escaped = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (c == '#' && !inString)
            {
                return i;
            }
        }

        return -1;
    }

    private static string RemoveComment(string value)
    {
        var index = IndexOfUnquotedHash(value);
        return index < 0 ? value : value[..index];
    }

    private static bool HasBalancedQuotes(string value)
    {
        var inString = false;
        var escaped = false;

        foreach (var c in value)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
            }
        }

        return !inString;
    }

    private static bool HasBalancedBrackets(string value)
    {
        var square = 0;
        var curly = 0;
        var inString = false;
        var escaped = false;

        foreach (var c in value)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            switch (c)
            {
                case '[':
                    square++;
                    break;
                case ']':
                    square--;
                    break;
                case '{':
                    curly++;
                    break;
                case '}':
                    curly--;
                    break;
            }

            if (square < 0 || curly < 0)
            {
                return false;
            }
        }

        return square == 0 && curly == 0;
    }

    private static int FirstNonWhiteSpace(string line)
    {
        for (var i = 0; i < line.Length; i++)
        {
            if (!char.IsWhiteSpace(line[i]))
            {
                return i;
            }
        }

        return 0;
    }
}
