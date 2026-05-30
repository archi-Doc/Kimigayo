// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimigayo.Diagnostics;

namespace Kimigayo.Diagnostics;

public sealed class LspMessage
{
    public string Jsonrpc { get; set; } = string.Empty;

    public JsonElement? Id { get; set; }

    public string? Method { get; set; }

    public JsonElement? Params { get; set; }
}

public sealed class JsonRpcResponse
{
    public string Jsonrpc { get; set; } = "2.0";

    public JsonElement? Id { get; set; }

    public object? Result { get; set; }

    public JsonRpcError? Error { get; set; }
}

public sealed class JsonRpcNotification
{
    public string Jsonrpc { get; set; } = "2.0";

    public string Method { get; set; } = string.Empty;

    public object? Params { get; set; }
}

public sealed class JsonRpcError
{
    public int Code { get; set; }

    public string Message { get; set; } = string.Empty;
}

public sealed class InitializeResult
{
    public ServerCapabilities Capabilities { get; set; } = new();

    public ServerInfo ServerInfo { get; set; } = new();
}

public sealed class ServerCapabilities
{
    public TextDocumentSyncOptions TextDocumentSync { get; set; } = new();
}

public sealed class TextDocumentSyncOptions
{
    public bool OpenClose { get; set; }

    public int Change { get; set; }
}

public sealed class ServerInfo
{
    public string Name { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;
}

public sealed class DidOpenTextDocumentParams
{
    public TextDocumentItem TextDocument { get; set; } = new();
}

public sealed class DidChangeTextDocumentParams
{
    public VersionedTextDocumentIdentifier TextDocument { get; set; } = new();

    public List<TextDocumentContentChangeEvent> ContentChanges { get; set; } = new();
}

public sealed class DidCloseTextDocumentParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = new();
}

public sealed class TextDocumentItem
{
    public string Uri { get; set; } = string.Empty;

    public string LanguageId { get; set; } = string.Empty;

    public int Version { get; set; }

    public string? Text { get; set; }
}

public class TextDocumentIdentifier
{
    public string Uri { get; set; } = string.Empty;
}

public sealed class VersionedTextDocumentIdentifier : TextDocumentIdentifier
{
    public int Version { get; set; }
}

public sealed class TextDocumentContentChangeEvent
{
    public Range Range { get; set; }

    public int? RangeLength { get; set; }

    public string? Text { get; set; }
}

public sealed class PublishDiagnosticsParams
{
    public string Uri { get; set; } = string.Empty;

    public int? Version { get; set; }

    public Diagnostic[] Diagnostics { get; set; } = [];
}
